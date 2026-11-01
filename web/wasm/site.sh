#!/usr/bin/env bash
# Lays out the web game as a static site in <outdir> (the GitHub Pages deploy, .github/workflows/pages.yml; specs/08
# section 12.3): index.html plus exactly the files the phone artifact publishes (web/wasm/artifact-files.py).
# Usage: web/wasm/site.sh <outdir>   (run web/wasm/build.sh first; <outdir> is emptied)
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
OUT="${1:?usage: web/wasm/site.sh <outdir>}"
FILES=$(python3 "$HERE/artifact-files.py")
rm -rf "$OUT"
mkdir -p "$OUT"
python3 - "$ROOT" "$OUT" "$FILES" <<'PY'
import json, os, shutil, sys
root, out, files = sys.argv[1], sys.argv[2], json.loads(sys.argv[3])
files["index.html"] = "web/index.html"
for dest, src in files.items():
    d = os.path.join(out, dest)
    os.makedirs(os.path.dirname(d), exist_ok=True)
    shutil.copyfile(os.path.join(root, src), d)
open(os.path.join(out, ".nojekyll"), "w").close()   # serve _framework/ as-is (Jekyll skips folders starting with _)
print("site: %d files in %s" % (len(files), out))
PY
