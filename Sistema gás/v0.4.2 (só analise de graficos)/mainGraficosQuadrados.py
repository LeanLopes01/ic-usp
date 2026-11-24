import pandas as pd
import numpy as np
import plotly.graph_objects as go
# make_subplots não é mais necessário para os comparativos, mas mantido se precisar futuramente
from plotly.subplots import make_subplots 
import dash
from dash import dcc, html, Input, Output, State
import dash_bootstrap_components as dbc
import os
import re
import threading
import sys

# --- CONFIGURAÇÕES GLOBAIS ---
GASES = ['A', 'E', 'P', 'AR']
CONFIG_IDS = [0, 1, 2, 3, 4]
REPETICOES = ['R1', 'R2', 'R3'] 
SENSORES = ['MQ2', 'MQ3', 'MQ5', 'TODOS']
CONCENTRACOES = [100, 200]

# Cores
CORES_PRIMARIAS = ['#0000FF', '#FF0000', '#008000', '#FFD700', '#800080'] 

# Traduções
TRANSLATIONS = {
    'PT': {
        'xaxis': '<b>Tempo (s)</b>',
        'yaxis_v': '<b>Tensão (V)</b>',
        'yaxis_rate': '<b>Taxa (V/s)</b>',
        'label_rep': 'Rep',
        'label_mean': 'Média',
        'config': 'Configuração',
        'sensor': 'Sensor',
        'gas': 'Gás',
        'download_table': 'Baixar Tabela (Excel)',
        'download_matrix': 'Baixar Matriz Completa',
        'process': 'Processar Gráficos',
        'configs_names': {0: 'Padrão', 1: 'V', 2: 'A', 3: 'X', 4: 'O'}
    },
    'EN': {
        'xaxis': '<b>Time (s)</b>',
        'yaxis_v': '<b>Voltage (V)</b>',
        'yaxis_rate': '<b>Rate (V/s)</b>',
        'label_rep': 'Rep',
        'label_mean': 'Average',
        'config': 'Configuration',
        'sensor': 'Sensor',
        'gas': 'Gas',
        'download_table': 'Download Table (Excel)',
        'download_matrix': 'Download Full Matrix',
        'process': 'Process Graphs',
        'configs_names': {0: 'Default', 1: 'V', 2: 'A', 3: 'X', 4: 'O'}
    }
}

