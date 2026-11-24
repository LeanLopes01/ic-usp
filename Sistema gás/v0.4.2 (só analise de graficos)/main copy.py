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
CONFIGS = [0, 1, 2, 3, 4]
REPETICOES = ['R1', 'R2', 'R3'] 
SENSORES = ['MQ2', 'MQ3', 'MQ5', 'TODOS']
CONCENTRACOES = [100, 200]

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
            except Exception as e:
                print(f"Erro ao processar {prefixo} com conc {concentracao_alvo}: {e}")
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
        
        # --- ALTERADO: Baseline é o Mínimo nos primeiros 60s ---
        mask_60 = tempo <= 60
        if np.any(mask_60):
            v_baseline = np.min(sinal[mask_60])
        else:
            v_baseline = np.min(sinal) # Fallback
        # -------------------------------------------------------
        
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
                        if v_rel is not None:
                            dados_matriz.append({
                                'Config': config,
                                'Sensor': sensor,
                                'Gas': gas,
                                'Vmax_Relativo': v_rel
                            })
                        else:
                             dados_matriz.append({'Config': config, 'Sensor': sensor, 'Gas': gas, 'Vmax_Relativo': np.nan})
                else:
                     for sensor in ['MQ2', 'MQ3', 'MQ5']:
                         dados_matriz.append({'Config': config, 'Sensor': sensor, 'Gas': gas, 'Vmax_Relativo': np.nan})

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
        
        # --- ALTERADO: Baseline Min 60s ---
        mask_60 = tempo <= 60
        if np.any(mask_60):
            v_baseline = np.min(sinal[mask_60])
        else:
            v_baseline = np.min(sinal)
        # ----------------------------------
        
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
        dbc.Col([html.H2("Análise de Sensores de Gás", className="text-center my-3")])
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
                    html.Small("Seleciona para os gráficos de linha simples.", className="text-muted d-block mb-1"),
                    dcc.Dropdown(
                        id="config-select", options=[{'label': f'Config {c}', 'value': c} for c in CONFIGS],
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
                    dbc.Button("1. Processar Gráficos", id="btn-processar", color="primary", className="w-100 mb-2"),
                    dbc.Button("2. Baixar Tabela Atual (Excel)", id="btn-download", color="success", className="w-100 mb-2"),
                    
                    html.Hr(),
                    dbc.Label("Geração de Matriz Completa:"),
                    dbc.Button("3. Baixar Matriz Vmax (Excel)", id="btn-download-matrix", color="warning", className="w-100"),
                    html.Small("Isso processa todos os gases e configs para a concentração selecionada.", className="text-muted"),
                    
                    dcc.Download(id="download-file-1"),
                    dcc.Download(id="download-file-2")
                ])
            ])
        ], width=3),
        
        # Área de Gráficos
        dbc.Col([
            dcc.Loading(children=[
                dbc.Tabs([
                    dbc.Tab(label="Tensão & Repetições", children=[
                        dcc.Graph(id="grafico-repeticoes"),
                        html.Div(id="tabela-metricas-container", className="mt-3")
                    ]),
                    # --- ABA ESTATÍSTICA (BOX PLOT) ---
                    dbc.Tab(label="Comparativo Aletas (Box Plot)", children=[
                        dbc.Row([
                            dbc.Col(dcc.Graph(id="grafico-box-vmax"), width=12),
                        ], className="mt-3"),
                        dbc.Row([
                            dbc.Col(dcc.Graph(id="grafico-box-tempo"), width=12),
                        ], className="mt-3")
                    ]),
                    # --- ABA COMPARATIVO ALETAS (CURVAS) ---
                    dbc.Tab(label="Comparativo Aletas (Curvas)", children=[
                         dcc.Graph(id="grafico-comparacao-aletas", style={'height': '800px'})
                    ]),
                    # ---------------------------------------
                    dbc.Tab(label="Comparação de Gases", children=[
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

# Callback Principal dos Gráficos
@app.callback(
    [Output("grafico-repeticoes", "figure"),
     Output("grafico-ambiental-completo", "figure"),
     Output("grafico-derivadas-v", "figure"),
     Output("grafico-comparacao-gases", "figure"),
     Output("grafico-box-vmax", "figure"),
     Output("grafico-box-tempo", "figure"),
     # --- NOVO OUTPUT ---
     Output("grafico-comparacao-aletas", "figure"),
     # -------------------
     Output("tabela-metricas-container", "children"),
     Output("store-metricas", "data")],
    [Input("btn-processar", "n_clicks")],
    [State("pasta-base", "value"),
     State("gas-select", "value"),
     State("config-select", "value"),
     State("conc-select", "value"),
     State("sensor-select", "value"),
     State("volume", "value")]
)
def update_graphs(n_clicks, pasta_base, gas, config, conc, sensor_selecionado, volume):
    if not n_clicks:
        return go.Figure(), go.Figure(), go.Figure(), go.Figure(), go.Figure(), go.Figure(), go.Figure(), html.Div(), None

    analisador = AnalisadorGases(volume_recipiente=volume)
    
    # -------------------------------------------------------------
    # 1. Carregamento para os gráficos de LINHA (Config Selecionada)
    # -------------------------------------------------------------
    dfs_ind, df_media = analisador.carregar_arquivos_individuais(pasta_base, gas, config, conc)

    if df_media is None:
        fig_vazia = go.Figure()
        fig_vazia.update_layout(title="Dados não encontrados")
        # Retorna +1 figura vazia (total 7 figuras)
        return fig_vazia, fig_vazia, fig_vazia, fig_vazia, fig_vazia, fig_vazia, fig_vazia, html.Div(f"Dados não encontrados para Gás {gas} (Config {config}, {conc}ppm)"), None

    df_media_suave = analisador.suavizar_dados(df_media, janela=5)
    tempo = df_media_suave['tempo'].values if 'tempo' in df_media_suave.columns else df_media_suave.index.values
    sensores_loop = ['MQ2', 'MQ3', 'MQ5'] if sensor_selecionado == 'TODOS' else [sensor_selecionado]

    # --- Gráfico Repetições (Tensão Absoluta) ---
    fig_rep = go.Figure()
    fig_rep.add_vline(x=90, line_width=1, line_dash="dash", line_color="gray", annotation_text="Limite 90s")

    for s in sensores_loop:
        col_v = f'{s}_V'
        if dfs_ind:
            for rep, df_i in dfs_ind.items():
                if col_v in df_i.columns:
                    fig_rep.add_trace(go.Scatter(
                        x=tempo, y=df_i[col_v], mode='lines',
                        name=f'{s} - Rep {rep}', opacity=0.3, line=dict(width=1),
                        showlegend=False if sensor_selecionado == 'TODOS' else True
                    ))
        if col_v in df_media_suave.columns:
            fig_rep.add_trace(go.Scatter(
                x=tempo, y=df_media_suave[col_v], mode='lines',
                name=f'{s} - Média', line=dict(width=3)
            ))

    fig_rep.update_layout(title=f"Leitura de Tensão Absoluta ({conc} ppm) - Config {config}", xaxis_title="Tempo (s)", yaxis_title="Tensão (V)", template="plotly_white")

    # --- Métricas Tabela ---
    lista_metricas = []
    for s in sensores_loop:
        metricas = analisador.calcular_metricas_avancadas(df_media_suave, dfs_ind, s)
        if metricas:
            metricas['Sensor'] = s
            lista_metricas.append(metricas)
            if 'Tempo V_max (s)' in metricas:
                fig_rep.add_trace(go.Scatter(
                    x=[metricas['Tempo V_max (s)']], y=[metricas['V_max (90s) (V)']],
                    mode='markers', marker=dict(color='red', size=10, symbol='x'),
                    name=f'Max {s}', showlegend=False
                ))

    tabela_html = dbc.Table.from_dataframe(pd.DataFrame(lista_metricas).round(4), striped=True, bordered=True, responsive=True) if lista_metricas else html.Div("Sem métricas")

    # -------------------------------------------------------------
    # 2. Lógica BOX PLOT
    # -------------------------------------------------------------
    dados_box_vmax = {'Config': [], 'Sensor': [], 'Valor': []}
    dados_box_tempo = {'Config': [], 'Sensor': [], 'Valor': []}
    
    for cfg_iter in CONFIGS:
        dfs_cfg, _ = analisador.carregar_arquivos_individuais(pasta_base, gas, cfg_iter, conc)
        
        if dfs_cfg:
            for s_box in ['MQ2', 'MQ3', 'MQ5']:
                for rep, df_i in dfs_cfg.items():
                    m_ind = analisador.calcular_metricas_avancadas(df_i, None, s_box)
                    if m_ind:
                        if m_ind.get('V_max Relativo (V)') is not None:
                            dados_box_vmax['Config'].append(f'Config {cfg_iter}')
                            dados_box_vmax['Sensor'].append(s_box)
                            dados_box_vmax['Valor'].append(m_ind['V_max Relativo (V)'])
                        
                        if m_ind.get('Tempo Subida (s)') is not None:
                            dados_box_tempo['Config'].append(f'Config {cfg_iter}')
                            dados_box_tempo['Sensor'].append(s_box)
                            dados_box_tempo['Valor'].append(m_ind['Tempo Subida (s)'])

    fig_box_vmax = go.Figure()
    for s_box in ['MQ2', 'MQ3', 'MQ5']:
        indices = [i for i, x in enumerate(dados_box_vmax['Sensor']) if x == s_box]
        x_vals = [dados_box_vmax['Config'][i] for i in indices]
        y_vals = [dados_box_vmax['Valor'][i] for i in indices]
        
        fig_box_vmax.add_trace(go.Box(
            x=x_vals, y=y_vals, name=s_box, 
            boxpoints='all', jitter=0.3, pointpos=-1.8,
            marker_color='blue' if s_box == 'MQ2' else ('red' if s_box == 'MQ3' else 'green')
        ))
    
    fig_box_vmax.update_layout(title=f"Distribuição V_max Relativo por Aleta", yaxis_title="Tensão (V)", boxmode='group', template="plotly_white")

    fig_box_tempo = go.Figure()
    for s_box in ['MQ2', 'MQ3', 'MQ5']:
        indices = [i for i, x in enumerate(dados_box_tempo['Sensor']) if x == s_box]
        x_vals = [dados_box_tempo['Config'][i] for i in indices]
        y_vals = [dados_box_tempo['Valor'][i] for i in indices]
        
        fig_box_tempo.add_trace(go.Box(
            x=x_vals, y=y_vals, name=s_box, 
            boxpoints='all', jitter=0.3, pointpos=-1.8,
            marker_color='blue' if s_box == 'MQ2' else ('red' if s_box == 'MQ3' else 'green')
        ))
    fig_box_tempo.update_layout(title=f"Distribuição Tempo de Subida por Aleta", yaxis_title="Tempo (s)", boxmode='group', template="plotly_white")

    # -------------------------------------------------------------
    # 3. Gráficos Auxiliares
    # -------------------------------------------------------------
    
    # Ambiental
    fig_amb = make_subplots(specs=[[{"secondary_y": True}]])
    for s in sensores_loop:
        col_v = f'{s}_V'
        if col_v in df_media_suave.columns:
            fig_amb.add_trace(go.Scatter(x=tempo, y=df_media_suave[col_v], name=f"{s} (V)", line=dict(width=3)), secondary_y=False)
    if 'temp' in df_media_suave.columns:
        fig_amb.add_trace(go.Scatter(x=tempo, y=df_media_suave['temp'], name="Temp (°C)", line=dict(color='orange', dash='dot')), secondary_y=False)
    if 'pressao' in df_media_suave.columns:
        fig_amb.add_trace(go.Scatter(x=tempo, y=df_media_suave['pressao'], name="Pressão (hPa)", line=dict(color='green', dash='dot')), secondary_y=True)
    fig_amb.update_layout(title="Ambiental", template="plotly_white")

    # Derivadas
    fig_deriv = go.Figure()
    for s in sensores_loop:
        col_v = f'{s}_V'
        if col_v in df_media_suave.columns:
            dy = np.gradient(df_media_suave[col_v], tempo)
            fig_deriv.add_trace(go.Scatter(x=tempo, y=dy, name=f"dV/dt {s}"))
    fig_deriv.update_layout(title="Taxa de Variação", template="plotly_white")

    # -------------------------------------------------------------
    # 4. Comparação de Gases (NORMALIZADA PELO MINIMO 60s)
    # -------------------------------------------------------------
    fig_comp = make_subplots(rows=3, cols=1, subplot_titles=("Sensor MQ2 (Norm. Vmin 60s)", "Sensor MQ3 (Norm. Vmin 60s)", "Sensor MQ5 (Norm. Vmin 60s)"))
    cores_gases = {'A': 'blue', 'E': 'red', 'P': 'green', 'AR': 'gray'}
    
    for gas_comp in GASES:
        _, df_m_comp = analisador.carregar_arquivos_individuais(pasta_base, gas_comp, config, conc)
        if df_m_comp is not None:
            df_m_comp = analisador.suavizar_dados(df_m_comp, janela=5)
            t_comp = df_m_comp['tempo'].values if 'tempo' in df_m_comp.columns else df_m_comp.index.values
            
            for i, sensor in enumerate(['MQ2', 'MQ3', 'MQ5']):
                col_name = f'{sensor}_V'
                if col_name in df_m_comp.columns:
                    sinal = df_m_comp[col_name].values
                    
                    # --- NORMALIZAÇÃO: MINIMO DA ETAPA 1 (<= 60s) ---
                    mask_60 = t_comp <= 60
                    if np.any(mask_60):
                        v_min_etapa1 = np.min(sinal[mask_60])
                    else:
                        v_min_etapa1 = np.min(sinal)
                    
                    sinal_normalizado = sinal - v_min_etapa1
                    # ------------------------------------------------
                    
                    fig_comp.add_trace(go.Scatter(
                        x=t_comp, y=sinal_normalizado, 
                        name=f'Gás {gas_comp}', legendgroup=gas_comp,
                        line=dict(color=cores_gases.get(gas_comp, 'black')),
                        showlegend=(i==0)
                    ), row=i+1, col=1)

    fig_comp.update_layout(height=800, title=f"Comparação entre Gases (Config {config})", template="plotly_white")

    # -------------------------------------------------------------
    # 5. NOVO: Comparação de ALETAS (Curvas) - Normalizada
    # -------------------------------------------------------------
    fig_aletas = make_subplots(rows=3, cols=1, subplot_titles=("Sensor MQ2 (Norm. Vmin 60s)", "Sensor MQ3 (Norm. Vmin 60s)", "Sensor MQ5 (Norm. Vmin 60s)"))
    
    # Cores fixas para as configs para facilitar visualização
    cores_aletas = {0: 'black', 1: 'blue', 2: 'red', 3: 'green', 4: 'orange'}

    for cfg_iter in CONFIGS:
        _, df_m_aleta = analisador.carregar_arquivos_individuais(pasta_base, gas, cfg_iter, conc)
        if df_m_aleta is not None:
            df_m_aleta = analisador.suavizar_dados(df_m_aleta, janela=5)
            t_aleta = df_m_aleta['tempo'].values if 'tempo' in df_m_aleta.columns else df_m_aleta.index.values
            
            for i, sensor in enumerate(['MQ2', 'MQ3', 'MQ5']):
                col_name = f'{sensor}_V'
                if col_name in df_m_aleta.columns:
                    sinal = df_m_aleta[col_name].values
                    
                    # --- NORMALIZAÇÃO: MINIMO DA ETAPA 1 (<= 60s) ---
                    mask_60 = t_aleta <= 60
                    if np.any(mask_60):
                        v_min_etapa1 = np.min(sinal[mask_60])
                    else:
                        v_min_etapa1 = np.min(sinal)
                    
                    sinal_normalizado = sinal - v_min_etapa1
                    # ------------------------------------------------

                    fig_aletas.add_trace(go.Scatter(
                        x=t_aleta, y=sinal_normalizado, 
                        name=f'Config {cfg_iter}', legendgroup=str(cfg_iter),
                        line=dict(color=cores_aletas.get(cfg_iter, 'gray')),
                        showlegend=(i==0)
                    ), row=i+1, col=1)
    
    fig_aletas.update_layout(height=800, title=f"Comparação entre Aletas (Gás {gas} - {conc}ppm)", template="plotly_white")

    return fig_rep, fig_amb, fig_deriv, fig_comp, fig_box_vmax, fig_box_tempo, fig_aletas, tabela_html, lista_metricas

# Callback Download Tabela Simples (EXCEL)
@app.callback(
    Output("download-file-1", "data"),
    Input("btn-download", "n_clicks"),
    State("store-metricas", "data"),
    prevent_initial_call=True,
)
def download_excel_metrics(n_clicks, data):
    if not data: return dash.no_update
    return dcc.send_data_frame(pd.DataFrame(data).to_excel, "metricas_tabela_atual.xlsx", index=False)

# Callback Download Matriz Completa (EXCEL)
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
    
    if df_matriz.empty:
        return dash.no_update
        
    return dcc.send_data_frame(df_matriz.to_excel, f"matriz_vmax_relativo_{conc}ppm.xlsx", index=False)

if __name__ == '__main__':
    if not os.path.exists('dados'): os.makedirs('dados', exist_ok=True)
    app.run(debug=True, port=8050)