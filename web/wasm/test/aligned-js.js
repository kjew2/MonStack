// "Tuning-aligned" JS reference used only by parity-report.js. It runs the stock web/sim.js in-process after applying, in memory,
// the known intentional differences of the C# core, so what is left in the diff against C# is unexplained.
// Nothing here edits web/sim.js or any fixture. Each patch names the C# behaviour it mirrors.
'use strict';
const path = require('path');
const lib = require(path.join(__dirname, '..', '..', 'tools', 'parity-lib.js'));
const P = lib.simApi.Sim.prototype;
let patched = false;

function patch() {
  if (patched) return lib;
  patched = true;
  // 1. Boss rule "intro" (boss_rules.json): HP x4 below stage 60, x10 from stage 60. sim.js uses x10 everywhere.
  P.bossHp = function (stage) { return (stage < 60 ? 4 : this.c.combat.BOSS_HP_MULT) * this.hp(stage); };

  // 2. Gear drops auto-sell: a replaced piece or an unequipped drop is sold for GEAR_SELL_KILLS * Gold(tier) * rarityMult (Simulation.RollGearDrop).
  const roll0 = P._rollGearDrop;
  P._rollGearDrop = function (isBoss) {
    const n = this.events.length;
    roll0.call(this, isBoss);
    if (this.events.length === n) return;
    const e = this.events[this.events.length - 1];
    if (e.type !== 'gearDropped') return;
    const sellOf = (p) => 5 * this.gold(p.tier) * this.c.gear.RARITY_MULT[p.rarity];
    const sold = e.equipped ? (e.replaced ? sellOf(e.replaced) : 0) : sellOf(e.piece);
    if (sold > 0) { this.state.cur.gold += sold; this.state.life.goldEarned += sold; }
    e.soldFor = sold;
  };

  // 3. Whole-second wall clock (C# SyncWallClock takes seconds; offline credit uses integer elapsed seconds).
  const sync0 = P.syncWallClock;
  P.syncWallClock = function (nowMs) { return sync0.call(this, Math.floor(nowMs / 1000) * 1000); };
  const catch0 = P._catchUp;
  P._catchUp = function (gapMs) { return catch0.call(this, Math.floor(gapMs / 1000) * 1000); };

  // 4. AutoEquipBest (HeroState.AutoEquipBest, default true): after every pull the party is re-picked positionally;
  //    equip/unequip turn it off; autoEquip() turns it on and fills positionally.
  const fillBest = function () {
    const best = this.bestParty(), party = this.state.heroes.party;
    let changed = 0;
    const next = party.map((_, i) => (i < best.length ? best[i] : null));
    for (let i = 0; i < next.length; i++) if (next[i] !== party[i]) changed++;
    this.state.heroes.party = next; this._dirty = true;
    return changed;
  };
  Object.defineProperty(P, '_autoBest', { value: true, writable: true, configurable: true });
  const pull0 = P.pull;
  P.pull = function (count) { const r = pull0.call(this, count); if (r.ok && this._autoBest) fillBest.call(this); return r; };
  const eq0 = P.equipHero;
  P.equipHero = function (s, id) { const r = eq0.call(this, s, id); if (r.ok || r.reason === 'heroAlreadyEquipped') this._autoBest = false; return r; };
  const un0 = P.unequipHero;
  P.unequipHero = function (s) { this._autoBest = false; return un0.call(this, s); };
  P.autoEquip = function () { this._autoBest = true; return fillBest.call(this); };
  // 5. Curves: C# evaluates Base * (growth^k with growth^k built by repeated multiplication from 1); sim.js builds Base, Base*g, Base*g*g...
  //    The two differ in the last bit. (BaseLplus is aligned through the content: see alignContent.)
  const pows = new Map();
  const powOf = (g, k) => { let t = pows.get(g); if (!t) { t = [1]; pows.set(g, t); } while (t.length <= k) t.push(t[t.length - 1] * g); return t[k]; };
  P.hp = function (stage) { return this.c.combat.HP0 * powOf(this.c.combat.HP_G, stage); };
  P.gold = function (stage) { return this.c.economy.GOLD0 * powOf(this.c.economy.GOLD_G, stage); };
  P.statCost = function (n) { return this.c.economy.STAT_COST0 * powOf(this.c.economy.STAT_COST_G, n); };
  return lib;
}

// Content tweaks matching Content/*.json: BASE_ATK 20; BaseLplus is the sum of the L and M rates (0.025 + 0.005), not the literal 0.03.
function alignContent(content) {
  content.combat.BASE_ATK = 20;
  content.banner.BASE_LPLUS = content.banner.BASE_RATES[3] + content.banner.BASE_RATES[4];
  return content;
}

module.exports = { patch, alignContent, lib };
