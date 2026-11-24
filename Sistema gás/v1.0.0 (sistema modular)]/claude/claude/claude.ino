/*
 * ============================================================
 * AirWatch ESP32 — Firmware v2.0 (Sensor Fusion: BMP280 + DHT11)
 * Sensores: MQ-135, MQ-131, MQ-2, MQ-7, MQ-136, MQ-137 
 * Clima: Temperatura e Pressão (BMP280) + Umidade (DHT11)
 * ============================================================
 *
 * PINOS DOS SENSORES:
 * MQ-135 → GPIO 36 (VP)   MQ-131 → GPIO 39 (VN)
 * MQ-2   → GPIO 34        MQ-7   → GPIO 35
 * MQ-136 → GPIO 32        MQ-137 → GPIO 33
 * BMP280 → I2C SDA=21, SCL=22 (Endereço 0x76)
 * DHT11  → GPIO 4
 * LED    → GPIO 2
 *
 * BIBLIOTECAS NECESSÁRIAS:
 * PubSubClient, ArduinoJson, Adafruit_BMP280, DHT sensor library
 * ============================================================
 */

#include <WiFi.h>
#include <PubSubClient.h>
#include <ArduinoJson.h>
#include <Wire.h>
#include <Adafruit_BMP280.h>
#include <DHT.h>
#include <math.h>
#include <algorithm>

// ────────────────────────────────────────────────────────────
//  1. CONFIGURAÇÕES DE REDE E CLIMA
// ────────────────────────────────────────────────────────────
const char* WIFI_SSID   = "YOUR_WIFI_SSID";
const char* WIFI_PASS   = "YOUR_WIFI_PASSWORD";
const char* MQTT_SERVER = "192.168.15.24";
const int   MQTT_PORT   = 1883;
const char* MQTT_TOPIC  = "esp32/sensores/ambiental";
const char* MQTT_ID     = "ESP32_AirWatch_v2";

// Configurações DHT11
#define DHTPIN 4
#define DHTTYPE DHT11
DHT dht(DHTPIN, DHTTYPE);

// Configurações BMP280
Adafruit_BMP280 bmp;
bool bmp_ok = false;

// ────────────────────────────────────────────────────────────
//  2. TEMPORIZAÇÃO E BUFFER
// ────────────────────────────────────────────────────────────
const unsigned long SAMPLE_MS  = 5000UL;     // Coleta a cada 5s
const unsigned long PUBLISH_MS = 300000UL;   // Publica a cada 5 min

const int BUFFER_SIZE = (int)(PUBLISH_MS / SAMPLE_MS);
const int TRIM = (int)(BUFFER_SIZE * 0.10f); // Apara 10% dos extremos

// ────────────────────────────────────────────────────────────
//  3. HARDWARE E CONSTANTES FÍSICAS DOS MQs
// ────────────────────────────────────────────────────────────
const int PINOS[]      = { 36, 39, 34, 35, 32, 33 };
const int N_SENSORES   = 6;
const int PINO_LED     = 2;
const int ADC_SAMPLES  = 32;

const float RL         = 10.0f;    
const float VCC_SENSOR = 5.0f;     
const float VCC_ADC    = 3.3f;     
const float DIV_FATOR  = 1.5152f; 
const float V_MIN_OK   = 0.30f;   
const float V_MAX_OK   = 4.85f;   

const float R0[6] = { 10.0f, 4.90f, 3.00f, 1.00f, 10.0f, 20.0f };
const float COEF_M[6] = { -0.417f, -0.450f, -0.380f, -0.706f, -0.550f, -0.650f };
const float COEF_B[6] = {  0.858f,  0.750f,  1.130f,  1.148f,  0.800f,  0.850f };

const char* CHAVES[6] = { "mq135", "mq131", "mq2", "mq7", "mq136", "mq137" };
const char* NOMES[6]  = { "MQ-135","MQ-131","MQ-2","MQ-7","MQ-136","MQ-137" };

float buf_ratio[6][60];
float buf_tensao[6][60];
float buf_rs[6][60];
bool  buf_ok[6][60];
int   buf_n = 0;

WiFiClient      espClient;
PubSubClient    mqttClient(espClient);

// ────────────────────────────────────────────────────────────
//  4. FUNÇÕES MATEMÁTICAS
// ────────────────────────────────────────────────────────────
int adc_mediana(int pino) {
  int amostras[ADC_SAMPLES];
  for (int i = 0; i < ADC_SAMPLES; i++) {
    amostras[i] = analogRead(pino);
    delayMicroseconds(100);
  }
  int meio = ADC_SAMPLES / 2;
  std::nth_element(amostras, amostras + meio, amostras + ADC_SAMPLES);
  return amostras[meio];
}

float adc_para_v(int adc) {
  float v = (float)adc * VCC_ADC / 4095.0f * DIV_FATOR;
  return (v >= V_MIN_OK && v <= V_MAX_OK) ? v : -1.0f;
}

