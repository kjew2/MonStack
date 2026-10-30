#!/usr/bin/env node
// Generates Fixtures/Parity/*.json by running scripted scenarios on the JS reference core (web/sim.js).
// Deterministic: no wall clock, no Math.random. Usage: node web/tools/gen-parity.js [outDir]
'use strict';
const fs = require('fs');
const path = require('path');
const content = require('../content.js');
const lib = require('./parity-lib.js');
const { formatShort } = lib.simApi;

const OUT = process.argv[2] || path.join(__dirname, '..', '..', 'Fixtures', 'Parity');
const T0 = Date.UTC(2026, 0, 1);
const HOUR = 3600000, DAY = 86400000;
const TPM = 60 * content.combat.TICK_HZ; // ticks per minute

// Records steps while executing them on a live JS sim.
function Rec(name, seedHex, nowMs, emitHits) {
  this.fx = { name: name, content: 'Content/content.json', seedHex: seedHex, nowMs: nowMs, emitHits: !!emitHits, steps: [], expect: [] };
  this.sim = lib.create(this.fx, content);
}
Rec.prototype._do = function (step) {
  this.fx.steps.push(step);
  const r = lib.exec(this.sim, content, step);
  if (r.has) this.fx.expect.push(r.value);
  return r.has ? r.value : undefined;
};
Rec.prototype.advance = function (n) { this._do({ op: 'advance', n: n }); };
Rec.prototype.sync = function (ms) { this._do({ op: 'sync', nowMs: ms }); };
Rec.prototype.state = function () { return this._do({ op: 'state' }); };
Rec.prototype.drain = function (check) { return this._do(check === false ? { op: 'drain', check: false } : { op: 'drain' }); };
Rec.prototype.q = function (name) { return this._do({ op: 'query', name: name, args: Array.prototype.slice.call(arguments, 1) }); };
Rec.prototype.cmd = function (name) { return this._do({ op: 'cmd', name: name, args: Array.prototype.slice.call(arguments, 1) }); };
Object.defineProperty(Rec.prototype, 'S', { get: function () { return this.sim.state; } });

// Calls every query at least once; `big` selects larger arguments. Optional args are omitted, never null.
function battery(r, big) {
  const st = big ? [100, 500, 1000, 5000, 9000] : [1, 2, 10, 50, 100];
  for (const s of st) { r.q('hp', s); r.q('bossHp', s); r.q('gold', s); r.q('killTimeSeconds', s); r.q('gearTier', s); }
  r.q('hp', 0);
  for (const n of (big ? [200, 1000, 4000] : [0, 1, 10, 100])) { r.q('statCost', n); r.q('statMult', n); }
  r.q('statMult');
  const pieces = [null, { tier: 0, rarity: 0, enh: 0 }, { tier: 10, rarity: 1, enh: 3 }, { tier: 50, rarity: 2, enh: 7 },
    { tier: 120, rarity: 3, enh: 15 }, { tier: big ? 3000 : 90, rarity: 4, enh: 10 }];
  for (const p of pieces) { r.q('gearBasePct', p); r.q('gearEffPct', p); }
  for (let i = 0; i < 6; i++) r.q('gearSlotMult', i);
  r.q('gearMult');
  for (const c of [0, 1, 2, 3, 5, 6, 9, 10, 15, 16, 24, 25, 39, 40, 63, 64, 99, 100, 159, 160, 1000]) r.q('heroLevel', c);
  const owned = Object.keys(r.S.heroes.owned);
  for (const id of owned.slice(0, 8).concat(['C01', 'M03', 'Z99'])) r.q('heroPct', id);
  r.q('heroMult');
  r.q('classMult'); for (const k of [0, 1, 2, 5, 10]) r.q('classMult', k);
  r.q('dps'); r.q('hitDamage');
  for (const hp of [0, 1, 9.5, 10, 11, 1234.5, 1e6, 1e30]) r.q('hitsToKill', hp);
  r.q('isBossStage'); r.q('bossClearable'); r.q('needDps'); r.q('enhanceCost');
  for (const l of [0, 1, 5, 10, 13, 14, 15, 20]) r.q('enhanceSuccess', l);
  for (const n of [0, 1, 49, 50, 51, 60, 70, 79, 80, 81, 200]) r.q('pityRate', n);
  r.q('freePullsRemaining'); r.q('chestReadyAtMs'); r.q('chestReady'); r.q('nowMs');
  for (const a of [[0, 0], [0, 1], [0, 10], [5, 100], [37, 3], [big ? 2000 : 12, 25]]) r.q('bulkCost', a[0], a[1]);
  r.q('maxAffordable');
  for (const g of [0, 49, 50, 124.99, 5000, 1e9, 1e30]) r.q('maxAffordable', g);
  for (const c of [1, 2, 10]) r.q('pullPrice', c);
  r.q('bestParty'); r.q('autoEquipWouldChange'); r.q('validate'); r.q('consolidatedRates');
}