class AnalisadorGases:
    def __init__(self, volume_recipiente=200, concentracao_padrao=100):
        self.volume_recipiente = volume_recipiente
        self.concentracao_padrao = concentracao_padrao
        
    def extrair_concentracao_do_nome(self, filename):
        try:
            padrao = r'-(\d+)\.csv$'
            match = re.search(padrao, filename)
            if match:
                return int(match.group(1))
            return self.concentracao_padrao
        except:
            return self.concentracao_padrao

    def converter_adc_para_tensao(self, valor_adc):
        return (valor_adc / 1023.0) * 5.0

    def carregar_arquivos_individuais(self, pasta_base, gas, config, concentracao_alvo):
        dfs_dict = {}
        concentracoes_encontradas = []
        pasta_config = os.path.join(pasta_base, str(config))
        
        if not os.path.exists(pasta_config):
            return None, None

        for rep in REPETICOES:
            prefixo = f"{gas}{config}-{rep}-"
            try:
                arquivos_candidatos = [f for f in os.listdir(pasta_config) 
                                     if f.startswith(prefixo) and f.endswith('.csv')]
                arquivo_valido = None
                for arq in arquivos_candidatos:
                    conc_arquivo = self.extrair_concentracao_do_nome(arq)
                    if conc_arquivo == concentracao_alvo:
                        arquivo_valido = arq
                        concentracoes_encontradas.append(conc_arquivo)
                        break 
                
                if arquivo_valido:
                    filepath = os.path.join(pasta_config, arquivo_valido)
                    df = pd.read_csv(filepath, delimiter=';')
                    mapeamento = {
                        'Tempo(s)': 'tempo', 'MQ2(A0)': 'MQ2', 'MQ3(A1)': 'MQ3',
                        'MQ5(A2)': 'MQ5', 'Temperatura(C)': 'temp',
                        'Umidade(%)': 'umidade', 'Pressao(hPa)': 'pressao'
                    }
                    df = df.rename(columns=mapeamento)
                    for col in df.columns:
                        df[col] = pd.to_numeric(df[col], errors='coerce')
                    df = df.dropna(how='all')
                    dfs_dict[rep] = df
            except Exception:
                continue

        if not dfs_dict:
            return None, None

        min_len = min(len(df) for df in dfs_dict.values())
        dfs_truncados = [df.head(min_len) for df in dfs_dict.values()]
        
        df_concat = pd.concat(dfs_truncados, keys=dfs_dict.keys())
        df_media = df_concat.groupby(level=1).mean(numeric_only=True)
        
        for sensor in ['MQ2', 'MQ3', 'MQ5']:
            if sensor in df_media.columns:
                df_media[f'{sensor}_V'] = self.converter_adc_para_tensao(df_media[sensor])
        
        for rep, df in dfs_dict.items():
            for sensor in ['MQ2', 'MQ3', 'MQ5']:
                if sensor in df.columns:
                    df[f'{sensor}_V'] = self.converter_adc_para_tensao(df[sensor])
            dfs_dict[rep] = df.head(min_len)

        return dfs_dict, df_media

    def suavizar_dados(self, df, janela=5):
        df_suave = df.copy()
        for col in df.columns:
            if col != 'tempo' and pd.api.types.is_numeric_dtype(df[col]):
                df_suave[col] = df[col].rolling(window=janela, center=True, min_periods=1).mean()
        return df_suave

    def get_vmax_relativo(self, df_media, sensor):
        col_v = f'{sensor}_V'
        if col_v not in df_media.columns: return None
        tempo = df_media['tempo'].values if 'tempo' in df_media.columns else df_media.index.values
        sinal = df_media[col_v].values
        
        mask_60 = tempo <= 60
        v_baseline = np.min(sinal[mask_60]) if np.any(mask_60) else np.min(sinal)
        
        mask_90s = tempo <= 90
        sinal_recortado = sinal[mask_90s] if np.any(mask_90s) else sinal
        v_max = np.max(sinal_recortado)
        return v_max - v_baseline

    def gerar_matriz_completa(self, pasta_base, concentracao_alvo, config_map):
        dados_matriz = []
        for config in CONFIG_IDS:
            for gas in GASES:
                _, df_media = self.carregar_arquivos_individuais(pasta_base, gas, config, concentracao_alvo)
                nome_config = config_map[config]
                if df_media is not None:
                    df_media = self.suavizar_dados(df_media, janela=5)
                    for sensor in ['MQ2', 'MQ3', 'MQ5']:
                        v_rel = self.get_vmax_relativo(df_media, sensor)
                        dados_matriz.append({
                            'Config': nome_config, 'Sensor': sensor, 'Gas': gas,
                            'Vmax_Relativo': v_rel if v_rel is not None else np.nan
                        })
                else:
                     for sensor in ['MQ2', 'MQ3', 'MQ5']:
                         dados_matriz.append({'Config': nome_config, 'Sensor': sensor, 'Gas': gas, 'Vmax_Relativo': np.nan})

        df = pd.DataFrame(dados_matriz)
        if not df.empty:
            df_pivot = df.pivot_table(index=['Config', 'Sensor'], columns='Gas', values='Vmax_Relativo')
            df_pivot = df_pivot.reset_index()
            return df_pivot
        return pd.DataFrame()

    def calcular_metricas_avancadas(self, df_media, dfs_individuais, sensor):
        col_v = f'{sensor}_V'
        if col_v not in df_media.columns: return {}
        
        tempo = df_media['tempo'].values if 'tempo' in df_media.columns else df_media.index.values
        sinal = df_media[col_v].values
        
        mask_60 = tempo <= 60
        v_baseline = np.min(sinal[mask_60]) if np.any(mask_60) else np.min(sinal)
        
        mask_90s = tempo <= 90
        sinal_recortado = sinal[mask_90s] if np.any(mask_90s) else sinal
        tempo_recortado = tempo[mask_90s] if np.any(mask_90s) else tempo
        
        v_max = np.max(sinal_recortado)
        diferencial = v_max - v_baseline
        
        gradiente = np.gradient(sinal_recortado, tempo_recortado)
        taxa_maxima = np.max(gradiente)

        return {
            'V_baseline (V)': v_baseline,
            'V_max Relativo (V)': diferencial,
            'Taxa Max (V/s)': taxa_maxima
        }

