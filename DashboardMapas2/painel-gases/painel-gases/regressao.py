"""
Sistema de regressões
=====================
Funciona para qualquer série do painel (sensor de gás, clima, CO₂, PM ou o
próprio IQAr) — a regressão não sabe de que sensor veio o dado, só recebe
uma pandas.Series indexada por tempo.

Cada regressão é um dicionário salvo em regressoes.json e editável pela
interface. Campos:

  modelo      linear | polinomial | exponencial | logaritmica | potencia |
              media_movel | savgol
  metodo      ols | huber | ransac | theilsen   (só para `linear`)
  grau        grau do polinômio (modelo polinomial)
  janela      nº de pontos (media_movel / savgol)
  inicio/fim  recorte temporal do ajuste (ISO; vazio = ponta dos dados)
  extrapolar_horas / extrapolar_antes_horas
              quantas horas projetar depois/antes da janela ajustada
  banda       nenhuma | confianca | predicao   (linear OLS)
  nivel       nível da banda, ex. 0.95
  cor, espessura, tracejado, opacidade, legenda
  mostrar_equacao / mostrar_r2
              acrescentam a equação e o R² ao nome da linha na legenda
  ativo       desliga sem apagar
"""

from __future__ import annotations

import json
import os
import uuid

import numpy as np
import pandas as pd
import plotly.graph_objs as go
from scipy import stats as sps
from scipy.signal import savgol_filter
from sklearn.linear_model import (HuberRegressor, RANSACRegressor,
                                  TheilSenRegressor)

import config as cfg

MODELOS = {
    'linear': 'Linear — y = a·x + b',
    'polinomial': 'Polinomial — grau N',
    'exponencial': 'Exponencial — y = a·e^(b·x)',
    'logaritmica': 'Logarítmica — y = a + b·ln(x)',
    'potencia': 'Potência — y = a·x^b',
    'media_movel': 'Média móvel',
    'savgol': 'Savitzky-Golay',
}

METODOS = {
    'ols': 'Mínimos quadrados',
    'huber': 'Huber (robusto a outliers)',
    'ransac': 'RANSAC (robusto a outliers)',
    'theilsen': 'Theil-Sen (robusto a outliers)',
}

TRACEJADOS = {
    'dash': 'Tracejada',
    'solid': 'Contínua',
    'dot': 'Pontilhada',
    'dashdot': 'Traço-ponto',
    'longdash': 'Traço longo',
}

BANDAS = {
    'nenhuma': 'Sem banda',
    'confianca': 'Intervalo de confiança da média',
    'predicao': 'Intervalo de predição',
}

# Modelos que projetam para fora da janela ajustada.
MODELOS_PARAMETRICOS = {'linear', 'polinomial', 'exponencial', 'logaritmica', 'potencia'}

PADRAO = {
    'ativo': True,
    'modelo': 'linear',
    'metodo': 'ols',
    'grau': 2,
    'janela': 12,
    'inicio': None,
    'fim': None,
    'nome': None,
    'cor': '#7c8cff',
    'espessura': 2.5,
    'tracejado': 'dash',
    'opacidade': 1.0,
    'legenda': True,
    'mostrar_equacao': True,
    'mostrar_r2': True,
    'extrapolar_horas': 0.0,
    'extrapolar_antes_horas': 0.0,
    'banda': 'nenhuma',
    'nivel': 0.95,
}


# ─────────────────────────────────────────────
# PERSISTÊNCIA
# ─────────────────────────────────────────────

def carregar_todas() -> dict:
    if not os.path.exists(cfg.REGRESSOES_PATH):
        return {}
    try:
        with open(cfg.REGRESSOES_PATH, 'r', encoding='utf-8') as f:
            return json.load(f)
    except (json.JSONDecodeError, OSError):
        return {}


def salvar_todas(dados: dict):
    with open(cfg.REGRESSOES_PATH, 'w', encoding='utf-8') as f:
        json.dump(dados, f, ensure_ascii=False, indent=2)


