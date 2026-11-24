#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>
#include <ArduinoJson.h>

const char* ssid = "YOUR_WIFI_SSID";
const char* password = "YOUR_WIFI_PASSWORD";

const char* host = "script.google.com";
const int httpsPort = 443;
WiFiClientSecure client;
String appscriptID = "AKfycbyhd9gwAtB575PEFBvXvFLO_UGfjT9qk4xHm5Ju1dEGGHO47dDP2tpUnUEi-KB4t4HO4w";

ESP8266WebServer server(80);
unsigned long tempos[4] = {1000, 2000, 3000, 4000};

void setup() {
  Serial.begin(9600);
  client.setInsecure();

  WiFi.begin(ssid, password);
  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
  }
  Serial.println("\nWi-Fi conectado!");
  Serial.println(WiFi.localIP());

  server.on("/", handleRoot);
  server.on("/update", handleUpdate);
  server.begin();
}

void loop() {
  server.handleClient();

  if (Serial.available()) {
    String data = Serial.readStringUntil('\n');
    data.trim();
    if (data.length() > 0) {
      enviarParaPlanilha(data);
    }
  }
}

void handleRoot() {
  String html = R"rawliteral(
    <!DOCTYPE html><html><head>
    <meta charset="UTF-8">
    <title>Controle de Ciclo</title>
    <style>
      body { font-family: Arial; background: #f2f2f2; text-align: center; margin-top: 50px; }
      .container { background: white; display: inline-block; padding: 30px; border-radius: 20px; box-shadow: 0 0 15px rgba(0,0,0,0.1); }
      input { width: 120px; padding: 8px; margin: 10px; border-radius: 8px; border: 1px solid #aaa; text-align: center; }
      button { padding: 10px 20px; background: #007BFF; color: white; border: none; border-radius: 10px; cursor: pointer; }
      button:hover { background: #0056b3; }
    </style></head><body>
    <div class="container">
      <h2>Controle de Ciclo - Válvulas e Bomba</h2>
      <form action="/update" method="GET">
        <label>Tempo 1 (ms):</label><br><input type="number" name="t1" value="%T1%"><br>
        <label>Tempo 2 (ms):</label><br><input type="number" name="t2" value="%T2%"><br>
        <label>Tempo 3 (ms):</label><br><input type="number" name="t3" value="%T3%"><br>
        <label>Tempo 4 (ms):</label><br><input type="number" name="t4" value="%T4%"><br><br>
        <button type="submit">Iniciar Ciclo</button>
      </form>
    </div>
    </body></html>
  )rawliteral";

  html.replace("%T1%", String(tempos[0]));
  html.replace("%T2%", String(tempos[1]));
  html.replace("%T3%", String(tempos[2]));
  html.replace("%T4%", String(tempos[3]));

  server.send(200, "text/html", html);
}

void handleUpdate() {
  if (server.hasArg("t1")) tempos[0] = server.arg("t1").toInt();
  if (server.hasArg("t2")) tempos[1] = server.arg("t2").toInt();
  if (server.hasArg("t3")) tempos[2] = server.arg("t3").toInt();
  if (server.hasArg("t4")) tempos[3] = server.arg("t4").toInt();

  String comando = "TEMPOS:";
  comando += String(tempos[0]) + ",";
  comando += String(tempos[1]) + ",";
  comando += String(tempos[2]) + ",";
  comando += String(tempos[3]);

  Serial.println(comando);
  delay(100);
  Serial.println("START");

  String resposta = "<html><body><h3>Ciclo iniciado com novos tempos!</h3>";
  resposta += "<p>" + comando + "</p><a href='/'>Voltar</a></body></html>";
  server.send(200, "text/html", resposta);
}

void enviarParaPlanilha(String data) {
  int gas1, gas2, gas3;
  float temp, umid;
  unsigned long tempo;

  // formato: gas1,gas2,gas3,temp,umid,tempo
  if (sscanf(data.c_str(), "%d,%d,%d,%f,%f,%lu", &gas1, &gas2, &gas3, &temp, &umid, &tempo) != 6) return;

  if (!client.connect(host, httpsPort)) {
    Serial.println("Falha ao conectar com o Google Script");
    return;
  }

  String url = "/macros/s/" + appscriptID + "/exec?";
  url += "gas1=" + String(gas1);
  url += "&gas2=" + String(gas2);
  url += "&gas3=" + String(gas3);
  url += "&temperatura=" + String(temp);
  url += "&umidade=" + String(umid);
  url += "&tempo=" + String(tempo); // <<< novo parâmetro

  client.print(String("GET ") + url + " HTTP/1.1\r\n" +
               "Host: " + host + "\r\n" +
               "Connection: close\r\n\r\n");

  Serial.println("Dados enviados ao Google Sheets com tempo: " + String(tempo));
}
