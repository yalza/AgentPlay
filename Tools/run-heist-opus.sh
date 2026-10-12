#!/usr/bin/env bash
# Plays the Heist demo N times with the Opus brain (real model calls: subscription usage or API cost, per runner.json backend) and tallies the results.
# Usage: bash Tools/run-heist-opus.sh [N]   (default 5)
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
PROJECT="$ROOT/Demos/Heist"
LOGS="$PROJECT/Logs"
RUNS="${1:-5}"
mkdir -p "$LOGS"
dotnet build "$ROOT/Runner/AgentPlay.Runner.csproj" -c Debug -v q || exit 1
SUMMARY="$LOGS/opus-runs.txt"
: > "$SUMMARY"
wins=0
for i in $(seq 1 "$RUNS"); do
  dotnet "$ROOT/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll" --brain opus --idle-exit 20 \
    > "$LOGS/opus-runner-$i.log" 2>&1 < /dev/null &
  RUNNER_PID=$!
  AGENTPLAY_AUTOPLAY_SECONDS=900 "$UNITY_EXE" -projectPath "$(cygpath -m "$PROJECT")" \
    -executeMethod HeistDemo.HeistAutoPlay.Run -logFile "$(cygpath -m "$LOGS/opus-autoplay-$i.log")"
  UNITY_CODE=$?
  wait "$RUNNER_PID"
  RUNNER_CODE=$?
  [ "$RUNNER_CODE" -eq 0 ] && wins=$((wins + 1))
  tokens=$(grep -o "model calls .*" "$LOGS/opus-runner-$i.log" | tail -1)
  echo "run $i: runner=$RUNNER_CODE unity=$UNITY_CODE $tokens" | tee -a "$SUMMARY"
done
echo "wins: $wins/$RUNS" | tee -a "$SUMMARY"
