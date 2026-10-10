#!/usr/bin/env bash
# End-to-end check for M1: runner + Testbed in turn-based mode with simulated think time.
# Testbed must not be open in another Editor. Opens a Unity window for about a minute.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
LOGS="$ROOT/Testbed/Logs"
RESUME_SECONDS=2
THINK_MS=3000
mkdir -p "$LOGS"
rm -rf "$ROOT/Testbed/AgentPlay/Traces" "$LOGS/runner.log" "$LOGS/autoplay.log"

dotnet build "$ROOT/Runner/AgentPlay.Runner.csproj" -c Debug -v q || exit 1
dotnet "$ROOT/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll" \
  --mode turn_based --auto-resume --resume-seconds "$RESUME_SECONDS" --think-ms "$THINK_MS" --idle-exit 15 \
  > "$LOGS/runner.log" 2>&1 < /dev/null &
RUNNER_PID=$!

AGENTPLAY_AUTOPLAY_SECONDS=45 "$UNITY_EXE" -projectPath "$(cygpath -m "$ROOT/Testbed")" \
  -executeMethod AgentPlayTestbed.AutoPlay.Run -logFile "$(cygpath -m "$LOGS/autoplay.log")"
echo "Unity exited with $?"

wait "$RUNNER_PID"
echo "Runner exited with $?"
python -I "$ROOT/Tools/verify_m1.py" "$ROOT" "$RESUME_SECONDS" "$THINK_MS"
