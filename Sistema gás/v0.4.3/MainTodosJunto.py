import pandas as pd
import numpy as np
import plotly.graph_objects as go
from plotly.subplots import make_subplots 
import dash
from dash import dcc, html, Input, Output, State, dash_table
import dash_bootstrap_components as dbc
import os
import re
import time

# --- CONFIGURAÇÃO GLOBAL ---
# NOVA CONSTANTE PARA CONTROLAR O TAMANHO DA FONTE EM TUDO
TAMANHO_FONTE_GLOBAL = 26

GAS_INFO = {
    'E': 'Ethyl Alcohol',     # 1ª Linha (Etileno)
    'P': 'Isopropyl Alcohol', # 2ª Linha (Isopropílico)
    'A': 'Acetone'            # 3ª Linha (Acetona)
}
GAS_CODES = list(GAS_INFO.keys())
CONFIG_IDS = [0, 1, 2, 3, 4]

SENSORES = ['MQ2', 'MQ3', 'MQ5']
REPETICOES = ['R1', 'R2', 'R3']

# ALTERAÇÃO 1: Mudança de 'Standard' para 'Default'
CONFIG_NAMES = {0: 'Default', 1: 'Config V', 2: 'Config A', 3: 'Config X', 4: 'Config O'}
CONCENTRACOES = [100, 200]

# Cores
CORES_NEON = {'R1': '#FF00FF', 'R2': '#1F51FF', 'R3': '#00D400'}
CORES_CONFIG = {0: 'black', 1: '#E63946', 2: '#457B9D', 3: '#2A9D8F', 4: '#F4A261'}
CORES_PRIMARIAS = ['#0000FF', '#FF0000', '#008000', '#FFD700', '#800080'] 

CORES_EXPANDIDAS = [
    '#FF0000', '#0000FF', '#00CC00', '#FF00FF', '#FF8C00', 
    '#00CED1', '#9400D3', '#FF1493', '#FFD700', '#1E90FF', 
    '#DC143C', '#00FA9A', '#8A2BE2', '#FF4500', '#20B2AA', 
    '#C71585', '#4169E1', '#32CD32', '#BA55D3', '#008B8B'
]

