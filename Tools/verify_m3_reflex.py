"""Checks Tools/e2e-m3-reflex.sh. Usage: verify_m3_reflex.py PROJECT LOGS RUNNER_EXIT UNITY_EXIT"""
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
check("hello: game=AgentPlay Heist version=m3" in runner_log, "runner received hello from Heist m3")
check(re.search(r"\[AgentPlay\] \d+ reflex\(es\) active", unity_log) is not None, "Unity loaded the reflex")

fired = re.findall(r"reflex (R-\S+) t=(\S+) handled ([^\n]*)", runner_log)
handled = [(rid, t) for rid, t, events in fired if "guard.spotted" in events]
check(len(handled) >= 1, "a reflex handled guard.spotted (%d time(s))" % len(handled))
check(re.search(r"reflex_end R-\S+ t=\S+ done", runner_log) is not None, "a reflex finished with done")
event_pauses = set(re.findall(r"paused t=(\S+) reason=event", runner_log))
check(all(t not in event_pauses for _, t in handled), "the game did not pause for a handled guard.spotted")
check("game over: won" in runner_log, "the run was won")
check("unity error:" not in runner_log, "Unity reported no protocol errors")
check("NullReferenceException" not in unity_log, "no NullReferenceException in the Unity log")

telemetry = sorted((project / "AgentPlay" / "Telemetry").glob("*.json"))
check(len(telemetry) == 1, "one telemetry file")
if telemetry:
    report = json.loads(telemetry[-1].read_text(encoding="utf-8"))
    check(report.get("important_events_handled_by_reflex", 0) >= 1, "telemetry counts the handled event (%s)" % report.get("important_events_handled_by_reflex"))
    check(report.get("model_calls") == 0, "telemetry shows no model calls")
    print("telemetry: " + json.dumps(report, ensure_ascii=True))

after = json.loads((logs / "reflexes.after.json").read_text(encoding="utf-8"))
fired_ids = {rid for rid, _, _ in fired}
stats = [r for r in after["reflexes"] if r["id"] in fired_ids]
check(any(r["stats"]["fired"] >= 1 for r in stats), "reflexes.json kept the firing count")
print("reflex stats: " + json.dumps([(r["id"], r["status"], r["stats"]) for r in after["reflexes"]], ensure_ascii=True))

records = []
for path in sorted((project / "AgentPlay" / "Traces").glob("*.ndjson")):
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            records.append(json.loads(line))
check(any(r.get("type") == "header" and "hello" in r for r in records), "trace header carries hello")
check(any(r.get("type") == "reflex" for r in records), "trace has a reflex line")

print("%d check(s) failed" % len(failures) if failures else "ALL CHECKS PASSED")
sys.exit(1 if failures else 0)
