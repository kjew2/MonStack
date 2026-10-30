#!/usr/bin/env python3
"""Print the `files` map for publishing web/ as the claude.ai phone artifact.

Run web/wasm/build.sh first. Publish web/index.html with this map as `files` and the repo root as `root`.
Keys are the paths the page loads; values are the source files, relative to the repo root.
"""
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
WEB = os.path.join(ROOT, "web")
DIST = os.path.join(WEB, "wasm", "dist")

if not os.path.isfile(os.path.join(DIST, "_framework", "dotnet.js")):
    sys.exit("web/wasm/dist is not built: run web/wasm/build.sh first")

files = {}
with open(os.path.join(WEB, "index.html"), encoding="utf-8") as f:
    for src in re.findall(r'<script[^>]*\bsrc="([^":]+)"', f.read()):   # local scripts only
        files[src] = "web/" + src
for d, _, names in os.walk(DIST):
    for n in sorted(names):
        rel = os.path.relpath(os.path.join(d, n), WEB).replace(os.sep, "/")
        files[rel] = "web/" + rel
print(json.dumps(files, indent=1, sort_keys=True))
