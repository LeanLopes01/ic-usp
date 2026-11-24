#include <WiFi.h>
#include <esp_eap_client.h>
#include <esp_wifi.h>
#include <PubSubClient.h>
#include <ArduinoJson.h>
#include <math.h>

// =======================================================
// --- Configurações de Rede (eduroam) ---
// =======================================================
const char* ssid = "eduroam";
const char* EAP_ANONYMOUS = "anonymous@usp.br"; 
const char* EAP_IDENTITY = "14588023@usp.br"; 
const char* EAP_PASSWORD = "YOUR_EDUROAM_PASSWORD";           

// =======================================================
// --- Configurações Argus (Servidor MQTT) ---
// =======================================================
const char* mqtt_server   = "143.107.119.55"; 
const int   mqtt_port     = 1883;
const char* mqtt_user     = "argus-backend";   
const char* mqtt_pass     = "YOUR_MQTT_PASSWORD";     
const char* device_id     = "esp2-quality-hash-2640-jhgf"; 
const char* mqtt_topic    = "argus/esp2-quality-hash-2640-jhgf/telemetry"; 

// ==========================================
// --- CONSTANTES TÉCNICAS E PINAGEM ---
// ==========================================
const int pinoMQ9   = 36; // Pino 1 (VP)
const int pinoMQ7   = 39; // Pino 2 (VN)
const int pinoMQ3   = 34; // Pino 3 (34)
const int pinoMQ135 = 35; // Pino 4 (35)
const int pinoTeste = 32; // Pino 5 (32) - Sem uso / Retornar Zero
const int pinoMQ2   = 33; // Pino 6 (33)

const float RL = 10.0;      
const float VOLT_RES = 3.3; 
const float VIN_SENS = 5.0; 

// Fatores de Ar Limpo (RS/R0) - Valores típicos de datasheet
const float RATIO_AR_MQ9   = 9.80;
const float RATIO_AR_MQ7   = 27.0; 
const float RATIO_AR_MQ3   = 60.0; 
const float RATIO_AR_MQ135 = 3.60;
const float RATIO_AR_MQ2   = 9.83; 

// Coeficientes para conversão PPM (m, b) baseados em curvas de gases padrão
const float MQ9_m   = -0.45;  const float MQ9_b   = 1.25;  // CO/GLP
const float MQ7_m   = -0.65;  const float MQ7_b   = 1.40;  // CO
const float MQ3_m   = -0.66;  const float MQ3_b   = 0.44;  // Álcool
const float MQ135_m = -0.417; const float MQ135_b = 0.858; // NH3/CO2
const float MQ2_m   = -0.45;  const float MQ2_b   = 1.25;  // Fumaça/GLP

float R0_MQ9, R0_MQ7, R0_MQ3, R0_MQ135, R0_MQ2;

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
// --- Função de Conexão Wi-Fi ---
// =======================================================
void setup_wifi() {
  delay(10);
  Serial.println("\nIniciando tentativa de conexão na rede eduroam...");

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
  Serial.print("IP: "); Serial.println(WiFi.localIP());
}

void reconnect() {
  while (!client.connected()) {
    Serial.print("Conectando ao MQTT...");
    if (client.connect(device_id, mqtt_user, mqtt_pass)) {
      Serial.println(" OK!");
    } else {
      Serial.print(" Falhou ("); Serial.print(client.state()); Serial.println("). Tentando em 5s...");
      delay(5000);
    }
  }
}

// ==========================================
// --- CALIBRAÇÃO DOS SENSORES ---
// ==========================================
void calibrarInicial() {
  Serial.println("Calibrando sensores em ar limpo (Aguarde 5s)...");
  float rs_mq9 = 0, rs_mq7 = 0, rs_mq3 = 0, rs_mq135 = 0, rs_mq2 = 0;
  
  for(int i=0; i<10; i++) {
    // Leitura de descarte no pino teste (aterrado) para limpar o multiplexador do ADC
    analogRead(pinoTeste);
    
    rs_mq9   += calcularRS(calcularVoltagem(analogRead(pinoMQ9)));
    rs_mq7   += calcularRS(calcularVoltagem(analogRead(pinoMQ7)));
    rs_mq3   += calcularRS(calcularVoltagem(analogRead(pinoMQ3)));
    rs_mq135 += calcularRS(calcularVoltagem(analogRead(pinoMQ135)));
    rs_mq2   += calcularRS(calcularVoltagem(analogRead(pinoMQ2)));
    delay(500);
  }
  
  R0_MQ9   = (rs_mq9 / 10.0) / RATIO_AR_MQ9;
  R0_MQ7   = (rs_mq7 / 10.0) / RATIO_AR_MQ7;
  R0_MQ3   = (rs_mq3 / 10.0) / RATIO_AR_MQ3;
  R0_MQ135 = (rs_mq135 / 10.0) / RATIO_AR_MQ135;
  R0_MQ2   = (rs_mq2 / 10.0) / RATIO_AR_MQ2;
  
  Serial.println("Calibração concluída!");
}

// ==========================================
// --- SETUP ---
// ==========================================
void setup() {
  Serial.begin(115200);
  analogReadResolution(12);
  
  pinMode(pinoTeste, INPUT_PULLDOWN);
  
  setup_wifi();
  calibrarInicial();
  
  client.setServer(mqtt_server, mqtt_port);
  
  // ==========================================
  // CORREÇÃO: Aumenta o limite do MQTT para 1024 bytes
  // ==========================================
  client.setBufferSize(1024); 

  timer = timerBegin(1000000); 
  timerAttachInterrupt(timer, &onTimer);
  timerAlarm(timer, 5000000ULL, true, 0); 
}