# App Setup
app = dash.Dash(__name__, external_stylesheets=[dbc.themes.BOOTSTRAP])

app.layout = dbc.Container([
    dbc.Row([
        dbc.Col([html.H2("Análise de Sensores de Gás", className="text-center my-3")]),
        dbc.Col([
            dbc.RadioItems(
                id="language-select",
                options=[{'label': 'Português', 'value': 'PT'}, {'label': 'English', 'value': 'EN'}],
                value='PT',
                inline=True,
                className="mt-4"
            )
        ], width=2)
    ]),
    
    dbc.Row([
        dbc.Col([
            dbc.Card([
                dbc.CardHeader("Controles"),
                dbc.CardBody([
                    dbc.Label("Pasta Base:"),
                    dbc.Input(id="pasta-base", value="./dados", type="text", className="mb-2"),
                    dbc.Label("Gás Principal:"),
                    dcc.Dropdown(id="gas-select", options=[{'label': g, 'value': g} for g in GASES], value='A', className="mb-2"),
                    dbc.Label("Configuração (Aleta):"),
                    dcc.Dropdown(id="config-select", value=0, className="mb-2"),
                    dbc.Label("Concentração (ppm):"),
                    dcc.Dropdown(id="conc-select", options=[{'label': f'{c} ppm', 'value': c} for c in CONCENTRACOES], value=100, className="mb-2"),
                    dbc.Label("Sensor:"),
                    dcc.Dropdown(id="sensor-select", options=[{'label': s, 'value': s} for s in SENSORES], value='MQ2', className="mb-2"),
                    dbc.Label("Volume (mL):"),
                    dbc.Input(id="volume", value=200, type="number", className="mb-2"),
                    html.Hr(),
                    dbc.Button("Processar", id="btn-processar", color="primary", className="w-100 mb-2"),
                    dbc.Button("Baixar Tabela", id="btn-download", color="success", className="w-100 mb-2"),
                    html.Hr(),
                    dbc.Button("Baixar Matriz", id="btn-download-matrix", color="warning", className="w-100"),
                    dcc.Download(id="download-file-1"),
                    dcc.Download(id="download-file-2")
                ])
            ])
        ], width=3),
        
        dbc.Col([
            dcc.Loading(children=[
                dbc.Tabs([
                    dbc.Tab(label="Linhas (Repetições)", children=[
                        dcc.Graph(id="grafico-repeticoes"),
                        html.Div(id="tabela-metricas-container", className="mt-3")
                    ]),
                    dbc.Tab(label="Box Plot", children=[
                        dbc.Row([dbc.Col(dcc.Graph(id="grafico-box-vmax"), width=6),
                                 dbc.Col(dcc.Graph(id="grafico-box-tempo"), width=6)])
                    ]),
                    # Alteração: Tabs agora contêm múltiplos gráficos
                    dbc.Tab(label="Comparativo Aletas", children=[
                        html.Div("MQ2 - Comparativo de Aletas", className="text-center fw-bold mt-2"),
                        dcc.Graph(id="comp-aleta-mq2"),
                        html.Div("MQ3 - Comparativo de Aletas", className="text-center fw-bold mt-2"),
                        dcc.Graph(id="comp-aleta-mq3"),
                        html.Div("MQ5 - Comparativo de Aletas", className="text-center fw-bold mt-2"),
                        dcc.Graph(id="comp-aleta-mq5"),
                    ]),
                    dbc.Tab(label="Comparação Gases", children=[
                        html.Div("MQ2 - Comparativo de Gases", className="text-center fw-bold mt-2"),
                        dcc.Graph(id="comp-gas-mq2"),
                        html.Div("MQ3 - Comparativo de Gases", className="text-center fw-bold mt-2"),
                        dcc.Graph(id="comp-gas-mq3"),
                        html.Div("MQ5 - Comparativo de Gases", className="text-center fw-bold mt-2"),
                        dcc.Graph(id="comp-gas-mq5"),
                    ]),
                    dbc.Tab(label="Ambiental", children=[dcc.Graph(id="grafico-ambiental-completo")]),
                    dbc.Tab(label="Derivadas", children=[dcc.Graph(id="grafico-derivadas-v")]),
                ])
            ])
        ], width=9)
    ]),
    dcc.Store(id='store-metricas')
], fluid=True)

