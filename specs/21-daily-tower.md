# 21 - Daily Tower

Status: **in progress (T10 of `specs/13`, P0)**. Build steps 1 and 2 are **live** (section 10): the battle hooks of section 3 (`src/IdleRng.Sim/Combat`, `TowerBattleTests`), and the tower sim and content of sections 2, 4 and 5 (`src/IdleRng.Sim/Tower/TowerSim.cs`, `TowerTests`, golden `golden/tower-vectors.txt`). Steps 3 (account and rewards, section 7) and 4 (web, section 8) are design only.

The design is `specs/12` sections 8.2 and 8.3 and the Skill Book rules in section 5.6. This spec turns it into rules, data and build steps.

> **Numbers and content are placeholders** until balance resumes (`CLAUDE.md`). Anything marked **(agent)** is a decision made while writing, easy to veto; section 11 lists the questions that are kj's and the answers so far.

## 1. What it is

A 4v1 boss tower the player climbs once a day (a few attempts) with a team of Vault monsters. The team is the only choice: the fight is automatic, like every other battle. Its jobs, from `specs/12`:

- Make Vault monsters useful after their run (until now the Vault feeds only the gauntlet and dismantling).
- Be the source of **Skill Books** (`specs/15` section 4), replacing the test button that grants books today.
- Check broken builds by structure, not by immunities: the entropy gauge, boss phases and the environmental clock (`specs/12` section 8.3).
- Give a daily reason to open the game.

## 2. Flow

```
day  = UTC day number, passed in by the caller (the sim has no clock)
seed = Derive(StreamSeed(TOWER_SEED, "tower"), day)       same for every player on a day
lineup(day) = TOWER_FLOORS floors, each a boss picked from the pool by the seed (section 4)

attempt (team of 1..4 Vault monsters):
    for floor 1..TOWER_FLOORS:
        fight team vs the floor's boss (BattleSim, tower rules, section 3)  - team at full HP **(agent)**
        win  -> floor cleared;  loss or timeout -> attempt ends, FloorReached = floor
    all cleared -> FloorReached = TOWER_FLOORS + 1
pay rewards (section 7)
```

- **(agent)** The team starts every floor at full HP: each floor is a fresh fight, as in the gauntlet (`specs/19` section 1), so the result is "the first boss this team cannot beat". Carrying HP over would make healing and shields the whole game; easy to change.
- **Attempts.** `TOWER_ATTEMPTS_PER_DAY` per day (placeholder 3). The best floor of the day counts for rewards; rewards are claimed per floor once per day (section 7), so a retry can only improve.
- **Team.** 1 to 4 distinct Vault entries, in slot order (slot order breaks ATB ties and decides default targeting, `specs/16` section 4). Each fights with `VaultEntry.Fighter(content)`; every vaulted monster has the fighter id `monster`, so `TowerSim` renames them `t<slot>_monster` for the fight. A monster can be on a team any number of times across attempts and days; towers never consume or injure it. **(agent)** No Mercenaries yet (`specs/12` section 7); see question 1.
- **Replayable.** A result is a pure function of (content, team entries, day, slot order). It is not stored; the client replays it for the animation and a server can recheck it at T12 (`specs/10`).
- **Seeds.** Floor `f` fights on `Derive(StreamSeed(seed, "tower_fight"), f)`, so a fight never shares draws with the lineup or the drops (`specs/16` section 2).

## 3. Anti-trivialization (all in `BattleSim`)

Three mechanisms, each a field of `FightRules` and `FightState` (the "future hooks" of `specs/16` section 6.2). They are **off in rank fights and the gauntlet** and on only when the caller sets `FightRules.Tower` (a `TowerRules`), so existing fights, hashes and golden files do not move: the new state is hashed only when `Tower` is set (`FightState.Tower`), and `HashVersion` stays 1 **(agent)**. Boss phases need `Tower`: `BattleSim.Start` rejects a fighter with phases when it is null. `TowerRules` is plain data like `FightRules`; building it from content constants comes with step 2.

### 3.1 Entropy gauge

Per unit, `0` to `ENTROPY_CAP` (100). It measures how much a build leans on free extra effects.