// Fund and advance helper: keeps a run moving through early stages without touching internals.
function grow(r, goldGrant) {
  if (goldGrant) r.cmd('debugGrantGold', goldGrant);
  r.cmd('buyStatLevelsMax');
}

const scenarios = [];
function scenario(name, fn) { scenarios.push({ name: name, fn: fn }); }

// ---------------------------------------------------------------- 1
scenario('first-minute', function () {
  const r = new Rec('first-minute', '1a2b3c', T0, true);
  r.state();
  battery(r, false);
  for (let i = 0; i < 6; i++) { r.advance(100); r.drain(); r.state(); }
  r.cmd('buyStatLevels', 1);
  r.advance(1); r.drain();
  r.q('statCost', 1); r.q('maxAffordable'); r.q('dps'); r.q('hitDamage');
  return r;
});

// ---------------------------------------------------------------- 2
function buildFights(seedHex) {
  const r = new Rec('fights', seedHex, T0 + 9 * HOUR, false);
  r.state();
  for (let i = 0; i < 10; i++) {
    r.advance(1000);
    r.drain(); r.state();
    grow(r, 3000 * (i + 1));
    if (r.S.stage.boss === 'walled' && r.sim.bossClearable()) r.cmd('attemptBoss');
    if (i === 4) battery(r, false);
  }
  battery(r, true);
  return r;
}
scenario('fights', function () {
  let best = null;
  for (let s = 1; s <= 400 && !best; s++) {
    const hex = s.toString(16);
    const r = buildFights(hex);
    if (r.S.life.gearDrops >= 4 && r.S.life.bossKills >= 1) best = hex;
  }
  if (!best) throw new Error('no seed with gear drops');
  return buildFights(best);
});

// ---------------------------------------------------------------- 3
scenario('boss-fail-retry', function () {
  const r = new Rec('boss-fail-retry', 'b055', T0 + 3 * HOUR, false);
  r.cmd('attemptBoss');                       // noBossToAttempt (stage 1)
  r.cmd('debugSetStage', 10);
  r.state();
  r.q('needDps'); r.q('bossClearable'); r.q('isBossStage');
  let guard = 0;
  while (r.S.stage.boss !== 'pending' && guard++ < 60) r.advance(50);
  r.drain();
  while (r.S.stage.boss === 'pending' && guard++ < 200) r.advance(50);   // boss spawns, timer runs out
  if (r.S.stage.boss !== 'walled') throw new Error('expected a wall');
  r.drain(); r.state();
  r.cmd('attemptBoss');                       // pending
  r.cmd('attemptBoss');                       // bossAlreadyPending
  r.advance(300); r.drain(); r.state();       // fails again
  r.q('bossClearable'); r.q('needDps'); r.q('hitsToKill', r.sim.bossHp(10));
  // wall farming: no kill credit while walled
  for (let i = 0; i < 6; i++) { r.advance(400); r.q('bossClearable'); r.cmd('buyStatLevelsMax'); }
  r.drain(); r.state();
  // set up a clearable boss with gold, then retry
  r.cmd('debugGrantGold', 5000);
  r.cmd('buyStatLevelsMax');
  r.q('bossClearable'); r.q('needDps'); r.q('dps');
  r.cmd('attemptBoss');
  r.advance(300); r.drain(); r.state();
  // next wall at stage 20
  r.cmd('debugSetStage', 20);
  guard = 0;
  while (r.S.stage.boss !== 'walled' && guard++ < 400) r.advance(50);
  r.drain(); r.state();
  battery(r, false);
  guard = 0;
  while (guard++ < 60) {
    r.advance(500);
    r.cmd('buyStatLevelsMax');
    if (r.S.stage.boss === 'walled' && r.sim.bossClearable()) { r.cmd('attemptBoss'); r.advance(260); }
    if (r.S.stage.stage > 20) break;
  }
  r.drain(); r.state();
  r.q('bossClearable'); r.q('needDps'); r.q('killTimeSeconds', r.S.stage.stage);
  return r;
});

