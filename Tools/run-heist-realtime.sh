#!/usr/bin/env bash
# Plays Heist N times in realtime mode with the Opus brain, the model router and plan prefetch (real model calls),
# using the playbook and reflexes already in Demos/Heist/AgentPlay. Usage: bash Tools/run-heist-realtime.sh [N]
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
PROJECT="$ROOT/Demos/Heist"
LOGS="$ROOT/.e2e/heist-realtime-opus"
RUNS="${1:-5}"
mkdir -p "$LOGS"
rm -f "$LOGS"/*
dotnet build "$ROOT/Runner/AgentPlay.Runner.csproj" -c Debug -v q || exit 1
wins=0
for i in $(seq 1 "$RUNS"); do
  rm -rf "$PROJECT/AgentPlay/Telemetry"
  dotnet "$ROOT/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll" --brain opus --mode realtime --idle-exit 20 --no-reflect \
    > "$LOGS/run-$i.log" 2>&1 < /dev/null &
  RUNNER_PID=$!
  AGENTPLAY_SESSION_TAG=debug AGENTPLAY_AUTOPLAY_SECONDS=600 "$UNITY_EXE" -projectPath "$(cygpath -m "$PROJECT")" \
    -executeMethod HeistDemo.HeistAutoPlay.Run -logFile "$(cygpath -m "$LOGS/autoplay-$i.log")"
  UNITY_CODE=$?
  wait "$RUNNER_PID"
  RUNNER_CODE=$?
  cp "$PROJECT"/AgentPlay/Telemetry/*.json "$LOGS/telemetry-$i.json" 2>/dev/null
  [ "$RUNNER_CODE" -eq 0 ] && wins=$((wins + 1))
  echo "run $i: runner=$RUNNER_CODE unity=$UNITY_CODE"
done
echo "wins: $wins/$RUNS"
python -I - "$LOGS" "$RUNS" <<'PY'
import json, pathlib, sys
logs, runs = pathlib.Path(sys.argv[1]), int(sys.argv[2])
for i in range(1, runs + 1):
    f = logs / ("telemetry-%d.json" % i)
    if not f.exists():
        print("run %d: no telemetry" % i); continue
    r = json.loads(f.read_text(encoding="utf-8"))
    roles = {m: c["role"] for m, c in r.get("calls_by_model", {}).items()}
    print("run %d: %s, %.1f s, calls %d %s, prefetch %s, decision wait median %.0f ms, reflex handled %d, time split %s" % (
        i, r["result"], r["game_seconds"], r["model_calls"], roles, r.get("prefetch"), r["decision_wait_ms"]["median"],
        r["important_events_handled_by_reflex"], {k: round(v, 2) for k, v in r.get("time_split", {}).items() if k.endswith("ratio")}))
PY