float calc_rs(float v) {
  return ((VCC_SENSOR - v) * RL) / v;
}

float calc_ppm(float ratio, int idx) {
  if (ratio <= 0.005f) return -1.0f;
  float ppm = powf(10.0f, (log10f(ratio) - COEF_B[idx]) / COEF_M[idx]);
  if (!isfinite(ppm) || ppm < 0.001f || ppm > 500000.0f) return -1.0f;
  return ppm;
}

float trimmed_mean(float* arr, bool* ok, int n, int trim) {
  float temp[60];
  int cnt = 0;
  for (int i = 0; i < n; i++) {
    if (ok[i] && isfinite(arr[i]) && arr[i] > 0.0f) temp[cnt++] = arr[i];
  }
  if (cnt == 0) return NAN;

  int t = (cnt > trim * 2 + 2) ? trim : 0;
  std::sort(temp, temp + cnt);

  float soma = 0.0f;
  int   util = 0;
  for (int i = t; i < cnt - t; i++) {
    soma += temp[i];
    util++;
  }
  return (util > 0) ? soma / util : NAN;
}

// ────────────────────────────────────────────────────────────
//  5. COLETA DE AMOSTRAS NO BUFFER
// ────────────────────────────────────────────────────────────
void coletar_amostra() {
  if (buf_n >= BUFFER_SIZE) return;
  int slot = buf_n;

  for (int i = 0; i < N_SENSORES; i++) {
    float v = adc_para_v(adc_mediana(PINOS[i]));
    if (v < 0.0f) {
      buf_ok[i][slot] = false;
      buf_ratio[i][slot] = buf_tensao[i][slot] = buf_rs[i][slot] = NAN;
      continue;
    }

    float rs    = calc_rs(v);
    float ratio = rs / R0[i];

    if (ratio <= 0.0f || ratio > 300.0f) {
      buf_ok[i][slot] = false;
      buf_ratio[i][slot] = buf_tensao[i][slot] = buf_rs[i][slot] = NAN;
      continue;
    }

    buf_ok[i][slot]     = true;
    buf_tensao[i][slot] = v;
    buf_rs[i][slot]     = rs;
    buf_ratio[i][slot]  = ratio;
  }

  buf_n++;
  Serial.printf("[%2d/%d] ", buf_n, BUFFER_SIZE);
  for (int i = 0; i < N_SENSORES; i++) {
    if (buf_ok[i][slot]) Serial.printf("%s:%.3f ", CHAVES[i], buf_ratio[i][slot]);
    else Serial.printf("%s:-- ", CHAVES[i]);
  }
  Serial.println();
}

// ────────────────────────────────────────────────────────────
//  6. PUBLICAÇÃO MQTT (COM FUSÃO BMP + DHT)
// ────────────────────────────────────────────────────────────
void publicar_media() {
  if (buf_n == 0) return;

  Serial.printf("\n[MQTT] Calculando médias e lendo clima...\n");
  StaticJsonDocument<768> doc; // Aumentado um pouco para garantir espaço

  // --- 1. Processa os gases MQ ---
  for (int i = 0; i < N_SENSORES; i++) {
    float med_ratio  = trimmed_mean(buf_ratio[i],  buf_ok[i], buf_n, TRIM);
    float med_tensao = trimmed_mean(buf_tensao[i], buf_ok[i], buf_n, TRIM);
    float med_rs     = trimmed_mean(buf_rs[i],     buf_ok[i], buf_n, TRIM);

    JsonObject obj = doc.createNestedObject(CHAVES[i]);

    if (isnan(med_ratio)) {
      obj["ok"] = false;
      continue;
    }

    float med_ppm = calc_ppm(med_ratio, i);
    obj["tensao"] = roundf(med_tensao * 10000.0f) / 10000.0f;
    obj["rs"]     = roundf(med_rs     * 10000.0f) / 10000.0f;
    obj["ratio"]  = roundf(med_ratio  * 100000.0f) / 100000.0f;
    
    if (med_ppm > 0.0f) {
      obj["ppm"] = roundf(med_ppm * 100.0f) / 100.0f;
      obj["ok"]  = true;
    } else {
      obj["ok"]  = false;
    }
  }

  // --- 2. Processa o Clima (BMP280 + DHT11) ---
  // Enviamos como "bme280" para enganar a bridge e o banco de dados
  JsonObject climaObj = doc.createNestedObject("bme280");
  
  float t_bmp = bmp_ok ? bmp.readTemperature() : NAN;
  float press = bmp_ok ? bmp.readPressure() : NAN;
  float altit = bmp_ok ? bmp.readAltitude(1013.25) : NAN;
  float umid  = dht.readHumidity();
  float t_dht = dht.readTemperature();

  // Estratégia de backup: Se o BMP falhar na temperatura, usa a do DHT11
  float temp_final = (!isnan(t_bmp) && t_bmp > 0.0) ? t_bmp : t_dht;

  // Só adiciona ao JSON se o valor for válido
  if (!isnan(temp_final)) climaObj["temp"]    = roundf(temp_final * 100.0f) / 100.0f;
  if (!isnan(umid))       climaObj["umidade"] = roundf(umid * 100.0f) / 100.0f;
  if (!isnan(press) && press > 0) climaObj["pressao"] = roundf(press * 10.0f) / 10.0f;
  if (!isnan(altit))      climaObj["altitude"]= roundf(altit * 10.0f) / 10.0f;

  Serial.printf("  CLIMA: Temp=%.2fC | Umid=%.2f%% | Press=%.0fPa\n", temp_final, umid, press);

  // --- 3. Envia o pacote ---
  char payload[768];
  size_t tam = serializeJson(doc, payload);

  if (mqttClient.publish(MQTT_TOPIC, payload, tam)) {
    Serial.printf("[MQTT] Publicado com sucesso (%zu bytes).\n\n", tam);
    for (int p = 0; p < 3; p++) {
      digitalWrite(PINO_LED, LOW);  delay(60);
      digitalWrite(PINO_LED, HIGH); delay(60);
    }
  } else {
    Serial.println("[MQTT] Falha ao publicar!");
  }
}

