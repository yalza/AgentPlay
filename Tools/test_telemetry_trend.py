"""Tests for telemetry_trend.py. Run: python -I Tools/test_telemetry_trend.py"""
import json
import pathlib
import subprocess
import sys
import tempfile

TOOL = pathlib.Path(__file__).with_name("telemetry_trend.py")


def run(values, results=None):
    with tempfile.TemporaryDirectory() as d:
        for i, v in enumerate(values):
            result = results[i] if results else "won"
            report = {"mode": "realtime", "result": result, "model_calls": 3, "calls_per_game_minute": v, "important_events_handled_by_reflex": i}
            (pathlib.Path(d) / ("20261013-10%02d00.json" % i)).write_text(json.dumps(report), encoding="utf-8")
        p = subprocess.run([sys.executable, "-I", str(TOOL), d], capture_output=True, text=True)
        return p.returncode, p.stdout


code, out = run([10, 9, 9.5, 7, 6])
assert code == 0, (code, out)
assert "slope" in out, out
code, out = run([6, 7, 9.5, 9, 10])
assert code == 1, (code, out)
code, out = run([5, 5])
assert code == 1, (code, out)   # not lower
code, out = run([5])
assert code == 2, (code, out)
# A session that never finished a game (model failure, quota) has no rate to compare: it is listed but not counted.
code, out = run([4, 5, 5, 5.5, 0], ["lost", "lost", "won", "won", "stopped"])
assert code == 1, (code, out)
assert "skipped" in out, out
code, out = run([5, 0, 4], ["won", "unfinished", "lost"])
assert code == 0, (code, out)
code, out = run([5, 0], ["won", "stopped"])
assert code == 2, (code, out)
print("telemetry_trend tests passed")
