// Node tests for web/engine.js. Run: node web/tools/engine-test.js
'use strict';
const assert = require('assert');
const fs = require('fs');
const path = require('path');
const IS = require('../sim.js');
const content = require('../content.js');
const IdleEngine = require('../engine.js');
const { mockLoader } = require('./engine-mock.js');

let failed = 0;
async function test(name, fn) {
  try { await fn(); console.log('ok   ' + name); } catch (e) { failed++; console.log('FAIL ' + name + '\n     ' + (e.stack || e).toString().split('\n').slice(0, 8).join('\n     ')); }
}

const SEED = 'c0ffee1234abcd', NOW = Date.UTC(2026, 0, 5, 12, 0, 0);
const opts = (extra) => Object.assign({ content: content, profileSeedHex: SEED, nowMs: NOW, profileId: 'test' }, extra);

// Direct sim.js adaptor with the facade's shape (the reference).
function direct() {
  const sim = IS.Sim.newProfile(content, { profileSeed: BigInt('0x' + SEED), nowMs: NOW, profileId: 'test' });
  const d = { state: sim.state, heroById: sim.heroById, advance: (n) => sim.advance(n), drain: () => sim.drain(),
    consolidatedRates: () => IS.consolidatedRates(content), exportSave: () => IS.serialize(sim.state) };
  for (const n of IdleEngine.QUERIES.concat(IdleEngine.COMMANDS)) if (!d[n]) d[n] = function () { return sim[n].apply(sim, arguments); };
  Object.defineProperty(d, 'emitHits', { set(v) { sim.emitHits = v; } });
  Object.defineProperty(d, 'state', { get() { return sim.state; } });
  return d;
}

const stripRng = (s) => JSON.parse(JSON.stringify(s, (k, v) => (k === 'rng' ? undefined : v)));
// A 20 minute scripted session (12 000 ticks at 10 Hz). Returns a log of every observable result.
function session(e, normalize) {
  const N = normalize || ((x) => x), log = [];
  const rec = (tag, v) => log.push([tag, N(v)]);
  const queries = (st) => {
    rec('q', [e.hp(st), e.bossHp(st), e.gold(st), e.statCost(st), e.statMult(), e.statMult(st), e.gearTier(st), e.gearMult(), e.heroMult(),
      e.classMult(), e.classMult(2), e.dps(), e.hitDamage(), e.hitsToKill(e.hp(st)), e.killTimeSeconds(st), e.isBossStage(), e.bossClearable(),
      e.needDps(), e.enhanceCost(), e.enhanceSuccess(3), e.pityRate(60), e.freePullsRemaining(), e.chestReadyAtMs(), e.chestReady(),
      e.bulkCost(5, 7), e.maxAffordable(), e.maxAffordable(1e9), e.pullPrice(1), e.pullPrice(10), e.bestParty(), e.autoEquipWouldChange(),
      e.nowMs(), e.validate(), e.consolidatedRates(), e.heroLevel(7)]);
    const piece = { tier: 20, rarity: 2, enh: 3 };
    rec('qp', [e.gearBasePct(piece), e.gearEffPct(piece), e.gearBasePct(null)]);
    for (let i = 0; i < 6; i++) rec('slot', [e.gearSlotMult(i), e.gearBasePct(e.state.gear[i]), e.gearEffPct(e.state.gear[i])]);
    for (const id of Object.keys(e.state.heroes.owned).sort().slice(0, 4)) rec('hero', [id, e.heroPct(id), e.heroById[id]]);
  };
  e.emitHits = true;
  rec('heroById', e.heroById);
  rec('state0', stripRng(e.state));
  for (let i = 0; i < 40; i++) {                       // 40 x 30 s = 20 min
    e.advance(300);
    rec('events', e.drain());
    if (i === 0) rec('pull10', e.pull(10));
    if (i % 3 === 0) rec('statMax', e.buyStatLevelsMax());
    if (i % 4 === 1) rec('autoEquip', e.autoEquip());
    if (i % 5 === 2) rec('pull', e.pull(i % 2 ? 1 : 10));
    if (i % 2 === 0) rec('enhance', e.enhance(i % 6));
    if (i % 7 === 3) rec('buy', e.buyStatLevels(3));
    if (i === 5) rec('grantGold', [e.debugGrantGold(1e6), e.debugGrantGems(5000)]);
    if (i === 6) { const id = Object.keys(e.state.heroes.owned).sort()[0]; if (id) { rec('unequip', e.unequipHero(0)); rec('equip', e.equipHero(0, id)); } }
    if (i === 10) rec('sync+3h', e.syncWallClock(NOW + 3 * 3600000 + i * 30000));
    if (i === 14) rec('sync-back', e.syncWallClock(NOW - 5000));
    if (i === 18) rec('stage', e.debugSetStage(95));
    if (i === 22) rec('claim', e.claimChest());
    if (i === 24) rec('sync+30h', e.syncWallClock(NOW + 30 * 3600000));
    if (e.state.stage.boss === 'walled') rec('boss', e.attemptBoss());
    rec('events2', e.drain());
    rec('state', stripRng(e.state));
    queries(e.state.stage.stage);
  }
  rec('badPull', e.pull(7));
  rec('export', typeof e.exportSave());
  return log;
}
function diff(a, b) {
  assert.strictEqual(a.length, b.length, 'log length');
  for (let i = 0; i < a.length; i++) {
    try { assert.deepStrictEqual(b[i], a[i]); } catch (err) { throw new Error('mismatch at log step ' + i + ' (' + a[i][0] + ')\n' + err.message.slice(0, 600)); }
  }
}

