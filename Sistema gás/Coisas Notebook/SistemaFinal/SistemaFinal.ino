/*
 * NÓ IOT: COZINHA (VERSÃO WROOM - UART CRUA PMS5003 + JSON PLANO)
 * Hardware: ESP32 WROOM + AHT10 + PMS5003 + MQ135 + MQ02 + MQ03 + MQ07
 * Rede: Eduroam (WPA2-Enterprise)
 * Broker: Argus MQTT
 */

#include <WiFi.h>
#include <esp_eap_client.h>
#include <esp_wifi.h>
#include <PubSubClient.h>
#include <ArduinoJson.h>
#include <Wire.h>
#include <Adafruit_AHTX0.h>
#include <math.h>

// =======================================================
// --- Configurações de Rede (eduroam) ---
// =======================================================
const char* ssid = "eduroam";
const char* EAP_ANONYMOUS = "anonymous@usp.br"; 
const char* EAP_IDENTITY  = "14588023@usp.br"; 
const char* EAP_PASSWORD  = "YOUR_EDUROAM_PASSWORD";            

// =======================================================
// --- Configurações Argus (Servidor MQTT) ---
// =======================================================
const char* mqtt_server   = "143.107.119.55"; 
const int   mqtt_port     = 1883;
const char* mqtt_user     = "argus-backend";   
const char* mqtt_pass     = "YOUR_MQTT_PASSWORD";     
const char* device_id     = "esp2-quality-hash-2640-jhgf"; 
const char* mqtt_topic    = "argus/esp2-quality-hash-2640-jhgf/telemetry"; 
const char* LOCAL_NOME    = "cozinha"; 

// ==========================================
// --- Pinagem ESP32 WROOM ---
// ==========================================
const int PIN_MQ135 = 36; // VP
const int PIN_MQ02  = 39; // VN
const int PIN_MQ03  = 34; 
const int PIN_MQ07  = 35; 
const int PIN_TESTE = 32; // Pino de descarte para limpar o ADC

const int PMS_RX = 16; // Ligado no TX do sensor
const int PMS_TX = 17; // Ligado no RX do sensor

// ==========================================
// --- Constantes Técnicas MOS ---
// ==========================================
const float RL = 10.0;      
const float VOLT_RES = 3.3; 
const float VIN_SENS = 5.0; 

const float RATIO_AR_MQ135 = 3.60;
const float RATIO_AR_MQ2   = 9.83; 
const float RATIO_AR_MQ3   = 60.0; 
const float RATIO_AR_MQ7   = 27.0; 

const float MQ135_m = -0.417; const float MQ135_b = 0.858; // NH3/CO2
const float MQ2_m   = -0.45;  const float MQ2_b   = 1.25;  // Fumaça/GLP
const float MQ3_m   = -0.66;  const float MQ3_b   = 0.44;  // Álcool
const float MQ7_m   = -0.65;  const float MQ7_b   = 1.40;  // CO

float R0_MQ135, R0_MQ2, R0_MQ3, R0_MQ7;

// ==========================================
// --- Estrutura de Dados PMS5003 ---
// ==========================================
struct pms5003data {
  uint16_t framelen;
  uint16_t pm10_standard, pm25_standard, pm100_standard;
  uint16_t pm10_env, pm25_env, pm100_env;
  uint16_t particles_03um, particles_05um, particles_10um, particles_25um, particles_50um, particles_100um;
  uint16_t unused;
  uint16_t checksum;
};

struct pms5003data dataPMS;
unsigned long ultimoLogTempoPMS = 0;

// ==========================================
// --- Objetos e Instâncias ---
// ==========================================
WiFiClient espClient;
PubSubClient mqttClient(espClient);
Adafruit_AHTX0 aht;
HardwareSerial SerialPMS(2); 

bool aht_ok = false;

// Temporização de Publicação (A cada 5 segundos para Argus)
const unsigned long PUBLISH_MS = 5000UL; 
unsigned long t_ultimo_publish = 0UL;

// ==========================================
// --- FUNÇÕES PMS5003 (UART CRUA) ---
// ==========================================
boolean readPMSdata(Stream *s) {
  if (!s->available()) {
    if (millis() - ultimoLogTempoPMS > 3000) {
      Serial.println("[LOG PMS] Aguardando dados na porta Serial...");
      ultimoLogTempoPMS = millis();
    }
    return false;
  }

  if (s->peek() != 0x42) {
    s->read(); // Descarta o byte errado silenciosamente
    return false;
  }

  if (s->available() < 32) {
    return false;
  }

  uint8_t buffer[32];
  uint16_t sum = 0;
  s->readBytes(buffer, 32);

  for (uint8_t i = 0; i < 30; i++) {
    sum += buffer[i];
  }

  if (buffer[0] != 0x42 || buffer[1] != 0x4d) {
    Serial.println("[ERRO PMS] Cabecalho invalido!");
    return false;
  }

  uint16_t buffer_u16[15];
  for (uint8_t i = 0; i < 15; i++) {
    buffer_u16[i] = buffer[2 + i * 2 + 1];
    buffer_u16[i] += (buffer[2 + i * 2] << 8);
  }

  memcpy((void *)&dataPMS, (void *)buffer_u16, 30);

  if (sum != dataPMS.checksum) {
    Serial.println("[ERRO PMS] Checksum falhou.");
    return false;
  }

  return true;
}

