// Facade over the C# training-run engine (IdleRng.Web.Engine.Run*, contract in web/wasm/BRIDGE.md "Run bridge").
// RunEngine.create(opts) -> Promise<run>. No game rule lives here: every number and label comes from the C# engine.
// The saved run is species + seed + books + command log (the C# side rebuilds it by replay); sealed monsters are kept
// as Vault entries in their own key until the account save (specs/13 T5) takes them over.
(function (root) {
  'use strict';

  const SAVE_KEY = 'idle-rng-run-v1';
  const VAULT_KEY = 'idle-rng-vault-v1';

  function randomSeedHex() {
    const a = new Uint32Array(2);
    try { root.crypto.getRandomValues(a); } catch (e) { a[0] = (Math.random() * 2 ** 32) >>> 0; a[1] = (Math.random() * 2 ** 32) >>> 0; }
    return a[0].toString(16).padStart(8, '0') + a[1].toString(16).padStart(8, '0');
  }
  function store(key) {
    return {
      get() { try { return root.localStorage.getItem(key); } catch (e) { return null; } },
      set(v) { try { root.localStorage.setItem(key, v); } catch (e) { /* storage blocked: play without saving */ } },
      del() { try { root.localStorage.removeItem(key); } catch (e) { /* ignore */ } },
    };
  }

  function wrap(host) {
    const runStore = store(SAVE_KEY), vaultStore = store(VAULT_KEY);
    const parse = (text) => {
      const v = JSON.parse(text);
      if (v && v.error) throw new Error(v.error);
      return v;
    };
    const content = parse(host.RunContent());
    let cache = null;

    const run = {
      content: content,
      saveKey: SAVE_KEY,
      version() { return host.Version(); },
      get state() { if (cache === null) cache = parse(host.RunState()); return cache; },
      hasSave() { return !!runStore.get(); },
      start(speciesId, opts) {
        opts = opts || {};
        parse(host.RunStart(speciesId, opts.seedHex || randomSeedHex(), JSON.stringify(opts.books || [])));
        cache = null; run.persist();
      },
      load() {                      // true when a saved run was restored; a save that no longer replays is dropped
        const text = runStore.get();
        if (!text) return false;
        try { parse(host.RunLoad(text)); cache = null; return true; }
        catch (e) { console.warn('Saved run not loaded:', e); runStore.del(); return false; }
      },
      persist() { const t = host.RunSave(); if (t && t !== 'null') runStore.set(t); },
      // One command; returns the result object. The engine refusing a command (ok:false) is reported, never thrown.
      cmd(name) {
        const r = parse(host.RunCmd(name, JSON.stringify([].slice.call(arguments, 1))));
        cache = null;
        if (r.ok) run.persist();
        return r;
      },
      // Freezes the finished run into a Vault entry and stores it (once per run hash).
      seal() {
        const r = parse(host.RunSeal(Date.now()));
        if (!r.ok) return r;
        const list = run.vault();
        if (!list.some((e) => e.hash === r.entry.hash)) { list.unshift(r.entry); vaultStore.set(JSON.stringify(list)); }
        return r;
      },
      vault() { try { const v = JSON.parse(vaultStore.get() || '[]'); return Array.isArray(v) ? v : []; } catch (e) { return []; } },
      abandon() { host.RunAbandon(); cache = null; runStore.del(); },
      randomSeedHex: randomSeedHex,
    };
    return run;
  }

  async function create(o) {
    const loaded = await root.IdleEngine.loadWasm(o || {});
    loaded.host.RunInit(loaded.contentPack);
    return wrap(loaded.host);
  }

  root.RunEngine = { create: create, SAVE_KEY: SAVE_KEY, VAULT_KEY: VAULT_KEY };
})(typeof self !== 'undefined' ? self : this);
