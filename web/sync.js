// Sync with the account server (specs/27 sections 4 to 6): the client plays on its own engine, records each command it applied,
// and sends them at checkpoints; the server re-applies them and keeps the result only when its hash matches.
// - Open (section 5.1): the game waits ("Connecting...") until the server answers, then loads the server's account and re-applies
//   the device's pending commands when the server is still at the device's base. Claims on a guest's first open.
// - Checkpoints (section 5.2), no timer: after every account-level command, when the page is hidden, when
//   CHECKPOINT_RUN_COMMANDS run commands are pending, and on open. One commit in flight at a time; a network failure keeps the
//   commands and retries with backoff, and play waits when an account-level command is next or the run commands pending reach
//   twice the checkpoint.
// - Rejections (section 4): NotHolder signs this device out (section 6.1); Stale reloads from the server; ContentStale says a new
//   version is out; Mismatch and Refused revert to the server's account and report the command.
// - One tab plays (section 5.3): a second tab sees the first through a BroadcastChannel.
// - Server time: the Dungeon day and the Endless season come from the time of the last Open or commit (issue #21), not the
//   device clock.
// The transport is pluggable (web/wasm/BRIDGE.md, "Sync transport"): the local test server in the page (section 5.4) unless one
// is set, so a remote endpoint (step 4, UGS Cloud Code) plugs in with the same JSON. No game rule lives here.
(function (root) {
  'use strict';

  const SYNC_KEY = 'monstack-sync-v1';                 // {deviceId, baseSeq, baseHash, pending[{k, a}], inflight, claim, lastReject}
  const SAVE_KEY = 'idle-rng-account-v1';              // the AccountSave cache (web/run-engine.js): server account + pending
  const OLD_KEYS = ['idle-rng-run-v1', 'idle-rng-vault-v1'];
  const BAD_PREFIX = 'idle-rng-account-v1-unloadable'; // kept-aside saves from before the server (and their -<ms> copies)
  const LOCAL_STORE_KEY = 'monstack-local-server-v1';  // the local test server's store (section 5.4)
  const LOCAL_PLAYER_KEY = 'monstack-local-player-v1'; // the local test server's player id for this browser
  const NOTICE_KEY = 'monstack-sync-notice';           // sessionStorage: a sentence to show after a reload this file asked for
  const TAB_CHANNEL = 'monstack-tab-v1';
  const CHECKPOINT_RUN_COMMANDS = 25;                  // placeholder (specs/27 section 5.2)
  const COMMIT_MAX_CLIMBS = 10;                        // the server's cap on Endless climbs in one commit (ServerConfig)
  const OPEN_TIMEOUT_MS = 20000;                       // above a cold Cloud Code worker (section 5.1)
  const COMMIT_TIMEOUT_MS = 20000;
  const RETRY_MS = [1000, 2000, 4000, 8000, 15000, 30000];
  const REFRESH_AFTER_HIDDEN_MS = 10 * 60 * 1000;      // a page shown again after this long asks the server for its time again
  const TAB_WAIT_MS = 300;                              // how long a new tab listens for one already playing
  const DAY_MS = 86400000;
  const PAGES = { origin: 'https://kjew2.github.io', path: '/MonStack/' };   // the GitHub Pages build (case-sensitive path)
  const RUN_COMMANDS = ['drill', 'rest', 'fight', 'take', 'discard', 'reroll', 'spend', 'book'];   // web/wasm/BRIDGE.md
  const SIGNED_OUT = 'Signed out: your account was opened on another device. Sign in to continue here.';

  const ls = {
    get(k) { try { return root.localStorage.getItem(k); } catch (e) { return null; } },
    set(k, v) { try { root.localStorage.setItem(k, v); return true; } catch (e) { return false; } },
    del(k) { try { root.localStorage.removeItem(k); } catch (e) { /* ignore */ } },
  };
  const ss = {
    take(k) { try { const v = root.sessionStorage.getItem(k); root.sessionStorage.removeItem(k); return v; } catch (e) { return null; } },
    set(k, v) { try { root.sessionStorage.setItem(k, v); } catch (e) { /* the notice is lost, nothing else */ } },
  };
  function randomId() {
    const a = new Uint32Array(4);
    try { root.crypto.getRandomValues(a); } catch (e) { for (let i = 0; i < 4; i++) a[i] = (Math.random() * 2 ** 32) >>> 0; }
    return Array.from(a, (x) => x.toString(16).padStart(8, '0')).join('');
  }
  const perfNow = () => (root.performance && root.performance.now ? root.performance.now() : Date.now());
  const isRun = (k) => RUN_COMMANDS.indexOf(k) >= 0;
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
  function timed(p, ms, what) {
    let t;
    return Promise.race([Promise.resolve(p), new Promise((_, rej) => { t = setTimeout(() => rej(new Error(what + ' timed out')), ms); })]).finally(() => clearTimeout(t));
  }

  // ------------------------------------------------------------------ the local test server (specs/27 section 5.4)
  // The server core (IdleRng.Server) compiled into the page's WebAssembly build, over a store kept in localStorage. Each call
  // reads the store and writes it back, so a second tab or a reload sees the last write. It answers synchronously (an answer,
  // not a promise), so a commit lands before the next tap even when taps come faster than the event loop turns.
  function localTransport(host, o) {
    o = o || {};
    const storeKey = o.storeKey || LOCAL_STORE_KEY, playerKey = o.playerKey || LOCAL_PLAYER_KEY;
    // With browser storage blocked (a private window, a sandboxed frame) the store and the player live in memory for this page.
    const mem = { store: null, player: null };
    const t = {
      kind: 'local',
      clockOffsetMs: 0,   // tests move the server's clock with this; the device clock stays as it is
      player() {
        let p = mem.player || ls.get(playerKey);
        if (!p) { p = 'local-' + randomId().slice(0, 16); if (!ls.set(playerKey, p)) mem.player = p; }
        return p;
      },
      // One call on the server core: op 'open', 'commit' or 'install'; returns the response, throws on a server error.
      call(op, playerId, req) {
        const out = JSON.parse(host.LocalServerCall(op, playerId, JSON.stringify(req), (mem.store != null ? mem.store : ls.get(storeKey)) || '', Date.now() + t.clockOffsetMs));
        if (out.error) throw new Error(out.error);
        if (ls.set(storeKey, out.store)) mem.store = null; else mem.store = out.store;
        return out.response;
      },
      open(req) { return t.call('open', t.player(), req); },
      commit(req) { return t.call('commit', t.player(), req); },
      // A test and development hook: store an account (AccountSave text) as this player's new head, held by this device.
      install(req) { return t.call('install', t.player(), req); },
      // The device was signed out (NotHolder): the next open is a new guest, made by genesis.
      signOut() { ls.del(playerKey); mem.player = null; },
    };
    return t;
  }
  const onPages = () => !!root.location && root.location.origin === PAGES.origin && root.location.pathname.startsWith(PAGES.path);

  // ------------------------------------------------------------------ state
  const S = {
    st: null,             // the persisted sync state (SYNC_KEY)
    core: null,           // the engine facade's hooks (web/run-engine.js)
    transport: null,
    ready: false,         // the account is open
    blocked: null,        // null, or why this page may not play ('version', 'tab', 'out')
    busy: false,          // a commit is in flight
    failing: false,       // the last call could not reach the server (play may wait)
    error: null,          // the last network or server error, for the account sheet
    retryN: 0, retryTimer: 0,
    serverMs: null, atPerf: 0,   // the server's time at the last answer, and performance.now() then
    hiddenAt: 0,
    listeners: [],
    waitMode: null,       // the overlay on screen: null, 'connecting', 'play', 'version', 'tab', 'taken'
    retryNow: null,       // resolves the boot's wait for "Try again"
  };

  function newState(deviceId) {
    return { deviceId: deviceId || randomId(), baseSeq: -1, baseHash: null, pending: [], inflight: null, claim: true, lastReject: null };
  }
  function loadState() {
    try {
      const v = JSON.parse(ls.get(SYNC_KEY) || 'null');
      if (!v || typeof v.deviceId !== 'string' || !Array.isArray(v.pending)) return null;
      return Object.assign(newState(v.deviceId), v);
    } catch (e) { return null; }
  }
  function saveState() { if (S.blocked !== 'tab' && S.blocked !== 'taken') ls.set(SYNC_KEY, JSON.stringify(S.st)); }
  const runPending = () => S.st.pending.filter((c) => isRun(c.k)).length;

  // The first open of this build deletes the device's account from before the server (decision 9, section 5.3).
  function wipeOld() {
    [SAVE_KEY].concat(OLD_KEYS).forEach(ls.del);
    try {
      const keys = [];
      for (let i = 0; i < root.localStorage.length; i++) { const k = root.localStorage.key(i); if (k && k.startsWith(BAD_PREFIX)) keys.push(k); }
      keys.forEach(ls.del);
    } catch (e) { /* storage blocked */ }
  }

  function stamp(r) {
    if (r && typeof r.serverSec === 'number' && r.serverSec > 0) { S.serverMs = r.serverSec * 1000; S.atPerf = perfNow(); }
  }
  // The server's time now: its time at the last answer plus the time since by the page's monotonic clock (agent, 2026-11-03:
  // so a page left open past midnight moves to the next day; the device's wall clock is never read).
  function now() { return S.serverMs == null ? Date.now() : Math.floor(S.serverMs + perfNow() - S.atPerf); }

  function emit() { S.listeners.forEach((f) => { try { f(state()); } catch (e) { console.warn('sync listener', e); } }); }
  function state() {
    const st = S.st || newState('');
    let status = 'connecting';
    if (S.blocked) status = 'blocked';
    else if (S.failing) status = 'offline';
    else if (S.busy) status = 'saving';
    else if (S.ready) status = st.pending.length ? 'pending' : 'saved';
    return {
      status, local: !!S.transport && S.transport.kind === 'local', seq: st.baseSeq, pending: st.pending.length,
      error: S.error, lastReject: st.lastReject, day: Math.floor(now() / DAY_MS), serverTime: S.serverMs != null,
    };
  }

  // ------------------------------------------------------------------ the overlay ("Connecting...", a new version, another tab)
  function el(tag, attrs, kids) {
    const e = root.document.createElement(tag);
    Object.entries(attrs || {}).forEach(([k, v]) => {
      if (v == null || v === false) return;
      if (k === 'text') e.textContent = v;
      else if (k.startsWith('on')) e.addEventListener(k.slice(2), v);
      else e.setAttribute(k, v === true ? '' : v);
    });
    (kids || []).forEach((c) => c && e.append(c));
    return e;
  }
  function showWait(mode, detail) {
    S.waitMode = mode;
    const doc = root.document;
    if (!doc || !doc.body) return;
    let box = doc.getElementById('sync-wait');
    if (!box) { box = el('div', { id: 'sync-wait', role: 'dialog', 'aria-modal': 'true', 'aria-live': 'polite' }); (doc.getElementById('app') || doc.body).append(box); }
    box.dataset.mode = mode;
    const card = el('div', { class: 'sync-card' });
    const p = (text, id, cls) => card.append(el('p', { 'data-id': id, class: cls || null, text }));
    if (mode === 'version') {
      card.append(el('h2', { text: 'A new version is out' }));
      p('Reload to play on it. Moves not saved yet are kept and played again on the new version.', 'sync-text');
      card.append(el('button', { class: 'btn primary', type: 'button', 'data-act': 'sync-reload', text: 'Reload', onclick: () => root.location.reload() }));
    } else if (mode === 'tab' || mode === 'taken') {
      card.append(el('h2', { text: mode === 'tab' ? 'MonStack is open in another tab' : 'MonStack was opened in another tab' }));
      p('Only one tab plays at a time.', 'sync-text');
      card.append(el('button', { class: 'btn primary', type: 'button', 'data-act': 'sync-here', text: 'Play here', onclick: playHere }));
    } else {
      card.append(el('h2', { 'data-id': 'sync-connecting', text: 'Connecting...' }));
      p(mode === 'play' ? 'Waiting for the server to save your last moves. Play goes on once it has them.' : 'Opening your account on the server.', 'sync-text');
      if (detail) p(detail, 'sync-error', 'small bad');
      if (detail) card.append(el('button', { class: 'btn', type: 'button', 'data-act': 'sync-retry', text: 'Try again', onclick: tryAgain }));
    }
    box.replaceChildren(card);
    box.hidden = false;
    emit();
  }
  function hideWait() {
    S.waitMode = null;
    const box = root.document && root.document.getElementById('sync-wait');
    if (box) box.remove();
    emit();
  }
  function tryAgain() {
    if (S.retryNow) { const f = S.retryNow; S.retryNow = null; f(); }
    else retry();
  }

  // ------------------------------------------------------------------ one tab (section 5.3)
  const TAB = { id: randomId(), born: Date.now(), other: false, chan: null, loaded: perfNow(), took: null };
  const olderThan = (m) => TAB.born < m.born || (TAB.born === m.born && TAB.id < m.id);
  try {
    if (typeof root.BroadcastChannel === 'function') {
      TAB.chan = new root.BroadcastChannel(TAB_CHANNEL);
      TAB.chan.onmessage = (ev) => {
        const m = ev.data || {};
        if (m.t === 'hello' && !S.blocked && olderThan(m)) TAB.chan.postMessage({ t: 'here', to: m.id });
        else if (m.t === 'here' && m.to === TAB.id) TAB.other = true;
        else if (m.t === 'take' && m.id !== TAB.id && !S.blocked) {
          S.blocked = 'taken';
          if (S.retryTimer) { clearTimeout(S.retryTimer); S.retryTimer = 0; }
          showWait('taken');
        }
      };
      TAB.chan.postMessage({ t: 'hello', id: TAB.id, born: TAB.born });
    }
  } catch (e) { TAB.chan = null; }
  function playHere() {
    if (TAB.chan) TAB.chan.postMessage({ t: 'take', id: TAB.id });
    if (S.blocked === 'taken') { root.location.reload(); return; }   // the other tab played meanwhile: open the account again
    if (TAB.took) { const f = TAB.took; TAB.took = null; f(); }
  }

  // ------------------------------------------------------------------ open (section 5.1)
  // The server's account replaces the device's copy. When the server is still at the device's base (or at the commit the device
  // sent last, which landed though its answer was lost), the pending commands are applied again on top and kept; otherwise
  // they are dropped. Commands the engine now refuses (new rules after a reload) are dropped with everything after them.
  function adopt(r) {
    const st = S.st, f = st.inflight;
    let keep = [];
    if (f && f.fromSeq === st.baseSeq && f.fromHash === st.baseHash && r.seq === f.fromSeq + 1 && r.hash === f.toHash) keep = st.pending.slice(f.count);
    else if (r.seq === st.baseSeq && r.hash === st.baseHash) keep = st.pending;
    else if (st.pending.length) console.info('sync: the server moved on; ' + st.pending.length + ' commands not saved were dropped');
    const view = S.core.load(r.account);
    const kept = [];
    for (const c of keep) {
      let res;
      try { res = S.core.apply(c); } catch (e) { res = { ok: false, reason: e.message }; }
      if (!res || !res.ok) { console.warn('sync: a pending command no longer applies, dropped with the ' + (keep.length - kept.length - 1) + ' after it', c, res && res.reason); break; }
      kept.push(c);
    }
    st.baseSeq = r.seq; st.baseHash = r.hash; st.pending = kept; st.inflight = null; st.claim = false;
    saveState();
    S.core.persist();
    return [r.notice, view && view.notice].filter(Boolean)[0] || null;
  }

  async function signOutHere() {
    S.st = newState(S.st.deviceId);   // a new guest: its first open claims (and genesis makes it the holder)
    saveState();
    if (S.transport.signOut) { try { S.transport.signOut(); } catch (e) { /* ignore */ } }
    if (root.MonStackAuth) { try { await root.MonStackAuth.signOut(); } catch (e) { /* the auth sheet shows its own error */ } }
  }

  function pickTransport(host) {
    if (S.transport) return S.transport;                       // set by a test or another script before start
    if (root.MonStackSyncTransport) return root.MonStackSyncTransport;
    // TODO(specs/27 step 4): on GitHub Pages use the UGS Cloud Code transport (MonStackUgsTransport, same open/commit JSON);
    // until it is deployed the Pages build runs the local test server too, and says so in the header.
    if (onPages() && root.MonStackUgsTransport) return root.MonStackUgsTransport;
    return localTransport(host);
  }

  function header() {
    const top = root.document && root.document.getElementById('top');
    if (!top || root.document.getElementById('server-mode')) return;
    if (S.transport.kind !== 'local') return;
    const tag = el('small', { id: 'server-mode', class: 'muted', title: 'The game\'s server runs inside this page: the account lives in this browser only', text: 'Local test build' });
    const build = root.document.getElementById('build');
    if (build) build.before(tag); else top.append(tag);
  }

  // Opens the account and loads it into the engine. Resolves {notice} once the game may start; never resolves while a new
  // version must be loaded first.
  async function start(core) {
    S.core = core;
    S.transport = pickTransport(core.host);
    header();
    const notices = [];
    const carried = ss.take(NOTICE_KEY);
    if (carried) notices.push(carried);

    const wait = TAB_WAIT_MS - (perfNow() - TAB.loaded);
    if (wait > 0) await sleep(wait);
    if (TAB.other) {
      S.blocked = 'tab';
      showWait('tab');
      await new Promise((r) => { TAB.took = r; });
      S.blocked = null;
    }
    // Read after the other tab (if any) stopped, so its last pending commands are not lost.
    let st = loadState();
    if (!st) { wipeOld(); st = newState(); }
    S.st = st;
    saveState();

    showWait('connecting');
    for (let n = 0; ; n++) {
      let r;
      try {
        r = await timed(S.transport.open({ deviceId: S.st.deviceId, claim: !!S.st.claim }), OPEN_TIMEOUT_MS, 'Opening the account');
        stamp(r);
        if (r.code === 'NotHolder') { notices.push(SIGNED_OUT); await signOutHere(); continue; }
        if (r.code && r.code !== 'None' && r.code !== 'ContentStale') throw new Error('the server answered ' + r.code);
        // Other content on the server, or an account stored by a newer server (ContentStale): a reload onto the new version.
        if (r.code === 'ContentStale' || (r.contentVersion && r.contentVersion !== core.contentVersion)) {
          S.blocked = 'version';
          showWait('version');
          return new Promise(() => {});
        }
        const notice = adopt(r);
        if (notice) notices.push(notice);
      } catch (e) {
        S.error = e && e.message ? e.message : String(e);
        S.failing = true;
        showWait('connecting', 'Could not reach the server: ' + S.error);
        await new Promise((res) => { S.retryNow = res; setTimeout(res, RETRY_MS[Math.min(n, RETRY_MS.length - 1)]); });
        S.retryNow = null;
        continue;
      }
      break;
    }
    S.ready = true; S.failing = false; S.error = null;
    hideWait();
    if (S.st.pending.length) commit();
    return { notice: notices.join(' ') || null };
  }

  // ------------------------------------------------------------------ commands and checkpoints (section 5.2)
  // Whether a command may be applied now. False shows the wait and starts a commit: an account-level command waits while the
  // server cannot be reached and commands are pending (its commit is the checkpoint), or when a commit could not carry it
  // (a second finishRun, more climbs than the server takes); run commands wait at twice the checkpoint.
  function mayApply(name) {
    if (S.blocked) { showWait(S.blocked === 'out' ? 'connecting' : S.blocked); return false; }
    if (!S.ready) return false;
    const p = S.st.pending;
    let wait;
    if (isRun(name)) wait = runPending() >= 2 * CHECKPOINT_RUN_COMMANDS;
    else {
      wait = (S.failing && p.length > 0)
        || (name === 'finishRun' && p.some((c) => c.k === 'finishRun'))
        || (name === 'runEndless' && p.filter((c) => c.k === 'runEndless').length >= COMMIT_MAX_CLIMBS);
    }
    if (!wait) return true;
    showWait('play', S.failing ? 'Could not reach the server' + (S.error ? ': ' + S.error : '') : null);
    if (!S.failing) commit();
    return false;
  }
  const waitReason = () => 'Waiting for the server to save your last moves';

  function record(name, args) {
    if (!S.ready || S.blocked) return;
    S.st.pending.push({ k: name, a: args });
    saveState();
    if (!isRun(name) || runPending() >= CHECKPOINT_RUN_COMMANDS) commit();
    else emit();
  }

  function reloadWith(notice) {
    if (notice) ss.set(NOTICE_KEY, notice);
    S.blocked = 'out';
    root.location.reload();
  }

  // Sends the pending commands. A retry after a lost answer sends the same commit again (same base, commands and hash), which
  // the server accepts again if it landed (section 4, idempotent); new commands go in the next one. A transport that answers
  // at once (the local test server) is handled at once; a promise when it settles.
  function commit() {
    if (S.busy || S.blocked || !S.ready) return;
    const st = S.st;
    if (!st.inflight) {
      if (!st.pending.length) return;
      let toHash;
      try { toHash = S.core.hash(); } catch (e) { S.error = e.message; emit(); return; }
      st.inflight = { fromSeq: st.baseSeq, fromHash: st.baseHash, count: st.pending.length, toHash };
      saveState();
    }
    const f = st.inflight;
    const req = { deviceId: st.deviceId, fromSeq: f.fromSeq, fromHash: f.fromHash, commands: st.pending.slice(0, f.count), toHash: f.toHash, contentVersion: S.core.contentVersion };
    S.busy = true; emit();
    let r;
    try { r = S.transport.commit(req); } catch (e) { commitFailed(e); return; }
    if (r && typeof r.then === 'function') timed(r, COMMIT_TIMEOUT_MS, 'Saving').then((a) => committed(req, f, a), commitFailed);
    else committed(req, f, r);
  }

  function commitFailed(e) {
    S.busy = false;
    if (S.blocked) return;
    S.failing = true; S.error = e && e.message ? e.message : String(e);
    scheduleRetry();
    if (S.waitMode === 'play') showWait('play', 'Could not reach the server: ' + S.error);
    emit();
  }

  function committed(req, f, r) {
    S.busy = false;
    if (S.blocked) return;
    stamp(r);
    const st = S.st;
    const code = r && r.code ? r.code : 'None';
    if (code === 'None') {
      st.baseSeq = r.seq; st.baseHash = r.hash; st.pending = st.pending.slice(f.count); st.inflight = null;
      S.failing = false; S.error = null; S.retryN = 0;
      if (S.retryTimer) { clearTimeout(S.retryTimer); S.retryTimer = 0; }
      saveState();
      if (S.waitMode === 'play') hideWait();
      if (st.pending.length && (st.pending.some((c) => !isRun(c.k)) || runPending() >= CHECKPOINT_RUN_COMMANDS)) commit();
      emit();
      return;
    }
    if (code === 'NotHolder') {
      S.blocked = 'out';
      signOutHere().then(() => reloadWith(SIGNED_OUT));
      return;
    }
    if (code === 'Stale') {
      st.pending = []; st.inflight = null; saveState();
      reloadWith('Your account changed on the server (another tab or device). It was loaded again.');
      return;
    }
    if (code === 'ContentStale') {
      st.inflight = null; saveState();   // the commands are kept and played again by the new version
      S.blocked = 'version';
      showWait('version');
      return;
    }
    // Mismatch or Refused: the server's account stands. Report the first differing command for diagnosis, never a ban.
    const bad = r.index >= 0 ? req.commands[r.index] : null;
    st.lastReject = { code, index: r.index, reason: r.reason || null, command: bad, at: now() };
    console.warn('sync: the server did not accept the commit (' + code + ')', r.reason, bad);
    st.pending = []; st.inflight = null; st.baseSeq = -1; st.baseHash = null;
    saveState();
    reloadWith('The server did not accept your last moves' + (r.reason ? ' (' + r.reason + ')' : '') + '. Your account was set back to the server\'s copy.');
  }

  function scheduleRetry() {
    if (S.retryTimer) return;
    const ms = RETRY_MS[Math.min(S.retryN++, RETRY_MS.length - 1)];
    S.retryTimer = setTimeout(() => { S.retryTimer = 0; commit(); }, ms);
  }
  function retry() {
    if (S.retryTimer) { clearTimeout(S.retryTimer); S.retryTimer = 0; }
    commit();
  }

  // A page shown again after a long time asks the server for its time (and learns whether another device took the account).
  async function refresh() {
    if (!S.ready || S.blocked || S.busy || S.st.pending.length || S.st.inflight) return;
    let r;
    try { r = await timed(S.transport.open({ deviceId: S.st.deviceId, claim: false }), OPEN_TIMEOUT_MS, 'Opening the account'); }
    catch (e) { return; }
    stamp(r);
    if (r.code === 'NotHolder') { await signOutHere(); reloadWith(SIGNED_OUT); return; }
    if (r.code === 'None' && (r.seq !== S.st.baseSeq || r.hash !== S.st.baseHash) && !S.st.pending.length) reloadWith(null);
    emit();
  }

  if (root.document) {
    root.document.addEventListener('visibilitychange', () => {
      if (root.document.visibilityState === 'hidden') { S.hiddenAt = perfNow(); commit(); return; }
      if (S.failing) retry();
      else if (S.hiddenAt && perfNow() - S.hiddenAt >= REFRESH_AFTER_HIDDEN_MS) refresh();
      S.hiddenAt = 0;
    });
  }
  if (root.addEventListener) {
    root.addEventListener('pagehide', () => commit());
    root.addEventListener('online', () => { if (S.failing) retry(); });
  }

  // After a Google sign-in (web/account.js): the device takes the account (section 5.1). On the local test server the player
  // does not follow the sign-in, so nothing changes; on a remote server a switch to another player opens that one.
  function signedIn(switched) {
    if (!S.ready || !S.transport || S.transport.kind === 'local' || !switched) return;
    S.st.claim = true; S.st.pending = []; S.st.inflight = null; S.st.baseSeq = -1; S.st.baseHash = null;
    saveState();
    reloadWith(null);
  }

  root.MonStackSync = {
    CHECKPOINT_RUN_COMMANDS, SYNC_KEY, LOCAL_STORE_KEY, RUN_COMMANDS: RUN_COMMANDS.slice(),
    get transport() { return S.transport; },
    set transport(t) { S.transport = t; },
    localTransport,
    start, mayApply, waitReason, record, commit, retry: tryAgain, signedIn,
    now, day: () => Math.floor(now() / DAY_MS),
    state,
    onChange: (f) => { S.listeners.push(f); return () => { const i = S.listeners.indexOf(f); if (i >= 0) S.listeners.splice(i, 1); }; },
    // Test and development hook of the local test server: store an account as this player's (web/wasm/test/harness.js).
    install(saveText) {
      if (!S.transport || !S.transport.install) throw new Error('only the local test server installs accounts');
      return S.transport.install({ account: saveText, deviceId: S.st.deviceId });
    },
  };
})(typeof self !== 'undefined' ? self : this);
