// Backend-agnostic engine facade (web/wasm/BRIDGE.md, "JS-side facade").
// Exposes IdleEngine.create(opts) -> Promise<engine>. The engine has the same members app.js used on `sim`:
//   state (getter), heroById, every Query/Cmd name, advance(n), drain(), emitHits (get/set), plus
//   reload(opts), exportSave(), version(), consolidatedRates(), backend, saveKey.
// Backends: 'js' wraps web/sim.js directly; 'wasm' drives the C# core through IdleRng.Web.Engine (JSON strings).
// UMD: window.IdleEngine in the browser, module.exports in Node.
(function (root) {
  'use strict';

  const SAVE_KEYS = { js: 'idle-rng-web-save-v1', wasm: 'idle-rng-wasm-save-v1' };
  const NOTICE_FALLBACK = 'C# engine unavailable, using JS engine';
  const WASM_BASE = 'wasm/dist/';

  const QUERIES = ['hp', 'bossHp', 'gold', 'statCost', 'statMult', 'gearTier', 'gearBasePct', 'gearEffPct', 'gearSlotMult', 'gearMult',
    'heroLevel', 'heroPct', 'heroMult', 'classMult', 'dps', 'hitDamage', 'hitsToKill', 'killTimeSeconds', 'isBossStage', 'bossClearable',
    'needDps', 'enhanceCost', 'enhanceSuccess', 'pityRate', 'freePullsRemaining', 'chestReadyAtMs', 'chestReady', 'bulkCost',
    'maxAffordable', 'pullPrice', 'bestParty', 'autoEquipWouldChange', 'nowMs', 'validate', 'consolidatedRates'];
  const COMMANDS = ['buyStatLevels', 'buyStatLevelsMax', 'enhance', 'attemptBoss', 'pull', 'equipHero', 'unequipHero', 'autoEquip',
    'claimChest', 'syncWallClock', 'debugGrantGold', 'debugGrantGems', 'debugSetStage'];

  function getIdleSim() {
    if (root.IdleSim) return root.IdleSim;
    if (typeof require === 'function') return require('./sim.js');
    throw new Error('IdleSim (sim.js) is not loaded');
  }
  function randomSeedHex() {
    const a = new Uint32Array(2);
    try { (root.crypto || require('crypto').webcrypto).getRandomValues(a); } catch (e) { a[0] = (Math.random() * 2 ** 32) >>> 0; a[1] = (Math.random() * 2 ** 32) >>> 0; }
    return a[0].toString(16).padStart(8, '0') + a[1].toString(16).padStart(8, '0');
  }
  // Same construction as Sim's constructor, so names/tier indexes match the JS reference exactly.
  function buildHeroById(content) {
    const out = {}, rar = content.rarities;
    content.heroes.pool.forEach((tierList) => tierList.forEach((h) => { out[h.id] = { id: h.id, name: h.name, short: h.short || h.name, tier: rar.indexOf(h.tier) }; }));
    return out;
  }
  function fillDefaults(o) {
    const now = o.nowMs !== undefined ? o.nowMs : Date.now();
    return { nowMs: now, profileSeedHex: o.profileSeedHex || randomSeedHex(), profileId: o.profileId || 'web-' + Date.now().toString(36) };
  }

  // ------------------------------------------------------------------ JS backend
  function createJsBackend(o) {
    const IS = getIdleSim(), content = o.content;
    let sim = null, emit = false;
    const eng = { backend: 'js', saveKey: SAVE_KEYS.js, content: content, heroById: buildHeroById(content) };
    function build(opts) {
      if (opts.saveString) return IS.deserialize(content, opts.saveString);
      const d = fillDefaults(opts);
      return IS.Sim.newProfile(content, { profileSeed: BigInt('0x' + d.profileSeedHex), nowMs: d.nowMs, profileId: d.profileId });
    }
    eng.reload = function (opts) { const s = build(opts || {}); sim = s; sim.emitHits = emit; eng.heroById = sim.heroById; };
    Object.defineProperty(eng, 'state', { get() { return sim.state; } });
    Object.defineProperty(eng, 'emitHits', { get() { return emit; }, set(v) { emit = !!v; sim.emitHits = emit; } });
    eng.advance = (n) => sim.advance(n);
    eng.drain = () => sim.drain();
    eng.exportSave = () => IS.serialize(sim.state);
    eng.version = () => 'web/sim.js (JS engine) content ' + content.version;
    eng.consolidatedRates = () => IS.consolidatedRates(content);
    for (const n of QUERIES.concat(COMMANDS)) if (n !== 'consolidatedRates') eng[n] = function () { return sim[n].apply(sim, arguments); };
    eng.reload(o);
    return eng;
  }

  // ------------------------------------------------------------------ WASM backend
  // `host` is the .NET export object (IdleRng.Web.Engine): Init, Advance, State, Drain, Query, Cmd, Export, SetEmitHits, Version.
  const PURE = new Set(['hp', 'bossHp', 'gold', 'statCost', 'gearTier', 'gearBasePct', 'gearEffPct', 'heroLevel', 'pityRate',
    'enhanceSuccess', 'bulkCost', 'consolidatedRates']);                           // functions of content and args only
  const BUILD = new Set(['dps', 'hitDamage', 'heroMult', 'gearMult', 'gearSlotMult', 'hitsToKill', 'killTimeSeconds']); // change only when the build changes
  const BUILD_IF_NO_ARG = new Set(['statMult', 'classMult']);                      // pure with an arg, build-keyed without

  function createWasmBackend(o, host, contentJson) {
    const content = o.content;
    const eng = { backend: 'wasm', saveKey: SAVE_KEYS.wasm, content: content, heroById: buildHeroById(content), host: host };
    let cache = null, dirty = true, pendingEvents = true, version = 0, emit = false;
    let buildKey = '', pureMemo = new Map(), buildMemo = new Map(), versionMemo = new Map(), stats = { queries: 0, hits: 0, stateReads: 0 };
    const cb = content.combat, ec = content.economy;
    eng.stats = stats;

    const touch = () => { dirty = true; pendingEvents = true; version++; versionMemo.clear(); };
    function refresh() {
      cache = JSON.parse(host.State());
      dirty = false; stats.stateReads++;
      const s = cache, g = s.gear, p = s.heroes.party, own = s.heroes.owned;
      let k = s.statLevel + '|' + s.classAdvances;
      for (let i = 0; i < g.length; i++) k += g[i] ? '|' + g[i].tier + ',' + g[i].rarity + ',' + g[i].enh : '|-';
      for (let i = 0; i < p.length; i++) k += p[i] ? '|' + p[i] + ':' + own[p[i]] : '|-';
      if (k !== buildKey) { buildKey = k; buildMemo.clear(); }
    }
    Object.defineProperty(eng, 'state', { get() { if (dirty) refresh(); return cache; } });
    Object.defineProperty(eng, 'stateVersion', { get() { return version; } });
    Object.defineProperty(eng, 'emitHits', { get() { return emit; }, set(v) { emit = !!v; host.SetEmitHits(emit ? 1 : 0); } });

    function wireArgs(a) {
      let n = a.length; while (n > 0 && a[n - 1] === undefined) n--;
      const out = new Array(n);
      for (let i = 0; i < n; i++) out[i] = a[i] === undefined ? null : a[i];
      return JSON.stringify(out);
    }
    const isErr = (v) => v && typeof v === 'object' && !Array.isArray(v) && typeof v.error === 'string' && !('ok' in v);
    function rawQuery(name, args) {
      stats.queries++;
      const v = JSON.parse(host.Query(name, args));
      if (isErr(v)) { console.error('engine query ' + name + ' failed: ' + v.error); return null; }
      return v;
    }
    function query(name, a) {
      const args = wireArgs(a);
      let memo = null;
      if (PURE.has(name)) memo = pureMemo;
      else if (BUILD.has(name)) { if (dirty) refresh(); memo = buildMemo; }
      else if (BUILD_IF_NO_ARG.has(name)) { if (args === '[]' || args === '[null]') { if (dirty) refresh(); memo = buildMemo; } else memo = pureMemo; }
      else memo = versionMemo;
      const key = name + args;
      if (memo.has(key)) { stats.hits++; return memo.get(key); }
      const v = rawQuery(name, args);
      if (memo !== pureMemo || pureMemo.size < 5000) memo.set(key, v);
      return v;
    }
    function cmd(name, a) {
      const r = JSON.parse(host.Cmd(name, wireArgs(a)));
      touch();
      if (isErr(r)) { console.error('engine command ' + name + ' failed: ' + r.error); return { ok: false, reason: 'engineError', error: r.error }; }
      return r === null ? undefined : r;                                 // void commands: sim.js returns undefined
    }
    for (const n of QUERIES) eng[n] = function () { return query(n, arguments); };
    for (const n of COMMANDS) eng[n] = function () { return cmd(n, arguments); };
    // Cheap pure functions of the cached state (same formulas as sim.js; the tests compare them with the C# queries).
    eng.nowMs = () => { const s = eng.state; return s.clock.lastSyncUtcMs + (s.tick - s.clock.tickAtLastSync) * (1000 / cb.TICK_HZ); };
    eng.chestReadyAtMs = () => eng.state.prog.chestTimerStartMs + ec.IDLE_CHEST_HOURS * 3600000;
    eng.chestReady = () => eng.nowMs() >= eng.chestReadyAtMs();
    eng.freePullsRemaining = () => { const c = eng.state.cur; return c.freePullsDaily + c.freePullsBanked; };
    eng.isBossStage = () => eng.state.stage.stage % cb.BOSS_EVERY === 0;

    eng.advance = function (n) { n = n | 0; if (n > 0) { host.Advance(n); touch(); } };
    eng.drain = function () {
      if (!pendingEvents) return [];
      pendingEvents = false;
      const s = host.Drain();
      return s && s !== '[]' ? JSON.parse(s) : [];
    };
    eng.exportSave = () => host.Export();
    eng.version = () => host.Version();
    eng.reload = function (opts) {
      opts = opts || {};
      let snapshot = null;
      if (cache || !dirty) { try { snapshot = host.Export(); } catch (e) { /* none yet */ } }
      const d = fillDefaults(opts);
      try { host.Init(contentJson, opts.saveString || '', d.profileSeedHex, d.nowMs, d.profileId); }
      catch (err) {
        if (snapshot) { try { host.Init(contentJson, snapshot, '0', d.nowMs, d.profileId); } catch (e2) { /* nothing more to do */ } }
        touch(); throw err;
      }
      pureMemo = new Map(); buildMemo = new Map(); buildKey = '';
      if (emit) host.SetEmitHits(1);
      touch();
    };
    eng.reload(o);
    return eng;
  }

  // ------------------------------------------------------------------ loading the .NET bundle
  function withTimeout(p, ms, what) {
    let t;
    return Promise.race([p, new Promise((_, rej) => { t = setTimeout(() => rej(new Error(what + ' timed out')), ms); })]).finally(() => clearTimeout(t));
  }
  async function bundlePresent(base) {
    if (typeof fetch !== 'function') return false;
    if (root.location && root.location.protocol === 'file:') return false;     // wasm cannot load from file://
    try { const r = await fetch(base + '_framework/dotnet.js', { method: 'HEAD' }); return !!r.ok; } catch (e) { return false; }
  }
  // Default loader: resolves {engine, contentJson} where engine is the IdleRng.Web.Engine export object.
  async function defaultLoadWasm(o) {
    const base = o.wasmBase || WASM_BASE;
    const abs = (p) => (typeof document !== 'undefined' ? new URL(p, document.baseURI).href : p);
    if (!(await bundlePresent(base))) throw new Error('wasm bundle not found at ' + base);
    const run = async () => {
      const { dotnet } = await import(abs(base + '_framework/dotnet.js'));
      const runtime = await dotnet.withDiagnosticTracing(false).create();
      const exports = await runtime.getAssemblyExports('IdleRng.Web.dll');
      const engine = exports.IdleRng.Web.Engine;
      let contentJson = null;
      try { const r = await fetch(base + 'content.json'); if (r.ok) contentJson = await r.text(); } catch (e) { /* use the JS content */ }
      return { engine: engine, contentJson: contentJson };
    };
    return withTimeout(run(), o.timeoutMs || 30000, 'wasm load');
  }

  // ------------------------------------------------------------------ selection
  function isArtifactOrigin(loc) {
    if (root.claude) return true;
    const h = (loc && loc.hostname) || '';
    return /(^|\.)(claude\.ai|claudeusercontent\.com|claude\.com)$/.test(h);
  }
  // -> {backend:'js'|'wasm'|'auto', explicit:boolean}
  function chooseBackend(env) {
    env = env || {};
    const loc = env.location || root.location;
    let q = null;
    try { q = new URLSearchParams((loc && loc.search) || '').get('engine'); } catch (e) { /* ignore */ }
    if (q === 'js' || q === 'wasm') return { backend: q, explicit: true };
    if (loc && isArtifactOrigin(loc)) return { backend: 'js', explicit: false };
    let stored = null;
    try { stored = (env.storage || root.localStorage).getItem('idle-rng-engine'); } catch (e) { /* ignore */ }
    if (stored === 'js') return { backend: 'js', explicit: true };
    if (stored === 'wasm') return { backend: 'wasm', explicit: true };
    return { backend: 'auto', explicit: false };
  }

  async function create(o) {
    o = o || {};
    if (!o.content) o.content = root.IdleContent;
    let want = o.backend || 'js';
    const notice = o.onNotice || function () {};
    const loadWasm = o.loadWasm || defaultLoadWasm;
    let eng = null, fell = null;
    if (want === 'wasm' || want === 'auto') {
      let loaded = null;
      try {
        if (want === 'auto' && !o.loadWasm && !(await bundlePresent(o.wasmBase || WASM_BASE))) throw Object.assign(new Error('no bundle'), { silent: true });
        loaded = await loadWasm(o);
        const contentJson = loaded.contentJson || JSON.stringify(o.content);
        try { eng = createWasmBackend(o, loaded.engine, contentJson); }
        catch (err) {
          if (!o.saveString) throw err;                                  // a broken host, not a broken save
          const e2 = createWasmBackend(Object.assign({}, o, { saveString: '' }), loaded.engine, contentJson);
          e2.saveError = err.message; eng = e2;
        }
      } catch (err) {
        fell = err;
        if (!err.silent) { try { notice(NOTICE_FALLBACK); } catch (e) { /* ignore */ } console.warn('wasm engine failed:', err && err.message || err); }
      }
    }
    if (!eng) {
      try { eng = createJsBackend(o); }
      catch (err) {
        if (!o.saveString) throw err;
        eng = createJsBackend(Object.assign({}, o, { saveString: '' })); eng.saveError = err.message;
      }
      if (fell) { eng.fellBack = true; eng.fallbackReason = fell.message; }
    }
    return eng;
  }

  // ------------------------------------------------------------------ save envelope (shared by app and tests)
  function wrapSave(eng, clockOffsetMs) {
    const w = { v: 1, clockOffsetMs: clockOffsetMs || 0, state: eng.exportSave() };
    if (eng.backend !== 'js') w.backend = eng.backend;                   // legacy JS saves carry no tag
    return JSON.stringify(w);
  }
  // -> {clockOffsetMs, saveString}; throws a clear message when the save belongs to the other backend.
  function unwrapSave(str, backend) {
    const w = JSON.parse(str);
    if (!w || typeof w !== 'object' || typeof w.state !== 'string') throw new Error('Not an Idle RNG save');
    const from = w.backend || 'js';
    if (from !== backend) {
      const nm = (b) => b === 'wasm' ? 'C# engine' : 'JS engine';
      throw new Error('This save is from the ' + nm(from) + ' and cannot be loaded by the ' + nm(backend) + '. Switch engine in Settings and import it there.');
    }
    return { clockOffsetMs: w.clockOffsetMs || 0, saveString: w.state };
  }

  const api = { create: create, chooseBackend: chooseBackend, wrapSave: wrapSave, unwrapSave: unwrapSave, SAVE_KEYS: SAVE_KEYS,
    QUERIES: QUERIES, COMMANDS: COMMANDS, NOTICE_FALLBACK: NOTICE_FALLBACK, defaultLoadWasm: defaultLoadWasm, buildHeroById: buildHeroById };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.IdleEngine = api;
})(typeof self !== 'undefined' ? self : this);
