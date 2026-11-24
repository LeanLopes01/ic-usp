/*
 * NÓ IOT: COZINHA (ATUALIZADO V2)
 * Hardware: ESP32-C3 Super Mini + AHT10 + SCD40 + PMS5003 + MQ-02 + MQ-05
 * * Ligações:
 * I2C (AHT10 e SCD40) : SDA -> GPIO 8 | SCL -> GPIO 9
 * Serial (PMS5003)    : TX do Sensor -> GPIO 4 | RX do Sensor -> GPIO 5
 * Analógico (MOS)     : MQ-02 -> GPIO 0 | MQ-05 -> GPIO 1
 */

#include <WiFi.h>
#include <PubSubClient.h>
#include <ArduinoJson.h>
#include <Wire.h>
#include <Adafruit_AHTX0.h>
#include <Adafruit_SCD4X.h>
#include "PMS.h"

// --- Identificação do Nó ---
const char* LOCAL_NOME = "cozinha"; 

// --- Configurações de Rede ---
const char* WIFI_SSID   = "YOUR_WIFI_SSID";
const char* WIFI_PASS   = "YOUR_WIFI_PASSWORD";
const char* MQTT_SERVER = "192.168.15.24"; 
const int   MQTT_PORT   = 1883;
const char* MQTT_TOPIC  = "esp32/sensores/ambiental";
const char* MQTT_ID     = "ESP32_Cozinha";

// --- Pinos ---
const int PIN_MQ02 = 0; // ADC1_CH0
const int PIN_MQ05 = 1; // ADC1_CH1
const int PMS_RX   = 4; // RX no ESP32 (Ligar no TX do PMS5003)
const int PMS_TX   = 5; // TX no ESP32 (Ligar no RX do PMS5003)

// --- Objetos dos Sensores ---
Adafruit_AHTX0 aht;
Adafruit_SCD4X scd40;
HardwareSerial SerialPMS(1); // Usa a UART1 do ESP32
PMS pms(SerialPMS);
PMS::DATA dataPMS;

bool aht_ok   = false;
bool scd40_ok = false;

// --- Temporização ---
const unsigned long PUBLISH_MS = 60000UL; // 1 minuto
unsigned long t_ultimo_publish = 0UL;

WiFiClient espClient;
PubSubClient mqttClient(espClient);

// =================================================================
// VARIÁVEIS E CONFIGURAÇÃO DA INTERRUPÇÃO DE TIMER (MOS SENSORS)
// =================================================================
hw_timer_t * timer = NULL;

// Variáveis voláteis pois são alteradas dentro da ISR (Interrupt)
volatile float mq2_filtrado = 0;
volatile float mq5_filtrado = 0;

// Rotina de Interrupção (ISR) executada no background
void IRAM_ATTR onTimer() {
  // Filtro EMA (Média Móvel Exponencial): Suaviza ruídos
  // 10% da leitura nova + 90% da leitura antiga
  mq2_filtrado = (0.1 * analogReadMilliVolts(PIN_MQ02)) + (0.9 * mq2_filtrado);
  mq5_filtrado = (0.1 * analogReadMilliVolts(PIN_MQ05)) + (0.9 * mq5_filtrado);
}
// =================================================================

void conectar_wifi() {
  if (WiFi.status() == WL_CONNECTED) return;
  Serial.printf("\nConectando ao WiFi %s...", WIFI_SSID);
  WiFi.mode(WIFI_STA);
  WiFi.begin(WIFI_SSID, WIFI_PASS);
  while (WiFi.status() != WL_CONNECTED) { 
    delay(500); 
    Serial.print("."); 
  }
  Serial.printf("\nWiFi OK - IP: %s\n", WiFi.localIP().toString().c_str());
}

void garantir_mqtt() {
  while (!mqttClient.connected()) {
    conectar_wifi(); 
    Serial.print("Conectando ao MQTT...");
    if (mqttClient.connect(MQTT_ID)) {
      Serial.println(" OK.");
    } else {
      Serial.print(" falhou, state: ");
      Serial.print(mqttClient.state());
      Serial.println(" - aguardando 5s...");
      delay(5000);
    }
  }
}