class AnalisadorGases:
    def __init__(self):
        # Contadores de Performance
        self.total_linhas_lidas = 0
        self.total_arquivos_lidos = 0

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
                    
                    # Contabiliza Performance (Dados Brutos lidos)
                    self.total_linhas_lidas += len(df)
                    self.total_arquivos_lidos += 1
                    
                    mapeamento = {
                        'MQ2(A0)': 'MQ2', 'MQ3(A1)': 'MQ3', 'MQ5(A2)': 'MQ5', 
                        'Tempo(s)': 'tempo', 'Temperatura(C)': 'Temperatura',
                        'Umidade(%)': 'Umidade', 'Pressao(hPa)': 'Pressao'
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

    def calcular_metricas_avancadas(self, df_media, sensor):
        col_v = f'{sensor}_V'
        if col_v not in df_media.columns: return {}
        tempo = df_media['tempo'].values if 'tempo' in df_media.columns else df_media.index.values
        sinal = df_media[col_v].values
        mask_60 = tempo <= 60
        v_baseline = np.min(sinal[mask_60]) if np.any(mask_60) else np.min(sinal)
        v_max = np.max(sinal)
        gradiente = np.gradient(sinal, tempo)
        taxa_maxima = np.max(gradiente)
        return {
            'Sensor': sensor, 'V_baseline (V)': v_baseline,
            'V_max Abs (V)': v_max, 'V_max Relativo (V)': v_max - v_baseline,
            'Taxa Max (V/s)': taxa_maxima
        }

    def gerar_matriz_completa(self, pasta_base, concentracao_alvo):
        # Esta função gera uma tabela 'flat' (long format) para exportação Excel
        dados_matriz = []
        for config_id in CONFIG_IDS:
            for gas in GAS_CODES:
                res = self.carregar_dados(pasta_base, gas, config_id, concentracao_alvo)
                if isinstance(res, tuple):
                    _, df_media = res
                    if df_media is not None:
                        df_media = self.suavizar(df_media, janela=5)
                        for sensor in SENSORES:
                            metricas = self.calcular_metricas_avancadas(df_media, sensor)
                            if metricas:
                                dados_matriz.append({
                                    'Config': CONFIG_NAMES[config_id], 
                                    'Sensor': sensor, 'Gas': gas,
                                    'Vmax_Relativo': metricas['V_max Relativo (V)']
                                })
        df = pd.DataFrame(dados_matriz)
        if not df.empty:
            return df.pivot_table(index=['Config', 'Sensor'], columns='Gas', values='Vmax_Relativo').reset_index()
        return pd.DataFrame()

    def gerar_tabelas_comparativas(self, pasta_base, concentracao_alvo, janela):
        """
        Gera dicionário de DataFrames pivotados para visualização de Matriz na Aba 14.
        Cálculo de Porcentagem baseado em valores arredondados de 2 casas.
        """
        dados = {s: {g: {} for g in GAS_CODES} for s in SENSORES}
        
        # 1. Coleta os valores absolutos e ARREDONDA PREVIAMENTE para 2 casas
        for config_id in CONFIG_IDS:
            config_name = CONFIG_NAMES[config_id]
            for gas in GAS_CODES:
                res = self.carregar_dados(pasta_base, gas, config_id, concentracao_alvo)
                if isinstance(res, tuple):
                    _, df_media = res
                    if df_media is not None:
                          df_media = self.suavizar(df_media, janela)
                          for sensor in SENSORES:
                              m = self.calcular_metricas_avancadas(df_media, sensor)
                              if m:
                                  # CORREÇÃO: Arredonda aqui para garantir consistência no cálculo da %
                                  val_arredondado = round(m['V_max Relativo (V)'], 2)
                                  dados[sensor][gas][config_name] = val_arredondado
        
        # 2. Formata combinando Valor Absoluto e Porcentagem (usando os valores já arredondados)
        baseline_name = CONFIG_NAMES[0] # 'Default'
        
        for sensor in SENSORES:
            for gas in GAS_CODES:
                row_data = dados[sensor][gas]
                val_baseline = row_data.get(baseline_name)
                
                # Vamos iterar sobre as chaves (configs) que existem para este gás
                for cfg_name in list(row_data.keys()):
                    val_atual = row_data[cfg_name]
                    
                    if val_baseline is not None and val_baseline > 0.00:
                        pct = (val_atual / val_baseline) * 100
                        dados[sensor][gas][cfg_name] = f"{val_atual:.2f} V ({pct:.2f}%)"
                    elif cfg_name == baseline_name:
                        dados[sensor][gas][cfg_name] = f"{val_atual:.2f} V (100.00%)"
                    else:
                        dados[sensor][gas][cfg_name] = f"{val_atual:.2f} V (N/A)"

        dfs_finais = {}
        for sensor in SENSORES:
            df = pd.DataFrame.from_dict(dados[sensor], orient='index')
            df.index = df.index.map(GAS_INFO)
            
            cols_existentes = [CONFIG_NAMES[i] for i in CONFIG_IDS if CONFIG_NAMES[i] in df.columns]
            df = df[cols_existentes]
            
            df.reset_index(inplace=True)
            df.rename(columns={'index': 'Gás'}, inplace=True)
            
            dfs_finais[sensor] = df
            
        return dfs_finais

# --- DASH APP ---
app = dash.Dash(__name__, external_stylesheets=[dbc.themes.JOURNAL])

app.layout = dbc.Container([
    dbc.Row([
        # --- SIDEBAR ---
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
                    
                    html.Hr(),
                    dbc.Label("Para Visualização Detalhada (Aba 12):", className="fw-bold mt-2"),
                    dbc.Label("Gás:"),
                    dcc.Dropdown(id="gas-detalhe", options=[{'label': f'{k} ({v})', 'value': k} for k, v in GAS_INFO.items()], value='A', className="mb-1"),
                    dbc.Label("Sensor:"),
                    dcc.Dropdown(id="sensor-detalhe", options=[{'label': s, 'value': s} for s in SENSORES], value='MQ2', className="mb-2"),
                    
                    dbc.Button("Gerar Gráficos", id="btn-gerar", color="primary", className="w-100 mt-3 mb-3"),
                    
                    html.Hr(),
                    dbc.Button("Baixar Tabela (Métricas)", id="btn-download-metrics", color="success", className="w-100 mb-2", disabled=True),
                    dbc.Button("Baixar Matriz Geral", id="btn-download-matrix", color="warning", className="w-100 mb-2"),
                    dcc.Download(id="download-file-1"),
                    dcc.Download(id="download-file-2")
                ])
            ])
        ], width=3),
        
        # --- MAIN CONTENT ---
        dbc.Col([
            dcc.Tabs(id="tabs-graficos", value='tab-voltage', children=[
                dcc.Tab(label='1. Tensão Absoluta', value='tab-voltage', children=[dcc.Loading(dcc.Graph(id="grafico-voltage", style={"height": "800px"}))]),
                dcc.Tab(label='2. Tensão Relativa (ΔV)', value='tab-relative', children=[dcc.Loading(dcc.Graph(id="grafico-relative", style={"height": "800px"}))]),
                dcc.Tab(label='3. Pressão', value='tab-pressao', children=[dcc.Loading(dcc.Graph(id="grafico-pressao", style={"height": "800px"}))]),
                dcc.Tab(label='4. Temperatura', value='tab-temp', children=[dcc.Loading(dcc.Graph(id="grafico-temp", style={"height": "800px"}))]),
                dcc.Tab(label='5. Umidade', value='tab-umidade', children=[dcc.Loading(dcc.Graph(id="grafico-umidade", style={"height": "800px"}))]),
                dcc.Tab(label='6. Comparativo Aletas', value='tab-aletas', children=[dcc.Loading(dcc.Graph(id="grafico-aletas", style={"height": "800px"}))]),
                dcc.Tab(label='7. Média Selecionada', value='tab-media-sel', children=[dcc.Loading(dcc.Graph(id="grafico-media-sel", style={"height": "800px"}))]),
                dcc.Tab(label='8. Box Plots', value='tab-boxplot', children=[dcc.Loading(dcc.Graph(id="grafico-boxplot", style={"height": "800px"}))]),
                dcc.Tab(label='9. Tabela Métricas', value='tab-tabela', children=[html.Div(id="tabela-container", className="p-4")]),
                dcc.Tab(label='10. Taxa de Variação', value='tab-derivada', children=[dcc.Loading(dcc.Graph(id="grafico-derivada", style={"height": "800px"}))]),
                dcc.Tab(label='11. Ambiental Combinado', value='tab-amb-comb', children=[dcc.Loading(dcc.Graph(id="grafico-amb-comb", style={"height": "800px"}))]),
                
                # Aba 12
                dcc.Tab(label='12. Detalhe', value='tab-detalhe', children=[
                    html.Div([dcc.Loading(dcc.Graph(id="grafico-detalhe"))], style={'display': 'flex', 'justify-content': 'center', 'align-items': 'center', 'padding': '20px'})
                ]),

                # Aba 13
                dcc.Tab(label='13. Performance do Sistema', value='tab-performance', children=[
                    dcc.Loading(dcc.Graph(id="grafico-performance", style={"height": "800px"}))
                ]),

                # Aba 14 (ATUALIZADA)
                dcc.Tab(label='14. Matrizes de Comparação', value='tab-matrizes', children=[
                    html.Div(id="container-matrizes", className="p-4")
                ]),
            ], className="mt-3")
        ], width=9)
    ]),
    dcc.Store(id='store-metricas'),
    dcc.Store(id='store-performance', storage_type='local'), 
], fluid=True)

