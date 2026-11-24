#include <ESP8266WiFi.h>
#include <PubSubClient.h>

const char* ssid = "sua_rede_wifi";
const char* password = "sua_senha_wifi";

const char* mqtt_server = "IP_do_broker_MQTT";
const int mqtt_port = 1883;
const char* mqtt_topic = "sensor/gas";

WiFiClient espClient;
PubSubClient client(espClient);

void setup_wifi() {
  delay(10);
  Serial.println();
  Serial.print("Conectando a ");
  Serial.println(ssid);

  WiFi.begin(ssid, password);

  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
  }

  Serial.println("");
  Serial.println("WiFi conectado");
  Serial.println("IP: ");
  Serial.println(WiFi.localIP());
}

void reconnect() {
  while (!client.connected()) {
    Serial.print("Tentando conexão MQTT...");
    if (client.connect("ESP8266Client")) {
      Serial.println("Conectado");
    } else {
      Serial.print("Falhou, rc=");
      Serial.print(client.state());
      Serial.println(" Tentando novamente em 5 segundos");
      delay(5000);
    }
  }
}

void setup() {
  Serial.begin(115200);
  setup_wifi();
  client.setServer(mqtt_server, mqtt_port);
}

void loop() {
  if (!client.connected()) {
    reconnect();
  }
  client.loop();

  // Ler o valor do sensor de gás
  int valor_gas = analogRead(A0);

  // Converter o valor para string e enviar via MQTT
  String payload = String(valor_gas);
  client.publish(mqtt_topic, payload.c_str());

  // Aguarde 1 segundo antes da próxima leitura
  delay(1000);
}
