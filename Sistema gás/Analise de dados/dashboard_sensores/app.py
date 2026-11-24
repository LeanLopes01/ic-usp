"""
Dashboard de Monitoramento de Gases e Qualidade do Ar
======================================================
Lê os dados da tabela `leituras_sensores` (banco SQLite) e gera
gráficos interativos (Plotly) servidos via Flask.

Sensores monitorados:
  - MQ-3   (Álcool/Etanol)        -> coluna mq3_adc
  - MQ-9   (CO / Inflamáveis)     -> coluna mq9_adc
  - MQ-135 (CO2/NH3/VOC)          -> coluna mq135_adc
  - MQ-2   (GLP/Fumaça/Metano)    -> coluna mq2_adc
  - AHT10  (Temperatura/Umidade)  -> aht10_temp_c, aht10_hum_percent
  - SCD40  (CO2 - "ADC40")        -> scd40_co2_ppm
  - PMS5003 (Material Particulado)-> pms5003_pm1_0, pms5003_pm2_5, pms5003_pm10_0
"""

import os
import re
import sqlite3
import warnings

import numpy as np
import pandas as pd
from flask import Flask, render_template, jsonify, request
from plotly.io import to_html
from plotly.subplots import make_subplots
import plotly.graph_objs as go
from scipy.signal import savgol_filter
from sklearn.preprocessing import StandardScaler
from sklearn.ensemble import IsolationForest

warnings.filterwarnings('ignore')

app = Flask(__name__)

# ─────────────────────────────────────────────
# CONFIGURAÇÃO
# ─────────────────────────────────────────────
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
DB_PATH = os.path.join(BASE_DIR, 'banco_teste_gases.sqlite')
SQL_DUMP_PATH = os.path.join(BASE_DIR, 'banco_teste_gases.sql')
TABELA = 'leituras_sensores'

# O dump banco_teste_gases.sql é exportado no formato "longo" do ArgusDB
# (INSERT INTO ArgusDB.Historical_Data (Device_Id,Variable,Var_Avg,Var_Min,
# Var_Max,Var_Std,Data_Time_Stamp)), uma linha por variável/timestamp.
# Mapeamento da coluna `Variable` do dump para as colunas "largas" que o
# resto do app espera na tabela `leituras_sensores`.
VARIAVEL_DUMP_PARA_COLUNA = {
    'co2': 'scd40_co2_ppm',
    'mq135': 'mq135_adc',
    'mq2': 'mq2_adc',
    'mq3': 'mq3_adc',
    'mq9': 'mq9_adc',
    'pm1': 'pms5003_pm1_0',
    'pm10': 'pms5003_pm10_0',
    'temp': 'aht10_temp_c',
    'umidade': 'aht10_hum_percent',
}

# Sensores de gás (MOS) - valores brutos do ADC (0-4095 no ESP32)
SENSORES_GAS = {
    'mq3': {
        'coluna': 'mq3_adc', 'nome': 'MQ-3', 'gas': 'Álcool / Etanol',
        'cor': '#ff4757', 'unidade': 'ADC', 'alerta': 2000,
    },
    'mq9': {
        'coluna': 'mq9_adc', 'nome': 'MQ-9', 'gas': 'CO / Gás Combustível',
        'cor': '#2ed573', 'unidade': 'ADC', 'alerta': 2000,
    },
    'mq135': {
        'coluna': 'mq135_adc', 'nome': 'MQ-135', 'gas': 'CO₂ / NH₃ / VOC',
        'cor': '#1e90ff', 'unidade': 'ADC', 'alerta': 1500,
    },
    'mq2': {
        'coluna': 'mq2_adc', 'nome': 'MQ-2', 'gas': 'GLP / Fumaça / Metano',
        'cor': '#ffa502', 'unidade': 'ADC', 'alerta': 2000,
    },
}

# Sensor ambiental AHT10
SENSORES_AMBIENTE = {
    'aht10_temp': {
        'coluna': 'aht10_temp_c', 'nome': 'AHT10 - Temperatura',
        'cor': '#ff6348', 'unidade': '°C', 'alerta': None,
    },
    'aht10_hum': {
        'coluna': 'aht10_hum_percent', 'nome': 'AHT10 - Umidade',
        'cor': '#48dbfb', 'unidade': '%', 'alerta': None,
    },
}

