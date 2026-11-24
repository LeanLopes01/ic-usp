#include <WiFi.h>
#include <PubSubClient.h>
#include <ArduinoJson.h>
#include <math.h>

// ==========================================
// 1. CONFIGURAÇÕES DE REDE (EDUROAM USP)
// ==========================================
const char* ssid = "eduroam";
const char* eap_identity = "SEU_NUMERO_USP@usp.br"; // <-- Seu e-mail USP
const char* eap_username = "SEU_NUMERO_USP@usp.br"; // <-- Seu e-mail USP
const char* eap_password = "SUA_SENHA_UNIFICADA";   // <-- Sua senha USP
const char* mqtt_server  = "docjediwho.com.br";     // <-- Seu servidor acessível externamente

// ==========================================
// 2. CONSTANTES TÉCNICAS DOS SENSORES
// ==========================================
const int pinoMQ135 = 36; // VP
const int pinoMQ9   = 39; // VN
const int pinoMQ5   = 34; // 34

const float RL = 10.0;      
const float VOLT_RES = 3.3; 
const float VIN_SENS = 5.0; 

// Fatores de Ar Limpo (RS/R0)
const float RATIO_AR_MQ135 = 3.60;
const float RATIO_AR_MQ9   = 9.80;
const float RATIO_AR_MQ5   = 6.50;

// Coeficientes para conversão PPM
const float MQ135_m = -0.417; const float MQ135_b = 0.858;
const float MQ9_m   = -0.45;  const float MQ9_b   = 1.25;
const float MQ5_m   = -0.38;  const float MQ5_b   = 1.15;

float R0_MQ135, R0_MQ9, R0_MQ5;

WiFiClient espClient;
PubSubClient client(espClient);
hw_timer_t * timer = NULL;
volatile bool flagLerSensores = false;

void IRAM_ATTR onTimer() { flagLerSensores = true; }

// ==========================================
// FUNÇÕES MATEMÁTICAS (BLINDADAS)
// ==========================================

float calcularVoltagem(int adc_val) {
  float v_out = (adc_val * VOLT_RES) / 4095.0;
  float v_sensor = v_out * 1.515; // Fator de correção do divisor
  
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

// ==========================================
// SETUP E CONEXÃO
// ==========================================

void setup_wifi() {
  delay(10);
  Serial.println("\nConectando à rede eduroam...");
  
  // O ESP32 Core V3 usa essa sintaxe para WPA2-Enterprise nativamente
  WiFi.disconnect(true);
  WiFi.mode(WIFI_STA);
  WiFi.begin(ssid, WPA2_AUTH_PEAP, eap_identity, eap_username, eap_password);
  
  while (WiFi.status() != WL_CONNECTED) { 
    delay(500); 
    Serial.print("."); 
  }
  Serial.println("\nWi-Fi Conectado!");
  Serial.print("IP: "); Serial.println(WiFi.localIP());
}

void calibrarInicial() {
  Serial.println("Calibrando sensores em ar limpo (Aguarde 5s)...");
  float rs1 = 0, rs2 = 0, rs3 = 0;
  for(int i=0; i<10; i++) {
    rs1 += calcularRS(calcularVoltagem(analogRead(pinoMQ135)));
    rs2 += calcularRS(calcularVoltagem(analogRead(pinoMQ9)));
    rs3 += calcularRS(calcularVoltagem(analogRead(pinoMQ5)));
    delay(500);
  }
  R0_MQ135 = (rs1 / 10.0) / RATIO_AR_MQ135;
  R0_MQ9   = (rs2 / 10.0) / RATIO_AR_MQ9;
  R0_MQ5   = (rs3 / 10.0) / RATIO_AR_MQ5;
  
  Serial.println("Calibração concluída!");
  Serial.printf("R0 Gravados na Memória -> MQ135: %.2f | MQ9: %.2f | MQ5: %.2f\n", R0_MQ135, R0_MQ9, R0_MQ5);
}

void setup() {
  Serial.begin(115200);
  analogReadResolution(12);
  setup_wifi();
  calibrarInicial();
  
  client.setServer(mqtt_server, 1883);

  // Inicia o timer em 1 MHz
  timer = timerBegin(1000000); 
  timerAttachInterrupt(timer, &onTimer);
  
  // Alarme de 5 minutos exatos (300.000.000 microssegundos)
  timerAlarm(timer, 300000000ULL, true, 0);
}

void loop() {
  if (!client.connected()) {
    while (!client.connected()) {
      Serial.print("Conectando ao MQTT externo...");
      if (client.connect("ESP32_Gases_USP")) Serial.println(" OK");
      else { Serial.print(" Falha. Erro: "); Serial.println(client.state()); delay(5000); }
    }
  }
  client.loop();

  if (flagLerSensores) {
    flagLerSensores = false;

    float v1 = calcularVoltagem(analogRead(pinoMQ135));
    float v2 = calcularVoltagem(analogRead(pinoMQ9));
    float v3 = calcularVoltagem(analogRead(pinoMQ5));

    float rs1 = calcularRS(v1);
    float rs2 = calcularRS(v2);
    float rs3 = calcularRS(v3);

    float ratio1 = rs1 / R0_MQ135;
    float ratio2 = rs2 / R0_MQ9;
    float ratio3 = rs3 / R0_MQ5;

    float ppm1 = calcularPPM(ratio1, MQ135_m, MQ135_b);
    float ppm2 = calcularPPM(ratio2, MQ9_m, MQ9_b);
    float ppm3 = calcularPPM(ratio3, MQ5_m, MQ5_b);

    StaticJsonDocument<512> doc;
    
    JsonObject mq135 = doc.createNestedObject("mq135");
    mq135["tensao"] = v1; mq135["rs"] = rs1; mq135["ratio"] = ratio1; mq135["ppm"] = ppm1;

    JsonObject mq9 = doc.createNestedObject("mq9");
    mq9["tensao"] = v2; mq9["rs"] = rs2; mq9["ratio"] = ratio2; mq9["ppm"] = ppm2;

    JsonObject mq5 = doc.createNestedObject("mq5");
    mq5["tensao"] = v3; mq5["rs"] = rs3; mq5["ratio"] = ratio3; mq5["ppm"] = ppm3;

    char buffer[512];
    serializeJson(doc, buffer);
    client.publish("esp32/sensores/ambiental", buffer);
    Serial.println(buffer);
  }
}