"""
Camada de dados
===============
Lê o dump .sql (fonte da verdade), mantém o SQLite em dia, e entrega
séries já limpas para os gráficos. Nada aqui sabe o que é Plotly.
"""

import os
import re
import sqlite3
import warnings

import numpy as np
import pandas as pd
from scipy.signal import savgol_filter
from sklearn.ensemble import IsolationForest
from sklearn.preprocessing import StandardScaler

import config as cfg

warnings.filterwarnings('ignore')

_REGISTRO_DUMP_RE = re.compile(
    r"\(\s*\d+\s*,\s*'([\w.]+)'\s*,\s*(-?[\d.]+)\s*,\s*-?[\d.]+\s*,\s*-?[\d.]+\s*,"
    r"\s*-?[\d.]+\s*,\s*'([\d\- :]+)'\s*\)"
)

_ultimo_sync_mtime = None


# ─────────────────────────────────────────────
# BANCO
# ─────────────────────────────────────────────

def sincronizar_dump():
    """Reconstroi `leituras_sensores` a partir do dump sempre que o arquivo
    .sql muda. Se não houver dump, usa o SQLite como está."""
    global _ultimo_sync_mtime

    if not os.path.exists(cfg.SQL_DUMP_PATH):
        return

    mtime = os.path.getmtime(cfg.SQL_DUMP_PATH)
    if mtime == _ultimo_sync_mtime:
        return

    with open(cfg.SQL_DUMP_PATH, 'r', encoding='utf-8') as f:
        conteudo = f.read()

    registros = _REGISTRO_DUMP_RE.findall(conteudo)
    if not registros:
        _ultimo_sync_mtime = mtime
        return

    bruto = pd.DataFrame(registros, columns=['variavel', 'valor', 'timestamp'])
    bruto['valor'] = pd.to_numeric(bruto['valor'], errors='coerce')
    bruto['timestamp'] = pd.to_datetime(bruto['timestamp'])
    bruto['coluna'] = bruto['variavel'].map(cfg.VARIAVEL_DUMP_PARA_COLUNA)
    bruto = bruto.dropna(subset=['coluna'])

    largo = (bruto.pivot_table(index='timestamp', columns='coluna',
                               values='valor', aggfunc='mean')
                  .sort_index().reset_index())

    with sqlite3.connect(cfg.DB_PATH) as db:
        largo.to_sql(cfg.TABELA, db, if_exists='replace', index=False)

    _ultimo_sync_mtime = mtime


def conectar():
    sincronizar_dump()
    return sqlite3.connect(cfg.DB_PATH)


def _tabela_existe(db) -> bool:
    return db.execute(
        "SELECT 1 FROM sqlite_master WHERE type='table' AND name=?",
        (cfg.TABELA,)).fetchone() is not None


def carregar(horas: int | None = None) -> pd.DataFrame:
    """Carrega a tabela inteira, opcionalmente só as últimas `horas` horas."""
    db = conectar()
    try:
        if not _tabela_existe(db):
            # Sem dump e sem banco ainda: a interface mostra o aviso de
            # "nenhuma leitura" em vez de estourar um erro.
            return pd.DataFrame()

        query = f"SELECT * FROM {cfg.TABELA}"
        if horas:
            query += f" WHERE timestamp >= datetime('now', '-{horas} hours')"
        query += " ORDER BY timestamp"
        df = pd.read_sql(query, db, parse_dates=['timestamp'])
    finally:
        db.close()

    if df.empty:
        return df
    df = df.set_index('timestamp')
    return df


def carregar_intervalo(inicio: str, fim: str) -> pd.DataFrame:
    """Carrega só o intervalo [inicio, fim] (ambos inclusivos) — usado pela
    aba Box plots, que trabalha com datas explícitas em vez de 'últimas N
    horas'. A coluna `timestamp` é TEXT no banco, então as duas pontas
    precisam vir no mesmo formato salvo por `sincronizar_dump()`, senão a
    comparação lexicográfica do SQLite dá resultado errado."""
    inicio = pd.Timestamp(inicio).strftime('%Y-%m-%d %H:%M:%S')
    fim = pd.Timestamp(fim).strftime('%Y-%m-%d %H:%M:%S')

    db = conectar()
    try:
        if not _tabela_existe(db):
            return pd.DataFrame()
        query = (f"SELECT * FROM {cfg.TABELA} "
                 "WHERE timestamp >= ? AND timestamp <= ? ORDER BY timestamp")
        df = pd.read_sql(query, db, params=(inicio, fim), parse_dates=['timestamp'])
    finally:
        db.close()

    if df.empty:
        return df
    return df.set_index('timestamp')


