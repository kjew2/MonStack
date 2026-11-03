// Facade over the C# monster-trainer engine (IdleRng.Web.Engine.Run*/Account*, contract in web/wasm/BRIDGE.md, "Run bridge").
// RunEngine.create(opts) -> Promise<run>. No game rule lives here: every number and label comes from the C# engine.
// The whole save is the C# AccountSave text (account, shards, talents, achievements, vendor offers, vault, and the active run as its
// replay recipe) in one localStorage key. A save that no longer loads is kept aside under its own timestamped backup key and
// reported in `loadError` (damaged) or `loadNotice` (its run in progress was dropped). At most BAD_KEEP backups are kept: the oldest
// one (most likely the real account) is never evicted; a new backup evicts the second oldest. A backup key is never overwritten
// (specs/06 section 5).
(function (root) {
  'use strict';

  const SAVE_KEY = 'idle-rng-account-v1';
  const BAD_KEY = 'idle-rng-account-v1-unloadable';          // legacy single backup (before issue #93): kept, counts as the oldest
  const BAD_PREFIX = BAD_KEY + '-';                           // backups: BAD_PREFIX + Date.now() ms
  const BAD_KEEP = 3;                                         // placeholder cap
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
  // The backup keys, oldest first: the legacy BAD_KEY, then BAD_PREFIX + ms by time. Other keys under the prefix are left alone.
  function badKeys() {
    const out = [];
    try {
      for (let i = 0; i < root.localStorage.length; i++) {
        const k = root.localStorage.key(i);
        if (k === BAD_KEY || (k && k.startsWith(BAD_PREFIX) && /^\d+$/.test(k.slice(BAD_PREFIX.length)))) out.push(k);
      }
    } catch (e) { return []; }
    const ms = (k) => (k === BAD_KEY ? -1 : Number(k.slice(BAD_PREFIX.length)));
    return out.sort((a, b) => ms(a) - ms(b));
  }
  // Keep a save that did not load as it was, under a new backup key. The same text already kept is not kept twice.
  function keepAside(text) {
    const keys = badKeys();
    if (keys.some((k) => ls.get(k) === text)) return;
    let ms = Date.now();
    while (keys.indexOf(BAD_PREFIX + ms) >= 0) ms++;
    ls.set(BAD_PREFIX + ms, text);
    const all = badKeys();
    while (all.length > BAD_KEEP) ls.del(all.splice(1, 1)[0]);   // keep the oldest, evict the second oldest
  }
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
      loadError: null,           // set when a saved account could not be loaded (it is kept aside: keepAside)
      version() { return host.Version(); },
      get account() { if (acctCache === undefined) acctCache = parse(host.AccountView()); return acctCache; },
      get state() { if (runCache === undefined) runCache = parse(host.RunState()); return runCache; },   // the active run, or null
      persist() { const t = host.AccountSave(); if (t && t !== 'null') ls.set(SAVE_KEY, t); },
      // Account command (startRun, finishRun, buyOffer, rerollVendor, setLoadout, abandon, dismantle, lock, runTower, setRulebook, runEndless,
      // debugGrantBook); returns the result, ok:false when refused.
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
      // Dungeon (specs/21 section 8). The day is the UTC day number from this device's clock; the engine has no clock.
      towerDay() { return Math.floor(Date.now() / 86400000); },
      towerInfo(day) { return parse(host.AccountCmd('towerInfo', JSON.stringify([day]))); },   // read only
      runTower(day, vaultIndices) { return run.acct('runTower', day, vaultIndices); },
      // Read only: one floor's fight of that day's attempt by that team, for playback (the same fight runTower fought).
      towerFloor(day, vaultIndices, floor) { return parse(host.AccountCmd('towerFloor', JSON.stringify([day, vaultIndices, floor]))); },
      // Endless Tower (specs/28): the season is the UTC week from Monday 00:00 UTC (EndlessSim.SeasonOf).
      endlessSeason(day) { const d = day == null ? Math.floor(Date.now() / 86400000) : day; return Math.floor((d + 3) / 7); },
      endlessInfo(season) { return parse(host.AccountCmd('endlessInfo', JSON.stringify([season]))); },   // read only
      runEndless(season, vaultIndices) { return run.acct('runEndless', season, vaultIndices); },
      // Read only: one floor's fight for that team (the same fight the climb fought there), and the same floor with edited
      // rulebooks (one text per team slot, null = the saved one), the editor's test. Neither pays nor saves anything.
      endlessFloor(season, vaultIndices, floor) { return parse(host.AccountCmd('endlessFloor', JSON.stringify([season, vaultIndices, floor]))); },
      endlessPreview(season, vaultIndices, floor, rulebooks) { return parse(host.AccountCmd('endlessPreview', JSON.stringify([season, vaultIndices, floor, rulebooks]))); },
      setRulebook(vaultIndex, text) { return run.acct('setRulebook', vaultIndex, text); },
      startRun(speciesId, books, talentsOff) { return talentsOff ? run.acct('startRun', speciesId, books || [], talentsOff) : run.acct('startRun', speciesId, books || []); },
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
        if (view.notice) { run.loadNotice = view.notice; keepAside(saved); }   // the original is kept aside
      }
      catch (e) {
        run.loadError = e.message;
        console.warn('Saved account not loaded:', e);
        keepAside(saved);
        ls.del(SAVE_KEY);
      }
    }
    if (!loaded) { parse(host.AccountNew(randomSeedHex())); run.persist(); }
    else {
      // The post-run vendor is transient (kj, 2026-11-01, specs/18 section 4): seen only on the post-run screens of the page
      // that sealed the run. A load closes one left open, so a reload never brings it back.
      const v = parse(host.AccountView());
      if (v.vendor) { parse(host.AccountCmd('closeVendor', '[]')); run.persist(); }
      else if (run.loadNotice) run.persist();
    }
    dirty();
    return run;
  }

  async function create(o) {
    const loaded = await root.IdleEngine.loadWasm(o || {});
    loaded.host.RunInit(loaded.contentPack);
    return wrap(loaded.host);
  }

  root.RunEngine = { create: create, SAVE_KEY: SAVE_KEY, BAD_KEY: BAD_KEY, BAD_PREFIX: BAD_PREFIX, BAD_KEEP: BAD_KEEP, badKeys: badKeys };
})(typeof self !== 'undefined' ? self : this);
