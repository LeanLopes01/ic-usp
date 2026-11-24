"""
Configuração central do painel
==============================
Tudo que é "conteúdo" (quais sensores existem, cores, limites de alerta,
faixas do IQAr e quais cards aparecem em cada aba) mora aqui. Para incluir
um sensor novo basta acrescentar uma entrada em SENSORES_* e citar a chave
em ABAS — o resto do app se adapta sozinho, inclusive as regressões.
"""

import os

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
DB_PATH = os.path.join(BASE_DIR, 'banco_teste_gases.sqlite')
SQL_DUMP_PATH = os.path.join(BASE_DIR, 'banco_teste_gases.sql')
REGRESSOES_PATH = os.path.join(BASE_DIR, 'regressoes.json')
APARENCIA_PATH = os.path.join(BASE_DIR, 'aparencia.json')
TABELA = 'leituras_sensores'

# Tamanho da fonte dos eixos. Estava fixo em 48 no app antigo; agora é um
# ajuste único que vale para todos os gráficos.
FONTE_EIXO = 13
FONTE_TITULO_EIXO = 13

# ─────────────────────────────────────────────
# DUMP → TABELA LARGA
# ─────────────────────────────────────────────
# O dump banco_teste_gases.sql vem no formato longo do ArgusDB
# (uma linha por variável/timestamp). Este mapa converte a coluna
# `Variable` do dump para as colunas da tabela `leituras_sensores`.
VARIAVEL_DUMP_PARA_COLUNA = {
    'co2': 'scd40_co2_ppm',
    'mq135': 'mq135_adc',
    'mq2': 'mq2_adc',
    'mq3': 'mq3_adc',
    'mq9': 'mq9_adc',
    'pm1': 'pms5003_pm1_0',
    'pm2.5': 'pms5003_pm2_5',
    'pm10': 'pms5003_pm10_0',
    'temp': 'aht10_temp_c',
    'umidade': 'aht10_hum_percent',
}

# ─────────────────────────────────────────────
# SENSORES
# ─────────────────────────────────────────────
# `permite_negativo=False` faz leituras negativas virarem lacuna (elas são
# fisicamente impossíveis nestes sensores).
SENSORES_GAS = {
    'mq3': {
        'coluna': 'mq3_adc', 'nome': 'MQ-3', 'gas': 'Álcool / Etanol',
        'cor': '#ff5c7a', 'unidade': 'ADC', 'alerta': 2000, 'permite_negativo': False,
    },
    'mq9': {
        'coluna': 'mq9_adc', 'nome': 'MQ-9', 'gas': 'CO / Gás combustível',
        'cor': '#3ddc97', 'unidade': 'ADC', 'alerta': 2000, 'permite_negativo': False,
    },
    'mq135': {
        'coluna': 'mq135_adc', 'nome': 'MQ-135', 'gas': 'CO₂ / NH₃ / VOC',
        'cor': '#4cc9f0', 'unidade': 'ADC', 'alerta': 1500, 'permite_negativo': False,
    },
    'mq2': {
        'coluna': 'mq2_adc', 'nome': 'MQ-2', 'gas': 'GLP / Fumaça / Metano',
        'cor': '#ffb703', 'unidade': 'ADC', 'alerta': 2000, 'permite_negativo': False,
    },
}

SENSORES_AMBIENTE = {
    'aht10_temp': {
        'coluna': 'aht10_temp_c', 'nome': 'Temperatura', 'gas': 'AHT10',
        'cor': '#ff7849', 'unidade': '°C', 'alerta': None, 'permite_negativo': True,
    },
    'aht10_hum': {
        'coluna': 'aht10_hum_percent', 'nome': 'Umidade', 'gas': 'AHT10',
        'cor': '#38bdf8', 'unidade': '%', 'alerta': None, 'permite_negativo': False,
    },
}

SENSORES_CO2 = {
    'co2': {
        'coluna': 'scd40_co2_ppm', 'nome': 'CO₂ (SCD40)', 'gas': 'Dióxido de carbono',
        'cor': '#a78bfa', 'unidade': 'ppm', 'alerta': 1000, 'permite_negativo': False,
        'piso_zero': False,  # eixo Y começa na menor leitura, não em zero
    },
}