# --- FUNÇÕES DE PLOTAGEM ---
def formatar_layout_matriz(fig, tem_dados):
    # ALTERAÇÃO 2: Aplica TAMANHO_FONTE_GLOBAL (22) no layout geral e nas anotações (títulos dos subplots)
    fig.update_layout(
        template="simple_white", 
        font=dict(family="Times New Roman", size=TAMANHO_FONTE_GLOBAL, color="black"), # Fonte Geral
        margin=dict(l=120, r=60, t=80, b=100), 
        legend=dict(orientation="h", y=1.05, x=0.5, font=dict(size=TAMANHO_FONTE_GLOBAL)), # Fonte Legenda
        plot_bgcolor='rgba(0,0,0,0)'
    )
    # Força o tamanho da fonte nos títulos das linhas e colunas dos subplots
    fig.update_annotations(font=dict(size=TAMANHO_FONTE_GLOBAL))
    
    if not tem_dados: fig.update_layout(title="Sem dados")
    
    # Título do eixo X inferior
    fig.add_annotation(
        x=0.5, y=-0.09, 
        text="<b>Time (s)</b>", 
        xref="paper", yref="paper", 
        showarrow=False, 
        font=dict(size=TAMANHO_FONTE_GLOBAL)
    )
    return fig

def criar_figura_simples(analisador, pasta, config, conc, janela, modo_relativo=False):
    colunas_titulos = [f"<b>{s}</b>" for s in SENSORES]
    fig = make_subplots(rows=3, cols=3, shared_xaxes=True, horizontal_spacing=0.07, vertical_spacing=0.08, column_titles=colunas_titulos, row_titles=[f"<b>{GAS_INFO[g]}</b>" for g in GAS_CODES])
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
    
    fig = formatar_layout_matriz(fig, tem_dados)
    
    titulo_y = "<b>Relative Voltage (ΔV)</b>" if modo_relativo else "<b>Voltage (V)</b>"
    
    # Aplica o tamanho global no título lateral
    fig.add_annotation(
        x=-0.09, y=0.5, 
        text=titulo_y, 
        textangle=-90, 
        xref="paper", yref="paper", 
        showarrow=False, 
        font=dict(size=TAMANHO_FONTE_GLOBAL) # <--- Usa a constante global
    )
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
    
    fig = formatar_layout_matriz(fig, tem_dados)
    fig.update_layout(margin=dict(l=140, r=100, t=80, b=100))
    
    # Aplica fonte global aos títulos laterais
    fig.add_annotation(x=-0.1, y=0.5, text=f"<b>{titulo_eixo_esq}</b>", textangle=-90, xref="paper", yref="paper", showarrow=False, font=dict(size=TAMANHO_FONTE_GLOBAL, color='gray'))
    fig.add_annotation(x=1.1, y=0.5, text="<b>Voltage (V)</b>", textangle=-90, xref="paper", yref="paper", showarrow=False, font=dict(size=TAMANHO_FONTE_GLOBAL))
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
    
    fig = formatar_layout_matriz(fig, tem_dados)
    # Ajuste de margem e legenda, mantendo a fonte global
    fig.update_layout(margin=dict(l=120, r=60, t=100, b=100), legend=dict(orientation="h", y=1.1, x=0.5, xanchor="center", font=dict(size=TAMANHO_FONTE_GLOBAL)))
    
    # Aplica fonte global ao título lateral
    fig.add_annotation(x=-0.09, y=0.5, text="<b>Relative Mean Voltage (ΔV)</b>", textangle=-90, xref="paper", yref="paper", showarrow=False, font=dict(size=TAMANHO_FONTE_GLOBAL))
    return fig

