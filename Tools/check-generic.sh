#!/usr/bin/env bash
# Fails when shared code (package Contract/Runtime/Editor and the runner) names a specific game (spec M2 section 2).
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
for dir in Contract Runtime Editor; do
  [ -d "$ROOT/Packages/com.agentplay.core/$dir" ] || { echo "check-generic: missing $dir"; exit 1; }
done
[ -d "$ROOT/Runner" ] || { echo "check-generic: missing Runner"; exit 1; }
hits=$(grep -rniE "tile.?thieves|testbed|heist"   "$ROOT/Packages/com.agentplay.core/Contract"   "$ROOT/Packages/com.agentplay.core/Runtime"   "$ROOT/Packages/com.agentplay.core/Editor"   "$ROOT/Runner"   --exclude-dir=bin --exclude-dir=obj)
if [ -n "$hits" ]; then
  echo "Game-specific references in shared code:"
  echo "$hits"
  exit 1
fi
echo "check-generic: OK"
