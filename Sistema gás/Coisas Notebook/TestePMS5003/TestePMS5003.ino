struct pms5003data {
  uint16_t framelen;
  uint16_t pm10_standard, pm25_standard, pm100_standard;
  uint16_t pm10_env, pm25_env, pm100_env;
  uint16_t particles_03um, particles_05um, particles_10um, particles_25um, particles_50um, particles_100um;
  uint16_t unused;
  uint16_t checksum;
};

struct pms5003data data;
unsigned long ultimoLogTempo = 0;

void setup() {
  Serial.begin(9600);
  Serial1.begin(9600);
  
  // Esse loop força o Arduino a esperar você abrir o Monitor Serial para não perder os primeiros logs
  while (!Serial) {
    ; 
  }
  
  Serial.println("\n--- [SISTEMA INICIADO] ---");
  Serial.println("Aguardando comunicacao com o sensor PMS5003 na Serial1...");
}

boolean readPMSdata(Stream *s) {
  // Se não tem dados na Serial1, avisa a cada 3 segundos para sabermos que o código não travou
  if (!s->available()) {
    if (millis() - ultimoLogTempo > 3000) {
      Serial.println("[LOG] Aguardando dados na Serial1... (Nenhum byte recebido ainda)");
      ultimoLogTempo = millis();
    }
    return false;
  }

  // Se chegou algo, vamos olhar o primeiro byte
  if (s->peek() != 0x42) {
    Serial.print("[LOG] Byte inicial incorreto detectado: 0x");
    Serial.println(s->read(), HEX); // Descarta o byte errado e avisa qual era
    return false;
  }

  // Se o primeiro byte é 0x42, precisamos esperar o pacote de 32 bytes encher
  if (s->available() < 32) {
    // Não limpa o buffer, só espera o resto chegar no próximo loop
    return false;
  }

  Serial.println("[LOG] Pacote de 32 bytes recebido! Processando...");

  uint8_t buffer[32];
  uint16_t sum = 0;
  s->readBytes(buffer, 32);

  for (uint8_t i = 0; i < 30; i++) {
    sum += buffer[i];
  }

  if (buffer[0] != 0x42 || buffer[1] != 0x4d) {
    Serial.println("[ERRO] Cabecalho invalido! Esperado 0x42 e 0x4D.");
    return false;
  }

  uint16_t buffer_u16[15];
  for (uint8_t i = 0; i < 15; i++) {
    buffer_u16[i] = buffer[2 + i * 2 + 1];
    buffer_u16[i] += (buffer[2 + i * 2] << 8);
  }

  memcpy((void *)&data, (void *)buffer_u16, 30);

  if (sum != data.checksum) {
    Serial.print("[ERRO] Checksum falhou. Calculado: ");
    Serial.print(sum);
    Serial.print(" | Sensor enviou: ");
    Serial.println(data.checksum);
    return false;
  }

  return true;
}

void loop() {
  if (readPMSdata(&Serial1)) {
    Serial.println("===============================================");
    Serial.println("Concentracao Ambiental:");
    Serial.print("PM 1.0: "); Serial.print(data.pm10_env);
    Serial.print("\t\tPM 2.5: "); Serial.print(data.pm25_env);
    Serial.print("\t\tPM 10: "); Serial.println(data.pm100_env);
    Serial.println("===============================================\n");
    
    delay(1000); 
  }
}
