#include <Wire.h>
#include "ClosedCube_HDC1080.h"

ClosedCube_HDC1080 hdc1080;

#define GAS_PINS_COUNT 3
const uint8_t gasPins[GAS_PINS_COUNT] = {A0, A1, A2};

volatile int gasReadings[GAS_PINS_COUNT];
volatile float tempReading = 0.0;
volatile float humReading = 0.0;
volatile bool readyToSend = false;

// Contadores usados nas ISRs:
volatile uint8_t gasIndex = 0;
volatile uint8_t sendCounter = 0;

void setup() {
  Serial.begin(9600);
  Serial3.begin(115200); // comunicação com ESP8266
  Wire.begin();          // inicializa I2C para HDC1080
  hdc1080.begin(0x40);

  // opcional: configura HDC1080 (resolução default já ok); se quiser configurar, pode escrever em reg 0x02

  // desabilita interrupções durante setup de timers
  cli();
  setupTimer1();  // para gás, 1,25 s
  setupTimer3();  // para trigger do envio, 1 s
  sei();
}

// Timer1 (16-bit) – prescaler 1024 → tick = 1024/16e6 = 64 µs
// OCR1A = 1,25 s / 64 µs ≈ 19531
void setupTimer1() {
  TCCR1A = 0;
  // CTC, prescaler 1024
  TCCR1B = (1<<WGM12)|(1<<CS12)|(1<<CS10);
  OCR1A = 19531;
  TIMSK1 |= (1<<OCIE1A);
}

// Timer3 (16-bit) – prescaler 1024 → OCR3A = 1 s / 64 µs = 15625
void setupTimer3() {
  TCCR3A = 0;
  TCCR3B = (1<<WGM32)|(1<<CS32)|(1<<CS30);
  OCR3A = 15625;
  TIMSK3 |= (1<<OCIE3A);
}

// ISR ao completar 1,25 s: leitura rotativa de cada sensor de gás
ISR(TIMER1_COMPA_vect) {
  gasReadings[gasIndex] = analogRead(gasPins[gasIndex]);
  gasIndex = (gasIndex + 1) % GAS_PINS_COUNT;
}

// ISR a cada 1 s: conta até 5, então sinaliza envio
// (não lemos o HDC1080 aqui — Wire não pode ser usado em ISRs)
ISR(TIMER3_COMPA_vect) {
  sendCounter++;
  if (sendCounter >= 5) {
    readyToSend = true; // sinal para o loop ler HDC1080 e enviar
    sendCounter = 0;
  }
}



void loop() {
  if (readyToSend) {
    // lê HDC1080 (no loop — não usar Wire em ISR)
    tempReading = hdc1080.readTemperature();
    humReading  = hdc1080.readHumidity();

    // monta e envia via Serial3 para o ESP8266
    Serial3.print(gasReadings[0]); Serial3.print(",");
    Serial3.print(gasReadings[1]); Serial3.print(",");
    Serial3.print(gasReadings[2]); Serial3.print(",");
    Serial3.print(tempReading);   Serial3.print(",");
    Serial3.println(humReading);

    // echo no Serial USB (opcional)
    Serial.print("G1:"); Serial.print(gasReadings[0]);
    Serial.print(" G2:"); Serial.print(gasReadings[1]);
    Serial.print(" G3:"); Serial.print(gasReadings[2]);
    Serial.print(" T:");  Serial.print(tempReading);
    Serial.print(" H:");  Serial.println(humReading);

    readyToSend = false;
  }

  // aqui o loop principal fica livre para outras tarefas sem delay()
}
