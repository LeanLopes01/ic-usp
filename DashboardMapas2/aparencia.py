"""
Aparência dos gráficos
======================
Cada gráfico do painel pode ter título próprio, nomes de eixo próprios e
tamanhos de fonte próprios. As escolhas ficam em aparencia.json:

    {
      "global":   { ...valores usados por todo gráfico sem ajuste próprio... },
      "graficos": { "sensor-co2": { ...só para esse card... } }
    }

O eixo X aceita três escalas (`modo_x`):

    tempo   data e hora, como vem do banco
    dias    dias decorridos desde a primeira leitura, com marcas inteiras
            (1, 2, 3 …) — `dia_inicial` define se a contagem começa em 1 ou 0
    horas   horas decorridas desde a primeira leitura
"""

from __future__ import annotations

import json
import os
import re

import numpy as np
import pandas as pd

import config as cfg

MODOS_X = {
    'tempo': 'Data e hora',
    'dias': 'Dias decorridos (1, 2, 3…)',
    'horas': 'Horas decorridas',
}

PADRAO = {
    'titulo': None,               # None = mantém o título automático
    'titulo_tamanho': 15,
    'eixo_x_nome': None,          # None = nome automático
    'eixo_x_tamanho': 13,         # números do eixo
    'eixo_x_nome_tamanho': 13,    # nome do eixo
    'eixo_y_nome': None,
    'eixo_y_tamanho': 13,
    'eixo_y_nome_tamanho': 13,
    'eixo_y2_nome': None,         # eixo da direita, quando existir
    'eixo_y2_tamanho': 13,
    'eixo_y2_nome_tamanho': 13,
    'legenda_tamanho': 12,
    'modo_x': 'tempo',
    'dia_inicial': 1,
    'curvatura_padrao': 1.0,      # suavização das linhas sem curvatura própria
}

# Campos que a aba "Aparência" pode gravar como padrão de todos os gráficos.
CAMPOS_TAMANHO = [c for c in PADRAO if c.endswith('_tamanho')]

_TEXTOS = ('titulo', 'eixo_x_nome', 'eixo_y_nome', 'eixo_y2_nome')
_INTEIROS = CAMPOS_TAMANHO + ['dia_inicial']

_COR_RE = re.compile(r'^#[0-9a-fA-F]{3,8}$')


def _num(v, minimo, maximo):
    try:
        v = float(v)
    except (TypeError, ValueError):
        return None
    return max(minimo, min(maximo, v))


def _normalizar_serie(item: dict) -> dict:
    cor = item.get('cor') or None
    if cor and not _COR_RE.match(cor):
        cor = None
    return {
        'cor': cor,
        'espessura': _num(item.get('espessura'), 0.5, 10),
        'curvatura': _num(item.get('curvatura'), 0, 1.3),
    }


def normalizar_series(bruto: dict | None) -> dict:
    """Cor/espessura/curvatura por série — chaves dinâmicas (variam por
    card), então não cabem no PADRAO fixo. Overrides são só do card, nunca
    herdam do global."""
    saida = {}
    for chave, item in (bruto or {}).items():
        if not isinstance(item, dict):
            continue
        norm = _normalizar_serie(item)
        if any(v is not None for v in norm.values()):
            saida[chave] = norm
    return saida


# ─────────────────────────────────────────────
# PERSISTÊNCIA
# ─────────────────────────────────────────────

def carregar_arquivo() -> dict:
    if not os.path.exists(cfg.APARENCIA_PATH):
        return {'global': {}, 'graficos': {}}
    try:
        with open(cfg.APARENCIA_PATH, 'r', encoding='utf-8') as f:
            dados = json.load(f)
    except (json.JSONDecodeError, OSError):
        return {'global': {}, 'graficos': {}}

    dados.setdefault('global', {})
    dados.setdefault('graficos', {})
    return dados


def salvar_arquivo(dados: dict):
    with open(cfg.APARENCIA_PATH, 'w', encoding='utf-8') as f:
        json.dump(dados, f, ensure_ascii=False, indent=2)