def listar(chave: str) -> list[dict]:
    return carregar_todas().get(chave, [])


def normalizar(corpo: dict, base: dict | None = None) -> dict:
    """Mistura o que veio da interface com os padrões (ou com a regressão
    existente, num PATCH) e valida os campos de escolha."""
    reg = dict(PADRAO)
    if base:
        reg.update(base)

    for campo, valor in (corpo or {}).items():
        if campo in ('id',):
            continue
        reg[campo] = valor

    if reg['modelo'] not in MODELOS:
        reg['modelo'] = 'linear'
    if reg['metodo'] not in METODOS:
        reg['metodo'] = 'ols'
    if reg['tracejado'] not in TRACEJADOS:
        reg['tracejado'] = 'dash'
    if reg['banda'] not in BANDAS:
        reg['banda'] = 'nenhuma'

    reg['grau'] = int(max(1, min(6, _num(reg['grau'], 2))))
    reg['janela'] = int(max(3, min(500, _num(reg['janela'], 12))))
    reg['espessura'] = float(max(0.5, min(10, _num(reg['espessura'], 2.5))))
    reg['opacidade'] = float(max(0.1, min(1.0, _num(reg['opacidade'], 1.0))))
    reg['nivel'] = float(max(0.5, min(0.999, _num(reg['nivel'], 0.95))))
    reg['extrapolar_horas'] = float(max(0, _num(reg['extrapolar_horas'], 0)))
    reg['extrapolar_antes_horas'] = float(max(0, _num(reg['extrapolar_antes_horas'], 0)))
    reg['ativo'] = bool(reg['ativo'])
    reg['legenda'] = bool(reg['legenda'])
    reg['mostrar_equacao'] = bool(reg['mostrar_equacao'])
    reg['mostrar_r2'] = bool(reg['mostrar_r2'])
    reg['inicio'] = reg.get('inicio') or None
    reg['fim'] = reg.get('fim') or None
    nome = (reg.get('nome') or '').strip()
    reg['nome'] = nome or None
    reg.setdefault('id', uuid.uuid4().hex[:8])
    return reg


def criar(chave: str, corpo: dict) -> dict:
    nova = normalizar(corpo)
    nova['id'] = uuid.uuid4().hex[:8]
    dados = carregar_todas()
    dados.setdefault(chave, []).append(nova)
    salvar_todas(dados)
    return nova


def atualizar(chave: str, id_reg: str, corpo: dict) -> dict | None:
    dados = carregar_todas()
    for i, reg in enumerate(dados.get(chave, [])):
        if reg.get('id') == id_reg:
            atualizada = normalizar(corpo, base=reg)
            atualizada['id'] = id_reg
            dados[chave][i] = atualizada
            salvar_todas(dados)
            return atualizada
    return None


def remover(chave: str, id_reg: str) -> bool:
    dados = carregar_todas()
    linhas = dados.get(chave, [])
    restantes = [r for r in linhas if r.get('id') != id_reg]
    if len(restantes) == len(linhas):
        return False
    dados[chave] = restantes
    salvar_todas(dados)
    return True


def _num(valor, padrao):
    try:
        return float(valor)
    except (TypeError, ValueError):
        return padrao


# ─────────────────────────────────────────────
# AJUSTE
# ─────────────────────────────────────────────

def _janela(s: pd.Series, reg: dict) -> pd.Series:
    sub = s.dropna()
    if reg.get('inicio'):
        sub = sub[sub.index >= pd.Timestamp(reg['inicio'])]
    if reg.get('fim'):
        sub = sub[sub.index <= pd.Timestamp(reg['fim'])]
    return sub