- **Gain.** `ENTROPY_PER_TRIGGER` for each triggered effect that fires (`specs/16` section 6.1) **except `BattleStart` passives**, which would start every unit with a build-up **(agent)**; `ENTROPY_PER_ECHO` for each `echo_cast` repeat; `ENTROPY_PER_COUNTER` for each `counter_mitigated` counterattack. Gain is scaled by `max(0, 1 + sum of entropy_gain effects)`: a new bonus-only effect stat, so a debuff or node that lowers it cuts gain, down to nothing.
- **Decay.** `ENTROPY_DECAY` at the start of the unit's own action.
- **Vent.** When a unit's entropy reaches `ENTROPY_CAP`, the gauge vents and drops to 0 (the vent has a fixed size; it does not scale with the overshoot **(agent)**). The vent kind rotates with the unit's vent count, with no random draw: recoil, then stun, then enrage.
  - `recoil`: the unit takes `ENTROPY_RECOIL_FRAC` of its max HP as true damage (a shield absorbs it).
  - `stun`: the unit loses `ENTROPY_STUN_GAUGE` of its ATB gauge (floored at 0).
  - `enrage`: every enemy gains a `+ENTROPY_ENRAGE_FRAC` damage buff for `ENTROPY_ENRAGE_TURNS` turns. The buff is the new bonus-only effect stat `dmg_dealt`: damage before mitigation is multiplied by `max(0, 1 + sum of dmg_dealt effects)`. Boss phases use it too.
- **Management.** A new rule modifier `entropy_convert` (a chase capstone candidate): instead of venting, the unit gains a shield of `ENTROPY_CONVERT_SHIELD` of its max HP for `ENTROPY_CONVERT_TURNS` turns. A node effect on `entropy_gain` lowers gain. Both are content, so a build can plan for entropy.
- Bosses use the same gauge only if their row lists loop skills; by default they gain none.
- The UI shows each unit's gauge and the vent that is next.

### 3.2 Boss phases

A boss row has ordered phases. Each is entered once, when the boss's HP first falls to or below its `enter_hp` fraction (checked after every action, highest threshold first, at most one per action). Entering a phase fires a `phase` action in the log. Bosses never get immunities (`specs/12` section 8.3); each phase sets a different **win condition** by one of these coded kinds (the table-row-cannot-express rule of `specs/13` section 7):

| Kind | Effect on entry | What it checks |
|---|---|---|
| `shield_check` | The boss gains a shield of `power` x its max HP for `duration` turns (replacing any shield it had). When the shield expires unbroken (checked at the end of each tick), the boss heals `heal_frac` of max HP and gets a permanent `buff` damage buff. | Burst: a single-target DPS check |
| `split` | `count` adds join the boss's side (up to `MON_SIDE_SIZE`), built from the `adds` row. While any add lives the boss takes `guard` less damage (not immunity). | AoE and multi-target damage, taunts |
| `channel` | The boss charges for `duration` turns. If the team has not dealt `power` x boss max HP to it (shield absorption counts) since the channel began, it ends by hitting every enemy for `strike` x the boss's `STR` or `INT` (the higher) as true damage (shields still absorb). Interrupted, it stops and strikes nothing. | Interrupt: focus fire in a window |
| `enrage` | From entry the boss's damage buff grows by `buff` every turn, forever (one permanent effect). | A survival clock on the last phase |

- A phase may also add skills to the boss (`add_skills`, ids from `monster_skills`, passives included) on entry; they go after its own, so existing cooldowns are kept, and passives apply at once. The fighter snapshot is not changed: the unit gets a copy.
- Adds are ordinary fighters with no phase of their own, named `<boss id>#<n>`. They never keep a side alive: killing the boss with adds still up wins the fight **(agent)**. A channel strike that wipes the team ends the fight, the boss's side winning.
- Phases only advance forward. A boss healed above a threshold does not re-enter it.

### 3.3 Environmental clock

Arena corruption: `level = max(0, (tick - CORRUPT_START_TICK) / CORRUPT_EVERY_TICKS)` (integer division). It applies to **both sides**, so it caps stall without favouring the boss:

- Healing and shield amounts are multiplied by `max(CORRUPT_HEAL_FLOOR, 1 - level x CORRUPT_HEAL_PER_LEVEL)`.
- All damage is multiplied by `1 + level x CORRUPT_DMG_PER_LEVEL`.

Every floor uses the same `CORRUPT_START_TICK` **(agent: the per-floor override first planned here was dropped; floors are generated, so there is no floor row to hold it)**. `MON_TIME_LIMIT_TICKS` still ends the fight; a timeout is a loss for the team (`specs/16` section 10, proposed there). The UI shows the corruption level as a meter.