def normalizar(corpo: dict, base: dict | None = None) -> dict:
    conf = dict(base or PADRAO)
    for campo, valor in (corpo or {}).items():
        if campo in PADRAO:
            conf[campo] = valor

    for campo in _TEXTOS:
        texto = (conf.get(campo) or '')
        conf[campo] = texto.strip() or None

    for campo in _INTEIROS:
        try:
            conf[campo] = int(round(float(conf.get(campo))))
        except (TypeError, ValueError):
            conf[campo] = PADRAO[campo]

    for campo in CAMPOS_TAMANHO:
        conf[campo] = max(6, min(72, conf[campo]))

    conf['dia_inicial'] = max(0, min(1, conf['dia_inicial']))
    if conf.get('modo_x') not in MODOS_X:
        conf['modo_x'] = 'tempo'
    curvatura = _num(conf.get('curvatura_padrao'), 0, 1.3)
    conf['curvatura_padrao'] = (PADRAO['curvatura_padrao'] if curvatura is None
                                 else curvatura)
    conf['series'] = normalizar_series((corpo or {}).get('series'))
    return conf


def obter(id_card: str | None) -> dict:
    """Padrão de fábrica ← ajustes globais ← ajustes do card."""
    dados = carregar_arquivo()
    conf = normalizar(dados.get('global', {}))
    if id_card:
        conf = normalizar(dados.get('graficos', {}).get(id_card, {}), base=conf)
    return conf


def salvar(id_card: str, corpo: dict) -> dict:
    dados = carregar_arquivo()
    base = normalizar(dados.get('global', {}))
    conf = normalizar(corpo, base=base)
    dados['graficos'][id_card] = conf
    salvar_arquivo(dados)
    return conf


def salvar_global(corpo: dict) -> dict:
    """Padrão de todos os gráficos. Só tamanhos de fonte e escala do eixo X —
    títulos e nomes de eixo são de cada gráfico, não faria sentido repetir o
    mesmo texto em todos."""
    dados = carregar_arquivo()
    corpo = {k: v for k, v in (corpo or {}).items() if k not in _TEXTOS and k != 'series'}
    conf = normalizar(corpo, base=normalizar(dados.get('global', {})))
    dados['global'] = conf
    salvar_arquivo(dados)
    return conf


def restaurar(id_card: str) -> bool:
    dados = carregar_arquivo()
    if id_card not in dados.get('graficos', {}):
        return False
    del dados['graficos'][id_card]
    salvar_arquivo(dados)
    return True


def personalizados() -> list[str]:
    return sorted(carregar_arquivo().get('graficos', {}))


# ─────────────────────────────────────────────
# APLICAÇÃO NA FIGURA
# ─────────────────────────────────────────────

def _eh_temporal(x) -> bool:
    """True só quando o eixo x realmente carrega datas — evita converter
    histogramas e gráficos de dispersão, cujo x é numérico."""
    if x is None or len(x) == 0:
        return False
    arr = np.asarray(x)
    if np.issubdtype(arr.dtype, np.datetime64):
        return True
    if arr.dtype == object:
        return isinstance(arr.flat[0], (pd.Timestamp, np.datetime64))
    return False


def _converter_eixo_x(fig, conf: dict, origem):
    """Troca as datas por dias ou horas decorridos desde `origem`."""
    if conf['modo_x'] == 'tempo' or origem is None:
        return False

    origem = pd.Timestamp(origem)
    divisor = 86400.0 if conf['modo_x'] == 'dias' else 3600.0
    deslocamento = conf['dia_inicial'] if conf['modo_x'] == 'dias' else 0

    converteu = False
    for trace in fig.data:
        x = getattr(trace, 'x', None)
        if not _eh_temporal(x):
            continue
        instantes = pd.to_datetime(pd.Series(list(x)))
        trace.x = ((instantes - origem).dt.total_seconds() / divisor
                   + deslocamento).values
        converteu = True

    return converteu