def criar_grafico_media_individual(analisador, pasta, config, conc, janela):
    colunas_titulos = [f"<b>{s}</b>" for s in SENSORES]
    fig = make_subplots(rows=3, cols=3, shared_xaxes=True, horizontal_spacing=0.07, vertical_spacing=0.08, column_titles=colunas_titulos, row_titles=[f"<b>{GAS_INFO[g]}</b>" for g in GAS_CODES])
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
                if len(y_val) > 0: y_val = y_val - np.min(y_val)
                fig.add_trace(go.Scatter(x=tempo, y=y_val, mode='lines', line=dict(color=cor_linha, width=2.5), showlegend=(row==1 and col==1), name=f"{nome_config} (Mean)"), row=row, col=col)
            fig.update_xaxes(showline=True, linewidth=1.5, linecolor='black', mirror=True, row=row, col=col)
            fig.update_yaxes(showline=True, linewidth=1.5, linecolor='black', mirror=True, row=row, col=col)
    fig = formatar_layout_matriz(fig, tem_dados)
    
    # Aplica fonte global ao título lateral
    fig.add_annotation(x=-0.09, y=0.5, text="<b>Relative Mean Voltage (ΔV)</b>", textangle=-90, xref="paper", yref="paper", showarrow=False, font=dict(size=TAMANHO_FONTE_GLOBAL))
    return fig

def criar_grafico_derivada(analisador, pasta, config, conc, janela):
    colunas_titulos = [f"<b>{s}</b>" for s in SENSORES]
    fig = make_subplots(rows=3, cols=3, shared_xaxes=True, horizontal_spacing=0.07, vertical_spacing=0.08, column_titles=colunas_titulos, row_titles=[f"<b>{GAS_INFO[g]}</b>" for g in GAS_CODES])
    tem_dados = False
    for i, gas in enumerate(GAS_CODES):
        row = i+1
        _, df_media = analisador.carregar_dados(pasta, gas, config, conc)
        if df_media is None: continue
        tem_dados = True
        df_media = analisador.suavizar(df_media, janela)
        tempo = df_media['tempo'] if 'tempo' in df_media.columns else df_media.index.values
        for j, s in enumerate(SENSORES):
            col = j+1
            col_v = f'{s}_V'
            if col_v in df_media.columns:
                gradiente = np.gradient(df_media[col_v].values, tempo)
                fig.add_trace(go.Scatter(x=tempo, y=gradiente, mode='lines', line=dict(color='purple', width=1.5), name="Rate", showlegend=(row==1 and col==1)), row=row, col=col)
            fig.update_xaxes(showline=True, linewidth=1.5, linecolor='black', mirror=True, row=row, col=col)
            fig.update_yaxes(showline=True, linewidth=1.5, linecolor='black', mirror=True, row=row, col=col)
    
    fig = formatar_layout_matriz(fig, tem_dados)
    
    # Aplica fonte global ao título lateral
    fig.add_annotation(x=-0.09, y=0.5, text="<b>Rate (V/s)</b>", textangle=-90, xref="paper", yref="paper", showarrow=False, font=dict(size=TAMANHO_FONTE_GLOBAL))
    return fig

