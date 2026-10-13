"""Checks Tools/e2e-m4a-realtime.sh. Usage: verify_m4a_realtime.py PROJECT LOGS RUNNER_EXIT UNITY_EXIT"""
import json
import pathlib
import re
import sys

project = pathlib.Path(sys.argv[1])
logs = pathlib.Path(sys.argv[2])
runner_code = int(sys.argv[3])
unity_code = int(sys.argv[4])
runner_log = (logs / "runner.log").read_text(encoding="utf-8", errors="replace")
unity_log = (logs / "autoplay.log").read_text(encoding="utf-8", errors="replace")
failures = []


def check(ok, message):
    print(("PASS " if ok else "FAIL ") + message)
    if not ok:
        failures.append(message)


check(runner_code == 0, "runner exited with 0 (won), got %d" % runner_code)
check(unity_code == 0, "Unity exited with 0 (won), got %d" % unity_code)
check("paused t=" not in runner_log, "the game never paused")
attention = re.findall(r"attention t=\S+ reason=(\w+)", runner_log)
check(len(attention) >= 4, "at least four attention signals (%d: %s)" % (len(attention), ",".join(attention)))
check("attention t=" in runner_log and attention[:1] == ["mode"], "the first attention is the mode start")
check(len(re.findall(r"sent plan s\d", runner_log)) == 4, "the four scripted plans were sent")
check("game over: won" in runner_log, "the run was won")
check("unity error:" not in runner_log, "Unity reported no protocol errors")
check("Unhandled exception" not in runner_log, "the runner did not crash")
check("NullReferenceException" not in unity_log, "no NullReferenceException in the Unity log")
telemetry = sorted((project / "AgentPlay" / "Telemetry").glob("*.json"))
check(len(telemetry) == 1, "one telemetry file")
if telemetry:
    r = json.loads(telemetry[-1].read_text(encoding="utf-8"))
    check(r.get("mode") == "realtime", "telemetry mode is realtime")
    check(r.get("time_split", {}).get("plan", 0) > 0, "telemetry has a time split with plan time")
    check(r.get("model_calls") == 0, "no model calls")
    print("telemetry: " + json.dumps({k: r.get(k) for k in ("result", "game_seconds", "time_split", "decision_wait_ms", "pauses_by_reason")}, ensure_ascii=True))
print("%d check(s) failed" % len(failures) if failures else "ALL CHECKS PASSED")
sys.exit(1 if failures else 0)
