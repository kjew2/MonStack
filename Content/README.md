# Content schema

`Content/content.json` is generated from `web/content.js` by `node web/tools/export-content.js`. Never edit it by hand: `node web/tools/check-content.js` fails when it drifts. Key names are exactly those of the JS object (UPPER_SNAKE constants stay UPPER_SNAKE); the C# loader binds to them. (The older split files in this folder are the spec drafts and are not used by the loader.)

All numbers are JSON numbers and must be read as IEEE doubles (e.g. `M_SHARE_OF_LPLUS` is 1/6 written with 17 digits and must parse to exactly the same double). Fields documented as integers must be integral.

## Top level
`version` string. `combat`, `economy`, `quests`, `rarities`, `rarityNames`, `gear`, `banner`, `heroes`, `class`.

## combat
| field | unit / meaning |
|---|---|
| TICK_HZ | ticks per second (10); wall ms per tick = 1000/TICK_HZ |
| BASE_ATK | base dps at stat level 0 |
| ATTACK_INTERVAL_TICKS | ticks between hits (hit damage = dps * ATTACK_INTERVAL_TICKS / TICK_HZ) |
| HP0, HP_G | enemy hp at stage k = HP0 * HP_G^k, built by repeated multiplication (stage 0 = HP0) |
| KILLS_PER_STAGE | kills to advance; boss goes pending at KILLS_PER_STAGE-1 kills on boss stages (so must be >= 2) |
| SPAWN_DELAY_TICKS | ticks between a death and the next spawn |
| BOSS_EVERY | stages divisible by this have a boss |
| BOSS_HP_MULT, BOSS_TIME_TICKS, BOSS_GOLD_MULT | boss hp multiplier, time limit in ticks, gold multiplier |
| STAGE_CAP | highest stage |

## economy
GOLD0, GOLD_G: gold per kill = GOLD0 * GOLD_G^stage. STAT_PER_LVL: dps +5% per level (multiplier 1 + STAT_PER_LVL*n). STAT_COST0, STAT_COST_G: cost of level n = STAT_COST0 * STAT_COST_G^n. STAT_BULK_STEPS: UI bulk sizes. PULL_COST_GEMS: gems per pull (x10 for ten). FREE_PULLS_PER_DAY, DAY_SECONDS (day key = floor(utcMs / (DAY_SECONDS*1000))). FIRST_CLEAR_GEMS: gems for first clear of each new boss stage. IDLE_CHEST_HOURS, IDLE_CHEST_GEMS. TUTORIAL_GEMS, TUTORIAL_FREE_PULLS: one-time grants at profile creation. OFFLINE_RATE: fraction of online gold rate earned offline. OFFLINE_CAP_H: max credited hours. OFFLINE_MIN_GAP_S: gaps below this are ignored (no catch-up).

## quests
Array of `{id, kind, target, gems, label}`. `kind` is one of `login`, `kills`, `bossAttempts`, `pulls`; `target` positive integer progress; `gems` reward when reached. Order is significant (daily progress arrays are index-aligned).

## rarities, rarityNames
`rarities`: 5 tier letters in order C, R, E, L, M (index = tier number used everywhere). `rarityNames`: letter to display name.

## gear
SLOTS: 6 slot names (index = slot). DROP_P / BOSS_DROP_P: drop probability per normal / boss kill. BOSS_MIN_RARITY: lowest rarity index on boss drops. TIER_WIDTH: gear tier = floor(stage/TIER_WIDTH)*TIER_WIDTH. SLOT_G: piece base value (tier) = SLOT_G^tier - 1. RARITY_W: 5 drop weights (sum 1). RARITY_MULT: 5 multipliers. ENH_MAX, ENH_PER_LVL (+5% effect per level), ENH_SUCCESS_FLOOR/STEP (success = max(FLOOR, 1 - STEP*level)), ENH_COST_GOLD_MULT (cost = mult * gold(stage)).

## banner
BASE_RATES: 5 non-pity tier rates (sum 1; only C, R, E are sampled when no L+). BASE_LPLUS, PITY_SOFT_START, PITY_SOFT_STEP, PITY_HARD: L+ rate at pity n is min(1, BASE_LPLUS + STEP*max(0, n - SOFT_START)), 1 at n >= PITY_HARD. M_SHARE_OF_LPLUS: probability an L+ is Mythic (1/6). SHARD_VALUE: 5 shards per overflow duplicate by tier.

## heroes
SLOTS: party size 6. BASE_PCT: 5 per-tier base dps percent. LVL_CUM: cumulative copies for each hero level (strictly increasing; level = count of entries <= copies). LVL_BONUS: per-level bonus fraction. `pool`: 5 arrays (by tier) of `{id, tier, name, short}`; sizes 20, 15, 10, 6, 3; id = tier letter + 2-digit 1-based index ("C01"); `tier` is the letter; `short` is the first word of name. Pool order is the draw order (`floor(u * size)`).

## class
STAGES: stages at which a class advance occurs (strictly increasing). MULT: dps multiplier per advance (MULT^advances). NAMES: STAGES.length + 1 display names.

## Invariants a loader must validate
- `BASE_RATES` and `RARITY_W` have 5 entries each and sum to 1 within 1e-9; `RARITY_MULT`, `SHARD_VALUE`, `BASE_PCT` have 5 entries; all positive (weights/shards >= 0).
- Growth factors `HP_G, GOLD_G, STAT_COST_G, SLOT_G, class.MULT` > 0 (values > 0 required; the shipped ones are > 1).
- `LVL_CUM` positive and strictly increasing.
- Pool sizes exactly 20/15/10/6/3, ids `<tier><2-digit index>` unique, `tier` equals `rarities[t]`.
- `gear.SLOTS` has 6 entries, `heroes.SLOTS` is 6, `rarities` has 5 distinct entries and `rarityNames` covers them.
- `0 <= PITY_SOFT_START < PITY_HARD` integers; `0 < M_SHARE_OF_LPLUS < 1`; probabilities in [0,1]; `BOSS_MIN_RARITY` in 0..4 with positive weight above it.
- `KILLS_PER_STAGE >= 2`; tick counts and `STAGE_CAP`, `BOSS_EVERY`, `TIER_WIDTH`, `ENH_MAX` positive integers.
- Quest ids unique, kinds from the list, targets positive. `class.NAMES.length == class.STAGES.length + 1`.

`node web/tools/check-content.js` implements these checks (and the drift check).