def criar_grafico_ambiental_combinado(analisador, pasta, config, conc, janela):
    colunas_titulos = [f"<b>{s}</b>" for s in SENSORES]
    fig = make_subplots(rows=3, cols=3, shared_xaxes=True, horizontal_spacing=0.12, vertical_spacing=0.08, column_titles=colunas_titulos, row_titles=[f"<b>{GAS_INFO[g]}</b>" for g in GAS_CODES], specs=[[{"secondary_y": True}] * 3] * 3)
    tem_dados = False
    for i, gas in enumerate(GAS_CODES):
        row = i+1
        _, df_media = analisador.carregar_dados(pasta, gas, config, conc)
        if df_media is None: continue
        tem_dados = True
        df_media = analisador.suavizar(df_media, janela)
        tempo = df_media['tempo'] if 'tempo' in df_media.columns else df_media.index.values
        
        for j, s in enumerate(SENSORES):
            col = j+1
            if 'Temperatura' in df_media.columns:
                fig.add_trace(go.Scatter(x=tempo, y=df_media['Temperatura'], mode='lines', line=dict(color='red', width=1.5), name="Temp", showlegend=(row==1 and col==1)), row=row, col=col, secondary_y=False)
            if 'Umidade' in df_media.columns:
                fig.add_trace(go.Scatter(x=tempo, y=df_media['Umidade'], mode='lines', line=dict(color='blue', width=1.5), name="Umid", showlegend=(row==1 and col==1)), row=row, col=col, secondary_y=False)
            if 'Pressao' in df_media.columns:
                fig.add_trace(go.Scatter(x=tempo, y=df_media['Pressao'], mode='lines', line=dict(color='green', width=1.5, dash='dot'), name="Press", showlegend=(row==1 and col==1)), row=row, col=col, secondary_y=True)
            
            fig.update_xaxes(showline=True, linewidth=1.5, linecolor='black', mirror=True, row=row, col=col)
            fig.update_yaxes(showline=True, linewidth=1.5, linecolor='black', row=row, col=col, secondary_y=False)
            fig.update_yaxes(showline=True, linewidth=1.5, linecolor='black', row=row, col=col, secondary_y=True)
            if col != 1: fig.update_yaxes(showticklabels=False, row=row, col=col, secondary_y=False)
            if col != 3: fig.update_yaxes(showticklabels=False, row=row, col=col, secondary_y=True)

    fig = formatar_layout_matriz(fig, tem_dados)
    fig.update_layout(margin=dict(l=140, r=100, t=80, b=100))
    
    # Aplica fonte global aos títulos laterais
    fig.add_annotation(x=-0.1, y=0.5, text="<b>Temp (C) / Umid (%)</b>", textangle=-90, xref="paper", yref="paper", showarrow=False, font=dict(size=TAMANHO_FONTE_GLOBAL, color='black'))
    fig.add_annotation(x=1.1, y=0.5, text="<b>Pressão (hPa)</b>", textangle=-90, xref="paper", yref="paper", showarrow=False, font=dict(size=TAMANHO_FONTE_GLOBAL, color='green'))
    return fig

def criar_grafico_detalhado(analisador, pasta, config, conc, janela, gas_sel, sensor_sel):
    fig = go.Figure()
    res = analisador.carregar_dados(pasta, gas_sel, config, conc)
    if not isinstance(res, tuple): return fig
    dfs_reps, df_media = res
    if df_media is None: 
        fig.update_layout(title="Sem dados para esta combinação")
        return fig
    
    df_media = analisador.suavizar(df_media, janela)
    tempo_media = df_media['tempo'] if 'tempo' in df_media.columns else df_media.index.values
    col_v = f'{sensor_sel}_V'
    
    if dfs_reps:
        for i, (rep, df_r) in enumerate(dfs_reps.items()):
            df_r = analisador.suavizar(df_r, janela)
            tempo_r = df_r['tempo'] if 'tempo' in df_r.columns else df_r.index.values
            if col_v in df_r.columns:
                cor = CORES_EXPANDIDAS[i % len(CORES_EXPANDIDAS)]
                fig.add_trace(go.Scatter(x=tempo_r, y=df_r[col_v], mode='lines', name=rep, line=dict(color=cor, width=2)))
    
    if col_v in df_media.columns:
         fig.add_trace(go.Scatter(x=tempo_media, y=df_media[col_v], mode='lines', name="Média", line=dict(color='black', width=4, dash='dot')))
    
    # Aplica fonte global ao layout deste gráfico específico
    fig.update_layout(
        template="plotly_white",
        title=dict(text=f"{GAS_INFO[gas_sel]} - {sensor_sel} ({CONFIG_NAMES[config]})", font=dict(size=TAMANHO_FONTE_GLOBAL)),
        xaxis_title=dict(text="<b>Tempo (s)</b>", font=dict(size=TAMANHO_FONTE_GLOBAL)),
        yaxis_title=dict(text="<b>Tensão (V)</b>", font=dict(size=TAMANHO_FONTE_GLOBAL)),
        font=dict(family="Times New Roman", size=TAMANHO_FONTE_GLOBAL, color="black"),
        legend=dict(orientation="h", yanchor="bottom", y=1.02, xanchor="center", x=0.5, font=dict(size=TAMANHO_FONTE_GLOBAL)),
        margin=dict(l=80, r=40, t=100, b=80),
        width=900, height=700, autosize=False
    )
    fig.update_xaxes(showgrid=False, showline=True, linewidth=2, linecolor='black', mirror=True, tickfont=dict(size=TAMANHO_FONTE_GLOBAL))
    fig.update_yaxes(showgrid=False, showline=True, linewidth=2, linecolor='black', mirror=True, tickfont=dict(size=TAMANHO_FONTE_GLOBAL))
    return fig

