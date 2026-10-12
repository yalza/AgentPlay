#!/usr/bin/env bash
# M3: an active reflex handles guard.spotted in-game; no model calls. Demos/Heist must not be open in another Editor.
# REFLEXES=<file> replaces the fixture (e2e-m3-learn.sh passes the reflexes learned from the pilot sessions).
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
PROJECT="$ROOT/Demos/Heist"
LOGS="$ROOT/.e2e/heist-reflex"
FIXTURE="${REFLEXES:-$PROJECT/AgentPlay/fixtures/reflexes-active.json}"
mkdir -p "$LOGS"
rm -rf "$PROJECT/AgentPlay/Traces" "$PROJECT/AgentPlay/Telemetry" "$LOGS"/*.log "$LOGS"/reflexes.*.json
if [ -f "$PROJECT/AgentPlay/reflexes.json" ]; then cp "$PROJECT/AgentPlay/reflexes.json" "$LOGS/reflexes.backup.json"; fi
cp "$FIXTURE" "$PROJECT/AgentPlay/reflexes.json"

dotnet build "$ROOT/Runner/AgentPlay.Runner.csproj" -c Debug -v q || exit 1
dotnet "$ROOT/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll" \
  --brain script "$(cygpath -w "$PROJECT/AgentPlay/scripts/reflex.json")" --idle-exit 20 --no-reflect \
  > "$LOGS/runner.log" 2>&1 < /dev/null &
RUNNER_PID=$!
AGENTPLAY_SESSION_TAG=debug AGENTPLAY_AUTOPLAY_SECONDS=240 "$UNITY_EXE" -projectPath "$(cygpath -m "$PROJECT")" \
  -executeMethod HeistDemo.HeistAutoPlay.Run -logFile "$(cygpath -m "$LOGS/autoplay.log")"
UNITY_CODE=$?
echo "Unity exited with $UNITY_CODE"
wait "$RUNNER_PID"
RUNNER_CODE=$?
echo "Runner exited with $RUNNER_CODE"
cp "$PROJECT/AgentPlay/reflexes.json" "$LOGS/reflexes.after.json"
python -I "$ROOT/Tools/verify_m3_reflex.py" "$PROJECT" "$LOGS" "$RUNNER_CODE" "$UNITY_CODE"
CODE=$?
if [ -f "$LOGS/reflexes.backup.json" ]; then mv "$LOGS/reflexes.backup.json" "$PROJECT/AgentPlay/reflexes.json"; else rm -f "$PROJECT/AgentPlay/reflexes.json"; fi
exit $CODE
