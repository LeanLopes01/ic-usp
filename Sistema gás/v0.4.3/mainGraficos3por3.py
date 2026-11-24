import pandas as pd
import numpy as np
import plotly.graph_objects as go
from plotly.subplots import make_subplots 
import dash
from dash import dcc, html, Input, Output, State
import dash_bootstrap_components as dbc
import os
import re

# --- CONFIGURAÇÃO ---
GAS_INFO = {
    'A': 'Acetone',
    'E': 'Ethyl Alcohol', 
    'P': 'Isopropyl Alcohol'
}
GAS_CODES = list(GAS_INFO.keys())

SENSORES = ['MQ2', 'MQ3', 'MQ5']
REPETICOES = ['R1', 'R2', 'R3']
CONFIG_NAMES = {0: 'Standard', 1: 'Config V', 2: 'Config A', 3: 'Config X', 4: 'Config O'}
CONCENTRACOES = [100, 200]

# Cores Neon para Repetições (Abas 1-5)
CORES_NEON = {
    'R1': '#FF00FF',
    'R2': '#1F51FF',
    'R3': '#00D400'
}

# Cores para as Aletas (Aba 6 e 7)
CORES_CONFIG = {
    0: 'black',      # Standard
    1: '#E63946',    # Vermelho
    2: '#457B9D',    # Azul Médio
    3: '#2A9D8F',    # Verde Azulado
    4: '#F4A261'     # Laranja
}

class AnalisadorGases:
    def __init__(self):
        pass

    def extrair_concentracao(self, filename):
        match = re.search(r'-(\d+)\.csv$', filename)
        return int(match.group(1)) if match else 100

    def converter_adc_para_tensao(self, valor_adc):
        return (valor_adc / 1023.0) * 5.0

    def carregar_dados(self, pasta_base, gas, config, concentracao_alvo):
        dfs_dict = {}
        pasta_config = os.path.join(pasta_base, str(config))
        
        if not os.path.exists(pasta_config): return None, None

        for rep in REPETICOES:
            prefixo = f"{gas}{config}-{rep}-"
            try:
                arquivos = [f for f in os.listdir(pasta_config) if f.startswith(prefixo) and f.endswith('.csv')]
                arquivo_valido = next((f for f in arquivos if self.extrair_concentracao(f) == concentracao_alvo), None)
                
                if arquivo_valido:
                    df = pd.read_csv(os.path.join(pasta_config, arquivo_valido), delimiter=';')
                    
                    mapeamento = {
                        'MQ2(A0)': 'MQ2', 'MQ3(A1)': 'MQ3', 'MQ5(A2)': 'MQ5', 
                        'Tempo(s)': 'tempo',
                        'Temperatura(C)': 'Temperatura',
                        'Umidade(%)': 'Umidade',
                        'Pressao(hPa)': 'Pressao'
                    }
                    
                    df = df.rename(columns={k: v for k, v in mapeamento.items() if k in df.columns})
                    df.columns = [c.strip() for c in df.columns]

                    for col in ['MQ2', 'MQ3', 'MQ5']:
                        if col in df.columns:
                            df[col] = pd.to_numeric(df[col], errors='coerce')
                            df[f'{col}_V'] = self.converter_adc_para_tensao(df[col])
                    
                    for col_amb in ['Pressao', 'Temperatura', 'Umidade']:
                        if col_amb in df.columns:
                            df[col_amb] = pd.to_numeric(df[col_amb], errors='coerce')

                    dfs_dict[rep] = df.dropna(how='all')
            except: continue

        if not dfs_dict: return None, None

        min_len = min(len(df) for df in dfs_dict.values())
        dfs_truncados = [df.head(min_len) for df in dfs_dict.values()]
        df_concat = pd.concat(dfs_truncados)
        df_media = df_concat.groupby(df_concat.index).mean(numeric_only=True)
        
        for col in ['MQ2', 'MQ3', 'MQ5']:
             if col in df_media.columns and f'{col}_V' not in df_media.columns:
                 df_media[f'{col}_V'] = self.converter_adc_para_tensao(df_media[col])

        return dfs_dict, df_media

    def suavizar(self, df, janela=5):
        return df.rolling(window=janela, center=True, min_periods=1).mean()

