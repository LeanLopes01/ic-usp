"""
Dashboard IoT - Monitoramento de Qualidade do Ar
=================================================
Arquitetura modular com:
  - Análise individual por sensor (MQ135, MQ9, MQ5)
  - Índice combinado de qualidade do ar (AQI simplificado)
  - Detecção de anomalias com Isolation Forest
  - Pipeline pronto para BME280 (compensação T/H/P)
  - Filtros robustos de ruído (IQR + Savitzky-Golay)
"""

from flask import Flask, request, jsonify
import mysql.connector
import plotly.graph_objs as go
from plotly.subplots import make_subplots
from plotly.io import to_html
import numpy as np
import pandas as pd
from scipy.signal import savgol_filter
from sklearn.decomposition import PCA
from sklearn.preprocessing import StandardScaler
from sklearn.ensemble import IsolationForest
import warnings
warnings.filterwarnings('ignore')

app = Flask(__name__)

# ─────────────────────────────────────────────
# CONFIGURAÇÃO DOS SENSORES
# ─────────────────────────────────────────────
SENSORES = {
    'mq135': {
        'tabela':  'sensor_mq135',
        'nome':    'MQ-135 (VP)',
        'cor':     '#ff4757',
        'gas':     'CO₂ / NH₃ / Benzeno',
        # Limiares de razão Rs/R0 para ar limpo e poluição
        'ratio_clean':  3.6,
        'ratio_warn':   2.0,
        'ratio_alert':  1.0,
        # PPM: verde / amarelo / vermelho
        'ppm_ok':   1000,
        'ppm_warn': 2000,
        # Fator de sensibilidade à umidade (para futura compensação BME280)
        'humidity_factor': 0.012,   # % de erro por % RH
        'temp_factor':     0.008,   # % de erro por °C acima de 20°C
    },
    'mq9': {
        'tabela':  'sensor_mq9',
        'nome':    'MQ-9 (VN)',
        'cor':     '#2ed573',
        'gas':     'CO / Gás Combustível / Metano',
        'ratio_clean':  9.8,
        'ratio_warn':   5.0,
        'ratio_alert':  2.5,
        'ppm_ok':   200,
        'ppm_warn': 500,
        'humidity_factor': 0.010,
        'temp_factor':     0.006,
    },
    'mq5': {
        'tabela':  'sensor_mq5',
        'nome':    'MQ-5 (34)',
        'cor':     '#1e90ff',
        'gas':     'GLP / Propano / Butano / Metano',
        'ratio_clean':  6.5,
        'ratio_warn':   3.5,
        'ratio_alert':  1.8,
        'ppm_ok':   300,
        'ppm_warn': 700,
        'humidity_factor': 0.009,
        'temp_factor':     0.007,
    },
}

UNIDADES = {
    'ppm':    ('Concentração', 'PPM'),
    'tensao': ('Tensão', 'V'),
    'rs':     ('Resistência', 'Ω'),
    'ratio':  ('Razão Rs/R0', ''),
}

DB_CONFIG = dict(
    host="localhost", user="esp32_user",
    password=os.getenv("DB_PASSWORD", "your-db-password"), database="ic_teste"
)

JANELA_HORAS = 24


# ─────────────────────────────────────────────
# UTILITÁRIOS DE DADOS
# ─────────────────────────────────────────────

def conectar_db():
    return mysql.connector.connect(**DB_CONFIG)


def carregar_sensor(db, key: str, metrica: str) -> pd.DataFrame:
    """Carrega dados de um sensor das últimas JANELA_HORAS horas."""
    cfg = SENSORES[key]
    query = f"""
        SELECT data_hora, {metrica} AS valor
        FROM {cfg['tabela']}
        WHERE data_hora >= NOW() - INTERVAL {JANELA_HORAS} HOUR
        ORDER BY data_hora
    """
    df = pd.read_sql(query, db, parse_dates=['data_hora'])
    df.set_index('data_hora', inplace=True)
    return df


