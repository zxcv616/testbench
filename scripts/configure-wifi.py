#!/usr/bin/env python3
"""Write local Wi-Fi credentials without printing or committing them."""
from getpass import getpass
from pathlib import Path
import os

path = Path(__file__).resolve().parents[1] / 'firmware/TestBenchWifi/TestBenchSecrets.h'
ssid = input('2.4 GHz Wi-Fi network name: ')
password = getpass('Wi-Fi password (hidden): ')
if not ssid:
    raise SystemExit('Network name cannot be empty.')
if len(ssid.encode()) > 32 or len(password.encode()) > 63:
    raise SystemExit('Network name or password is too long.')

def literal(value):
    return '"' + ''.join('\\%03o' % byte for byte in value.encode('utf-8')) + '"'

content = ('#pragma once\n#define WIFI_SSID ' + literal(ssid) + '\n#define WIFI_PASSWORD ' + literal(password)
           + '\n#define RELAY_ENABLED false\n#define RELAY_ACTIVE_LOW true\n#define TCP_PORT 8266\n')
fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
os.fchmod(fd, 0o600)
with os.fdopen(fd, 'w') as stream:
    stream.write(content)
print('Saved local TestBenchSecrets.h. Relay control is disabled until board and wiring are confirmed.')
