#!/usr/bin/env bash
# Run every check in the repo. Agents and humans run this before calling a change done.
#
#   ./check.sh            everything (what CI runs, see specs/09 section 6)
#   ./check.sh --fast     inner loop: skips the golden tests and the golden-file check
#   ./check.sh --no-dotnet  only the content check (for machines without the .NET SDK; the web build needs .NET too)
#   ./check.sh --no-wasm    skip the WebAssembly build and browser test (full mode runs them when the
#                           wasm-tools workload is installed; see web/wasm/BRIDGE.md)
#
# CI (.github/workflows/check.yml) runs the full mode. When CI is set to anything but "" or "false"
# (GitHub Actions sets CI=true), a missing wasm-tools workload is an error instead of a skip, so CI
# cannot pass without the web steps. --no-wasm still skips them on purpose.
#
# Exits non-zero on the first failure.
set -euo pipefail
cd "$(dirname "$0")"

FAST=0
DOTNET=1
WASM=1
for arg in "$@"; do
  case "$arg" in
    --fast) FAST=1 ;;
    --no-dotnet) DOTNET=0 ;;
    --no-wasm) WASM=0 ;;
    -h|--help) sed -n 2,13p "$0"; exit 0 ;;
    *) echo "unknown option: $arg" >&2; exit 2 ;;
  esac
done

step() { printf '\n== %s\n' "$1"; }

step "Content: content_src builds to Content/ with no drift"
python3 tools/content_build.py --check

step "Content: builder tests"
python3 tools/test_content_build.py >/dev/null

# E090/E091 (specs/05 section 6): no id removed without a MAJOR bump, the version never goes backwards. The baseline is the
# Content/ at the merge base with origin/main, so a branch is judged against what main already shipped. Locally a missing
# origin/main skips this; in CI (CI set) it is an error, because CI checks out the full history.
step "Content: ids compatible with main (E090, E091)"
if BASE="$(git merge-base HEAD origin/main 2>/dev/null)"; then
  python3 tools/content_build.py --check --baseline-ref "$BASE"
elif [ -n "${CI:-}" ] && [ "${CI:-}" != "false" ]; then
  echo "CI is set but origin/main is not available to compare content ids against (the checkout needs fetch-depth: 0)." >&2
  exit 1
else
  echo "origin/main not found: skipped the id compatibility check. Run 'git fetch origin main' first."
fi

if [ "$DOTNET" -eq 0 ]; then
  printf '\nSkipped .NET checks (--no-dotnet). Not a full pass.\n'
  exit 0
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet not found. Install the .NET SDK, or run with --no-dotnet for a partial check." >&2
  exit 1
fi

# Full mode runs the web steps when the wasm-tools workload is installed. In CI a missing workload fails here,
# before the slow steps, instead of skipping them at the end.
HAVE_WASM=0
if [ "$FAST" -eq 0 ] && [ "$WASM" -eq 1 ]; then
  WORKLOADS="$(dotnet workload list 2>/dev/null || true)"   # captured first: grep -q in a pipe can SIGPIPE dotnet under pipefail
  if grep -q wasm-tools <<<"$WORKLOADS"; then
    HAVE_WASM=1
  elif [ -n "${CI:-}" ] && [ "${CI:-}" != "false" ]; then
    echo "CI is set but the wasm-tools workload is not installed: CI may not skip the WebAssembly checks." >&2
    echo "Install it with: dotnet workload install wasm-tools (or pass --no-wasm to skip them on purpose)." >&2
    exit 1
  fi
fi

step ".NET: build"
dotnet build IdleRng.sln -c Release

BOTS="dotnet run -c Release --no-build --project src/IdleRng.Bots --"

# The balance report is not a gate (specs/09 section 7): this only keeps the verb and its output forms from rotting. Its sanity
# checks may print WARN here; that exits 0 by design (no --strict).
step "Bots: balance report smoke run (2 seeds; WARNs do not fail)"
$BOTS balance --seeds 2 > /dev/null
$BOTS balance --seeds 2 --format json | python3 -c 'import json, sys; json.load(sys.stdin)'
$BOTS balance --seeds 2 --format csv > /dev/null
$BOTS balance --seeds 2 --books own --no-tower > /dev/null

if [ "$FAST" -eq 1 ]; then
  step ".NET: unit tests (no golden)"
  dotnet test IdleRng.sln -c Release --no-build --filter "TestCategory!=Golden"
else
  step ".NET: all tests"
  dotnet test IdleRng.sln -c Release --no-build

  step "Bots: golden files match"
  $BOTS golden --check

  if [ "$HAVE_WASM" -eq 1 ]; then
    step "Web: C# core builds to WebAssembly"
    bash web/wasm/build.sh --fast

    step "Web: phone artifact publish call lists only files that exist"
    bash web/wasm/publish-artifact.sh --no-build > /dev/null

    step "Web: monster trainer plays a full run in headless Chromium"
    node web/wasm/test/run-trainer.js

    step "Web: phone-width screens, tree overwrite, save/reload/resume and stale or damaged saves in headless Chromium"
    node web/wasm/test/flows.js

    step "Web: player accounts (guest, Google link, second device, sign out, offline) in headless Chromium"
    node web/wasm/test/auth.js

    step "Web: the GitHub Pages site lays out"
    SITE_TMP=$(mktemp -d)
    bash web/wasm/site.sh "$SITE_TMP/site" > /dev/null
    [ -f "$SITE_TMP/site/index.html" ] && [ -f "$SITE_TMP/site/auth.js" ] && [ -f "$SITE_TMP/site/wasm/dist/_framework/dotnet.js" ] || { echo "site.sh left out files" >&2; exit 1; }
    rm -rf "$SITE_TMP"
  else
    printf '\nSkipped WebAssembly checks (--no-wasm or wasm-tools workload not installed). Not a full pass.\n'
  fi
fi

printf '\nAll checks passed.\n'