def criar_grafico_boxplot(analisador, pasta, conc, janela):
    dados_box = {'Config': [], 'Sensor': [], 'Valor': [], 'Gás': []}
    for cfg_iter in CONFIG_IDS:
        for gas in GAS_CODES:
            res = analisador.carregar_dados(pasta, gas, cfg_iter, conc)
            if isinstance(res, tuple):
                dfs_cfg, _ = res
                if dfs_cfg:
                    for s_box in SENSORES:
                        for rep, df_i in dfs_cfg.items():
                            df_i_suave = analisador.suavizar(df_i, janela)
                            col = f'{s_box}_V'
                            if col in df_i_suave.columns:
                                val = df_i_suave[col].values
                                if len(val) > 0:
                                    v_max_rel = np.max(val) - np.min(val)
                                    dados_box['Config'].append(CONFIG_NAMES[cfg_iter])
                                    dados_box['Sensor'].append(s_box)
                                    dados_box['Gás'].append(GAS_INFO[gas])
                                    dados_box['Valor'].append(v_max_rel)
    
    df_box = pd.DataFrame(dados_box)
    if df_box.empty:
        fig = go.Figure()
        fig.update_layout(title="Sem dados suficientes para Box Plot")
        return fig

    fig = go.Figure()
    for i, s_box in enumerate(SENSORES):
        df_filt = df_box[df_box['Sensor'] == s_box]
        fig.add_trace(go.Box(x=df_filt['Config'], y=df_filt['Valor'], name=s_box, marker_color=CORES_PRIMARIAS[i], boxpoints='all', jitter=0.3))
        
    # Aplica fonte global ao layout do Boxplot
    fig.update_layout(
        template="simple_white", 
        font=dict(family="Times New Roman", size=TAMANHO_FONTE_GLOBAL, color="black"),
        yaxis_title=dict(text="Vmax Relativo (V)", font=dict(size=TAMANHO_FONTE_GLOBAL)),
        boxmode='group', 
        title=dict(text="Dispersão de Vmax Relativo por Configuração (Todos os Gases)", font=dict(size=TAMANHO_FONTE_GLOBAL)),
        margin=dict(l=80, r=40, t=100, b=80),
        legend=dict(font=dict(size=TAMANHO_FONTE_GLOBAL))
    )
    fig.update_xaxes(tickfont=dict(size=TAMANHO_FONTE_GLOBAL))
    fig.update_yaxes(tickfont=dict(size=TAMANHO_FONTE_GLOBAL))
    return fig

