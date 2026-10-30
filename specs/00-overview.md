# 00 - Prototype Overview and Shared Contracts

Working title: **Idle RNG** (placeholder). Genre reference: Sword x Staff, Maple Idle.
This document is the contract every other spec in `specs/` builds on. If another spec disagrees with a number or name here, this file wins and the other spec is wrong.

## 1. Prototype scope

The prototype answers one question: *does the core loop feel good for two weeks of simulated play, and does the code shape let us tune it from a spreadsheet?*

| Tag | Meaning |
|---|---|
| **P0** | In the prototype. Must be implemented and testable. |
| **P1** | Designed in the spec so P0 code leaves room for it, but not built. |
| **P2** | Mentioned only so we do not paint ourselves into a corner. |

P0 feature list:
1. Auto-combat stage push with boss walls every 10 stages.
2. Gold sink: ATK stat levels.
3. Gear drops (main stat only) into 6 slots, with enhancement.
4. Companion (hero) gacha with pity, 10-pull guarantee, dupe-to-level, a 4-companion party. Companions have a stat sheet, role and element (`specs/11`).
5. Per-companion promotion at stage milestones (replaces class advance; `specs/11` section 6).
6. Idle earnings in-app, offline catch-up with cap, server-clock seam (local fake clock in prototype).
7. Versioned local save with migrations.
8. Headless bot harness that plays N days in seconds and emits the tuning table.
9. Ugly-UI Unity client exposing all of the above.

