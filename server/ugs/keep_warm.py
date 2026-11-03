#!/usr/bin/env python3
"""Keep the account server's Cloud Code worker warm (issue #109, server/ugs/README.md "Keep-warm").

A UGS schedule emits the event `monstack-keep-warm` every 10 minutes and a UGS trigger runs `MonStackAccount/Warm` on it, so a
worker stays up and players do not wait 12 to 15 s on a cold start (specs/08 section 10.1). About 4.3 k calls a month.

    python3 server/ugs/keep_warm.py          create the schedule and the trigger, or fix them; nothing changes if both are right
    python3 server/ugs/keep_warm.py --off    remove both

Needs UGS_CLI_SERVICE_KEY_ID, UGS_CLI_SERVICE_SECRET_KEY, UGS_CLI_PROJECT_ID and UGS_CLI_ENVIRONMENT_NAME, as deploy.sh does; the
service account also needs the project roles Scheduler Configuration Editor and Triggers Configuration Editor. UGS_API overrides
the admin API's base address (tests). .github/workflows/pages.yml runs this after it deploys the module. Exit 0: done; 3: the
service account lacks a role; 1: anything else.
"""
import base64
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request

NAME = 'monstack-keep-warm'
SCHEDULE = {'name': NAME, 'eventName': NAME, 'type': 'recurring', 'schedule': '*/10 * * * *', 'payloadVersion': 1, 'payload': '{}'}
# A scheduler event has no player, so no actionScopeType: Warm needs no player (AccountModule.Warm).
TRIGGER = {'name': NAME, 'eventType': f'com.unity.services.scheduler.{NAME}.v1', 'actionType': 'cloud-code',
           'actionUrn': 'urn:ugs:cloud-code:MonStackAccount/Warm'}
ROLES = 'Scheduler Configuration Editor and Triggers Configuration Editor'


class ApiError(Exception):
    def __init__(self, method, url, code, body):
        super().__init__(f'{method} {url}: HTTP {code} {body[:300]}')
        self.code = code


def main(argv):
    off = '--off' in argv[1:]
    unknown = [a for a in argv[1:] if a != '--off']
    if unknown:
        print(__doc__.strip(), file=sys.stderr)
        return 2
    try:
        key, secret, project, env_name = (os.environ[k] for k in (
            'UGS_CLI_SERVICE_KEY_ID', 'UGS_CLI_SERVICE_SECRET_KEY', 'UGS_CLI_PROJECT_ID', 'UGS_CLI_ENVIRONMENT_NAME'))
    except KeyError as e:
        print(f'keep-warm: {e.args[0]} is not set', file=sys.stderr)
        return 1
    api = os.environ.get('UGS_API', 'https://services.api.unity.com').rstrip('/')
    auth = 'Basic ' + base64.b64encode(f'{key}:{secret}'.encode()).decode()

    def call(method, url, body=None):
        data = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(url, data=data, method=method, headers={'Authorization': auth, 'Content-Type': 'application/json'})
        try:
            with urllib.request.urlopen(req, timeout=30) as r:
                text = r.read().decode()
        except urllib.error.HTTPError as e:
            raise ApiError(method, url, e.code, e.read().decode(errors='replace')) from None
        return json.loads(text) if text.strip() else None

    def configs(base):
        out, after = [], ''
        while True:
            page = call('GET', base + '?' + urllib.parse.urlencode({'limit': 100, **({'after': after} if after else {})}))
            out += page.get('configs') or []
            after = page.get('after') or ''
            if not after:
                return out

    def ensure(what, base, want):
        mine = [c for c in configs(base) if c.get('name') == NAME]
        keep = next((c for c in mine if not off and all(c.get(k) == v for k, v in want.items())), None)
        for c in mine:
            if c is not keep:
                call('DELETE', f'{base}/{c["id"]}')
                print(f'keep-warm: removed the {what} {c["id"]}')
        if off:
            return
        if keep:
            print(f'keep-warm: the {what} is in place ({keep["id"]})')
        else:
            made = call('POST', base, want) or {}
            print(f'keep-warm: created the {what} ({made.get("id", "?")})')

    try:
        envs = call('GET', f'{api}/unity/v1/projects/{project}/environments')
        env = next((e['id'] for e in envs.get('results') or [] if e.get('name') == env_name), None)
        if env is None:
            print(f'keep-warm: no environment named {env_name} in the project', file=sys.stderr)
            return 1
        tail = f'/v1/projects/{project}/environments/{env}/configs'
        # The trigger first, so the schedule's first event already has somewhere to go; removed in the other order.
        steps = [('trigger', f'{api}/triggers{tail}', TRIGGER), ('schedule', f'{api}/scheduler{tail}', SCHEDULE)]
        for what, base, want in (reversed(steps) if off else steps):
            ensure(what, base, want)
    except ApiError as e:
        if e.code in (401, 403):
            print(f'keep-warm: the service account may not do this ({e}). Give it the project roles {ROLES}'
                  ' (server/ugs/README.md).', file=sys.stderr)
            return 3
        print(f'keep-warm: {e}', file=sys.stderr)
        return 1
    except (urllib.error.URLError, OSError, ValueError) as e:
        print(f'keep-warm: {e}', file=sys.stderr)
        return 1
    print('keep-warm: off' if off else f'keep-warm: on, {SCHEDULE["schedule"]} (UTC) -> {TRIGGER["actionUrn"]}')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
