#!/usr/bin/env node
// Drift check + invariant validation for Content/content.json. Exit code 1 on any failure.
'use strict';
const fs = require('fs');
const path = require('path');
const { render, OUT } = require('./export-content.js');

const errors = [];
const fail = (m) => errors.push(m);

function validate(c) {
  const isInt = (v) => Number.isInteger(v);
  const posInt = (v) => isInt(v) && v > 0;
  const pos = (v) => typeof v === 'number' && isFinite(v) && v > 0;
  const sum = (a) => a.reduce((x, y) => x + y, 0);
  const arr = (a, n, what) => { if (!Array.isArray(a) || a.length !== n) { fail(what + ' must be an array of length ' + n); return false; } return true; };

  if (typeof c.version !== 'string' || !c.version) fail('version must be a non-empty string');
  const cb = c.combat, ec = c.economy, g = c.gear, b = c.banner, h = c.heroes, cl = c.class;
  for (const k of ['TICK_HZ', 'BASE_ATK', 'ATTACK_INTERVAL_TICKS', 'HP0', 'KILLS_PER_STAGE', 'SPAWN_DELAY_TICKS', 'BOSS_EVERY', 'BOSS_HP_MULT', 'BOSS_TIME_TICKS', 'BOSS_GOLD_MULT', 'STAGE_CAP'])
    if (!pos(cb[k])) fail('combat.' + k + ' must be > 0');
  for (const k of ['TICK_HZ', 'ATTACK_INTERVAL_TICKS', 'KILLS_PER_STAGE', 'SPAWN_DELAY_TICKS', 'BOSS_EVERY', 'BOSS_TIME_TICKS', 'STAGE_CAP'])
    if (!posInt(cb[k])) fail('combat.' + k + ' must be a positive integer');
  if (posInt(cb.KILLS_PER_STAGE) && cb.KILLS_PER_STAGE < 2) fail('combat.KILLS_PER_STAGE must be >= 2 (boss is pending at KILLS_PER_STAGE-1 kills)');
  if (!pos(cb.HP_G)) fail('combat.HP_G must be > 0');
  if (!pos(ec.GOLD0) || !pos(ec.GOLD_G)) fail('economy.GOLD0 / GOLD_G must be > 0');
  if (!pos(ec.STAT_COST0) || !pos(ec.STAT_COST_G)) fail('economy.STAT_COST0 / STAT_COST_G must be > 0');
  if (!pos(g.SLOT_G)) fail('gear.SLOT_G must be > 0');
  if (!pos(cl.MULT)) fail('class.MULT must be > 0');
  if (!(ec.STAT_PER_LVL >= 0)) fail('economy.STAT_PER_LVL must be >= 0');
  if (!Array.isArray(ec.STAT_BULK_STEPS) || !ec.STAT_BULK_STEPS.length || !ec.STAT_BULK_STEPS.every(posInt)) fail('economy.STAT_BULK_STEPS must be positive integers');
  for (const k of ['PULL_COST_GEMS', 'DAY_SECONDS', 'IDLE_CHEST_HOURS', 'OFFLINE_CAP_H', 'OFFLINE_MIN_GAP_S'])
    if (!pos(ec[k])) fail('economy.' + k + ' must be > 0');
  for (const k of ['FREE_PULLS_PER_DAY', 'FIRST_CLEAR_GEMS', 'IDLE_CHEST_GEMS', 'TUTORIAL_GEMS', 'TUTORIAL_FREE_PULLS'])
    if (!isInt(ec[k]) || ec[k] < 0) fail('economy.' + k + ' must be an integer >= 0');
  if (!(ec.OFFLINE_RATE >= 0 && ec.OFFLINE_RATE <= 1)) fail('economy.OFFLINE_RATE must be in [0,1]');

  // quests
  const kinds = ['login', 'kills', 'bossAttempts', 'pulls'];
  if (!Array.isArray(c.quests) || !c.quests.length) fail('quests must be a non-empty array');
  else {
    const ids = new Set();
    c.quests.forEach((q, i) => {
      if (!q.id || ids.has(q.id)) fail('quests[' + i + '].id must be unique and non-empty'); ids.add(q.id);
      if (kinds.indexOf(q.kind) < 0) fail('quests[' + i + '].kind must be one of ' + kinds.join(','));
      if (!posInt(q.target)) fail('quests[' + i + '].target must be a positive integer');
      if (!isInt(q.gems) || q.gems < 0) fail('quests[' + i + '].gems must be an integer >= 0');
      if (typeof q.label !== 'string') fail('quests[' + i + '].label must be a string');
    });
  }

  // rarities
  if (arr(c.rarities, 5, 'rarities')) {
    if (new Set(c.rarities).size !== 5) fail('rarities must be distinct');
    for (const r of c.rarities) if (!c.rarityNames || typeof c.rarityNames[r] !== 'string') fail('rarityNames missing ' + r);
  }

  // gear
  if (arr(g.SLOTS, 6, 'gear.SLOTS') && new Set(g.SLOTS).size !== 6) fail('gear.SLOTS must be distinct');
  if (arr(g.RARITY_W, 5, 'gear.RARITY_W')) {
    if (!g.RARITY_W.every((x) => x >= 0)) fail('gear.RARITY_W must be >= 0');
    if (Math.abs(sum(g.RARITY_W) - 1) > 1e-9) fail('gear.RARITY_W must sum to 1 within 1e-9 (got ' + sum(g.RARITY_W) + ')');
  }
  if (arr(g.RARITY_MULT, 5, 'gear.RARITY_MULT') && !g.RARITY_MULT.every(pos)) fail('gear.RARITY_MULT must be > 0');
  if (!(g.DROP_P >= 0 && g.DROP_P <= 1) || !(g.BOSS_DROP_P >= 0 && g.BOSS_DROP_P <= 1)) fail('gear.DROP_P / BOSS_DROP_P must be in [0,1]');
  if (!isInt(g.BOSS_MIN_RARITY) || g.BOSS_MIN_RARITY < 0 || g.BOSS_MIN_RARITY > 4) fail('gear.BOSS_MIN_RARITY must be an index 0..4');
  else if (g.RARITY_W && !(sum(g.RARITY_W.slice(g.BOSS_MIN_RARITY)) > 0)) fail('boss rarity range must have positive total weight');
  if (!posInt(g.TIER_WIDTH)) fail('gear.TIER_WIDTH must be a positive integer');
  if (!posInt(g.ENH_MAX)) fail('gear.ENH_MAX must be a positive integer');
  if (!(g.ENH_PER_LVL >= 0) || !(g.ENH_SUCCESS_FLOOR >= 0 && g.ENH_SUCCESS_FLOOR <= 1) || !(g.ENH_SUCCESS_STEP >= 0)) fail('gear enhance parameters out of range');
  if (!pos(g.ENH_COST_GOLD_MULT)) fail('gear.ENH_COST_GOLD_MULT must be > 0');

  // banner
  if (arr(b.BASE_RATES, 5, 'banner.BASE_RATES') && Math.abs(sum(b.BASE_RATES) - 1) > 1e-9) fail('banner.BASE_RATES must sum to 1 within 1e-9 (got ' + sum(b.BASE_RATES) + ')');
  if (arr(b.SHARD_VALUE, 5, 'banner.SHARD_VALUE') && !b.SHARD_VALUE.every((x) => x >= 0)) fail('banner.SHARD_VALUE must be >= 0');
  if (!(b.BASE_LPLUS >= 0 && b.BASE_LPLUS <= 1)) fail('banner.BASE_LPLUS must be in [0,1]');
  if (!(b.PITY_SOFT_STEP >= 0)) fail('banner.PITY_SOFT_STEP must be >= 0');
  if (!posInt(b.PITY_HARD) || !isInt(b.PITY_SOFT_START) || b.PITY_SOFT_START < 0 || b.PITY_SOFT_START >= b.PITY_HARD) fail('banner pity: 0 <= PITY_SOFT_START < PITY_HARD, both integers');
  if (!(b.M_SHARE_OF_LPLUS > 0 && b.M_SHARE_OF_LPLUS < 1)) fail('banner.M_SHARE_OF_LPLUS must be in (0,1)');

  // heroes
  if (h.SLOTS !== 6) fail('heroes.SLOTS must be 6');
  if (arr(h.BASE_PCT, 5, 'heroes.BASE_PCT') && !h.BASE_PCT.every(pos)) fail('heroes.BASE_PCT must be > 0');
  if (!Array.isArray(h.LVL_CUM) || !h.LVL_CUM.length || !h.LVL_CUM.every(posInt)) fail('heroes.LVL_CUM must be positive integers');
  else for (let i = 1; i < h.LVL_CUM.length; i++) if (!(h.LVL_CUM[i] > h.LVL_CUM[i - 1])) fail('heroes.LVL_CUM must be strictly increasing (index ' + i + ')');
  if (!(h.LVL_BONUS >= 0)) fail('heroes.LVL_BONUS must be >= 0');
  const sizes = [20, 15, 10, 6, 3];
  if (arr(h.pool, 5, 'heroes.pool')) {
    const seen = new Set();
    h.pool.forEach((list, t) => {
      if (list.length !== sizes[t]) fail('heroes.pool[' + t + '] must have ' + sizes[t] + ' heroes (got ' + list.length + ')');
      list.forEach((x, i) => {
        const id = c.rarities[t] + String(i + 1).padStart(2, '0');
        if (x.id !== id) fail('heroes.pool[' + t + '][' + i + '].id must be ' + id + ' (got ' + x.id + ')');
        if (x.tier !== c.rarities[t]) fail('heroes.pool[' + t + '][' + i + '].tier must be ' + c.rarities[t]);
        if (!x.name || !x.short) fail('hero ' + x.id + ' needs name and short');
        if (seen.has(x.id)) fail('duplicate hero id ' + x.id); seen.add(x.id);
      });
    });
  }

  // class
  if (!Array.isArray(cl.STAGES) || !cl.STAGES.length || !cl.STAGES.every(posInt)) fail('class.STAGES must be positive integers');
  else {
    for (let i = 1; i < cl.STAGES.length; i++) if (!(cl.STAGES[i] > cl.STAGES[i - 1])) fail('class.STAGES must be strictly increasing');
    if (!Array.isArray(cl.NAMES) || cl.NAMES.length !== cl.STAGES.length + 1) fail('class.NAMES must have STAGES.length + 1 entries');
  }
}

