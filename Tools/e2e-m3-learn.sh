#!/usr/bin/env bash
# M3 with the real model (subscription usage: 3 reflector calls and one Opus game):
# three pilot play sessions (human controls, no agent) -> reflect -> lessons and a reflex proposal ->
# approve -> the learned reflex handles guard.spotted in the deterministic scenario -> one Opus game for telemetry.
# Demos/Heist must not be open in another Editor.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
PROJECT="$ROOT/Demos/Heist"
AP="$PROJECT/AgentPlay"
LOGS="$ROOT/.e2e/heist-learn"
mkdir -p "$LOGS"
rm -rf "$AP/Traces" "$AP/Inbox" "$AP/Telemetry" "$AP/playbook.json" "$AP/playbook.md" "$AP/reflected.json" "$AP/reflexes.json" "$LOGS"/*
dotnet build "$ROOT/Runner/AgentPlay.Runner.csproj" -c Debug -v q || exit 1
RUNNER="$ROOT/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll"

for i in 1 2 3; do
  AGENTPLAY_HEIST_PILOT=1 AGENTPLAY_SESSION_TAG=play AGENTPLAY_AUTOPLAY_SECONDS=180 "$UNITY_EXE" -projectPath "$(cygpath -m "$PROJECT")" \
    -executeMethod HeistDemo.HeistAutoPlay.Run -logFile "$(cygpath -m "$LOGS/pilot-$i.log")"
  echo "pilot $i: Unity exit $?"
done
mkdir -p "$LOGS/traces"
cp "$AP/Traces"/*.ndjson "$LOGS/traces/"

dotnet "$RUNNER" reflect --project "$(cygpath -w "$PROJECT")" > "$LOGS/reflect.log" 2>&1 < /dev/null
echo "reflect exit $?"
python -I "$ROOT/Tools/verify_m3_learn.py" "$PROJECT" "$LOGS" || exit 1

# Approve the learned reflex with the AutoPlay hook, then rerun the deterministic scenario with it.
"$UNITY_EXE" -batchmode -nographics -projectPath "$(cygpath -m "$PROJECT")" \
  -executeMethod HeistDemo.HeistAutoPlay.ApproveOnly -logFile "$(cygpath -m "$LOGS/approve.log")"
echo "approve exit $?"
LEARNED="$LOGS/reflexes-learned.json"
cp "$AP/reflexes.json" "$LEARNED"
REFLEXES="$LEARNED" bash "$ROOT/Tools/e2e-m3-reflex.sh" > "$LOGS/reflex-learned.out" 2>&1
REFLEX_CODE=$?
grep -E "^(PASS|FAIL|ALL|telemetry|reflex stats)" "$LOGS/reflex-learned.out"
[ "$REFLEX_CODE" -eq 0 ] || { echo "learned reflex scenario failed"; exit 1; }

# One Opus game with the learned reflex active; its telemetry goes into the verification notes.
cp "$LEARNED" "$AP/reflexes.json"
rm -rf "$AP/Telemetry"
dotnet "$RUNNER" --brain opus --idle-exit 20 --no-reflect > "$LOGS/opus-runner.log" 2>&1 < /dev/null &
RP=$!
AGENTPLAY_SESSION_TAG=debug AGENTPLAY_AUTOPLAY_SECONDS=900 "$UNITY_EXE" -projectPath "$(cygpath -m "$PROJECT")" \
  -executeMethod HeistDemo.HeistAutoPlay.Run -logFile "$(cygpath -m "$LOGS/opus-autoplay.log")"
echo "opus Unity exit $?"
wait $RP
echo "opus runner exit $?"
cat "$AP/Telemetry"/*.json
echo "LEARN E2E DONE"
