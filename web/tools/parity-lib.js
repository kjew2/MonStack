// Shared executor for parity fixtures: used by gen-parity.js (record) and verify-parity.js (replay).
'use strict';
const simApi = require('../sim.js');
const { Sim } = simApi;

const CMDS = ['buyStatLevels', 'buyStatLevelsMax', 'enhance', 'attemptBoss', 'pull', 'equipHero', 'unequipHero', 'autoEquip',
  'claimChest', 'debugGrantGold', 'debugGrantGems', 'debugSetStage'];
const QUERIES = ['hp', 'bossHp', 'gold', 'statCost', 'statMult', 'gearTier', 'gearBasePct', 'gearEffPct', 'gearSlotMult', 'gearMult',
  'heroLevel', 'heroPct', 'heroMult', 'classMult', 'dps', 'hitDamage', 'hitsToKill', 'killTimeSeconds', 'isBossStage',
  'bossClearable', 'needDps', 'enhanceCost', 'enhanceSuccess', 'pityRate', 'freePullsRemaining', 'chestReadyAtMs',
  'chestReady', 'bulkCost', 'maxAffordable', 'pullPrice', 'bestParty', 'autoEquipWouldChange', 'nowMs', 'validate',
  'consolidatedRates'];

// JSON-normalise a result: BigInt or non-finite numbers are an error; undefined becomes null.
function clean(v) {
  if (v === undefined) return null;
  return JSON.parse(JSON.stringify(v, function (k, x) {
    if (typeof x === 'bigint') throw new Error('BigInt reached JSON at key ' + k);
    if (typeof x === 'number' && !isFinite(x)) throw new Error('non-finite number at key ' + k);
    return x;
  }));
}

// UiState projection: sim.state minus rng; heroes.owned keys sorted by id.
function uiState(sim) {
  const s = sim.state, out = {};
  for (const k of Object.keys(s)) if (k !== 'rng') out[k] = s[k];
  const owned = {};
  for (const id of Object.keys(s.heroes.owned).sort()) owned[id] = s.heroes.owned[id];
  out.heroes = { owned: owned, party: s.heroes.party };
  return clean(out);
}

function create(fx, content) {
  const sim = Sim.newProfile(content, { profileSeed: BigInt('0x' + fx.seedHex), nowMs: fx.nowMs, profileId: 'fixture' });
  sim.emitHits = !!fx.emitHits;
  return sim;
}

// Executes one step. Returns {has:boolean, value}. has=false when the step contributes no expect entry.
function exec(sim, content, step) {
  switch (step.op) {
    case 'advance': sim.advance(step.n); return { has: false };
    case 'sync': sim.syncWallClock(step.nowMs); return { has: false };
    case 'state': return { has: true, value: uiState(sim) };
    case 'drain': {
      const ev = clean(sim.drain());
      return step.check === false ? { has: false } : { has: true, value: ev };
    }
    case 'query': {
      if (QUERIES.indexOf(step.name) < 0) throw new Error('unknown query ' + step.name);
      const v = step.name === 'consolidatedRates' ? simApi.consolidatedRates(content) : sim[step.name].apply(sim, step.args);
      return { has: true, value: clean(v) };
    }
    case 'cmd': {
      if (CMDS.indexOf(step.name) < 0) throw new Error('unknown cmd ' + step.name);
      return { has: true, value: clean(sim[step.name].apply(sim, step.args)) };
    }
    default: throw new Error('unknown op ' + step.op);
  }
}

// Order-insensitive deep equality of JSON values. Returns null or a path string of the first difference.
function diff(a, b, path) {
  path = path || '$';
  if (a === b) return null;
  if (typeof a !== typeof b || a === null || b === null || typeof a !== 'object') return path + ': ' + JSON.stringify(a) + ' != ' + JSON.stringify(b);
  if (Array.isArray(a) !== Array.isArray(b)) return path + ': array/object mismatch';
  if (Array.isArray(a)) {
    if (a.length !== b.length) return path + ': length ' + a.length + ' != ' + b.length;
    for (let i = 0; i < a.length; i++) { const d = diff(a[i], b[i], path + '[' + i + ']'); if (d) return d; }
    return null;
  }
  const ka = Object.keys(a).sort(), kb = Object.keys(b).sort();
  if (ka.join('\u0000') !== kb.join('\u0000')) return path + ': keys {' + ka + '} != {' + kb + '}';
  for (const k of ka) { const d = diff(a[k], b[k], path + '.' + k); if (d) return d; }
  return null;
}

// Fixture file text: header fields, then one step / expect entry per line (keeps big files diffable).
function serializeFixture(fx) {
  const head = {};
  for (const k of Object.keys(fx)) if (k !== 'steps' && k !== 'expect') head[k] = fx[k];
  const h = JSON.stringify(head);
  const lines = (a) => a.map((x) => JSON.stringify(x)).join(',\n');
  return h.slice(0, -1) + ',\n"steps":[\n' + lines(fx.steps) + '\n],\n"expect":[\n' + lines(fx.expect) + '\n]}\n';
}

module.exports = { create, exec, uiState, clean, diff, serializeFixture, CMDS, QUERIES, simApi };
