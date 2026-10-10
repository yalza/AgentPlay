#!/usr/bin/env bash
# Runs the package EditMode tests inside Testbed. Testbed must not be open in another Editor.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
OUT="$ROOT/Testbed/Logs"
mkdir -p "$OUT"
rm -f "$OUT/editmode-results.xml"
"$UNITY_EXE" -batchmode -nographics -projectPath "$(cygpath -m "$ROOT/Testbed")" \
  -runTests -testPlatform EditMode \
  -testResults "$(cygpath -m "$OUT/editmode-results.xml")" \
  -logFile "$(cygpath -m "$OUT/editmode.log")"
code=$?
if [ -f "$OUT/editmode-results.xml" ]; then
  grep -o '<test-run [^>]*>' "$OUT/editmode-results.xml" | grep -oE '(result|total|passed|failed)="[^"]*"' | tr '\n' ' '
  echo
  grep -B2 -A6 '<failure>' "$OUT/editmode-results.xml" | head -80
else
  echo "No results file. Compile errors:"
  grep -E 'error CS[0-9]+' "$OUT/editmode.log" | sort -u | head -40
fi
echo "Unity exit code: $code"
exit $code