def criar_grafico_performance(history_data):
    if not history_data:
        fig = go.Figure()
        fig.update_layout(title="Sem dados de histórico ainda. Gere gráficos para popular.")
        return fig

    df_perf = pd.DataFrame(history_data)
    df_perf['Run'] = range(1, len(df_perf) + 1)
    
    fig = make_subplots(
        rows=1, cols=2, 
        specs=[[{"secondary_y": True}, {"type": "domain"}]],
        subplot_titles=("Histórico: Tempo de Processamento vs Dados", "Resumo da Última Execução")
    )
    
    fig.add_trace(
        go.Scatter(x=df_perf['Run'], y=df_perf['time'], name="Tempo (s)", mode='lines+markers', line=dict(color='red', width=3)),
        row=1, col=1, secondary_y=False
    )
    fig.add_trace(
        go.Bar(x=df_perf['Run'], y=df_perf['rows'], name="Linhas Lidas", marker_color='rgba(0, 0, 255, 0.3)'),
        row=1, col=1, secondary_y=True
    )
    
    last_run = df_perf.iloc[-1]
    fig.add_trace(
        go.Indicator(
            mode = "number+delta",
            value = last_run['time'],
            title = {"text": "Tempo da Última Execução (s)", "font": {"size": TAMANHO_FONTE_GLOBAL}},
            delta = {'reference': df_perf.iloc[-2]['time'] if len(df_perf) > 1 else last_run['time'], 'relative': False},
            domain = {'x': [0.6, 1], 'y': [0, 1]},
            number = {"font": {"size": TAMANHO_FONTE_GLOBAL * 1.5}}
        ),
        row=1, col=2
    )

    # Aplica fonte global ao layout de Performance
    fig.update_layout(
        template="simple_white", 
        title=dict(text="Performance do Dashboard", font=dict(size=TAMANHO_FONTE_GLOBAL)),
        font=dict(family="Times New Roman", size=TAMANHO_FONTE_GLOBAL, color="black"),
        legend=dict(font=dict(size=TAMANHO_FONTE_GLOBAL))
    )
    fig.update_annotations(font=dict(size=TAMANHO_FONTE_GLOBAL)) # Para os subtítulos
    fig.update_yaxes(title_text="Tempo (s)", secondary_y=False, row=1, col=1, title_font=dict(size=TAMANHO_FONTE_GLOBAL), tickfont=dict(size=TAMANHO_FONTE_GLOBAL))
    fig.update_yaxes(title_text="Total Linhas CSV", secondary_y=True, row=1, col=1, title_font=dict(size=TAMANHO_FONTE_GLOBAL), tickfont=dict(size=TAMANHO_FONTE_GLOBAL))
    fig.update_xaxes(title_text="Execução (#)", row=1, col=1, title_font=dict(size=TAMANHO_FONTE_GLOBAL), tickfont=dict(size=TAMANHO_FONTE_GLOBAL))
    
    return fig

