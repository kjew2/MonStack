#!/usr/bin/env python3
"""Print the `files` map for publishing web/ as the claude.ai phone artifact.

web/wasm/publish-artifact.sh builds and prints the whole publish call with this map; use that. By hand: run web/wasm/build.sh first, then publish web/index.html (the monster trainer, the page kj opens) with this map as `files` and the repo root as `root`.
Keys are the paths the pages load; values are the source files, relative to the repo root. Every other web/*.html page
goes in as a further page, with the scripts all pages load (there are none besides index.html now).
"""
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
WEB = os.path.join(ROOT, "web")
ENTRY = "index.html"
DIST = os.path.join(WEB, "wasm", "dist")

if not os.path.isfile(os.path.join(DIST, "_framework", "dotnet.js")):
    sys.exit("web/wasm/dist is not built: run web/wasm/build.sh first")

files = {}
pages = sorted(n for n in os.listdir(WEB) if n.endswith(".html"))
for page in pages:
    if page != ENTRY:
        files[page] = "web/" + page          # the entry page is the publish call's own file_path
    with open(os.path.join(WEB, page), encoding="utf-8") as f:
        for src in re.findall(r'<script[^>]*\bsrc="([^":]+)"', f.read()):   # local scripts only
            files[src] = "web/" + src
for d, _, names in os.walk(os.path.join(WEB, "art")):   # generated creature art (specs/26 section 8)
    for n in sorted(names):
        rel = os.path.relpath(os.path.join(d, n), WEB).replace(os.sep, "/")
        files[rel] = "web/" + rel
for d, _, names in os.walk(DIST):
    for n in sorted(names):
        rel = os.path.relpath(os.path.join(d, n), WEB).replace(os.sep, "/")
        files[rel] = "web/" + rel
# What build.sh always writes (build-info.json is the page's build stamp, index.html #build), plus every listed source.
required = ["web/wasm/dist/" + n for n in ("_framework/dotnet.js", "content-pack.json", "build-info.json")]
missing = sorted(set(src for src in list(files.values()) + required if not os.path.isfile(os.path.join(ROOT, src))))
if missing:
    sys.exit("listed files are missing: " + ", ".join(missing))
print(json.dumps(files, indent=1, sort_keys=True))
