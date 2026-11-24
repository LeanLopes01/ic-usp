// ==========================================
// --- CONSTANTES TÉCNICAS E PINAGEM ---
// ==========================================
const int pinoMQ9   = 36; // VP
const int pinoMQ7   = 39; // VN
const int pinoMQ3   = 34; 
const int pinoMQ135 = 35; 

// ==========================================
// --- FUNÇÃO DE LEITURA BLINDADA ---
// ==========================================
// Faz duas leituras para limpar o multiplexador interno do ADC
int lerPinoAnalogico(int pino) {
  analogRead(pino); // Leitura fantasma de descarte
  delay(10);        // Dá tempo para o capacitor interno carregar
  return analogRead(pino); // Leitura valendo
}

// ==========================================
// --- SETUP ---
// ==========================================
void setup() {
  Serial.begin(115200);
  analogReadResolution(12);

  Serial.println("\n==================================");
  Serial.println("INICIANDO TESTE BRUTO (RAW)");
  Serial.println("==================================");
}

// ==========================================
// --- LOOP PRINCIPAL ---
// ==========================================
void loop() {
  // 1. Leitura bruta (0 a 4095)
  int raw_mq9   = lerPinoAnalogico(pinoMQ9);
  int raw_mq7   = lerPinoAnalogico(pinoMQ7);
  int raw_mq3   = lerPinoAnalogico(pinoMQ3);
  int raw_mq135 = lerPinoAnalogico(pinoMQ135);

  // 2. Conversão direta (SEM travar no mínimo de 0.1)
  float v_mq9   = (raw_mq9   * 3.3 / 4095.0) * 1.515;
  float v_mq7   = (raw_mq7   * 3.3 / 4095.0) * 1.515;
  float v_mq3   = (raw_mq3   * 3.3 / 4095.0) * 1.515;
  float v_mq135 = (raw_mq135 * 3.3 / 4095.0) * 1.515;

  // 3. Impressão dupla para diagnóstico
  Serial.println("\n--- Novas Leituras ---");
  Serial.printf("MQ9   (Porta %d) | Bruto (ADC): %4d | Voltagem: %.2f V\n", pinoMQ9, raw_mq9, v_mq9);
  Serial.printf("MQ7   (Porta %d) | Bruto (ADC): %4d | Voltagem: %.2f V\n", pinoMQ7, raw_mq7, v_mq7);
  Serial.printf("MQ3   (Porta %d) | Bruto (ADC): %4d | Voltagem: %.2f V\n", pinoMQ3, raw_mq3, v_mq3);
  Serial.printf("MQ135 (Porta %d) | Bruto (ADC): %4d | Voltagem: %.2f V\n", pinoMQ135, raw_mq135, v_mq135);
  Serial.println("----------------------");

  delay(2000); 
}