# --- DASH APP ---
app = dash.Dash(__name__, external_stylesheets=[dbc.themes.JOURNAL])

app.layout = dbc.Container([
    dbc.Row([
        dbc.Col([
            html.H5("Dashboard Analítico", className="text-center my-3"),
            dbc.Card([
                dbc.CardBody([
                    dbc.Label("Pasta de Dados:"), dbc.Input(id="pasta", value="./dados", className="mb-2"),
                    dbc.Label("Configuração (Aleta):"), 
                    dcc.Dropdown(id="config", options=[{'label': v, 'value': k} for k, v in CONFIG_NAMES.items()], value=0, className="mb-2"),
                    dbc.Label("Concentração (ppm):"), 
                    dcc.Dropdown(id="conc", options=[{'label': f'{c} ppm', 'value': c} for c in CONCENTRACOES], value=100, className="mb-2"),
                    dbc.Label("Suavização (Janela):"), dbc.Input(id="janela", value=5, type="number", className="mb-2"),
                    dbc.Button("Gerar Gráficos", id="btn-gerar", color="primary", className="w-100 mt-2")
                ])
            ])
        ], width=3),
        
        dbc.Col([
            dcc.Tabs(id="tabs-graficos", value='tab-voltage', children=[
                dcc.Tab(label='1. Tensão Absoluta', value='tab-voltage', children=[
                    dcc.Loading(dcc.Graph(id="grafico-voltage", style={"height": "850px"}))
                ]),
                dcc.Tab(label='2. Tensão Relativa (ΔV)', value='tab-relative', children=[
                    dcc.Loading(dcc.Graph(id="grafico-relative", style={"height": "850px"}))
                ]),
                dcc.Tab(label='3. Pressão', value='tab-pressao', children=[
                    dcc.Loading(dcc.Graph(id="grafico-pressao", style={"height": "850px"}))
                ]),
                dcc.Tab(label='4. Temperatura', value='tab-temp', children=[
                    dcc.Loading(dcc.Graph(id="grafico-temp", style={"height": "850px"}))
                ]),
                dcc.Tab(label='5. Umidade', value='tab-umidade', children=[
                    dcc.Loading(dcc.Graph(id="grafico-umidade", style={"height": "850px"}))
                ]),
                 dcc.Tab(label='6. Comparativo de Aletas', value='tab-aletas', children=[
                    dcc.Loading(dcc.Graph(id="grafico-aletas", style={"height": "850px"}))
                ]),
                dcc.Tab(label='7. Média Individual (Aleta Selecionada)', value='tab-media-sel', children=[
                    dcc.Loading(dcc.Graph(id="grafico-media-sel", style={"height": "850px"}))
                ]),
            ], className="mt-3")
        ], width=9)
    ])
], fluid=True)

# --- FUNÇÕES DE PLOTAGEM ---

