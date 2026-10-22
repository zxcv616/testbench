// ESP8266 / ESP-01 serial test firmware. No GPIO outputs are changed in M1.
#include <Arduino.h>

static const char* VERSION = "0.1.0";
static char command[128];
static size_t length = 0;
static bool overflow = false;

void handleCommand(const char* line) {
  if (strcmp(line, "PING") == 0) {
    Serial.println("OK PONG");
  } else if (strcmp(line, "VERSION") == 0) {
    Serial.print("OK ");
    Serial.println(VERSION);
  } else if (strcmp(line, "UPTIME") == 0) {
    Serial.print("OK ");
    Serial.println(millis());
  } else if (strncmp(line, "ECHO ", 5) == 0) {
    Serial.print("OK ");
    Serial.println(line + 5);
  } else {
    Serial.println("ERR unknown command");
  }
}

void setup() {
  Serial.begin(115200);
}

void loop() {
  while (Serial.available() > 0) {
    char ch = static_cast<char>(Serial.read());
    if (ch == '\n') {
      if (overflow) {
        Serial.println("ERR command too long");
      } else {
        if (length > 0 && command[length - 1] == '\r') length--;
        command[length] = '\0';
        handleCommand(command);
      }
      length = 0;
      overflow = false;
    } else if (!overflow) {
      if (length < sizeof(command) - 1) command[length++] = ch;
      else overflow = true;
    }
  }
  yield();
}