(async () => {
  const sessionJs = [];
  await test('(a) JS backend == direct sim.js over a 20 minute session', async () => {
    const e = await IdleEngine.create(opts({ backend: 'js' }));
    assert.strictEqual(e.backend, 'js');
    const a = session(direct()), b = session(e);
    sessionJs.push(...b);
    diff(a, b);
    assert.ok(a.some((x) => x[0] === 'events' && x[1].length), 'session produced events');
  });

  await test('(b) mock-wasm backend == JS backend (state, drain, queries, commands)', async () => {
    const out = {};
    const w = await IdleEngine.create(opts({ backend: 'wasm', loadWasm: mockLoader(out) }));
    assert.strictEqual(w.backend, 'wasm');
    assert.ok(!w.fellBack);
    const bw = session(w);
    // JSON normalisation of the JS side models what the bridge does (undefined fields vanish, -0 -> 0 never arises).
    const n = (x) => (x === undefined ? null : JSON.parse(JSON.stringify(x)));
    const js = session(await IdleEngine.create(opts({ backend: 'js' })), n);
    diff(js, bw.map((x) => [x[0], n(x[1])]));
    assert.ok(/MOCK/.test(w.version()));
    assert.strictEqual(w.heroById.C01 === undefined, w.heroById.C01 === undefined);
    const c = out.host.calls;
    console.log('     bridge calls over session:', JSON.stringify(c), 'cache hits', w.stats.hits, 'queries', w.stats.queries);
  });

  await test('(b2) strict (non-normalised) comparison: JSON round trip must not change results', async () => {
    const w = await IdleEngine.create(opts({ backend: 'wasm', loadWasm: mockLoader() }));
    const js = await IdleEngine.create(opts({ backend: 'js' }));
    // Only difference tolerated: object properties holding undefined are absent on the wasm side (JSON has no undefined).
    const strip = (x) => Array.isArray(x) ? x.map(strip) : x && typeof x === 'object'
      ? Object.fromEntries(Object.entries(x).filter(([, v]) => v !== undefined).map(([k, v]) => [k, strip(v)])) : x;
    diff(session(js, strip), session(w, strip));
  });

  await test('(b3) wasm hot path: <= 3 bridge queries and 1 State per frame', async () => {
    const out = {};
    const w = await IdleEngine.create(opts({ backend: 'wasm', loadWasm: mockLoader(out) }));
    w.advance(500); w.drain();
    const run = (frames) => {
      const c0 = Object.assign({}, out.host.calls);
      for (let f = 0; f < frames; f++) {            // what frame()+drawStrip() read per frame
        w.advance(2); w.drain();
        const s = w.state; void s.cur.gold; void s.stage.stage;
        w.isBossStage(); w.dps(); w.hitsToKill(w.hp(s.stage.stage)); w.killTimeSeconds(s.stage.stage); w.hitDamage();
        if (s.stage.boss === 'walled') w.bossClearable();
      }
      const c1 = out.host.calls;
      return { q: (c1.Query - c0.Query) / frames, st: (c1.State - c0.State) / frames, adv: (c1.Advance - c0.Advance) / frames };
    };
    const r = run(100);
    assert.ok(r.st <= 1 && r.adv <= 1, JSON.stringify(r));
    assert.ok(r.q <= 3, 'queries per frame ' + r.q);
    console.log('     per frame:', JSON.stringify(r));
  });

  await test('(b4) wasm engine reload / export round trip and reload keeps emitHits', async () => {
    const w = await IdleEngine.create(opts({ backend: 'wasm', loadWasm: mockLoader() }));
    w.emitHits = true; w.advance(400); w.drain();
    const save = w.exportSave();
    const w2 = await IdleEngine.create(opts({ backend: 'wasm', loadWasm: mockLoader(), saveString: save }));
    assert.deepStrictEqual(w2.state, w.state);
    w.advance(100); w2.advance(100);
    assert.deepStrictEqual(w2.state, w.state);
    w.reload({ profileSeedHex: '1', nowMs: NOW, profileId: 'again' });
    assert.strictEqual(w.state.tick, 0);
    assert.strictEqual(w.state.profileId, 'again');
    assert.throws(() => w.reload({ saveString: 'garbage' }));
    assert.strictEqual(w.state.profileId, 'again', 'failed import leaves the game intact');
  });

  await test('(c) saves keyed per backend; cross-import rejected', async () => {
    assert.notStrictEqual(IdleEngine.SAVE_KEYS.js, IdleEngine.SAVE_KEYS.wasm);
    assert.strictEqual(IdleEngine.SAVE_KEYS.js, 'idle-rng-web-save-v1');
    assert.strictEqual(IdleEngine.SAVE_KEYS.wasm, 'idle-rng-wasm-save-v1');
    const js = await IdleEngine.create(opts({ backend: 'js' }));
    const w = await IdleEngine.create(opts({ backend: 'wasm', loadWasm: mockLoader() }));
    assert.strictEqual(js.saveKey, IdleEngine.SAVE_KEYS.js);
    assert.strictEqual(w.saveKey, IdleEngine.SAVE_KEYS.wasm);
    js.advance(100); w.advance(100);
    const sj = IdleEngine.wrapSave(js, 123), sw = IdleEngine.wrapSave(w, 456);
    assert.strictEqual(JSON.parse(sj).backend, undefined, 'JS save envelope unchanged (legacy compatible)');
    assert.strictEqual(JSON.parse(sw).backend, 'wasm');
    assert.throws(() => IdleEngine.unwrapSave(sw, 'js'), /C# engine.*JS engine/);
    assert.throws(() => IdleEngine.unwrapSave(sj, 'wasm'), /JS engine.*C# engine/);
    const u = IdleEngine.unwrapSave(sj, 'js');
    assert.strictEqual(u.clockOffsetMs, 123);
    const js2 = await IdleEngine.create(opts({ backend: 'js', saveString: u.saveString }));
    assert.deepStrictEqual(js2.state, js.state);
    const legacy = JSON.stringify({ v: 1, clockOffsetMs: 0, state: IS.serialize(js.state) });
    assert.strictEqual(IdleEngine.unwrapSave(legacy, 'js').clockOffsetMs, 0);
    // app.js must use the engine's key, not a literal.
    const app = fs.readFileSync(path.join(__dirname, '..', 'app.js'), 'utf8');
    assert.ok(/engine\.saveKey|sim\.saveKey/.test(app) && !/SAVE_KEY\s*=\s*'idle/.test(app));
  });

  await test('fallback: explicit wasm failure -> JS engine + notice; auto without bundle is silent', async () => {
    const notes = [];
    const e = await IdleEngine.create(opts({ backend: 'wasm', loadWasm: async () => { throw new Error('404'); }, onNotice: (m) => notes.push(m) }));
    assert.strictEqual(e.backend, 'js'); assert.ok(e.fellBack);
    assert.deepStrictEqual(notes, ['C# engine unavailable, using JS engine']);
    const e2 = await IdleEngine.create(opts({ backend: 'wasm', loadWasm: async () => ({ engine: { Init() { throw new Error('boom'); } } }), onNotice: (m) => notes.push(m) }));
    assert.strictEqual(e2.backend, 'js'); assert.strictEqual(notes.length, 2);
    const notes2 = [];
    const e3 = await IdleEngine.create(opts({ backend: 'auto', onNotice: (m) => notes2.push(m) }));   // node: no fetch of a bundle
    assert.strictEqual(e3.backend, 'js'); assert.strictEqual(notes2.length, 0);
  });

  await test('bad save: engine starts a new profile and reports saveError', async () => {
    const e = await IdleEngine.create(opts({ backend: 'js', saveString: '{nope' }));
    assert.ok(e.saveError); assert.strictEqual(e.state.tick, 0);
    const w = await IdleEngine.create(opts({ backend: 'wasm', loadWasm: mockLoader(), saveString: '{nope' }));
    assert.ok(w.saveError); assert.strictEqual(w.backend, 'wasm');
  });

  await test('chooseBackend', async () => {
    const st = (v) => ({ getItem: () => v });
    assert.deepStrictEqual(IdleEngine.chooseBackend({ location: { search: '?engine=js', hostname: 'x' }, storage: st('wasm') }), { backend: 'js', explicit: true });
    assert.deepStrictEqual(IdleEngine.chooseBackend({ location: { search: '?engine=wasm', hostname: 'x' }, storage: st(null) }), { backend: 'wasm', explicit: true });
    assert.deepStrictEqual(IdleEngine.chooseBackend({ location: { search: '', hostname: 'x' }, storage: st('wasm') }), { backend: 'wasm', explicit: true });
    assert.deepStrictEqual(IdleEngine.chooseBackend({ location: { search: '', hostname: 'x' }, storage: st(null) }), { backend: 'auto', explicit: false });
    assert.strictEqual(IdleEngine.chooseBackend({ location: { search: '', hostname: 'a.claudeusercontent.com' }, storage: st('wasm') }).backend, 'js');
  });

  console.log(failed ? '\n' + failed + ' FAILED' : '\nall engine tests passed');
  process.exit(failed ? 1 : 0);
})();
