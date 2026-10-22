# TestBench

A C#/.NET serial test station for an ESP-01 (ESP8266). The default `tests.json` runs four checks in order: `PING`, `VERSION`, `UPTIME`, and `ECHO hello`. Each has a 200 ms command/reply budget and prints a measured duration. The final summary reports how many passed; the process exits with 0 only when all four pass, or 1 for test/device failures. Configuration or logging errors exit with 2.

## Run

Install the .NET 10 SDK, connect the ESP-01 USB programmer, and run:

```sh
dotnet build
dotnet run -- /dev/cu.usbserial-110
```

Replace the port with your device's port (for example, `COM3` on Windows). Omitting it uses `/dev/cu.usbserial-110`. Close any serial monitor before running.

## Configure tests and save results (M2)

`dotnet run` reads `tests.json` from the current folder and creates a unique `logs/run-<UTC timestamp>-<unique ID>.csv` file. Edit the JSON to change the tests; no C# edits or firmware upload are needed for changes to expectations and limits.

Each test needs a unique `name`, a positive `maxMs`, either `command` or a nonempty `steps` array, and exactly one expectation:

- `expect`: exact reply, such as `OK PONG`.
- `expectPrefix`: reply starts with this text, such as `ERR`.
- `expectFormat`: `version` checks `OK major.minor.patch`; `uptime` checks `OK` followed by unsigned 32-bit milliseconds.

For example, add this object to the array to check an unknown command:

```json
{ "name": "bad-command", "command": "FOO", "expectPrefix": "ERR", "maxMs": 200 }
```

For sequences, use `"steps": ["PING", "WAIT 50", "ECHO hello"]` instead of `command`. `WAIT` pauses locally; other steps send a device command and read its reply. Intermediate replies must be `OK` or start with `OK `; the final reply is checked against the expectation. `maxMs` covers the entire sequence, including waits. The last step must be a device command. Relay commands require future firmware support.

Choose a different configuration or log folder with:

```sh
dotnet run -- /dev/cu.usbserial-110 tests.json logs
```

Each CSV row contains the UTC timestamp, port, test name, commands, expectation, limit, PASS/FAIL, whether the test executed, elapsed milliseconds, actual response, and failure reason. For a sequence, the response is the last received reply. Tests not run after a device failure have `executed=false` and no duration. Rows are flushed as tests finish. The log path is printed at startup, and generated logs are excluded from Git.

Invalid JSON, unknown fields, and invalid definitions are rejected before connecting. If the log cannot be created, the tests do not run. If logging fails mid-run, the app stops and reports an error; any existing log may be incomplete.

## Connection separation (M3)

The test runner uses `ITransport` to connect, send a command, read a reply, and close the connection. `SerialTransport` owns the USB serial settings, startup settling, line endings, timeouts, and connection cleanup. `TestRunner` owns sequencing, timing, expectations, console results, and CSV rows. `Program.cs` loads the configuration, opens the log, and supplies the connection to the runner.

The runner passes the remaining test time to each send/read operation, so all steps share one deadline. The caller disposes the connection even if running or logging fails. Future TCP and simulator transports can implement the same interface without changing the test sequence logic. M3 established USB serial support; M4 also adds `TcpTransport`. Existing serial commands and JSON definitions remain the same.

M3 validation: the .NET build, all serial smoke checks, and all four tests on the connected ESP-01 pass. The hardware CSV was checked for four passing rows and valid timings.

## Firmware

The Arduino sketch is `firmware/TestBench/TestBench.ino`. Arduino CLI 1.5.1 and ESP8266 core 3.1.2 are available on this Mac. The connected ESP-01 was detected as an ESP8266EX with a 26 MHz crystal and 1 MB flash. The upload script uses the Generic ESP8266 board, 1 MB flash without a filesystem, and compatible DOUT flash mode.

To rebuild and upload:

```sh
./scripts/flash-firmware.sh /dev/cu.usbserial-110
dotnet run -- /dev/cu.usbserial-110
```

The current programmer enters upload mode and resets automatically. Close any serial monitor before uploading. Serial communication is 115200 baud, newline terminated; CRLF is also accepted.

For a fresh Mac, install the tools first:

```sh
brew install arduino-cli
arduino-cli core update-index --additional-urls https://arduino.esp8266.com/stable/package_esp8266com_index.json
arduino-cli core install esp8266:esp8266@3.1.2 --additional-urls https://arduino.esp8266.com/stable/package_esp8266com_index.json
```