SENSORES_PM = {
    'pm1_0': {
        'piso_zero': True,
        'coluna': 'pms5003_pm1_0', 'nome': 'PM1.0', 'gas': 'PMS5003',
        'cor': '#2dd4bf', 'unidade': 'µg/m³', 'alerta': 50, 'permite_negativo': False,
    },
    'pm2_5': {
        'piso_zero': True,
        'coluna': 'pms5003_pm2_5', 'nome': 'PM2.5', 'gas': 'PMS5003',
        'cor': '#f59e0b', 'unidade': 'µg/m³', 'alerta': 35, 'permite_negativo': False,
    },
    'pm10_0': {
        'piso_zero': True,
        'coluna': 'pms5003_pm10_0', 'nome': 'PM10', 'gas': 'PMS5003',
        'cor': '#ef4444', 'unidade': 'µg/m³', 'alerta': 70, 'permite_negativo': False,
    },
}

TODOS_SENSORES = {}
for _grupo in (SENSORES_GAS, SENSORES_AMBIENTE, SENSORES_CO2, SENSORES_PM):
    TODOS_SENSORES.update(_grupo)

# Série virtual: o IQAr também aceita regressão, como qualquer sensor.
SERIE_IQA = {
    'iqa': {'coluna': None, 'nome': 'IQAr', 'gas': 'CONAMA 506/2024',
            'cor': '#818cf8', 'unidade': 'índice', 'alerta': 100,
            'permite_negativo': False},
}

# Toda série que pode receber linhas de regressão (sensores reais + IQAr).
SERIES_REGRESSIVEIS = dict(TODOS_SENSORES)
SERIES_REGRESSIVEIS.update(SERIE_IQA)

# ─────────────────────────────────────────────
# ÍNDICE DE QUALIDADE DO AR — RES. CONAMA Nº 506/2024
# ─────────────────────────────────────────────
IQA_FAIXAS = [
    (0,   40,  'Boa',        '#3ddc97'),
    (40,  80,  'Moderada',   '#ffd166'),
    (80,  120, 'Ruim',       '#fb8500'),
    (120, 200, 'Muito ruim', '#ef476f'),
    (200, 400, 'Péssima',    '#9d4edd'),
]

# (Iini, Ifin, Cini, Cfin) por poluente. A faixa N1 vem do Anexo II; N2–N5
# foram derivadas alinhando as categorias do índice aos padrões PI-4…PI-1
# do Anexo I. Ajuste aqui se precisar bater com outra tabela.
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

POLUENTES_IQA = {'pm10_0': 'MP₁₀', 'pm2_5': 'MP₂,₅'}

# ─────────────────────────────────────────────
# PERÍODOS DO SELETOR
# ─────────────────────────────────────────────
PERIODOS = [
    (1, 'Última hora'),
    (6, 'Últimas 6 horas'),
    (24, 'Últimas 24 horas'),
    (72, 'Últimos 3 dias'),
    (168, 'Última semana'),
    (720, 'Últimos 30 dias'),
    (0, 'Todo o histórico'),
]

ROTULOS_PERIODO = {1: '1h', 6: '6h', 24: '24h', 72: '3dias',
                   168: '1semana', 720: '30dias', 0: 'tudo'}

# ─────────────────────────────────────────────
# ESTRUTURA DAS ABAS
# ─────────────────────────────────────────────
# Cada card descreve: qual figura montar (`figura` + `params`), a altura em
# px e quais séries o botão "Regressões" oferece (`regressao`).
# `vistas` cria as sub-abas internas do card (série / distribuição / stats).
VISTAS_SENSOR = [
    ('serie', 'Série temporal'),
    ('distribuicao', 'Distribuição'),
    ('estatisticas', 'Estatísticas'),
]


def _card_sensor(chave, cfg, largura='meia'):
    return {
        'id': f'sensor-{chave}',
        'titulo': f"{cfg['nome']} — {cfg.get('gas', '')}".strip(' —'),
        'figura': 'sensor',
        'params': {'chave': chave},
        'vistas': VISTAS_SENSOR,
        'altura': 420,
        'largura': largura,
        'regressao': [chave],
    }


