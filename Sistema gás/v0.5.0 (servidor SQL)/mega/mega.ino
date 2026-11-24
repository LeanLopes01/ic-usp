/*
 * ARDUINO MEGA - HARDWARE & TIMERS (Low Level)
 * Sensores Físicos: MQ2(A0), MQ5(A1), MQ9(A2)
 * Sensores Simulados: BME280 (Temp, Umid, Pressão) -> Valores Aleatórios
 * Comunicação: Serial3 com ESP8266
 */

#include <Wire.h>
#include "TimerOne.h"

// --- Hardware ---
// (Biblioteca BME280/HDC1080 removida pois o sensor não está funcionando)

#define GAS_PINS_COUNT 3
// Ordem física: A0=MQ2, A1=MQ5, A2=MQ9
const uint8_t gasPins[GAS_PINS_COUNT] = {A0, A1, A2}; 

// --- Variáveis Voláteis (Para ISR) ---
volatile int gasReadings[GAS_PINS_COUNT];
volatile bool readyToSend = false;

// Contadores e Índices
volatile uint8_t gasIndex = 0;
volatile uint8_t sendCounter = 0;

void setup() {
  Serial.begin(115200);   // Debug USB
  Serial3.begin(115200);  // Comunicação ESP

  // Inicializa a semente randomica lendo uma porta analógica flutuante (A15)
  // Isso garante que os números aleatórios mudem toda vez que reiniciar
  randomSeed(analogRead(A15));

  // Inicializa leituras de gás com zero
  for(int i=0; i<GAS_PINS_COUNT; i++) gasReadings[i] = 0;

  // --- Configuração dos Timers (Registradores) ---
  cli(); // Desabilita interrupções globalmente durante setup

  setupTimer1();  // Leitura rotativa dos gases (1.25s)
  setupTimer3();  // Trigger de envio (1.0s base)

  sei(); // Habilita interrupções globalmente
  
  Serial.println("Sistema Iniciado. Modo Simulação BME280 Ativo.");
}

// Timer1 (16-bit): Leitura rotativa de sensores de gás
// Prescaler 1024 -> Tick = 64us. OCR1A = 19531 ~= 1.25s
void setupTimer1() {
  TCCR1A = 0;
  TCCR1B = (1<<WGM12) | (1<<CS12) | (1<<CS10);
  OCR1A = 19531; 
  TIMSK1 |= (1<<OCIE1A); 
}

// Timer3 (16-bit): Base de tempo para envio
// Prescaler 1024 -> Tick = 64us. OCR3A = 15625 ~= 1.00s
void setupTimer3() {
  TCCR3A = 0;
  TCCR3B = (1<<WGM32) | (1<<CS32) | (1<<CS30);
  OCR3A = 15625;
  TIMSK3 |= (1<<OCIE3A);
}

// ISR Timer 1: Leitura rotativa dos MQ (Esses são reais)
ISR(TIMER1_COMPA_vect) {
  gasReadings[gasIndex] = analogRead(gasPins[gasIndex]);
  gasIndex = (gasIndex + 1) % GAS_PINS_COUNT;
}

// ISR Timer 3: Controle de tempo de envio (10s)
ISR(TIMER3_COMPA_vect) {
  sendCounter++;
  if (sendCounter >= 10) {
    readyToSend = true; 
    sendCounter = 0;
  }
}

void loop() {
  if (readyToSend) {
    // --- GERAÇÃO DE VALORES ALEATÓRIOS (SIMULAÇÃO) ---
    // Temperatura: entre 20.00 e 30.00 graus
    float simTemp = random(2000, 3000) / 100.0;
    
    // Umidade: entre 40.00 e 70.00 %
    float simHum = random(4000, 7000) / 100.0;
    
    // Pressão: entre 1008.0 e 1018.0 hPa
    float simPress = random(10080, 10180) / 10.0;

    // --- ENVIO ---
    // Formato CSV: MQ2, MQ5, MQ9, Temp, Umid, Pressao
    
    Serial3.print(gasReadings[0]); Serial3.print(","); // MQ2
    Serial3.print(gasReadings[1]); Serial3.print(","); // MQ5
    Serial3.print(gasReadings[2]); Serial3.print(","); // MQ9
    Serial3.print(simTemp);        Serial3.print(","); // Temp (Simulado)
    Serial3.print(simHum);         Serial3.print(","); // Umid (Simulado)
    Serial3.println(simPress);     // Press (Simulado) - println no último

    // Debug USB
    Serial.print("Enviado (Simulado): T="); Serial.print(simTemp);
    Serial.print(" H="); Serial.print(simHum);
    Serial.print(" P="); Serial.println(simPress);

    readyToSend = false;
  }
}