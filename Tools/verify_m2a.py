"""Checks logs and traces from Tools/e2e-m2a.sh. Usage: verify_m2a.py ROOT RUNNER_EXIT UNITY_EXIT"""
import json
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
runner_code = int(sys.argv[2])
unity_code = int(sys.argv[3])
project = root / "Demos" / "Heist"
logs = project / "Logs"
runner_log = (logs / "runner.log").read_text(encoding="utf-8", errors="replace")
unity_log = (logs / "autoplay.log").read_text(encoding="utf-8", errors="replace")
failures = []


def check(ok, message):
    print(("PASS " if ok else "FAIL ") + message)
    if not ok:
        failures.append(message)


check(runner_code == 0, "runner exited with 0 (won), got %d" % runner_code)
check(unity_code == 0, "Unity exited with 0 (won), got %d" % unity_code)
check("hello: game=AgentPlay Heist version=m3" in runner_log, "runner received hello from the Heist demo")
check("  observe guards : list [" in runner_log, "hello lists the guards list observe with its fields")
check("  event game.won (important)" in runner_log, "hello lists the declared game.won event")
check("WARNING: the game sent no brief" not in runner_log, "the game sent its brief")
for plan_id in ("s1", "s2", "s3", "s4"):
    check(("sent plan %s:" % plan_id) in runner_log, "runner sent plan %s" % plan_id)
check(runner_log.count("plan_done") >= 4, "four plans reported plan_done")
check("plan_failed" not in runner_log, "no plan failed")
check("unity error:" not in runner_log, "Unity reported no protocol errors")
check("game over: won" in runner_log, "runner saw the win")
check("[AgentPlay] Registry: 7 observes, 6 actions" in unity_log, "registry found 7 observes and 6 actions")
check("[Heist] result=won" in unity_log, "the game ended in a win")
check("NullReferenceException" not in unity_log, "no NullReferenceException in the Unity log")
check("AgentActionException" not in unity_log, "no AgentActionException in the Unity log")

traces = sorted((project / "AgentPlay" / "Traces").glob("*.ndjson"))
records = []
for path in traces:
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            records.append(json.loads(line))
check(any(r.get("type") == "control" and r.get("who") == "agent" for r in records), "trace marks agent control")
check(sum(1 for r in records if r.get("type") == "plan") >= 4, "trace has the four plans")
check(sum(1 for r in records if r.get("type") == "plan_status" and r.get("status") == "plan_done") >= 4, "trace has four plan_done statuses")
check(any(r.get("type") == "event" and r.get("name") == "game.won" for r in records), "trace has the game.won event")

print("%d check(s) failed" % len(failures) if failures else "ALL CHECKS PASSED")
sys.exit(1 if failures else 0)
