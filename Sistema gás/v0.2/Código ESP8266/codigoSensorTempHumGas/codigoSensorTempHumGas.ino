#include <ESP8266WiFi.h>
#include <ArduinoJson.h>
#include <DHT.h>

#define DHTPIN D1       
#define DHTTYPE DHT11   
DHT dht(DHTPIN, DHTTYPE);

#define MQ3_PIN A0  

//#define LED_PIN D2

const char* ssid = "YOUR_WIFI_SSID";    
const char* password = "YOUR_WIFI_PASSWORD";

const char* host = "script.google.com";
const int httpsPort = 443;

WiFiClientSecure client;

String appscriptID = "AKfycbzDHPptqWdnIqRfQVnnS4qKnCssIO9BgoVuwdiEvFUlAcmOzQvvITU-h9L6XJCuuF02aA";
WiFiServer server(80); 

String header;

void setup() {
  Serial.begin(115200); 
  dht.begin();          
  client.setInsecure();
  //pinMode(LED_PIN, OUTPUT);
  //digitalWrite(LED_PIN, HIGH);
  
  Serial.println("Conectando ao Wi-Fi...");
  WiFi.begin(ssid, password);
  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
  }
  Serial.println("\nWi-Fi conectado!");
  Serial.print("Endereço IP: ");
  Serial.println(WiFi.localIP());
  server.begin(); 
}

void loop(){

  if (WiFi.status() != WL_CONNECTED) {
    Serial.println("WiFi desconectado! Tentando reconectar...");
    WiFi.disconnect();
    WiFi.begin(ssid, password);
    while (WiFi.status() != WL_CONNECTED) {
        delay(500);
        Serial.print(".");
    }
    Serial.println("\nWi-Fi reconectado!");
  }
  int temperature = dht.readTemperature();
  delay(100);
  int humidity = dht.readHumidity();
  int mq3_value = analogRead(MQ3_PIN);

  if (isnan(temperature) || isnan(humidity)) {
      Serial.println("Erro ao ler o sensor DHT11.");
      temperature = -1; 
      humidity = -1;    
    }
  
  Serial.println(temperature);
  Serial.println(humidity);
  Serial.println(mq3_value);
  Serial.println(WiFi.macAddress());

  //adicionar uma coluna para colocar o tempo que foi lido os dados
  //implementar a interrupção  
  
  sendData(temperature, humidity, mq3_value); //--> Calls the sendData Subroutine
  delay(1000);
}

void sendData(float temperatura, float umidade, int gas) {
  Serial.println("==========");
  Serial.print("connecting to ");
  Serial.println(host);
  
  //----------------------------------------Connect to Google host
  if (!client.connect(host, httpsPort)) {
    Serial.println("connection failed");
    return;
  }

  //String string_temp =  String(temperatura, DEC);
  //String string_umi =  String(umidade, DEC); 
  //String string_gas =  String(gas, DEC); 
  String url = "/macros/s/" + appscriptID + "/exec?temperatura=" + temperatura + "&umidade=" + umidade + "&gas=" + gas;
  Serial.print("requesting URL: ");
  Serial.println(url);

  client.print(String("GET ") + url + " HTTP/1.1\r\n" +
         "Host: " + host + "\r\n" +
         "User-Agent: BuildFailureDetectorESP8266\r\n" +
         "Connection: close\r\n\r\n");

  Serial.println("request sent");
  //----------------------------------------

  //----------------------------------------Checking whether the data was sent successfully or not
  while (client.connected()) {
    String line = client.readStringUntil('\n');
    if (line == "\r") {
      Serial.println("headers received");
      break;
    }
  }
  String line = client.readStringUntil('\n');
  if (line.startsWith("{\"state\":\"success\"")) {
    Serial.println("esp8266/Arduino CI successfull!");
  } else {
    Serial.println("esp8266/Arduino CI has failed");
  }
  Serial.print("reply was : ");
  Serial.println(line);
  Serial.println("closing connection");
  Serial.println("==========");
  Serial.println();
  //----------------------------------------
} 