The board package URL comes from the [official ESP8266 installation guide](https://arduino-esp8266.readthedocs.io/en/latest/installing.html).

The sketch replies with `OK PONG`, firmware version `OK 0.1.0`, uptime as unsigned milliseconds, and the exact text following `ECHO `. Unknown commands return `ERR unknown command`. Commands exceeding the 127-byte buffer return `ERR command too long` and are discarded through the next newline. M1 does not drive relay pins.

The runner validates a numeric three-part firmware version and unsigned 32-bit uptime. Uptime is currently a format/liveness check; comparing it across a longer sequence to detect resets is future work. Startup settling takes 500 ms and is outside the per-test timing. A timeout stops subsequent commands to avoid matching a late reply to the wrong test; remaining tests are reported as not run and count against the pass total.

## Validation

After building, run the POSIX serial smoke checks (macOS/Linux, Python 3):

```sh
python3 checks/serial_smoke.py
```

These exercise the real serial runner through a pseudo-terminal: valid and malformed replies, timeouts, JSON sequences, intermediate errors, expected error replies, and CSV escaping. They also check invalid configuration, missing-device logs, and an unusable log directory. They do not replace firmware compilation or testing on the ESP-01.

Current validation: the .NET build and the serial smoke checks pass. Missing-device handling was checked. The firmware compiled with ESP8266 core 3.1.2, uploaded successfully, and passed flash hash verification. Hardware acceptance on the connected ESP-01 reports **4/4 passed** (`PING`, `VERSION`, `UPTIME`, `ECHO`), with all responses below 200 ms. M1 is complete. M2 also passed a hardware run using JSON definitions and produced a CSV with four passing results.

See `info.txt` for the full roadmap. M4 physical contact verification remains outstanding; M5 provides a hardware-free simulator and CI.

## Wi-Fi and relay testing (M4)

TCP support is implemented. Use `dotnet run -- tcp://<device-ip>:8266 tests-wifi.json` for Wi-Fi checks and `tests-relay.json` for the relay sequence after board/wiring confirmation. The CSV `port` column contains the TCP endpoint for these runs. TCP connection attempts have a five-second limit; command/reply operations share each test's configured deadline.

See [Wi-Fi setup and relay wiring](firmware/TestBenchWifi/README.md). The Wi-Fi sketch compiles, and local TCP smoke checks pass. Wi-Fi validation used firmware 0.2.0. Firmware 0.2.1 enables relay commands. The user observed OFF → ON → OFF during the HIGH → LOW → HIGH sequence, confirming active-low control. All six Wi-Fi tests pass on the actual ESP-01 at `tcp://10.0.0.176:8266`. Feedback wiring and automatic contact verification are still required, so M4 remains open.

## Measured results

Bench observations collected during development on October 21, 2026 (Pacific time):

| Measurement | Observed result |
| --- | --- |
| Real ESP-01 Wi-Fi validation | 3 complete runs, 6/6 checks passed in each |
| TCP command/reply duration across those runs | 18 replies; 11.12 ms mean, 6.7–22.1 ms range |
| USB serial baseline | 4/4 checks passed on real hardware |
| Unloaded relay switching | 3/3 timed phases passed; operator observed OFF → ON → OFF |
| Relay phase hold time | 2 seconds per phase |
| Automated local regression checks | 12 serial check groups and 10 TCP check groups |

TCP timing samples include PING, VERSION, UPTIME, ECHO, IP, and RSSI from three six-test TCP CSV logs. Durations are measured by the host runner and include command transmission and reply processing; they exclude connection setup. These are a small set of development measurements, not a sustained-load benchmark or reliability guarantee. The two-second relay holds are deliberately excluded from command-latency statistics.

Regression checks cover protocol errors, invalid configuration, timeouts, connection failures, fragmented TCP replies, CSV output, and simulated stuck-open feedback. Local fixtures exercise software behavior; they do not substitute for contact measurements. Physical relay switching has been observed, but automated contact-feedback verification is pending. M5 adds the C# simulator, xUnit tests, and GitHub Actions workflow described below.

## Simulator and continuous integration (M5)

No board, wiring, Wi-Fi credentials, or Arduino tools are required for these commands:

```sh
# Run the regular four-command suite against the C# simulator
dotnet run -- sim

# Run all eight relay-suite checks using simulated contact feedback
dotnet run -- sim tests-relay.json

# Demonstrate a stuck-open contact: relay-closes fails; final OFF still runs
dotnet run -- sim:stuck-open tests-relay.json

# Run the xUnit tests
dotnet test tests/TestBench.Tests/TestBench.Tests.csproj
```

Available simulator endpoints: `sim`, `sim:stuck-open`, `sim:stuck-closed`, `sim:garbage`, `sim:timeout`, `sim:disconnect`, and `sim:missing`. Fault scenarios intentionally produce FAIL results and exit code 1. An unknown simulator scenario exits with 2. The endpoint appears in the CSV `port` column, so simulated results are distinguishable from physical-device runs.

`DeviceSimulator` models the firmware's commands, uptime, relay state, contact faults, five-second relay watchdog, and relay shutdown on connection close. `SimTransport` implements `ITransport`, adds reply delay/failure injection, and requires a reply to be consumed before the next command. The runner uses unchanged JSON definitions. IP and RSSI values are synthetic; simulation timings measure local software only and must not be presented as hardware/network latency. The model does not emulate boot pins, radio behavior, electrical faults, or mechanical contact settling.

The suite contains **36 xUnit cases**, plus the existing **22 serial/TCP regression check groups**. It covers protocol behavior, shared timing budgets, session shutdown after faults, contact-state mismatches despite successful acknowledgments, CSV escaping, configuration validation, uptime rollover, and watchdog behavior.

[GitHub Actions](https://github.com/zxcv616/testbench/actions/workflows/tests.yml) builds the application and runs xUnit tests, default/relay simulator suites, serial pseudo-terminal checks, and TCP loopback checks on every push and pull request. It can also be started manually. CI runs on Linux without physical hardware or Wi-Fi credentials. Local serial/TCP Python checks require Python 3 and macOS/Linux.

M5 is implemented. M4 remains open specifically for wiring and automatic verification of the real relay contacts; simulated contact tests do not complete that hardware milestone.