// ==========================================
// --- LOOP PRINCIPAL ---
// ==========================================
void loop() {
  if (!client.connected()) reconnect();
  client.loop();

  if (flagLerSensores) {
    flagLerSensores = false;

    // 1. Limpeza do ADC lendo o pino de teste (aterrado pelo pulldown)
    analogRead(pinoTeste); 

    // 2. Leitura real dos sensores
    float v_mq9   = calcularVoltagem(analogRead(pinoMQ9));
    float v_mq7   = calcularVoltagem(analogRead(pinoMQ7));
    float v_mq3   = calcularVoltagem(analogRead(pinoMQ3));
    float v_mq135 = calcularVoltagem(analogRead(pinoMQ135));
    float v_mq2   = calcularVoltagem(analogRead(pinoMQ2));

    // 3. Cálculos de RS
    float rs_mq9   = calcularRS(v_mq9);
    float rs_mq7   = calcularRS(v_mq7);
    float rs_mq3   = calcularRS(v_mq3);
    float rs_mq135 = calcularRS(v_mq135);
    float rs_mq2   = calcularRS(v_mq2);

    // 4. Cálculos de Ratio
    float r_mq9   = rs_mq9 / R0_MQ9;
    float r_mq7   = rs_mq7 / R0_MQ7;
    float r_mq3   = rs_mq3 / R0_MQ3;
    float r_mq135 = rs_mq135 / R0_MQ135;
    float r_mq2   = rs_mq2 / R0_MQ2;

    // 5. Cálculos de PPM
    float ppm_mq9   = calcularPPM(r_mq9, MQ9_m, MQ9_b);
    float ppm_mq7   = calcularPPM(r_mq7, MQ7_m, MQ7_b);
    float ppm_mq3   = calcularPPM(r_mq3, MQ3_m, MQ3_b);
    float ppm_mq135 = calcularPPM(r_mq135, MQ135_m, MQ135_b);
    float ppm_mq2   = calcularPPM(r_mq2, MQ2_m, MQ2_b);

    // --- IMPRESSÃO DOS DADOS NO SERIAL ---
    Serial.println("\n--- Novas Leituras (5s) ---");
    Serial.printf("MQ9   | Tensão: %.2f V | RS: %6.2f | Ratio: %.2f | PPM: %.2f\n", v_mq9, rs_mq9, r_mq9, ppm_mq9);
    Serial.printf("MQ7   | Tensão: %.2f V | RS: %6.2f | Ratio: %.2f | PPM: %.2f\n", v_mq7, rs_mq7, r_mq7, ppm_mq7);
    Serial.printf("MQ3   | Tensão: %.2f V | RS: %6.2f | Ratio: %.2f | PPM: %.2f\n", v_mq3, rs_mq3, r_mq3, ppm_mq3);
    Serial.printf("MQ135 | Tensão: %.2f V | RS: %6.2f | Ratio: %.2f | PPM: %.2f\n", v_mq135, rs_mq135, r_mq135, ppm_mq135);
    Serial.printf("MQ2   | Tensão: %.2f V | RS: %6.2f | Ratio: %.2f | PPM: %.2f\n", v_mq2, rs_mq2, r_mq2, ppm_mq2);
    Serial.println("---------------------------");

    // 6. Montagem do JSON
    StaticJsonDocument<1024> doc;
    
    JsonObject j_mq9 = doc.createNestedObject("mq9");
    j_mq9["tensao"] = v_mq9; j_mq9["rs"] = rs_mq9; j_mq9["ratio"] = r_mq9; j_mq9["ppm"] = ppm_mq9;

    JsonObject j_mq7 = doc.createNestedObject("mq7");
    j_mq7["tensao"] = v_mq7; j_mq7["rs"] = rs_mq7; j_mq7["ratio"] = r_mq7; j_mq7["ppm"] = ppm_mq7;

    JsonObject j_mq3 = doc.createNestedObject("mq3");
    j_mq3["tensao"] = v_mq3; j_mq3["rs"] = rs_mq3; j_mq3["ratio"] = r_mq3; j_mq3["ppm"] = ppm_mq3;

    JsonObject j_mq135 = doc.createNestedObject("mq135");
    j_mq135["tensao"] = v_mq135; j_mq135["rs"] = rs_mq135; j_mq135["ratio"] = r_mq135; j_mq135["ppm"] = ppm_mq135;

    JsonObject j_mq2 = doc.createNestedObject("mq2");
    j_mq2["tensao"] = v_mq2; j_mq2["rs"] = rs_mq2; j_mq2["ratio"] = r_mq2; j_mq2["ppm"] = ppm_mq2;

    // Objeto de teste, retornando zero cravado via software
    JsonObject j_teste = doc.createNestedObject("teste");
    j_teste["tensao"] = 0.0; j_teste["rs"] = 0.0; j_teste["ratio"] = 0.0; j_teste["ppm"] = 0.0;

    char buffer[1024];
    serializeJson(doc, buffer);
    
    Serial.print("Tamanho do pacote: ");
    Serial.print(strlen(buffer));
    Serial.println(" bytes");
    Serial.print("Publicando MQTT: ");
    Serial.println(buffer);
    
    // Verificação de sucesso no envio
    if (client.publish(mqtt_topic, buffer)) {
      Serial.println("-> SUCESSO: Dados entregues ao Argus!");
    } else {
      Serial.println("-> ERRO: O broker recusou a mensagem ou falha de conexão.");
    }
  }
}