def criar_figura_simples(analisador, pasta, config, conc, janela, modo_relativo=False):
    colunas_titulos = [f"<b>{s}</b>" for s in SENSORES]
    
    fig = make_subplots(
        rows=3, cols=3, shared_xaxes=True, horizontal_spacing=0.07, vertical_spacing=0.08,
        column_titles=colunas_titulos, 
        row_titles=[f"<b>{GAS_INFO[g]}</b>" for g in GAS_CODES]
    )
    tem_dados = False
    for i, gas in enumerate(GAS_CODES):
        row = i+1
        res = analisador.carregar_dados(pasta, gas, config, conc)
        if isinstance(res, tuple): dfs, dfm = res
        else: continue
        if dfm is None: continue
        tem_dados = True
        dfm = analisador.suavizar(dfm, janela)
        t_m = dfm['tempo'] if 'tempo' in dfm.columns else np.arange(len(dfm))
        for j, sens in enumerate(SENSORES):
            col = j+1
            c_v = f'{sens}_V'
            if dfs:
                for r in REPETICOES:
                    if r in dfs:
                        dfr = analisador.suavizar(dfs[r], janela)
                        tr = dfr['tempo'] if 'tempo' in dfr.columns else np.arange(len(dfr))
                        if c_v in dfr: 
                            y_val = dfr[c_v].values
                            if modo_relativo and len(y_val) > 0: y_val = y_val - np.min(y_val)
                            fig.add_trace(go.Scatter(x=tr, y=y_val, mode='lines', line=dict(color=CORES_NEON[r], width=2), showlegend=(row==1 and col==1), name=r), row=row, col=col)
            if c_v in dfm: 
                y_val_m = dfm[c_v].values
                if modo_relativo and len(y_val_m) > 0: y_val_m = y_val_m - np.min(y_val_m)
                fig.add_trace(go.Scatter(x=t_m, y=y_val_m, mode='lines', line=dict(color='black', width=2.5, dash='dot'), showlegend=(row==1 and col==1), name="Mean"), row=row, col=col)
            fig.update_xaxes(showline=True, linewidth=1.5, linecolor='black', mirror=True, row=row, col=col)
            fig.update_yaxes(showline=True, linewidth=1.5, linecolor='black', mirror=True, row=row, col=col)

    fig.update_layout(template="simple_white", font=dict(family="Times New Roman", size=12, color="black"), margin=dict(l=100, r=40, t=50, b=80), legend=dict(orientation="h", y=1.05, x=0.5), plot_bgcolor='rgba(0,0,0,0)')
    if not tem_dados: fig.update_layout(title="Sem dados")
    
    titulo_y = "<b>Relative Voltage (ΔV)</b>" if modo_relativo else "<b>Voltage (V)</b>"
    fig.add_annotation(x=-0.07, y=0.5, text=titulo_y, textangle=-90, xref="paper", yref="paper", showarrow=False, font=dict(size=14))
    fig.add_annotation(x=0.5, y=-0.07, text="<b>Time (s)</b>", xref="paper", yref="paper", showarrow=False, font=dict(size=14))
    return fig

def criar_figura_hibrida(analisador, pasta, config, conc, janela, var_ambiental, titulo_eixo_esq):
    colunas_titulos = [f"<b>{s}</b>" for s in SENSORES]
    fig = make_subplots(rows=3, cols=3, shared_xaxes=True, horizontal_spacing=0.12, vertical_spacing=0.08, column_titles=colunas_titulos, row_titles=[f"<b>{GAS_INFO[g]}</b>" for g in GAS_CODES], specs=[[{"secondary_y": True}] * 3] * 3)
    tem_dados = False
    for i, gas_code in enumerate(GAS_CODES):
        row = i + 1
        res = analisador.carregar_dados(pasta, gas_code, config, conc)
        if isinstance(res, tuple): dfs_reps, df_media = res
        else: continue
        if df_media is None: continue
        tem_dados = True
        df_media = analisador.suavizar(df_media, janela)
        tempo_media = df_media['tempo'] if 'tempo' in df_media.columns else np.arange(len(df_media))
        for j, sensor in enumerate(SENSORES):
            col = j + 1
            col_v = f'{sensor}_V'
            if dfs_reps:
                for rep in REPETICOES:
                    if rep in dfs_reps:
                        df_r = dfs_reps[rep]
                        df_r = analisador.suavizar(df_r, janela)
                        tempo_r = df_r['tempo'] if 'tempo' in df_r.columns else np.arange(len(df_r))
                        if var_ambiental in df_r.columns: fig.add_trace(go.Bar(x=tempo_r, y=df_r[var_ambiental], marker_color=CORES_NEON[rep], opacity=0.25, showlegend=False), row=row, col=col, secondary_y=False)
                        if col_v in df_r.columns: fig.add_trace(go.Scatter(x=tempo_r, y=df_r[col_v], mode='lines', line=dict(color=CORES_NEON[rep], width=2.0), showlegend=(row==1 and col==1), name=rep), row=row, col=col, secondary_y=True)
            if var_ambiental in df_media.columns: fig.add_trace(go.Scatter(x=tempo_media, y=df_media[var_ambiental], mode='lines', line=dict(color='gray', width=1.5, dash='dot'), showlegend=False), row=row, col=col, secondary_y=False)
            if col_v in df_media.columns: fig.add_trace(go.Scatter(x=tempo_media, y=df_media[col_v], mode='lines', name="Mean", line=dict(color='black', width=2.5, dash='dot'), showlegend=(row==1 and col==1)), row=row, col=col, secondary_y=True)
            fig.update_xaxes(showline=True, linewidth=1.5, linecolor='black', mirror=True, row=row, col=col)
            fig.update_yaxes(showline=True, linewidth=1.5, linecolor='black', row=row, col=col, secondary_y=False)
            fig.update_yaxes(showline=True, linewidth=1.5, linecolor='black', row=row, col=col, secondary_y=True)
            if col != 1: fig.update_yaxes(showticklabels=False, row=row, col=col, secondary_y=False)
            if col != 3: fig.update_yaxes(showticklabels=False, row=row, col=col, secondary_y=True)
    fig.update_layout(template="simple_white", font=dict(family="Times New Roman", size=12, color="black"), margin=dict(l=120, r=80, t=50, b=80), legend=dict(orientation="h", y=1.05, x=0.5), plot_bgcolor='rgba(0,0,0,0)', barmode='overlay')
    if not tem_dados: fig.update_layout(title="Sem dados")
    fig.add_annotation(x=-0.08, y=0.5, text=f"<b>{titulo_eixo_esq}</b>", textangle=-90, xref="paper", yref="paper", showarrow=False, font=dict(size=14, color='gray'))
    fig.add_annotation(x=1.07, y=0.5, text="<b>Voltage (V)</b>", textangle=-90, xref="paper", yref="paper", showarrow=False, font=dict(size=14))
    fig.add_annotation(x=0.5, y=-0.07, text="<b>Time (s)</b>", xref="paper", yref="paper", showarrow=False, font=dict(size=14))
    return fig