# Sensor de CO2 (identificado pelo usuário como "ADC40")
SENSORES_CO2 = {
    'co2': {
        'coluna': 'scd40_co2_ppm', 'nome': 'SCD40 - CO₂ (ADC40)',
        'gas': 'CO₂', 'cor': '#a29bfe', 'unidade': 'ppm', 'alerta': 1000,
    },
}

# Sensor de material particulado PMS5003
SENSORES_PM = {
    'pm1_0': {
        'coluna': 'pms5003_pm1_0', 'nome': 'PMS5003 - PM1.0',
        'cor': '#55efc4', 'unidade': 'µg/m³', 'alerta': 50,
    },
    'pm2_5': {
        'coluna': 'pms5003_pm2_5', 'nome': 'PMS5003 - PM2.5',
        'cor': '#fdcb6e', 'unidade': 'µg/m³', 'alerta': 35,
    },
    'pm10_0': {
        'coluna': 'pms5003_pm10_0', 'nome': 'PMS5003 - PM10',
        'cor': '#e17055', 'unidade': 'µg/m³', 'alerta': 70,
    },
}

# Junta tudo num único dicionário para facilitar lookup por chave
TODOS_SENSORES = {}
TODOS_SENSORES.update(SENSORES_GAS)
TODOS_SENSORES.update(SENSORES_AMBIENTE)
TODOS_SENSORES.update(SENSORES_CO2)
TODOS_SENSORES.update(SENSORES_PM)

# ─────────────────────────────────────────────
# ÍNDICE DE QUALIDADE DO AR (IQAr) - RESOLUÇÃO CONAMA Nº 506/2024
# ─────────────────────────────────────────────
# Equação 1 da resolução (interpolação linear):
#   I = Iini + (Ifin - Iini) / (Cfin - Cini) * (C - Cini)
#
# Faixas do índice (Anexo II):
IQA_FAIXAS = [
    (0,   40,  'Boa',        '#2ed573'),
    (40,  80,  'Moderada',   '#fdcb6e'),
    (80,  120, 'Ruim',       '#ff8c42'),
    (120, 200, 'Muito Ruim', '#ff4757'),
    (200, 400, 'Péssima',    '#8e44ad'),
]

# Faixas de concentração (Cini-Cfin) por faixa do índice (Iini-Ifin), para
# os poluentes que os sensores atuais conseguem medir em µg/m³ (PMS5003).
#
# A faixa N1 (índice 0-40) foi informada pelo usuário a partir do Anexo II
# (PM10: 0-40 µg/m³, PM2.5: 0-15 µg/m³). As faixas N2-N5 foram derivadas
# alinhando cada categoria do índice (40-80-120-200-400) com os padrões
# PI-4, PI-3, PI-2 e PI-1 do Anexo I da mesma resolução.
# -> Caso o Anexo II oficial traga valores diferentes para N2-N5, ajuste
#    esta tabela (BREAKPOINTS_IQA) com os valores corretos.
BREAKPOINTS_IQA = {
    'pm10_0': [
        (0,   40,  0,   40),
        (40,  80,  40,  50),
        (80,  120, 50,  75),
        (120, 200, 75,  100),
        (200, 400, 100, 120),
    ],
    'pm2_5': [
        (0,   40,  0,   15),
        (40,  80,  15,  25),
        (80,  120, 25,  37),
        (120, 200, 37,  50),
        (200, 400, 50,  60),
    ],
}

POLUENTES_IQA = {
    'pm10_0': 'MP₁₀',
    'pm2_5':  'MP₂,₅',
}


def calcular_subindice(c: float, tabela: list[tuple[float, float, float, float]]) -> float:
    """Aplica a Equação 1 da Res. CONAMA 506/2024 para um valor de
    concentração `c`, retornando o sub-índice (0-400+)."""
    if pd.isna(c) or c < 0:
        return np.nan

    for i_ini, i_fin, c_ini, c_fin in tabela:
        if c_ini <= c <= c_fin:
            if c_fin == c_ini:
                return float(i_fin)
            return i_ini + (i_fin - i_ini) / (c_fin - c_ini) * (c - c_ini)

    # Acima da última faixa: extrapola usando a inclinação do último trecho
    i_ini, i_fin, c_ini, c_fin = tabela[-1]
    inclinacao = (i_fin - i_ini) / (c_fin - c_ini)
    return i_fin + inclinacao * (c - c_fin)