def carregar_todos(db, metrica: str) -> dict[str, pd.Series]:
    """Retorna um dict {sensor_key: pd.Series} com dados limpos."""
    result = {}
    for key in SENSORES:
        df = carregar_sensor(db, key, metrica)
        if not df.empty:
            s = pd.to_numeric(df['valor'], errors='coerce')
            result[key] = s
    return result


def limpar_serie(s: pd.Series,
                 zero_como_nan: bool = True,
                 iqr_fator: float = 3.0) -> pd.Series:
    """
    Pipeline de limpeza robusto:
      1. Zeros → NaN (opcional)
      2. Remoção de outliers por IQR
      3. Interpolação linear
      4. Suavização Savitzky-Golay (preserva picos reais)
    """
    s = s.copy()

    if zero_como_nan:
        s.replace(0.0, np.nan, inplace=True)

    # Outliers por IQR
    q1, q3 = s.quantile(0.25), s.quantile(0.75)
    iqr = q3 - q1
    lower, upper = q1 - iqr_fator * iqr, q3 + iqr_fator * iqr
    s[(s < lower) | (s > upper)] = np.nan

    # Interpola lacunas ≤ 10 pontos
    s = s.interpolate(method='time', limit=10)

    # Savitzky-Golay: só se houver dados suficientes
    vals = s.dropna().values
    if len(vals) >= 11:
        try:
            suave = savgol_filter(vals, window_length=11, polyorder=3)
            s_suave = pd.Series(suave, index=s.dropna().index)
            s = s_suave.reindex(s.index)
        except Exception:
            pass

    return s


def reamostrar_e_limpar(raw: dict[str, pd.Series],
                        freq: str = '5min') -> pd.DataFrame:
    """
    Une todas as séries, reamostra e limpa.
    Retorna DataFrame com colunas = chaves dos sensores.
    """
    frames = {}
    for key, s in raw.items():
        s_clean = limpar_serie(s)
        frames[key] = s_clean.resample(freq).mean()

    df = pd.DataFrame(frames)
    df.dropna(how='all', inplace=True)
    return df


def estatisticas(s: pd.Series) -> dict:
    """Calcula estatísticas descritivas para exibição."""
    clean = s.dropna()
    if clean.empty:
        return {}
    return {
        'min':    round(float(clean.min()), 3),
        'max':    round(float(clean.max()), 3),
        'mean':   round(float(clean.mean()), 3),
        'median': round(float(clean.median()), 3),
        'std':    round(float(clean.std()), 3),
        'p95':    round(float(clean.quantile(0.95)), 3),
        'count':  int(clean.count()),
    }


# ─────────────────────────────────────────────
# COMPENSAÇÃO BME280 (placeholder ativável)
# ─────────────────────────────────────────────

def compensar_bme280(df: pd.DataFrame,
                     temp: float | None = None,
                     umidade: float | None = None,
                     pressao: float | None = None) -> pd.DataFrame:
    """
    Aplica fatores de compensação ambiental nas leituras PPM.
    
    Ativado automaticamente quando o BME280 estiver integrado.
    Referência: T=20°C, RH=65%, P=101325 Pa (condições de calibração típicas)
    
    Para ativar: passe os valores reais de T, RH e P lidos do BME280.
    """
    if temp is None or umidade is None:
        return df  # sem compensação enquanto BME280 não estiver disponível

    df_comp = df.copy()
    ref_temp, ref_rh = 20.0, 65.0

    for key, cfg in SENSORES.items():
        if key in df_comp.columns:
            delta_t  = temp    - ref_temp
            delta_rh = umidade - ref_rh
            fator_t  = 1.0 + cfg['temp_factor']     * delta_t
            fator_rh = 1.0 + cfg['humidity_factor'] * delta_rh
            # Pressão: compensação simples por altitude (ideal ~1 atm)
            fator_p  = (pressao / 101325.0) if pressao else 1.0
            df_comp[key] = df_comp[key] / (fator_t * fator_rh * fator_p)

    return df_comp


# ─────────────────────────────────────────────
# CÁLCULO DO ÍNDICE DE QUALIDADE DO AR (AQI)
# ─────────────────────────────────────────────

