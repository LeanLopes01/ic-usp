import sys
import gspread
import pandas as pd
import plotly.graph_objects as go
from PyQt6.QtWidgets import QApplication, QVBoxLayout, QWidget
from PyQt6.QtWebEngineWidgets import QWebEngineView
from oauth2client.service_account import ServiceAccountCredentials
import threading
import time

# Configuração da conexão com a planilha do Google
SCOPE = ["https://spreadsheets.google.com/feeds", "https://www.googleapis.com/auth/drive"]
CREDENTIALS_FILE = "credenciais.json"  # Substitua pelo caminho correto
SPREADSHEET_NAME = "1fUToK1SlJb-rn6Pr5Ceqedi2DqVyVmpNYSAM9I2BpgU"  # Substitua pelo nome da sua planilha

credentials = ServiceAccountCredentials.from_json_keyfile_name(CREDENTIALS_FILE, SCOPE)
gc = gspread.authorize(credentials)
sheet = gc.open(SPREADSHEET_NAME).sheet1

class RealTimePlot(QWidget):
    def __init__(self):
        super().__init__()
        self.initUI()
        self.update_thread = threading.Thread(target=self.update_data, daemon=True)
        self.update_thread.start()

    def initUI(self):
        self.layout = QVBoxLayout()
        self.graph_temp = QWebEngineView()
        self.graph_hum = QWebEngineView()
        self.graph_gas = QWebEngineView()
        
        self.layout.addWidget(self.graph_temp)
        self.layout.addWidget(self.graph_hum)
        self.layout.addWidget(self.graph_gas)
        self.setLayout(self.layout)
        
        self.update_charts()

    def fetch_data(self):
        data = sheet.get_all_records()
        return pd.DataFrame(data)

    def update_charts(self):
        df = self.fetch_data()
        
        if df.empty:
            return
        
        fig_temp = go.Figure([go.Scatter(x=df['Horario'], y=df['Temperatura'], mode='lines+markers', name='Temperatura')])
        fig_temp.update_layout(title='Temperatura por Tempo', xaxis_title='Horário', yaxis_title='Temperatura (°C)')
        
        fig_hum = go.Figure([go.Scatter(x=df['Horario'], y=df['Umidade'], mode='lines+markers', name='Umidade')])
        fig_hum.update_layout(title='Umidade por Tempo', xaxis_title='Horário', yaxis_title='Umidade (%)')
        
        fig_gas = go.Figure([go.Scatter(x=df['Horario'], y=df['Gas'], mode='lines+markers', name='Gás')])
        fig_gas.update_layout(title='Concentração de Gás por Tempo', xaxis_title='Horário', yaxis_title='Concentração de Gás (ppm)')
        
        self.graph_temp.setHtml(fig_temp.to_html(include_plotlyjs='cdn'))
        self.graph_hum.setHtml(fig_hum.to_html(include_plotlyjs='cdn'))
        self.graph_gas.setHtml(fig_gas.to_html(include_plotlyjs='cdn'))

    def update_data(self):
        while True:
            self.update_charts()
            time.sleep(5)  # Atualiza a cada 5 segundos

if __name__ == "__main__":
    app = QApplication(sys.argv)
    window = RealTimePlot()
    window.show()
    sys.exit(app.exec())
