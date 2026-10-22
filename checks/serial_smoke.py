"""POSIX serial smoke checks; run after dotnet build. No hardware required."""
import os
from pathlib import Path
import pty
import select
import subprocess
import threading
import csv
import json
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def check(name, replies, expected_summary, expected_exit, delay=0, definitions=None, verify=None):
    temporary = tempfile.TemporaryDirectory()
    folder = Path(temporary.name)
    definitions = definitions if definitions is not None else json.loads((ROOT / "tests.json").read_text())
    config = folder / "tests.json"
    config.write_text(json.dumps(definitions))
    master, slave = pty.openpty()
    stop = threading.Event()

    def respond():
        pending = b""
        while not stop.is_set():
            ready, _, _ = select.select([master], [], [], 0.05)
            if not ready:
                continue
            pending += os.read(master, 1024)
            while b"\n" in pending:
                line, pending = pending.split(b"\n", 1)
                response = replies.get(line.rstrip(b"\r"), b"ERR unknown command")
                if stop.wait(delay):
                    return
                os.write(master, response + b"\r\n")

    worker = threading.Thread(target=respond)
    worker.start()
    try:
        result = subprocess.run(
            ["dotnet", "run", "--no-build", "--", os.ttyname(slave), str(config), str(folder / "logs")],
            cwd=ROOT, capture_output=True, text=True, timeout=10)
        assert result.returncode == expected_exit, result.stdout + result.stderr
        assert expected_summary in result.stdout, result.stdout
        if delay:
            assert "session stopped after timeout" in result.stdout, result.stdout
        logs = list((folder / "logs").glob("*.csv"))
        assert len(logs) == 1
        with logs[0].open(newline="") as stream:
            rows = list(csv.DictReader(stream))
        assert len(rows) == len(definitions)
        assert sum(row["status"] == "PASS" for row in rows) == int(expected_summary.split("/")[0])
        if delay:
            assert rows[0]["executed"] == "true"
            assert all(row["executed"] == "false" and row["durationMs"] == "" for row in rows[1:])
        if verify:
            verify(rows)
        print(f"PASS {name}")
    finally:
        stop.set()
        worker.join(timeout=2)
        os.close(master)
        os.close(slave)
        temporary.cleanup()


valid = {b"PING": b"OK PONG", b"VERSION": b"OK 0.1.0",
         b"UPTIME": b"OK 1234", b"ECHO hello": b"OK hello"}
check("all commands", valid, "4/4 passed", 0)
check("garbage reply", {**valid, b"PING": b"garbage"}, "3/4 passed", 1)
check("invalid version", {**valid, b"VERSION": b"OK nonsense"}, "3/4 passed", 1)
check("invalid uptime", {**valid, b"UPTIME": b"OK -1"}, "3/4 passed", 1)
check("late reply stops session", valid, "0/4 passed", 1, delay=0.3)

check("JSON sequence and prefix", valid, "1/1 passed", 0, definitions=[
    {"name": "sequence", "steps": ["PING", "WAIT 10", "VERSION"], "expectPrefix": "OK 0.", "maxMs": 200}])
check("intermediate error fails sequence", valid, "0/1 passed", 1, definitions=[
    {"name": "sequence", "steps": ["FOO", "PING"], "expect": "OK PONG", "maxMs": 200}])
check("expected error reply passes", valid, "1/1 passed", 0, definitions=[
    {"name": "unknown", "command": "FOO", "expectPrefix": "ERR", "maxMs": 200}])

def verify_csv(rows):
    assert rows[0]["test"] == 'echo, "quoted"'
    assert rows[0]["response"] == 'OK hello, "world"'
    assert json.loads(rows[0]["commands"]) == ['ECHO hello, "world"']

check("CSV escaping", {b'ECHO hello, "world"': b'OK hello, "world"'}, "1/1 passed", 0,
      definitions=[{"name": 'echo, "quoted"', "command": 'ECHO hello, "world"',
                    "expect": 'OK hello, "world"', "maxMs": 200}], verify=verify_csv)

with tempfile.TemporaryDirectory() as directory:
    folder = Path(directory)
    config = folder / "invalid.json"
    invalid_cases = ["{", "[]", "[null]",
        '[{"name":"oops","command":"PING","expect":"OK PONG","maxMs":0}]',
        '[{"name":"oops","command":"PING","expect":"OK","maxMs":200,"typo":1}]',
        '[{"name":"oops","steps":["WAIT -1","PING"],"expect":"OK","maxMs":200}]']
    for content in invalid_cases:
        config.write_text(content)
        result = subprocess.run(["dotnet", "run", "--no-build", "--", "/dev/nonexistent-testbench",
                                 str(config), str(folder / "logs")], cwd=ROOT, capture_output=True, text=True)
        assert result.returncode == 2 and "ERROR tests file" in result.stdout, result.stdout
        assert not (folder / "logs").exists()
    print("PASS invalid configurations rejected before connection")
    result = subprocess.run(["dotnet", "run", "--no-build", "--", "/dev/nonexistent-testbench",
                             str(ROOT / "tests.json"), str(folder / "logs")], cwd=ROOT, capture_output=True, text=True)
    assert result.returncode == 1 and "0/4 passed" in result.stdout, result.stdout
    with next((folder / "logs").glob("*.csv")).open(newline="") as stream:
        rows = list(csv.DictReader(stream))
    assert len(rows) == 4 and all(row["executed"] == "false" and row["reason"] for row in rows)
    print("PASS missing device logged")
    blocked = folder / "not-a-directory"
    blocked.write_text("occupied")
    result = subprocess.run(["dotnet", "run", "--no-build", "--", "/dev/nonexistent-testbench",
                             str(ROOT / "tests.json"), str(blocked)], cwd=ROOT, capture_output=True, text=True)
    assert result.returncode == 2 and "ERROR run could not complete" in result.stdout
    print("PASS unusable log directory reported")
