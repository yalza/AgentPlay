#!/usr/bin/env bash
# M2a end-to-end: the script brain plays the Heist demo turn-based until it wins.
# Demos/Heist must not be open in another Editor. Opens a Unity window for one to two minutes.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
PROJECT="$ROOT/Demos/Heist"
LOGS="$PROJECT/Logs"
mkdir -p "$LOGS"
rm -rf "$PROJECT/AgentPlay/Traces" "$LOGS/runner.log" "$LOGS/autoplay.log"

dotnet build "$ROOT/Runner/AgentPlay.Runner.csproj" -c Debug -v q || exit 1
dotnet "$ROOT/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll" \
  --brain script "$(cygpath -w "$PROJECT/AgentPlay/scripts/win.json")" --idle-exit 20 \
  > "$LOGS/runner.log" 2>&1 < /dev/null &
RUNNER_PID=$!

AGENTPLAY_AUTOPLAY_SECONDS=240 "$UNITY_EXE" -projectPath "$(cygpath -m "$PROJECT")" \
  -executeMethod HeistDemo.HeistAutoPlay.Run -logFile "$(cygpath -m "$LOGS/autoplay.log")"
UNITY_CODE=$?
echo "Unity exited with $UNITY_CODE"

wait "$RUNNER_PID"
RUNNER_CODE=$?
echo "Runner exited with $RUNNER_CODE"

bash "$ROOT/Tools/check-generic.sh" || exit 1
python -I "$ROOT/Tools/verify_m2a.py" "$ROOT" "$RUNNER_CODE" "$UNITY_CODE"
