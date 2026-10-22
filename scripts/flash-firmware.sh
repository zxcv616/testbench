#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
port="${1:-/dev/cu.usbserial-110}"
# Confirmed ESP-01: ESP8266EX, 26 MHz crystal, 1 MB flash.
fqbn='esp8266:esp8266:generic:eesz=1M,FlashMode=dout'
arduino-cli compile --fqbn "$fqbn" --output-dir build/firmware firmware/TestBench
arduino-cli upload --port "$port" --fqbn "$fqbn" --input-dir build/firmware firmware/TestBench
