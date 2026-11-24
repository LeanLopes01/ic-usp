"""
Construtores de figuras
=======================
Toda figura de série temporal chama `regressao.aplicar(...)`, então qualquer
gráfico do painel aceita as mesmas linhas de tendência configuráveis.
As funções devolvem (figura, resumo_das_regressoes).
"""

import numpy as np
import pandas as pd
import plotly.graph_objs as go
from plotly.subplots import make_subplots

import config as cfg
import dados
import regressao


def _vazia(mensagem: str, tema: str) -> go.Figure:
    fig = go.Figure()
    cfg.aplicar_layout(fig, tema, mensagem)
    fig.update_xaxes(visible=False)
    fig.update_yaxes(visible=False)
    return fig


# ─────────────────────────────────────────────
# SENSOR INDIVIDUAL
# ─────────────────────────────────────────────

def sensor(df, chave: str, vista: str = 'serie', tema: str = 'claro'):
    conf = cfg.SERIES_REGRESSIVEIS.get(chave)
    if conf is None:
        return _vazia(f"Sensor '{chave}' não existe.", tema), []

    s = dados.serie(df, chave)
    if s.dropna().empty:
        return _vazia(f"Sem leituras de {conf['nome']} no período.", tema), []

    if vista == 'distribuicao':
        return _distribuicao(s, conf, tema), []
    if vista == 'estatisticas':
        return _estatisticas(s, conf, tema), []
    return _serie_temporal(s, chave, conf, tema)


def _serie_temporal(s, chave, conf, tema):
    fig = go.Figure()
    cor = conf['cor']

    fig.add_trace(go.Scatter(
        x=s.index, y=s, mode='lines',
        line=dict(color=cor, width=1.4, shape='spline'),
        opacity=0.45, name=conf['nome'], connectgaps=True, meta=chave,
    ))

    media_movel = s.rolling(6, center=True, min_periods=1).mean()
    fig.add_trace(go.Scatter(
        x=media_movel.index, y=media_movel, mode='lines',
        line=dict(color=cor, width=2.6, shape='spline'),
        name='Média móvel (6)', connectgaps=True, meta=chave,
    ))

    media = float(s.mean())
    fig.add_hline(y=media, line_dash='dot', line_color=cfg.TEMAS[tema]['texto_suave'],
                  annotation_text=f'Média {media:.2f} {conf["unidade"]}',
                  annotation_font_color=cfg.TEMAS[tema]['texto_suave'])

    if conf.get('alerta'):
        fig.add_hline(y=conf['alerta'], line_dash='dash', line_color='#ef476f',
                      annotation_text=f'Alerta {conf["alerta"]} {conf["unidade"]}',
                      annotation_font_color='#ef476f')

    resumo = regressao.aplicar(fig, chave, s, unidade=conf['unidade'])

    # Só o particulado começa em zero; nos demais o zero esmagaria a variação.
    minimo, maximo = float(s.min()), float(s.max())
    margem = (maximo - minimo) * 0.08 or abs(maximo) * 0.08 or 1
    y_min = 0 if conf.get('piso_zero', False) else minimo - margem
    fig.update_yaxes(title_text=conf['unidade'], range=[y_min, maximo + margem])
    cfg.aplicar_layout(fig, tema, f"{conf['nome']} — série temporal ({conf['unidade']})")
    cfg.aplicar_grade(fig, tema)
    return fig, resumo


def _distribuicao(s, conf, tema):
    fig = make_subplots(rows=1, cols=2, column_widths=[0.68, 0.32],
                        subplot_titles=['Histograma', 'Box plot'])
    valores = s.dropna().values

    fig.add_trace(go.Histogram(x=valores, nbinsx=40, marker_color=conf['cor'],
                               opacity=0.75, name='Frequência'), row=1, col=1)
    try:
        from scipy.stats import gaussian_kde
        kde = gaussian_kde(valores)
        grade = np.linspace(valores.min(), valores.max(), 240)
        escala = len(valores) * (valores.max() - valores.min()) / 40
        fig.add_trace(go.Scatter(x=grade, y=kde(grade) * escala, mode='lines',
                                 line=dict(color=cfg.TEMAS[tema]['contraste'], width=2),
                                 name='Densidade'), row=1, col=1)
    except Exception:
        pass

    fig.add_trace(go.Box(y=valores, marker_color=conf['cor'], line_color=conf['cor'],
                         name=conf['nome'], boxmean='sd'), row=1, col=2)

    cfg.aplicar_layout(fig, tema, f"{conf['nome']} — distribuição")
    cfg.aplicar_grade(fig, tema, rows=1, cols=2, subplot=True)
    return fig


