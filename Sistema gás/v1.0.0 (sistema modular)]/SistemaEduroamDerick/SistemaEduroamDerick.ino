#include <WiFi.h>
#include <esp_eap_client.h> // Biblioteca necessária para WPA2 Enterprise
#include <esp_wifi.h>       // Biblioteca do sistema WiFi do ESP32
#include <PubSubClient.h>
#include <ArduinoJson.h>
#include <math.h>

// =======================================================
// --- Configurações de Rede (eduroam) ---
// =======================================================
const char* ssid = "eduroam";

// IDENTIDADE EXTERNA: Usada para roteamento. Geralmente "anonymous@dominio.edu.br"
const char* EAP_ANONYMOUS = "anonymous@usp.br"; 

// IDENTIDADE INTERNA: Seu login real
const char* EAP_IDENTITY = "SEU_NUMERO_USP@usp.br"; // <-- Seu e-mail/número USP

// SUA SENHA: A mesma usada no portal/Wi-Fi
const char* EAP_PASSWORD = "SUA_SENHA_UNIFICADA";   // <-- Sua senha USP        

// =======================================================
// --- Configurações Argus (Servidor MQTT) ---
// =======================================================
const char* mqtt_server   = "143.107.119.55"; 
const int   mqtt_port     = 1883;
const char* mqtt_user     = "argus-backend";   
const char* mqtt_pass     = "YOUR_MQTT_PASSWORD";     
const char* device_id     = "esp1-gases-hash-4567-xyzk"; // ID do dispositivo
const char* mqtt_topic    = "argus/esp1-gases-hash-4567-xyzk/telemetry"; // Tópico de publicação

// ==========================================
// --- CONSTANTES TÉCNICAS DOS SENSORES ---
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
// --- FUNÇÕES MATEMÁTICAS (BLINDADAS) ---
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

// =======================================================
// --- Função de Conexão Wi-Fi (WPA2 Enterprise) ---
// =======================================================
void setup_wifi() {
  delay(10);
  Serial.println();
  Serial.print("Iniciando tentativa de conexão na rede: ");
  Serial.println(ssid);

  WiFi.disconnect(true);  // Limpa configurações anteriores
  WiFi.mode(WIFI_STA);    // Define o ESP32 como estação (cliente)

  // Configuração da autenticação EAP-PEAP (eduroam)
  esp_eap_client_set_identity((uint8_t *)EAP_ANONYMOUS, strlen(EAP_ANONYMOUS));
  esp_eap_client_set_username((uint8_t *)EAP_IDENTITY, strlen(EAP_IDENTITY));
  esp_eap_client_set_password((uint8_t *)EAP_PASSWORD, strlen(EAP_PASSWORD));

  // Ativa o motor WPA2 Enterprise antes de conectar
  esp_wifi_sta_enterprise_enable(); 

  // Inicia a conexão
  WiFi.begin(ssid);

  int counter = 0;
  while (WiFi.status() != WL_CONNECTED) {
    delay(1000);
    Serial.print(".");
    counter++;
    
    if (counter % 5 == 0) { 
      Serial.print(" [Status atual do WiFi: ");
      Serial.print(WiFi.status());
      Serial.print("] ");
    }

    if (counter >= 30) {
      Serial.println("\n\n[ERRO FATAL] Falha ao conectar na eduroam.");
      Serial.println("Reiniciando o ESP32 em 5 segundos...");
      delay(5000);
      ESP.restart();
    }
  }

  Serial.println("\n\n>>> Wi-Fi Conectado com Sucesso! <<<");
  Serial.print("Endereço IP recebido: ");
  Serial.println(WiFi.localIP());
}

// =======================================================
// --- Função de Reconexão MQTT ---
// =======================================================
void reconnect() {
  while (!client.connected()) {
    Serial.print("Tentando conexão com o broker MQTT...");
    
    // Conecta usando ID, Usuário e Senha do novo servidor
    if (client.connect(device_id, mqtt_user, mqtt_pass)) {
      Serial.println("Conectado ao MQTT!");
    } else {
      Serial.print("Falhou, erro código=");
      Serial.print(client.state());
      Serial.println(" -> Tentando novamente em 5 segundos.");
      delay(5000);
    }
  }
}

// ==========================================
// --- CALIBRAÇÃO DOS SENSORES ---
// ==========================================
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

// ==========================================
// --- SETUP ---
// ==========================================
void setup() {
  Serial.begin(115200);
  analogReadResolution(12);
  
  setup_wifi();
  calibrarInicial();
  
  client.setServer(mqtt_server, mqtt_port);

  // Inicia o timer em 1 MHz
  timer = timerBegin(1000000); 
  timerAttachInterrupt(timer, &onTimer);
  
  // Alarme de 5 minutos exatos (300.000.000 microssegundos)
  timerAlarm(timer, 300000000ULL, true, 0);
}

// ==========================================
// --- LOOP PRINCIPAL ---
// ==========================================
void loop() {
  // Mantém a conexão MQTT viva com o novo servidor
  if (!client.connected()) {
    reconnect();
  }
  client.loop();

  // Acionado pelo timer de 5 em 5 minutos
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
    
    // Publica no tópico do servidor Argus
    Serial.print("Publicando no tópico [");
    Serial.print(mqtt_topic);
    Serial.print("]: ");
    Serial.println(buffer);
    
    client.publish(mqtt_topic, buffer);
  }
}