def criar_grafico_comparativo_aletas(analisador, pasta, conc, janela):
    colunas_titulos = [f"<b>{s}</b>" for s in SENSORES]
    fig = make_subplots(rows=3, cols=3, shared_xaxes=True, horizontal_spacing=0.07, vertical_spacing=0.08, column_titles=colunas_titulos, row_titles=[f"<b>{GAS_INFO[g]}</b>" for g in GAS_CODES])
    tem_dados = False
    for i, gas in enumerate(GAS_CODES):
        row = i + 1
        for config_id, config_nome in CONFIG_NAMES.items():
            res = analisador.carregar_dados(pasta, gas, config_id, conc)
            if isinstance(res, tuple): _, df_media = res
            else: continue
            if df_media is None: continue
            tem_dados = True
            df_media = analisador.suavizar(df_media, janela)
            tempo = df_media['tempo'] if 'tempo' in df_media.columns else np.arange(len(df_media))
            for j, sensor in enumerate(SENSORES):
                col = j + 1
                col_v = f'{sensor}_V'
                if col_v in df_media.columns:
                    y_val = df_media[col_v].values
                    if len(y_val) > 0: y_val = y_val - np.min(y_val)
                    fig.add_trace(go.Scatter(x=tempo, y=y_val, mode='lines', name=config_nome, line=dict(color=CORES_CONFIG.get(config_id, 'gray'), width=2), showlegend=(row==1 and col==1)), row=row, col=col)
                fig.update_xaxes(showline=True, linewidth=1.5, linecolor='black', mirror=True, row=row, col=col)
                fig.update_yaxes(showline=True, linewidth=1.5, linecolor='black', mirror=True, row=row, col=col)

    # --- AJUSTE DE LAYOUT AQUI ---
    fig.update_layout(
        template="simple_white", 
        font=dict(family="Times New Roman", size=12, color="black"), 
        # Aumentei a margem superior (t=100)
        margin=dict(l=100, r=40, t=50, b=80), 
        # Subi a legenda (y=1.15)
        legend=dict(orientation="h", y=1.095, x=0.5, xanchor="center"), 
        plot_bgcolor='rgba(0,0,0,0)'
    )
    
    if not tem_dados: fig.update_layout(title="Sem dados nas configurações")
    fig.add_annotation(x=-0.07, y=0.5, text="<b>Relative Mean Voltage (ΔV)</b>", textangle=-90, xref="paper", yref="paper", showarrow=False, font=dict(size=14))
    fig.add_annotation(x=0.5, y=-0.07, text="<b>Time (s)</b>", xref="paper", yref="paper", showarrow=False, font=dict(size=14))
    return fig

