#!/usr/bin/env bash
# Criterion 5 (spec M4a): N realtime Opus sessions tagged play on Heist, starting from an empty playbook.
# The runner learns after each session (reflect); proposed reflexes are approved before the next one.
# Prints the calls-per-minute trend. Real model calls. Usage: bash Tools/run-heist-learning.sh [N]
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
PROJECT="$ROOT/Demos/Heist"
AP="$PROJECT/AgentPlay"
LOGS="$ROOT/.e2e/heist-learning"
RUNS="${1:-5}"
mkdir -p "$LOGS/before"
rm -f "$LOGS"/*.log "$LOGS"/*.json
# Keep what was learned before, then start from nothing.
for f in playbook.json playbook.md reflected.json reflexes.json; do [ -f "$AP/$f" ] && cp "$AP/$f" "$LOGS/before/"; done
rm -rf "$AP/Traces" "$AP/Inbox" "$AP/Telemetry" "$AP/playbook.json" "$AP/playbook.md" "$AP/reflected.json" "$AP/reflexes.json"
dotnet build "$ROOT/Runner/AgentPlay.Runner.csproj" -c Debug -v q || exit 1
for i in $(seq 1 "$RUNS"); do
  "$UNITY_EXE" -batchmode -nographics -projectPath "$(cygpath -m "$PROJECT")" \
    -executeMethod HeistDemo.HeistAutoPlay.ApproveOnly -logFile "$(cygpath -m "$LOGS/approve-$i.log")"
  grep -h "approved reflex" "$LOGS/approve-$i.log" | sed "s/^/session $i: /"
  dotnet "$ROOT/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll" --brain opus --mode realtime --idle-exit 20 \
    > "$LOGS/session-$i.log" 2>&1 < /dev/null &
  RUNNER_PID=$!
  AGENTPLAY_SESSION_TAG=play AGENTPLAY_AUTOPLAY_SECONDS=600 "$UNITY_EXE" -projectPath "$(cygpath -m "$PROJECT")" \
    -executeMethod HeistDemo.HeistAutoPlay.Run -logFile "$(cygpath -m "$LOGS/autoplay-$i.log")"
  UNITY_CODE=$?
  wait "$RUNNER_PID"
  RUNNER_CODE=$?
  echo "session $i: runner=$RUNNER_CODE unity=$UNITY_CODE; $(grep -h 'reflect done' "$LOGS/session-$i.log")"
  # Criterion 5 needs consecutive sessions; a model failure (exit 4, e.g. a usage limit) or a failed reflect breaks the chain.
  FAILED="$(grep -h 'model call failed' "$LOGS/session-$i.log" | head -1)"
  if [ "$RUNNER_CODE" = 4 ] || [ -n "$FAILED" ]; then
    echo "session $i: model failure, batch stopped: $FAILED"
    exit 4
  fi
done
python -I "$ROOT/Tools/telemetry_trend.py" "$AP/Telemetry"
