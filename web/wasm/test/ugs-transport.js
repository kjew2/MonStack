#!/usr/bin/env node
// Checks web/ugs-transport.js (specs/27 section 10 step 4) in plain Node against a fake Cloud Code endpoint (no network, no
// browser): the URL, the player's token and the request each call sends (never a player id), the gzip+base64 account unpacked
// to text, verdicts handed back as they come, and every failure that is not a verdict (no network, a timeout, 401, 429, 5xx, a
// reply that is not the module's) thrown as retryable while a refusal of the call itself (4xx) is not.
// Usage: node web/wasm/test/ugs-transport.js
'use strict';
const assert = require('assert');
const path = require('path');
const zlib = require('zlib');
const T = require(path.join(__dirname, '..', '..', 'ugs-transport.js'));

const PROJECT = 'e83a4b44-27a7-4f76-8769-08380b340736';
const auth = { config: { ugsProjectId: PROJECT }, token: async () => 'id-token-1' };

function reply(status, body) {
  return { ok: status >= 200 && status < 300, status, json: async () => { if (body === undefined) throw new Error('not JSON'); return body; } };
}

// A fetch that records each call and answers with the next scripted reply (a function gets the parsed request).
function fake(...answers) {
  const calls = [];
  const f = async (url, init) => {
    const c = { url, init, body: JSON.parse(init.body) };
    calls.push(c);
    const a = answers.shift();
    if (typeof a === 'function') return a(c);
    if (a instanceof Error) throw a;
    return a;
  };
  f.calls = calls;
  return f;
}

async function rejects(p, check) {
  try { await p; } catch (e) { check(e); return; }
  assert.fail('expected a rejection');
}

