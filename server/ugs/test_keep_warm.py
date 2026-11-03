#!/usr/bin/env python3
"""keep_warm.py against a fake UGS admin API (environments, scheduler and triggers configs). In check.sh; no network."""
import base64
import contextlib
import http.server
import io
import json
import os
import sys
import threading
import urllib.parse

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import keep_warm  # noqa: E402

PROJECT, ENV = 'proj', 'env-1'
AUTH = 'Basic ' + base64.b64encode(b'kid:sec').decode()


class Fake:
    def __init__(self):
        self.store = {'scheduler': {}, 'triggers': {}}
        self.next_id = 0
        self.denied = set()   # services answering 403
        self.calls = []


def handler(fake):
    class H(http.server.BaseHTTPRequestHandler):
        def log_message(self, *a):
            pass

        def reply(self, code, body=None):
            data = b'' if body is None else json.dumps(body).encode()
            self.send_response(code)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def route(self, method):
            u = urllib.parse.urlparse(self.path)
            fake.calls.append((method, u.path))
            if self.headers.get('Authorization') != AUTH:
                return self.reply(401, {'detail': 'bad auth'})
            parts = u.path.strip('/').split('/')
            if parts == ['unity', 'v1', 'projects', PROJECT, 'environments']:
                return self.reply(200, {'results': [{'id': 'env-0', 'name': 'dev'}, {'id': ENV, 'name': 'production'}]})
            if len(parts) < 7 or parts[0] not in fake.store or parts[1:6] != ['v1', 'projects', PROJECT, 'environments', ENV] \
                    or parts[6] != 'configs':
                return self.reply(404, {'detail': 'no route'})
            if parts[0] in fake.denied:
                return self.reply(403, {'detail': 'forbidden'})
            store = fake.store[parts[0]]
            if method == 'GET' and len(parts) == 7:
                q = urllib.parse.parse_qs(u.query)
                ids = sorted(store)
                start = ids.index(q['after'][0]) + 1 if 'after' in q else 0
                page = ids[start:start + 1]                      # one per page, so the listing must follow `after`
                after = page[-1] if start + 1 < len(ids) else ''
                return self.reply(200, {'configs': [store[i] for i in page], 'after': after})
            if method == 'POST' and len(parts) == 7:
                body = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
                fake.next_id += 1
                cid = f'id-{fake.next_id:03d}'
                store[cid] = {'id': cid, **body}
                return self.reply(200, {'id': cid})
            if method == 'DELETE' and len(parts) == 8 and parts[7] in store:
                del store[parts[7]]
                return self.reply(204)
            return self.reply(404, {'detail': 'no config'})

        def do_GET(self):
            self.route('GET')

        def do_POST(self):
            self.route('POST')

        def do_DELETE(self):
            self.route('DELETE')
    return H


def run(fake_url, *args, env='production'):
    os.environ.update({'UGS_API': fake_url, 'UGS_CLI_SERVICE_KEY_ID': 'kid', 'UGS_CLI_SERVICE_SECRET_KEY': 'sec',
                       'UGS_CLI_PROJECT_ID': PROJECT, 'UGS_CLI_ENVIRONMENT_NAME': env})
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        code = keep_warm.main(['keep_warm.py', *args])
    return code, out.getvalue() + err.getvalue()


def mine(fake, service):
    return [c for c in fake.store[service].values() if c['name'] == keep_warm.NAME]


def main():
    for k in ('HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy'):
        os.environ.pop(k, None)
    os.environ['NO_PROXY'] = os.environ['no_proxy'] = '127.0.0.1'
    fake = Fake()
    server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), handler(fake))
    threading.Thread(target=server.serve_forever, daemon=True).start()
    url = f'http://127.0.0.1:{server.server_address[1]}'
    failures = []

    def check(cond, what):
        print(('ok   ' if cond else 'FAIL ') + what)
        if not cond:
            failures.append(what)

    # Someone else's configs are never touched.
    fake.store['scheduler']['other-s'] = {'id': 'other-s', 'name': 'daily-reset', 'eventName': 'x', 'type': 'recurring',
                                          'schedule': '0 0 * * *', 'payloadVersion': 1, 'payload': '{}'}
    fake.store['triggers']['other-t'] = {'id': 'other-t', 'name': 'daily-reset', 'eventType': 'x', 'actionType': 'cloud-code',
                                         'actionUrn': 'urn:ugs:cloud-code:Other'}

    code, log = run(url)
    s, t = mine(fake, 'scheduler'), mine(fake, 'triggers')
    check(code == 0, f'first run succeeds (exit {code})')
    check(len(s) == 1 and all(s[0][k] == v for k, v in keep_warm.SCHEDULE.items()), 'creates the schedule, every 10 minutes')
    check(len(t) == 1 and t[0]['actionUrn'] == 'urn:ugs:cloud-code:MonStackAccount/Warm'
          and t[0]['eventType'] == 'com.unity.services.scheduler.monstack-keep-warm.v1', 'creates the trigger on Warm')
    posts = [p for m, p in fake.calls if m == 'POST']
    check(len(posts) == 2 and '/triggers/' in posts[0], 'the trigger is created before the schedule')

    fake.calls.clear()
    code, log = run(url)
    check(code == 0 and not [c for c in fake.calls if c[0] != 'GET'], 'a second run changes nothing')

    # A stale schedule (other cron) and a duplicate trigger are fixed: schedules cannot be edited, so delete and create.
    sid = s[0]['id']
    fake.store['scheduler'][sid]['schedule'] = '0 * * * *'
    fake.store['triggers']['dup'] = {'id': 'dup', **keep_warm.TRIGGER}
    code, log = run(url)
    s, t = mine(fake, 'scheduler'), mine(fake, 'triggers')
    check(code == 0 and len(s) == 1 and s[0]['id'] != sid and s[0]['schedule'] == '*/10 * * * *', 'replaces a stale schedule')
    check(len(t) == 1, 'removes a duplicate trigger')

    fake.calls.clear()
    code, log = run(url, '--off')
    deletes = [p for m, p in fake.calls if m == 'DELETE']
    check(len(deletes) == 2 and '/scheduler/' in deletes[0], '--off removes the schedule before the trigger')
    check(code == 0 and not mine(fake, 'scheduler') and not mine(fake, 'triggers'), '--off removes both')
    check('other-s' in fake.store['scheduler'] and 'other-t' in fake.store['triggers'], 'leaves other configs alone')

    fake.denied.add('scheduler')
    code, log = run(url)
    check(code == 3 and 'Scheduler Configuration Editor' in log, 'a missing role exits 3 and names the roles')
    fake.denied.clear()

    code, log = run(url, env='staging')
    check(code == 1 and 'staging' in log, 'an unknown environment fails')

    check(run(url, '--bogus')[0] == 2, 'an unknown option is a usage error')
    server.shutdown()
    if failures:
        print(f'{len(failures)} failed', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
