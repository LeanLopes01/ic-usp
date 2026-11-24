#include <ESP8266WiFi.h>
#include <ArduinoJson.h>

const char* ssid = "YOUR_WIFI_SSID";    
const char* password = "YOUR_WIFI_PASSWORD";
const char* host = "script.google.com";
const int httpsPort = 443;

WiFiClientSecure client;
String appscriptID = "AKfycbzkVbP7EMyqyTkm2iXz4fpTlvIInk9cXILn1VZBt_aOr75Wf5yqX1m97pqr2v_gsSYPYg";

void setup() {
    Serial.begin(115200);   // Comunicação com o Mega

    client.setInsecure();
    
    Serial.println("Conectando ao Wi-Fi...");
    WiFi.begin(ssid, password);
    while (WiFi.status() != WL_CONNECTED) {
        delay(500);
        Serial.print(".");
    }
    Serial.println("\nWi-Fi conectado!");
    Serial.print("Endereço IP: ");
    Serial.println(WiFi.localIP());
}

void loop() {
    if (Serial.available()) {
        String data = Serial.readStringUntil('\n');  // Lê até a quebra de linha
        data.trim();

        int gas1, gas2, gas3, gas4;
        float temp, umid;

        // Converte a string recebida para valores numéricos
        sscanf(data.c_str(), "%d,%d,%d,%f,%f", &gas1, &gas2, &gas3, &temp, &umid);

        // Armazena em um array (tupla)
        float valores[5] = { (float)gas1, (float)gas2, (float)gas3, temp, umid };

        // Exibe no Serial Monitor
        Serial.printf("Gás1: %d, Gás2: %d, Gás3: %d, Temp: %.2f°C, Umid: %.2f%%\n",
              (int)valores[0], (int)valores[1], (int)valores[2], valores[3], valores[4]);

        // Envia os dados para o Google Sheets
        sendData(valores);
    }
}

void sendData(float valores[6]) {
    Serial.println("==========");
    Serial.print("Conectando a ");
    Serial.println(host);
  
    if (!client.connect(host, httpsPort)) {
        Serial.println("Falha na conexão");
        return;
    }

    // Monta a URL com os valores recebidos
    String url = "/macros/s/" + appscriptID + "/exec?";
    url += "gas1=" + String(valores[0]);
    url += "&gas2=" + String(valores[1]);
    url += "&gas3=" + String(valores[2]);
    url += "&temperatura=" + String(valores[3]);
    url += "&umidade=" + String(valores[4]);

    Serial.print("Requisitando URL: ");
    Serial.println(url);

    client.print(String("GET ") + url + " HTTP/1.1\r\n" +
                 "Host: " + host + "\r\n" +
                 "User-Agent: ESP8266\r\n" +
                 "Connection: close\r\n\r\n");

    Serial.println("Requisição enviada");

    while (client.connected()) {
        String line = client.readStringUntil('\n');
        if (line == "\r") {
            Serial.println("Headers recebidos");
            break;
        }
    }
    String line = client.readStringUntil('\n');
    Serial.print("Resposta do servidor: ");
    Serial.println(line);
    Serial.println("Conexão encerrada");
    Serial.println("==========\n");
}
