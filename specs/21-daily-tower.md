# 21 - Daily Tower

Status: **design, not built (T10 of `specs/13`, P0)**. Nothing here exists in code yet. When a part lands, mark it live in this header and in `specs/13`. Planned code: `src/IdleRng.Sim/Tower` (tower sim), additions to `src/IdleRng.Sim/Combat` (entropy, phases, clock), `AccountOps` and `AccountSave` (attempts, rewards). Planned tests: `TowerTests`, `BattleSim` additions. Planned golden: `golden/tower-vectors.txt`.

The design is `specs/12` sections 8.2 and 8.3 and the Skill Book rules in section 5.6. This spec turns it into rules, data and build steps.

> **Numbers and content are placeholders** until balance resumes (`CLAUDE.md`). Anything marked **(agent)** is a decision made while writing, easy to veto; section 11 lists the questions that are kj's.

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
- **Team.** 1 to 4 distinct Vault entries, in slot order (slot order breaks ATB ties and decides default targeting, `specs/16` section 4). Each fights with `VaultEntry.Fighter(content)`. A monster can be on a team any number of times across attempts and days; towers never consume or injure it. **(agent)** No Mercenaries yet (`specs/12` section 7); see question 1.
- **Replayable.** A result is a pure function of (content, team entries, day, slot order). It is not stored; the client replays it for the animation and a server can recheck it at T12 (`specs/10`).
- **Seeds.** Floor `f` fights on `Derive(seed, f)`. Fights never advance a game stream by an unknown amount (`specs/16` section 2).

## 3. Anti-trivialization (all in `BattleSim`)

Three mechanisms, each a field of `FightRules` and `FightState` (the "future hooks" of `specs/16` section 6.2). They are **off in rank fights and the gauntlet** and on only when the caller passes `TowerRules`, so existing fights, hashes and golden files do not move: the new state is hashed only when `TowerRules` is set, and `HashVersion` stays 1 **(agent)**.

### 3.1 Entropy gauge

Per unit, `0` to `ENTROPY_CAP` (100). It measures how much a build leans on free extra effects.

- **Gain.** `ENTROPY_PER_TRIGGER` for each triggered effect that fires (`specs/16` section 6.1), `ENTROPY_PER_ECHO` for each `echo_cast` repeat, `ENTROPY_PER_COUNTER` for each `counter_mitigated` counterattack, plus any `entropy_gain` effect on the unit (a fraction; negative reduces gain, floor 0).
- **Decay.** `ENTROPY_DECAY` at the start of the unit's own action.
- **Vent.** When a unit's entropy reaches `ENTROPY_CAP`, the excess vents and entropy drops to 0. The vent kind rotates with the unit's vent count, with no random draw: recoil, then stun, then enrage.
  - `recoil`: the unit takes `ENTROPY_RECOIL_FRAC` of its max HP as true damage (a shield absorbs it).
  - `stun`: the unit loses `ENTROPY_STUN_GAUGE` of its ATB gauge (floored at 0).
  - `enrage`: every enemy gains a `+ENTROPY_ENRAGE_FRAC` damage buff for `ENTROPY_ENRAGE_TURNS` turns.
- **Management.** A new rule modifier `entropy_convert` (a chase capstone candidate): instead of venting, the excess becomes a shield of `excess x ENTROPY_CONVERT_SHIELD` of max HP. A node effect `entropy_gain` lowers gain. Both are content, so a build can plan for entropy.
- Bosses use the same gauge only if their row lists loop skills; by default they gain none.
- The UI shows each unit's gauge and the vent that is next.

### 3.2 Boss phases

A boss row has ordered phases. Each is entered once, when the boss's HP first falls to or below its `enter_hp` fraction (checked after every action, highest threshold first, at most one per action). Entering a phase fires a `phase` action in the log. Bosses never get immunities (`specs/12` section 8.3); each phase sets a different **win condition** by one of these coded kinds (the table-row-cannot-express rule of `specs/13` section 7):

| Kind | Effect on entry | What it checks |
|---|---|---|
| `shield_check` | The boss gains a shield of `power` x its max HP for `duration` turns. When the shield expires unbroken, the boss heals `heal_frac` of max HP and gets a damage buff. | Burst: a single-target DPS check |
| `split` | `count` adds join the boss's side (up to `MON_SIDE_SIZE`), built from the `adds` row. While any add lives the boss takes `guard` less damage (not immunity). | AoE and multi-target damage, taunts |
| `channel` | The boss charges for `duration` turns. If the team has not dealt `power` x boss max HP to it since the channel began, it ends by hitting every ally for `strike` x boss `STR` (or `INT`) as true damage. | Interrupt: focus fire in a window |
| `enrage` | From entry the boss gains a stacking `+enrage_per_turn` damage buff each turn, forever. | A survival clock on the last phase |