// ---------------------------------------------------------------- 4
scenario('economy-commands', function () {
  const r = new Rec('economy-commands', 'ec01', T0 + 2 * HOUR, false);
  r.state();
  r.cmd('buyStatLevels', 1);                  // notEnoughGold
  r.cmd('buyStatLevels', 0);                  // invalid
  r.cmd('debugGrantGold', 400);
  r.cmd('buyStatLevels', 1);
  r.cmd('buyStatLevels', 10);
  r.cmd('buyStatLevelsMax');
  r.q('bulkCost', 0, 10); r.q('maxAffordable'); r.q('statCost', r.S.statLevel);
  // pulls: pull(10) with the banked tutorial pulls, then daily free pull(1), then banked, then gems
  r.q('pullPrice', 10); r.q('freePullsRemaining');
  r.cmd('pull', 10); r.state();
  r.q('pullPrice', 1); r.q('freePullsRemaining');
  for (let i = 0; i < 3; i++) r.cmd('pull', 1);       // freeDaily x3
  r.q('pullPrice', 1);
  r.cmd('pull', 1);                                   // gems (banked now empty)
  r.cmd('pull', 10);                                  // cannotAfford (gems 900 < 1000)
  r.cmd('debugGrantGems', 1100);
  r.cmd('pull', 10);                                  // gems x10
  r.cmd('pull', 1);
  r.state();
  // heroes
  r.q('bestParty'); r.q('autoEquipWouldChange');
  const ids = r.sim.bestParty();
  r.cmd('equipHero', 0, ids[0]);
  r.cmd('equipHero', 1, ids[0]);                      // heroAlreadyEquipped
  r.cmd('equipHero', 0, 'M03');                       // heroNotOwned (almost surely)
  r.cmd('equipHero', 2, ids[1]);
  r.q('heroMult'); r.q('dps'); r.q('autoEquipWouldChange');
  r.cmd('unequipHero', 0);
  r.q('autoEquipWouldChange');
  r.cmd('autoEquip');
  r.cmd('autoEquip');
  r.state();
  r.cmd('unequipHero', 5);
  // gear: play until two slots are filled
  let guard = 0;
  while (r.S.gear.filter(Boolean).length < 2 && guard++ < 300) { r.cmd('debugGrantGold', 200); r.cmd('buyStatLevelsMax'); r.advance(500); }
  if (r.S.gear.filter(Boolean).length < 2) throw new Error('no gear');
  r.drain(); r.state();
  r.cmd('enhance', r.S.gear.findIndex((g) => !g) ); // emptySlot
  const full = r.S.gear.map((g, i) => (g ? i : -1)).filter((i) => i >= 0);
  r.cmd('debugGrantGold', 1e6);
  r.q('enhanceCost');
  guard = 0;
  while (r.S.gear[full[0]].enh < content.gear.ENH_MAX && guard++ < 300) { r.cmd('enhance', full[0]); if (guard % 20 === 0) r.cmd('debugGrantGold', 1e6); }
  r.cmd('enhance', full[0]);                          // maxed
  for (let i = 0; i < 12; i++) r.cmd('enhance', full[1]);
  r.state();
  battery(r, false);
  // chest: not ready, then ready after >4 h
  r.cmd('claimChest');
  r.sync(T0 + 2 * HOUR + Math.round(r.sim.nowMs() - r.fx.nowMs - 2 * HOUR) + 5 * HOUR);
  r.q('chestReady'); r.q('chestReadyAtMs');
  r.cmd('claimChest');
  r.cmd('claimChest');                                // notReady again
  r.drain(); r.state();
  // gold-starved enhance at a high stage
  r.cmd('debugSetStage', 500);
  r.cmd('enhance', full[1]);
  r.q('enhanceCost'); r.q('classMult');
  battery(r, true);
  r.state();
  return r;
});

