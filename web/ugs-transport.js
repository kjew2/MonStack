// The account server on Unity Gaming Services (specs/27 section 10 step 4): Open and Commit on the MonStackAccount Cloud Code
// module (server/ugs/MonStackAccount), through the Cloud Code REST API with the signed-in player's token from web/auth.js. The
// player is whoever the token names; no call sends a player id.
//
//   const t = MonStackUgsTransport.create();          // options: { auth, projectId, module, fetch, timeoutMs }
//   await t.open({ deviceId, claim })                  -> { code, accepted, account (AccountSave text), notice, seq, hash,
//                                                           created, contentVersion, serverSec, day }
//   await t.commit({ deviceId, fromSeq, fromHash, commands: [{ k, a }], toHash, contentVersion })
//                                                       -> { code, accepted, index, reason, seq, hash, serverSec, day }
//   await t.endlessBoard(season, top)                  -> { season, total, top: [{ rank, name, floor, you }], you, fallbackName }
//   await t.gauntletBoard(species, top)                -> { species, total, top: [{ rank, name, species, speciesName, floor,
//                                                           cleared, label, you }], you, fallbackName }  (species null: overall)
//   await t.profile(of)                                -> { found, profile }  (of: whose, a UGS player id; empty: the caller's)
//
// `code` is "None" for an accepted call, else the rejection (NotHolder, Stale, ContentStale, Mismatch, Refused): a verdict.
// Anything else (no network, a timeout, 401, 429, 5xx, an answer that is not the module's) throws MonStackUgsTransport.Error
// with `retryable` set, so the caller keeps its pending commands and tries again (specs/27 section 4). The account arrives gzip
// then base64 (section 5.1) and is handed back as text. Self-contained: web/sync.js (step 3) takes it as its transport.
'use strict';
(function () {
  const BASE = 'https://cloud-code.services.api.unity.com/v1/projects/';
  const MODULE = 'MonStackAccount';
  const TIMEOUT_MS = 20 * 1000;   // above a cold Cloud Code worker's 12 to 15 s (specs/08 section 10.1, #109)

  class TransportError extends Error {
    constructor(message, status, retryable) { super(message); this.name = 'MonStackUgsTransportError'; this.status = status; this.retryable = retryable; }
  }

  // Not a verdict: the caller retries later. A 4xx other than 401, 408 and 429 means the module refused the call itself (a
  // malformed request or a damaged account): retrying the same call will not help.
  function retryableStatus(s) { return s === 0 || s === 401 || s === 408 || s === 429 || s >= 500; }

  function b64bytes(b64) {
    const bin = atob(b64);
    const out = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
    return out;
  }

  async function gunzipBase64(b64) {
    if (typeof DecompressionStream !== 'function') throw new TransportError('This browser cannot unpack the account (no DecompressionStream).', 0, false);
    const stream = new Blob([b64bytes(b64)]).stream().pipeThrough(new DecompressionStream('gzip'));
    return new Response(stream).text();
  }

  function create(options) {
    const o = options || {};
    const auth = o.auth || (typeof window !== 'undefined' ? window.MonStackAuth : null);
    if (!auth || typeof auth.token !== 'function') throw new Error('MonStackUgsTransport needs MonStackAuth (web/auth.js)');
    const projectId = o.projectId || (auth.config && auth.config.ugsProjectId);
    if (!projectId) throw new Error('MonStackUgsTransport needs the UGS project id');
    const moduleName = o.module || MODULE;
    const doFetch = o.fetch || ((...a) => fetch(...a));
    const timeoutMs = o.timeoutMs || TIMEOUT_MS;

    async function call(fn, params) {
      let token;
      try { token = await auth.token(); } catch (e) { throw new TransportError(e.message || 'Not signed in.', 401, true); }
      const ctl = typeof AbortController === 'function' ? new AbortController() : null;
      const timer = ctl ? setTimeout(() => ctl.abort(), timeoutMs) : null;
      let r;
      try {
        r = await doFetch(BASE + encodeURIComponent(projectId) + '/modules/' + encodeURIComponent(moduleName) + '/' + fn, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + token },
          body: JSON.stringify({ params }),
          signal: ctl ? ctl.signal : undefined,
        });
      } catch (e) {
        throw new TransportError(e && e.name === 'AbortError' ? 'The server took too long to answer.' : 'Could not reach the server.', 0, true);
      } finally { if (timer) clearTimeout(timer); }
      let j = null;
      try { j = await r.json(); } catch (e) { /* not JSON */ }
      if (!r.ok) {
        const why = (j && (j.detail || j.title || j.message)) || ('HTTP ' + r.status);
        throw new TransportError(fn + ': ' + why, r.status, retryableStatus(r.status));
      }
      const out = j && typeof j === 'object' && 'output' in j ? j.output : j;
      if (!out || typeof out !== 'object') throw new TransportError(fn + ': the server sent an answer that is not the module\'s', r.status, true);
      return out;
    }

    function verdict(fn, out) {
      if (typeof out.code !== 'string' || typeof out.accepted !== 'boolean') throw new TransportError(fn + ': the answer has no verdict', 200, true);
      return out;
    }

    return {
      kind: 'ugs',
      async open(req) {
        const out = verdict('Open', await call('Open', { request: { deviceId: req.deviceId, claim: !!req.claim } }));
        if (out.accepted) {
          if (typeof out.account !== 'string') throw new TransportError('Open: the answer has no account', 200, true);
          out.account = await gunzipBase64(out.account);
        }
        return out;
      },
      async commit(req) {
        return verdict('Commit', await call('Commit', {
          request: {
            deviceId: req.deviceId, fromSeq: req.fromSeq, fromHash: req.fromHash,
            commands: req.commands, toHash: req.toHash, contentVersion: req.contentVersion,
          },
        }));
      },
      async endlessBoard(season, top) {
        return call('EndlessBoard', { season: season == null ? -1 : season, top: top || 0 });
      },
      async gauntletBoard(species, top) {
        return call('GauntletBoard', { species: species || '', top: top || 0 });
      },
      async profile(of) {
        return call('Profile', { of: of || '' });
      },
    };
  }

  const api = { create, Error: TransportError, gunzipBase64 };
  if (typeof window !== 'undefined') window.MonStackUgsTransport = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})();