# --- ESTILO: Quadrado e Limpo ---
def style_figure(fig, width=None, height=500, legend_inside=False):
    layout_args = dict(
        template="plotly_white",
        height=height,
        width=width,
        margin=dict(l=40, r=20, t=30, b=40)
    )
    
    if legend_inside:
        layout_args['legend'] = dict(
            yanchor="top", y=0.98,
            xanchor="left", x=0.02,
            bgcolor="rgba(255, 255, 255, 0.7)",
            bordercolor="Black", borderwidth=1
        )
    
    fig.update_layout(**layout_args)
    
    axis_style = dict(
        showgrid=False, showline=True, linewidth=2, linecolor='black',
        mirror=True, title_font=dict(size=16, color='black'),
        tickfont=dict(size=14, color='black', family='Arial Black')
    )
    fig.update_xaxes(**axis_style)
    fig.update_yaxes(**axis_style)
    return fig

@app.callback(
    Output("config-select", "options"),
    Input("language-select", "value")
)
def update_dropdown_options(lang):
    mapa = TRANSLATIONS[lang]['configs_names']
    return [{'label': mapa[c], 'value': c} for c in CONFIG_IDS]

@app.callback(
    [Output("grafico-repeticoes", "figure"),
     Output("grafico-ambiental-completo", "figure"),
     Output("grafico-derivadas-v", "figure"),
     # Saídas separadas para Gases
     Output("comp-gas-mq2", "figure"),
     Output("comp-gas-mq3", "figure"),
     Output("comp-gas-mq5", "figure"),
     # Saídas Box Plot
     Output("grafico-box-vmax", "figure"),
     Output("grafico-box-tempo", "figure"),
     # Saídas separadas para Aletas
     Output("comp-aleta-mq2", "figure"),
     Output("comp-aleta-mq3", "figure"),
     Output("comp-aleta-mq5", "figure"),
     Output("tabela-metricas-container", "children"),
     Output("store-metricas", "data"),
     Output("btn-processar", "children"),
     Output("btn-download", "children"),
     Output("btn-download-matrix", "children")],
    [Input("btn-processar", "n_clicks"), Input("language-select", "value")],
    [State("pasta-base", "value"), State("gas-select", "value"),
     State("config-select", "value"), State("conc-select", "value"),
     State("sensor-select", "value"), State("volume", "value")]
)
def update_graphs(n_clicks, lang, pasta_base, gas, config, conc, sensor_selecionado, volume):
    txt = TRANSLATIONS[lang]
    config_map = txt['configs_names']
    
    # Retorno Vazio padrão (agora com 16 itens)
    empty_res = (go.Figure(),)*11 + (html.Div(), None, txt['process'], txt['download_table'], txt['download_matrix'])

    if not n_clicks: return empty_res

    analisador = AnalisadorGases(volume_recipiente=volume)
    dfs_ind, df_media = analisador.carregar_arquivos_individuais(pasta_base, gas, config, conc)

    if df_media is None: return empty_res

    df_media_suave = analisador.suavizar_dados(df_media, janela=5)
    tempo = df_media_suave['tempo'].values if 'tempo' in df_media_suave.columns else df_media_suave.index.values
    sensores_loop = ['MQ2', 'MQ3', 'MQ5'] if sensor_selecionado == 'TODOS' else [sensor_selecionado]

    # --- 1. Gráfico Repetições (Média Pontilhada) ---
    fig_rep = go.Figure()
    for idx_s, s in enumerate(sensores_loop):
        col_v = f'{s}_V'
        # Linhas individuais
        if dfs_ind:
            for i, (rep, df_i) in enumerate(dfs_ind.items()):
                if col_v in df_i.columns:
                    nome_legenda = f"{rep}" if sensor_selecionado != 'TODOS' else f"{s} {rep}"
                    cor = CORES_PRIMARIAS[i % len(CORES_PRIMARIAS)]
                    fig_rep.add_trace(go.Scatter(
                        x=tempo, y=df_i[col_v], mode='lines',
                        name=nome_legenda, opacity=0.4, 
                        line=dict(width=1.5, color=cor)
                    ))
        # Média Pontilhada
        if col_v in df_media_suave.columns:
            nome_media = f"{txt['label_mean']} {s}"
            fig_rep.add_trace(go.Scatter(
                x=tempo, y=df_media_suave[col_v], mode='lines',
                name=nome_media, 
                line=dict(width=3, dash='dot', color='black') # <-- PONTILHADA
            ))

    fig_rep = style_figure(fig_rep, height=500, legend_inside=True)
    fig_rep.update_layout(xaxis_title=txt['xaxis'], yaxis_title=txt['yaxis_v'])

    # --- Métricas ---
    lista_metricas = []
    for s in sensores_loop:
        metricas = analisador.calcular_metricas_avancadas(df_media_suave, dfs_ind, s)
        if metricas:
            metricas[txt['sensor']] = s
            lista_metricas.append(metricas)
    tabela = dbc.Table.from_dataframe(pd.DataFrame(lista_metricas).round(4), striped=True, bordered=True) if lista_metricas else html.Div()

    # --- Box Plot (Melhorado) ---
    dados_box = {'Config': [], 'Sensor': [], 'Valor': [], 'Gas': []}
    for cfg_iter in CONFIG_IDS:
        for gas_iter in GASES:
            dfs_cfg, _ = analisador.carregar_arquivos_individuais(pasta_base, gas_iter, cfg_iter, conc)
            if dfs_cfg:
                for s_box in ['MQ2', 'MQ3', 'MQ5']:
                    for rep, df_i in dfs_cfg.items():
                        m_ind = analisador.calcular_metricas_avancadas(df_i, None, s_box)
                        if m_ind.get('V_max Relativo (V)'):
                            dados_box['Config'].append(config_map[cfg_iter])
                            dados_box['Sensor'].append(s_box)
                            dados_box['Valor'].append(m_ind['V_max Relativo (V)'])

    fig_box_vmax = go.Figure()
    # Adiciona traços agrupados por Sensor
    unique_sensors = ['MQ2', 'MQ3', 'MQ5']
    for i, s_box in enumerate(unique_sensors):
        indices = [k for k, x in enumerate(dados_box['Sensor']) if x == s_box]
        if indices:
            fig_box_vmax.add_trace(go.Box(
                x=[dados_box['Config'][k] for k in indices], 
                y=[dados_box['Valor'][k] for k in indices], 
                name=s_box, 
                marker_color=CORES_PRIMARIAS[i],
                boxpoints='all', # Mostra todos os pontos
                jitter=0.3,      # Espalha os pontos
                pointpos=-1.8    # Pontos ao lado da caixa
            ))
    
    fig_box_vmax = style_figure(fig_box_vmax, height=600)
    fig_box_vmax.update_layout(
        yaxis_title=txt['yaxis_v'],
        boxmode='group' # Garante agrupamento correto
    )
    fig_box_tempo = go.Figure()

    # --- Comparação Gases (Separados e Quadrados) ---
    figs_gases = []
    for sensor_loop in ['MQ2', 'MQ3', 'MQ5']:
        fig_g = go.Figure()
        for idx_g, gas_comp in enumerate(GASES):
            _, df_m_comp = analisador.carregar_arquivos_individuais(pasta_base, gas_comp, config, conc)
            if df_m_comp is not None:
                df_m_comp = analisador.suavizar_dados(df_m_comp)
                col = f'{sensor_loop}_V'
                if col in df_m_comp.columns:
                    t_comp = df_m_comp['tempo'].values if 'tempo' in df_m_comp.columns else df_m_comp.index.values
                    sinal = df_m_comp[col].values - np.min(df_m_comp[col].values)
                    fig_g.add_trace(go.Scatter(
                        x=t_comp, y=sinal, name=gas_comp, 
                        line=dict(color=CORES_PRIMARIAS[idx_g % len(CORES_PRIMARIAS)])
                    ))
        # Quadrado: Width = Height (ex: 500x500)
        fig_g = style_figure(fig_g, height=500, width=500, legend_inside=True)
        fig_g.update_layout(title=f"{sensor_loop} - Gases")
        figs_gases.append(fig_g)

    # --- Comparação Aletas (Separados e Quadrados) ---
    figs_aletas = []
    for sensor_loop in ['MQ2', 'MQ3', 'MQ5']:
        fig_a = go.Figure()
        for idx_c, cfg_iter in enumerate(CONFIG_IDS):
            _, df_m_aleta = analisador.carregar_arquivos_individuais(pasta_base, gas, cfg_iter, conc)
            if df_m_aleta is not None:
                df_m_aleta = analisador.suavizar_dados(df_m_aleta)
                col = f'{sensor_loop}_V'
                if col in df_m_aleta.columns:
                    t_aleta = df_m_aleta['tempo'].values if 'tempo' in df_m_aleta.columns else df_m_aleta.index.values
                    sinal = df_m_aleta[col].values - np.min(df_m_aleta[col].values)
                    fig_a.add_trace(go.Scatter(
                        x=t_aleta, y=sinal, name=config_map[cfg_iter],
                        line=dict(color=CORES_PRIMARIAS[idx_c % len(CORES_PRIMARIAS)])
                    ))
        # Quadrado: Width = Height
        fig_a = style_figure(fig_a, height=500, width=500, legend_inside=True)
        fig_a.update_layout(title=f"{sensor_loop} - Configs")
        figs_aletas.append(fig_a)

    fig_amb = style_figure(go.Figure(), height=500) 
    fig_deriv = style_figure(go.Figure(), height=500)

    # Ordenar outputs conforme a lista do @app.callback
    return (
        fig_rep, 
        fig_amb, 
        fig_deriv, 
        figs_gases[0], figs_gases[1], figs_gases[2], # Gases separado
        fig_box_vmax, 
        fig_box_tempo, 
        figs_aletas[0], figs_aletas[1], figs_aletas[2], # Aletas separado
        tabela, 
        lista_metricas, 
        txt['process'], 
        txt['download_table'], 
        txt['download_matrix']
    )