def carregar_com_fallback(horas: int | None) -> pd.DataFrame:
    """Se o recorte recente vier vazio (banco de teste com dados antigos),
    devolve o histórico completo em vez de uma tela vazia."""
    df = carregar(horas=horas)
    if df.empty and horas:
        df = carregar(horas=None)
    return df


def periodo_dos_dados(df: pd.DataFrame) -> tuple[str | None, str | None]:
    if df.empty:
        return None, None
    return str(df.index[0]), str(df.index[-1])


# ─────────────────────────────────────────────
# LIMPEZA
# ─────────────────────────────────────────────

def limpar_serie(s: pd.Series, iqr_fator: float = 3.0, suavizar: bool = True,
                 sem_negativos: bool = False) -> pd.Series:
    """Descarta outliers (IQR), interpola lacunas curtas e suaviza
    (Savitzky-Golay). Com `sem_negativos`, leituras negativas viram lacuna
    antes de tudo — elas são impossíveis nesses sensores."""
    s = s.copy().astype(float)

    if sem_negativos:
        s[s < 0] = np.nan

    q1, q3 = s.quantile(0.25), s.quantile(0.75)
    iqr = q3 - q1
    if iqr > 0:
        limite_inf, limite_sup = q1 - iqr_fator * iqr, q3 + iqr_fator * iqr
        s[(s < limite_inf) | (s > limite_sup)] = np.nan

    s = s.interpolate(method='linear', limit=10)

    if suavizar:
        vals = s.dropna()
        if len(vals) >= 11:
            try:
                suave = savgol_filter(vals.values, window_length=11, polyorder=3)
                s = pd.Series(suave, index=vals.index).reindex(s.index)
            except Exception:
                pass

    return s


def serie(df: pd.DataFrame, chave: str, limpar: bool = True) -> pd.Series:
    """Série pronta de um sensor (ou do IQAr, que é calculado)."""
    if chave == 'iqa':
        tabela = calcular_iqa(df)
        return tabela['iqa'] if 'iqa' in tabela else pd.Series(dtype=float)

    conf = cfg.TODOS_SENSORES.get(chave)
    if not conf or conf['coluna'] not in df.columns:
        return pd.Series(dtype=float)

    bruta = pd.to_numeric(df[conf['coluna']], errors='coerce')
    if not limpar:
        return bruta
    return limpar_serie(bruta, sem_negativos=not conf.get('permite_negativo', True))


_UNIDADE_TIMEDELTA = {'minutos': 'minutes', 'horas': 'hours',
                      'dias': 'days', 'semanas': 'weeks'}


def bin_intervalos(indice: pd.DatetimeIndex, valor: int, unidade: str,
                   ancora_hora: int = 0, ancora_minuto: int = 0,
                   ancora_dia_semana: int = 0) -> pd.Series:
    """Início do bloco de cada instante de `indice`, por divisão inteira do
    tempo decorrido desde uma época fixa — não usa regras de calendário do
    pandas.resample, que ficam estranhas com múltiplos dias/semanas e
    âncoras arbitrárias. `ancora_hora`/`ancora_minuto` deslocam a borda dos
    blocos (ex.: blocos de 1h começando às 6:30 em vez de em ponto);
    `ancora_dia_semana` (0=segunda) só importa quando `unidade='semanas'`."""
    epoca = pd.Timestamp('1970-01-05')  # uma segunda-feira
    deslocamento = pd.Timedelta(hours=ancora_hora, minutes=ancora_minuto)
    if unidade == 'semanas':
        deslocamento += pd.Timedelta(days=ancora_dia_semana)

    tamanho = pd.Timedelta(**{_UNIDADE_TIMEDELTA[unidade]: valor})
    decorrido = pd.Series(pd.DatetimeIndex(indice)) - epoca - deslocamento
    n = decorrido // tamanho
    inicio_bloco = epoca + deslocamento + n * tamanho
    return pd.Series(inicio_bloco.values, index=indice)


