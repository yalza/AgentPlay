"""Checks logs and traces from Tools/e2e-m2c.sh. Usage: verify_m2c.py GAME_DIR LOGS_DIR RUNNER_EXIT UNITY_EXIT"""
import json
import pathlib
import sys

game = pathlib.Path(sys.argv[1])
logs = pathlib.Path(sys.argv[2])
runner_code = int(sys.argv[3])
unity_code = int(sys.argv[4])
runner_log = (logs / "agentplay-runner.log").read_text(encoding="utf-8", errors="replace")
unity_log = (logs / "agentplay-autoplay.log").read_text(encoding="utf-8", errors="replace")
failures = []


def check(ok, message):
    print(("PASS " if ok else "FAIL ") + message)
    if not ok:
        failures.append(message)


check(runner_code == 0, "runner exited with 0 (won), got %d" % runner_code)
check(unity_code == 0, "Unity exited with 0 (won), got %d" % unity_code)
check("hello: game=Tile Thieves" in runner_log, "runner received hello from TileThieves")
check("WARNING: the game sent no brief" not in runner_log, "TileThieves sent its brief")
check("game over: won" in runner_log, "runner saw the win")
check("unity error:" not in runner_log, "Unity reported no protocol errors")
check("[TileThieves agent] result=won" in unity_log, "the adapter declared the win")
check("[AgentPlay] virtual gamepad created" in unity_log, "the virtual gamepad was created")
check("[AgentPlay] virtual gamepad removed" in unity_log, "the virtual gamepad was removed when Play mode ended")
check("error CS" not in unity_log, "no compile errors")
check("NullReferenceException" not in unity_log, "no NullReferenceException in the Unity log")
check("[AgentPlay] Registry:" in unity_log and "[AgentPlay] More than one" not in unity_log, "registry built without duplicate games")

records = []
for path in sorted((game / "AgentPlay" / "Traces").glob("*.ndjson")):
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            records.append(json.loads(line))
check(sum(1 for r in records if r.get("type") == "event" and r.get("name") == "tile.pried") >= 6, "trace has at least six tile.pried events")
won = [r for r in records if r.get("type") == "event" and r.get("name") == "game.won"]
check(len(won) == 1 and (won[0].get("state") or {}).get("sack.count", 0) >= 6, "game.won with sack.count >= 6")
check(not any(r.get("type") == "event" and r.get("name") == "game.lost" for r in records), "no game.lost")

print("%d check(s) failed" % len(failures) if failures else "ALL CHECKS PASSED")
sys.exit(1 if failures else 0)
