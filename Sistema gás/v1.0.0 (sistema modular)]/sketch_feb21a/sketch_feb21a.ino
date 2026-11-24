#include <WiFi.h>
#include <PubSubClient.h>
#include <ArduinoJson.h>
#include <math.h>

// ==========================================
// 1. CONFIGURAÇÕES DE REDE (EDUROAM)
// ==========================================
const char* ssid = "eduroam";
const char* eap_identity = "SEU_NUMERO_USP@usp.br";
const char* eap_username = "SEU_NUMERO_USP@usp.br";
const char* eap_password = "SUA_SENHA_UNIFICADA";
const char* mqtt_server = "docjediwho.com.br"; // Seu domínio via túnel Cloudflare

// ==========================================
// 2. CONSTANTES TÉCNICAS DOS SENSORES
// ==========================================
const int pinoMQ135 = 36; // VP
const int pinoMQ9   = 39; // VN
const int pinoMQ5   = 34; // 34

const float RL = 10.0;      // Resistor de carga em KOhms (padrão 102)
const float VOLT_RES = 3.3; // Tensão de operação do ADC do ESP32
const float VIN_SENS = 5.0; // Tensão de alimentação dos sensores

// Fatores de Ar Limpo (RS/R0) da Literatura
const float RATIO_AR_MQ135 = 3.60;
const float RATIO_AR_MQ9   = 9.80;
const float RATIO_AR_MQ5   = 6.50;

// Coeficientes para conversão PPM: log(y) = m*log(x) + b -> x = 10^((log(y)-b)/m)
// MQ135 (NH3): m = -0.417, b = 0.858 (Baseado no seu dado)
const float MQ135_m = -0.417; 
const float MQ135_b = 0.858;

// MQ9 (CO): Valores típicos de datasheet
const float MQ9_m = -0.45;
const float MQ9_b = 1.25;

// MQ5 (GLP): Valores típicos de datasheet
const float MQ5_m = -0.38;
const float MQ5_b = 1.15;

// Variáveis de Calibração (R0)
float R0_MQ135, R0_MQ9, R0_MQ5;

WiFiClient espClient;
PubSubClient client(espClient);
hw_timer_t * timer = NULL;
volatile bool flagLerSensores = false;

void IRAM_ATTR onTimer() { flagLerSensores = true; }

// ==========================================
// FUNÇÕES MATEMÁTICAS DE CONVERSÃO
// ==========================================

// Calcula a resistência atual do sensor (RS)
float calcularRS(int adc_val) {
  float v_out = (adc_val * VOLT_RES) / 4095.0;
  // Se você usa divisor de tensão para proteger o ESP (5V -> 3.3V), 
  // multiplicamos v_out pelo fator de correção (Ex: 1.51)
  float v_sensor = v_out * 1.515; 
  if (v_sensor >= VIN_SENS) v_sensor = VIN_SENS - 0.1;
  return ((VIN_SENS - v_sensor) * RL) / v_sensor;
}

// Calcula o PPM baseado na Lei da Potência
float calcularPPM(float rs, float r0, float m, float b) {
  float ratio = rs / r0;
  // Fórmula: PPM = 10 ^ ((log10(ratio) - b) / m)
  return pow(10, ((log10(ratio) - b) / m));
}

// ==========================================
// SETUP E CONEXÃO
// ==========================================

void setup_wifi() {
  WiFi.disconnect(true);
  WiFi.mode(WIFI_STA);
  WiFi.begin(ssid, WPA2_AUTH_PEAP, eap_identity, eap_username, eap_password);
  while (WiFi.status() != WL_CONNECTED) { delay(1000); Serial.print("."); }
  Serial.println("\nConectado!");
}

void calibrarInicial() {
  Serial.println("Calibrando sensores em ar limpo...");
  float rs1 = 0, rs2 = 0, rs3 = 0;
  for(int i=0; i<10; i++) {
    rs1 += calcularRS(analogRead(pinoMQ135));
    rs2 += calcularRS(analogRead(pinoMQ9));
    rs3 += calcularRS(analogRead(pinoMQ5));
    delay(500);
  }
  // R0 = RS_ar / Fator_Literatura
  R0_MQ135 = (rs1 / 10.0) / RATIO_AR_MQ135;
  R0_MQ9   = (rs2 / 10.0) / RATIO_AR_MQ9;
  R0_MQ5   = (rs3 / 10.0) / RATIO_AR_MQ5;
  
  Serial.printf("R0s Calculados: MQ135: %.2f | MQ9: %.2f | MQ5: %.2f\n", R0_MQ135, R0_MQ9, R0_MQ5);
}

void setup() {
  Serial.begin(115200);
  analogReadResolution(12);
  setup_wifi();
  calibrarInicial();
  
  client.setServer(mqtt_server, 1883);

  timer = timerBegin(0, 80, true);
  timerAttachInterrupt(timer, &onTimer, true);
  // 5 Minutos = 300.000.000 us
  timerAlarmWrite(timer, 300000000ULL, true);
  timerAlarmEnable(timer);
}

void loop() {
  if (!client.connected()) {
    while (!client.connected()) {
      if (client.connect("ESP32_Gases_USP")) Serial.println("MQTT OK");
      else delay(5000);
    }
  }
  client.loop();

  if (flagLerSensores) {
    flagLerSensores = false;

    // Leitura e Cálculos
    float rs1 = calcularRS(analogRead(pinoMQ135));
    float rs2 = calcularRS(analogRead(pinoMQ9));
    float rs3 = calcularRS(analogRead(pinoMQ5));

    float ppm1 = calcularPPM(rs1, R0_MQ135, MQ135_m, MQ135_b);
    float ppm2 = calcularPPM(rs2, R0_MQ9, MQ9_m, MQ9_b);
    float ppm3 = calcularPPM(rs3, R0_MQ5, MQ5_m, MQ5_b);

    StaticJsonDocument<300> doc;
    doc["mq135_ppm"] = ppm1;
    doc["mq9_ppm"]   = ppm2;
    doc["mq5_ppm"]   = ppm3;
    doc["mq135_rs"]  = rs1;
    doc["status"]    = (ppm1 > 1000) ? "ALERTA" : "NORMAL";

    char buffer[300];
    serializeJson(doc, buffer);
    client.publish("esp32/sensores/ambiental", buffer);
    Serial.println(buffer);
  }
}