def _coef_linear(x: np.ndarray, y: np.ndarray, metodo: str) -> tuple[float, float]:
    """Inclinação e intercepto por OLS ou por um estimador robusto."""
    if metodo == 'ols' or len(x) < 6:
        a, b = np.polyfit(x, y, 1)
        return float(a), float(b)

    X = x.reshape(-1, 1)
    try:
        if metodo == 'huber':
            m = HuberRegressor().fit(X, y)
            return float(m.coef_[0]), float(m.intercept_)
        if metodo == 'ransac':
            m = RANSACRegressor(random_state=42).fit(X, y)
            est = m.estimator_
            return float(est.coef_[0]), float(est.intercept_)
        m = TheilSenRegressor(random_state=42).fit(X, y)
        return float(m.coef_[0]), float(m.intercept_)
    except Exception:
        a, b = np.polyfit(x, y, 1)
        return float(a), float(b)


def _formatar(v: float, casas: int = 4) -> str:
    if v == 0:
        return '0'
    if abs(v) >= 1e4 or abs(v) < 1e-3:
        return f'{v:.2e}'
    return f'{v:.{casas}g}'


def ajustar(s: pd.Series, reg: dict, unidade: str = '') -> dict | None:
    """Ajusta o modelo pedido e devolve a curva + métricas.

    Retorna None quando não há pontos suficientes na janela. O eixo x é
    sempre "horas desde o primeiro ponto da janela", então a inclinação sai
    em unidade/hora — que é o que faz sentido ler num painel de sensores.
    """
    sub = _janela(s, reg)
    modelo = reg.get('modelo', 'linear')
    minimo = reg.get('grau', 2) + 1 if modelo == 'polinomial' else 2
    if len(sub) < max(2, minimo):
        return None

    t0 = sub.index[0]
    x = (sub.index - t0).total_seconds().values / 3600.0
    y = sub.values.astype(float)

    # ── modelos não paramétricos: a "curva" é a própria série suavizada
    if modelo in ('media_movel', 'savgol'):
        janela = int(reg.get('janela', 12))
        if modelo == 'media_movel':
            ajuste = sub.rolling(janela, center=True, min_periods=1).mean()
            y_curva = ajuste.values
            eq = f'Média móvel de {janela} pontos'
        else:
            janela = min(janela if janela % 2 else janela + 1, len(sub) - (1 - len(sub) % 2))
            if janela < 5:
                return None
            ordem = min(3, janela - 1)
            y_curva = savgol_filter(y, window_length=janela, polyorder=ordem)
            eq = f'Savitzky-Golay (janela {janela}, ordem {ordem})'

        return {
            'x': sub.index,
            'y': y_curva,
            'equacao': eq,
            'metricas': _metricas(y, y_curva, n_par=1),
            'banda': None,
            'parametros': {'janela': janela},
        }

    # ── grade de saída (permite projetar antes/depois da janela)
    x_ini = x.min() - float(reg.get('extrapolar_antes_horas', 0) or 0)
    x_fim = x.max() + float(reg.get('extrapolar_horas', 0) or 0)
    x_grade = np.linspace(x_ini, x_fim, max(200, len(sub)))
    idx_grade = t0 + pd.to_timedelta(x_grade, unit='h')

    banda = None
    parametros = {}

    if modelo == 'linear':
        a, b = _coef_linear(x, y, reg.get('metodo', 'ols'))
        y_curva = a * x_grade + b
        y_ajuste = a * x + b
        eq = f'y = {_formatar(a)}·x {"+" if b >= 0 else "−"} {_formatar(abs(b))}'
        parametros = {'inclinacao_hora': a, 'intercepto': b,
                      'inclinacao_dia': a * 24}
        if reg.get('banda', 'nenhuma') != 'nenhuma':
            banda = _banda_linear(x, y, x_grade, y_curva,
                                  reg['banda'], float(reg.get('nivel', 0.95)))

    elif modelo == 'polinomial':
        grau = int(reg.get('grau', 2))
        coefs = np.polyfit(x, y, grau)
        y_curva = np.polyval(coefs, x_grade)
        y_ajuste = np.polyval(coefs, x)
        termos = []
        for i, c in enumerate(coefs):
            p = grau - i
            termo = _formatar(c) + ('' if p == 0 else f'·x{_sup(p)}' if p > 1 else '·x')
            termos.append(termo)
        eq = 'y = ' + ' + '.join(termos)
        parametros = {'coeficientes': [float(c) for c in coefs], 'grau': grau}

    elif modelo == 'exponencial':
        mascara = y > 0
        if mascara.sum() < 2:
            return None
        a_log, b_log = np.polyfit(x[mascara], np.log(y[mascara]), 1)
        a, b = float(np.exp(b_log)), float(a_log)
        y_curva = a * np.exp(b * x_grade)
        y_ajuste = a * np.exp(b * x)
        eq = f'y = {_formatar(a)}·e^({_formatar(b)}·x)'
        parametros = {'a': a, 'b': b,
                      'meia_vida_h': float(np.log(2) / abs(b)) if b else None}

    elif modelo == 'logaritmica':
        desloc = 1e-6 - min(0.0, float(x.min())) + 1e-6
        b, a = np.polyfit(np.log(x + desloc), y, 1)
        y_curva = a + b * np.log(np.clip(x_grade + desloc, 1e-9, None))
        y_ajuste = a + b * np.log(x + desloc)
        eq = f'y = {_formatar(a)} {"+" if b >= 0 else "−"} {_formatar(abs(b))}·ln(x)'
        parametros = {'a': float(a), 'b': float(b)}

    else:  # potencia
        desloc = 1e-6 - min(0.0, float(x.min())) + 1e-6
        mascara = y > 0
        if mascara.sum() < 2:
            return None
        b_log, a_log = np.polyfit(np.log(x[mascara] + desloc), np.log(y[mascara]), 1)
        a, b = float(np.exp(a_log)), float(b_log)
        y_curva = a * np.power(np.clip(x_grade + desloc, 1e-9, None), b)
        y_ajuste = a * np.power(x + desloc, b)
        eq = f'y = {_formatar(a)}·x^{_formatar(b)}'
        parametros = {'a': a, 'b': b}

    n_par = {'linear': 2, 'polinomial': int(reg.get('grau', 2)) + 1}.get(modelo, 2)
    metricas = _metricas(y, y_ajuste, n_par=n_par)
    metricas['inicio'] = str(sub.index[0])
    metricas['fim'] = str(sub.index[-1])
    metricas['duracao_h'] = round(float(x.max() - x.min()), 3)
    if modelo == 'linear':
        metricas['inclinacao'] = f'{_formatar(parametros["inclinacao_hora"])} {unidade}/h'
        metricas['variacao_dia'] = f'{_formatar(parametros["inclinacao_dia"])} {unidade}/dia'

    return {'x': idx_grade, 'y': y_curva, 'equacao': eq,
            'metricas': metricas, 'banda': banda, 'parametros': parametros}