void setup() {
  Serial.begin(115200);
  delay(1000);
  Serial.println("\n--- Iniciando Nó IoT: " + String(LOCAL_NOME) + " ---");

  // Configuração ADC (ESP32-C3)
  analogReadResolution(12);

  // Inicia Serial do PMS5003
  SerialPMS.begin(9600, SERIAL_8N1, PMS_RX, PMS_TX);
  pms.passiveMode(); // Opcional: economiza energia lendo apenas quando solicitado

  // Inicia I2C
  Wire.begin(8, 9);

  // Inicializa AHT10
  if (aht.begin()) {
    Serial.println("[AHT10] OK!");
    aht_ok = true;
  } else {
    Serial.println("[AHT10] Falhou!");
  }

  // Inicializa SCD40
  if (scd40.begin()) {
    Serial.println("[SCD40] OK!");
    scd40.startPeriodicMeasurement();
    scd40_ok = true;
  } else {
    Serial.println("[SCD40] Falhou!");
  }

  // Configuração do Timer Interrupt (Dispara a cada 100ms)
  // Prescaler 80 (Para ESP32 base: 80MHz / 80 = 1MHz, 1 tick = 1 microsegundo)
  timer = timerBegin(0, 80, true);
  timerAttachInterrupt(timer, &onTimer, true);
  timerAlarmWrite(timer, 100000, true); // 100.000 microsegundos = 100ms
  timerAlarmEnable(timer);

  conectar_wifi();
  mqttClient.setServer(MQTT_SERVER, MQTT_PORT);
}

void loop() {
  if (!mqttClient.connected()) garantir_mqtt();
  mqttClient.loop();

  unsigned long agora = millis();
  
  if (agora - t_ultimo_publish >= PUBLISH_MS) {
    t_ultimo_publish = agora;

    JsonDocument doc; 
    doc["local"] = LOCAL_NOME; 
    
    // Objeto de Clima
    JsonObject clima = doc["clima"].to<JsonObject>();
    
    // Lê AHT10
    if (aht_ok) {
      sensors_event_t humidity, temp;
      aht.getEvent(&humidity, &temp);
      clima["temp"] = roundf(temp.temperature * 10.0f) / 10.0f;
      clima["umidade"] = roundf(humidity.relative_humidity * 10.0f) / 10.0f;
    }

    // Lê SCD40 (CO2)
    if (scd40_ok && scd40.readMeasurement()) {
      clima["co2"] = scd40.getCO2();
      // O SCD40 também mede temp/umidade, mas vamos confiar no AHT10 que costuma ser mais rápido.
    }

    // Lê PMS5003 (Particulados)
    JsonObject pm = doc["particulados"].to<JsonObject>();
    pms.requestRead(); // Solicita leitura no modo passivo
    if (pms.readUntil(dataPMS)) {
      pm["pm1"]   = dataPMS.PM_AE_UG_1_0;
      pm["pm25"]  = dataPMS.PM_AE_UG_2_5;
      pm["pm10"]  = dataPMS.PM_AE_UG_10_0;
    } else {
      pm["pm1"]   = 0;
      pm["pm25"]  = 0;
      pm["pm10"]  = 0;
    }

    // Lê os sensores MOS atualizados pela Interrupção
    // Usamos 'noInterrupts' rapidamente apenas para copiar a variável sem corromper o dado
    float val_mq2, val_mq5;
    portENTER_CRITICAL(&timerMux); // Para ESP32-C3
    val_mq2 = mq2_filtrado;
    val_mq5 = mq5_filtrado;
    portEXIT_CRITICAL(&timerMux);

    // Salvamos no JSON (agora a leitura está muito mais estável por causa da Interrupção + EMA)
    doc["mq2"]["valor"] = roundf(val_mq2);
    doc["mq5"]["valor"] = roundf(val_mq5);

    char payload[768];
    size_t tam = serializeJson(doc, payload);

    Serial.printf("Publicando: %s\n", payload);
    mqttClient.publish(MQTT_TOPIC, payload, tam);
  }
}