def calcular_iqa(df: pd.DataFrame) -> pd.DataFrame:
    """Calcula o sub-índice de cada poluente disponível e o IQAr final
    (o maior sub-índice = poluente dominante)."""
    resultado = pd.DataFrame(index=df.index)

    for chave, tabela in BREAKPOINTS_IQA.items():
        coluna = TODOS_SENSORES[chave]['coluna']
        if coluna not in df.columns:
            continue
        s = limpar_serie(pd.to_numeric(df[coluna], errors='coerce'))
        resultado[chave] = s.apply(lambda c: calcular_subindice(c, tabela))

    if resultado.empty:
        resultado['iqa'] = pd.Series(dtype=float)
        resultado['dominante'] = pd.Series(dtype=object)
        return resultado

    colunas_poluentes = list(resultado.columns)
    resultado['iqa'] = resultado.max(axis=1, skipna=True)
    resultado['dominante'] = resultado[colunas_poluentes].idxmax(axis=1, skipna=True)
    return resultado


def faixa_iqa(valor: float) -> tuple[str, str]:
    """Retorna (label, cor) da faixa de qualidade para um valor de IQAr."""
    if pd.isna(valor):
        return 'Sem dados', '#64748b'
    for i_ini, i_fin, nome, cor in IQA_FAIXAS:
        if i_ini <= valor <= i_fin:
            return nome, cor
    return IQA_FAIXAS[-1][2], IQA_FAIXAS[-1][3]


# Grupos exibidos na página inicial
GRUPOS = [
    {'titulo': 'Sensores de Gás (MOS)', 'chaves': list(SENSORES_GAS)},
    {'titulo': 'Clima (AHT10)', 'chaves': list(SENSORES_AMBIENTE)},
    {'titulo': 'CO₂ (SCD40 / ADC40)', 'chaves': list(SENSORES_CO2)},
    {'titulo': 'Material Particulado (PMS5003)', 'chaves': list(SENSORES_PM)},
]

LAYOUT_BASE = dict(
    template='plotly_dark',
    paper_bgcolor='rgba(0,0,0,0)',
    plot_bgcolor='rgba(0,0,0,0)',
    margin=dict(l=50, r=30, t=60, b=40),
    hovermode='x unified',
    hoverlabel=dict(
        bgcolor="rgba(15,23,42,0.95)",
        font_size=13,
        font_color="white",
        bordercolor="#38bdf8"
    ),
    legend=dict(
        bgcolor='rgba(30,41,59,0.8)',
        bordercolor='#334155',
        borderwidth=1
    ),
    font=dict(family='Segoe UI, sans-serif', color='#94a3b8'),
)


def aplicar_grid_escuro(fig, rows=1, cols=1, is_subplot=False):
    estilo = dict(
        gridcolor='rgba(255,255,255,0.06)',
        linecolor='rgba(255,255,255,0.1)',
        tickfont=dict(color='#64748b'),
    )
    if not is_subplot:
        fig.update_xaxes(**estilo)
        fig.update_yaxes(**estilo)
        return

    for r in range(1, rows + 1):
        for c in range(1, cols + 1):
            fig.update_xaxes(**estilo, row=r, col=c)
            fig.update_yaxes(**estilo, row=r, col=c)


# ─────────────────────────────────────────────
# ACESSO AO BANCO DE DADOS
# ─────────────────────────────────────────────

_REGISTRO_DUMP_RE = re.compile(
    r"\(\s*\d+\s*,\s*'(\w+)'\s*,\s*(-?[\d.]+)\s*,\s*-?[\d.]+\s*,\s*-?[\d.]+\s*,\s*-?[\d.]+\s*,\s*'([\d\- :]+)'\s*\)"
)

_ultimo_sync_mtime = None


def _sincronizar_sqlite_com_dump():
    """Reconstroi a tabela `leituras_sensores` do sqlite a partir do dump
    banco_teste_gases.sql sempre que esse arquivo for modificado, já que
    ele (e não o .sqlite) é a fonte de dados padrão."""
    global _ultimo_sync_mtime

    if not os.path.exists(SQL_DUMP_PATH):
        return

    mtime = os.path.getmtime(SQL_DUMP_PATH)
    if mtime == _ultimo_sync_mtime:
        return

    with open(SQL_DUMP_PATH, 'r', encoding='utf-8') as f:
        conteudo = f.read()

    registros = _REGISTRO_DUMP_RE.findall(conteudo)
    bruto = pd.DataFrame(registros, columns=['variavel', 'valor', 'timestamp'])
    bruto['valor'] = pd.to_numeric(bruto['valor'], errors='coerce')
    bruto['timestamp'] = pd.to_datetime(bruto['timestamp'])
    bruto['coluna'] = bruto['variavel'].map(VARIAVEL_DUMP_PARA_COLUNA)
    bruto = bruto.dropna(subset=['coluna'])

    largo = (bruto.pivot_table(index='timestamp', columns='coluna', values='valor', aggfunc='mean')
                  .sort_index().reset_index())

    with sqlite3.connect(DB_PATH) as db:
        largo.to_sql(TABELA, db, if_exists='replace', index=False)

    _ultimo_sync_mtime = mtime


