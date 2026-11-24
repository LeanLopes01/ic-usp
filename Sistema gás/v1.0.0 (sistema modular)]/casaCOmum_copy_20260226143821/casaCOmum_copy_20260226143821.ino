#include <WiFi.h>
#include <PubSubClient.h>
#include <ArduinoJson.h>
#include <math.h>

// ==========================================
// 1. CONFIGURAÇÕES DE REDE (CASA)
// ==========================================
const char* ssid = "YOUR_WIFI_SSID";       // <-- Mude aqui
const char* password = "YOUR_WIFI_PASSWORD";       // <-- Mude aqui
const char* mqtt_server = "192.168.15.24";        // IP do seu servidor local para teste em casa

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
// FUNÇÕES MATEMÁTICAS
// ==========================================

// Retorna a voltagem real do sensor (já revertendo o divisor de tensão)
float calcularVoltagem(int adc_val) {
  float v_out = (adc_val * VOLT_RES) / 4095.0;
  float v_sensor = v_out * 1.515; // Fator de correção do divisor
  
  // BLINDAGEM: Evita divisão por zero e limites que quebram o logaritmo
  if (v_sensor <= 0.1) v_sensor = 0.1; 
  if (v_sensor >= 4.9) v_sensor = 4.9; 
  
  return v_sensor;
}

// Calcula a resistência (RS)
float calcularRS(float v_sensor) {
  return ((VIN_SENS - v_sensor) * RL) / v_sensor;
}

// Calcula o PPM
float calcularPPM(float ratio, float m, float b) {
  if (ratio <= 0.01) ratio = 0.01; // Proteção extra: log10 de zero ou negativo não existe!
  return pow(10, ((log10(ratio) - b) / m));
}

// ==========================================
// SETUP E CONEXÃO
// ==========================================

void setup_wifi() {
  delay(10);
  Serial.println("\nConectando ao Wi-Fi...");
  WiFi.mode(WIFI_STA);
  WiFi.begin(ssid, password);
  while (WiFi.status() != WL_CONNECTED) { delay(500); Serial.print("."); }
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

// 1. Inicia o timer com frequência de 1 MHz (1 tick = 1 microssegundo)
  timer = timerBegin(1000000); 
  
  // 2. Vincula a função que será chamada
  timerAttachInterrupt(timer, &onTimer);
  
  // 3. Configura e liga o alarme de uma vez só!
  // Parâmetros: (ponteiro do timer, valor em microssegundos, auto-reload, limite de repetições [0 = infinito])
  // PARA TESTE EM CASA (5 segundos): 5000000
  // QUANDO FOR PARA A BANCADA: 300000000ULL (5 minutos)
  timerAlarm(timer, 5000000, true, 0);
}

void loop() {
  if (!client.connected()) {
    while (!client.connected()) {
      Serial.print("Conectando ao MQTT...");
      if (client.connect("ESP32_Gases_Casa")) Serial.println(" OK");
      else { Serial.print(" Falha. Erro: "); Serial.println(client.state()); delay(5000); }
    }
  }
  client.loop();

  if (flagLerSensores) {
    flagLerSensores = false;

    // 1. Lê a voltagem
    float v1 = calcularVoltagem(analogRead(pinoMQ135));
    float v2 = calcularVoltagem(analogRead(pinoMQ9));
    float v3 = calcularVoltagem(analogRead(pinoMQ5));

    // 2. Calcula RS
    float rs1 = calcularRS(v1);
    float rs2 = calcularRS(v2);
    float rs3 = calcularRS(v3);

    // 3. Calcula Ratio
    float ratio1 = rs1 / R0_MQ135;
    float ratio2 = rs2 / R0_MQ9;
    float ratio3 = rs3 / R0_MQ5;

    // 4. Calcula PPM
    float ppm1 = calcularPPM(ratio1, MQ135_m, MQ135_b);
    float ppm2 = calcularPPM(ratio2, MQ9_m, MQ9_b);
    float ppm3 = calcularPPM(ratio3, MQ5_m, MQ5_b);

    // 5. Monta o JSON Estruturado (Aumentamos o tamanho para 512 bytes)
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