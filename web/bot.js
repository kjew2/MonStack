// Headless greedy F2P bot (specs/04 section 9, specs/09 section 4) driving the real browser core.
// Plays N days in seconds and prints the day table against the specs/00 section 2.6 targets.
// Run: node web/bot.js [days=30] [seed=7]
'use strict';
const content = require('./content.js');
const { Sim, formatShort } = require('./sim.js');

const DAYS = +(process.argv[2] || 30);
const SEED = +(process.argv[3] || 7);
const ACTIVE_MIN = 120;
const TICKS_PER_MIN = 60 * content.combat.TICK_HZ;
const DAY_MS = 86400000;
const TARGETS = { 1: 90, 3: 240, 7: 380, 14: 480, 30: 550 };

const t0 = Date.UTC(2026, 0, 1);
const sim = Sim.newProfile(content, { profileSeed: SEED, nowMs: t0 });

function spend() {
  const s = sim.state;
  sim.buyStatLevelsMax();
  // one enhancement attempt on the weakest equipped slot
  let slot = -1;
  for (let i = 0; i < s.gear.length; i++) if (s.gear[i] && (slot < 0 || s.gear[i].enh < s.gear[slot].enh)) slot = i;
  if (slot >= 0 && s.gear[slot].enh < content.gear.ENH_MAX && s.cur.gold >= sim.enhanceCost()) sim.enhance(slot);
  if (sim.chestReady()) sim.claimChest();
  if (s.cur.freePullsBanked >= 10) sim.pull(10);
  let pulled = false;
  while (sim.pullPrice(1)) { sim.pull(1); pulled = true; }
  if (pulled || sim.autoEquipWouldChange()) sim.autoEquip();
  if (s.stage.boss === 'walled' && sim.bossClearable()) sim.attemptBoss();
}

const pad = (v, n) => String(v).padStart(n);
console.log('day  stage target   dps      statLvl  gear       heroes  class  pulls  L+  gems/day');
const start = Date.now();
for (let day = 1; day <= DAYS; day++) {
  let earned = 0;
  const tally = () => { for (const e of sim.drain()) if (e.gems && e.type !== 'pulled') earned += e.gems; };
  sim.syncWallClock(t0 + (day - 1) * DAY_MS);   // overnight catch-up and daily rollover
  for (let m = 0; m < ACTIVE_MIN; m++) {
    sim.advance(TICKS_PER_MIN);
    tally();
    spend();
    tally();
  }
  const s = sim.state;
  const target = TARGETS[day] ? `${TARGETS[day]}` : '';
  const off = TARGETS[day] ? ` (${s.stage.stage >= TARGETS[day] * 0.8 && s.stage.stage <= TARGETS[day] * 1.2 ? 'ok' : 'OUT'})` : '';
  console.log(`${pad(day, 3)}  ${pad(s.stage.stage, 5)} ${pad(target, 4)}${off.padEnd(5)} ${pad(formatShort(sim.dps()), 7)}  ${pad(s.statLevel, 6)}  x${pad(formatShort(sim.gearMult()), 7)}  x${sim.heroMult().toFixed(2)}  x${sim.classMult().toFixed(2)}  ${pad(s.banner.totalPulls, 4)}  ${pad(s.life.lplus, 2)}  ${pad(earned, 4)}`);
}
const errs = sim.validate();
console.log(`\n${DAYS} days in ${((Date.now() - start) / 1000).toFixed(1)} s. Invariants: ${errs.length ? errs.join('; ') : 'ok'}`);
console.log('Gems/day counts login, quests, first clears and chests (day 1 excludes the 1000-gem tutorial grant). One 2 h session per day, so one idle chest per day.');
