// Checks the browser core against the test vectors published in the specs. Run: node web/test.js
'use strict';
const assert = require('assert');
const content = require('./content.js');
const S = require('./sim.js');

let passed = 0;
function test(name, fn) {
  try { fn(); passed++; console.log('ok   ' + name); }
  catch (e) { console.log('FAIL ' + name + '\n     ' + e.message); process.exitCode = 1; }
}
const close = (a, b, tol, msg) => assert.ok(Math.abs(a - b) <= tol, (msg || '') + ` got ${a}, want ${b}`);

// specs/03 section 5.3
test('SplitMix64 derive vectors', () => {
  const v = [
    [0x0n, 0, 0xE220A8397B1DCDAFn, 0.88331080821364261],
    [0x0n, 1, 0x6E789E6AA1B965F4n, 0.43152799704850997],
    [0x0n, 2, 0x06C45D188009454Fn, 0.026433771592597743],
    [0x0n, 1000, 0x2CFA2F23425329E1n, 0.17569250689332405],
    [0x7n, 0, 0x63CBE1E459320DD7n, 0.38982974839127149],
    [0x7n, 1, 0x044C3CD7F43C661Cn, 0.016788294528156111],
    [0x7n, 2, 0xE6984080BAB12A02n, 0.90076068060688341],
    [0x7n, 1000, 0xCF1B8D545D1615CAn, 0.80901416120203629],
    [0x123456789ABCDEF0n, 0, 0x161922C645CE50E8n, 0.08632104244844041],
    [0x123456789ABCDEF0n, 1, 0xAD760CAFA1697B60n, 0.67758254325727829],
  ];
  for (const [seed, k, out, unit] of v) {
    assert.strictEqual(S.rng.derive(seed, k), out, `derive(${seed},${k})`);
    assert.strictEqual(S.rng.toUnit(out), unit, `toUnit(${out})`);
  }
});

test('FNV-1a and stream seed vectors', () => {
  const v = {
    combat: [0xCA0BF97CCE717B21n, 0xD638BA460341F349n],
    gear_drop: [0xC1A5A0EE94A0A3BAn, 0xFB1D7F3436BC41A4n],
    gear_rarity: [0x6AB86776709CD868n, 0x9A1426458CA81741n],
    enhance: [0xDC465426329C1EE9n, 0x84C074F86395F937n],
    gacha_hero: [0x8C00A1F67FD04D96n, 0x9BB72FE2750126ACn],
  };
  for (const name in v) {
    assert.strictEqual(S.rng.fnv1a64(name), v[name][0], 'fnv ' + name);
    assert.strictEqual(S.rng.streamSeed(7n, name), v[name][1], 'streamSeed ' + name);
  }
});

// specs/01 section 9.2
test('pity ramp table', () => {
  const want = { 1: 0.03, 50: 0.03, 51: 0.0625, 55: 0.1925, 60: 0.355, 65: 0.5175, 70: 0.68, 75: 0.8425, 79: 0.9725, 80: 1, 81: 1 };
  for (const n in want) close(S.pityRate(content.banner, +n), want[n], 1e-12, 'n=' + n);
  for (let n = 1; n < 100; n++) assert.ok(S.pityRate(content.banner, n + 1) >= S.pityRate(content.banner, n));
});

// specs/01 section 3.2
test('consolidated rates', () => {
  const r = S.consolidatedRates(content);
  close(r.expectedPulls, 27.35, 0.005);
  close(r.lplus * 100, 3.6567, 0.0001);
  close(r.m * 100, 0.6094, 0.0001);
});

// specs/01 section 9.10
test('dupe levels', () => {
  const want = [[1, 1], [2, 1], [3, 2], [6, 3], [10, 4], [160, 10], [161, 10]];
  for (const [c, l] of want) assert.strictEqual(S.heroLevel(content, c), l, 'copies ' + c);
});

function fresh(seed) {
  return S.Sim.newProfile(content, { profileSeed: seed === undefined ? 7 : seed, nowMs: Date.UTC(2026, 0, 1) });
}

test('tutorial and first-day grants', () => {
  const sim = fresh();
  // 1000 tutorial + 50 login
  assert.strictEqual(sim.state.cur.gems, 1050);
  assert.strictEqual(sim.state.cur.freePullsBanked, 10);
  assert.strictEqual(sim.state.cur.freePullsDaily, 3);
});

// specs/01 section 9.5 and 9.6: script the gacha stream's draws (counter still advances).
function scripted(sim, values) {
  const orig = sim._draw;
  sim._draw = function (stream) {
    if (stream !== this.state.rng.streams.gacha_hero) return orig.call(this, stream);
    stream.counter++;
    return values.shift();
  };
}
test('10-pull guarantee upgrades slot 10 when slots 1-9 are below Epic', () => {
  const sim = fresh();
  const script = [];
  for (let i = 0; i < 10; i++) script.push(0.99, 0.0, 0.0);   // no L+, Common, first hero
  scripted(sim, script);
  const out = sim.pull(10);
  assert.ok(out.ok);
  for (let i = 0; i < 9; i++) assert.strictEqual(out.results[i].tier, 0, 'slot ' + (i + 1));
  assert.strictEqual(out.results[9].tier, 2, 'slot 10 forced Epic');
});
test('10-pull does not force when an Epic already landed', () => {
  const sim = fresh();
  const script = [];
  for (let i = 0; i < 10; i++) script.push(0.99, i === 2 ? 0.95 : 0.0, 0.0);
  scripted(sim, script);
  const out = sim.pull(10);
  assert.strictEqual(out.results[2].tier, 2);
  assert.strictEqual(out.results[9].tier, 0, 'slot 10 rolls normally');
});
test('hard pity: u1 = 0.999 still gives L+ on pull 80 and resets the counter', () => {
  const sim = fresh();
  sim.state.banner.pity = 79;
  scripted(sim, [0.999, 0.5, 0.0]);
  const out = sim.pull(1);
  assert.strictEqual(out.results[0].tier, 3);
  assert.strictEqual(sim.state.banner.pity, 0);
});

