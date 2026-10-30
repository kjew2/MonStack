// Content table for the browser prototype. Every balance number from specs/00 section 2 lives here,
// so the game code never holds a balance literal (specs/00 section 5.3). Change a number here, then
// rerun `node web/bot.js` to see the effect on the 30-day curve.
(function (root) {
  'use strict';

  const HERO_NAMES = {
    C: ['Pip', 'Bram', 'Tilda', 'Odo', 'Wren', 'Moss', 'Kett', 'Lark', 'Fen', 'Rook',
        'Nell', 'Cob', 'Sorrel', 'Hob', 'Juniper', 'Tam', 'Birch', 'Dace', 'Ivy', 'Gorse'],
    R: ['Sable', 'Corin', 'Maren', 'Thistle', 'Jory', 'Vesna', 'Halden', 'Briar',
        'Osric', 'Linnea', 'Pell', 'Rhosyn', 'Caddoc', 'Elsin', 'Wystan'],
    E: ['Isolde', 'Draven', 'Seraphine', 'Kaelen', 'Mirelle', 'Thorne', 'Ysolde', 'Corvin', 'Anwen', 'Bastian'],
    L: ['Aurelion', 'Nyx', 'Valka', 'Solenne', 'Grimwald', 'Eira'],
    M: ['Astraeus', 'Morrigan', 'Zephyrine'],
  };

  const content = {
    version: '0.1.0-web',

    combat: {
      TICK_HZ: 10,
      BASE_ATK: 10,
      ATTACK_INTERVAL_TICKS: 10,
      HP0: 20, HP_G: 1.07,
      KILLS_PER_STAGE: 10,
      SPAWN_DELAY_TICKS: 5,
      BOSS_EVERY: 10,
      BOSS_HP_MULT: 10,
      BOSS_TIME_TICKS: 200,
      BOSS_GOLD_MULT: 3,
      STAGE_CAP: 9000,
    },

    economy: {
      GOLD0: 2, GOLD_G: 1.05,
      STAT_PER_LVL: 0.05,
      STAT_COST0: 50, STAT_COST_G: 1.07,
      STAT_BULK_STEPS: [1, 10, 100],
      PULL_COST_GEMS: 100,
      FREE_PULLS_PER_DAY: 3,
      DAY_SECONDS: 86400,
      FIRST_CLEAR_GEMS: 10,
      IDLE_CHEST_HOURS: 4, IDLE_CHEST_GEMS: 50,
      TUTORIAL_GEMS: 1000, TUTORIAL_FREE_PULLS: 10,
      OFFLINE_RATE: 0.5,
      OFFLINE_CAP_H: 8,
      OFFLINE_MIN_GAP_S: 60,
    },

    // specs/04 section 7.1. Kinds: login, kills, bossAttempts, pulls.
    quests: [
      { id: 'daily_login', kind: 'login', target: 1, gems: 50, label: 'Log in' },
      { id: 'daily_kills', kind: 'kills', target: 500, gems: 30, label: 'Defeat 500 enemies' },
      { id: 'daily_boss', kind: 'bossAttempts', target: 1, gems: 30, label: 'Challenge a boss' },
      { id: 'daily_pulls', kind: 'pulls', target: 3, gems: 30, label: 'Summon 3 heroes' },
    ],

    rarities: ['C', 'R', 'E', 'L', 'M'],
    rarityNames: { C: 'Common', R: 'Rare', E: 'Epic', L: 'Legendary', M: 'Mythic' },

    gear: {
      SLOTS: ['Weapon', 'Helm', 'Chest', 'Gloves', 'Boots', 'Ring'],
      DROP_P: 0.005,
      BOSS_DROP_P: 0.10,
      BOSS_MIN_RARITY: 1, // index into rarities: R
      TIER_WIDTH: 10,
      SLOT_G: 1.0074,
      RARITY_W: [0.60, 0.28, 0.09, 0.025, 0.005],
      RARITY_MULT: [1.0, 1.2, 1.5, 2.0, 3.0],
      ENH_MAX: 15,
      ENH_PER_LVL: 0.05,
      ENH_SUCCESS_FLOOR: 0.30, ENH_SUCCESS_STEP: 0.05,
      ENH_COST_GOLD_MULT: 20,
    },

    banner: {
      BASE_RATES: [0.60, 0.28, 0.09, 0.025, 0.005],
      BASE_LPLUS: 0.03,
      PITY_SOFT_START: 50,
      PITY_SOFT_STEP: 0.0325,
      PITY_HARD: 80,
      M_SHARE_OF_LPLUS: 1 / 6,
      SHARD_VALUE: [1, 5, 25, 100, 500],
    },

    heroes: {
      SLOTS: 6,
      BASE_PCT: [10, 20, 40, 80, 160],
      LVL_CUM: [1, 3, 6, 10, 16, 25, 40, 64, 100, 160],
      LVL_BONUS: 0.20,
      // Pool order is C, R, E, L, M; within a tier, index order. Glyph = tier letter + 1-based index.
      pool: ['C', 'R', 'E', 'L', 'M'].map(function (t) {
        return HERO_NAMES[t].map(function (name, i) {
          return { id: t + String(i + 1).padStart(2, '0'), tier: t, name: name };
        });
      }),
    },

    class: {
      STAGES: [50, 150, 300, 500],
      MULT: 1.5,
      NAMES: ['Squire', 'Knight', 'Champion', 'Warlord', 'Paragon'],
    },
  };

  if (typeof module !== 'undefined' && module.exports) module.exports = content;
  else root.IdleContent = content;
})(typeof self !== 'undefined' ? self : this);
