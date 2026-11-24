/*
 * ESP8266 (RobotDyn) - FIRMWARE DE REDE COM DEBUG (CORRIGIDO)
 */

#include <ESP8266WiFi.h>
#include <ESP8266HTTPClient.h>
#include <WiFiClientSecure.h>
#include <ArduinoJson.h>

// --- Configurações de Rede ---
const char* ssid = "YOUR_WIFI_SSID";
const char* password = "YOUR_WIFI_PASSWORD";

// URL EXATA do servidor
const char* serverUrl = "https://docjediwho.com.br/receiver.php"; 

// --- Configurações da API ---
String apiKey = "4cb88fad020ec520281e722b6692be593e71d9eca97e4c7e205b20857232c4d9"; 
String gasName = "A";
int repNum = 1;

// --- VARIÁVEL GLOBAL (MUITO IMPORTANTE) ---
// Ela precisa estar aqui fora para guardar o valor entre os loops
float tempoDecorrido = 0.0; 

void setup() {
  Serial.begin(115200);
  delay(1000); 

  Serial.println("\n\n--- INICIANDO ESP8266 ---");
  Serial.print("Conectando na rede: ");
  Serial.println(ssid);

  WiFi.mode(WIFI_STA);
  WiFi.begin(ssid, password);

  int tentativas = 0;
  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
    tentativas++;
    if (tentativas > 40) { 
       Serial.println("\n[ERRO] WiFi Timeout. Verifique senha.");
       tentativas = 0; 
    }
  }

  Serial.println("\n[SUCESSO] WiFi Conectado!");
  Serial.println("Aguardando dados do Mega...");
}

void loop() {
  if (Serial.available()) {
    String data = Serial.readStringUntil('\n');
    data.trim();

    if (data.length() > 0) {
      Serial.print("Recebido: ");
      Serial.println(data);
      processarEnvio(data);
    }
  }
}

void processarEnvio(String data) {
  int rMQ2, rMQ5, rMQ9;
  float rTemp, rUmid, rPress; 

  // Tenta ler os 6 valores: MQ2, MQ5, MQ9, Temp, Umid, Pressao
  int n = sscanf(data.c_str(), "%d,%d,%d,%f,%f,%f", &rMQ2, &rMQ5, &rMQ9, &rTemp, &rUmid, &rPress);

  // Se leu pelo menos 5 (a pressao pode falhar se o mega não mandar), envia
  if (n >= 5) { 
    // Incrementa a variável GLOBAL
    tempoDecorrido += 10.0;
    
    Serial.println("Dados OK. Iniciando envio HTTPS...");
    
    // Passa a pressão (rPress) para a função de envio
    enviarHTTP(rMQ2, rMQ5, rMQ9, rTemp, rUmid, rPress);
  } else {
    Serial.println("[ERRO] Formato invalido.");
  }
}

// CORREÇÃO AQUI: Adicionado 'float valPress' nos argumentos
void enviarHTTP(int valMQ2, int valMQ5, int valMQ9, float valTemp, float valUmid, float valPress) {
  
  WiFiClientSecure client;
  client.setInsecure(); // Ignora SSL (Necessário)
  client.setTimeout(15000); 

  HTTPClient http;

  if (http.begin(client, serverUrl)) {
    http.addHeader("Content-Type", "application/json");

    // Monta o JSON
    StaticJsonDocument<384> doc;
    doc["api_key"] = apiKey;
    doc["gas"]     = gasName; // Opcional no novo sistema, mas mantido
    
    doc["mq2"]     = valMQ2;
    doc["mq3"]     = valMQ9; // Mapeia MQ9 físico para coluna mq3 no banco
    doc["mq5"]     = valMQ5;
    
    doc["temp"]    = valTemp;
    doc["umid"]    = valUmid;
    doc["press"]   = valPress; // Adicionado Pressão

    // Campos legados (para não quebrar compatibilidade se houver)
    doc["config"]  = 0; 
    doc["rep"]     = repNum;
    doc["tempo"]   = tempoDecorrido; // Envia o tempo acumulado

    String jsonStr;
    serializeJson(doc, jsonStr);

    Serial.println("Enviando POST...");
    int httpCode = http.POST(jsonStr);

    if (httpCode > 0) {
      Serial.print("[HTTP] Codigo: ");
      Serial.println(httpCode);
      
      if (httpCode == 200 || httpCode == 201) {
        String payload = http.getString();
        Serial.print("[SERVER] Sucesso: ");
        Serial.println(payload);
      } else {
        String payload = http.getString();
        Serial.print("[SERVER] Erro: ");
        Serial.println(payload);
      }
    } else {
      Serial.print("[ERRO CONEXAO]: ");
      Serial.println(http.errorToString(httpCode).c_str());
    }
    http.end();
  } else {
    Serial.println("[ERRO] Falha ao iniciar HTTPClient.");
  }
}