test('draw count: 1000 pulls advance the gacha stream by 3000', () => {
  const sim = fresh();
  sim.state.cur.gems = 1e9;
  for (let i = 0; i < 100; i++) sim.pull(10);
  assert.strictEqual(sim.state.rng.streams.gacha_hero.counter, 3000 + 0);
});

test('reproducibility: same seed, same 1000 pulls; save round-trip continues identically', () => {
  const a = fresh(42), b = fresh(42);
  a.state.cur.gems = b.state.cur.gems = 1e9;
  const ra = [], rb = [];
  for (let i = 0; i < 100; i++) { ra.push(...a.pull(10).results.map(r => r.id)); rb.push(...b.pull(10).results.map(r => r.id)); }
  assert.deepStrictEqual(ra, rb);
  const c = S.deserialize(content, S.serialize(a.state));
  assert.deepStrictEqual(c.pull(10).results.map(r => r.id), a.pull(10).results.map(r => r.id));
});

test('consolidated L+ rate from the real generator (Monte Carlo, fixed seed)', () => {
  const sim = fresh(1234);
  sim.state.cur.gems = 1e12;
  const N = 200000;
  for (let i = 0; i < N / 10; i++) sim.pull(10);
  const rate = sim.state.life.lplus / sim.state.banner.totalPulls;
  // 10-pull guarantee forces Epic, not L+, so L+ frequency should still match 3.657% closely.
  close(rate * 100, 3.657, 0.15, 'L+ %');
});

test('wall condition matches the formula exactly', () => {
  const sim = fresh();
  sim.debugSetStage(10);
  const cb = content.combat;
  const bossHp = sim.bossHp(10);
  const hits = Math.ceil(bossHp / sim.hitDamage());
  assert.strictEqual(sim.bossClearable(), hits * cb.ATTACK_INTERVAL_TICKS <= cb.BOSS_TIME_TICKS);
});

test('combat: one stage takes kills * (hits*10 + 5) ticks at stage 1', () => {
  const sim = fresh();
  const per = sim.hitsToKill(sim.hp(1)) * 10 + 5;
  sim.advance(per * 10);
  assert.strictEqual(sim.state.stage.stage, 2);
  assert.strictEqual(sim.state.life.kills, 10);
});

test('boss fail walls the stage, retry is accepted, stream invariants hold', () => {
  const sim = fresh();
  sim.debugSetStage(60);           // far too strong a boss for a fresh profile
  sim.advance(20000);
  assert.strictEqual(sim.state.stage.boss, 'walled');
  assert.strictEqual(sim.state.stage.stage, 60);
  assert.ok(sim.attemptBoss().ok);
  assert.strictEqual(sim.attemptBoss().reason, 'bossAlreadyPending');
  assert.deepStrictEqual(sim.validate(), []);
});

test('offline catch-up: capped at 8 h, half rate', () => {
  const sim = fresh();
  const g0 = sim.state.cur.gold;
  const now = sim.nowMs();
  sim.syncWallClock(now + 24 * 3600 * 1000);
  const want = 8 * 3600 * 0.5 * sim.gold(1) / sim.killTimeSeconds(1);
  close(sim.state.cur.gold - g0, want, 1e-6);
});

test('day rollover grants login once, never on a clock that went backwards', () => {
  const sim = fresh();
  const gems = sim.state.cur.gems;
  const day = 86400000;
  sim.syncWallClock(sim.nowMs() + day);
  assert.strictEqual(sim.state.cur.gems, gems + 50);
  sim.syncWallClock(sim.nowMs() - day);
  sim.syncWallClock(sim.nowMs() + 1000);
  assert.strictEqual(sim.state.cur.gems, gems + 50);
});

test('stat purchase: max affordable equals the bulk loop', () => {
  const sim = fresh();
  sim.state.cur.gold = 12345;
  const m = sim.maxAffordable();
  assert.ok(sim.bulkCost(0, m) <= 12345 && sim.bulkCost(0, m + 1) > 12345);
  sim.buyStatLevelsMax();
  assert.strictEqual(sim.state.statLevel, m);
});

test('big number formatter', () => {
  const f = S.formatShort;
  assert.strictEqual(f(999), '999');
  assert.strictEqual(f(1234), '1.23K');
  assert.strictEqual(f(45.6e6), '45.6M');
  assert.strictEqual(f(1.23e17), '123aa');
  assert.strictEqual(f(2.9e18), '2.90ab');
});

console.log(`\n${passed} passed${process.exitCode ? ', some FAILED' : ''}`);