def filtrar_dias_semana(s: pd.Series, filtro: str) -> pd.Series:
    """Filtra por dia da semana ANTES do binning, para que os dias
    excluídos virem lacuna visível no eixo de datas em vez de serem
    absorvidos por blocos vizinhos. Chamar depois de `serie()` — filtrar
    antes da limpeza colaria posições de dias não consecutivos (ex.: sexta
    23h com segunda 0h) na interpolação/suavização."""
    if filtro == 'uteis':
        return s[s.index.dayofweek < 5]
    if filtro == 'fds':
        return s[s.index.dayofweek >= 5]
    return s


def estatisticas(s: pd.Series) -> dict:
    limpa = s.dropna()
    if limpa.empty:
        return {}
    return {
        'min': round(float(limpa.min()), 3),
        'max': round(float(limpa.max()), 3),
        'media': round(float(limpa.mean()), 3),
        'mediana': round(float(limpa.median()), 3),
        'desvio': round(float(limpa.std()), 3),
        'p95': round(float(limpa.quantile(0.95)), 3),
        'n': int(limpa.count()),
    }


def detectar_anomalias(df: pd.DataFrame, contaminacao: float = 0.05) -> pd.Series:
    numerico = df.select_dtypes(include=[np.number]).dropna(axis=1, how='all')
    numerico = numerico.fillna(numerico.mean())

    if numerico.shape[0] < 20 or numerico.shape[1] < 1:
        return pd.Series(False, index=df.index)

    X = StandardScaler().fit_transform(numerico.values)
    modelo = IsolationForest(n_estimators=100, contamination=contaminacao,
                             random_state=42, n_jobs=-1)
    return pd.Series(modelo.fit_predict(X) == -1, index=df.index)


# ─────────────────────────────────────────────
# IQAr — RES. CONAMA Nº 506/2024
# ─────────────────────────────────────────────

def subindice(c: float, tabela: list) -> float:
    """Equação 1 da resolução: I = Iini + (Ifin-Iini)/(Cfin-Cini) · (C-Cini)."""
    if pd.isna(c) or c < 0:
        return np.nan

    for i_ini, i_fin, c_ini, c_fin in tabela:
        if c_ini <= c <= c_fin:
            if c_fin == c_ini:
                return float(i_fin)
            return i_ini + (i_fin - i_ini) / (c_fin - c_ini) * (c - c_ini)

    i_ini, i_fin, c_ini, c_fin = tabela[-1]
    inclinacao = (i_fin - i_ini) / (c_fin - c_ini)
    return i_fin + inclinacao * (c - c_fin)


def calcular_iqa(df: pd.DataFrame) -> pd.DataFrame:
    """Sub-índice de cada poluente + IQAr final (o maior sub-índice) e o
    poluente dominante."""
    resultado = pd.DataFrame(index=df.index)

    for chave, tabela in cfg.BREAKPOINTS_IQA.items():
        coluna = cfg.TODOS_SENSORES[chave]['coluna']
        if coluna not in df.columns:
            continue
        s = limpar_serie(pd.to_numeric(df[coluna], errors='coerce'), sem_negativos=True)
        resultado[chave] = s.apply(lambda c: subindice(c, tabela))

    if resultado.empty:
        resultado['iqa'] = pd.Series(dtype=float)
        resultado['dominante'] = pd.Series(dtype=object)
        return resultado

    poluentes = list(resultado.columns)
    resultado['iqa'] = resultado[poluentes].max(axis=1, skipna=True)

    resultado['dominante'] = pd.Series(np.nan, index=resultado.index, dtype=object)
    com_dado = resultado[poluentes].notna().any(axis=1)
    if com_dado.any():
        resultado.loc[com_dado, 'dominante'] = (
            resultado.loc[com_dado, poluentes].idxmax(axis=1, skipna=True)
        )
    return resultado


def faixa_iqa(valor) -> tuple[str, str]:
    if valor is None or pd.isna(valor):
        return 'Sem dados', '#64748b'
    for i_ini, i_fin, nome, cor in cfg.IQA_FAIXAS:
        if i_ini <= valor <= i_fin:
            return nome, cor
    return cfg.IQA_FAIXAS[-1][2], cfg.IQA_FAIXAS[-1][3]