def _aplicar_series(fig, series: dict):
    """Cor/espessura/curvatura por série, casando pelo `meta` gravado em
    cada trace na hora de montar a figura (graficos.py)."""
    if not series:
        return
    for trace in fig.data:
        overrides = series.get(getattr(trace, 'meta', None))
        if not overrides or getattr(trace, 'line', None) is None:
            continue
        if overrides.get('cor'):
            trace.line.color = overrides['cor']
        if overrides.get('espessura') is not None:
            trace.line.width = overrides['espessura']
        if overrides.get('curvatura') is not None:
            trace.line.shape = 'spline'
            trace.line.smoothing = overrides['curvatura']


def _aplicar_curvatura_padrao(fig, curvatura: float, series: dict):
    """Suaviza toda linha de série (traces com `meta`, gravado em
    graficos.py) que não tenha uma curvatura própria (acima, em
    `_aplicar_series`) — é o que faz o traçado curvar em vez de quebrar em
    ângulos retos a cada ponto (ex.: IQAr, PM). Linhas de tendência
    (regressao.py) não têm `meta` e ficam de fora de propósito: precisam
    seguir exatamente o ajuste calculado, sem interpolação de curva."""
    for trace in fig.data:
        chave = getattr(trace, 'meta', None)
        if chave is None or getattr(trace, 'type', None) != 'scatter':
            continue
        if 'lines' not in (getattr(trace, 'mode', None) or ''):
            continue
        overrides = series.get(chave)
        if overrides and overrides.get('curvatura') is not None:
            continue
        trace.line.shape = 'spline'
        trace.line.smoothing = curvatura


def aplicar(fig, conf: dict, origem=None):
    """Aplica título, nomes e tamanhos de fonte, e reescala o eixo X."""
    series = conf.get('series') or {}
    _aplicar_series(fig, series)
    _aplicar_curvatura_padrao(fig, conf['curvatura_padrao'], series)
    converteu = _converter_eixo_x(fig, conf, origem)

    # ── título
    titulo = fig.layout.title.text
    fig.update_layout(
        title=dict(text=conf['titulo'] or titulo,
                   font=dict(size=conf['titulo_tamanho'])),
        legend=dict(font=dict(size=conf['legenda_tamanho'])),
    )

    # ── eixo X
    nome_x = conf['eixo_x_nome']
    if nome_x is None and converteu:
        nome_x = 'Dia' if conf['modo_x'] == 'dias' else 'Hora'

    ajuste_x = dict(tickfont=dict(size=conf['eixo_x_tamanho']))
    if nome_x is not None:
        ajuste_x['title'] = dict(text=nome_x,
                                 font=dict(size=conf['eixo_x_nome_tamanho']))
    else:
        ajuste_x['title_font'] = dict(size=conf['eixo_x_nome_tamanho'])

    if converteu:
        ajuste_x['type'] = 'linear'
        if conf['modo_x'] == 'dias':
            # marcas de 1 em 1 dia, sem casas decimais
            ajuste_x.update(tickmode='linear', tick0=conf['dia_inicial'],
                            dtick=1, tickformat='d')
    fig.update_xaxes(**ajuste_x)

    # ── eixos Y: o secundário é o que fica sobreposto ao primeiro
    for nome_eixo in [k for k in fig.layout if re.fullmatch(r'yaxis\d*', k)]:
        eixo = fig.layout[nome_eixo]
        secundario = getattr(eixo, 'overlaying', None) == 'y'
        prefixo = 'eixo_y2' if secundario else 'eixo_y'

        eixo.tickfont = dict(eixo.tickfont.to_plotly_json() or {},
                             size=conf[f'{prefixo}_tamanho'])
        nome = conf[f'{prefixo}_nome']
        if nome is not None:
            eixo.title.text = nome
        eixo.title.font = dict(eixo.title.font.to_plotly_json() or {},
                               size=conf[f'{prefixo}_nome_tamanho'])

    return fig
