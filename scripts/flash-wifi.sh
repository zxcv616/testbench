#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ ! -f firmware/TestBenchWifi/TestBenchSecrets.h ]]; then
  echo 'First run: python3 scripts/configure-wifi.py'
  exit 2
fi
port="${1:-/dev/cu.usbserial-110}"
fqbn='esp8266:esp8266:generic:eesz=1M,FlashMode=dout'
arduino-cli compile --fqbn "$fqbn" --output-dir build/wifi firmware/TestBenchWifi
arduino-cli upload --port "$port" --fqbn "$fqbn" --input-dir build/wifi firmware/TestBenchWifi