def _sup(p: int) -> str:
    mapa = str.maketrans('0123456789', '⁰¹²³⁴⁵⁶⁷⁸⁹')
    return str(p).translate(mapa)


def _metricas(y: np.ndarray, y_ajuste: np.ndarray, n_par: int = 2) -> dict:
    residuo = y - y_ajuste
    ss_res = float(np.sum(residuo ** 2))
    ss_tot = float(np.sum((y - y.mean()) ** 2))
    r2 = 1 - ss_res / ss_tot if ss_tot > 0 else float('nan')
    n = len(y)
    r2_aj = (1 - (1 - r2) * (n - 1) / (n - n_par)) if n > n_par and np.isfinite(r2) else float('nan')
    return {
        'r2': round(r2, 4) if np.isfinite(r2) else None,
        'r2_ajustado': round(r2_aj, 4) if np.isfinite(r2_aj) else None,
        'rmse': round(float(np.sqrt(ss_res / n)), 4),
        'mae': round(float(np.mean(np.abs(residuo))), 4),
        'n': int(n),
    }


def _banda_linear(x, y, x_grade, y_grade, tipo: str, nivel: float):
    """Intervalo de confiança da média ou de predição para o ajuste linear."""
    n = len(x)
    if n < 3:
        return None
    gl = n - 2
    y_ajuste = np.polyval(np.polyfit(x, y, 1), x)
    s_err = np.sqrt(np.sum((y - y_ajuste) ** 2) / gl)
    sxx = np.sum((x - x.mean()) ** 2)
    if sxx == 0:
        return None

    base = 1.0 / n + (x_grade - x.mean()) ** 2 / sxx
    if tipo == 'predicao':
        base = base + 1.0
    t = sps.t.ppf(0.5 + nivel / 2, gl)
    margem = t * s_err * np.sqrt(base)
    return y_grade - margem, y_grade + margem


