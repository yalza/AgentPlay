#!/usr/bin/env bash
# Fails when shared code (package Contract/Runtime/Editor and the runner) names a specific game (spec M2 section 2).
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
hits=$(grep -rniE "tilethieves|testbed|heist" \
  "$ROOT/Packages/com.agentplay.core/Contract" \
  "$ROOT/Packages/com.agentplay.core/Runtime" \
  "$ROOT/Packages/com.agentplay.core/Editor" \
  "$ROOT/Runner" \
  --include=*.cs --include=*.json --include=*.asmdef --include=*.csproj \
  | grep -v "/bin/" | grep -v "/obj/")
if [ -n "$hits" ]; then
  echo "Game-specific references in shared code:"
  echo "$hits"
  exit 1
fi
echo "check-generic: OK"