def _estatisticas(s, conf, tema):
    st = dados.estatisticas(s)
    t = cfg.TEMAS[tema]

    fig = make_subplots(rows=1, cols=2, column_widths=[0.42, 0.58],
                        specs=[[{'type': 'indicator'}, {'type': 'table'}]])

    ultimo = float(s.dropna().iloc[-1])
    maximo = st.get('p95', 1) * 1.5 or 1
    if conf.get('alerta'):
        maximo = max(maximo, conf['alerta'] * 1.25)

    fig.add_trace(go.Indicator(
        mode='gauge+number+delta',
        value=ultimo,
        delta={'reference': st.get('media', 0), 'valueformat': '.2f'},
        title={'text': f"{conf['nome']}<br><span style='font-size:0.75em;color:{t['texto_suave']}'>"
                       f"{conf.get('gas') or conf['unidade']}</span>"},
        gauge={
            'axis': {'range': [0, maximo], 'tickcolor': t['texto_suave']},
            'bar': {'color': conf['cor']},
            'steps': [
                {'range': [0, maximo * 0.4], 'color': 'rgba(61,220,151,0.16)'},
                {'range': [maximo * 0.4, maximo * 0.7], 'color': 'rgba(255,183,3,0.16)'},
                {'range': [maximo * 0.7, maximo], 'color': 'rgba(239,71,111,0.16)'},
            ],
            'threshold': {'line': {'color': t['contraste'], 'width': 3},
                          'thickness': 0.8, 'value': st.get('media', 0)},
        },
    ), row=1, col=1)

    rotulos = ['Mínimo', 'Máximo', 'Média', 'Mediana', 'Desvio padrão',
               'Percentil 95', 'Leituras']
    valores = [st.get(k) for k in ('min', 'max', 'media', 'mediana', 'desvio', 'p95', 'n')]

    fig.add_trace(go.Table(
        header=dict(values=['<b>Medida</b>', f"<b>{conf['unidade']}</b>"],
                    fill_color=t['superficie'], font=dict(color=t['hover_borda'], size=12),
                    line_color=t['legenda_borda'], align='left'),
        cells=dict(values=[rotulos, valores],
                   fill_color=t['papel'] if tema == 'claro' else 'rgba(11,16,32,0.6)',
                   font=dict(color=[t['texto_suave'], t['contraste']], size=12),
                   line_color=t['legenda_borda'], align='left', height=26),
    ), row=1, col=2)

    cfg.aplicar_layout(fig, tema, f"{conf['nome']} — painel estatístico")
    return fig


# ─────────────────────────────────────────────
# COMBINADOS
# ─────────────────────────────────────────────

def combo(df, chaves: list[str], tema: str = 'claro', secundario: str | None = None,
          titulo: str | None = None):
    """Várias séries no mesmo gráfico. `secundario` joga uma delas no eixo
    Y da direita (usado para temperatura × umidade)."""
    if secundario:
        fig = make_subplots(specs=[[{'secondary_y': True}]])
    else:
        fig = go.Figure()

    resumo = []
    tem_dados = False
    faixa_esq = []
    faixa_dir = []

    for chave in chaves:
        conf = cfg.SERIES_REGRESSIVEIS.get(chave)
        if not conf:
            continue
        s = dados.serie(df, chave)
        if s.dropna().empty:
            continue

        tem_dados = True
        direita = chave == secundario
        extra = {'secondary_y': direita} if secundario else {}

        fig.add_trace(go.Scatter(
            x=s.index, y=s, mode='lines',
            line=dict(color=conf['cor'], width=2.4, shape='spline'),
            name=f"{conf['nome']} ({conf['unidade']})", connectgaps=True,
            meta=chave,
        ), **extra)

        resumo += regressao.aplicar(fig, chave, s, unidade=conf['unidade'], **extra)
        (faixa_dir if direita else faixa_esq).append((float(s.min()), float(s.max()), conf))

    if not tem_dados:
        return _vazia('Sem leituras no período selecionado.', tema), []

    def _aplica_faixa(faixa, direita):
        if not faixa:
            return
        minimo = min(f[0] for f in faixa)
        maximo = max(f[1] for f in faixa)
        conf = faixa[0][2]
        unidade = conf['unidade'] if len({f[2]['unidade'] for f in faixa}) == 1 else ''
        margem = (maximo - minimo) * 0.06 or abs(maximo) * 0.06 or 1
        args = dict(title_text=unidade, range=[minimo - margem, maximo + margem])
        if secundario:
            fig.update_yaxes(**args, secondary_y=direita, showgrid=not direita,
                             linecolor=conf['cor'],
                             title_font=dict(color=conf['cor'], size=cfg.FONTE_TITULO_EIXO),
                             tickfont=dict(color=conf['cor'], size=cfg.FONTE_EIXO))
        else:
            fig.update_yaxes(**args)

    _aplica_faixa(faixa_esq, False)
    _aplica_faixa(faixa_dir, True)

    if not secundario:
        cfg.aplicar_grade(fig, tema)
    else:
        t = cfg.TEMAS[tema]
        fig.update_xaxes(gridcolor=t['grade'], linecolor=t['linha_eixo'],
                         tickfont=dict(color=t['texto_suave'], size=cfg.FONTE_EIXO))

    nomes = ', '.join(cfg.SERIES_REGRESSIVEIS[k]['nome'] for k in chaves
                      if k in cfg.SERIES_REGRESSIVEIS)
    cfg.aplicar_layout(fig, tema, titulo or nomes)
    return fig, resumo


