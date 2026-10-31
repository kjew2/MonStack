#!/usr/bin/env bash
# Rebuilds the C# engine for the browser and prints the Artifact publish call that republishes the phone artifact
# (https://claude.ai/artifact/LpdHtJ53ujKkXPizqPkkkg): one JSON object with url, file_path, root and files, ready to paste.
# Usage: web/wasm/publish-artifact.sh [--no-build]
#   --no-build   skip web/wasm/build.sh (only when web/wasm/dist was just built from this commit)
# Progress goes to stderr; stdout is only the JSON. Fails (non-zero, nothing on stdout) if the build fails or a listed file is missing.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
URL="https://claude.ai/artifact/LpdHtJ53ujKkXPizqPkkkg"
BUILD=true
for a in "$@"; do
  case "$a" in
    --no-build) BUILD=false ;;
    -h|--help) sed -n 2,6p "$0"; exit 0 ;;
    *) echo "unknown option $a" >&2; exit 2 ;;
  esac
done

if $BUILD; then
  bash "$HERE/build.sh" >&2 || { echo "publish-artifact: web/wasm/build.sh failed; nothing to publish" >&2; exit 1; }
fi

FILES=$(python3 "$HERE/artifact-files.py") || { echo "publish-artifact: artifact-files.py failed" >&2; exit 1; }
STAMP=$(python3 -c 'import json,sys; b=json.load(open(sys.argv[1])); print(b["commit"] + ("+dirty" if b["dirty"] else "") + " built " + b["built"])' \
  "$HERE/dist/build-info.json") || { echo "publish-artifact: dist/build-info.json has no build stamp; rebuild" >&2; exit 1; }
case "$STAMP" in *+dirty*) echo "publish-artifact: warning: built from a dirty tree ($STAMP)" >&2 ;; esac

python3 - "$URL" "$ROOT" "$FILES" <<'PY'
import json, sys
url, root, files = sys.argv[1], sys.argv[2], json.loads(sys.argv[3])
print(json.dumps({"action": "publish", "url": url, "file_path": root + "/web/index.html", "root": root, "files": files}, indent=1))
PY
echo "publish-artifact: $STAMP; publish the JSON above with the Artifact tool (files sources are relative to root)" >&2