def calcular_aqi_simplificado(df: pd.DataFrame) -> pd.Series:
    """
    Índice composto 0–100 baseado na normalização ponderada dos sensores.
    
    Pesos refletem a severidade típica de cada gás detectado:
      MQ135 (CO₂/NH₃): 40%
      MQ9  (CO/CH4):   35%
      MQ5  (GLP/GN):   25%
    
    0–30  → 🟢 Bom
    31–60 → 🟡 Moderado
    61–80 → 🟠 Ruim
    81–100 → 🔴 Perigoso
    """
    pesos = {'mq135': 0.40, 'mq9': 0.35, 'mq5': 0.25}
    aqi = pd.Series(0.0, index=df.index)

    for key, peso in pesos.items():
        if key not in df.columns:
            continue
        cfg = SENSORES[key]
        s = df[key].fillna(0)
        # Normaliza PPM: 0=limpo, 100=nível crítico
        critico = cfg['ppm_warn'] * 2
        norm = (s / critico).clip(0, 1) * 100
        aqi += norm * peso

    return aqi.clip(0, 100)


def nivel_aqi(valor: float) -> tuple[str, str]:
    """Retorna (label, cor) para um valor de AQI."""
    if valor <= 30:
        return 'Bom', '#2ed573'
    elif valor <= 60:
        return 'Moderado', '#ffa502'
    elif valor <= 80:
        return 'Ruim', '#ff6b35'
    else:
        return 'Perigoso', '#ff4757'


# ─────────────────────────────────────────────
# DETECÇÃO DE ANOMALIAS (ML)
# ─────────────────────────────────────────────

def detectar_anomalias(df: pd.DataFrame,
                       contaminacao: float = 0.05) -> pd.Series:
    """
    Usa Isolation Forest para identificar leituras anômalas.
    
    Retorna uma Series booleana: True = ponto anômalo.
    Isso pode ser treinado offline com dados rotulados no futuro
    (ex: eventos conhecidos de poluição com timestamp).
    
    contaminacao: proporção esperada de outliers (0.01–0.2)
    """
    cols_disponiveis = [c for c in SENSORES if c in df.columns]
    df_ml = df[cols_disponiveis].fillna(df[cols_disponiveis].mean())

    if df_ml.shape[0] < 20 or df_ml.shape[1] < 1:
        return pd.Series(False, index=df.index)

    scaler = StandardScaler()
    X = scaler.fit_transform(df_ml.values)

    iso = IsolationForest(
        n_estimators=100,
        contamination=contaminacao,
        random_state=42,
        n_jobs=-1
    )
    preds = iso.fit_predict(X)  # -1 = anomalia, 1 = normal
    return pd.Series(preds == -1, index=df.index)


# ─────────────────────────────────────────────
# LAYOUT BASE PLOTLY
# ─────────────────────────────────────────────

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


def aplicar_grid_escuro(fig, rows=1, cols=1):
    """Aplica estilo de grid discreto a todos os eixos."""
    for r in range(1, rows + 1):
        for c in range(1, cols + 1):
            fig.update_xaxes(
                gridcolor='rgba(255,255,255,0.06)',
                linecolor='rgba(255,255,255,0.1)',
                tickfont=dict(color='#64748b'),
                row=r, col=c
            )
            fig.update_yaxes(
                gridcolor='rgba(255,255,255,0.06)',
                linecolor='rgba(255,255,255,0.1)',
                tickfont=dict(color='#64748b'),
                row=r, col=c
            )


# ─────────────────────────────────────────────
# GRÁFICOS INDIVIDUAIS POR SENSOR
# ─────────────────────────────────────────────