def conectar_db():
    _sincronizar_sqlite_com_dump()
    return sqlite3.connect(DB_PATH)


def carregar_dados_com_fallback(horas: int | None) -> pd.DataFrame:
    """Carrega dados das últimas `horas` horas; se não houver leituras
    recentes (ex: banco de teste com dados antigos), usa o histórico completo."""
    df = carregar_dados(horas=horas)
    if df.empty and horas:
        df = carregar_dados(horas=None)
    return df


def carregar_dados(horas: int | None = None) -> pd.DataFrame:
    """Carrega todas as colunas da tabela, opcionalmente filtrando pelas
    últimas `horas` horas (com base na coluna timestamp)."""
    db = conectar_db()
    query = f"SELECT * FROM {TABELA}"
    if horas:
        query += f" WHERE timestamp >= datetime('now', '-{horas} hours')"
    query += " ORDER BY timestamp"
    df = pd.read_sql(query, db, parse_dates=['timestamp'])
    db.close()
    df.set_index('timestamp', inplace=True)
    return df


# ─────────────────────────────────────────────
# LIMPEZA E ESTATÍSTICAS
# ─────────────────────────────────────────────

def limpar_serie(s: pd.Series, iqr_fator: float = 3.0) -> pd.Series:
    """Remove outliers (IQR), interpola lacunas e suaviza com Savitzky-Golay."""
    s = s.copy().astype(float)

    q1, q3 = s.quantile(0.25), s.quantile(0.75)
    iqr = q3 - q1
    if iqr > 0:
        lower, upper = q1 - iqr_fator * iqr, q3 + iqr_fator * iqr
        s[(s < lower) | (s > upper)] = np.nan

    s = s.interpolate(method='linear', limit=10)

    vals = s.dropna().values
    if len(vals) >= 11:
        try:
            suave = savgol_filter(vals, window_length=11, polyorder=3)
            s_suave = pd.Series(suave, index=s.dropna().index)
            s = s_suave.reindex(s.index)
        except Exception:
            pass

    return s


def estatisticas(s: pd.Series) -> dict:
    clean = s.dropna()
    if clean.empty:
        return {}
    return {
        'min': round(float(clean.min()), 3),
        'max': round(float(clean.max()), 3),
        'mean': round(float(clean.mean()), 3),
        'median': round(float(clean.median()), 3),
        'std': round(float(clean.std()), 3),
        'p95': round(float(clean.quantile(0.95)), 3),
        'count': int(clean.count()),
    }


def detectar_anomalias(df: pd.DataFrame, contaminacao: float = 0.05) -> pd.Series:
    """Isolation Forest sobre todas as colunas numéricas disponíveis."""
    df_ml = df.select_dtypes(include=[np.number]).dropna(axis=1, how='all')
    df_ml = df_ml.fillna(df_ml.mean())

    if df_ml.shape[0] < 20 or df_ml.shape[1] < 1:
        return pd.Series(False, index=df.index)

    X = StandardScaler().fit_transform(df_ml.values)
    iso = IsolationForest(n_estimators=100, contamination=contaminacao,
                           random_state=42, n_jobs=-1)
    preds = iso.fit_predict(X)
    return pd.Series(preds == -1, index=df.index)


# ─────────────────────────────────────────────
# GRÁFICOS - SENSOR INDIVIDUAL
# ─────────────────────────────────────────────

