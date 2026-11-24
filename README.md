# Projetos de Iniciação Científica (USP) - Nariz Eletrônico & Sensoriamento de Gases

Repositório que reúne a pesquisa, desenvolvimento de hardware embarcado, aquisição de dados e interfaces analíticas desenvolvidas para projetos de Iniciação Científica na **Universidade de São Paulo (USP)**.

O ecossistema abrange desde o firmware dos sensores de gás até painéis de visualização espacial e modelos de classificação de odores e compostos voláteis (VOCs).

---

## 🔬 Módulos do Projeto

```text
ic-usp/
├── Nariz Eletronico/           # Sistema completo de Nariz Eletrônico (Desktop + Firmware)
│   ├── arduino/                # Sketches para aquisição da matriz de sensores MQ/TGS
│   ├── desktop/                # Interface desktop em Python para leitura e controle serial
│   ├── automacao_release/      # Scripts de empacotamento, versionamento e release
│   └── tests/                  # Testes automatizados do protocolo e aquisição
│
├── DashboardMapas2/            # Visualização geoespacial e mapas térmicos de dispersão
│   ├── painel-gases/           # Interface web com templates e gráficos analíticos
│   ├── static/ & templates/    # Estilização CSS e layouts interativos
│   └── dashboard/              # Módulos analíticos em Python (Plotly, Pandas, NumPy)
│
├── Sistema gás/                # Histórico evolutivo da arquitetura IoT (v0.1 até v1.0)
│   ├── v0.1/ a v0.4.3/         # Primeiras versões com ESP8266, interfaces e calibrações
│   ├── v0.5.0 (servidor SQL)/  # Integração com banco de dados MySQL e endpoints
│   ├── v1.0.0 (sistema modular)/ # Versão final modularizada para escalabilidade
│   ├── espValvulas/            # Controle de fluxo de ar e injeção de amostras com servomotores
│   └── Coisas Notebook/        # Firmware com suporte a redes WPA2-Enterprise (USP / Eduroam)
│
└── Treinador/                  # Processamento, extração de dados e treinamento de IA
    └── extrator_rpg_pdf.py     # Scripts de extração e suporte a modelos Ollama/Llama
```

---

## 🛠️ Tecnologias & Hardware

### Hardware & Microcontroladores
- **Microcontroladores:** ESP8266 (NodeMCU/Wemos D1), ESP32, Arduino Uno/Mega
- **Sensores:** Matriz de sensores de gás semicondutores de óxido metálico (Série MQ: MQ-2, MQ-3, MQ-135, TGS)
- **Atuadores:** Válvulas solenoides e servomotores para injeção controlada de amostras de gás
- **Rede:** Wi-Fi com suporte a WPA2-Personal e WPA2-Enterprise (Eduroam / 802.1X via `esp_wpa2.h`)

### Software & Bibliotecas
- **Linguagens:** C/C++ (Arduino IDE / PlatformIO), Python 3.10+
- **Comunicação:** Serial (UART), MQTT (PubSubClient), WebSockets, REST APIs
- **Visualização & Dashboard:** Plotly, Flask, Matplotlib, Seaborn
- **Análise de Dados:** Pandas, NumPy, Scikit-learn, SciPy

---

## 🚀 Como Executar

### 1. Firmware dos Sensores (ESP / Arduino)
1. Abra a pasta do módulo desejado (por exemplo, `Sistema gás/v1.0.0 (sistema modular)` ou `Nariz Eletronico/arduino`) na **Arduino IDE** ou **VS Code (PlatformIO)**.
2. Instale as bibliotecas necessárias: `WiFi`, `PubSubClient`, `ArduinoJson`.
3. Configure o SSID e senha da sua rede (substituindo os placeholders `YOUR_WIFI_SSID` e `YOUR_WIFI_PASSWORD`).
4. Conecte o microcontrolador via USB e faça o upload do código.

### 2. Interface Desktop do Nariz Eletrônico
Para monitorar a leitura dos sensores em tempo real pela porta serial:
```bash
cd "Nariz Eletronico"
pip install pyserial matplotlib numpy
python desktop/main.py
```

### 3. Painel Web de Dispersão e Mapas
Para iniciar o dashboard de visualização:
```bash
cd DashboardMapas2
pip install flask plotly pandas numpy
python app.py
```
Acesse `http://localhost:5000` no seu navegador.