- A phase may also add skills or triggers to the boss (`add_skills`, ids from `monster_skills`), applied on entry.
- Adds are ordinary fighters with no phase of their own. Killing the boss with adds alive wins the fight (the side's last unit counts only the boss: "boss dead" is the win condition **(agent)**).
- Phases only advance forward. A boss healed above a threshold does not re-enter it.

### 3.3 Environmental clock

Arena corruption: `level = max(0, (tick - CORRUPT_START_TICK) / CORRUPT_EVERY_TICKS)` (integer division). It applies to **both sides**, so it caps stall without favouring the boss:

- Healing and shield amounts are multiplied by `max(CORRUPT_HEAL_FLOOR, 1 - level x CORRUPT_HEAL_PER_LEVEL)`.
- All damage is multiplied by `1 + level x CORRUPT_DMG_PER_LEVEL`.

The floor's `corrupt_start_tick` can override `CORRUPT_START_TICK` (deeper floors start sooner). `MON_TIME_LIMIT_TICKS` still ends the fight; a timeout is a loss for the team (`specs/16` section 10, proposed there). The UI shows the corruption level as a meter.

## 4. Floors and bosses

- `TOWER_FLOORS` floors (placeholder 20). Level: `TOWER_BASE x TOWER_GROWTH^(floor - 1)` by repeated multiplication (no `Math.Pow`).
- A boss's stats are its row's eight weights x the floor level; `hp` is also multiplied by `TOWER_BOSS_HP_MULT`, because four monsters face one boss.
- **Daily lineup.** Every `TOWER_MILESTONE_EVERY`-th floor (5, 10, 15, 20) is a *marquee* boss chosen from the `marquee` rows by the seed; the other floors draw from the `regular` rows. Draws use a `tower` stream seeded from the day seed, without repeats until the pool is used up. Bosses carry their own skills (`skills`, ids from `monster_skills`) and phases.
- Because the lineup changes by day, a team good against one day's bosses is not best on another's: the intended reason to own more than one monster.
- Placeholders: 6 regular and 3 marquee bosses. The draft names are reused where they exist (Apex Chimera, Void Hydra, Storm Titan, `specs/19` section 2); each marquee boss has 2 to 3 phases chosen from section 3.2.

## 5. Content (content `3.5`, MINOR)

New tables (a new table is a MINOR bump, `CLAUDE.md`). Ids are added, never renamed.

| Table | Columns |
|---|---|
| `tower_bosses` | `id`, `name_key`, `element`, `marquee` (bool), eight stat weights, `skills` (ids, `\|`-separated) |
| `boss_phases` | `boss_id`, `order`, `enter_hp`, `kind` (section 3.2), `power`, `duration`, `count`, `guard`, `heal_frac`, `strike`, `enrage_per_turn`, `adds` (a `tower_bosses` id), `add_skills` |
| `book_drops` | `book_id`, `min_floor`, `weight` |

Strings for names, phase banners and tooltips (`rule.entropy_convert.desc`, `phase.<kind>.name`, `phase.<kind>.desc`, `stat.entropy_gain.*`), with constants filled in as in `specs/16` section 9. `skill_books` gains no column.

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
| `ENTROPY_CONVERT_SHIELD` | 0.5 | `>= 0` |
| `CORRUPT_START_TICK` | 300 | `>= 0` |
| `CORRUPT_EVERY_TICKS` | 100 | `> 0` |
| `CORRUPT_HEAL_PER_LEVEL` / `_FLOOR` | 0.2 / 0.2 | `>= 0` / `[0, 1]` |
| `CORRUPT_DMG_PER_LEVEL` | 0.1 | `>= 0` |
| `TOWER_SHARDS_PER_FLOOR` | 4 | `>= 0` |
| `TOWER_BOOK_CHANCE` | 0.15 | `[0, 1]` |
| `TOWER_BOOK_CHANCE_PER_FLOOR` | 0.01 | `>= 0` |

Validation: every `skills` and `add_skills` id exists (`E023`); `adds` is a non-marquee boss with no phases of its own; at least one regular and one marquee boss (`E044`); phases of a boss have strictly decreasing `enter_hp` in `(0, 1)` (`E032`); `book_drops.book_id` exists; `min_floor` in `[1, TOWER_FLOORS]`. A new error code range is assigned when building (`E094` onward).

## 6. API

```csharp
TowerResult r = TowerSim.Run(content, team /* IReadOnlyList<Fighter> */, day, onAction, onFloorStart);
// r.FloorReached, r.Cleared, r.Floors[]: boss id, won, ticks, phases entered, vents, peak corruption, fight hash
// r.Hash: SHA-256 over all of it (HashVersion 1)
TowerLineup l = TowerSim.Lineup(content, day);          // bosses and phases per floor, for the preview screen
```

`FightRules.FromContent(content, tower: true)` carries the three mechanisms; `BattleSim.Start` validates the new fields (known phase kinds, `adds` within the side-size cap). `FightAction` gains `Phase`, `Vent` and `Corruption` fields so playback can show banners, entropy bars and the corruption meter; observers only read, as before.

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
- **Skill Books:** on each floor cleared, one roll with chance `TOWER_BOOK_CHANCE + TOWER_BOOK_CHANCE_PER_FLOOR x (floor - 1)`; on a hit, the book is drawn from `book_drops` rows with `min_floor <= floor`, by weight, so rarer books sit deeper. The roll's seed is `Derive(StreamSeed(seed, "drop"), floor)`: fixed by (day, floor), so a retry cannot re-roll a floor and the books are reproducible. Granted through `GrantBooks`.
- **Star-Gems:** the first clear ever of a floor (above `BestFloorEver`) and each `TOWER_MILESTONE_EVERY` floor cleared in a day pay gems **once T9 adds `AccountState.Gems`** (`specs/18` section 1). Until then the tower pays shards and books only. Amounts are decided with the gacha economy (question 5).

The test button that grants books (`specs/13` T3) stays as a debug-only control, hidden behind the debug flag, once the tower grants them **(agent)**.

The day number is read from the device clock by the caller. The sim cannot tell if the clock is wrong, so a player who moves it forward can claim more days. Accepted while the game is single-player and offline-first; the server-checked day arrives with T12 (`specs/10`) **(agent)**.

## 8. Web client

A **Tower** tab beside the Vault (`web/`), all through the bridge (`web/wasm/BRIDGE.md`; new `Tower*` exports, nothing computed in JS):

- Today's lineup: floor list with each boss's name, element and phase badges, the milestone floors marked, attempts left and today's best floor.
- Team picker: choose 1 to 4 Vault monsters from a list with the Vault's sort, filters and search (`specs/18` section 8); slot order is shown and reorderable.
- Playback: per floor, the existing action-by-action fight view (`specs/13` T6; 1x, 2x or Skip) with phase banners, each unit's entropy gauge and next vent, and the corruption meter. A summary at the end: floor reached, floors cleared, rewards paid (shards, books, gems when present).
- A tooltip for every mechanism, built from the same data as the fight (`BattleText`, `specs/16` section 9).

## 9. Determinism and tests

- No clock, IO, reflection or `Math.Pow`; every draw comes from a named stream (`specs/00` section 5). New streams: `tower` (lineup) and the per-floor `drop` seed.
- `TowerTests`: the same (team, day) gives the same result and hash; different days differ; slot order matters; each phase kind changes the fight as its row says and fires once; entropy gains, decays, vents in the fixed rotation and `entropy_convert` shields instead; corruption level and its multipliers; rank fights and the gauntlet are unchanged with the tower rules off (their goldens do not move); drops are fixed by (day, floor) and a retry pays nothing twice; attempts cap; a new day resets; account save round-trips and a v2 save migrates.
- `golden/tower-vectors.txt`: each species after a `focus` bot run at seed 7 and the four together, on two fixed days: floor, cleared, phases, vents, hash. Regenerated through the bot CLI (`golden --update`), never by hand. Bots get a `tower` verb (`specs/09`).
- No balance harness: whether floors are fair is balance work and waits (`CLAUDE.md`).

## 10. Build order

Each step merges on its own with `./check.sh` green.

1. **Engine hooks** (`BattleSim`): `TowerRules`, entropy, the four phase kinds, the clock, tests. Rank fights and the gauntlet unchanged.
2. **Tower sim and content:** the three tables, constants, validators in both `content_build.py` and `ContentDb`, `TowerSim`, goldens.
3. **Account:** `AccountState.Tower`, `RunTower`, rewards, save schema v3, tests.
4. **Web:** the Tower tab, the bridge, tooltips, the debug-only book button. Publish the artifact (`CLAUDE.md`).

## 11. Questions for kj

Recommendations first. Questions 1 and 2 block step 2; the rest can wait.

1. **Team rule.** Teams of 1 to 4 Vault monsters, no Mercenaries (recommended: simplest, and a new player with one monster can climb a few floors). Alternative: build the Mercenaries of `specs/12` section 7 so a team is always four.
2. **Floor model.** Full heal each floor, 3 attempts a day (recommended: fresh fights, easy to reason about). Alternative: HP carries over, so healing and shields decide depth.
3. **A practice mode** (an undated tower with no rewards, always available) so there is something to try with a new team after the day's attempts are spent? Recommended: yes, later; it is the same sim with a free seed.
4. **Entropy on bosses.** By default bosses build none. Should marquee bosses have entropy so they can be stunned or enraged on purpose? Recommended: no for the MVP.
5. **Gem amounts and book rarity** wait for the T9 economy and the monetization call (`specs/13` section 9 item 4). The placeholders here only prove the pipeline.
6. **Daily reset time.** UTC midnight (recommended) or the player's local midnight.
