# M4 Wi-Fi and relay firmware

This separate sketch preserves the working serial-only sketch. It joins an existing 2.4 GHz network and serves the same line protocol over TCP port 8266. One TCP client controls the device at a time; serial commands are serviced while no TCP client is connected. `IP` returns the assigned address and `RSSI` returns Wi-Fi signal strength. No credentials are sent in protocol replies.

## Current status

Wi-Fi hardware testing passed all six checks at `10.0.0.176` (address may change). Firmware 0.2.1 enables GPIO0 relay commands for an unloaded polarity check. The user observed OFF → ON → OFF during the HIGH → LOW → HIGH sequence, confirming the active-low setting on this board. This resolves the polarity uncertainty from the conflicting v4.0 references; actual contact continuity remains unverified. COM/NO/NC remain disconnected, and physical relay/contact acceptance is still pending.

## Configure and upload Wi-Fi

From the TestBench folder:

```sh
python3 scripts/configure-wifi.py
./scripts/flash-wifi.sh
```

The setup command prompts for the network name and a hidden password, saving them in `TestBenchSecrets.h` beside the sketch with owner-only file permissions. That file and the compiled binaries are ignored by Git. Running setup again replaces the settings and disables relay control. Build binaries also contain the credentials; do not share them. The supplied example header uses blank credentials and disables relay outputs, allowing a build without local configuration.

The upload script targets the previously detected ESP-01 with 1 MB flash. Keep it on its USB programmer for uploading. After upload, allow time for Wi-Fi to connect, then run:

```sh
dotnet run -- /dev/cu.usbserial-110 tests-wifi-serial.json
```

Opening USB serial can reset the board. This serial-only suite waits 30 seconds before its first command; the TCP suite does not need that delay. The `ip` result prints `OK` followed by the ESP's address. If Wi-Fi is disconnected, check the network name/password and 2.4 GHz availability, then retry. Use that address instead of `10.0.0.176` below:

```sh
dotnet run -- tcp://10.0.0.176:8266 tests-wifi.json
```

These six checks do not actuate the relay. Keep your Mac on a network that can reach the ESP; guest-network isolation can prevent this. The TCP protocol has no authentication or encryption and is intended for a trusted local test network; do not expose the port to the internet.

## Unloaded switching check

After uploading 0.2.1 on the USB programmer, disconnect USB, move the ESP to the unpowered relay board, then power the board from its 5 V DC adapter. Leave COM/NO/NC disconnected. Allow Wi-Fi to reconnect and run:

```sh
dotnet run -- tcp://10.0.0.176:8266 tests-relay-click.json
```

The sequence holds GPIO0 HIGH for two seconds, LOW for two seconds, then HIGH for two seconds. Test names identify the confirmed OFF → ON → OFF sequence. Observe the relay indicator/clicks in each phase; the console prints each result at the END of its two-second phase. PASS means the device acknowledged the command and still responds, not that the contacts moved or that the ON/OFF labels are correct. A continuity check or feedback wiring is needed to confirm actual contact state. Unplug the adapter before adding feedback wiring.

## Confirm the relay hardware before enabling it

Provide the board model or clear photos of both sides. Some ESP-01 relay boards use a separate serial controller instead of GPIO0, so they require different firmware.

For the GPIO0-controlled board in the project plan:

- Confirm its relay activation level. `RELAY_ACTIVE_LOW true` means GPIO0 LOW turns it on. Set it to `false` only for a confirmed active-high board.
- Power the relay module through its documented supply input. The bare ESP-01 uses 3.3 V; do not apply the module's 5 V supply to ESP GPIO pins.
- Use unpowered relay contacts for this feedback test: COM to GND, NO to GPIO2. Do not connect a load or external voltage to those contacts.
- GPIO2 must be HIGH during normal boot. A stuck closed contact that grounds it can prevent boot; the runner may report a connection failure instead of a contact mismatch. See the [ESP8266 boot-mode documentation](https://github.com/esp8266/esp8266-wiki/wiki/Boot-Process).

After board/polarity/wiring confirmation, set `RELAY_ENABLED true` in the local header, rebuild/upload, power down, and move the ESP-01 from the programmer to the relay module. Power the module and rerun with its current IP address:

```sh
dotnet run -- tcp://10.0.0.176:8266 tests-relay.json
```

The eight tests include initial OFF/open, ON/closed, and final OFF/open verification. `CONTACT` reads GPIO2; it never substitutes the last commanded relay state. Limits are 500 ms per test, including the 50 ms relay-settling wait.

When enabled, firmware applies the configured OFF level during startup, on TCP disconnect or Wi-Fi loss, and after five seconds of commanded ON time. The active-low setting has been confirmed by the observed indicator/click sequence; feedback is still required to detect stuck contacts. An idle TCP session expires after five seconds. These software measures do not guarantee a glitch-free relay state during reset, programming, or power loss; that depends on the board hardware.

## Checks without relay hardware

```sh
dotnet build
python3 checks/tcp_smoke.py
python3 checks/serial_smoke.py
```

The TCP fixture checks pass/fail evaluation, fragmented replies, stuck-open feedback, timeouts (including slow partial replies), disconnection, oversized replies, connection refusal, invalid endpoints, and CSV output. It is a local protocol fixture, not proof of ESP Wi-Fi or physical relay behavior.

Firmware uses the ESP8266 core's [server](https://arduino-esp8266.readthedocs.io/en/stable/esp8266wifi/server-examples.html) and [client](https://arduino-esp8266.readthedocs.io/en/stable/esp8266wifi/client-class.html) APIs.