def visao_geral(df, tema: str = 'claro'):
    grupos = [
        ('Gases (MOS)', list(cfg.SENSORES_GAS)),
        ('Clima (AHT10)', list(cfg.SENSORES_AMBIENTE)),
        ('CO₂ (SCD40)', list(cfg.SENSORES_CO2)),
        ('Material particulado (PMS5003)', list(cfg.SENSORES_PM)),
    ]
    fig = make_subplots(rows=len(grupos), cols=1, vertical_spacing=0.06,
                        subplot_titles=[g[0] for g in grupos])

    resumo = []
    for linha, (_, chaves) in enumerate(grupos, start=1):
        for chave in chaves:
            conf = cfg.TODOS_SENSORES[chave]
            s = dados.serie(df, chave)
            if s.dropna().empty:
                continue
            fig.add_trace(go.Scatter(
                x=s.index, y=s, mode='lines',
                line=dict(color=conf['cor'], width=2, shape='spline'),
                name=f"{conf['nome']} ({conf['unidade']})", connectgaps=True,
                meta=chave,
            ), row=linha, col=1)
            resumo += regressao.aplicar(fig, chave, s, unidade=conf['unidade'],
                                        row=linha, col=1)

    cfg.aplicar_layout(fig, tema, 'Todos os sensores', showlegend=True)
    cfg.aplicar_grade(fig, tema, rows=len(grupos), cols=1, subplot=True)
    return fig, resumo


# ─────────────────────────────────────────────
# IQAr
# ─────────────────────────────────────────────

def iqa(df, tema: str = 'claro'):
    tabela = dados.calcular_iqa(df)
    if tabela.empty or tabela['iqa'].dropna().empty:
        return _vazia('Sem material particulado suficiente para calcular o IQAr.', tema), []

    indice = tabela['iqa']
    cores = [dados.faixa_iqa(v)[1] for v in indice]
    rotulos = [dados.faixa_iqa(v)[0] for v in indice]
    dominantes = [cfg.POLUENTES_IQA.get(d, '—') if pd.notna(d) else '—'
                  for d in tabela['dominante']]

    y_max = max(float(indice.max()) * 1.08, 45.0)

    fig = go.Figure()
    for i_ini, i_fim, nome, cor in cfg.IQA_FAIXAS:
        if i_ini >= y_max:
            break
        fig.add_hrect(y0=i_ini, y1=min(i_fim, y_max), fillcolor=cor,
                      opacity=0.14 if tema == 'escuro' else 0.18, line_width=0,
                      annotation_text=nome, annotation_position='top left',
                      annotation_font=dict(size=10, color=cor))

    fig.add_trace(go.Scatter(
        x=indice.index, y=indice, mode='lines+markers',
        line=dict(color=cfg.TEMAS[tema]['texto_suave'], width=1.4),
        marker=dict(color=cores, size=7, line=dict(width=0.5,
                    color=cfg.TEMAS[tema]['contraste'])),
        name='IQAr', connectgaps=True, meta='iqa',
        customdata=np.stack([rotulos, dominantes], axis=-1),
        hovertemplate='<b>IQAr %{y:.1f}</b> — %{customdata[0]}<br>'
                      'Dominante: %{customdata[1]}<extra></extra>',
    ))

    resumo = regressao.aplicar(fig, 'iqa', indice, unidade='índice')

    atual = float(indice.dropna().iloc[-1])
    nivel, _ = dados.faixa_iqa(atual)
    fig.update_yaxes(title_text='IQAr', range=[0, y_max])
    cfg.aplicar_layout(fig, tema, f'IQAr — atual {atual:.1f} ({nivel})')
    cfg.aplicar_grade(fig, tema)
    return fig, resumo


