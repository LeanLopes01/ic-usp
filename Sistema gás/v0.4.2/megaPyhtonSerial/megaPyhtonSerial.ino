#include <Wire.h>
#include "ClosedCube_HDC1080.h"

ClosedCube_HDC1080 hdc1080;

// ---------- PINOS ----------
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
float tempReading = 0.0;
float humReading = 0.0;

volatile bool cicloAtivo = false;
volatile bool cicloFinalizado = false;

unsigned long tempoTotal = 0;
unsigned long ultimaLeituraSec = 0;

// Durações das etapas (ms)
unsigned long stageDurations[6] = {0UL, 10000UL, 10000UL, 10000UL, 10000UL, 10000UL};

unsigned long lastStageTime = 0;
uint8_t currentStage = 0;

// Flags de sincronização
volatile bool gasReadFlag = false;
volatile uint8_t gasIndexISR = 0;
volatile uint8_t gasISRCount = 0;
volatile bool tempHumFlag = false;

// ---------- SETUP ----------
void setup() {
  Serial.begin(115200);
  while (!Serial) {}

  Serial.println("Tempo(s),MQ2(A0),MQ3(A1),MQ5(A2),Temperatura(C),Umidade(%)");

  Wire.begin();
  hdc1080.begin(0x40);

  for (int i = 0; i < RELAY_COUNT; i++) {
    pinMode(relayPins[i], OUTPUT);
    digitalWrite(relayPins[i], HIGH);
  }

  // Configura Timer1 para 250 ms
  noInterrupts();
  TCCR1A = 0;
  TCCR1B = (1 << WGM12);
  TCCR1B |= (1 << CS11) | (1 << CS10); // prescaler = 64
  OCR1A = 62499;
  TIMSK1 |= (1 << OCIE1A);
  interrupts();

  lastStageTime = millis();

  Serial.println("Sistema pronto. Use comandos: START, STOP, RESET, SHOW, SET <etapa> <tempo_ms>");
}

// ---------- ISR Timer1 ----------
ISR(TIMER1_COMPA_vect) {
  if (cicloAtivo && !cicloFinalizado) {
    gasReadFlag = true;
    gasIndexISR = (gasIndexISR + 1) % GAS_PINS_COUNT;

    gasISRCount++;
    if (gasISRCount >= GAS_PINS_COUNT) {
      gasISRCount = 0;
      tempHumFlag = true;
    }
  }
}

