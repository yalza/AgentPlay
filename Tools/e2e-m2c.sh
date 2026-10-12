#!/usr/bin/env bash
# M2c end-to-end: the script brain plays TileThieves' prying prototype until the sack holds the goal.
# TileThieves must not be open in another Editor and must have the AgentPlay junction and adapter.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GAME="${TILETHIEVES_DIR:-/e/Unity-WorkSpace/TileThieves}"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
BRAIN="${1:-script}"
# TileThieves forbids touching its Logs/ folder, so logs live in this repo (git-ignored).
LOGS="${E2E_LOGS:-$ROOT/.e2e/tilethieves}"
mkdir -p "$LOGS"
rm -rf "$GAME/AgentPlay/Traces" "$LOGS/agentplay-runner.log" "$LOGS/agentplay-autoplay.log"

dotnet build "$ROOT/Runner/AgentPlay.Runner.csproj" -c Debug -v q || exit 1
if [ "$BRAIN" = "opus" ]; then
  BRAIN_ARGS=(--brain opus)
else
  BRAIN_ARGS=(--brain script "$(cygpath -w "$GAME/AgentPlay/scripts/pry6.json")")
fi
dotnet "$ROOT/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll" "${BRAIN_ARGS[@]}" --idle-exit 30 \
  > "$LOGS/agentplay-runner.log" 2>&1 < /dev/null &
RUNNER_PID=$!

AGENTPLAY_AUTOPLAY_SECONDS=300 "$UNITY_EXE" -projectPath "$(cygpath -m "$GAME")" \
  -executeMethod TileThieves.AgentAdapter.Editor.AgentAdapterAutoPlay.Run -logFile "$(cygpath -m "$LOGS/agentplay-autoplay.log")"
UNITY_CODE=$?
echo "Unity exited with $UNITY_CODE"
wait "$RUNNER_PID"
RUNNER_CODE=$?
echo "Runner exited with $RUNNER_CODE"
git -C "$GAME" restore Packages/packages-lock.json 2>/dev/null || true
python -I "$ROOT/Tools/verify_m2c.py" "$GAME" "$LOGS" "$RUNNER_CODE" "$UNITY_CODE"