ABAS = [
    {
        'id': 'visao',
        'titulo': 'Visão geral',
        'cards': [
            {'id': 'visao-geral', 'titulo': 'Todos os sensores',
             'descricao': 'Quatro faixas: gases MOS, clima, CO₂ e material particulado.',
             'figura': 'visao_geral', 'altura': 1180, 'largura': 'inteira',
             'regressao': []},
        ],
    },
    {
        'id': 'gases',
        'titulo': 'Gases (MOS)',
        'cards': [
            {'id': 'combo-gases', 'titulo': 'Comparativo dos quatro sensores',
             'descricao': 'Valores brutos do ADC (0–4095 no ESP32).',
             'figura': 'combo', 'params': {'chaves': list(SENSORES_GAS)},
             'altura': 460, 'largura': 'inteira', 'regressao': list(SENSORES_GAS)},
        ] + [_card_sensor(k, SENSORES_GAS[k]) for k in SENSORES_GAS],
    },
    {
        'id': 'clima',
        'titulo': 'Clima',
        'cards': [
            {'id': 'combo-clima', 'titulo': 'Temperatura e umidade',
             'descricao': 'Eixos independentes: temperatura à esquerda, umidade à direita.',
             'figura': 'combo',
             'params': {'chaves': ['aht10_temp', 'aht10_hum'], 'secundario': 'aht10_hum'},
             'altura': 460, 'largura': 'inteira',
             'regressao': ['aht10_temp', 'aht10_hum']},
        ] + [_card_sensor(k, SENSORES_AMBIENTE[k]) for k in SENSORES_AMBIENTE],
    },
    {
        'id': 'co2',
        'titulo': 'CO₂',
        'cards': [_card_sensor('co2', SENSORES_CO2['co2'], largura='inteira')],
    },
    {
        'id': 'particulado',
        'titulo': 'Particulado',
        'cards': [
            {'id': 'combo-pm', 'titulo': 'PM1.0, PM2.5 e PM10',
             'descricao': 'Concentração em massa medida pelo PMS5003.',
             'figura': 'combo', 'params': {'chaves': list(SENSORES_PM)},
             'altura': 460, 'largura': 'inteira', 'regressao': list(SENSORES_PM)},
        ] + [_card_sensor(k, SENSORES_PM[k]) for k in SENSORES_PM],
    },
    {
        'id': 'iqa',
        'titulo': 'IQAr',
        'cards': [
            {'id': 'iqa', 'titulo': 'Índice de qualidade do ar',
             'descricao': 'Res. CONAMA 506/2024 — o maior sub-índice entre MP₁₀ e MP₂,₅ define o IQAr.',
             'figura': 'iqa', 'altura': 520, 'largura': 'inteira', 'regressao': ['iqa']},
            {'id': 'subindices', 'titulo': 'Sub-índices por poluente',
             'descricao': 'Contribuição de cada poluente para o índice final.',
             'figura': 'subindices', 'altura': 420, 'largura': 'inteira', 'regressao': []},
        ],
    },
    {
        'id': 'analise',
        'titulo': 'Análise',
        'cards': [
            {'id': 'correlacao', 'titulo': 'Correlação entre sensores',
             'descricao': 'Coeficiente de Pearson sobre as séries já limpas.',
             'figura': 'correlacao', 'altura': 560, 'largura': 'inteira', 'regressao': []},
            {'id': 'anomalias', 'titulo': 'Detecção de anomalias',
             'descricao': 'Isolation Forest sobre todas as colunas numéricas.',
             'figura': 'anomalias', 'altura': 520, 'largura': 'inteira', 'regressao': []},
            {'id': 'dispersao', 'titulo': 'Dispersão entre duas séries',
             'descricao': 'Escolha o par de sensores e veja o ajuste linear entre eles.',
             'figura': 'dispersao', 'params': {'x': 'aht10_temp', 'y': 'co2'},
             'altura': 520, 'largura': 'inteira', 'regressao': [],
             'seletor_xy': True},
        ],
    },
]

