#include <Arduino.h>
#include <ESP8266WiFi.h>
#if __has_include("TestBenchSecrets.h")
#include "TestBenchSecrets.h"
#else
#include "Config.example.h"
#endif

static const uint8_t RELAY_PIN = 0;
static const uint8_t CONTACT_PIN = 2;
static WiFiServer server(TCP_PORT);
static WiFiClient client;
static bool serving = false;
static bool hadClient = false;
static bool relayOn = false;
static unsigned long relayStarted = 0;
static unsigned long clientActivity = 0;

struct LineBuffer {
  char data[128];
  size_t length = 0;
  bool invalid = false;
};
static LineBuffer serialLine, tcpLine;

void setRelay(bool on) {
  if (!RELAY_ENABLED) return;
  digitalWrite(RELAY_PIN, (on == RELAY_ACTIVE_LOW) ? LOW : HIGH);
  relayOn = on;
  if (on) relayStarted = millis();
}

void handleCommand(const char* line, Print& output) {
  if (strcmp(line, "PING") == 0) output.println("OK PONG");
  else if (strcmp(line, "VERSION") == 0) output.println("OK 0.2.1");
  else if (strcmp(line, "UPTIME") == 0) { output.print("OK "); output.println(millis()); }
  else if (strncmp(line, "ECHO ", 5) == 0) { output.print("OK "); output.println(line + 5); }
  else if (strcmp(line, "IP") == 0) {
    if (WiFi.status() != WL_CONNECTED) output.println("ERR wifi disconnected");
    else { output.print("OK "); output.println(WiFi.localIP()); }
  } else if (strcmp(line, "RSSI") == 0) {
    if (WiFi.status() != WL_CONNECTED) output.println("ERR wifi disconnected");
    else { output.print("OK "); output.println(WiFi.RSSI()); }
  } else if (strcmp(line, "RELAY ON") == 0 || strcmp(line, "RELAY OFF") == 0) {
    if (!RELAY_ENABLED) output.println("ERR relay disabled: confirm board and wiring");
    else { setRelay(strcmp(line, "RELAY ON") == 0); output.println("OK"); }
  } else if (strcmp(line, "CONTACT") == 0) {
    if (!RELAY_ENABLED) output.println("ERR relay disabled: confirm board and wiring");
    else output.println(digitalRead(CONTACT_PIN) == LOW ? "OK CLOSED" : "OK OPEN");
  } else output.println("ERR unknown command");
}

void receive(Stream& input, Print& output, LineBuffer& buffer, bool network) {
  // Limit work per loop so a busy client cannot starve relay shutdown or Wi-Fi.
  for (int count = 0; count < 128 && input.available() > 0; count++) {
    int value = input.read();
    if (value < 0) break;
    if (network) clientActivity = millis();
    char ch = static_cast<char>(value);
    if (ch == '\n') {
      if (buffer.invalid) output.println("ERR invalid or oversized command");
      else {
        if (buffer.length && buffer.data[buffer.length - 1] == '\r') buffer.length--;
        buffer.data[buffer.length] = '\0';
        handleCommand(buffer.data, output);
      }
      buffer = LineBuffer{};
    } else if (!buffer.invalid) {
      if ((value < 32 && ch != '\r') || value > 126 || buffer.length >= sizeof(buffer.data) - 1)
        buffer.invalid = true;
      else buffer.data[buffer.length++] = ch;
    }
  }
}

void setup() {
  if (RELAY_ENABLED) {
    setRelay(false);
    pinMode(RELAY_PIN, OUTPUT);
    pinMode(CONTACT_PIN, INPUT_PULLUP);
  }
  Serial.begin(115200);
  WiFi.persistent(false);
  WiFi.mode(WIFI_STA);
  WiFi.setAutoReconnect(true);
  WiFi.setSleepMode(WIFI_NONE_SLEEP);
  if (strlen(WIFI_SSID)) WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
}

void loop() {
  if (relayOn && millis() - relayStarted >= 5000) setRelay(false);
  if (WiFi.status() != WL_CONNECTED) {
    if (serving) { client.stop(); server.stop(); serving = false; hadClient = false; setRelay(false); tcpLine = LineBuffer{}; }
  } else {
    if (!serving) { server.begin(); server.setNoDelay(true); serving = true; }
    if (hadClient && (!client.connected() || millis() - clientActivity >= 5000)) {
      client.stop(); hadClient = false; setRelay(false); tcpLine = LineBuffer{};
    }
    if (!client || !client.connected()) {
      tcpLine = LineBuffer{};
      client = server.accept();
      if (client) { hadClient = true; setRelay(false); serialLine = LineBuffer{}; client.setNoDelay(true); clientActivity = millis(); }
    } else {
      WiFiClient extra = server.accept();
      if (extra) extra.stop(); // One owner at a time.
    }
    if (client && client.connected()) receive(client, client, tcpLine, true);
  }
  // Avoid two controllers driving the relay at the same time.
  if (!client || !client.connected()) receive(Serial, Serial, serialLine, false);
  yield();
}