// ---------- LOOP ----------
void loop() {
  // --------- COMANDOS SERIAL ---------
  if (Serial.available()) {
    String cmd = Serial.readStringUntil('\n');
    cmd.trim();

    if (cmd.startsWith("SET")) {
      int etapa, tempo;
      if (sscanf(cmd.c_str(), "SET %d %d", &etapa, &tempo) == 2) {
        if (etapa >= 0 && etapa < 6) {
          stageDurations[etapa] = tempo;
          Serial.print("Etapa ");
          Serial.print(etapa);
          Serial.print(" tempo atualizado para ");
          Serial.print(tempo);
          Serial.println(" ms");
        } else {
          Serial.println("Erro: etapa deve ser 0 a 5");
        }
      } else {
        Serial.println("Uso: SET <etapa> <tempo_ms>");
      }
    } 
    else if (cmd == "SHOW") {
      Serial.println("Tempos atuais (ms):");
      for (int i = 0; i < 6; i++) {
        Serial.print("Etapa "); Serial.print(i);
        Serial.print(": "); Serial.println(stageDurations[i]);
      }
    }
    else if (cmd == "START" && !cicloAtivo && !cicloFinalizado) {
      cicloAtivo = true;
      cicloFinalizado = false;
      currentStage = 0;
      lastStageTime = millis();
      tempoTotal = 0;
      aplicarEtapa(currentStage);
      Serial.println("CICLO STARTED");
    } 
    else if (cmd == "STOP") {
      cicloAtivo = false;
      cicloFinalizado = true;
      aplicarEtapa(5);
      Serial.println("CICLO STOPPED");
    } 
    else if (cmd == "RESET") {
      cicloAtivo = false;
      cicloFinalizado = false;
      currentStage = 0;
      tempoTotal = 0;
      aplicarEtapa(5);
      Serial.println("RESET");
    }
  }

  // --------- LEITURA DE SENSORES ---------
  static uint8_t nextToRead = 0;
  if (gasReadFlag) {
    uint8_t idx = nextToRead % GAS_PINS_COUNT;
    gasReadings[idx] = analogRead(gasPins[idx]);
    nextToRead = (nextToRead + 1) % GAS_PINS_COUNT;
    gasReadFlag = false;
  }

  if (tempHumFlag) {
    tempReading = hdc1080.readTemperature();
    humReading = hdc1080.readHumidity();
    tempHumFlag = false;
  }

  // --------- CONTROLE DE ETAPAS ---------
  if (cicloAtivo && !cicloFinalizado) {
    unsigned long agora = millis();
    if (agora - lastStageTime >= stageDurations[currentStage]) {
      currentStage++;
      lastStageTime = agora;

      if (currentStage < 6) {
        aplicarEtapa(currentStage);
      } else {
        aplicarEtapa(5);
        cicloAtivo = false;
        cicloFinalizado = true;
        Serial.println("CICLO FINALIZADO");
      }
    }

    // Envia dados CSV a cada 1s
    if (agora - ultimaLeituraSec >= 1000) {
      ultimaLeituraSec = agora;
      Serial.print(tempoTotal); Serial.print(",");
      Serial.print(gasReadings[0]); Serial.print(",");
      Serial.print(gasReadings[1]); Serial.print(",");
      Serial.print(gasReadings[2]); Serial.print(",");
      Serial.print(tempReading); Serial.print(",");
      Serial.println(humReading);
      tempoTotal++;
    }
  }

  delay(1);
}

// ---------- CONTROLE DE RELÉS ----------
void aplicarEtapa(uint8_t etapa) {
  switch (etapa) {
    case 0:
      digitalWrite(relayPins[V1], LOW);
      digitalWrite(relayPins[V2], LOW);
      digitalWrite(relayPins[V3], LOW);
      digitalWrite(relayPins[BOMBA], LOW);
      Serial.println("Etapa 0");
      break;

    case 1:
      digitalWrite(relayPins[V1], LOW);
      digitalWrite(relayPins[V2], HIGH);
      digitalWrite(relayPins[V3], LOW);
      digitalWrite(relayPins[BOMBA], LOW);
      Serial.println("Etapa 1");
      break;

    case 2:
      digitalWrite(relayPins[V1], HIGH);
      digitalWrite(relayPins[V2], LOW);
      digitalWrite(relayPins[V3], LOW);
      digitalWrite(relayPins[BOMBA], LOW);
      Serial.println("Etapa 2");
      break;

    case 3:
      digitalWrite(relayPins[V1], HIGH);
      digitalWrite(relayPins[V2], HIGH);
      digitalWrite(relayPins[V3], HIGH);
      digitalWrite(relayPins[BOMBA], HIGH);
      Serial.println("Etapa 3");
      break;

    case 4:
      digitalWrite(relayPins[V1], LOW);
      digitalWrite(relayPins[V2], HIGH);
      digitalWrite(relayPins[V3], HIGH);
      digitalWrite(relayPins[BOMBA], HIGH);
      Serial.println("Etapa 4");
      break;

    case 5:
      digitalWrite(relayPins[V1], LOW);
      digitalWrite(relayPins[V2], LOW);
      digitalWrite(relayPins[V3], LOW);
      digitalWrite(relayPins[BOMBA], LOW);
      Serial.println("Etapa 5 (final) - tudo desligado");
      break;

    default:
      digitalWrite(relayPins[V1], LOW);
      digitalWrite(relayPins[V2], LOW);
      digitalWrite(relayPins[V3], LOW);
      digitalWrite(relayPins[BOMBA], LOW);
      Serial.println("Etapa desconhecida - tudo desligado");
      break;
  }
}
