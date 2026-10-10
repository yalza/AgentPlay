"""Checks the logs and traces produced by Tools/e2e-m1.sh. Usage: verify_m1.py ROOT RESUME_SECONDS THINK_MS"""
import json
import pathlib
import re
import sys

root = pathlib.Path(sys.argv[1])
resume_seconds = float(sys.argv[2])
think_seconds = int(sys.argv[3]) / 1000.0
logs = root / "Testbed" / "Logs"
runner_log = (logs / "runner.log").read_text(encoding="utf-8", errors="replace")
unity_log = (logs / "autoplay.log").read_text(encoding="utf-8", errors="replace")
failures = []


def check(ok, message):
    print(("PASS " if ok else "FAIL ") + message)
    if not ok:
        failures.append(message)


# Handshake and events
check("hello: game=AgentPlay Testbed version=m1" in runner_log, "runner received hello from the Testbed")
check("  observe walker.x : float" in runner_log, "hello lists observe walker.x")
check("  observe walker.pos : vector3" in runner_log, "hello lists observe walker.pos")
check("  action wait() [safe]" in runner_log, "hello lists the safe action")
check(re.search(r"event t=\S+ frame=\d+ walker\.turned", runner_log) is not None, "runner received walker.turned events")
check('"at":[' in runner_log, "event payload serialized Vector3 as an array")
check("unity error:" not in runner_log, "Unity reported no protocol errors")

# Bridge lifecycle (spec risks 1 and 2)
ports = re.findall(r"\[AgentPlay\] Bridge listening on 127\.0\.0\.1:(\d+)", unity_log)
check(len(ports) >= 1 and all(p == "47800" for p in ports), "bridge always listened on 47800 (saw %s)" % ports)
check("SocketException" not in unity_log, "no SocketException in the Unity log")
check("[AgentPlay] Registry: 3 observes, 1 actions" in unity_log, "registry found the demo's 3 observes and 1 action")
reload_requested = "[Testbed] Requesting a domain reload during Play mode" in unity_log
print("INFO domain reload requested during Play: %s; bridge starts seen: %d" % (reload_requested, len(ports)))

# Turn-based: game time between pauses stays within the budget while wall time covers the think time
# No "$" anchor: the runner log may end lines with \r\n on Windows.
pattern = re.compile(r"^\[(\d\d):(\d\d):(\d\d)\.(\d{3})\] (connected to \S+|paused t=([\d.]+) reason=(\w+))", re.M)
gaps = []
previous = None
for m in pattern.finditer(runner_log):
    wall = int(m[1]) * 3600 + int(m[2]) * 60 + int(m[3]) + int(m[4]) / 1000.0
    if m[5].startswith("connected"):
        previous = None
        continue
    t = float(m[6])
    if previous is not None:
        gaps.append((t - previous[0], wall - previous[1]))
    previous = (t, wall)
game_gaps = [round(g, 2) for g, _ in gaps]
wall_gaps = [round(w, 2) for _, w in gaps]
check(len(gaps) >= 3, "at least 3 pause-to-pause intervals (saw %d)" % len(gaps))
check(all(g <= resume_seconds + 0.5 for g, _ in gaps), "game time between pauses <= %.1f s: %s" % (resume_seconds + 0.5, game_gaps))
check(all(w >= think_seconds - 0.1 for _, w in gaps), "wall time between pauses >= think time %.1f s: %s" % (think_seconds, wall_gaps))

# Traces
traces = sorted((root / "Testbed" / "AgentPlay" / "Traces").glob("*.ndjson"))
check(len(traces) >= 1, "trace file written (%d files)" % len(traces))
records = []
for path in traces:
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            records.append(json.loads(line))
check(any(r.get("type") == "header" and r.get("game") == "AgentPlay Testbed" and r.get("who") == "human" and r.get("tag") in ("debug", "play") for r in records),
      "trace header names the game, who and tag")
check(any(r.get("type") == "event" and isinstance((r.get("payload") or {}).get("at"), list) for r in records), "trace has an event with a Vector3 payload")
check(any(r.get("type") == "snapshot" and "walker.x" in (r.get("state") or {}) for r in records), "trace has a snapshot with walker.x")

print("%d check(s) failed" % len(failures) if failures else "ALL CHECKS PASSED")
sys.exit(1 if failures else 0)