# ─────────────────────────────────────────────
# TEMAS DOS GRÁFICOS
# ─────────────────────────────────────────────
TEMAS = {
    'escuro': {
        'template': 'plotly_dark',
        'papel': 'rgba(0,0,0,0)',
        'plot': 'rgba(0,0,0,0)',
        'texto': '#c7d2e8',
        'texto_suave': '#8592b8',
        'grade': 'rgba(255,255,255,0.06)',
        'linha_eixo': 'rgba(255,255,255,0.14)',
        'hover_fundo': 'rgba(11,16,32,0.95)',
        'hover_texto': '#e6ecff',
        'hover_borda': '#7c8cff',
        'legenda_fundo': 'rgba(21,27,46,0.85)',
        'legenda_borda': '#232c45',
        'superficie': '#151b2e',
        'contraste': '#e6ecff',
    },
    'claro': {
        'template': 'plotly_white',
        'papel': 'white',
        'plot': 'white',
        'texto': '#1e293b',
        'texto_suave': '#475569',
        'grade': 'rgba(15,23,42,0.08)',
        'linha_eixo': 'rgba(15,23,42,0.25)',
        'hover_fundo': 'white',
        'hover_texto': '#0f172a',
        'hover_borda': '#94a3b8',
        'legenda_fundo': 'rgba(255,255,255,0.9)',
        'legenda_borda': '#cbd5e1',
        'superficie': '#ffffff',
        'contraste': '#0f172a',
    },
}


def layout_base(tema: str = 'escuro') -> dict:
    """Devolve um layout novo (nunca o mesmo dicionário) para o tema pedido."""
    t = TEMAS.get(tema, TEMAS['escuro'])
    return dict(
        template=t['template'],
        paper_bgcolor=t['papel'],
        plot_bgcolor=t['plot'],
        margin=dict(l=60, r=28, t=54, b=44),
        hovermode='x unified',
        hoverlabel=dict(bgcolor=t['hover_fundo'], font_size=12,
                        font_color=t['hover_texto'], bordercolor=t['hover_borda']),
        legend=dict(bgcolor=t['legenda_fundo'], bordercolor=t['legenda_borda'],
                    borderwidth=1, orientation='h', yanchor='bottom', y=1.02,
                    xanchor='left', x=0),
        font=dict(family='Inter, Segoe UI, sans-serif', color=t['texto']),
        title=dict(font=dict(size=15, color=t['contraste'])),
    )


def aplicar_layout(fig, tema: str = 'escuro', titulo: str | None = None, **extra):
    """Aplica o layout do tema já mesclando o título — evita passar `title`
    duas vezes para o update_layout."""
    base = layout_base(tema)
    if titulo is not None:
        base['title'] = dict(base['title'], text=titulo)
    base.update(extra)
    fig.update_layout(**base)
    return fig


def aplicar_grade(fig, tema='escuro', rows=1, cols=1, subplot=False):
    t = TEMAS.get(tema, TEMAS['escuro'])
    eixo = dict(
        gridcolor=t['grade'],
        linecolor=t['linha_eixo'],
        zerolinecolor=t['grade'],
        tickfont=dict(color=t['texto_suave'], size=FONTE_EIXO),
        title_font=dict(color=t['texto_suave'], size=FONTE_TITULO_EIXO),
    )
    if not subplot:
        fig.update_xaxes(**eixo)
        fig.update_yaxes(**eixo)
        return
    for r in range(1, rows + 1):
        for c in range(1, cols + 1):
            fig.update_xaxes(**eixo, row=r, col=c)
            fig.update_yaxes(**eixo, row=r, col=c)


def catalogo_cards() -> list[dict]:
    """Todos os cards de gráfico do painel, na ordem em que aparecem — usado
    pela aba Aparência para montar a lista de gráficos personalizáveis."""
    catalogo = []
    for aba in ABAS:
        for card in aba['cards']:
            catalogo.append({
                'id': card['id'],
                'titulo': card['titulo'],
                'aba': aba['titulo'],
                'figura': card['figura'],
                'params': card.get('params', {}),
                'altura': card.get('altura', 420),
                'vistas': [v[0] for v in card.get('vistas', [])],
            })
    return catalogo
