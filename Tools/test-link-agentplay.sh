#!/usr/bin/env bash
# Tests Tools/link-agentplay.ps1 against a throwaway fake Unity project.
# The junction is removed with "cmd //c rmdir" only: rm -rf could follow it into the real package.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TMP="$(mktemp -d)"
PROJECT="$TMP/FakeGame"
mkdir -p "$PROJECT/Packages"
echo '{"dependencies":{}}' > "$PROJECT/Packages/manifest.json"
printf 'Library/' > "$PROJECT/.gitignore"
fail=0
check() { if eval "$1"; then echo "PASS $2"; else echo "FAIL $2"; fail=1; fi; }

LINK_PS="$(cygpath -w "$ROOT/Tools/link-agentplay.ps1")"
PROJECT_W="$(cygpath -w "$PROJECT")"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$PROJECT_W"
check '[ $? -eq 0 ]' "first link succeeds"
check '[ -f "$PROJECT/Packages/com.agentplay.core/package.json" ]' "junction exposes the package"
check '[ "$(grep -c "^/Packages/com.agentplay.core$" "$PROJECT/.gitignore")" -eq 1 ]' "gitignore has the junction once"
check 'grep -q "^Library/$" "$PROJECT/.gitignore"' "existing gitignore line kept"
check '[ -f "$PROJECT/AgentPlay/brief.md" ]' "brief template created"

powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$PROJECT_W"
check '[ $? -eq 0 ]' "second link is idempotent"
check '[ "$(grep -c "^/Packages/com.agentplay.core$" "$PROJECT/.gitignore")" -eq 1 ]' "gitignore still has the line once"

mkdir -p "$TMP/NotUnity"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$(cygpath -w "$TMP/NotUnity")" 2>/dev/null
check '[ $? -ne 0 ]' "refuses a folder without Packages/manifest.json"

cmd //c rmdir "$(cygpath -w "$PROJECT/Packages/com.agentplay.core")"
check '[ -f "$ROOT/Packages/com.agentplay.core/package.json" ]' "real package untouched after unlinking"
rm -rf "$TMP"
exit $fail
