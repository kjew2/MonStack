// Facade over the C# monster-trainer engine (IdleRng.Web.Engine.Run*/Account*, contract in web/wasm/BRIDGE.md, "Run bridge").
// RunEngine.create(opts) -> Promise<run>. No game rule lives here: every number and label comes from the C# engine.
// The whole save is the C# AccountSave text (account, shards, talents, vault, and the active run as its replay recipe) in one
// localStorage key. A save that no longer loads is kept under a backup key, never overwritten, and reported in `loadError`.
(function (root) {
  'use strict';

  const SAVE_KEY = 'idle-rng-account-v1';
  const BAD_KEY = 'idle-rng-account-v1-unloadable';
  const OLD_KEYS = ['idle-rng-run-v1', 'idle-rng-vault-v1'];   // the first trainer slice's saves: clean save break (specs/13)

  function randomSeedHex() {
    const a = new Uint32Array(2);
    try { root.crypto.getRandomValues(a); } catch (e) { a[0] = (Math.random() * 2 ** 32) >>> 0; a[1] = (Math.random() * 2 ** 32) >>> 0; }
    return a[0].toString(16).padStart(8, '0') + a[1].toString(16).padStart(8, '0');
  }
  const ls = {
    get(k) { try { return root.localStorage.getItem(k); } catch (e) { return null; } },
    set(k, v) { try { root.localStorage.setItem(k, v); } catch (e) { /* storage blocked: play without saving */ } },
    del(k) { try { root.localStorage.removeItem(k); } catch (e) { /* ignore */ } },
  };
  const parse = (text) => {
    const v = JSON.parse(text);
    if (v && v.error) throw new Error(v.error);
    return v;
  };

  function wrap(host) {
    const content = parse(host.RunContent());
    let runCache, acctCache;
    const dirty = () => { runCache = undefined; acctCache = undefined; };

    const run = {
      content: content,
      saveKey: SAVE_KEY,
      loadNotice: null,          // set when a saved account loaded but gave something up (its run in progress was dropped)
      loadError: null,           // set when a saved account could not be loaded (it is kept under BAD_KEY)
      version() { return host.Version(); },
      get account() { if (acctCache === undefined) acctCache = parse(host.AccountView()); return acctCache; },
      get state() { if (runCache === undefined) runCache = parse(host.RunState()); return runCache; },   // the active run, or null
      persist() { const t = host.AccountSave(); if (t && t !== 'null') ls.set(SAVE_KEY, t); },
      // Account command (startRun, finishRun, buyTalent, abandon, debugGrantBook); returns the result, ok:false when refused.
      acct(name) {
        const r = parse(host.AccountCmd(name, JSON.stringify([].slice.call(arguments, 1))));
        dirty();
        if (r.ok) run.persist();
        return r;
      },
      // Run command (drill, rest, fight, take, discard, spend, book); returns the result, ok:false when refused.
      cmd(name) {
        const r = parse(host.RunCmd(name, JSON.stringify([].slice.call(arguments, 1))));
        dirty();
        if (r.ok) run.persist();
        return r;
      },
      // Read only: the floors Vault entry i fought in its gauntlet (the wheel replays them). Changes nothing, so no save.
      gauntlet(i) { return parse(host.AccountCmd('gauntlet', JSON.stringify([i]))); },
      startRun(speciesId, books) { return run.acct('startRun', speciesId, books || []); },
      finishRun() { return run.acct('finishRun', Date.now()); },
      randomSeedHex: randomSeedHex,
    };

    OLD_KEYS.forEach(ls.del);
    const saved = ls.get(SAVE_KEY);
    let loaded = false;
    if (saved) {
      try {
        const view = parse(host.AccountLoad(saved));
        loaded = true;
        // The save loaded but gave something up (a run saved under older rules was dropped): tell the player, and save the result.
        if (view.notice) { run.loadNotice = view.notice; ls.set(BAD_KEY, saved); }   // the original stays under BAD_KEY
      }
      catch (e) {
        run.loadError = e.message;
        console.warn('Saved account not loaded:', e);
        ls.set(BAD_KEY, saved);
        ls.del(SAVE_KEY);
      }
    }
    if (!loaded) { parse(host.AccountNew(randomSeedHex())); run.persist(); }
    else if (run.loadNotice) run.persist();
    dirty();
    return run;
  }

  async function create(o) {
    const loaded = await root.IdleEngine.loadWasm(o || {});
    loaded.host.RunInit(loaded.contentPack);
    return wrap(loaded.host);
  }

  root.RunEngine = { create: create, SAVE_KEY: SAVE_KEY, BAD_KEY: BAD_KEY };
})(typeof self !== 'undefined' ? self : this);