// ---------------------------------------------------------------- 5
function buildPity(seedHex) {
  const r = new Rec('hard-pity', seedHex, T0 + 5 * HOUR, false);
  let above50 = 0, maxPity = 0;
  for (let i = 0; i < 90; i++) {
    r.cmd('debugGrantGems', 1000);
    const res = r.cmd('pull', 10);
    for (const x of res.results) if (x.pity > maxPity) maxPity = x.pity;
    if (r.S.banner.pity > 50) above50++;
    if (i % 3 === 0 || r.S.banner.pity > 50) r.state();
  }
  for (let i = 0; i < 25; i++) { r.cmd('debugGrantGems', 100); r.cmd('pull', 1); if (r.S.banner.pity > 50) r.state(); }
  r.q('pityRate', 80); r.q('pityRate', 60);
  r.q('consolidatedRates');
  r.state(); r.drain();
  battery(r, false);
  r.maxPity = maxPity; r.above50 = above50;
  return r;
}
scenario('hard-pity', function () {
  let best = null;
  for (let s = 1; s <= 3000 && !best; s++) {
    const r = buildPity(s.toString(16));
    if (r.maxPity >= 65 && r.above50 >= 2) best = s.toString(16);
  }
  if (!best) throw new Error('no pity seed');
  const r = buildPity(best);
  delete r.maxPity; delete r.above50;
  return r;
});

// ---------------------------------------------------------------- 6
scenario('offline-and-days', function () {
  const start = T0 + DAY - 30000;             // 30 s before midnight: day rolls over during ticks
  const r = new Rec('offline-and-days', '0ff1e', start, false);
  r.state();
  r.advance(200); r.drain(); r.state();      // 20 s
  r.advance(300); r.drain(); r.state();      // crosses midnight
  let now = start + 500 * 100;
  now += 30000; r.sync(now); r.drain(); r.state();             // 30 s gap: no catch-up
  now += 59999; r.sync(now); r.drain();                         // just under the 60 s minimum
  now += 60000; r.sync(now); r.drain(); r.state();              // exactly 60 s: catch-up
  r.advance(100);
  now = r.sim.nowMs() + 2 * HOUR; r.sync(now); r.drain(); r.state(); r.q('nowMs');
  r.cmd('claimChest'); r.q('chestReadyAtMs'); r.q('chestReady');
  now = r.sim.nowMs() + 24 * HOUR; r.sync(now); r.drain(); r.state();   // 8 h cap, new day
  r.advance(50); r.drain();
  r.sync(r.sim.nowMs() - 500); r.drain();                       // tiny step back: ignored
  r.sync(r.sim.nowMs() - HOUR); r.drain(); r.state();           // backwards: clockWentBackwards
  r.advance(100); r.q('nowMs'); r.drain();
  r.sync(r.sim.nowMs() + 1000); r.drain(); r.state();           // forward within gap
  // day rollover by sync, quest reset, free pulls reset
  r.cmd('pull', 1);
  r.q('freePullsRemaining');
  now = (Math.floor(r.sim.nowMs() / DAY) + 1) * DAY + 5000; r.sync(now); r.drain(); r.state();
  r.q('freePullsRemaining');
  r.sync(now + 3 * DAY); r.drain(); r.state();                 // multi-day gap
  battery(r, false);
  return r;
});

// ---------------------------------------------------------------- 7
scenario('class-advance', function () {
  const r = new Rec('class-advance', 'c1a55', T0 + HOUR, false);
  r.cmd('debugSetStage', 49);
  r.state();
  r.q('classMult'); r.q('dps');
  r.cmd('debugGrantGold', 5e6);
  r.cmd('buyStatLevelsMax');
  let guard = 0;
  while (r.S.stage.stage < 50 && guard++ < 400) { r.advance(200); r.drain(); }
  if (r.S.stage.stage < 50) throw new Error('did not reach 50');
  r.state(); r.q('classMult'); r.q('dps'); r.q('hitDamage');
  r.advance(500); r.drain(); r.state();
  r.cmd('debugSetStage', 300);                 // multiple class advances at once, no event from debugSetStage
  r.state(); r.drain(); r.q('classMult'); r.q('dps');
  r.cmd('debugSetStage', 149);                 // stage goes down, classAdvances stays
  r.state(); r.q('classMult');
  r.cmd('debugSetStage', 9999);                // clamped to STAGE_CAP
  r.state(); r.q('hp', 9000);
  r.cmd('debugSetStage', 0);                   // clamped to 1
  r.state();
  battery(r, true);
  return r;
});