def subindices(df, tema: str = 'claro'):
    tabela = dados.calcular_iqa(df)
    poluentes = [c for c in tabela.columns if c in cfg.BREAKPOINTS_IQA]
    if not poluentes:
        return _vazia('Sem sub-índices para exibir.', tema), []

    fig = go.Figure()
    for chave in poluentes:
        conf = cfg.TODOS_SENSORES[chave]
        fig.add_trace(go.Scatter(
            x=tabela.index, y=tabela[chave], mode='lines',
            line=dict(color=conf['cor'], width=2.2, shape='spline'),
            name=f"Sub-índice {cfg.POLUENTES_IQA.get(chave, conf['nome'])}",
            connectgaps=True, meta=chave,
        ))

    fig.update_yaxes(title_text='sub-índice')
    cfg.aplicar_layout(fig, tema, 'Sub-índices por poluente')
    cfg.aplicar_grade(fig, tema)
    return fig, []


# ─────────────────────────────────────────────
# BOX PLOTS POR PERÍODO
# ─────────────────────────────────────────────

def box_intervalos(df, chave: str, bin_valor: int, bin_unidade: str,
                   ancora_hora: int = 0, ancora_minuto: int = 0,
                   ancora_dia_semana: int = 0, filtro_dias: str = 'todos',
                   tema: str = 'claro'):
    """Um box plot por bloco de tempo, em ordem cronológica — não agrega
    'dia típico', cada janela do período vira um box separado."""
    conf = cfg.SERIES_REGRESSIVEIS.get(chave)
    if conf is None:
        return _vazia(f"Sensor '{chave}' não existe.", tema), []

    s = dados.serie(df, chave).dropna()
    s = dados.filtrar_dias_semana(s, filtro_dias)
    if s.empty:
        return _vazia('Sem leituras no intervalo e filtro escolhidos.', tema), []

    rotulos = dados.bin_intervalos(s.index, bin_valor, bin_unidade,
                                   ancora_hora, ancora_minuto, ancora_dia_semana)

    fig = go.Figure(go.Box(
        x=rotulos.values, y=s.values, marker_color=conf['cor'],
        line_color=conf['cor'], boxmean='sd', name=conf['nome'],
    ))
    fig.update_xaxes(title_text='período', type='date')
    fig.update_yaxes(title_text=conf['unidade'])
    cfg.aplicar_layout(fig, tema, f"{conf['nome']} — box plots por período")
    cfg.aplicar_grade(fig, tema)
    return fig, []


# ─────────────────────────────────────────────
# ANÁLISE
# ─────────────────────────────────────────────

def correlacao(df, tema: str = 'claro'):
    colunas = {k: c['coluna'] for k, c in cfg.TODOS_SENSORES.items()
               if c['coluna'] in df.columns}
    if len(colunas) < 2:
        return _vazia('São necessárias ao menos duas séries.', tema), []

    numerico = pd.DataFrame({k: dados.serie(df, k) for k in colunas}).dropna()
    if numerico.empty or numerico.shape[1] < 2:
        return _vazia('Não há sobreposição suficiente entre as séries.', tema), []

    matriz = numerico.corr()
    nomes = [cfg.TODOS_SENSORES[k]['nome'] for k in matriz.columns]

    fig = go.Figure(go.Heatmap(
        z=matriz.values, x=nomes, y=nomes, colorscale='RdBu', zmid=0,
        zmin=-1, zmax=1, text=matriz.round(2).values, texttemplate='%{text}',
        textfont=dict(size=11), colorbar=dict(title='r'),
    ))
    cfg.aplicar_layout(fig, tema, 'Correlação de Pearson')
    return fig, []


