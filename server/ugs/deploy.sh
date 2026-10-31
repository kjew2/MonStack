#!/usr/bin/env bash
# Build the IdleRngBattle Cloud Code module and deploy it with the UGS Cloud Code Admin API (issue #17, specs/08 section 10).
# Needs UGS_CLI_SERVICE_KEY_ID, UGS_CLI_SERVICE_SECRET_KEY, UGS_CLI_PROJECT_ID and UGS_CLI_ENVIRONMENT_NAME, and the .NET 10 SDK.
# Uses curl rather than the ugs CLI: same endpoints, one fewer install. Not part of check.sh, because it needs credentials.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
module=IdleRngBattle
out="$repo/build/ugs"
: "${UGS_CLI_SERVICE_KEY_ID:?}" "${UGS_CLI_SERVICE_SECRET_KEY:?}" "${UGS_CLI_PROJECT_ID:?}" "${UGS_CLI_ENVIRONMENT_NAME:?}"

rm -rf "$out/publish" && mkdir -p "$out"
(cd "$here/$module" && dotnet publish -c Release -r linux-x64 --self-contained false -o "$out/publish" -nologo -v quiet)
rm -f "$out/publish/"*.pdb
rm -f "$out/$module.ccm"
(cd "$out/publish" && python3 -c 'import os,sys,zipfile
z=zipfile.ZipFile(sys.argv[1],"w",zipfile.ZIP_DEFLATED)
for f in sorted(os.listdir(".")): z.write(f)
z.close()' "$out/$module.ccm")
echo "# $module.ccm: $(stat -c %s "$out/$module.ccm") bytes ($(ls "$out/publish" | wc -l) files)"

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