# ─────────────────────────────────────────────
# DESENHO
# ─────────────────────────────────────────────

def _rotulo(reg: dict, resultado: dict, i: int) -> str:
    partes = [reg.get('nome') or f'Tendência {i}']
    extras = []
    if reg.get('mostrar_equacao') and resultado['equacao']:
        extras.append(resultado['equacao'])
    if reg.get('mostrar_r2') and resultado['metricas'].get('r2') is not None:
        extras.append(f"R²={resultado['metricas']['r2']:.3f}")
    if extras:
        partes.append(' · '.join(extras))
    return '<br>'.join(partes) if len(partes) > 1 else partes[0]


def _rgba(cor_hex: str, alfa: float) -> str:
    cor = (cor_hex or '#7c8cff').lstrip('#')
    if len(cor) == 3:
        cor = ''.join(c * 2 for c in cor)
    try:
        r, g, b = (int(cor[i:i + 2], 16) for i in (0, 2, 4))
    except ValueError:
        r, g, b = 124, 140, 255
    return f'rgba({r},{g},{b},{alfa})'


def aplicar(fig: go.Figure, chave: str, s: pd.Series, unidade: str = '',
            **kwargs_trace) -> list[dict]:
    """Desenha no `fig` todas as regressões ativas da série `chave`.

    Use os mesmos kwargs que passaria em fig.add_trace (row/col ou
    secondary_y) para gráficos com subplots ou dois eixos Y.
    Devolve um resumo de cada ajuste (para a interface exibir as métricas).
    """
    if s is None or s.dropna().empty:
        return []

    resumo = []
    for i, reg in enumerate(listar(chave), start=1):
        if not reg.get('ativo', True):
            continue

        resultado = ajustar(s, reg, unidade=unidade)
        if resultado is None:
            resumo.append({'id': reg.get('id'), 'nome': reg.get('nome'),
                           'erro': 'Pontos insuficientes na janela escolhida.'})
            continue

        if resultado['banda'] is not None:
            baixo, alto = resultado['banda']
            fig.add_trace(go.Scatter(
                x=list(resultado['x']) + list(resultado['x'])[::-1],
                y=list(alto) + list(baixo)[::-1],
                fill='toself', fillcolor=_rgba(reg.get('cor'), 0.13),
                line=dict(width=0), hoverinfo='skip', showlegend=False,
                name=f"{reg.get('nome') or 'Tendência'} — banda",
            ), **kwargs_trace)

        fig.add_trace(go.Scatter(
            x=resultado['x'], y=resultado['y'],
            mode='lines',
            line=dict(color=reg.get('cor') or '#7c8cff',
                      width=float(reg.get('espessura', 2.5)),
                      dash=reg.get('tracejado', 'dash')),
            opacity=float(reg.get('opacidade', 1.0)),
            name=_rotulo(reg, resultado, i),
            showlegend=bool(reg.get('legenda', True)),
            hovertemplate='%{y:.3f}<extra></extra>',
        ), **kwargs_trace)

        resumo.append({
            'id': reg.get('id'),
            'nome': reg.get('nome') or f'Tendência {i}',
            'modelo': reg.get('modelo'),
            'equacao': resultado['equacao'],
            'metricas': resultado['metricas'],
            'parametros': {k: (round(v, 6) if isinstance(v, float) else v)
                           for k, v in resultado['parametros'].items()},
        })

    return resumo