// ────────────────────────────────────────────────────────────
//  7. REDE E SETUP
// ────────────────────────────────────────────────────────────
void conectar_wifi() {
  Serial.printf("Conectando ao WiFi '%s'", WIFI_SSID);
  WiFi.mode(WIFI_STA);
  WiFi.begin(WIFI_SSID, WIFI_PASS);
  int t = 0;
  while (WiFi.status() != WL_CONNECTED && t++ < 60) {
    delay(500); Serial.print(".");
  }
  if (WiFi.isConnected()) Serial.printf("\nWiFi OK — IP: %s\n", WiFi.localIP().toString().c_str());
  else ESP.restart();
}

void garantir_mqtt() {
  if (mqttClient.connected()) return;
  Serial.print("[MQTT] Conectando ao broker...");
  int tentativas = 0;
  while (!mqttClient.connected() && tentativas++ < 10) {
    if (mqttClient.connect(MQTT_ID)) Serial.println(" OK.");
    else { delay(5000); if (WiFi.status() != WL_CONNECTED) conectar_wifi(); }
  }
  if (!mqttClient.connected()) ESP.restart();
}

void setup() {
  Serial.begin(115200);
  delay(500);
  Serial.println("\n========================================");
  Serial.println("  AirWatch ESP32 v2.0 (BMP280 + DHT11)");
  Serial.println("========================================");

  pinMode(PINO_LED, OUTPUT);
  digitalWrite(PINO_LED, LOW);

  analogReadResolution(12);
  analogSetAttenuation(ADC_11db);
  memset(buf_ok, false, sizeof(buf_ok));
  buf_n = 0;

  // Inicializa o DHT11
  dht.begin();
  Serial.println("[DHT11] Sensor de Umidade inicializado no GPIO 4.");

  // Inicializa o BMP280
  Wire.begin(21, 22);
  bmp_ok = bmp.begin(0x76);
  if (bmp_ok) {
    bmp.setSampling(Adafruit_BMP280::MODE_NORMAL,
                    Adafruit_BMP280::SAMPLING_X2,
                    Adafruit_BMP280::SAMPLING_X16,
                    Adafruit_BMP280::FILTER_X16,
                    Adafruit_BMP280::STANDBY_MS_500);
    Serial.println("[BMP280] Sensor de Temp/Pressao inicializado (0x76).");
  } else {
    Serial.println("[BMP280] FALHA! Sensor nao encontrado.");
  }

  conectar_wifi();
  mqttClient.setServer(MQTT_SERVER, MQTT_PORT);
  mqttClient.setBufferSize(768); // Aumentado para suportar JSON maior
  garantir_mqtt();

  delay(3000);
  digitalWrite(PINO_LED, HIGH);
  Serial.println("\n=== SISTEMA PRONTO ===\n");
}

void loop() {
  if (!mqttClient.connected()) garantir_mqtt();
  mqttClient.loop();

  static unsigned long t_ultimo_sample  = 0UL;
  static unsigned long t_ultimo_publish = 0UL;
  static bool          inicializado     = false;

  unsigned long agora = millis();

  if (!inicializado) {
    t_ultimo_sample  = agora;
    t_ultimo_publish = agora;
    inicializado     = true;
  }

  if (agora - t_ultimo_sample >= SAMPLE_MS) {
    t_ultimo_sample = agora;
    coletar_amostra();
  }

  if (agora - t_ultimo_publish >= PUBLISH_MS) {
    t_ultimo_publish = agora;
    garantir_mqtt();
    publicar_media();
    memset(buf_ok, false, sizeof(buf_ok));
    buf_n = 0;
    Serial.println("=== Buffer zerado. Novo ciclo iniciado ===\n");
  }

  yield();
}