def grafico_sensor(df: pd.DataFrame, chave: str, tipo: str) -> go.Figure:
    cfg = TODOS_SENSORES[chave]
    cor = cfg['cor']
    unidade = cfg['unidade']
    nome = cfg['nome']

    if cfg['coluna'] not in df.columns:
        fig = go.Figure()
        fig.update_layout(**LAYOUT_BASE, title='Sem dados para este sensor')
        return fig

    s_raw = pd.to_numeric(df[cfg['coluna']], errors='coerce')
    s = limpar_serie(s_raw)
    stats = estatisticas(s)

    if tipo == 'serie':
        fig = go.Figure()

        if not s.dropna().empty:
            roll_mean = s.rolling(6, center=True).mean()
            roll_std = s.rolling(6, center=True).std().fillna(0)

            fig.add_trace(go.Scatter(
                x=s.index, y=s,
                mode='lines',
                line=dict(color=cor, width=2, shape='spline'),
                name=nome,
                opacity=0.4
            ))

            fig.add_trace(go.Scatter(
                x=roll_mean.index, y=roll_mean,
                mode='lines',
                line=dict(color=cor, width=3, shape='spline'),
                name='Média Móvel',
            ))

            media = s.mean()
            fig.add_hline(
                y=media, line_dash='dot', line_color='#64748b',
                annotation_text=f'Média: {media:.2f} {unidade}',
                annotation_font_color='#94a3b8'
            )

            if cfg.get('alerta'):
                fig.add_hline(
                    y=cfg['alerta'], line_dash='dash', line_color='#ff4757',
                    annotation_text=f'Limite de alerta: {cfg["alerta"]} {unidade}',
                    annotation_font_color='#ff4757'
                )

        fig.update_layout(
            **LAYOUT_BASE,
            title=f'{nome} — Série Temporal ({unidade})',
            yaxis_title=unidade,
            height=480
        )
        aplicar_grid_escuro(fig)
        return fig

    elif tipo == 'distribuicao':
        fig = make_subplots(
            rows=1, cols=2, column_widths=[0.65, 0.35],
            subplot_titles=['Distribuição (Histograma)', 'Box Plot']
        )

        dados = s.dropna().values
        if len(dados) > 0:
            fig.add_trace(go.Histogram(
                x=dados, nbinsx=40, marker_color=cor, opacity=0.7, name='Frequência'
            ), row=1, col=1)

            try:
                from scipy.stats import gaussian_kde
                kde = gaussian_kde(dados)
                x_range = np.linspace(dados.min(), dados.max(), 200)
                densidade = kde(x_range) * len(dados) * (dados.max() - dados.min()) / 40
                fig.add_trace(go.Scatter(
                    x=x_range, y=densidade, mode='lines',
                    line=dict(color='white', width=2), name='KDE'
                ), row=1, col=1)
            except Exception:
                pass

            fig.add_trace(go.Box(
                y=dados, marker_color=cor, line_color=cor,
                name=nome, boxmean='sd'
            ), row=1, col=2)

        fig.update_layout(
            **LAYOUT_BASE,
            title=f'{nome} — Distribuição Estatística',
            height=440
        )
        aplicar_grid_escuro(fig, rows=1, cols=2, is_subplot=True)
        return fig

    elif tipo == 'estatisticas':
        fig = make_subplots(
            rows=1, cols=2,
            specs=[[{'type': 'indicator'}, {'type': 'table'}]],
            column_widths=[0.4, 0.6]
        )

        ultimo = float(s.dropna().iloc[-1]) if not s.dropna().empty else 0
        max_gauge = stats.get('p95', 1) * 1.5 if stats else 1
        if cfg.get('alerta'):
            max_gauge = max(max_gauge, cfg['alerta'] * 1.3)

        fig.add_trace(go.Indicator(
            mode='gauge+number+delta',
            value=ultimo,
            delta={'reference': stats.get('mean', 0), 'valueformat': '.2f'},
            title={'text': f'{nome}<br><span style="font-size:0.8em;color:#64748b">{cfg.get("gas", unidade)}</span>'},
            gauge={
                'axis': {'range': [0, max_gauge], 'tickcolor': '#64748b'},
                'bar': {'color': cor},
                'steps': [
                    {'range': [0, max_gauge * 0.4], 'color': 'rgba(46,213,115,0.15)'},
                    {'range': [max_gauge * 0.4, max_gauge * 0.7], 'color': 'rgba(255,165,2,0.15)'},
                    {'range': [max_gauge * 0.7, max_gauge], 'color': 'rgba(255,71,87,0.15)'},
                ],
                'threshold': {
                    'line': {'color': 'white', 'width': 3},
                    'thickness': 0.8,
                    'value': stats.get('mean', 0)
                }
            },
        ), row=1, col=1)

        if stats:
            fig.add_trace(go.Table(
                header=dict(
                    values=['<b>Estatística</b>', f'<b>{unidade}</b>'],
                    fill_color='#1e293b',
                    font=dict(color='#38bdf8', size=13),
                    line_color='#334155', align='left'
                ),
                cells=dict(
                    values=[
                        ['Mínimo', 'Máximo', 'Média', 'Mediana', 'Desvio Padrão', 'Percentil 95', 'Nº de Leituras'],
                        [stats['min'], stats['max'], stats['mean'],
                         stats['median'], stats['std'], stats['p95'], stats['count']]
                    ],
                    fill_color=['#0f172a', '#0f172a'],
                    font=dict(color=['#94a3b8', 'white'], size=12),
                    line_color='#334155', align='left'
                )
            ), row=1, col=2)

        fig.update_layout(
            **LAYOUT_BASE,
            title=f'{nome} — Painel Estatístico',
            height=420
        )
        return fig

    return go.Figure()