Gameplay overhaul (#60, `specs/11`): 4v1 party battle for bosses is live; its later phases (affix gear, dungeons, uniques) are cancelled by the pivot below.

**Pivot planned (kj, 2026-10-30):** the game becomes a monster-raising game with training runs, skill trees, towers and async PvP. Target design: `specs/12`. Transition plan: `specs/13`. This file keeps describing the idle game until `specs/13` phase T7 rewrites it.

P1: pet banner, shard shop, rate-up banners, accounts, cloud save, receipt validation and IAP, rewarded ads, certified progress (server-verified), stage leaderboard, asynchronous PvP. The trust model that keeps these fair is `specs/10`; P0 builds only its cheap shapes (command log, state hash, schema fields).
P2: rebirth/prestige, guilds, real-time PvP.

## 2. Canonical constants

All constants below are validated by `tools/gacha_model.py` and `tools/power_curve_model.py`. Change them there first, rerun, then update this table and the content tables. Code must read these from content data (`specs/05`), never hard-code them.

### 2.1 Combat and stages

| Name | Value | Notes |
|---|---|---|
| `TICK_HZ` | 10 | Fixed simulation tick. 1 tick = 100 ms. |
| `SPD_REF` | 100 | Companion SPD that gives one hit per `ATTACK_INTERVAL_TICKS` in farming (`specs/11` section 7). Replaces `BASE_ATK`: there is no player character, so party DPS comes from companion stats. |
| `ATTACK_INTERVAL_TICKS` | 10 | One hit per second. Sets the kill-time floor. |
| `HP0`, `HP_G` | 20, 1.07 | Enemy HP at stage `s` = `HP0 * HP_G^s`. |
| `KILLS_PER_STAGE` | 10 | Kills to advance a normal stage. |
| `SPAWN_DELAY_TICKS` | 5 | Dead enemy to next enemy. |
| `BOSS_EVERY` | 10 | Stage `s` is a boss stage when `s % 10 == 0`. |
| `BOSS_HP_MULT` | 10 | Boss HP = `BOSS_HP_MULT * HP(s)`. **POC content adds an `intro` boss rule (hp_mult 4) for stages 1-59** so the first wall lands at 60+ (specs/02 section 9 item 3); 10 applies from stage 60. |
| `BOSS_TIME_TICKS` | 200 | 20 s. Turn limit of the 4v1 boss battle (`specs/11` section 8). Fail (wipe or limit) = boss despawns, player keeps farming stage `s` mobs. |
| Boss battle | see `specs/11` section 8 | ATB gauge `BATTLE_GAUGE` 1000, `DEF_K_MULT` 0.1, elements x1.3 / x0.8, boss ATK/DEF/SPD/AoE per `boss_rules`. Placeholders. |
| `BOSS_GOLD_MULT` | 3 | Boss drops 3x the stage's gold. |
| `STAGE_CAP` | 9000 | Sim emits `StageCapReached` instead of advancing past it; `double` range guard (`specs/03`). |

### 2.2 Economy

| Name | Value | Notes |
|---|---|---|
| `GOLD0`, `GOLD_G` | 2, 1.05 | Gold per kill at stage `s` = `GOLD0 * GOLD_G^s`. |
| `STAT_PER_LVL` | 0.05 | ATK stat multiplier = `1 + 0.05 * n`. |
| `STAT_COST0`, `STAT_COST_G` | 50, 1.07 | Cost of level `n -> n+1` = `50 * 1.07^n` gold. |
| `GEMS_PER_DAY_F2P` | 300 | Target free gem income (dailies + first clears). Tuning target, not a formula. |
| `PULL_COST_GEMS` | 100 | Single pull. 10-pull = 1000. |
| `FREE_PULLS_PER_DAY` | 3 | Reset at daily rollover (server time). |
| `DAY_SECONDS` | 86400 | Day key = `floor(serverSeconds / DAY_SECONDS)`; rollover 00:00 UTC. |
| `LOGIN_GEMS` | 50 | Granted once per day key on first `EnsureDay`. |
| `QUEST_GEMS` | 30 | Reward per completed daily quest. |
| `QUEST_KILLS_TARGET` | 500 | Daily quest: kills. |
| `QUEST_BOSS_TARGET` | 1 | Daily quest: boss fights **started** (not cleared), so it is completable while walled. |
| `QUEST_PULLS_TARGET` | 3 | Daily quest: pulls, free pulls count. |
| `FIRST_CLEAR_GEMS` | 10 | Per boss stage cleared for the first time. |
| `IDLE_CHEST_HOURS`, `IDLE_CHEST_GEMS` | 4, 50 | One chest timer, restarts at claim. Two chests/day is the budgeted 100. |
| `TUTORIAL_GEMS`, `TUTORIAL_FREE_PULLS` | 1000, 10 | One-time grants at profile creation. |
| `STAT_BULK_STEPS` | 1, 10, 100 | Fixed bulk-buy buttons; Max is a separate button. |
| `OFFLINE_RATE` | 0.5 | Offline gold per second as a fraction of in-app rate. |
| `OFFLINE_CAP_H` | 8 | Offline accrual stops after 8 h. |

### 2.3 Gear

| Name | Value | Notes |
|---|---|---|
| `GEAR_SLOTS` | 6 | weapon, helm, chest, gloves, boots, ring. |
| `GEAR_DROP_P` | 0.005 | Per normal kill. |
| `GEAR_BOSS_DROP_P` | 0.10 | Per boss kill. |
| `GEAR_BOSS_MIN_RARITY` | R | Boss drops renormalise the rarity table over R+. |
| `TIER_WIDTH` | 10 | `GEAR_TIER(s) = floor(s / TIER_WIDTH) * TIER_WIDTH`. Dropped gear carries the tier of the stage it dropped on. |
| `GEAR_SLOT_G` | 1.0074 | Piece ATK% = `(1.0074^tier - 1) * RARITY_MULT[r]`. Six slots at tier T ~ `1.045^T`. |
| `GEAR_RARITY_W` | C .60 / R .28 / E .09 / L .025 / M .005 | Drop rarity weights. Same shape as gacha, separate RNG stream. |
| `RARITY_MULT` | C 1.0 / R 1.2 / E 1.5 / L 2.0 / M 3.0 | Multiplies the piece's ATK%. |
| `GEAR_SELL_KILLS` | 5 | Auto-replaced piece sells for `5 * GOLD(piece.tier) * RARITY_MULT[r]`; under 3% of kill income. |
| `ENH_MAX` | 15 | Enhancement levels per piece. |
| `ENH_PER_LVL` | 0.05 | Each level adds 5% of the piece's base ATK% (so +75% at 15). |
| `ENH_SUCCESS_FLOOR`, `ENH_SUCCESS_STEP` | 0.30, 0.05 | `ENH_SUCCESS(l) = max(FLOOR, 1 - STEP*l)`: chance to go from level `l` to `l+1`. Fail: cost consumed, nothing else. |
| `ENH_COST_GOLD_MULT` | 20 | `ENH_COST(s) = 20 * GOLD(s)` gold, where `s` is the player's current stage. |

### 2.4 Heroes and gacha

| Name | Value | Notes |
|---|---|---|
| Rates | C .60 / R .28 / E .09 / L .025 / M .005 | Sum 1.0. |
| `PITY_SOFT_START` | 50 | Pulls 1-50 since last L+ use base rate. |
| `PITY_SOFT_STEP` | 0.0325 | L+ rate on pull `n` = `min(1, 0.03 + 0.0325*(n-50))`. |
| `PITY_HARD` | 80 | Forced L+. Effectively never reached (P99 = 62). |
| `M_SHARE_OF_LPLUS` | 1/6 | Given an L+ result, 1/6 are Mythic. |
| 10-pull guarantee | at least one E+ | If the first 9 are below Epic, slot 10 rolls from the E+ sub-distribution. |
| `HERO_SLOTS` | 4 | Party size. Companions in the party contribute; the rest sit in the roster. |
| Companion stats | per companion | HP, ATK, DEF, SPD, crit rate, crit damage, role, element in `heroes.csv` (`specs/11` sections 3-4). Placeholders. |
| `HERO_LVL_CUM` | 1,3,6,10,16,25,40,64,100,160 | Total copies needed for level 1..10. |
| `HERO_LVL_BONUS` | 0.20 | Each level past 1 adds 20% of the companion's base HP/ATK/DEF. Level 10 = 2.8x base. |
| Pool size | C 5 / R 5 / E 4 / L 4 / M 2 | 20 companions (`specs/11` section 4.3). Prototype uses placeholder icons. |
| Shard value of excess copy (P1) | C 1 / R 5 / E 25 / L 100 / M 500 | |

### 2.5 Promotion (replaces class advance)

| Name | Value |
|---|---|
| Promotion unlock stages | 50, 150, 300 |
| Promotion mult | 1.5 per promotion on that companion's HP/ATK/DEF |
| Max promotions | C 0 / R 1 / E 2 / L 3 / M 3 |
| Promotion cost | `300 * GOLD(unlock_stage)` gold (placeholder; dungeon materials in a later phase) |

Details in `specs/11` section 6.

### 2.6a Monster battle (pivot, `specs/16`)

The `MON_*` constants of `specs/16` section 7 (content `2.3`): max HP per HP point, mitigation constant, evade and crit formulas, minimum speed, turn limit, side size. All placeholders. The engine also reads `BATTLE_GAUGE`, `SPD_REF`, the element multipliers, `BASIC_ATTACK_POWER` and the AI thresholds above.

### 2.6b Training run (pivot, `specs/14`)

The run constants of `specs/14` section 8 (content `2.4`): cycles, action points, energy, drill odds, injury, stat cap, fight frequency, plus the `species`, `drill_tiers`, `fight_ranks` and `stat_milestones` tables. All placeholders.

### 2.6c Skill trees (pivot, `specs/15`)

`SP_PER_CYCLE`, `SP_PER_WIN`, `TREE_SLOTS`, `DRAFT_SIZE` and `MON_ECHO_POWER` (content `2.5`), plus the `monster_skills`, `trees`, `tree_nodes`, `chase_conditions` and `skill_books` tables. All placeholders.

### 2.6d Account and meta-progression (pivot, `specs/18`)

`SHARDS_PER_CYCLE`, `SHARDS_PER_RANK` and the `talents` table (content `2.6`). All placeholders.

### 2.6 Session model and targets

Assumed F2P player: 2 h active/day, offline the rest. Targets the bot must land within +/-20%:

| Day | Stage |
|---|---|
| 1 | 90 |
| 3 | 240 |
| 7 | 380 |
| 14 | 480 |
| 30 | 550 |

## 3. The power formula

```
DPS(s) = sum_{party} FarmDps(companion)          # party layer: stats x level x promotion x crit x speed (specs/11 section 7)
       * (1 + STAT_PER_LVL * statLevel)          # gold layer, log-growth in gold
       * prod_{slot} (1 + gearPct_slot * (1 + ENH_PER_LVL * enh_slot))   # gear layer, ~1.045^stage
```

The overhaul (`specs/11`) replaced the fixed `BASE_ATK`, the hero DPS% sum and the class multiplier with the party term. The paragraph below describes the pre-overhaul tuning and is kept as history until balance resumes.

Kill time = `max(ATTACK_INTERVAL, ceil(HP / hitDamage) * ATTACK_INTERVAL) + SPAWN_DELAY`.
Wall condition (boss stage): `BOSS_HP_MULT * HP(s) / DPS(s) > 20 s`, i.e. the player walls when a normal mob takes more than 2 s.

Why these exponents, in one paragraph: HP grows `1.07^s`, gear grows `1.045^s`, so the residual `1.0237^s` has to come from stat levels, heroes and class. Over 550 stages that residual is about 390,000x. The run in `tools/power_curve_model.py` gets there with stats x24, heroes x5.6, class x5.06 at day 30 (~680x) plus gear running a few tiers ahead of the 1.045 line thanks to rarity multipliers. The point of the split: gear carries most of the exponential so drops feel essential, stat levels give smooth minute-to-minute progress, gacha gives the spiky dopamine, class gives identity moments.

## 4. Shared vocabulary

| Term | Meaning |
|---|---|
| **Stage** | Integer `s >= 1`. The player farms exactly one stage at a time. |
| **Tier** | `floor(s/10)*10`. Gear and content are keyed by tier, not stage. |
| **Wall** | Boss stage the player cannot clear in time. Player keeps farming that stage's normal mobs. |
| **Layer** | One multiplicative term in `DPS(s)`. |
| **Sink** | Anything that consumes a currency. |
| **Pull** | One gacha result. A 10-pull is 10 pulls with a batch guarantee. |
| **Hero / Companion** | A named character pulled from the gacha. Has a tier, a stat sheet, a role, an element, a level from dupes and a promotion count. "Companion" is the player-facing word; code says hero. |
| **Party** | The `HERO_SLOTS` companions currently contributing to DPS. "Equip" in code means "put in the party". |
| **Promotion** | A per-companion upgrade unlocked at a stage milestone; multiplies HP/ATK/DEF and adds one kit skill. |
| **Pity counter** | Pulls since the last L+ result on that banner. |
| **Catch-up** | Computing progress for elapsed wall-clock time the sim did not tick through. |
| **Server time** | The only clock the economy trusts. Prototype: `IClock` with a fake implementation. |

## 5. Cross-spec contracts

1. **Sim core is pure C#** (`IdleRng.Sim` assembly). No `UnityEngine` reference, no JSON library, no reflection, no clock reads, no `Math.Pow/Exp/Log` (`specs/03` section 4). The Unity client, the bot harness and the offline catch-up all call the same `Simulation.Tick()` / `Simulation.Advance(ticks)`. Code that needs IO, a real clock or a serializer lives in `IdleRng.Runtime` (plain C#, references `IdleRng.Sim` and Newtonsoft.Json, still no `UnityEngine`), which both the client and the bots reference.
2. **Every RNG consumer gets its own named stream.** Streams: `combat`, `gear_drop`, `gear_rarity`, `enhance`, `gacha_hero`, and `battle` (the 4v1 boss battle, `specs/11` section 8). The monster battle (`specs/16`) runs on a `battle_sim` stream seeded per fight by its caller. A training run (`specs/14`) has its own `training` and `draft` streams and derives fight seeds from its run seed. A stream is `(seed, counter)`; the counter persists in the save. See `specs/03` for the algorithm and `specs/01` for why the gacha stream is pre-committed.
3. **Content is data.** Every number in section 2 lives in a table defined by `specs/05`; code receives a `ContentDb` object and never a literal.
4. **Save schema is versioned from v1** with forward-only migrations (`specs/06`).
5. **Server-sensitive operations go through an interface** (`IGachaService`, `IClock`, `ISaveStore`) with a local implementation in the prototype and a documented remote contract (`specs/08`).
6. **The bot harness is the acceptance test** for any balance change: `specs/09` defines the table it prints and the tolerance.
7. **No competitive surface reads a value the client merely claims.** Anything a leaderboard, PvP match or paid balance depends on is either server-authoritative or server-verified by replaying the client's command log on the same `IdleRng.Sim` source (`specs/10`). The sim therefore records its commands and exposes `StateHash()` (`specs/03` section 22).

## 6. File map

```
idle-rng-game/
  README.md
  specs/
    00-overview.md               this file
    01-gacha-math.md
    02-power-curve-math.md
    03-sim-core-combat.md
    04-economy-sinks.md
    05-content-data-schema.md
    06-save-and-offline.md
    07-client-unity-prototype.md
    08-backend-seams.md
    09-test-harness-and-bots.md
    10-online-competitive.md
    11-party-and-builds.md      overhaul (#60): party, companions, promotion, 4v1 battle
    12-monster-trainer-design.md  pivot target design (not built)
    13-pivot-transition.md      pivot plan: carry-over, phases, open questions
    14-training-run.md          training run: AP cycles, Drill/Rest, rank fights (pivot T2)
    15-skill-trees.md           skill trees: slots, SP, nodes, offers, chase trees (pivot T3)
    18-meta-progression.md      account, shards, talents, run modifiers, account save (pivot T5)
    16-battle.md                monster battle engine: 1-4 vs 1-4, stat sheet, ATB, triggers (pivot T1)
  tools/
    gacha_model.py               exact pity math
    power_curve_model.py         greedy-bot 30-day model
```

Planned source layout (from `specs/03`, `06`, `07`, `09`):

```
src/IdleRng.Sim/            pure sim (state, tick, RNG, economy, gacha algorithm, catch-up arithmetic)
src/IdleRng.Runtime/        serializer + migrations, file save store, LocalClock, FakeClock, SessionRunner
src/IdleRng.Sim.Tests/      
src/IdleRng.Runtime.Tests/  
src/IdleRng.Bots/           headless harness (specs/09)
src/IdleRng.Web/            .NET 8 WebAssembly host: adapter exposing IdleRng.Sim to the browser UI (web/wasm/BRIDGE.md)
unity/                      Unity project; Assets/IdleRng.Client references Sim + Runtime as local packages
Content/                    JSON tables built from the sheet (specs/05)
```

## 7. Decisions made while reconciling specs 03-09

These were open or contradictory across the subagent-written specs and are now settled. Each spec was patched to match; if you find a stale reference, this section wins.

1. **Shared type names.** The save root is `GameState`; the currency bag is `Currencies` (fields `Gold`, `Gems`, `Shards`, P1 `DungeonTickets`, `EnhMaterials`). `specs/03` and `09` originally said `PlayerState`/`Wallet` and were renamed. `Shards` lives on `Currencies`, not on `BannerState`.
2. **Boss daily quest counts attempts, not clears.** `QUEST_BOSS_TARGET` is satisfied by starting a boss fight (automatic first attempt or manual `AttemptBoss`). Reason: with clears, late-game gem income tapers to ~230-250/day on walled days against the 300 target the bot assumes.
3. **Wall-length acceptance (AC4).** Measured in wall-clock hours, threshold 48 h for walls opened in the first 14 days (a wall that spans one night is already 22 h). The harness also reports the longest wall in active hours; if playtests say active hours is the number that matters, switch AC4 to it.
4. **Runtime assembly.** `IdleRng.Sim` stays free of IO, clocks, JSON and reflection. `IdleRng.Runtime` holds `SaveSerializer`, migrations, `LocalSaveStore`, `LocalClock`, `FakeClock`, `SessionRunner`, and file-backed seam implementations. The Unity `SimRunner` MonoBehaviour forwards `Update`/pause to `SessionRunner`; `MAX_TICKS_PER_FRAME = 100` is defined once there.
5. **Curves are evaluated by repeated multiplication**, never `Math.Pow`, in both `ContentDb.CurveDef.Eval` and the sim, so bot output is byte-identical across x64/ARM64/IL2CPP.
6. **Clock going backwards.** `EnsureDay` compares `key <= Daily.DayKey` (not `==`) so a day is never re-granted; offline catch-up credits zero for a negative gap.
7. **Enhancement does not carry** to a replacing piece in P0 (matches the bot). Revisit after the first playtest; full carry is estimated at +30 stages by day 30 and would need a bot rerun.
8. **Constant ownership.** Names introduced by `04`/`05`/`03` are now rows in section 2. Until `specs/05` section 12 is done (Python models reading the JSON), the Python tools remain the place to change a number first.
9. **Hero gacha replaces skill gacha.** The gacha pulls heroes (characters) instead of skills. Every number is unchanged: rates, pity, pool sizes, base DPS% and dupe levels carry over one for one, so `tools/` output and the bot targets are identical. In P0 a hero is a passive party bonus plus a cosmetic sprite on the combat screen (`specs/01` section 7.1); hero roles and active abilities are P1 ideas, not commitments. Identifiers were renamed (`gacha_skill` -> `gacha_hero`, `SKILL_*` -> `HERO_*`, `SkillId` -> `HeroId`, and so on).
10. **Online and competitive play is P1, built on server verification, not a server sim.** Leaderboards, async PvP and IAP move from P2/unscheduled to P1. The engine stays on the device; the server replays segments of the command log to certify progress, owns time, gacha, the gem ledger and receipts, and issues every ranked profile's stream seeds at genesis (`specs/10` sections 2 to 4). A profile created offline is permanently unranked.
11. **Party and builds overhaul (#60).** No player character: the party is 4 companions with their own stat sheets, roles and elements; class advance became per-companion promotion; the pool shrank to 20 companions. Built in phases, numbers are placeholders until the systems are final. `specs/11` is the source of truth for the overhaul and wins over older sections that describe heroes as passive DPS% bonuses.