# --- Callbacks de Download ---
@app.callback(
    Output("download-file-1", "data"),
    Input("btn-download", "n_clicks"),
    State("store-metricas", "data"),
    prevent_initial_call=True,
)
def download_excel_metrics(n_clicks, data):
    if not data: return dash.no_update
    return dcc.send_data_frame(pd.DataFrame(data).to_excel, "metricas.xlsx", index=False)

@app.callback(
    Output("download-file-2", "data"),
    Input("btn-download-matrix", "n_clicks"),
    State("pasta-base", "value"), State("volume", "value"), State("conc-select", "value"), Input("language-select", "value"),
    prevent_initial_call=True,
)
def download_matrix_excel(n_clicks, pasta_base, volume, conc, lang):
    analisador = AnalisadorGases(volume)
    df = analisador.gerar_matriz_completa(pasta_base, conc, TRANSLATIONS[lang]['configs_names'])
    return dcc.send_data_frame(df.to_excel, "matriz.xlsx", index=False)

def run_server():
    app.run(debug=True, port=8050, use_reloader=False)

if __name__ == '__main__':
    if not os.path.exists('dados'): os.makedirs('dados', exist_ok=True)
    server_thread = threading.Thread(target=run_server)
    server_thread.daemon = True
    server_thread.start()
    
    print("-" * 50)
    print("SERVIDOR RODANDO EM http://127.0.0.1:8050/")
    print("Digite 'q' e pressione ENTER para sair.")
    print("-" * 50)
    
    while True:
        try:
            if input().lower() == 'q':
                print("Fechando...")
                os._exit(0)
        except KeyboardInterrupt:
            os._exit(0)