# ─────────────────────────────────────────────
# GRÁFICOS COMBINADOS
# ─────────────────────────────────────────────

def grafico_correlacao(df: pd.DataFrame) -> go.Figure:
    colunas = {chave: cfg['coluna'] for chave, cfg in TODOS_SENSORES.items()
               if cfg['coluna'] in df.columns}

    df_num = df[list(colunas.values())].apply(pd.to_numeric, errors='coerce').dropna()

    if df_num.empty or df_num.shape[1] < 2:
        fig = go.Figure()
        fig.update_layout(**LAYOUT_BASE, title='Dados insuficientes para correlação')
        return fig

    corr = df_num.corr()
    nomes = [TODOS_SENSORES[k]['nome'] for k in colunas]

    fig = go.Figure(data=go.Heatmap(
        z=corr.values, x=nomes, y=nomes,
        colorscale='RdBu', zmid=0,
        text=corr.round(2).values, texttemplate='%{text}',
        showscale=True
    ))
    fig.update_layout(
        **LAYOUT_BASE,
        title='Matriz de Correlação entre Sensores',
        height=600
    )
    return fig


def grafico_anomalias(df: pd.DataFrame) -> go.Figure:
    colunas = {chave: cfg['coluna'] for chave, cfg in TODOS_SENSORES.items()
               if cfg['coluna'] in df.columns}

    df_num = pd.DataFrame({chave: pd.to_numeric(df[col], errors='coerce')
                            for chave, col in colunas.items()})
    df_clean = pd.DataFrame({chave: limpar_serie(df_num[chave]) for chave in df_num.columns})

    anomalias = detectar_anomalias(df_clean)
    n_chaves = list(SENSORES_GAS) + list(SENSORES_CO2)

    fig = go.Figure()
    for chave in n_chaves:
        if chave not in df_clean.columns:
            continue
        cfg = TODOS_SENSORES[chave]
        s = df_clean[chave]
        s_normal = s.where(~anomalias)
        s_anomalo = s.where(anomalias)

        fig.add_trace(go.Scatter(
            x=s_normal.index, y=s_normal,
            mode='lines', line=dict(color=cfg['cor'], width=2, shape='spline'),
            name=cfg['nome']
        ))

        if s_anomalo.dropna().any():
            fig.add_trace(go.Scatter(
                x=s_anomalo.index, y=s_anomalo,
                mode='markers',
                marker=dict(size=10, color='white', symbol='x',
                             line=dict(color=cfg['cor'], width=2)),
                name=f'Anomalia {cfg["nome"]}',
                showlegend=False
            ))

    fig.update_layout(
        **LAYOUT_BASE,
        title=f'Detecção de Anomalias (Isolation Forest) — {int(anomalias.sum())} pontos detectados',
        height=520
    )
    aplicar_grid_escuro(fig)
    return fig


def grafico_visao_geral(df: pd.DataFrame) -> go.Figure:
    """Painel com 4 sub-gráficos: gases, clima, CO2, particulados."""
    fig = make_subplots(
        rows=4, cols=1,
        subplot_titles=[g['titulo'] for g in GRUPOS],
        vertical_spacing=0.06
    )

    for i, grupo in enumerate(GRUPOS, start=1):
        for chave in grupo['chaves']:
            cfg = TODOS_SENSORES[chave]
            if cfg['coluna'] not in df.columns:
                continue
            s = limpar_serie(pd.to_numeric(df[cfg['coluna']], errors='coerce'))
            fig.add_trace(go.Scatter(
                x=s.index, y=s, mode='lines',
                line=dict(color=cfg['cor'], width=2, shape='spline'),
                name=cfg['nome']
            ), row=i, col=1)

    fig.update_layout(
        **LAYOUT_BASE,
        title='Visão Geral — Todos os Sensores',
        height=1400,
        showlegend=True
    )
    aplicar_grid_escuro(fig, rows=4, cols=1, is_subplot=True)
    return fig


