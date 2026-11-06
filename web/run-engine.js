// Facade over the C# monster-trainer engine (IdleRng.Web.Engine.Run*/Account*, contract in web/wasm/BRIDGE.md, "Run bridge").
// RunEngine.create(opts) -> Promise<run>. No game rule lives here: every number and label comes from the C# engine.
// With opts.sync (web/sync.js, the page) the account comes from the server (specs/27): the game starts once the server's
// account is open, every command the engine accepts is recorded for the next checkpoint, and the Dungeon day, the Endless
// season and the seal time come from the server's time. The AccountSave text in localStorage['idle-rng-account-v1'] is then a
// cache, replaced by every open. Without it (bare test pages) the account is this device's own: that key is loaded, or a new
// account made.
(function (root) {
  'use strict';

  const SAVE_KEY = 'idle-rng-account-v1';
  const DAY_MS = 86400000;

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

  function wrap(host, sync) {
    const content = parse(host.RunContent());
    let runCache, acctCache;
    const dirty = () => { runCache = undefined; acctCache = undefined; };

    const run = {
      content: content,
      saveKey: SAVE_KEY,
      loadNotice: null,          // set when a saved account loaded but gave something up (its run in progress was dropped)
      loadError: null,           // set when this device's own account could not be loaded (no sync; a new one replaced it)
      version() { return host.Version(); },
      get account() { if (acctCache === undefined) acctCache = parse(host.AccountView()); return acctCache; },
      get state() { if (runCache === undefined) runCache = parse(host.RunState()); return runCache; },   // the active run, or null
      persist() { const t = host.AccountSave(); if (t && t !== 'null') ls.set(SAVE_KEY, t); },
      // Account command (startRun, finishRun, buyOffer, rerollVendor, setLoadout, abandon, dismantle, lock, runTower, setRulebook, runEndless,
      // setName, breed, debugGrantBook); returns the result, ok:false when refused (or, with sync, while play waits for the server).
      acct(name) { return command(name, [].slice.call(arguments, 1), host.AccountCmd); },
      // Run command (drill, rest, fight, take, discard, spend, book, lineageChoice); returns the result, ok:false when refused.
      cmd(name) { return command(name, [].slice.call(arguments, 1), host.RunCmd); },
      // The time: the server's with sync (its time at the last answer plus the time since, issue #21), else this device's.
      now() { return sync ? sync.now() : Date.now(); },
      // Dungeon (specs/21 section 8). The day is the UTC day number of run.now(); the engine has no clock.
      towerDay() { return Math.floor(run.now() / DAY_MS); },
      towerInfo(day) { return parse(host.AccountCmd('towerInfo', JSON.stringify([day]))); },   // read only
      runTower(day, vaultIndices) { return run.acct('runTower', day, vaultIndices); },
      // Read only: one floor's fight of that day's attempt by that team, for playback (the same fight runTower fought).
      towerFloor(day, vaultIndices, floor) { return parse(host.AccountCmd('towerFloor', JSON.stringify([day, vaultIndices, floor]))); },
      // Endless Tower (specs/28): the season is the UTC week from Monday 00:00 UTC (EndlessSim.SeasonOf).
      endlessSeason(day) { const d = day == null ? run.towerDay() : day; return Math.floor((d + 3) / 7); },
      endlessInfo(season) { return parse(host.AccountCmd('endlessInfo', JSON.stringify([season]))); },   // read only
      runEndless(season, vaultIndices) { return run.acct('runEndless', season, vaultIndices); },
      // Read only: one floor's fight for that team (the same fight the climb fought there), and the same floor with edited
      // rulebooks (one text per team slot, null = the saved one), the editor's test. Neither pays nor saves anything.
      endlessFloor(season, vaultIndices, floor) { return parse(host.AccountCmd('endlessFloor', JSON.stringify([season, vaultIndices, floor]))); },
      endlessPreview(season, vaultIndices, floor, rulebooks) { return parse(host.AccountCmd('endlessPreview', JSON.stringify([season, vaultIndices, floor, rulebooks]))); },
      setRulebook(vaultIndex, text) { return run.acct('setRulebook', vaultIndex, text); },
      // The season board (specs/28 section 6), read only: the top rows and the player's own, from this account only (local:
      // true). readBoard gives the server's board when there is one.
      endlessBoard(season, top) { return parse(host.AccountCmd('endlessBoard', JSON.stringify(top == null ? [season] : [season, top]))); },
      // A gauntlet board (specs/19 section 9), read only: null for the overall board, else a species id. Local as endlessBoard.
      gauntletBoard(species, top) { return parse(host.AccountCmd('gauntletBoard', JSON.stringify(top == null ? [species == null ? null : species] : [species == null ? null : species, top]))); },
      // Any board, as a promise: kind 'endless' (arg: the season) or 'gauntlet' (arg: a species id, null for overall). The
      // server's board when the page syncs with a remote server (web/sync.js on Pages), else the local board above (local: true).
      // A server board also says how far behind it is: `mine` is this account's own best (floor) for the "once it is saved" note.
      // The name fields come from this account (the server's may lag a pending setName); the generated name from the server,
      // which knows the player id. Rejects on a network failure.
      readBoard(kind, arg, top) {
        if (kind !== 'endless' && kind !== 'gauntlet') return Promise.reject(new Error('unknown board ' + kind));
        let own;
        try { own = kind === 'endless' ? run.endlessBoard(arg, top) : run.gauntletBoard(arg, top); }
        catch (e) { return Promise.reject(e); }
        const online = sync && sync.onlineBoard && sync.onlineBoard(kind) ? sync[kind + 'Board'](arg, top) : null;   // timed and checked
        if (!online) return Promise.resolve(own);
        return online.then((b) => ({
          ok: true, kind: kind, local: false, total: b.total || 0, top: b.top || [], you: b.you || null,
          season: kind === 'endless' ? b.season : undefined, species: kind === 'gauntlet' ? (b.species || null) : undefined,
          name: own.name, fallbackName: b.fallbackName || (b.you && !own.name ? b.you.name : null) || 'a generated name',
          nameMin: own.nameMin, nameMax: own.nameMax, mine: own.you ? own.you.floor : 0,
        }));
      },
      // The display name boards show ("" clears it); refused with the reason for a name the rules do not allow.
      setName(name) { return run.acct('setName', name); },
      startRun(speciesId, books, talentsOff) { return talentsOff ? run.acct('startRun', speciesId, books || [], talentsOff) : run.acct('startRun', speciesId, books || []); },
      finishRun() { return run.acct('finishRun', run.now()); },
      // Synthesis consumes two parents and inherits one distinct generic tree from each. The host validates everything.
      breed(primaryIndex, supportingIndex, talentsOff, focusTree, supportingTree) {
        return run.acct('breed', primaryIndex, supportingIndex, talentsOff == null ? null : talentsOff, focusTree, supportingTree);
      },
      randomSeedHex: randomSeedHex,
    };

    function command(name, args, fn) {
      if (sync && !sync.mayApply(name)) return { ok: false, reason: sync.waitReason(), waiting: true };
      const r = parse(fn(name, JSON.stringify(args)));
      dirty();
      if (r.ok) { run.persist(); if (sync) sync.record(name, args); }
      return r;
    }
    // The post-run vendor is transient (kj, 2026-11-01, specs/18 section 4): seen only on the post-run screens of the page that
    // sealed the run. A load closes one left open (a command, so the server closes it too), so a reload never brings it back.
    function closeVendor() { if (run.account.vendor) run.acct('closeVendor'); }

    // The hooks web/sync.js drives: load the server's account, apply a pending command again, the account hash, the cache.
    const core = {
      host, contentVersion: content.version, persist: run.persist,
      load(text) { const v = parse(host.AccountLoad(text)); dirty(); return v; },
      apply(c) { const r = parse((sync.RUN_COMMANDS.indexOf(c.k) >= 0 ? host.RunCmd : host.AccountCmd)(c.k, JSON.stringify(c.a || []))); dirty(); return r; },
      hash() { return parse(host.AccountHash()).hash; },
    };
    if (sync) {
      return sync.start(core).then((res) => {
        run.loadNotice = res.notice;
        closeVendor();
        dirty();
        return run;
      });
    }
    // No sync (bare test pages): this device's own account.
    const saved = ls.get(SAVE_KEY);
    let loaded = false;
    if (saved) {
      try { const view = parse(host.AccountLoad(saved)); loaded = true; if (view.notice) run.loadNotice = view.notice; }
      catch (e) { run.loadError = e.message; console.warn('Saved account not loaded:', e); }
    }
    if (!loaded) parse(host.AccountNew(randomSeedHex()));
    closeVendor();
    run.persist();
    dirty();
    return Promise.resolve(run);
  }

  async function create(o) {
    o = o || {};
    const loaded = await root.IdleEngine.loadWasm(o);
    loaded.host.RunInit(loaded.contentPack);
    return wrap(loaded.host, o.sync || null);
  }

  root.RunEngine = { create: create, SAVE_KEY: SAVE_KEY };
})(typeof self !== 'undefined' ? self : this);
