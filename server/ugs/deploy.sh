#!/usr/bin/env bash
# Build a Cloud Code C# module and create or update it with the UGS Cloud Code Admin API (specs/08 section 10, specs/27 section 7).
#
#   server/ugs/deploy.sh [Module]     Module: MonStackAccount (default, the account server) or IdleRngBattle (the spike)
#   server/ugs/deploy.sh --check      only build and package (no credentials, nothing uploaded)
#
# Needs the .NET 10 SDK (`dotnet` on PATH, or DOTNET=/path/to/dotnet) and, to upload, UGS_CLI_SERVICE_KEY_ID,
# UGS_CLI_SERVICE_SECRET_KEY, UGS_CLI_PROJECT_ID and UGS_CLI_ENVIRONMENT_NAME. MONSTACK_DEBUG_PLAYERS (optional: UGS player ids
# separated by spaces or commas) is built into the MonStackAccount module as its debug list (specs/27 section 6.3).
# .github/workflows/pages.yml runs this before it deploys the site. Uses curl rather than the ugs CLI: same endpoints, one fewer
# install. Build output goes to build/obj-net10 and build/bin-net10, apart from the .NET 8 build's (src/Directory.Build.props).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
CHECK=0
module=MonStackAccount
for arg in "$@"; do
  case "$arg" in
    --check) CHECK=1 ;;
    -h|--help) sed -n 2,11p "$0"; exit 0 ;;
    -*) echo "unknown option: $arg" >&2; exit 2 ;;
    *) module="$arg" ;;
  esac
done
[ -f "$here/$module/$module.csproj" ] || { echo "no module $module in server/ugs" >&2; exit 2; }
DOTNET="${DOTNET:-dotnet}"
out="$repo/build/ugs"
if [ "$CHECK" -eq 0 ]; then
  : "${UGS_CLI_SERVICE_KEY_ID:?}" "${UGS_CLI_SERVICE_SECRET_KEY:?}" "${UGS_CLI_PROJECT_ID:?}" "${UGS_CLI_ENVIRONMENT_NAME:?}"
fi

# The debug list as one MSBuild property value: ids separated by single spaces (a comma would split the -p argument).
debug="$(printf '%s' "${MONSTACK_DEBUG_PLAYERS:-}" | tr ',;\n\t' '    ' | tr -s ' ' | sed 's/^ //; s/ $//')"
case "$debug" in *[!A-Za-z0-9_\ -]*) echo "MONSTACK_DEBUG_PLAYERS holds something that is not a player id" >&2; exit 2 ;; esac

rm -rf "$out/publish" && mkdir -p "$out"
(cd "$here/$module" && "$DOTNET" publish -c Release -r linux-x64 --self-contained false -o "$out/publish" -nologo -v quiet \
  -p:MonStackBuildSuffix=-net10 "-p:MonStackDebugPlayers=$debug")
rm -f "$out/publish/"*.pdb
rm -f "$out/$module.ccm"
(cd "$out/publish" && python3 -c 'import os,sys,zipfile
z=zipfile.ZipFile(sys.argv[1],"w",zipfile.ZIP_DEFLATED)
for f in sorted(os.listdir(".")): z.write(f)
z.close()' "$out/$module.ccm")
echo "# $module.ccm: $(stat -c %s "$out/$module.ccm") bytes ($(ls "$out/publish" | wc -l) files); debug list: $(printf '%s' "$debug" | wc -w) players"
size="$(stat -c %s "$out/$module.ccm")"
[ "$size" -lt 10485760 ] || { echo "$module.ccm is over Cloud Code's 10 MB module limit" >&2; exit 1; }
[ "$CHECK" -eq 1 ] && exit 0

auth="Authorization: Basic $(printf '%s:%s' "$UGS_CLI_SERVICE_KEY_ID" "$UGS_CLI_SERVICE_SECRET_KEY" | base64 -w0)"
api=https://services.api.unity.com
envs="$(curl -sSf -H "$auth" "$api/unity/v1/projects/$UGS_CLI_PROJECT_ID/environments")"
env_id="$(printf '%s' "$envs" | python3 -c 'import json,sys; n=sys.argv[1]; print(next(e["id"] for e in json.load(sys.stdin)["results"] if e["name"]==n))' "$UGS_CLI_ENVIRONMENT_NAME")"
base="$api/cloud-code/v1/projects/$UGS_CLI_PROJECT_ID/environments/$env_id/modules"

# Create; if the module already exists, update it in place.
code="$(curl -sS -o "$out/deploy.json" -w '%{http_code}' -H "$auth" "$base" \
  -F "name=$module" -F 'tags={}' -F 'language=CS' -F "file=@$out/$module.ccm")"
if [ "$code" = 409 ] || { [ "$code" = 400 ] && grep -q "\"code\":9019" "$out/deploy.json"; }; then
  code="$(curl -sS -o "$out/deploy.json" -w '%{http_code}' -X PATCH -H "$auth" "$base/$module" \
    -F 'tags={}' -F "file=@$out/$module.ccm")"
fi
echo "# deploy HTTP $code: $(cat "$out/deploy.json")"
[ "${code:0:1}" = 2 ]