def grafico_iqa(df: pd.DataFrame) -> go.Figure:
    """Índice de Qualidade do Ar (IQAr) conforme Resolução CONAMA Nº 506/2024,
    calculado a partir de PM10 e PM2.5 (PMS5003). O sub-índice mais alto
    entre os dois poluentes define o IQAr e o poluente dominante."""
    iqa_df = calcular_iqa(df)

    fig = go.Figure()

    if iqa_df.empty or iqa_df['iqa'].dropna().empty:
        fig.update_layout(**LAYOUT_BASE, title='Dados insuficientes para o IQAr')
        return fig

    iqa = iqa_df['iqa']
    dominante = iqa_df['dominante']
    cores = [faixa_iqa(v)[1] for v in iqa]
    labels = [faixa_iqa(v)[0] for v in iqa]
    nomes_dom = [POLUENTES_IQA.get(d, '-') if pd.notna(d) else '-' for d in dominante]

    # Faixas de fundo (Boa / Moderada / Ruim / Muito Ruim / Péssima)
    for i_ini, i_fin, nome, cor in IQA_FAIXAS:
        fig.add_hrect(y0=i_ini, y1=i_fin, fillcolor=cor, opacity=0.08, line_width=0)

    fig.add_trace(go.Scatter(
        x=iqa.index, y=iqa,
        mode='lines',
        line=dict(color='rgba(255,255,255,0.25)', width=1),
        showlegend=False, hoverinfo='skip'
    ))

    fig.add_trace(go.Scatter(
        x=iqa.index, y=iqa,
        mode='markers+lines',
        marker=dict(color=cores, size=7, opacity=0.9),
        line=dict(color='rgba(255,255,255,0.2)', width=1),
        name='IQAr',
        customdata=np.stack([labels, nomes_dom], axis=-1),
        hovertemplate='<b>IQAr: %{y:.1f}</b> — %{customdata[0]}<br>Poluente dominante: %{customdata[1]}<extra></extra>'
    ))

    fig.update_yaxes(title_text='IQAr', range=[0, max(200, float(iqa.max()) * 1.1)])

    ultimo_iqa = float(iqa.dropna().iloc[-1]) if not iqa.dropna().empty else None
    nivel, _ = faixa_iqa(ultimo_iqa) if ultimo_iqa is not None else ('Sem dados', '#64748b')

    fig.update_layout(
        **LAYOUT_BASE,
        title=f'IQAr — Atual: {ultimo_iqa:.1f} ({nivel})' if ultimo_iqa is not None
              else 'IQAr — Índice de Qualidade do Ar',
        height=560
    )
    aplicar_grid_escuro(fig)
    return fig


def grafico_pm_concentracoes(df: pd.DataFrame) -> go.Figure:
    """Concentrações de PM10 e PM2.5 (PMS5003) que alimentam o cálculo do
    IQAr, exibidas em um gráfico próprio, separado do IQAr."""
    fig = go.Figure()

    tem_dados = False
    for chave, nome in POLUENTES_IQA.items():
        cfg = TODOS_SENSORES[chave]
        if cfg['coluna'] not in df.columns:
            continue
        s = limpar_serie(pd.to_numeric(df[cfg['coluna']], errors='coerce'))
        if s.dropna().empty:
            continue
        tem_dados = True
        fig.add_trace(go.Scatter(
            x=s.index, y=s, mode='lines',
            line=dict(color=cfg['cor'], width=2, shape='spline'),
            name=f'{nome} ({cfg["unidade"]})'
        ))

    if not tem_dados:
        fig.update_layout(**LAYOUT_BASE, title='Dados insuficientes de PM10/PM2.5')
        return fig

    fig.update_yaxes(title_text='µg/m³')
    fig.update_layout(
        **LAYOUT_BASE,
        title='Concentrações — PM10 e PM2.5',
        height=560
    )
    aplicar_grid_escuro(fig)
    return fig


# ─────────────────────────────────────────────
# ROTAS
# ─────────────────────────────────────────────

