#!/usr/bin/env bash
# Plays a linked game N times through the generic AutoPlay: the Opus brain (turn-based, real model calls), or a
# script plan when one is given (no model calls).
# Usage: bash Tools/run-adapter-batch.sh <game project> <N> [script.json]
# Stops at the first run where AutoPlay exits 2 (a hook failed: check the user's save) or the model fails.
# Env: AGENTPLAY_AUTOPLAY_SCENE, AGENTPLAY_AUTOPLAY_SECONDS (default 900), AGENTPLAY_PROGRESS_OBSERVE (an observe path
# printed as first -> last value from each run's trace). Each call logs to its own folder:
# .e2e/adapter-batch/<project folder name>/<yyyyMMdd-HHmmss>/ (printed at the start), so earlier tiers' logs stay.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
GAME="$(cd "$1" && pwd)"
RUNS="${2:-3}"
SCRIPT="${3:-}"
LOGS="$ROOT/.e2e/adapter-batch/$(basename "$GAME")/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$LOGS"
echo "logs: $LOGS"
dotnet build "$ROOT/Runner/AgentPlay.Runner.csproj" -c Debug -v q || exit 1
if [ -n "$SCRIPT" ]; then BRAIN=(--brain script "$(cygpath -w "$SCRIPT")"); else BRAIN=(--brain opus); fi
wins=0
for i in $(seq 1 "$RUNS"); do
  dotnet "$ROOT/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll" "${BRAIN[@]}" --idle-exit 60 --no-reflect \
    > "$LOGS/run-$i.log" 2>&1 < /dev/null &
  RUNNER_PID=$!
  AGENTPLAY_SESSION_TAG=debug AGENTPLAY_AUTOPLAY_SECONDS="${AGENTPLAY_AUTOPLAY_SECONDS:-900}" "$UNITY_EXE" -projectPath "$(cygpath -m "$GAME")" \
    -executeMethod AgentPlay.Editor.AutoPlay.Run -logFile "$(cygpath -m "$LOGS/autoplay-$i.log")"
  UNITY_CODE=$?
  # A runner that never saw the game (Unity failed before Play mode) would wait for it forever.
  for s in $(seq 1 30); do kill -0 "$RUNNER_PID" 2>/dev/null || break; sleep 1; done
  if kill -0 "$RUNNER_PID" 2>/dev/null; then
    kill "$RUNNER_PID" 2>/dev/null
    echo "run $i: the runner was still waiting 30 s after Unity exited; stopped it"
  fi
  wait "$RUNNER_PID"
  RUNNER_CODE=$?
  [ "$RUNNER_CODE" -eq 0 ] && wins=$((wins + 1))
  CALLS="$(grep -h -o 'model calls [0-9]*' "$LOGS/run-$i.log" | tail -1)"
  PROGRESS=""
  if [ -n "${AGENTPLAY_PROGRESS_OBSERVE:-}" ]; then
    TRACE="$(ls -t "$GAME/AgentPlay/Traces/"*.ndjson 2>/dev/null | head -1)"
    if [ -n "$TRACE" ]; then
      PROGRESS="$(python -I - "$TRACE" "$AGENTPLAY_PROGRESS_OBSERVE" <<'PY'
import json, sys
first = last = None
for line in open(sys.argv[1], encoding="utf-8"):
    try:
        r = json.loads(line)
    except ValueError:
        continue
    if isinstance(r, dict) and isinstance(r.get("state"), dict) and sys.argv[2] in r["state"]:
        last = r["state"][sys.argv[2]]
        if first is None:
            first = last
print("%s=%s->%s" % (sys.argv[2], first, last))
PY
)"
    fi
  fi
  echo "run $i: runner=$RUNNER_CODE unity=$UNITY_CODE ${CALLS:-model calls 0} $PROGRESS"
  if [ "$UNITY_CODE" = 2 ]; then
    echo "run $i: AutoPlay could not start or a hook failed (exit 2), so the user's state may need attention: see $LOGS/autoplay-$i.log; batch stopped"
    exit 2
  fi
  if [ "$RUNNER_CODE" != 0 ] && [ "$RUNNER_CODE" != 3 ] && grep -q "model call failed" "$LOGS/run-$i.log"; then
    echo "run $i: model failure, batch stopped"
    exit 4
  fi
done
echo "wins: $wins/$RUNS"
