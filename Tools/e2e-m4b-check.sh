#!/usr/bin/env bash
# M4b: `AgentPlay.Runner check` passes on Heist, driven by the generic AutoPlay; no model calls.
# Demos/Heist must not be open in another Editor.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
PROJECT="$ROOT/Demos/Heist"
LOGS="$ROOT/.e2e/m4b-check"
mkdir -p "$LOGS" && rm -f "$LOGS"/*.log
dotnet build "$ROOT/Runner/AgentPlay.Runner.csproj" -c Debug -v q || exit 1
dotnet "$ROOT/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll" check --idle-exit 120 > "$LOGS/check.log" 2>&1 < /dev/null &
CHECK_PID=$!
AGENTPLAY_AUTOPLAY_SECONDS=40 "$UNITY_EXE" -projectPath "$(cygpath -m "$PROJECT")" \
  -executeMethod AgentPlay.Editor.AutoPlay.Run -logFile "$(cygpath -m "$LOGS/autoplay.log")"
UNITY_CODE=$?
wait "$CHECK_PID"
CHECK_CODE=$?
cat "$LOGS/check.log"
fail=0
if [ "$CHECK_CODE" -eq 0 ]; then echo "PASS check exited 0"; else echo "FAIL check exited $CHECK_CODE"; fail=1; fi
if grep -q "FAIL " "$LOGS/check.log"; then echo "FAIL a check line failed"; fail=1; else echo "PASS no FAIL line"; fi
# No brain is attached, so the round never ends: the deadline exit (1) is the expected Unity exit here.
if [ "$UNITY_CODE" -eq 1 ]; then echo "PASS AutoPlay ended at its deadline"; else echo "FAIL AutoPlay exit $UNITY_CODE (expected 1)"; fail=1; fi
[ $fail -eq 0 ] && echo "ALL CHECKS PASSED"
exit $fail