def grafico_sensor_individual(df_clean: pd.DataFrame,
                               sensor_key: str,
                               metrica: str,
                               tipo: str) -> go.Figure:
    """
    Análise aprofundada de um único sensor.
    tipos disponíveis: 'serie', 'distribuicao', 'estatisticas', 'anomalias'
    """
    cfg = SENSORES[sensor_key]
    cor = cfg['cor']
    label_y, unidade = UNIDADES.get(metrica, ('Valor', ''))

    s = df_clean[sensor_key] if sensor_key in df_clean.columns else pd.Series(dtype=float)
    stats = estatisticas(s)

    if tipo == 'serie':
        # ── Série temporal com banda de confiança ──
        fig = go.Figure()

        if not s.empty:
            # Banda: média móvel ± 1σ
            roll_mean = s.rolling(6, center=True).mean()
            roll_std  = s.rolling(6, center=True).std().fillna(0)

            fig.add_trace(go.Scatter(
                x=pd.concat([s.index.to_series(), s.index.to_series()[::-1]]),
                y=pd.concat([(roll_mean + roll_std), (roll_mean - roll_std)[::-1]]),
                fill='toself',
                fillcolor=cor.replace(')', ', 0.12)').replace('rgb', 'rgba').replace('#', 'rgba(').replace('ff4757', '255,71,87,').replace('2ed573', '46,213,115,').replace('1e90ff', '30,144,255,'),
                line_color='rgba(0,0,0,0)',
                name='Banda ±1σ',
                showlegend=True,
                hoverinfo='skip'
            ))

            fig.add_trace(go.Scatter(
                x=s.index, y=s,
                mode='lines',
                line=dict(color=cor, width=2, shape='spline'),
                name=cfg['nome'],
                opacity=0.4
            ))

            fig.add_trace(go.Scatter(
                x=roll_mean.index, y=roll_mean,
                mode='lines',
                line=dict(color=cor, width=3, shape='spline'),
                name='Média Móvel',
            ))

            # Linha de referência (média geral)
            media = s.mean()
            fig.add_hline(
                y=media,
                line_dash='dot',
                line_color='#64748b',
                annotation_text=f'Média: {media:.2f} {unidade}',
                annotation_font_color='#94a3b8'
            )

            # Alerta PPM se aplicável
            if metrica == 'ppm':
                fig.add_hrect(y0=0, y1=cfg['ppm_ok'],
                               fillcolor='green', opacity=0.07, line_width=0)
                fig.add_hrect(y0=cfg['ppm_ok'], y1=cfg['ppm_warn'],
                               fillcolor='yellow', opacity=0.07, line_width=0)
                fig.add_hrect(y0=cfg['ppm_warn'], y1=cfg['ppm_warn']*3,
                               fillcolor='red', opacity=0.07, line_width=0)

        fig.update_layout(
            **LAYOUT_BASE,
            title=f'{cfg["nome"]} — Série Temporal | {label_y}',
            yaxis_title=f'{label_y} ({unidade})' if unidade else label_y,
            height=480
        )
        aplicar_grid_escuro(fig)
        return fig

    elif tipo == 'distribuicao':
        # ── Distribuição: histograma + box + violin ──
        fig = make_subplots(
            rows=1, cols=2,
            column_widths=[0.65, 0.35],
            subplot_titles=['Distribuição (Histograma + KDE)', 'Box Plot']
        )

        if not s.empty:
            dados = s.dropna().values

            # Histograma
            fig.add_trace(go.Histogram(
                x=dados,
                nbinsx=40,
                marker_color=cor,
                opacity=0.7,
                name='Frequência',
            ), row=1, col=1)

            # Linha KDE aproximada com curva de densidade
            from scipy.stats import gaussian_kde
            try:
                kde = gaussian_kde(dados)
                x_range = np.linspace(dados.min(), dados.max(), 200)
                densidade = kde(x_range) * len(dados) * (dados.max() - dados.min()) / 40
                fig.add_trace(go.Scatter(
                    x=x_range, y=densidade,
                    mode='lines',
                    line=dict(color='white', width=2),
                    name='KDE',
                ), row=1, col=1)
            except Exception:
                pass

            # Box plot
            fig.add_trace(go.Box(
                y=dados,
                marker_color=cor,
                line_color=cor,
                name=cfg['nome'],
                boxmean='sd',
                fillcolor=cor.replace('#', 'rgba(').replace('ff4757', '255,71,87,0.3)').replace('2ed573', '46,213,115,0.3)').replace('1e90ff', '30,144,255,0.3)'),
            ), row=1, col=2)

        fig.update_layout(
            **LAYOUT_BASE,
            title=f'{cfg["nome"]} — Distribuição Estatística',
            height=440
        )
        aplicar_grid_escuro(fig, rows=1, cols=2)
        return fig

    elif tipo == 'estatisticas':
        # ── Painel de estatísticas: gauge + tabela ──
        fig = make_subplots(
            rows=1, cols=2,
            specs=[[{'type': 'indicator'}, {'type': 'table'}]],
            column_widths=[0.4, 0.6]
        )

        ultimo = float(s.dropna().iloc[-1]) if not s.dropna().empty else 0

        # Gauge do último valor
        max_gauge = stats.get('p95', 1) * 1.5 if stats else 1
        fig.add_trace(go.Indicator(
            mode='gauge+number+delta',
            value=ultimo,
            delta={'reference': stats.get('mean', 0), 'valueformat': '.2f'},
            title={'text': f'{cfg["nome"]}<br><span style="font-size:0.8em;color:#64748b">{cfg["gas"]}</span>'},
            gauge={
                'axis': {'range': [0, max_gauge], 'tickcolor': '#64748b'},
                'bar':  {'color': cor},
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

        # Tabela de estatísticas
        if stats:
            fig.add_trace(go.Table(
                header=dict(
                    values=['<b>Estatística</b>', f'<b>{label_y} ({unidade})</b>'],
                    fill_color='#1e293b',
                    font=dict(color='#38bdf8', size=13),
                    line_color='#334155',
                    align='left'
                ),
                cells=dict(
                    values=[
                        ['Mínimo', 'Máximo', 'Média', 'Mediana', 'Desvio Padrão', 'Percentil 95', 'Nº de Leituras'],
                        [stats['min'], stats['max'], stats['mean'],
                         stats['median'], stats['std'], stats['p95'], stats['count']]
                    ],
                    fill_color=['#0f172a', '#0f172a'],
                    font=dict(color=['#94a3b8', 'white'], size=12),
                    line_color='#334155',
                    align='left'
                )
            ), row=1, col=2)

        fig.update_layout(
            **LAYOUT_BASE,
            title=f'{cfg["nome"]} — Painel Estatístico',
            height=420
        )
        return fig

    elif tipo == 'anomalias':
        # ── Detecção de anomalias no sensor individual ──
        fig = go.Figure()

        if not s.empty:
            df_tmp = df_clean[[sensor_key]].rename(columns={sensor_key: 'mq135'})
            # Usa só esse sensor para o IsolationForest individual
            df_tmp2 = pd.DataFrame({'mq135': s})
            anomalias = detectar_anomalias(df_tmp2.rename(columns={'mq135': sensor_key})
                                            .assign(**{k: df_clean[k] for k in df_clean.columns if k != sensor_key and k in SENSORES}))

            normais = s[~anomalias]
            alertas = s[anomalias]

            fig.add_trace(go.Scatter(
                x=normais.index, y=normais,
                mode='lines+markers',
                marker=dict(size=4, color=cor, opacity=0.6),
                line=dict(color=cor, width=2),
                name='Normal'
            ))

            if not alertas.empty:
                fig.add_trace(go.Scatter(
                    x=alertas.index, y=alertas,
                    mode='markers',
                    marker=dict(
                        size=12, color='#ff4757',
                        symbol='x',
                        line=dict(width=2, color='white')
                    ),
                    name=f'Anomalia ({len(alertas)} pontos)'
                ))

        fig.update_layout(
            **LAYOUT_BASE,
            title=f'{cfg["nome"]} — Detecção de Anomalias (Isolation Forest)',
            yaxis_title=f'{label_y} ({unidade})' if unidade else label_y,
            height=460
        )
        aplicar_grid_escuro(fig)
        return fig

    return go.Figure()


# ─────────────────────────────────────────────
# GRÁFICO COMBINADO (AQI + CORRELAÇÃO + PCA)
# ─────────────────────────────────────────────

def grafico_combinado(df_clean: pd.DataFrame,
                       metrica: str,
                       tipo: str) -> go.Figure:

    label_y, unidade = UNIDADES.get(metrica, ('Valor', ''))

    if tipo == 'aqi':
        # ── Índice de Qualidade do Ar no tempo ──
        fig = make_subplots(
            rows=2, cols=1,
            row_heights=[0.45, 0.55],
            subplot_titles=['AQI Simplificado (0–100)', 'Leituras dos 3 Sensores'],
            vertical_spacing=0.12
        )

        aqi = calcular_aqi_simplificado(df_clean)
        anomalias = detectar_anomalias(df_clean)

        # Coloração dinâmica do AQI
        cores_aqi = [nivel_aqi(v)[1] for v in aqi.fillna(0)]

        fig.add_trace(go.Scatter(
            x=aqi.index, y=aqi,
            mode='lines',
            line=dict(color='white', width=0.5),
            showlegend=False, hoverinfo='skip'
        ), row=1, col=1)

        fig.add_trace(go.Scatter(
            x=aqi.index, y=aqi,
            mode='markers+lines',
            marker=dict(color=cores_aqi, size=7, opacity=0.9),
            line=dict(color='rgba(255,255,255,0.2)', width=1),
            name='AQI',
            customdata=[[nivel_aqi(v)[0]] for v in aqi.fillna(0)],
            hovertemplate='<b>AQI: %{y:.1f}</b> — %{customdata[0]}<extra></extra>'
        ), row=1, col=1)

        # Faixas do AQI
        for (y0, y1, cor, rot) in [
            (0,  30,  '#2ed573', 'Bom'),
            (30, 60,  '#ffa502', 'Moderado'),
            (60, 80,  '#ff6b35', 'Ruim'),
            (80, 100, '#ff4757', 'Perigoso'),
        ]:
            fig.add_hrect(y0=y0, y1=y1, fillcolor=cor,
                           opacity=0.07, line_width=0, row=1, col=1)

        fig.update_yaxes(range=[0, 100], row=1, col=1)

        # Série de cada sensor
        for key, cfg in SENSORES.items():
            if key in df_clean.columns:
                s = df_clean[key]
                # Marca anomalias
                s_normal  = s.where(~anomalias)
                s_anomalo = s.where(anomalias)

                fig.add_trace(go.Scatter(
                    x=s_normal.index, y=s_normal,
                    mode='lines',
                    line=dict(color=cfg['cor'], width=2, shape='spline'),
                    name=cfg['nome']
                ), row=2, col=1)

                if s_anomalo.dropna().any():
                    fig.add_trace(go.Scatter(
                        x=s_anomalo.index, y=s_anomalo,
                        mode='markers',
                        marker=dict(size=10, color='white', symbol='x',
                                    line=dict(color=cfg['cor'], width=2)),
                        name=f'Anomalia {cfg["nome"]}',
                        showlegend=False
                    ), row=2, col=1)

        fig.update_yaxes(
            title_text=f'{label_y} ({unidade})' if unidade else label_y,
            row=2, col=1
        )
        fig.update_layout(
            **LAYOUT_BASE,
            title='Painel Combinado — AQI + Monitoramento + Anomalias',
            height=720
        )
        aplicar_grid_escuro(fig, rows=2, cols=1)
        return fig

    elif tipo == 'correlacao':
        # ── Matriz de Correlação + Scatter Matrix ──
        cols = [k for k in SENSORES if k in df_clean.columns]
        df_plot = df_clean[cols].dropna()

        if df_plot.empty or len(cols) < 2:
            fig = go.Figure()
            fig.update_layout(**LAYOUT_BASE, title='Dados insuficientes')
            return fig

        # Correlação de Pearson
        corr = df_plot.corr()
        nomes = [SENSORES[k]['nome'] for k in cols]

        fig = make_subplots(
            rows=1, cols=2,
            subplot_titles=['Matriz de Correlação', 'Dispersão MQ135 vs MQ9'],
            column_widths=[0.45, 0.55]
        )

        # Heatmap de correlação
        fig.add_trace(go.Heatmap(
            z=corr.values,
            x=nomes, y=nomes,
            colorscale='RdBu',
            zmid=0,
            text=corr.round(3).values,
            texttemplate='%{text}',
            showscale=True,
            colorbar=dict(x=0.42, thickness=15)
        ), row=1, col=1)

        # Scatter
        x_key, y_key = cols[0], cols[1] if len(cols) > 1 else cols[0]
        aqi = calcular_aqi_simplificado(df_clean)
        fig.add_trace(go.Scatter(
            x=df_plot[x_key], y=df_plot[y_key],
            mode='markers',
            marker=dict(
                size=6,
                color=aqi.reindex(df_plot.index).fillna(50),
                colorscale='RdYlGn_r',
                showscale=False,
                opacity=0.7
            ),
            name='Amostras',
            hovertemplate=(
                f'{SENSORES[x_key]["nome"]}: %{{x:.2f}}<br>'
                f'{SENSORES[y_key]["nome"]}: %{{y:.2f}}<extra></extra>'
            )
        ), row=1, col=2)

        fig.update_xaxes(title_text=SENSORES[x_key]['nome'], row=1, col=2)
        fig.update_yaxes(title_text=SENSORES[y_key]['nome'], row=1, col=2)

        fig.update_layout(
            **LAYOUT_BASE,
            title='Correlação entre Sensores',
            height=500
        )
        aplicar_grid_escuro(fig, rows=1, cols=2)
        return fig

    elif tipo == 'pca':
        # ── PCA: trajetória no espaço reduzido ──
        cols = [k for k in SENSORES if k in df_clean.columns]
        df_pca = df_clean[cols].dropna()

        fig = go.Figure()

        if len(df_pca) > 5 and len(cols) >= 2:
            X = StandardScaler().fit_transform(df_pca.values)
            pca = PCA(n_components=2)
            comp = pca.fit_transform(X)
            aqi = calcular_aqi_simplificado(df_clean).reindex(df_pca.index).fillna(50)

            var_exp = pca.explained_variance_ratio_ * 100

            textos = [
                f'⏱ {t.strftime("%H:%M")}<br>'
                + '<br>'.join([f'{SENSORES[k]["nome"]}: {df_pca[k].iloc[i]:.2f}' for i, k in enumerate(cols)])
                for i, t in enumerate(df_pca.index)
            ]

            fig.add_trace(go.Scatter(
                x=comp[:, 0], y=comp[:, 1],
                mode='lines+markers',
                text=textos,
                hoverinfo='text',
                line=dict(color='rgba(148,163,184,0.25)', width=1),
                marker=dict(
                    size=9,
                    color=aqi.values,
                    colorscale='RdYlGn_r',
                    cmin=0, cmax=100,
                    showscale=True,
                    colorbar=dict(
                        title='AQI',
                        tickvals=[0, 30, 60, 80, 100],
                        ticktext=['Bom', 'Moderado', '', 'Ruim', 'Perigoso']
                    ),
                    line=dict(width=1, color='rgba(255,255,255,0.3)')
                ),
                name='Trajetória'
            ))

            fig.update_layout(
                **LAYOUT_BASE,
                title=f'PCA — Espaço de Padrões | PC1: {var_exp[0]:.1f}% | PC2: {var_exp[1]:.1f}% variância explicada',
                xaxis_title=f'PC1 ({var_exp[0]:.1f}%)',
                yaxis_title=f'PC2 ({var_exp[1]:.1f}%)',
                height=520
            )
        else:
            fig.update_layout(**LAYOUT_BASE, title='Dados insuficientes para PCA')

        aplicar_grid_escuro(fig)
        return fig

    elif tipo == 'derivada':
        # ── Taxa de variação (derivada) ──
        fig = go.Figure()
        for key, cfg in SENSORES.items():
            if key in df_clean.columns:
                s = df_clean[key].fillna(method='ffill').fillna(0)
                deriv = np.gradient(s.values)
                fig.add_trace(go.Scatter(
                    x=df_clean.index,
                    y=deriv,
                    mode='lines',
                    line=dict(color=cfg['cor'], width=2, shape='spline'),
                    name=f'd/dt {cfg["nome"]}'
                ))

        fig.add_hline(y=0, line_dash='dash', line_color='#64748b', opacity=0.5)
        fig.update_layout(
            **LAYOUT_BASE,
            title=f'Taxa de Variação (d/dt) — {label_y}',
            yaxis_title='Variação por intervalo',
            height=460
        )
        aplicar_grid_escuro(fig)
        return fig

    return go.Figure()


# ─────────────────────────────────────────────
# ENDPOINT PRINCIPAL
# ─────────────────────────────────────────────

@app.route('/', strict_slashes=False)
@app.route('/grafico', strict_slashes=False)
@app.route('/api/grafico', strict_slashes=False)
def gerar_grafico():
    try:
        tipo_grafico = request.args.get('tipo', 'aqi')
        metrica = request.args.get('metrica', 'ppm')
        sensor  = request.args.get('sensor', None)   # 'mq135', 'mq9', 'mq5' ou None (todos)

        # Parâmetros BME280 opcionais (passados via query string quando disponíveis)
        temp    = request.args.get('temp',    type=float)
        umidade = request.args.get('umidade', type=float)
        pressao = request.args.get('pressao', type=float)

        db = conectar_db()
        raw = carregar_todos(db, metrica)
        db.close()

        if not raw:
            return "<h2 style='color:#94a3b8;font-family:sans-serif;padding:20px'>⏳ Aguardando dados no banco...</h2>"

        df_clean = reamostrar_e_limpar(raw, freq='5min')

        # Aplicar compensação BME280 se disponível
        if metrica == 'ppm' and temp is not None:
            df_clean = compensar_bme280(df_clean, temp, umidade, pressao)

        if df_clean.empty:
            return "<h2 style='color:#94a3b8;font-family:sans-serif;padding:20px'>⚠️ Dados insuficientes após filtragem.</h2>"

        # ── Roteia para o gráfico correto ──
        if sensor and sensor in SENSORES:
            # Análise individual
            fig = grafico_sensor_individual(df_clean, sensor, metrica, tipo_grafico)
        else:
            # Análise combinada
            fig = grafico_combinado(df_clean, metrica, tipo_grafico)

        html_grafico = to_html(fig, full_html=False, include_plotlyjs='cdn')
        refresh_url  = f'/api/grafico/?tipo={tipo_grafico}&metrica={metrica}' + (f'&sensor={sensor}' if sensor else '')

        return f"""<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8">
  <meta http-equiv="refresh" content="300; url={refresh_url}">
  <style>
    html, body {{ margin:0; padding:0; background:transparent; color:white;
                  font-family:'Segoe UI',sans-serif; }}
  </style>
</head>
<body>{html_grafico}</body>
</html>"""

    except Exception as e:
        import traceback
        return f"<pre style='color:#ff4757;padding:20px'>{traceback.format_exc()}</pre>", 500


# ─────────────────────────────────────────────
# ENDPOINT DE API (JSON) — para futura integração
# ─────────────────────────────────────────────

@app.route('/api/status', strict_slashes=False)
def status():
    """Retorna o status atual de cada sensor em JSON."""
    try:
        db = conectar_db()
        raw = carregar_todos(db, 'ppm')
        db.close()

        df = reamostrar_e_limpar(raw)
        aqi = calcular_aqi_simplificado(df)

        ultimo_aqi   = float(aqi.dropna().iloc[-1]) if not aqi.dropna().empty else 0
        nivel, cor   = nivel_aqi(ultimo_aqi)
        anomalias    = detectar_anomalias(df)
        n_anomalias  = int(anomalias.sum())

        sensores_status = {}
        for key in SENSORES:
            if key in df.columns:
                s = df[key].dropna()
                sensores_status[key] = {
                    'ultimo':  round(float(s.iloc[-1]), 2) if not s.empty else None,
                    'media':   round(float(s.mean()), 2)   if not s.empty else None,
                    'stats':   estatisticas(s),
                }

        return jsonify({
            'aqi': {'valor': round(ultimo_aqi, 1), 'nivel': nivel, 'cor': cor},
            'anomalias': n_anomalias,
            'sensores': sensores_status,
            'bme280_conectado': False,  # Atualizar quando BME280 estiver ativo
        })
    except Exception as e:
        return jsonify({'erro': str(e)}), 500


if __name__ == '__main__':
    app.run(host='0.0.0.0', port=5000, debug=False)