"""Exercise the actual TCP runner against a local protocol fixture; dotnet build first."""
import csv
import json
from pathlib import Path
import socket
import subprocess
import tempfile
import threading
import time

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / 'bin/Debug/net10.0/TestBench.dll'


def check(name, mode, expected, count=8):
    with tempfile.TemporaryDirectory() as directory, socket.socket() as server:
        folder = Path(directory)
        server.bind(('127.0.0.1', 0))
        server.listen(1)
        server.settimeout(5)
        endpoint = f'tcp://127.0.0.1:{server.getsockname()[1]}'
        observed = []
        errors = []
        stop = threading.Event()

        def respond():
            try:
                with server.accept()[0] as client:
                    client.settimeout(5)
                    pending = b''
                    closed = False
                    while not stop.is_set():
                        chunk = client.recv(1024)
                        if not chunk:
                            return
                        pending += chunk
                        while b'\n' in pending:
                            command, pending = pending.split(b'\n', 1)
                            observed.append(command)
                            if mode == 'disconnect':
                                client.sendall(b'OK PO')
                                return
                            if mode == 'delay':
                                stop.wait(0.8)
                            if mode == 'drip':
                                for byte in b'OK PONG\r\n':
                                    client.sendall(bytes([byte]))
                                    if stop.wait(0.15): return
                                continue
                            if command == b'RELAY ON': closed = True
                            if command == b'RELAY OFF': closed = False
                            replies = {b'PING': b'OK PONG', b'VERSION': b'OK 0.2.0', b'UPTIME': b'OK 1000',
                                       b'ECHO hello': b'OK hello', b'RELAY ON': b'OK', b'RELAY OFF': b'OK',
                                       b'CONTACT': b'OK CLOSED' if closed and mode != 'stuck' else b'OK OPEN'}
                            reply = replies.get(command, b'ERR unknown command')
                            if mode == 'garbage' and command == b'PING': reply = b'garbage'
                            if mode == 'oversized': reply = b'x' * 1025
                            if mode == 'fragmented':
                                for byte in reply + b'\r\n':
                                    client.sendall(bytes([byte]))
                                    time.sleep(0.001)
                            else:
                                client.sendall(reply + b'\r\n')
            except (BrokenPipeError, ConnectionResetError):
                pass  # Expected when the runner stops after timeout or malformed framing.
            except Exception as ex:
                errors.append(ex)

        worker = threading.Thread(target=respond)
        worker.start()
        try:
            result = subprocess.run(['dotnet', str(APP), endpoint, str(ROOT / 'tests-relay.json'), str(folder)],
                                    capture_output=True, text=True, timeout=12)
            assert result.returncode == (0 if expected == count else 1), result.stdout + result.stderr
            assert f'{expected}/{count} passed' in result.stdout, result.stdout
            with next(folder.glob('*.csv')).open(newline='') as stream:
                rows = list(csv.DictReader(stream))
            assert len(rows) == count and all(row['port'] == endpoint for row in rows)
            if mode in ('delay', 'drip', 'disconnect', 'oversized'):
                assert len(observed) == 1, observed
                assert all(row['executed'] == 'false' for row in rows[1:])
            if mode == 'stuck':
                assert rows[6]['status'] == 'FAIL' and 'OK OPEN' in rows[6]['reason']
                assert observed[-2:] == [b'RELAY OFF', b'CONTACT'], observed
            print(f'PASS {name}')
        finally:
            stop.set()
            worker.join(timeout=6)
        assert not errors, errors
        assert not worker.is_alive()


check('TCP relay sequence and CSV', 'normal', 8)
check('fragmented TCP replies', 'fragmented', 8)
check('stuck-open feedback fails and relay-off still runs', 'stuck', 7)
check('garbage reply', 'garbage', 7)
check('timeout stops subsequent commands', 'delay', 0)
check('slow fragmented line uses total deadline', 'drip', 0)
check('disconnect mid-reply', 'disconnect', 0)
check('oversized reply', 'oversized', 0)
with tempfile.TemporaryDirectory() as directory:
    with socket.socket() as unused:
        unused.bind(('127.0.0.1', 0))
        port = unused.getsockname()[1]
    result = subprocess.run(['dotnet', str(APP), f'tcp://127.0.0.1:{port}', str(ROOT / 'tests-relay.json'), directory],
                            capture_output=True, text=True, timeout=10)
    assert result.returncode == 1 and '0/8 passed' in result.stdout, result.stdout
    print('PASS refused connection')
    for endpoint in ['tcp://localhost', 'tcp://localhost:0', 'tcp://localhost:8266/path']:
        result = subprocess.run(['dotnet', str(APP), endpoint, str(ROOT / 'tests-relay.json'), directory],
                                capture_output=True, text=True, timeout=10)
        assert result.returncode == 2 and 'TCP endpoint must be' in result.stdout, result.stdout
    print('PASS invalid TCP endpoints')
