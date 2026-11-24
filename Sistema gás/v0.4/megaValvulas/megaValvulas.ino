#include <Wire.h>
#include "ClosedCube_HDC1080.h"

ClosedCube_HDC1080 hdc1080;

// ---------- CONFIGURAÇÃO DOS PINOS ----------
#define RELAY_COUNT 4
const uint8_t relayPins[RELAY_COUNT] = {22, 24, 26, 28}; // V1, V2, V3, BOMBA
#define V1 0
#define V2 1
#define V3 2
#define BOMBA 3

#define GAS_PINS_COUNT 3
const uint8_t gasPins[GAS_PINS_COUNT] = {A0, A1, A2};

// ---------- VARIÁVEIS ----------
volatile int gasReadings[GAS_PINS_COUNT];
volatile float tempReading = 0.0;
volatile float humReading = 0.0;
volatile bool readyToSend = false;
volatile uint8_t gasIndex = 0;
volatile uint8_t sendCounter = 0;

bool cicloAtivo = false;
unsigned long tempoTotal = 0;       // <<< NOVA VARIÁVEL GLOBAL DE TEMPO TOTAL
unsigned long lastStageTime = 0;
uint8_t currentStage = 0;
unsigned long stageDurations[4] = {1000, 2000, 3000, 4000};

// ---------- SETUP ----------
void setup() {
  Serial.begin(115200);
  Serial3.begin(9600);
  Wire.begin();
  hdc1080.begin(0x40);

  for (int i = 0; i < RELAY_COUNT; i++) {
    pinMode(relayPins[i], OUTPUT);
    digitalWrite(relayPins[i], HIGH);
  }

  cli();
  setupTimer1();
  setupTimer3();
  sei();

  Serial.println("Mega iniciado. Aguardando comando START do ESP...");
}

// ---------- TIMER1 ----------
void setupTimer1() {
  TCCR1A = 0;
  TCCR1B = (1 << WGM12) | (1 << CS12) | (1 << CS10);
  OCR1A = 19531;
  TIMSK1 |= (1 << OCIE1A);
}

// ---------- TIMER3 ----------
void setupTimer3() {
  TCCR3A = 0;
  TCCR3B = (1 << WGM32) | (1 << CS32) | (1 << CS30);
  OCR3A = 15625; // 1 s
  TIMSK3 |= (1 << OCIE3A);
}

// ---------- ISR: leitura dos sensores ----------
ISR(TIMER1_COMPA_vect) {
  if (cicloAtivo) {
    gasReadings[gasIndex] = analogRead(gasPins[gasIndex]);
    gasIndex = (gasIndex + 1) % GAS_PINS_COUNT;
  }
}

// ---------- ISR: trigger de envio ----------
ISR(TIMER3_COMPA_vect) {
  if (cicloAtivo) {
    sendCounter++;
    tempoTotal++; // <<< incremento contínuo (em segundos)
    if (sendCounter >= 5) {
      readyToSend = true;
      sendCounter = 0;
    }
  }
}

// ---------- LOOP ----------
void loop() {
  if (Serial3.available()) {
    String cmd = Serial3.readStringUntil('\n');
    cmd.trim();

    if (cmd.startsWith("TEMPOS:")) {
      sscanf(cmd.c_str(), "TEMPOS:%lu,%lu,%lu,%lu",
             &stageDurations[0], &stageDurations[1],
             &stageDurations[2], &stageDurations[3]);
      Serial.println("Novos tempos configurados!");
    }

    if (cmd == "START") {
      cicloAtivo = true;
      currentStage = 0;
      lastStageTime = millis();
      tempoTotal = 0; // <<< zera tempoTotal apenas no início de novo ciclo
      aplicarEtapa(currentStage);
      Serial.println("Ciclo iniciado!");
    }
  }

  if (cicloAtivo) {
    unsigned long currentTime = millis();

    if (currentTime - lastStageTime >= stageDurations[currentStage]) {
      currentStage = (currentStage + 1) % 4;
      lastStageTime = currentTime;
      aplicarEtapa(currentStage);
    }

    if (readyToSend) {
      tempReading = hdc1080.readTemperature();
      humReading = hdc1080.readHumidity();

      // Adiciona o "tempoTotal" ao final dos dados
      Serial3.print(gasReadings[0]); Serial3.print(",");
      Serial3.print(gasReadings[1]); Serial3.print(",");
      Serial3.print(gasReadings[2]); Serial3.print(",");
      Serial3.print(tempReading); Serial3.print(",");
      Serial3.print(humReading); Serial3.print(",");
      Serial3.println(tempoTotal);

      Serial.print("Tempo: "); Serial.print(tempoTotal); Serial.print("s ");
      Serial.print("G1:"); Serial.print(gasReadings[0]);
      Serial.print(" G2:"); Serial.print(gasReadings[1]);
      Serial.print(" G3:"); Serial.print(gasReadings[2]);
      Serial.print(" T:");  Serial.print(tempReading);
      Serial.print(" H:");  Serial.println(humReading);

      readyToSend = false;
    }
  }
}

// ---------- FUNÇÃO DE CONTROLE ----------
void aplicarEtapa(uint8_t etapa) {
  switch (etapa) {
    case 0:
      digitalWrite(relayPins[V1], LOW);
      digitalWrite(relayPins[V2], HIGH);
      digitalWrite(relayPins[V3], LOW);
      digitalWrite(relayPins[BOMBA], LOW);
      Serial.println("Etapa 1: V1 aberta, V2 fechada, V3 aberta, bomba ligada");
      break;
    case 1:
      digitalWrite(relayPins[V1], HIGH);
      digitalWrite(relayPins[V2], LOW);
      digitalWrite(relayPins[V3], LOW);
      digitalWrite(relayPins[BOMBA], LOW);
      Serial.println("Etapa 2: V1 fechada, V2 aberta, V3 aberta, bomba ligada");
      break;
    case 2:
      digitalWrite(relayPins[V1], HIGH);
      digitalWrite(relayPins[V2], HIGH);
      digitalWrite(relayPins[V3], HIGH);
      digitalWrite(relayPins[BOMBA], HIGH);
      Serial.println("Etapa 3: todas fechadas, bomba desligada");
      break;
    case 3:
      digitalWrite(relayPins[V1], LOW);
      digitalWrite(relayPins[V2], HIGH);
      digitalWrite(relayPins[V3], LOW);
      digitalWrite(relayPins[BOMBA], LOW);
      Serial.println("Etapa 4: igual ao tempo 1");
      break;
  }
}
