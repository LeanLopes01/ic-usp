import sqlite3
import random
from datetime import datetime, timedelta

def create_mock_database(db_name="sensores_IC.sqlite", num_records=1000):
    # 1. Conecta (ou cria) o arquivo do banco de dados
    conn = sqlite3.connect(db_name)
    cursor = conn.cursor()

    # 2. Cria a tabela com a estrutura dos sensores
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS leituras_sensores (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp DATETIME,
            mq3_adc INTEGER,
            mq9_adc INTEGER,
            mq135_adc INTEGER,
            mq2_adc INTEGER,
            aht10_temp_c REAL,
            aht10_hum_percent REAL,
            scd40_co2_ppm REAL,
            pms5003_pm1_0 INTEGER,
            pms5003_pm2_5 INTEGER,
            pms5003_pm10_0 INTEGER
        )
    ''')
    
    # Limpa a tabela caso o script seja rodado mais de uma vez
    cursor.execute('DELETE FROM leituras_sensores')

    # 3. Valores iniciais "base" para simular uma leitura contínua (Random Walk)
    current_time = datetime.now() - timedelta(days=2) # Começa de 2 dias atrás
    
    # Valores base realistas para iniciar
    v_mq3 = 1200
    v_mq9 = 1500
    v_mq135 = 800
    v_mq2 = 1100
    v_temp = 25.0
    v_hum = 55.0
    v_co2 = 450.0
    v_pm1 = 5
    v_pm25 = 12
    v_pm10 = 15

    registros = []

    # 4. Loop para gerar os dados fictícios
    print(f"Gerando {num_records} registros fictícios...")
    for _ in range(num_records):
        # Avança o tempo (ex: 1 minuto por leitura)
        current_time += timedelta(minutes=1)
        timestamp_str = current_time.strftime('%Y-%m-%d %H:%M:%S')

        # Varia os dados levemente para cima ou para baixo para simular a realidade
        # Sensores MOS (simulando a leitura do ADC do ESP32: 0 a 4095)
        v_mq3 = max(0, min(4095, v_mq3 + random.randint(-50, 50)))
        v_mq9 = max(0, min(4095, v_mq9 + random.randint(-50, 50)))
        v_mq135 = max(0, min(4095, v_mq135 + random.randint(-50, 50)))
        v_mq2 = max(0, min(4095, v_mq2 + random.randint(-50, 50)))
        
        # AHT10 (Temperatura e Umidade)
        v_temp = round(max(15.0, min(40.0, v_temp + random.uniform(-0.5, 0.5))), 2)
        v_hum = round(max(30.0, min(90.0, v_hum + random.uniform(-1.0, 1.0))), 2)
        
        # SCD40 (CO2 em PPM)
        v_co2 = round(max(400.0, min(2000.0, v_co2 + random.uniform(-10, 15))), 2)
        
        # PMS5003 (Partículas ug/m3) - Garantindo que PM10 >= PM2.5 >= PM1.0
        v_pm1 = max(0, v_pm1 + random.randint(-2, 2))
        v_pm25 = max(v_pm1, v_pm25 + random.randint(-3, 3))
        v_pm10 = max(v_pm25, v_pm10 + random.randint(-4, 4))

        # Adiciona a tupla na lista de registros
        registros.append((
            timestamp_str, 
            v_mq3, v_mq9, v_mq135, v_mq2, 
            v_temp, v_hum, 
            v_co2, 
            v_pm1, v_pm25, v_pm10
        ))

    # 5. Insere tudo no banco SQLite de uma vez
    cursor.executemany('''
        INSERT INTO leituras_sensores (
            timestamp, mq3_adc, mq9_adc, mq135_adc, mq2_adc, 
            aht10_temp_c, aht10_hum_percent, scd40_co2_ppm, 
            pms5003_pm1_0, pms5003_pm2_5, pms5003_pm10_0
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    ''', registros)

    conn.commit()
    conn.close()
    print(f"Sucesso! Arquivo '{db_name}' criado com {num_records} linhas de dados de teste.")

# Executa a função ao rodar o script
if __name__ == "__main__":
    # Você pode mudar a quantidade de linhas geradas aqui (1000 = aprox 16 horas de dados de 1 em 1 minuto)
    create_mock_database(db_name="banco_teste_gases.sqlite", num_records=1000)