(async () => {
  const text = JSON.stringify({ SchemaVersion: 12, Seed: '123', Vault: [], note: 'é'.repeat(50) });
  const packed = zlib.gzipSync(Buffer.from(text, 'utf8')).toString('base64');
  assert.strictEqual(await T.gunzipBase64(packed), text, 'gzip + base64 round trip');
  // What the module's Gzip.Pack (.NET GZipStream, CompressionLevel.Optimal) writes.
  assert.strictEqual(await T.gunzipBase64('H4sIAAAAAAAAA6tWSivKz1WyUtLzcw1RcI/KLAguKUpNzFXSUaoAih5eCYJKtQAEEF8PJwAAAA=='), '{"from":".NET GZipStream","x":"ééé"}');

  // open: the call, and the account unpacked.
  let f = fake(reply(200, { output: { code: 'None', accepted: true, account: packed, seq: 0, hash: 'h0', created: true, contentVersion: '6.3.0', serverSec: 5, day: 1 } }));
  let t = T.create({ auth, fetch: f });
  let r = await t.open({ deviceId: 'dev-1', claim: true, playerId: 'forged' });
  assert.strictEqual(r.account, text);
  assert.strictEqual(r.created, true);
  let c = f.calls[0];
  assert.strictEqual(c.url, `https://cloud-code.services.api.unity.com/v1/projects/${PROJECT}/modules/MonStackAccount/Open`);
  assert.strictEqual(c.init.method, 'POST');
  assert.strictEqual(c.init.headers.Authorization, 'Bearer id-token-1');
  assert.deepStrictEqual(c.body, { params: { request: { deviceId: 'dev-1', claim: true } } }, 'only the device and the claim; never a player id');

  // A verdict comes back as it is (no account to unpack).
  f = fake(reply(200, { output: { code: 'NotHolder', accepted: false, seq: 0, created: false, contentVersion: '6.3.0', serverSec: 5, day: 1 } }));
  r = await T.create({ auth, fetch: f }).open({ deviceId: 'dev-2' });
  assert.strictEqual(r.code, 'NotHolder');
  assert.strictEqual(f.calls[0].body.params.request.claim, false);

  // commit: every field of the request, nothing else.
  f = fake(reply(200, { output: { code: 'Stale', accepted: false, index: -1, reason: 'the account is at commit 4, not 3', seq: 0, serverSec: 5, day: 1 } }));
  t = T.create({ auth, fetch: f, module: 'Other' });
  const commands = [{ k: 'drill', a: ['str'] }, { k: 'rest', a: [] }];
  r = await t.commit({ deviceId: 'dev-1', fromSeq: 3, fromHash: 'a', commands, toHash: 'b', contentVersion: '6.3.0', playerId: 'forged' });
  assert.strictEqual(r.code, 'Stale');
  assert.ok(f.calls[0].url.endsWith('/modules/Other/Commit'));
  assert.deepStrictEqual(f.calls[0].body, { params: { request: { deviceId: 'dev-1', fromSeq: 3, fromHash: 'a', commands, toHash: 'b', contentVersion: '6.3.0' } } });

  // endlessBoard: the server's season unless one is named.
  f = fake(reply(200, { output: { season: 2913, total: 0, top: [] } }), reply(200, { output: { season: 7, total: 0, top: [] } }));
  t = T.create({ auth, fetch: f });
  assert.strictEqual((await t.endlessBoard()).season, 2913);
  assert.deepStrictEqual(f.calls[0].body, { params: { season: -1, top: 0 } });
  await t.endlessBoard(7, 5);
  assert.deepStrictEqual(f.calls[1].body, { params: { season: 7, top: 5 } });

  // Failures that are not verdicts: retryable.
  const retryable = (status) => (e) => { assert.ok(e instanceof T.Error, e); assert.strictEqual(e.retryable, true, e.message); assert.strictEqual(e.status, status); };
  for (const s of [401, 408, 429, 500, 503]) {
    await rejects(T.create({ auth, fetch: fake(reply(s, { title: 'x', detail: 'busy' })) }).commit({ commands }), retryable(s));
  }
  await rejects(T.create({ auth, fetch: fake(new TypeError('Failed to fetch')) }).open({ deviceId: 'd' }), retryable(0));
  await rejects(T.create({ auth, fetch: fake(reply(200, { output: 'nope' })) }).open({ deviceId: 'd' }), retryable(200));
  await rejects(T.create({ auth, fetch: fake(reply(200, { output: { accepted: true } })) }).commit({ commands }), retryable(200));
  await rejects(T.create({ auth, fetch: fake(reply(200, { output: { code: 'None', accepted: true } })) }).open({ deviceId: 'd' }), retryable(200));
  await rejects(T.create({ auth, fetch: fake(reply(502)) }).open({ deviceId: 'd' }), retryable(502));
  const noToken = { config: auth.config, token: async () => { throw new Error('Not signed in.'); } };
  await rejects(T.create({ auth: noToken, fetch: fake() }).open({ deviceId: 'd' }), retryable(401));

  // A timeout aborts the call and is retryable.
  const hang = (c) => new Promise((_, reject) => c.init.signal.addEventListener('abort', () => { const e = new Error('aborted'); e.name = 'AbortError'; reject(e); }));
  await rejects(T.create({ auth, fetch: fake(hang), timeoutMs: 20 }).open({ deviceId: 'd' }), (e) => {
    retryable(0)(e);
    assert.match(e.message, /too long/);
  });

  // The module refused the call itself (a malformed request, a damaged account): not retryable.
  await rejects(T.create({ auth, fetch: fake(reply(422, { title: 'ScriptError', detail: 'a device id is required' })) }).open({ deviceId: '' }), (e) => {
    assert.strictEqual(e.retryable, false);
    assert.strictEqual(e.status, 422);
    assert.match(e.message, /a device id is required/);
  });

  assert.throws(() => T.create({ auth: {} }), /MonStackAuth/);
  assert.throws(() => T.create({ auth: { token: async () => 't', config: {} } }), /project id/);
  console.log('ugs-transport: all checks passed');
})().catch((e) => { console.error(e); process.exit(1); });
