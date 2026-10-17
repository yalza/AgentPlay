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

# --- LocalOnly / Unlink on a git repo ---
REPO="$TMP/TeamGame"
mkdir -p "$REPO/Packages" "$REPO/Assets"
echo '{"dependencies":{}}' > "$REPO/Packages/manifest.json"
printf 'Library/\n' > "$REPO/.gitignore"
git -C "$REPO" init -q && git -C "$REPO" add -A && git -C "$REPO" -c user.email=t@t -c user.name=t commit -q -m init
REPO_W="$(cygpath -w "$REPO")"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$REPO_W" -LocalOnly
check '[ $? -eq 0 ]' "local-only link succeeds"
mkdir -p "$REPO/Assets/AgentPlayAdapter" && echo "// adapter" > "$REPO/Assets/AgentPlayAdapter/A.cs" && touch "$REPO/Assets/AgentPlayAdapter.meta"
printf -- '-define:AGENTPLAY\n' > "$REPO/Assets/csc.rsp"
check '[ -z "$(git -C "$REPO" status --porcelain)" ]' "local-only leaves git status clean"
check 'grep -q "^Library/$" "$REPO/.gitignore" && [ "$(wc -l < "$REPO/.gitignore")" -eq 1 ]' "local-only does not touch .gitignore"
check '[ "$(grep -c "^# AgentPlay begin$" "$REPO/.git/info/exclude")" -eq 1 ]' "exclude block written once"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$REPO_W" -LocalOnly
check '[ "$(grep -c "^# AgentPlay begin$" "$REPO/.git/info/exclude")" -eq 1 ]' "second local-only link keeps one block"

powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$REPO_W" -Unlink
check '[ $? -eq 0 ]' "unlink succeeds"
check '[ ! -e "$REPO/Packages/com.agentplay.core" ]' "unlink removes the junction"
check '[ ! -e "$REPO/Assets/AgentPlayAdapter" ] && [ ! -e "$REPO/Assets/AgentPlayAdapter.meta" ]' "unlink removes the adapter"
check '[ ! -e "$REPO/Assets/csc.rsp" ]' "unlink removes our csc.rsp"
check '! grep -q -e "com.agentplay.core" -e "AgentPlayAdapter" -e "csc.rsp" "$REPO/.git/info/exclude"' "unlink removes the junction, adapter and csc.rsp lines"
check '[ "$(grep -c "^/AgentPlay/$" "$REPO/.git/info/exclude")" -eq 1 ]' "unlink keeps AgentPlay/ excluded while it is kept"
check '[ -d "$REPO/AgentPlay" ]' "unlink keeps AgentPlay/ without -Purge"
check '[ -z "$(git -C "$REPO" status --porcelain)" ]' "git status clean after unlink"
check '[ -f "$ROOT/Packages/com.agentplay.core/package.json" ]' "real package untouched after -Unlink"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$REPO_W" -Unlink
check '[ $? -eq 0 ] && [ -z "$(git -C "$REPO" status --porcelain)" ]' "unlink is idempotent"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$REPO_W" -Unlink -Purge
check '[ ! -e "$REPO/AgentPlay" ]' "purge removes AgentPlay/"
check '! grep -q "AgentPlay" "$REPO/.git/info/exclude"' "purge removes the whole exclude block"

# Git warnings on stderr (here: CRLF conversion) must not abort -Unlink halfway (PowerShell 5.1 + ErrorActionPreference Stop).
CRLF="$TMP/CrlfGame"
mkdir -p "$CRLF/Packages" "$CRLF/Assets"
echo '{"dependencies":{}}' > "$CRLF/Packages/manifest.json"
printf '{\n  "dependencies": {}\n}\n' > "$CRLF/Packages/packages-lock.json"
git -C "$CRLF" init -q && git -C "$CRLF" config core.autocrlf true && git -C "$CRLF" add -A 2>/dev/null && git -C "$CRLF" -c user.email=t@t -c user.name=t commit -q -m init
CRLF_W="$(cygpath -w "$CRLF")"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$CRLF_W" -LocalOnly
printf '{\n  "dependencies": {\n    "com.agentplay.core": {}\n  }\n}\n' > "$CRLF/Packages/packages-lock.json"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$CRLF_W" -Unlink
check '[ $? -eq 0 ]' "unlink succeeds when git warns on stderr"
check '[ -z "$(git -C "$CRLF" status --porcelain)" ]' "unlink restores packages-lock.json and keeps AgentPlay/ excluded despite git warnings"

# Changes the user had in packages-lock.json before linking are never discarded by -Unlink.
DIRTY="$TMP/DirtyLock"
mkdir -p "$DIRTY/Packages"
echo '{"dependencies":{}}' > "$DIRTY/Packages/manifest.json"
printf '{"dependencies":{}}\n' > "$DIRTY/Packages/packages-lock.json"
git -C "$DIRTY" init -q && git -C "$DIRTY" add -A && git -C "$DIRTY" -c user.email=t@t -c user.name=t commit -q -m init
printf '{"dependencies":{"user.change":{}}}\n' > "$DIRTY/Packages/packages-lock.json"
DIRTY_W="$(cygpath -w "$DIRTY")"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$DIRTY_W" -LocalOnly
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$DIRTY_W" -Unlink
check 'grep -q "user.change" "$DIRTY/Packages/packages-lock.json"' "unlink keeps the user's own packages-lock.json changes"

# A csc.rsp that is not ours is never touched.
printf -- '-nowarn:0618\n' > "$REPO/Assets/csc.rsp"
git -C "$REPO" add -A && git -C "$REPO" -c user.email=t@t -c user.name=t commit -q -m rsp
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$REPO_W" -LocalOnly
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$REPO_W" -Unlink
check 'grep -q "nowarn" "$REPO/Assets/csc.rsp"' "foreign csc.rsp kept"
rm -rf "$TMP"
exit $fail
