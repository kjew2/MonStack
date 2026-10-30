// Test double for IdleRng.Web.Engine: implements the BRIDGE.md string/JSON interface on top of web/sim.js, so the
// wasm facade in engine.js can be exercised in Node. Everything crosses as JSON text, like the real bridge.
'use strict';
const IS = require('../sim.js');

function makeMockHost() {
  let sim = null, content = null, emit = false;
  const j = (v) => JSON.stringify(v === undefined ? null : v);
  function uiState(s) {
    const out = JSON.parse(JSON.stringify(s, (k, v) => (k === 'rng' ? undefined : v)));   // no rng internals, no seeds
    const sorted = {};
    for (const id of Object.keys(out.heroes.owned).sort()) sorted[id] = out.heroes.owned[id];
    out.heroes.owned = sorted;
    return out;
  }
  const host = {
    calls: { Init: 0, Advance: 0, State: 0, Drain: 0, Query: 0, Cmd: 0, Export: 0 },
    Init(contentJson, saveJson, seedHex, nowMs, profileId) {
      host.calls.Init++;
      const c = JSON.parse(contentJson);
      const s = saveJson ? IS.deserialize(c, saveJson)
        : IS.Sim.newProfile(c, { profileSeed: BigInt('0x' + (seedHex || '0')), nowMs: nowMs, profileId: profileId });
      content = c; sim = s; sim.emitHits = emit;
    },
    Advance(n) { host.calls.Advance++; sim.advance(n | 0); },
    State() { host.calls.State++; return j(uiState(sim.state)); },
    Drain() { host.calls.Drain++; return j(sim.drain()); },
    Query(name, argsJson) {
      host.calls.Query++;
      try {
        const args = JSON.parse(argsJson).map((a) => (a === null ? undefined : a));
        if (name === 'consolidatedRates') return j(IS.consolidatedRates(content));
        if (typeof sim[name] !== 'function' || name[0] === '_') return 'null';
        return j(sim[name].apply(sim, args));
      } catch (e) { return j({ error: String(e && e.message || e) }); }
    },
    Cmd(name, argsJson) {
      host.calls.Cmd++;
      const allowed = ['buyStatLevels', 'buyStatLevelsMax', 'enhance', 'attemptBoss', 'pull', 'equipHero', 'unequipHero', 'autoEquip',
        'claimChest', 'syncWallClock', 'debugGrantGold', 'debugGrantGems', 'debugSetStage'];
      if (allowed.indexOf(name) < 0) return j({ error: 'unknown' });
      try {
        const args = JSON.parse(argsJson).map((a) => (a === null ? undefined : a));
        return j(sim[name].apply(sim, args));
      } catch (e) { return j({ error: String(e && e.message || e) }); }
    },
    Export() { host.calls.Export++; return IS.serialize(sim.state); },
    SetEmitHits(on) { emit = !!on; if (sim) sim.emitHits = emit; },
    Version() { return 'IdleRng.Sim MOCK (sim.js behind JSON)'; },
  };
  return host;
}

// loadWasm seam for IdleEngine.create({loadWasm}).
function mockLoader(hostOut) {
  return async function (o) {
    const host = makeMockHost();
    if (hostOut) hostOut.host = host;
    return { engine: host, contentJson: JSON.stringify(o.content) };
  };
}
module.exports = { makeMockHost, mockLoader };