## 4. Floors and bosses

- `TOWER_FLOORS` floors (placeholder 20). Level: `TOWER_BASE x TOWER_GROWTH^(floor - 1)` by repeated multiplication (no `Math.Pow`).
- A boss's stats are its row's eight weights x the floor level; `hp` is also multiplied by `TOWER_BOSS_HP_MULT`, because four monsters face one boss.
- **Daily lineup.** Every `TOWER_MILESTONE_EVERY`-th floor (5, 10, 15, 20) is a *marquee* boss chosen from the `marquee` rows by the seed; the other floors draw from the `regular` rows. Draws use a `tower` stream seeded with the day seed, one draw per floor in floor order, without repeats until the pool is used up (it then refills in CSV order). `add` rows are never drawn: they only spawn from `split` phases, at the floor's level and without the boss HP multiplier. Bosses carry their own skills (`skills`, ids from `monster_skills`, in AI priority order) and phases.
- Because the lineup changes by day, a team good against one day's bosses is not best on another's: the intended reason to own more than one monster.
- Placeholders: 6 regular, 3 marquee and 2 add rows. The draft names are reused where they exist (Apex Chimera, Void Hydra, Storm Titan, `specs/19` section 2); each marquee boss has 2 to 3 phases covering all four kinds between them. Two boss skills were added to `monster_skills`: `boss_cleave` (hit every enemy) and `boss_roar` (lower every enemy's DEF); the rest reuse tree skills.

## 5. Content (content `3.6`, MINOR)

New tables (a new table is a MINOR bump, `CLAUDE.md`). Ids are added, never renamed.

| Table | Columns |
|---|---|
| `tower_bosses` | `id`, `name_key`, `element`, `role` (`regular`, `marquee` or `add`), eight stat weights, `skills` (ids, `\|`-separated, may be empty) |
| `boss_phases` | `id`, `boss_id`, `order`, `enter_hp`, `kind` (section 3.2), `power`, `duration`, `count`, `guard`, `heal_frac`, `strike`, `buff`, `adds` (a `tower_bosses` id of role `add`, or empty), `add_skills` (ids, `\|`-separated). Columns a kind does not use are 0 or empty |
| `book_drops` | `book_id` (the key), `min_floor`, `weight` |

Strings: boss names (`tower.<id>.name`), `rule.entropy_convert.*` and the phase banners and tooltips (`phase.<kind>.name`, `phase.<kind>.desc`), with constants filled in as in `specs/16` section 9. `stat.dmg_dealt.*` and `stat.entropy_gain.*` come when content first uses those stats in a node. `skill_books` gains no column.

Constants (all placeholders; range violations are `E043` in both `tools/content_build.py` and `ContentDb`, as in `specs/16`):

| Name | Value | Range |
|---|---|---|
| `TOWER_SEED` | 20260101 | whole number |
| `TOWER_FLOORS` | 20 | `> 0` |
| `TOWER_ATTEMPTS_PER_DAY` | 3 | `> 0` |
| `TOWER_BASE` | 80 | `> 0` |
| `TOWER_GROWTH` | 1.06 | `>= 1` |
| `TOWER_BOSS_HP_MULT` | 4 | `> 0` |
| `TOWER_MILESTONE_EVERY` | 5 | `> 0` |
| `ENTROPY_CAP` | 100 | `> 0` |
| `ENTROPY_PER_TRIGGER` / `_ECHO` / `_COUNTER` | 5 / 10 / 15 | `>= 0` |
| `ENTROPY_DECAY` | 3 | `>= 0` |
| `ENTROPY_RECOIL_FRAC` | 0.15 | `[0, 1]` |
| `ENTROPY_STUN_GAUGE` | 100 | `>= 0` |
| `ENTROPY_ENRAGE_FRAC` / `_TURNS` | 0.25 / 3 | `>= 0` / `> 0` |
| `ENTROPY_CONVERT_SHIELD` | 0.2 | `>= 0` |
| `ENTROPY_CONVERT_TURNS` | 3 | `> 0` |
| `CORRUPT_START_TICK` | 300 | `>= 0` |
| `CORRUPT_EVERY_TICKS` | 100 | `> 0` |
| `CORRUPT_HEAL_PER_LEVEL` / `_FLOOR` | 0.2 / 0.2 | `>= 0` / `[0, 1]` |
| `CORRUPT_DMG_PER_LEVEL` | 0.1 | `>= 0` |
| `TOWER_SHARDS_PER_FLOOR` | 4 | `>= 0` |
| `TOWER_BOOK_CHANCE` | 0.15 | `[0, 1]` |
| `TOWER_BOOK_CHANCE_PER_FLOOR` | 0.01 | `>= 0` |

Validation, in both `tools/content_build.py` (`validate_tower`) and `ContentDb.BuildTower`, reusing the existing codes **(agent: no new code range was needed)**: element, `skills`, `add_skills`, `boss_id`, `adds` (role `add`) and `book_id` references exist (`E023`); at least one regular and one marquee boss, an add has no phases, `book_drops` has at least one row and one with `min_floor` 1 (`E044`); each kind's values are in range, as `BattleSim.Start` checks them (`E043`); a boss's phases, by `order`, have strictly decreasing `enter_hp` in `(0, 1)` (`E032`); `min_floor` in `[1, TOWER_FLOORS]` and `weight > 0` (`E043`); the constants' ranges above (`E043`).

## 6. API

```csharp
TowerResult r = TowerSim.Run(content, team /* IReadOnlyList<Fighter> */, day, onAction, onFloorStart);
// r.FloorReached, r.Cleared, r.FloorsCleared, r.Floors[]: boss id, marquee, won, ticks, phases entered, vents, peak corruption, fight hash
// r.Hash: SHA-256 over all of it (HashVersion 1)
List<TowerFloorPlan> l = TowerSim.Lineup(content, day);   // floor, boss id, marquee, level: the preview screen
Fighter boss = TowerSim.Boss(content, bossId, level);     // a boss with its skills and phases (adds built at the same level)
string book = TowerSim.BookDrop(content, day, floor);     // the floor's drop that day, or null (section 7)
```

`FightRules.FromContent(content, tower: true)` carries the three mechanisms; `BattleSim.Start` validates the new fields (known phase kinds, `adds` within the side-size cap). `FightAction` gains one field, `Event` (null for an ordinary action): `phase:<kind>`, `vent:recoil|stun|enrage|convert`, `check_failed`, `channel_broken`, `channel_strike`, with the unit in `Actor` and the amount where there is one. Playback also reads `FightUnit.Entropy`, `FightState.PeakCorruption` and `BattleSim.CorruptionLevel(rules, ticks)` from the live state; observers only read, as before.

## 7. Account and rewards

`AccountState.Tower` (new, saved; account save schema v3, a v2 save migrates with an empty tower):

| Field | Meaning |
|---|---|
| `Day` | The last day an attempt was made; a new day resets the three below |
| `AttemptsUsed` | This day's attempts |
| `BestFloorToday` | Best floor reached today |
| `ClaimedToday` | Highest floor whose rewards were already paid today |
| `BestFloorEver` | Best floor ever reached (decides first clears) |

`AccountOps.RunTower(teamIndices, day)` (rejected commands throw and change nothing): requires `AttemptsUsed < TOWER_ATTEMPTS_PER_DAY`, and 1 to 4 distinct valid Vault indices. A run in progress does not block it: a tower attempt never touches the active run. It runs `TowerSim`, counts the attempt, then pays for each floor **above** `ClaimedToday` that was cleared:

- **Shards:** `TOWER_SHARDS_PER_FLOOR` per floor.
- **Skill Books:** on each floor cleared, one roll with chance `TOWER_BOOK_CHANCE + TOWER_BOOK_CHANCE_PER_FLOOR x (floor - 1)`; on a hit, the book is drawn from `book_drops` rows with `min_floor <= floor`, by weight, so rarer books sit deeper. The roll's seed is `Derive(StreamSeed(seed, "drop"), floor)` (`TowerSim.BookDrop`, built with step 2): fixed by (day, floor), so a retry cannot re-roll a floor and the books are reproducible. Granted through `GrantBooks`.
- **Star-Gems:** the first clear ever of a floor (above `BestFloorEver`) and each `TOWER_MILESTONE_EVERY` floor cleared in a day pay gems **once Star-Gems exist**: T9 scrapped the species gacha and added no gems, so what gems buy is open (#49, #77; `specs/18` section 1). Until then the tower pays shards and books only. Amounts are decided with the gacha economy (question 5).

The test button that grants books (`specs/13` T3) stays as a debug-only control, hidden behind the debug flag, once the tower grants them **(agent)**.

The day number is read from the device clock by the caller. The sim cannot tell if the clock is wrong, so a player who moves it forward can claim more days. Accepted while the game is single-player and offline-first; the server-checked day arrives with T12 (`specs/10`) **(agent)**.

## 8. Web client

A **Tower** tab beside the Vault (`web/`), all through the bridge (`web/wasm/BRIDGE.md`; new `Tower*` exports, nothing computed in JS):

- Today's lineup: floor list with each boss's name, element and phase badges, the milestone floors marked, attempts left and today's best floor.
- Team picker: choose 1 to 4 Vault monsters from a list with the Vault's sort, filters and search (`specs/18` section 9); slot order is shown and reorderable.
- Playback: per floor, the existing action-by-action fight view (`specs/13` T6; 1x, 2x or Skip) with phase banners, each unit's entropy gauge and next vent, and the corruption meter. A summary at the end: floor reached, floors cleared, rewards paid (shards, books, gems when present).
- A tooltip for every mechanism, built from the same data as the fight (`BattleText`, `specs/16` section 9).

## 9. Determinism and tests

- No clock, IO, reflection or `Math.Pow`; every draw comes from a named stream (`specs/00` section 5). New streams: `tower` (lineup), and the per-floor `tower_fight` and `drop` seeds.
- `TowerBattleTests` (step 1): each phase kind changes the fight as its row says and fires once; entropy gains, decays, vents in the fixed rotation and `entropy_convert` shields instead; corruption level and its multipliers; tower rules off leave fights unchanged.
- `TowerTests` (step 2): content loads; the lineup is fixed for a day, differs across days, puts marquees on milestones and uses the whole regular pool before repeating; boss scaling (adds skip the HP multiplier); the same (team, day) gives the same result and hash, different days differ, slot order matters, duplicate monster ids are fine; bad teams are rejected; a strong team clears and sees phases on every marquee floor; observers change nothing; drops are fixed by (day, floor), only eligible books drop, and the rate follows the constants.
- Step 3 adds: attempts cap; a new day resets; a retry pays nothing twice; account save round-trips and a v2 save migrates.
- `golden/tower-vectors.txt` (`TowerVectors`): each species after a `focus` bot run at seed 7, alone and up to four together, on days 0 and 20391: the lineup, then floor, cleared, vents, phases, hash. Regenerated through the bot CLI (`golden --update`), never by hand. **(agent)** No `tower` CLI verb yet; add one with the balance harness (`specs/09` section 7).
- No balance harness: whether floors are fair is balance work and waits (`CLAUDE.md`).

## 10. Build order

Each step merges on its own with `./check.sh` green.

1. **Engine hooks** (`BattleSim`): `TowerRules`, entropy, the four phase kinds, the clock, tests. Rank fights and the gauntlet unchanged. **Done** (`TowerBattleTests`; no golden moved).
2. **Tower sim and content:** the three tables, constants, validators in both `content_build.py` and `ContentDb`, `TowerSim`, goldens. **Done** (content 3.6, `TowerTests`, `golden/tower-vectors.txt`).
3. **Account:** `AccountState.Tower`, `RunTower`, rewards, save schema v3, tests.
4. **Web:** the Tower tab, the bridge, tooltips, the debug-only book button. Publish the artifact (`CLAUDE.md`).

## 11. Questions for kj

Recommendations first. **Answered (kj, 2026-10-31):** questions 1 and 2. The rest can wait.

1. **Team rule. Answered:** teams of 1 to 4 Vault monsters, no Mercenaries. (Alternative not taken: build the Mercenaries of `specs/12` section 7 so a team is always four.)
2. **Floor model. Answered:** full heal each floor, 3 attempts a day. (Alternative not taken: HP carries over.)
3. **A practice mode** (an undated tower with no rewards, always available) so there is something to try with a new team after the day's attempts are spent? Recommended: yes, later; it is the same sim with a free seed.
4. **Entropy on bosses.** By default bosses build none. Should marquee bosses have entropy so they can be stunned or enraged on purpose? Recommended: no for the MVP.
5. **Gems and book rarity** wait for the monetization call (`specs/13` section 9 item 4, R1.9 in `specs/20`): T9 scrapped the gacha, so there are no gems yet, and Skill Books are the premium gate. The placeholders here only prove the pipeline.
6. **Daily reset time.** UTC midnight (recommended) or the player's local midnight.