// ---------------------------------------------------------------- 8
const BOT_DAYS = +(process.env.PARITY_BOT_DAYS || 30);
scenario('bot-30-day', function () {
  const r = new Rec('bot-30-day', '7', T0, false);
  const sim = r.sim, s = sim.state;
  function spend() {   // identical to web/bot.js spend(), but every mutation is recorded as a step
    r.cmd('buyStatLevelsMax');
    let slot = -1;
    for (let i = 0; i < s.gear.length; i++) if (s.gear[i] && (slot < 0 || s.gear[i].enh < s.gear[slot].enh)) slot = i;
    if (slot >= 0 && s.gear[slot].enh < content.gear.ENH_MAX && s.cur.gold >= sim.enhanceCost()) r.cmd('enhance', slot);
    if (sim.chestReady()) r.cmd('claimChest');
    if (s.cur.freePullsBanked >= 10) r.cmd('pull', 10);
    let pulled = false;
    while (sim.pullPrice(1)) { r.cmd('pull', 1); pulled = true; }
    if (pulled || sim.autoEquipWouldChange()) r.cmd('autoEquip');
    if (s.stage.boss === 'walled' && sim.bossClearable()) r.cmd('attemptBoss');
  }
  for (let day = 1; day <= BOT_DAYS; day++) {
    r.sync(T0 + (day - 1) * DAY);
    for (let m = 0; m < 120; m++) { r.advance(TPM); spend(); }
    // events are compared in full on key days; on the others the drain executes but is not compared (check:false), to bound file size
    r.drain([1, 2, 3, 7, 14, 21, BOT_DAYS].indexOf(day) >= 0);
    r.state();
    if (day % 10 === 0) battery(r, day === BOT_DAYS);
  }
  r.q('validate');
  return r;
});

// ---------------------------------------------------------------- format vectors
function formatVectors() {
  const vals = [0, 0.5, 0.001, 0.004, 0.005, 1, 1.5, 2.25, 5, 9.99, 9.995, 9.999, 10, 10.4, 10.5, 99.4, 99.5, 100, 123.456, 999, 999.4, 999.5, 999.9, 1000, 1001, 1234,
    1500, 9999, 9999.9, 12345, 99949, 99950, 99999, 100000, 123456, 999499, 999500, 999999, 1000000, 1.005e6, 45.6e6, 999.5e6, 1e9, 2.5e9, 7.89e11, 1e12, 4.56e12, 999.5e12, 1e15, 1.23e17, 2.9e18,
    1e21, 3.14e24, 1e30, 5e50, 9.995e90, 1e100, 1.7e200, 1e300, 1.5e307];
  const out = [];
  for (const v of vals) { out.push([v, formatShort(v)]); if (v !== 0) out.push([-v, formatShort(-v)]); }
  return out;
}

// ---------------------------------------------------------------- write
fs.mkdirSync(OUT, { recursive: true });
const report = [];
for (const sc of scenarios) {
  const r = sc.fn();
  const text = lib.serializeFixture(r.fx);
  fs.writeFileSync(path.join(OUT, sc.name + '.json'), text);
  report.push(sc.name + ': ' + r.fx.steps.length + ' steps, ' + r.fx.expect.length + ' expects, ' + (text.length / 1024).toFixed(0) + ' KB');
}
const fv = formatVectors();
fs.writeFileSync(path.join(OUT, 'format-vectors.json'), '[\n' + fv.map((x) => JSON.stringify(x)).join(',\n') + '\n]\n');
report.push('format-vectors: ' + fv.length + ' pairs');
console.log(report.join('\n'));