// ==========================================
// --- FUNÇÕES MATEMÁTICAS (MOS) ---
// ==========================================
float calcularVoltagem(int adc_val) {
  float v_out = (adc_val * VOLT_RES) / 4095.0;
  float v_sensor = v_out * 1.515;
  if (v_sensor <= 0.1) v_sensor = 0.1; 
  if (v_sensor >= 4.9) v_sensor = 4.9; 
  return v_sensor;
}

float calcularRS(float v_sensor) {
  return ((VIN_SENS - v_sensor) * RL) / v_sensor;
}

float calcularPPM(float ratio, float m, float b) {
  if (ratio <= 0.01) ratio = 0.01; 
  return pow(10, ((log10(ratio) - b) / m));
}

// =======================================================
// --- Conexão Wi-Fi (Eduroam) e MQTT ---
// =======================================================
void conectar_wifi() {
  if (WiFi.status() == WL_CONNECTED) return;
  
  Serial.println("\nConectando na rede eduroam...");
  WiFi.disconnect(true);
  WiFi.mode(WIFI_STA);

  esp_eap_client_set_identity((uint8_t *)EAP_ANONYMOUS, strlen(EAP_ANONYMOUS));
  esp_eap_client_set_username((uint8_t *)EAP_IDENTITY, strlen(EAP_IDENTITY));
  esp_eap_client_set_password((uint8_t *)EAP_PASSWORD, strlen(EAP_PASSWORD));
  esp_wifi_sta_enterprise_enable(); 
  WiFi.begin(ssid);

  int counter = 0;
  while (WiFi.status() != WL_CONNECTED) {
    delay(1000);
    Serial.print(".");
    counter++;
    if (counter >= 30) {
      Serial.println("\n[ERRO] Falha ao conectar. Reiniciando em 5s...");
      delay(5000);
      ESP.restart();
    }
  }
  Serial.println("\nWi-Fi Conectado!");
}

void garantir_mqtt() {
  while (!mqttClient.connected()) {
    conectar_wifi();
    Serial.print("Conectando ao MQTT Argus...");
    if (mqttClient.connect(device_id, mqtt_user, mqtt_pass)) {
      Serial.println(" OK!");
    } else {
      Serial.print(" Falhou ("); Serial.print(mqttClient.state()); Serial.println("). Tentando em 5s...");
      delay(5000);
    }
  }
}

// ==========================================
// --- CALIBRAÇÃO INICIAL DOS SENSORES ---
// ==========================================
void calibrarInicial() {
  Serial.println("\nCalibrando sensores MOS em ar limpo (Aguarde 5s)...");
  float rs_mq135 = 0, rs_mq2 = 0, rs_mq3 = 0, rs_mq7 = 0;
  
  for(int i=0; i<10; i++) {
    analogRead(PIN_TESTE); 
    rs_mq135 += calcularRS(calcularVoltagem(analogRead(PIN_MQ135)));
    rs_mq2   += calcularRS(calcularVoltagem(analogRead(PIN_MQ02)));
    rs_mq3   += calcularRS(calcularVoltagem(analogRead(PIN_MQ03)));
    rs_mq7   += calcularRS(calcularVoltagem(analogRead(PIN_MQ07)));
    delay(500);
  }
  
  R0_MQ135 = (rs_mq135 / 10.0) / RATIO_AR_MQ135;
  R0_MQ2   = (rs_mq2 / 10.0)   / RATIO_AR_MQ2;
  R0_MQ3   = (rs_mq3 / 10.0)   / RATIO_AR_MQ3;
  R0_MQ7   = (rs_mq7 / 10.0)   / RATIO_AR_MQ7;
  
  Serial.println("Calibração concluída com sucesso!");
}

// ==========================================
// --- SETUP ---
// ==========================================
void setup() {
  Serial.begin(115200);
  delay(1000);
  Serial.println("\n--- Iniciando Nó IoT Argus: " + String(LOCAL_NOME) + " (WROOM) ---");

  analogReadResolution(12);
  pinMode(PIN_TESTE, INPUT_PULLDOWN);

  SerialPMS.begin(9600, SERIAL_8N1, PMS_RX, PMS_TX);

  Wire.begin(21, 22);
  if (aht.begin()) {
    Serial.println("[AHT10] OK!");
    aht_ok = true;
  } else {
    Serial.println("[AHT10] Falhou!");
  }

  conectar_wifi();
  calibrarInicial();
  
  mqttClient.setServer(mqtt_server, mqtt_port);
  
  // O JSON plano ocupa menos espaço, 512 bytes é mais que o suficiente agora
  mqttClient.setBufferSize(512); 
}