def criar_grafico_media_individual(analisador, pasta, config, conc, janela):
    colunas_titulos = [f"<b>{s}</b>" for s in SENSORES]
    
    fig = make_subplots(
        rows=3, cols=3, shared_xaxes=True, horizontal_spacing=0.07, vertical_spacing=0.08,
        column_titles=colunas_titulos, 
        row_titles=[f"<b>{GAS_INFO[g]}</b>" for g in GAS_CODES]
    )

    tem_dados = False
    cor_linha = CORES_CONFIG.get(config, 'black')
    nome_config = CONFIG_NAMES.get(config, 'Selected Config')

    for i, gas in enumerate(GAS_CODES):
        row = i+1
        res = analisador.carregar_dados(pasta, gas, config, conc)
        if isinstance(res, tuple): _, df_media = res
        else: continue
        if df_media is None: continue
        tem_dados = True

        df_media = analisador.suavizar(df_media, janela)
        tempo = df_media['tempo'] if 'tempo' in df_media.columns else np.arange(len(df_media))

        for j, sens in enumerate(SENSORES):
            col = j+1
            c_v = f'{sens}_V'
            if c_v in df_media.columns:
                y_val = df_media[c_v].values
                if len(y_val) > 0:
                    y_val = y_val - np.min(y_val)

                fig.add_trace(
                    go.Scatter(
                        x=tempo, y=y_val, 
                        mode='lines', 
                        line=dict(color=cor_linha, width=2.5), 
                        showlegend=(row==1 and col==1), 
                        name=f"{nome_config} (Mean)"
                    ), 
                    row=row, col=col
                )
            fig.update_xaxes(showline=True, linewidth=1.5, linecolor='black', mirror=True, row=row, col=col)
            fig.update_yaxes(showline=True, linewidth=1.5, linecolor='black', mirror=True, row=row, col=col)

    fig.update_layout(
        template="simple_white",
        font=dict(family="Times New Roman", size=12, color="black"),
        margin=dict(l=100, r=40, t=50, b=80),
        legend=dict(orientation="h", y=1.05, x=0.5),
        plot_bgcolor='rgba(0,0,0,0)'
    )
    
    if not tem_dados: fig.update_layout(title="Sem dados para esta configuração")
    
    fig.add_annotation(x=-0.07, y=0.5, text="<b>Relative Mean Voltage (ΔV)</b>", textangle=-90, xref="paper", yref="paper", showarrow=False, font=dict(size=14))
    fig.add_annotation(x=0.5, y=-0.07, text="<b>Time (s)</b>", xref="paper", yref="paper", showarrow=False, font=dict(size=14))
    return fig

@app.callback(
    [Output("grafico-voltage", "figure"),
     Output("grafico-relative", "figure"),
     Output("grafico-pressao", "figure"),
     Output("grafico-temp", "figure"),
     Output("grafico-umidade", "figure"),
     Output("grafico-aletas", "figure"),
     Output("grafico-media-sel", "figure")],
    Input("btn-gerar", "n_clicks"),
    [State("pasta", "value"), State("config", "value"), State("conc", "value"), State("janela", "value")]
)
def atualizar_todos_graficos(n_clicks, pasta, config, conc, janela):
    if not n_clicks: 
        return go.Figure(), go.Figure(), go.Figure(), go.Figure(), go.Figure(), go.Figure(), go.Figure()
    
    analisador = AnalisadorGases()
    
    fig_v = criar_figura_simples(analisador, pasta, config, conc, janela, modo_relativo=False)
    fig_rel = criar_figura_simples(analisador, pasta, config, conc, janela, modo_relativo=True)
    fig_p = criar_figura_hibrida(analisador, pasta, config, conc, janela, 'Pressao', 'Pressure (hPa)')
    fig_t = criar_figura_hibrida(analisador, pasta, config, conc, janela, 'Temperatura', 'Temp (°C)')
    fig_h = criar_figura_hibrida(analisador, pasta, config, conc, janela, 'Umidade', 'Humidity (%)')
    fig_aletas = criar_grafico_comparativo_aletas(analisador, pasta, conc, janela)
    fig_media_sel = criar_grafico_media_individual(analisador, pasta, config, conc, janela)

    return fig_v, fig_rel, fig_p, fig_t, fig_h, fig_aletas, fig_media_sel

if __name__ == '__main__':
    if not os.path.exists('dados/0'): os.makedirs('dados/0', exist_ok=True)
    app.run(debug=True, port=8055)