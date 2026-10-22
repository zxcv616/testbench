"""Verify soak CLI exit status, JSON summaries, and CSV rows without hardware."""
import csv
import json
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
for endpoint, code, passed, failed, not_run in [
    ('sim', 0, 10, 0, 0),
    ('sim:garbage', 1, 0, 10, 0),
    ('sim:timeout', 1, 0, 1, 9),
    ('sim:missing', 1, 0, 0, 10),
]:
    with tempfile.TemporaryDirectory() as temporary:
        result = subprocess.run(
            ['dotnet', 'run', '--no-build', '--', '--soak', endpoint, '10', '50', temporary],
            cwd=ROOT, capture_output=True, text=True, timeout=15)
        assert result.returncode == code, result.stdout + result.stderr
        report = json.loads(next(Path(temporary).glob('*.json')).read_text())
        stats = report['Statistics']
        assert (stats['Passed'], stats['Failed'], stats['NotRun']) == (passed, failed, not_run), report
        assert report['Simulated'] and report['Completed'] == (not_run == 0)
        with next(Path(temporary).glob('*.csv')).open(newline='') as stream:
            rows = list(csv.DictReader(stream))
        assert len(rows) == 10
        print(f'PASS soak command: {endpoint}')
