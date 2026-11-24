from flask import Flask, render_template
import pandas as pd
import plotly.graph_objs as go
from plotly.subplots import make_subplots
import plotly.offline as pyo
import numpy as np
from sklearn.linear_model import LinearRegression
from sklearn.metrics import r2_score

app = Flask(__name__)

def clean_series(series, z_thresh=3, min_valid=10):
    """
    Remove outliers (z-score) e valores abaixo de min_valid (ex: 0).
    """
    series = pd.to_numeric(series, errors='coerce')
    series = series.where(series > min_valid)  # remove zeros e negativos

    mean = series.mean()
    std = series.std()

    # remove pontos muito distantes da média
    series = series.where(abs((series - mean) / std) < z_thresh)

    # aplicar suavização com média móvel de 10 pontos
    series = series.rolling(window=10, min_periods=1, center=True).mean()

    return series

@app.route('/')
def index():
    try:
        df = pd.read_excel('BancoDeDados.xlsx')

        # Combinar data e hora corretamente
        df['datetime'] = pd.to_datetime(df['data'].astype(str) + " " + df['hora'].astype(str), errors='coerce')

        gases = ['Gás 1 - MQ-3', 'Gás 2 - MQ-5', 'Gás 3 - MQ-2']
        colors = ['red', 'green', 'blue']
        graph_htmls = []

        # --- LIMPAR E SUAVIZAR GASES ---
        for gas in gases:
            df[gas] = clean_series(df[gas], z_thresh=3, min_valid=10)

        # --- LIMPAR E SUAVIZAR TEMPERATURA E UMIDADE ---
        df['temperatura'] = clean_series(df['temperatura'], z_thresh=3, min_valid=0)
        df['umidade'] = clean_series(df['umidade'], z_thresh=3, min_valid=0)

        # Gráficos separados para cada variável (com médias móveis)
        window = 10
        for i, gas in enumerate(gases):
            y_gas = pd.to_numeric(df[gas], errors='coerce')
            y_ma = y_gas.rolling(window=window, min_periods=1, center=True).mean()
            fig_gas = go.Figure()
            fig_gas.add_trace(go.Scatter(
                x=df['datetime'],
                y=y_gas,
                name=f'{gas} (Original)',
                line=dict(color=colors[i], width=1)
            ))
            fig_gas.add_trace(go.Scatter(
                x=df['datetime'],
                y=y_ma,
                name=f'{gas} (Média Móvel {window})',
                line=dict(color=colors[i], width=3, dash='dash')
            ))
            fig_gas.update_layout(
                title=f"{gas}",
                xaxis_title="Tempo",
                yaxis_title="Concentração (ppm)",
                height=400
            )
            graph_htmls.append(pyo.plot(fig_gas, output_type='div', include_plotlyjs=False))

        # Temperatura (com média móvel)
        y_temp = pd.to_numeric(df['temperatura'], errors='coerce')
        y_temp_ma = y_temp.rolling(window=window, min_periods=1, center=True).mean()
        fig_temp = go.Figure()
        fig_temp.add_trace(go.Scatter(
            x=df['datetime'],
            y=y_temp,
            name='Temperatura (Original)',
            line=dict(color='orange', width=1)
        ))
        fig_temp.add_trace(go.Scatter(
            x=df['datetime'],
            y=y_temp_ma,
            name=f'Temperatura (Média Móvel {window})',
            line=dict(color='orange', width=3, dash='dash')
        ))
        fig_temp.update_layout(
            title="Temperatura",
            xaxis_title="Tempo",
            yaxis_title="Temperatura (°C)",
            height=400
        )
        graph_htmls.append(pyo.plot(fig_temp, output_type='div', include_plotlyjs=False))

        # Umidade (com média móvel)
        y_umid = pd.to_numeric(df['umidade'], errors='coerce')
        y_umid_ma = y_umid.rolling(window=window, min_periods=1, center=True).mean()
        fig_umid = go.Figure()
        fig_umid.add_trace(go.Scatter(
            x=df['datetime'],
            y=y_umid,
            name='Umidade (Original)',
            line=dict(color='purple', width=1)
        ))
        fig_umid.add_trace(go.Scatter(
            x=df['datetime'],
            y=y_umid_ma,
            name=f'Umidade (Média Móvel {window})',
            line=dict(color='purple', width=3, dash='dash')
        ))
        fig_umid.update_layout(
            title="Umidade",
            xaxis_title="Tempo",
            yaxis_title="Umidade (%)",
            height=400
        )
        graph_htmls.append(pyo.plot(fig_umid, output_type='div', include_plotlyjs=False))

        # Gráficos de regressão linear separados
        x_numeric_full = np.arange(len(df)).reshape(-1, 1)
        for i, gas in enumerate(gases):
            y_full = pd.to_numeric(df[gas], errors='coerce').values
            mask = ~np.isnan(y_full)
            x_numeric = x_numeric_full[mask]
            y = y_full[mask]
            x_datetime = df['datetime'][mask]
            model = LinearRegression()
            model.fit(x_numeric, y)
            trend = model.predict(x_numeric)
            r2 = r2_score(y, trend)
            fig_reg = go.Figure()
            fig_reg.add_trace(go.Scatter(
                x=x_datetime,
                y=y,
                name=gas,
                line=dict(color=colors[i])
            ))
            fig_reg.add_trace(go.Scatter(
                x=x_datetime,
                y=trend,
                name=f'Tendência {gas} (R²={r2:.3f})',
                line=dict(color=colors[i], dash='dash')
            ))
            fig_reg.update_layout(
                title=f"Regressão Linear - {gas}",
                xaxis_title="Tempo",
                yaxis_title="Concentração (ppm)",
                height=400
            )
            graph_htmls.append(pyo.plot(fig_reg, output_type='div', include_plotlyjs=False))

        # Temperatura regressão
        y_temp_full = pd.to_numeric(df['temperatura'], errors='coerce').values
        mask_temp = ~np.isnan(y_temp_full)
        x_numeric_temp = x_numeric_full[mask_temp]
        y_temp = y_temp_full[mask_temp]
        x_datetime_temp = df['datetime'][mask_temp]
        model_temp = LinearRegression()
        model_temp.fit(x_numeric_temp, y_temp)
        trend_temp = model_temp.predict(x_numeric_temp)
        r2_temp = r2_score(y_temp, trend_temp)
        fig_reg_temp = go.Figure()
        fig_reg_temp.add_trace(go.Scatter(
            x=x_datetime_temp,
            y=y_temp,
            name='Temperatura',
            line=dict(color='orange')
        ))
        fig_reg_temp.add_trace(go.Scatter(
            x=x_datetime_temp,
            y=trend_temp,
            name=f'Tendência Temperatura (R²={r2_temp:.3f})',
            line=dict(color='orange', dash='dash')
        ))
        fig_reg_temp.update_layout(
            title="Regressão Linear - Temperatura",
            xaxis_title="Tempo",
            yaxis_title="Temperatura (°C)",
            height=400
        )
        graph_htmls.append(pyo.plot(fig_reg_temp, output_type='div', include_plotlyjs=False))

        # Umidade regressão
        y_umid_full = pd.to_numeric(df['umidade'], errors='coerce').values
        mask_umid = ~np.isnan(y_umid_full)
        x_numeric_umid = x_numeric_full[mask_umid]
        y_umid = y_umid_full[mask_umid]
        x_datetime_umid = df['datetime'][mask_umid]
        model_umid = LinearRegression()
        model_umid.fit(x_numeric_umid, y_umid)
        trend_umid = model_umid.predict(x_numeric_umid)
        r2_umid = r2_score(y_umid, trend_umid)
        fig_reg_umid = go.Figure()
        fig_reg_umid.add_trace(go.Scatter(
            x=x_datetime_umid,
            y=y_umid,
            name='Umidade',
            line=dict(color='purple')
        ))
        fig_reg_umid.add_trace(go.Scatter(
            x=x_datetime_umid,
            y=trend_umid,
            name=f'Tendência Umidade (R²={r2_umid:.3f})',
            line=dict(color='purple', dash='dash')
        ))
        fig_reg_umid.update_layout(
            title="Regressão Linear - Umidade",
            xaxis_title="Tempo",
            yaxis_title="Umidade (%)",
            height=400
        )
        graph_htmls.append(pyo.plot(fig_reg_umid, output_type='div', include_plotlyjs=False))

        # Renderizar todos os gráficos
        return render_template('index.html', graph_htmls=graph_htmls)
    
    except Exception as e:
        return f"Erro ao processar os dados: {str(e)}"

if __name__ == '__main__':
    app.run(debug=True)