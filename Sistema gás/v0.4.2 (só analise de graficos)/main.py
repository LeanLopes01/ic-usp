import pandas as pd
import numpy as np
import plotly.graph_objects as go
from plotly.subplots import make_subplots
import dash
from dash import dcc, html, Input, Output, State
import dash_bootstrap_components as dbc
import os
import re

# --- CONFIGURAÇÕES GLOBAIS ---
GASES = ['A', 'E', 'P', 'AR']
# Mapeamento visual das configs (Internamente continua 0, 1, 2...)
CONFIG_MAP = {0: 'Padrão', 1: 'V', 2: 'A', 3: 'X', 4: 'O'}
CONFIGS = list(CONFIG_MAP.keys())
REPETICOES = ['R1', 'R2', 'R3'] 
SENSORES = ['MQ2', 'MQ3', 'MQ5', 'TODOS']
CONCENTRACOES = [100, 200]

# Cores "Primárias" e Fortes para os gráficos
CORES_PRIMARIAS = ['#0000FF', '#FF0000', '#008000', '#FFD700', '#000000'] # Azul, Vermelho, Verde, Amarelo, Preto

# Dicionário de Traduções
TRANSLATIONS = {
    'PT': {
        'xaxis': 'Tempo (s)',
        'yaxis_v': 'Tensão (V)',
        'yaxis_rate': 'Taxa (V/s)',
        'label_rep': 'Rep',
        'label_mean': 'Média',
        'config': 'Configuração',
        'sensor': 'Sensor',
        'gas': 'Gás',
        'download_table': 'Baixar Tabela (Excel)',
        'download_matrix': 'Baixar Matriz Completa',
        'process': 'Processar Gráficos'
    },
    'EN': {
        'xaxis': 'Time (s)',
        'yaxis_v': 'Voltage (V)',
        'yaxis_rate': 'Rate (V/s)',
        'label_rep': 'Rep',
        'label_mean': 'Average',
        'config': 'Configuration',
        'sensor': 'Sensor',
        'gas': 'Gas',
        'download_table': 'Download Table (Excel)',
        'download_matrix': 'Download Full Matrix',
        'process': 'Process Graphs'
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
        
        # Usa o ID numérico da config para achar a pasta
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
            except Exception as e:
                continue

        if not dfs_dict:
            return None, None

        if concentracoes_encontradas:
            self.concentracao_padrao = np.mean(concentracoes_encontradas)

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
        if col_v not in df_media.columns:
            return None

        tempo = df_media['tempo'].values if 'tempo' in df_media.columns else df_media.index.values
        sinal = df_media[col_v].values
        
        mask_60 = tempo <= 60
        if np.any(mask_60):
            v_baseline = np.min(sinal[mask_60])
        else:
            v_baseline = np.min(sinal)
        
        mask_90s = tempo <= 90
        if np.any(mask_90s):
            sinal_recortado = sinal[mask_90s]
        else:
            sinal_recortado = sinal
        
        v_max = np.max(sinal_recortado)
        return v_max - v_baseline

    def gerar_matriz_completa(self, pasta_base, concentracao_alvo):
        dados_matriz = []
        for config in CONFIGS:
            for gas in GASES:
                _, df_media = self.carregar_arquivos_individuais(pasta_base, gas, config, concentracao_alvo)
                if df_media is not None:
                    df_media = self.suavizar_dados(df_media, janela=5)
                    for sensor in ['MQ2', 'MQ3', 'MQ5']:
                        v_rel = self.get_vmax_relativo(df_media, sensor)
                        dados_matriz.append({
                            'Config': CONFIG_MAP[config], # Usa o nome mapeado
                            'Sensor': sensor,
                            'Gas': gas,
                            'Vmax_Relativo': v_rel if v_rel is not None else np.nan
                        })
                else:
                     for sensor in ['MQ2', 'MQ3', 'MQ5']:
                         dados_matriz.append({'Config': CONFIG_MAP[config], 'Sensor': sensor, 'Gas': gas, 'Vmax_Relativo': np.nan})

        df = pd.DataFrame(dados_matriz)
        if not df.empty:
            df_pivot = df.pivot_table(index=['Config', 'Sensor'], columns='Gas', values='Vmax_Relativo')
            df_pivot = df_pivot.reset_index()
            return df_pivot
        return pd.DataFrame()

    def calcular_metricas_avancadas(self, df_media, dfs_individuais, sensor):
        col_v = f'{sensor}_V'
        if col_v not in df_media.columns:
            return {}

        tempo = df_media['tempo'].values if 'tempo' in df_media.columns else df_media.index.values
        sinal = df_media[col_v].values
        
        mask_60 = tempo <= 60
        v_baseline = np.min(sinal[mask_60]) if np.any(mask_60) else np.min(sinal)
        
        mask_90s = tempo <= 90
        if np.any(mask_90s):
            sinal_recortado = sinal[mask_90s]
            tempo_recortado = tempo[mask_90s]
        else:
            sinal_recortado = sinal
            tempo_recortado = tempo
        
        v_max = np.max(sinal_recortado)
        idx_max_relativo = np.argmax(sinal_recortado)
        idx_max = np.where(tempo == tempo_recortado[idx_max_relativo])[0][0]
        t_max = tempo[idx_max]

        diferencial = v_max - v_baseline
        limiar_10 = v_baseline + 0.10 * diferencial
        limiar_90 = v_baseline + 0.90 * diferencial
        
        tempo_subida = None
        try:
            sinal_ate_pico = sinal[:idx_max+1]
            tempo_ate_pico = tempo[:idx_max+1]
            idx_10 = np.where(sinal_ate_pico >= limiar_10)[0][0]
            idx_90 = np.where(sinal_ate_pico >= limiar_90)[0][0]
            tempo_subida = tempo_ate_pico[idx_90] - tempo_ate_pico[idx_10]
        except:
            pass

        tempo_descida = None
        try:
            idx_corte_final = int(len(sinal) * 0.85)
            if idx_corte_final > idx_max:
                sinal_pos_pico = sinal[idx_max:idx_corte_final]
                tempo_pos_pico = tempo[idx_max:idx_corte_final]
                indices_recup_10 = np.where(sinal_pos_pico <= limiar_10)[0]
                indices_recup_90 = np.where(sinal_pos_pico <= limiar_90)[0]
                if len(indices_recup_10) > 0 and len(indices_recup_90) > 0:
                    tempo_descida = tempo_pos_pico[indices_recup_10[0]] - tempo_pos_pico[indices_recup_90[0]]
        except:
            pass

        tempo_estabilizacao = None
        faixa_estab = 0.05 * diferencial
        limite_inf = v_max - faixa_estab
        try:
            sinal_ate_pico = sinal[:idx_max]
            fora_faixa = np.where(sinal_ate_pico < limite_inf)[0]
            if len(fora_faixa) > 0:
                idx_estab = fora_faixa[-1] + 1
                tempo_estabilizacao = tempo[idx_estab] - tempo[0]
        except:
            pass

        erro_multiplicativo = None
        if dfs_individuais:
            valores_no_pico = []
            for df_ind in dfs_individuais.values():
                if col_v in df_ind.columns and len(df_ind) > idx_max:
                    valores_no_pico.append(df_ind[col_v].iloc[idx_max])
            if len(valores_no_pico) > 1:
                media_pico = np.mean(valores_no_pico)
                if media_pico != 0:
                    erro_multiplicativo = (np.std(valores_no_pico, ddof=1) / media_pico) * 100

        gradiente = np.gradient(sinal_recortado, tempo_recortado)
        taxa_maxima = np.max(gradiente)

        return {
            'V_baseline (V)': v_baseline,
            'V_max (90s) (V)': v_max,
            'V_max Relativo (V)': diferencial,
            'Tempo V_max (s)': t_max,
            'Taxa Max (V/s)': taxa_maxima,
            'Tempo Subida (s)': tempo_subida,
            'Tempo Descida (s)': tempo_descida,
            'Tempo Estabilização (s)': tempo_estabilizacao,
            'Erro Multiplicativo (%)': erro_multiplicativo
        }

# App Setup
app = dash.Dash(__name__, external_stylesheets=[dbc.themes.BOOTSTRAP])

app.layout = dbc.Container([
    dbc.Row([
        dbc.Col([html.H2("Análise de Sensores de Gás", className="text-center my-3")]),
        # Seletor de Idioma no topo
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
        # Painel Lateral
        dbc.Col([
            dbc.Card([
                dbc.CardHeader("Controles"),
                dbc.CardBody([
                    dbc.Label("Pasta Base:"),
                    dbc.Input(id="pasta-base", value="./dados", type="text", className="mb-2"),
                    
                    dbc.Label("Gás Principal:"),
                    dcc.Dropdown(
                        id="gas-select", options=[{'label': g, 'value': g} for g in GASES],
                        value='A', className="mb-2"
                    ),
                    
                    dbc.Label("Configuração (Aleta):"),
                    dcc.Dropdown(
                        id="config-select", 
                        # Opções agora mostram Padrão, V, A, X, O mas mandam 0, 1, 2...
                        options=[{'label': f'{CONFIG_MAP[c]}', 'value': c} for c in CONFIGS],
                        value=0, className="mb-2"
                    ),

                    dbc.Label("Concentração (ppm):"),
                    dcc.Dropdown(
                        id="conc-select", 
                        options=[{'label': f'{c} ppm', 'value': c} for c in CONCENTRACOES],
                        value=100,
                        className="mb-2"
                    ),
                    
                    dbc.Label("Sensor:"),
                    dcc.Dropdown(
                        id="sensor-select", options=[{'label': s, 'value': s} for s in SENSORES],
                        value='MQ2', className="mb-2"
                    ),
                    
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
        
        # Área de Gráficos
        dbc.Col([
            dcc.Loading(children=[
                dbc.Tabs([
                    dbc.Tab(label="Linhas (Lines)", children=[
                        dcc.Graph(id="grafico-repeticoes"),
                        html.Div(id="tabela-metricas-container", className="mt-3")
                    ]),
                    # --- ABA ESTATÍSTICA (BOX PLOT) ---
                    dbc.Tab(label="Box Plot (Todos Gases)", children=[
                        dbc.Row([
                            dbc.Col(dcc.Graph(id="grafico-box-vmax"), width=12),
                        ], className="mt-3"),
                        dbc.Row([
                            dbc.Col(dcc.Graph(id="grafico-box-tempo"), width=12),
                        ], className="mt-3")
                    ]),
                    dbc.Tab(label="Comparativo Aletas (Configs)", children=[
                         dcc.Graph(id="grafico-comparacao-aletas", style={'height': '800px'})
                    ]),
                    dbc.Tab(label="Comparação Gases (Gases)", children=[
                        dcc.Graph(id="grafico-comparacao-gases", style={'height': '800px'})
                    ]),
                    dbc.Tab(label="Ambiental", children=[
                        dcc.Graph(id="grafico-ambiental-completo")
                    ]),
                    dbc.Tab(label="Derivadas", children=[
                        dcc.Graph(id="grafico-derivadas-v")
                    ]),
                ])
            ])
        ], width=9)
    ]),
    
    dcc.Store(id='store-metricas')
], fluid=True)

# Helper para estilização comum (Grades removidas, mais quadrado)
def style_figure(fig, width=None, height=700):
    fig.update_layout(
        template="plotly_white",
        xaxis=dict(showgrid=False),
        yaxis=dict(showgrid=False),
        height=height,
        width=width, # Se None, ele expande, mas o height faz ficar "retangular/quadrado"
        title=None # Tira o título
    )
    return fig

# Callback Principal
@app.callback(
    [Output("grafico-repeticoes", "figure"),
     Output("grafico-ambiental-completo", "figure"),
     Output("grafico-derivadas-v", "figure"),
     Output("grafico-comparacao-gases", "figure"),
     Output("grafico-box-vmax", "figure"),
     Output("grafico-box-tempo", "figure"),
     Output("grafico-comparacao-aletas", "figure"),
     Output("tabela-metricas-container", "children"),
     Output("store-metricas", "data"),
     Output("btn-processar", "children"),
     Output("btn-download", "children"),
     Output("btn-download-matrix", "children")],
    [Input("btn-processar", "n_clicks"), Input("language-select", "value")],
    [State("pasta-base", "value"),
     State("gas-select", "value"),
     State("config-select", "value"),
     State("conc-select", "value"),
     State("sensor-select", "value"),
     State("volume", "value")]
)
def update_graphs(n_clicks, lang, pasta_base, gas, config, conc, sensor_selecionado, volume):
    # Textos dos Botões
    txt_btn_proc = TRANSLATIONS[lang]['process']
    txt_btn_down = TRANSLATIONS[lang]['download_table']
    txt_btn_matr = TRANSLATIONS[lang]['download_matrix']

    if not n_clicks:
        return (go.Figure(), go.Figure(), go.Figure(), go.Figure(), go.Figure(), 
                go.Figure(), go.Figure(), html.Div(), None, 
                txt_btn_proc, txt_btn_down, txt_btn_matr)

    analisador = AnalisadorGases(volume_recipiente=volume)
    txt = TRANSLATIONS[lang] # Atalho para traduções

    # 1. Carregamento para os gráficos de LINHA
    dfs_ind, df_media = analisador.carregar_arquivos_individuais(pasta_base, gas, config, conc)

    if df_media is None:
        fig_vazia = go.Figure()
        return (fig_vazia, fig_vazia, fig_vazia, fig_vazia, fig_vazia, fig_vazia, fig_vazia, 
                html.Div("No Data"), None, txt_btn_proc, txt_btn_down, txt_btn_matr)

    df_media_suave = analisador.suavizar_dados(df_media, janela=5)
    tempo = df_media_suave['tempo'].values if 'tempo' in df_media_suave.columns else df_media_suave.index.values
    sensores_loop = ['MQ2', 'MQ3', 'MQ5'] if sensor_selecionado == 'TODOS' else [sensor_selecionado]

    # --- Gráfico Repetições ---
    fig_rep = go.Figure()
    # (Removido) Linha vertical
    # (Removido) Marcador X vermelho

    for idx_s, s in enumerate(sensores_loop):
        col_v = f'{s}_V'
        
        # Plotar Repetições
        if dfs_ind:
            for i, (rep, df_i) in enumerate(dfs_ind.items()):
                if col_v in df_i.columns:
                    # Tira nome do sensor da legenda se for TODOS, ou simplifica
                    nome_legenda = f"{rep}" if sensor_selecionado != 'TODOS' else f"{s} {rep}"
                    
                    fig_rep.add_trace(go.Scatter(
                        x=tempo, y=df_i[col_v], mode='lines',
                        name=nome_legenda, opacity=0.3, line=dict(width=1, color=CORES_PRIMARIAS[i % len(CORES_PRIMARIAS)]),
                        showlegend=True
                    ))
        
        # Plotar Média (Pontilhada e grossa)
        if col_v in df_media_suave.columns:
            nome_media = f"{txt['label_mean']}" if sensor_selecionado != 'TODOS' else f"{s} {txt['label_mean']}"
            fig_rep.add_trace(go.Scatter(
                x=tempo, y=df_media_suave[col_v], mode='lines',
                name=nome_media, 
                line=dict(width=3, dash='dot', color=CORES_PRIMARIAS[3]) # Amarelo/Laranja ou outra cor de destaque
            ))

    fig_rep = style_figure(fig_rep)
    fig_rep.update_layout(xaxis_title=txt['xaxis'], yaxis_title=txt['yaxis_v'])

    # --- Métricas Tabela ---
    lista_metricas = []
    for s in sensores_loop:
        metricas = analisador.calcular_metricas_avancadas(df_media_suave, dfs_ind, s)
        if metricas:
            metricas[txt['sensor']] = s
            lista_metricas.append(metricas)

    tabela_html = dbc.Table.from_dataframe(pd.DataFrame(lista_metricas).round(4), striped=True, bordered=True, responsive=True) if lista_metricas else html.Div("...")

    # -------------------------------------------------------------
    # 2. BOX PLOT - AGORA COM TODOS OS GASES
    # -------------------------------------------------------------
    dados_box_vmax = {'Config': [], 'Sensor': [], 'Valor': [], 'Gas': []}
    dados_box_tempo = {'Config': [], 'Sensor': [], 'Valor': [], 'Gas': []}
    
    # Loop Externo por Config, Loop Interno por GÁS (para pegar todos)
    for cfg_iter in CONFIGS:
        for gas_iter in GASES:
            dfs_cfg, _ = analisador.carregar_arquivos_individuais(pasta_base, gas_iter, cfg_iter, conc)
            
            if dfs_cfg:
                for s_box in ['MQ2', 'MQ3', 'MQ5']:
                    for rep, df_i in dfs_cfg.items():
                        m_ind = analisador.calcular_metricas_avancadas(df_i, None, s_box)
                        if m_ind:
                            if m_ind.get('V_max Relativo (V)') is not None:
                                dados_box_vmax['Config'].append(CONFIG_MAP[cfg_iter]) # Usa Nome (Padrão, V...)
                                dados_box_vmax['Sensor'].append(s_box)
                                dados_box_vmax['Gas'].append(gas_iter)
                                dados_box_vmax['Valor'].append(m_ind['V_max Relativo (V)'])
                            
                            if m_ind.get('Tempo Subida (s)') is not None:
                                dados_box_tempo['Config'].append(CONFIG_MAP[cfg_iter])
                                dados_box_tempo['Sensor'].append(s_box)
                                dados_box_tempo['Gas'].append(gas_iter)
                                dados_box_tempo['Valor'].append(m_ind['Tempo Subida (s)'])

    # Box Plot Vmax
    fig_box_vmax = go.Figure()
    for i, s_box in enumerate(['MQ2', 'MQ3', 'MQ5']):
        indices = [k for k, x in enumerate(dados_box_vmax['Sensor']) if x == s_box]
        x_vals = [dados_box_vmax['Config'][k] for k in indices]
        y_vals = [dados_box_vmax['Valor'][k] for k in indices]
        
        fig_box_vmax.add_trace(go.Box(
            x=x_vals, y=y_vals, name=s_box, 
            boxpoints='all', jitter=0.3, pointpos=-1.8,
            marker_color=CORES_PRIMARIAS[i] # Azul, Vermelho, Verde
        ))
    
    fig_box_vmax = style_figure(fig_box_vmax)
    fig_box_vmax.update_layout(yaxis_title=txt['yaxis_v'], boxmode='group')

    # Box Plot Tempo
    fig_box_tempo = go.Figure()
    for i, s_box in enumerate(['MQ2', 'MQ3', 'MQ5']):
        indices = [k for k, x in enumerate(dados_box_tempo['Sensor']) if x == s_box]
        x_vals = [dados_box_tempo['Config'][k] for k in indices]
        y_vals = [dados_box_tempo['Valor'][k] for k in indices]
        
        fig_box_tempo.add_trace(go.Box(
            x=x_vals, y=y_vals, name=s_box, 
            boxpoints='all', jitter=0.3, pointpos=-1.8,
            marker_color=CORES_PRIMARIAS[i]
        ))
    fig_box_tempo = style_figure(fig_box_tempo)
    fig_box_tempo.update_layout(yaxis_title=txt['xaxis'], boxmode='group')

    # -------------------------------------------------------------
    # 3. Gráficos Auxiliares
    # -------------------------------------------------------------
    
    # Ambiental
    fig_amb = make_subplots(specs=[[{"secondary_y": True}]])
    for i, s in enumerate(sensores_loop):
        col_v = f'{s}_V'
        if col_v in df_media_suave.columns:
            fig_amb.add_trace(go.Scatter(x=tempo, y=df_media_suave[col_v], name=s, line=dict(width=3, color=CORES_PRIMARIAS[i])), secondary_y=False)
    if 'temp' in df_media_suave.columns:
        fig_amb.add_trace(go.Scatter(x=tempo, y=df_media_suave['temp'], name="Temp", line=dict(color=CORES_PRIMARIAS[3], dash='dot')), secondary_y=False)
    if 'pressao' in df_media_suave.columns:
        fig_amb.add_trace(go.Scatter(x=tempo, y=df_media_suave['pressao'], name="Pressão", line=dict(color=CORES_PRIMARIAS[4], dash='dot')), secondary_y=True)
    fig_amb = style_figure(fig_amb)

    # Derivadas
    fig_deriv = go.Figure()
    for i, s in enumerate(sensores_loop):
        col_v = f'{s}_V'
        if col_v in df_media_suave.columns:
            dy = np.gradient(df_media_suave[col_v], tempo)
            fig_deriv.add_trace(go.Scatter(x=tempo, y=dy, name=f"dV/dt {s}", line=dict(color=CORES_PRIMARIAS[i])))
    fig_deriv = style_figure(fig_deriv)
    fig_deriv.update_layout(yaxis_title=txt['yaxis_rate'])

    # -------------------------------------------------------------
    # 4. Comparação de Gases
    # -------------------------------------------------------------
    fig_comp = make_subplots(rows=3, cols=1, subplot_titles=("MQ2", "MQ3", "MQ5"))
    
    for idx_g, gas_comp in enumerate(GASES):
        _, df_m_comp = analisador.carregar_arquivos_individuais(pasta_base, gas_comp, config, conc)
        if df_m_comp is not None:
            df_m_comp = analisador.suavizar_dados(df_m_comp, janela=5)
            t_comp = df_m_comp['tempo'].values if 'tempo' in df_m_comp.columns else df_m_comp.index.values
            
            for i, sensor in enumerate(['MQ2', 'MQ3', 'MQ5']):
                col_name = f'{sensor}_V'
                if col_name in df_m_comp.columns:
                    sinal = df_m_comp[col_name].values
                    # Normalização Minimo
                    mask_60 = t_comp <= 60
                    v_min_etapa1 = np.min(sinal[mask_60]) if np.any(mask_60) else np.min(sinal)
                    sinal_normalizado = sinal - v_min_etapa1
                    
                    fig_comp.add_trace(go.Scatter(
                        x=t_comp, y=sinal_normalizado, 
                        name=f'{txt["gas"]} {gas_comp}', legendgroup=gas_comp,
                        line=dict(color=CORES_PRIMARIAS[idx_g % len(CORES_PRIMARIAS)]),
                        showlegend=(i==0)
                    ), row=i+1, col=1)

    fig_comp = style_figure(fig_comp, height=800)

    # -------------------------------------------------------------
    # 5. Comparação de ALETAS (Configurações)
    # -------------------------------------------------------------
    fig_aletas = make_subplots(rows=3, cols=1, subplot_titles=("MQ2", "MQ3", "MQ5"))
    
    for idx_c, cfg_iter in enumerate(CONFIGS):
        _, df_m_aleta = analisador.carregar_arquivos_individuais(pasta_base, gas, cfg_iter, conc)
        if df_m_aleta is not None:
            df_m_aleta = analisador.suavizar_dados(df_m_aleta, janela=5)
            t_aleta = df_m_aleta['tempo'].values if 'tempo' in df_m_aleta.columns else df_m_aleta.index.values
            
            for i, sensor in enumerate(['MQ2', 'MQ3', 'MQ5']):
                col_name = f'{sensor}_V'
                if col_name in df_m_aleta.columns:
                    sinal = df_m_aleta[col_name].values
                    
                    mask_60 = t_aleta <= 60
                    v_min_etapa1 = np.min(sinal[mask_60]) if np.any(mask_60) else np.min(sinal)
                    sinal_normalizado = sinal - v_min_etapa1

                    fig_aletas.add_trace(go.Scatter(
                        x=t_aleta, y=sinal_normalizado, 
                        name=f'{CONFIG_MAP[cfg_iter]}', legendgroup=str(cfg_iter),
                        line=dict(color=CORES_PRIMARIAS[idx_c % len(CORES_PRIMARIAS)]),
                        showlegend=(i==0)
                    ), row=i+1, col=1)
    
    fig_aletas = style_figure(fig_aletas, height=800)

    return (fig_rep, fig_amb, fig_deriv, fig_comp, fig_box_vmax, fig_box_tempo, fig_aletas, 
            tabela_html, lista_metricas, txt_btn_proc, txt_btn_down, txt_btn_matr)

# Callback Download Excel Simples
@app.callback(
    Output("download-file-1", "data"),
    Input("btn-download", "n_clicks"),
    State("store-metricas", "data"),
    prevent_initial_call=True,
)
def download_excel_metrics(n_clicks, data):
    if not data: return dash.no_update
    return dcc.send_data_frame(pd.DataFrame(data).to_excel, "metricas_atual.xlsx", index=False)

# Callback Download Matriz Completa
@app.callback(
    Output("download-file-2", "data"),
    Input("btn-download-matrix", "n_clicks"),
    State("pasta-base", "value"),
    State("volume", "value"),
    State("conc-select", "value"),
    prevent_initial_call=True,
)
def download_matrix_excel(n_clicks, pasta_base, volume, conc):
    if not n_clicks: return dash.no_update
    analisador = AnalisadorGases(volume_recipiente=volume)
    df_matriz = analisador.gerar_matriz_completa(pasta_base, conc)
    if df_matriz.empty: return dash.no_update
    return dcc.send_data_frame(df_matriz.to_excel, f"matriz_completa_{conc}ppm.xlsx", index=False)

if __name__ == '__main__':
    if not os.path.exists('dados'): os.makedirs('dados', exist_ok=True)
    # Adicione use_reloader=False
    app.run(debug=True, port=8050, use_reloader=False)