// ==========================================
// --- LOOP PRINCIPAL ---
// ==========================================
void loop() {
  if (!mqttClient.connected()) garantir_mqtt();
  mqttClient.loop();

  readPMSdata(&SerialPMS);

  unsigned long agora = millis();
  
  if (agora - t_ultimo_publish >= PUBLISH_MS) {
    t_ultimo_publish = agora;

    analogRead(PIN_TESTE); 

    float v_mq135 = calcularVoltagem(analogRead(PIN_MQ135));
    float v_mq2   = calcularVoltagem(analogRead(PIN_MQ02));
    float v_mq3   = calcularVoltagem(analogRead(PIN_MQ03));
    float v_mq7   = calcularVoltagem(analogRead(PIN_MQ07));

    float rs_mq135 = calcularRS(v_mq135);
    float rs_mq2   = calcularRS(v_mq2);
    float rs_mq3   = calcularRS(v_mq3);
    float rs_mq7   = calcularRS(v_mq7);

    float r_mq135 = rs_mq135 / R0_MQ135;
    float r_mq2   = rs_mq2   / R0_MQ2;
    float r_mq3   = rs_mq3   / R0_MQ3;
    float r_mq7   = rs_mq7   / R0_MQ7;

    float ppm_mq135 = calcularPPM(r_mq135, MQ135_m, MQ135_b);
    float ppm_mq2   = calcularPPM(r_mq2,   MQ2_m,   MQ2_b);
    float ppm_mq3   = calcularPPM(r_mq3,   MQ3_m,   MQ3_b);
    float ppm_mq7   = calcularPPM(r_mq7,   MQ7_m,   MQ7_b);

    float p_temp = 0, p_umi = 0;
    if (aht_ok) {
      sensors_event_t humidity, temp;
      aht.getEvent(&humidity, &temp);
      p_temp = roundf(temp.temperature * 10.0f) / 10.0f;
      p_umi  = roundf(humidity.relative_humidity * 10.0f) / 10.0f;
    }

    // ==========================================
    // --- MONTAGEM DO JSON PLANO ---
    // ==========================================
    StaticJsonDocument<512> doc; 
    
    doc["local"]   = LOCAL_NOME; 
    doc["temp"]    = p_temp;
    doc["umidade"] = p_umi;
    
    doc["pm1"]   = dataPMS.pm10_env;
    doc["pm2.5"] = dataPMS.pm25_env; // Usando ponto em vez de vírgula para evitar erros de parse
    doc["pm10"]  = dataPMS.pm100_env;
    
    // Arredondamos o PPM para 2 casas decimais direto na montagem
    doc["mq135"] = roundf(ppm_mq135 * 100.0) / 100.0;
    doc["mq02"]  = roundf(ppm_mq2 * 100.0) / 100.0;
    doc["mq03"]  = roundf(ppm_mq3 * 100.0) / 100.0;
    doc["mq07"]  = roundf(ppm_mq7 * 100.0) / 100.0;

    // --- IMPRESSÃO DOS DADOS NO SERIAL ---
    Serial.println("\n========================================");
    Serial.println("  LEITURAS DO AMBIENTE (ARGUS/EDUROAM)");
    Serial.println("========================================");
    Serial.printf("Temperatura : %.1f °C\n", p_temp);
    Serial.printf("Umidade     : %.1f %%\n", p_umi);
    Serial.println("----------------------------------------");
    Serial.println("PARTICULADOS (UART CRUA):");
    Serial.printf("PM 1.0 : %d ug/m3\n", dataPMS.pm10_env);
    Serial.printf("PM 2.5 : %d ug/m3\n", dataPMS.pm25_env);
    Serial.printf("PM 10  : %d ug/m3\n", dataPMS.pm100_env);
    Serial.println("----------------------------------------");
    Serial.println("GASES (MOS - PPM Arredondado):");
    Serial.printf("MQ135 (36/VP) : %.2f PPM\n", doc["mq135"].as<float>());
    Serial.printf("MQ02  (39/VN) : %.2f PPM\n", doc["mq02"].as<float>());
    Serial.printf("MQ03  (34)    : %.2f PPM\n", doc["mq03"].as<float>());
    Serial.printf("MQ07  (35)    : %.2f PPM\n", doc["mq07"].as<float>());
    Serial.println("========================================");

    // --- Publicação MQTT ---
    char buffer[512];
    size_t tam = serializeJson(doc, buffer);

    Serial.print("Publicando MQTT: "); Serial.println(buffer);
    
    if (mqttClient.publish(mqtt_topic, buffer, tam)) {
        Serial.println("-> SUCESSO: Dados entregues ao Argus!");
    } else {
        Serial.println("-> ERRO: O broker recusou a mensagem.");
    }
  }
}
