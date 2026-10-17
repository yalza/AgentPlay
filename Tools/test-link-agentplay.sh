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

# Files the team committed are never deleted, even where AgentPlay would put its own.
TRACKED="$TMP/TrackedGame"
mkdir -p "$TRACKED/Packages" "$TRACKED/Assets/AgentPlayAdapter" "$TRACKED/AgentPlay"
echo '{"dependencies":{}}' > "$TRACKED/Packages/manifest.json"
echo "// committed adapter" > "$TRACKED/Assets/AgentPlayAdapter/Committed.cs"
echo "# committed brief" > "$TRACKED/AgentPlay/brief.md"
git -C "$TRACKED" init -q && git -C "$TRACKED" add -A && git -C "$TRACKED" -c user.email=t@t -c user.name=t commit -q -m init
TRACKED_W="$(cygpath -w "$TRACKED")"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$TRACKED_W" -LocalOnly
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$TRACKED_W" -Unlink -Purge
check '[ -f "$TRACKED/Assets/AgentPlayAdapter/Committed.cs" ] && [ -f "$TRACKED/AgentPlay/brief.md" ]' "unlink and purge keep committed files"
check '[ -z "$(git -C "$TRACKED" status --porcelain)" ]' "git status clean after unlink of a project with committed AgentPlay files"

# A Unity project in a subfolder of the git repository.
SUB="$TMP/MonoRepo"
mkdir -p "$SUB/Client/Packages" "$SUB/Client/Assets" "$SUB/Server"
echo '{"dependencies":{}}' > "$SUB/Client/Packages/manifest.json"
echo "server" > "$SUB/Server/readme.txt"
git -C "$SUB" init -q && git -C "$SUB" add -A && git -C "$SUB" -c user.email=t@t -c user.name=t commit -q -m init
SUB_W="$(cygpath -w "$SUB/Client")"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$SUB_W" -LocalOnly
mkdir -p "$SUB/Client/Assets/AgentPlayAdapter" && echo "// adapter" > "$SUB/Client/Assets/AgentPlayAdapter/A.cs"
check '[ -z "$(git -C "$SUB" status --porcelain)" ]' "local-only in a repo subfolder leaves git status clean"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$SUB_W" -Unlink
check '[ -z "$(git -C "$SUB" status --porcelain)" ]' "unlink in a repo subfolder leaves git status clean"

# The user's own exclude lines survive byte for byte, non-ASCII included.
ENC="$TMP/EncGame"
mkdir -p "$ENC/Packages"
echo '{"dependencies":{}}' > "$ENC/Packages/manifest.json"
git -C "$ENC" init -q && git -C "$ENC" add -A && git -C "$ENC" -c user.email=t@t -c user.name=t commit -q -m init
printf '/Thư-mục-riêng/\n' >> "$ENC/.git/info/exclude"
cp "$ENC/.git/info/exclude" "$TMP/exclude.before"
ENC_W="$(cygpath -w "$ENC")"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$ENC_W" -LocalOnly
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$ENC_W" -Unlink -Purge
check 'cmp -s "$TMP/exclude.before" "$ENC/.git/info/exclude"' "user's exclude file restored byte for byte"

# A csc.rsp that is not ours is never touched.
printf -- '-nowarn:0618\n' > "$REPO/Assets/csc.rsp"
git -C "$REPO" add -A && git -C "$REPO" -c user.email=t@t -c user.name=t commit -q -m rsp
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$REPO_W" -LocalOnly
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$REPO_W" -Unlink
check 'grep -q "nowarn" "$REPO/Assets/csc.rsp"' "foreign csc.rsp kept"
# Remove any junction a failed step left behind with rmdir first: rm -rf could follow it into the real package.
for j in $(find "$TMP" -maxdepth 4 -name com.agentplay.core 2>/dev/null); do cmd //c rmdir "$(cygpath -w "$j")" > /dev/null 2>&1; done
check '[ -f "$ROOT/Packages/com.agentplay.core/package.json" ]' "real package untouched at the end"
rm -rf "$TMP"
exit $fail