@app.route('/')
def index():
    return render_template('index.html', grupos=GRUPOS, sensores=TODOS_SENSORES)


@app.route('/grafico/<chave>')
def grafico_individual(chave):
    if chave not in TODOS_SENSORES:
        return f"<h2 style='color:#ff4757'>Sensor '{chave}' não encontrado.</h2>", 404

    tipo = request.args.get('tipo', 'serie')
    horas = request.args.get('horas', default=24, type=int)

    try:
        df = carregar_dados_com_fallback(horas)
        if df.empty:
            return _msg("⏳ Aguardando dados no banco...")

        fig = grafico_sensor(df, chave, tipo)
        return _render_fig(fig, f'/grafico/{chave}?tipo={tipo}&horas={horas}')
    except Exception:
        import traceback
        return f"<pre style='color:#ff4757;padding:20px'>{traceback.format_exc()}</pre>", 500


@app.route('/grafico/combinado/<tipo>')
def grafico_combinado_rota(tipo):
    horas = request.args.get('horas', default=24, type=int)

    try:
        df = carregar_dados_com_fallback(horas)
        if df.empty:
            return _msg("⏳ Aguardando dados no banco...")

        if tipo == 'correlacao':
            fig = grafico_correlacao(df)
        elif tipo == 'anomalias':
            fig = grafico_anomalias(df)
        elif tipo == 'visao-geral':
            fig = grafico_visao_geral(df)
        elif tipo == 'iqa':
            fig = grafico_iqa(df)
        elif tipo == 'pm-concentracoes':
            fig = grafico_pm_concentracoes(df)
        else:
            return _msg(f"Tipo de gráfico combinado '{tipo}' não reconhecido.")

        return _render_fig(fig, f'/grafico/combinado/{tipo}?horas={horas}')
    except Exception:
        import traceback
        return f"<pre style='color:#ff4757;padding:20px'>{traceback.format_exc()}</pre>", 500


@app.route('/api/status')
def api_status():
    try:
        df = carregar_dados_com_fallback(24)
        if df.empty:
            return jsonify({'erro': 'sem dados'}), 404

        resultado = {}
        for chave, cfg in TODOS_SENSORES.items():
            if cfg['coluna'] not in df.columns:
                continue
            s = limpar_serie(pd.to_numeric(df[cfg['coluna']], errors='coerce'))
            s_valid = s.dropna()
            resultado[chave] = {
                'nome': cfg['nome'],
                'unidade': cfg['unidade'],
                'ultimo': round(float(s_valid.iloc[-1]), 2) if not s_valid.empty else None,
                'stats': estatisticas(s),
                'alerta': cfg.get('alerta'),
                'em_alerta': bool(cfg.get('alerta') and not s_valid.empty
                                  and s_valid.iloc[-1] >= cfg['alerta']),
            }

        iqa_df = calcular_iqa(df)
        iqa_info = None
        if not iqa_df.empty and not iqa_df['iqa'].dropna().empty:
            ultimo_iqa = float(iqa_df['iqa'].dropna().iloc[-1])
            dominante = iqa_df['dominante'].dropna().iloc[-1] if not iqa_df['dominante'].dropna().empty else None
            nivel, cor = faixa_iqa(ultimo_iqa)
            iqa_info = {
                'valor': round(ultimo_iqa, 1),
                'nivel': nivel,
                'cor': cor,
                'poluente_dominante': POLUENTES_IQA.get(dominante, '-'),
            }

        return jsonify({
            'timestamp_ultimo': str(df.index[-1]),
            'sensores': resultado,
            'iqa': iqa_info,
        })
    except Exception as e:
        return jsonify({'erro': str(e)}), 500


# ─────────────────────────────────────────────
# HELPERS DE RESPOSTA
# ─────────────────────────────────────────────

def _msg(texto):
    return f"<h2 style='color:#94a3b8;font-family:sans-serif;padding:20px'>{texto}</h2>"


def _render_fig(fig, refresh_url):
    html_grafico = to_html(fig, full_html=False, include_plotlyjs='cdn')
    return f"""<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8">
  <meta http-equiv="refresh" content="60; url={refresh_url}">
  <style>
    html, body {{ margin:0; padding:0; background:transparent; color:white;
                  font-family:'Segoe UI',sans-serif; }}
  </style>
</head>
<body>{html_grafico}</body>
</html>"""


if __name__ == '__main__':
    app.run(host='0.0.0.0', port=5000, debug=True)