if (require.main === module) {
  const expected = render();
  let actual = null;
  try { actual = fs.readFileSync(OUT, 'utf8'); } catch (e) { fail('missing ' + OUT + ' (run node web/tools/export-content.js)'); }
  if (actual !== null && actual !== expected) {
    const a = actual.split('\n'), e = expected.split('\n');
    let shown = 0;
    for (let i = 0; i < Math.max(a.length, e.length) && shown < 20; i++)
      if (a[i] !== e[i]) { fail('drift at line ' + (i + 1) + ':\n    file    : ' + a[i] + '\n    content : ' + e[i]); shown++; }
    fail('Content/content.json differs from web/content.js (run node web/tools/export-content.js)');
  }
  let parsed = null;
  try { parsed = JSON.parse(actual === null ? expected : actual); } catch (e) { fail('content.json is not valid JSON: ' + e.message); }
  if (parsed) {
    validate(parsed);
    if (parsed.banner && parsed.banner.M_SHARE_OF_LPLUS !== 1 / 6) fail('M_SHARE_OF_LPLUS did not round-trip to 1/6');
    validate(require('../content.js'));
  }
  if (errors.length) { console.error('check-content FAILED:\n  - ' + errors.join('\n  - ')); process.exit(1); }
  console.log('check-content ok: content.json matches content.js and all invariants hold');
}