def anomalias(df, tema: str = 'claro'):
    chaves = [k for k in list(cfg.SENSORES_GAS) + list(cfg.SENSORES_CO2)
              if cfg.TODOS_SENSORES[k]['coluna'] in df.columns]
    if not chaves:
        return _vazia('Sem séries para analisar.', tema), []

    limpos = pd.DataFrame({k: dados.serie(df, k) for k in chaves})
    marcadas = dados.detectar_anomalias(limpos)

    fig = go.Figure()
    for chave in chaves:
        conf = cfg.TODOS_SENSORES[chave]
        s = limpos[chave]
        fig.add_trace(go.Scatter(
            x=s.index, y=s.where(~marcadas), mode='lines',
            line=dict(color=conf['cor'], width=2, shape='spline'),
            name=conf['nome'], connectgaps=True, meta=chave,
        ))
        anomalo = s.where(marcadas)
        if anomalo.notna().any():
            fig.add_trace(go.Scatter(
                x=anomalo.index, y=anomalo, mode='markers',
                marker=dict(size=9, symbol='x', color=cfg.TEMAS[tema]['contraste'],
                            line=dict(color=conf['cor'], width=1.5)),
                name=f'Anomalia — {conf["nome"]}', showlegend=False,
            ))

    cfg.aplicar_layout(fig, tema, f'Isolation Forest — {int(marcadas.sum())} pontos marcados')
    cfg.aplicar_grade(fig, tema)
    return fig, []


def dispersao(df, chave_x: str, chave_y: str, tema: str = 'claro'):
    """Dispersão entre duas séries com ajuste linear e R² — a regressão
    "clássica" entre variáveis, complementando as tendências no tempo."""
    conf_x = cfg.SERIES_REGRESSIVEIS.get(chave_x)
    conf_y = cfg.SERIES_REGRESSIVEIS.get(chave_y)
    if not conf_x or not conf_y:
        return _vazia('Escolha duas séries válidas.', tema), []

    par = pd.DataFrame({'x': dados.serie(df, chave_x),
                        'y': dados.serie(df, chave_y)}).dropna()
    if len(par) < 3:
        return _vazia('Sem pontos coincidentes entre as duas séries.', tema), []

    fig = go.Figure()
    fig.add_trace(go.Scatter(
        x=par['x'], y=par['y'], mode='markers',
        marker=dict(size=6, color=par.index.astype('int64'), colorscale='Viridis',
                    opacity=0.75, showscale=False),
        name='Leituras',
        hovertemplate=f"{conf_x['nome']}: %{{x:.2f}}<br>{conf_y['nome']}: %{{y:.2f}}<extra></extra>",
    ))

    a, b = np.polyfit(par['x'].values, par['y'].values, 1)
    grade = np.linspace(par['x'].min(), par['x'].max(), 100)
    r = float(par['x'].corr(par['y']))
    fig.add_trace(go.Scatter(
        x=grade, y=a * grade + b, mode='lines',
        line=dict(color='#7c8cff', width=2.5, dash='dash'),
        name=f'y = {a:.4g}·x + {b:.4g} · R² = {r**2:.3f}',
    ))

    fig.update_xaxes(title_text=f"{conf_x['nome']} ({conf_x['unidade']})")
    fig.update_yaxes(title_text=f"{conf_y['nome']} ({conf_y['unidade']})")
    cfg.aplicar_layout(fig, tema, f"{conf_y['nome']} × {conf_x['nome']}", hovermode='closest')
    cfg.aplicar_grade(fig, tema)
    return fig, []


# ─────────────────────────────────────────────
# DESPACHANTE
# ─────────────────────────────────────────────

def montar(nome: str, df, params: dict, tema: str = 'claro'):
    if nome == 'sensor':
        return sensor(df, params.get('chave'), params.get('vista', 'serie'), tema)
    if nome == 'combo':
        chaves = params.get('chaves') or []
        if isinstance(chaves, str):
            chaves = [c for c in chaves.split(',') if c]
        return combo(df, chaves, tema, params.get('secundario'), params.get('titulo'))
    if nome == 'visao_geral':
        return visao_geral(df, tema)
    if nome == 'iqa':
        return iqa(df, tema)
    if nome == 'subindices':
        return subindices(df, tema)
    if nome == 'correlacao':
        return correlacao(df, tema)
    if nome == 'anomalias':
        return anomalias(df, tema)
    if nome == 'dispersao':
        return dispersao(df, params.get('x', 'aht10_temp'), params.get('y', 'co2'), tema)
    if nome == 'boxplot':
        return box_intervalos(
            df, params.get('chave'),
            max(1, int(params.get('bin_valor', 1) or 1)),
            params.get('bin_unidade', 'horas'),
            int(params.get('ancora_hora', 0) or 0),
            int(params.get('ancora_minuto', 0) or 0),
            int(params.get('ancora_dia_semana', 0) or 0),
            params.get('filtro_dias', 'todos'),
            tema,
        )
    return _vazia(f"Figura '{nome}' não existe.", tema), []