# --- CALLBACK UNIFICADO ---
@app.callback(
    [Output("grafico-voltage", "figure"),
     Output("grafico-relative", "figure"),
     Output("grafico-pressao", "figure"),
     Output("grafico-temp", "figure"),
     Output("grafico-umidade", "figure"),
     Output("grafico-aletas", "figure"),
     Output("grafico-media-sel", "figure"),
     Output("grafico-boxplot", "figure"),
     Output("grafico-derivada", "figure"),       
     Output("grafico-amb-comb", "figure"),       
     Output("grafico-detalhe", "figure"),
     Output("grafico-performance", "figure"),
     Output("tabela-container", "children"),
     Output("container-matrizes", "children"),
     Output("btn-download-metrics", "disabled"),
     Output("store-metricas", "data"),
     Output("store-performance", "data")],
    Input("btn-gerar", "n_clicks"),
    [State("pasta", "value"), State("config", "value"), State("conc", "value"), State("janela", "value"),
     State("gas-detalhe", "value"), State("sensor-detalhe", "value"),
     State("store-performance", "data")]
)
def atualizar_todos_graficos(n_clicks, pasta, config, conc, janela, gas_detalhe, sensor_detalhe, history_data):
    if not n_clicks: 
        return [go.Figure()] * 12 + [html.Div(), html.Div(), True, None, history_data]
    
    start_time = time.time()
    analisador = AnalisadorGases()
    
    # Gera Gráficos Existentes
    fig_v = criar_figura_simples(analisador, pasta, config, conc, janela, modo_relativo=False)
    fig_rel = criar_figura_simples(analisador, pasta, config, conc, janela, modo_relativo=True)
    fig_p = criar_figura_hibrida(analisador, pasta, config, conc, janela, 'Pressao', 'Pressure (hPa)')
    fig_t = criar_figura_hibrida(analisador, pasta, config, conc, janela, 'Temperatura', 'Temp (°C)')
    fig_h = criar_figura_hibrida(analisador, pasta, config, conc, janela, 'Umidade', 'Humidity (%)')
    fig_aletas = criar_grafico_comparativo_aletas(analisador, pasta, conc, janela)
    fig_media_sel = criar_grafico_media_individual(analisador, pasta, config, conc, janela)
    fig_box = criar_grafico_boxplot(analisador, pasta, conc, janela)
    fig_deriv = criar_grafico_derivada(analisador, pasta, config, conc, janela)
    fig_amb_comb = criar_grafico_ambiental_combinado(analisador, pasta, config, conc, janela)
    fig_detalhe = criar_grafico_detalhado(analisador, pasta, config, conc, janela, gas_detalhe, sensor_detalhe)
    
    # --- TABELA MÉTRICAS (Aba 9) ---
    metricas_lista = []
    for gas in GAS_CODES:
        res = analisador.carregar_dados(pasta, gas, config, conc)
        if isinstance(res, tuple):
            _, df_media = res
            if df_media is not None:
                df_media = analisador.suavizar(df_media, janela)
                for s in SENSORES:
                    m = analisador.calcular_metricas_avancadas(df_media, s)
                    if m:
                        m['Gas'] = GAS_INFO[gas]
                        metricas_lista.append(m)
    
    if metricas_lista:
        df_metricas = pd.DataFrame(metricas_lista).round(4)
        cols = ['Gas', 'Sensor', 'V_baseline (V)', 'V_max Abs (V)', 'V_max Relativo (V)', 'Taxa Max (V/s)']
        df_metricas = df_metricas[cols]
        tabela = dash_table.DataTable(
            data=df_metricas.to_dict('records'),
            columns=[{"name": i, "id": i} for i in df_metricas.columns],
            style_table={'overflowX': 'auto'},
            style_cell={'textAlign': 'center', 'padding': '10px'},
            style_header={'backgroundColor': 'black', 'color': 'white', 'fontWeight': 'bold'},
            style_data_conditional=[{'if': {'row_index': 'odd'}, 'backgroundColor': 'rgb(248, 248, 248)'}]
        )
        disabled_dl = False
        data_store = df_metricas.to_dict('records')
    else:
        tabela = html.Div("Sem dados para calcular métricas.")
        disabled_dl = True
        data_store = None

    # --- NOVA LÓGICA: MATRIZES COMPARATIVAS (Aba 14) ---
    dict_dfs_matriz = analisador.gerar_tabelas_comparativas(pasta, conc, janela)
    elementos_matriz = []
    
    style_header = {'backgroundColor': '#2c3e50', 'color': 'white', 'fontWeight': 'bold', 'textAlign': 'center'}
    style_cell = {'textAlign': 'center', 'fontFamily': 'Arial', 'padding': '10px'}
    style_data_cond = [{'if': {'row_index': 'odd'}, 'backgroundColor': 'rgb(240, 240, 240)'}]

    for sensor in SENSORES:
        df_s = dict_dfs_matriz.get(sensor, pd.DataFrame())
        
        if not df_s.empty:
            tabela_matriz = dash_table.DataTable(
                data=df_s.to_dict('records'),
                columns=[{"name": i, "id": i} for i in df_s.columns],
                style_header=style_header,
                style_cell=style_cell,
                style_data_conditional=style_data_cond,
                style_table={'overflowX': 'auto', 'marginBottom': '40px'}
            )
            elementos_matriz.append(html.H4(f"Matriz de Comparação - Sensor {sensor}", className="text-center mb-2"))
            elementos_matriz.append(html.P(f"Valores de Vmáx Relativo (V) e Porcentagem vs Default ({conc} ppm)", className="text-center text-muted mb-3"))
            elementos_matriz.append(tabela_matriz)
            elementos_matriz.append(html.Hr())
    
    if not elementos_matriz:
        elementos_matriz = html.Div("Não foi possível gerar as matrizes (verifique se há dados).")

    # Finaliza cronômetro
    end_time = time.time()
    elapsed_time = end_time - start_time
    
    if history_data is None: history_data = []
    new_entry = {
        'time': elapsed_time,
        'rows': analisador.total_linhas_lidas,
        'files': analisador.total_arquivos_lidos
    }
    history_data.append(new_entry)
    
    fig_perf = criar_grafico_performance(history_data)

    return fig_v, fig_rel, fig_p, fig_t, fig_h, fig_aletas, fig_media_sel, fig_box, fig_deriv, fig_amb_comb, fig_detalhe, fig_perf, tabela, elementos_matriz, disabled_dl, data_store, history_data

# --- CALLBACKS DE DOWNLOAD ---
@app.callback(Output("download-file-1", "data"), Input("btn-download-metrics", "n_clicks"), State("store-metricas", "data"), prevent_initial_call=True)
def download_excel_metrics(n_clicks, data):
    if data: return dcc.send_data_frame(pd.DataFrame(data).to_excel, "metricas_config_atual.xlsx", index=False)

@app.callback(Output("download-file-2", "data"), Input("btn-download-matrix", "n_clicks"), State("pasta", "value"), State("conc", "value"), prevent_initial_call=True)
def download_matrix_excel(n_clicks, pasta, conc):
    analisador = AnalisadorGases()
    df = analisador.gerar_matriz_completa(pasta, conc)
    return dcc.send_data_frame(df.to_excel, "matriz_completa.xlsx", index=False)

if __name__ == '__main__':
    if not os.path.exists('dados/0'): os.makedirs('dados/0', exist_ok=True)
    app.run(debug=True, port=8055)