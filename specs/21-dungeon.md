# 21 - Dungeon

Built as the **Daily Tower**; renamed the **Dungeon** with tickets by kj (2026-10-31): one free ticket a day, more ground from training runs, and every ticket pays in full (section 7). Code and content ids keep the `tower` name (`TowerSim`, `TOWER_*`, `AccountOps.RunTower`, `tower_bosses`, `tower.*` strings): only player-facing text says Dungeon **(agent: renaming ids would need a MAJOR content bump and a save migration for no player-visible gain)**.

Status: **live (T10 of `specs/13`, P0)**, all four build steps (section 10): the battle hooks of section 3 (`src/IdleRng.Sim/Combat`, `TowerBattleTests`); the tower sim and content of sections 2, 4 and 5 (`src/IdleRng.Sim/Tower/TowerSim.cs`, `TowerTests`, golden `golden/tower-vectors.txt`); attempts and rewards on the account, section 7 (`AccountOps.RunTower`, `TowerAccountTests`, account save v4); the web screen with floor playback, section 8 (`web/run.js`, browser test in `web/wasm/test/run-trainer.js`).

The design is `specs/12` sections 8.2 and 8.3 and the Skill Book rules in section 5.6. This spec turns it into rules, data and build steps.

> **Numbers and content are placeholders** until balance resumes (`CLAUDE.md`). Anything marked **(agent)** is a decision made while writing, easy to veto; section 11 lists the questions that are kj's and the answers so far.

## 1. What it is

A 4v1 boss dungeon the player clears floor by floor with a team of Vault monsters, one ticket per attempt. The team is the only choice: the fight is automatic, like every other battle. Its jobs, from `specs/12`:

- Make Vault monsters useful after their run (until now the Vault feeds only the gauntlet and dismantling).
- Be the source of **Skill Books** (`specs/15` section 4), replacing the test button that grants books today.
- Check broken builds by structure, not by immunities: the entropy gauge, boss phases and the environmental clock (`specs/12` section 8.3).
- Give a daily reason to open the game (the free ticket and the day's lineup), and a reason to keep training (ticket pieces).

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
- **Tickets.** Each attempt spends one ticket. `TOWER_ATTEMPTS_PER_DAY` free tickets a day (1), banked up to `TOWER_TICKET_CAP`; sealed runs earn ticket pieces by grade (section 7). Every attempt pays every floor it clears (kj, 2026-10-31).
- **Team.** 1 to 4 distinct Vault entries, in slot order (slot order breaks ATB ties and decides default targeting, `specs/16` section 4). Each fights with `AccountOps.TeamFighter` (`VaultEntry.Fighter(content)` plus the monster's gambit rulebook, if it has one: one rulebook per monster, used in every team mode, `specs/28` section 4.4); every vaulted monster has the fighter id `monster`, so `TowerSim` renames them `t<slot>_monster` for the fight. A monster can be on a team any number of times across attempts and days; towers never consume or injure it. **(agent)** No Mercenaries yet (`specs/12` section 7); see question 1.
- **Replayable.** A result is a pure function of (content, team entries with their rulebooks, day, slot order). It is not stored; the client replays it for the animation and a server can recheck it at T12 (`specs/10`).
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
  - `enrage`: every enemy gains a `+ENTROPY_ENRAGE_FRAC` damage buff for `ENTROPY_ENRAGE_TURNS` turns. The buff is the new bonus-only effect stat `dmg_dealt`: damage before mitigation is multiplied by `max(0, 1 + sum of dmg_dealt effects)`. Boss phases use it too. A unit carries at most `EFFECT_STACK_CAP` enrage buffs from vents at once (`specs/16` section 3).
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

Content 3.16 (MINOR) added the ticket constants and the `ticket_pieces` column of `fight_ranks` (`>= 0`, `E043` in both validators): pieces a sealed run of that grade earns, placeholder E 0, D 1, C 1, B 2, A 3, S 4.

Strings: boss names (`tower.<id>.name`), `rule.entropy_convert.*` and the phase banners and tooltips (`phase.<kind>.name`, `phase.<kind>.desc`), with constants filled in as in `specs/16` section 9. Content 3.10 added the playback tooltips (section 8): `tower.entropy.*`, `tower.corruption.*` and `vent.<recoil|stun|enrage>.*`, `vent.convert.name`. `stat.dmg_dealt.*` and `stat.entropy_gain.*` come when content first uses those stats in a node. `skill_books` gains no column.

Constants (all placeholders; range violations are `E043` in both `tools/content_build.py` and `ContentDb`, as in `specs/16`):

| Name | Value | Range |
|---|---|---|
| `TOWER_SEED` | 20260101 | whole number |
| `TOWER_FLOORS` | 20 | `> 0` |
| `TOWER_ATTEMPTS_PER_DAY` (free tickets a day; the name is kept from the attempt cap it replaced) | 1 | `> 0` |
| `TOWER_TICKET_CAP` | 5 | `> 0` |
| `TOWER_TICKET_PIECES` | 4 | `> 0` |
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
| `TOWER_BOOK_CHANCE` | 0.0005 | `[0, 1]` |
| `TOWER_BOOK_CHANCE_PER_FLOOR` | 0.0003 | `>= 0` |

**Skill Books are uber rare (kj, 2026-10-31, issue #112).** Content 3.14 cut the drop chance from 0.15 + 0.01 per floor (3 to 5 books a day for a good team) to 0.0005 + 0.0003 per floor. A full 20-floor clear then drops about 0.067 books, **about one book per 15 full clears** (one ticket each since 3.16), and a given species' own book about one in 45 **(agent: the numbers; kj asked for "uber rare")**. Content 4.2 added two more books (`book_roots`, `book_shadows`, weight 1 from floor 10), so each species' own book is rarer than that now; the weight-1 books most of all. The debug book button (section 8) stays for testing.

Validation, in both `tools/content_build.py` (`validate_tower`) and `ContentDb.BuildTower`, reusing the existing codes **(agent: no new code range was needed)**: element, `skills`, `add_skills`, `boss_id`, `adds` (role `add`) and `book_id` references exist (`E023`); at least one regular and one marquee boss, an add has no phases, `book_drops` has at least one row and one with `min_floor` 1 (`E044`); each kind's values are in range, as `BattleSim.Start` checks them (`E043`); a boss's phases, by `order`, have strictly decreasing `enter_hp` in `(0, 1)` (`E032`); `min_floor` in `[1, TOWER_FLOORS]` and `weight > 0` (`E043`); the constants' ranges above (`E043`).

## 6. API

```csharp
TowerResult r = TowerSim.Run(content, team /* IReadOnlyList<Fighter> */, day, onAction, onFloorStart);
// r.FloorReached, r.Cleared, r.FloorsCleared, r.Floors[]: boss id, marquee, won, ticks, phases entered, vents, peak corruption, fight hash
// r.Hash: SHA-256 over all of it (HashVersion 1)
List<TowerFloorPlan> l = TowerSim.Lineup(content, day);   // floor, boss id, marquee, level: the preview screen
Fighter boss = TowerSim.Boss(content, bossId, level);     // a boss with its skills and phases (adds built at the same level)
string book = TowerSim.BookDrop(content, day, floor, attempt); // the floor's drop on the day's attempt-th attempt (0 first), or null (section 7)
```

`FightRules.FromContent(content, tower: true)` carries the three mechanisms; `BattleSim.Start` validates the new fields (known phase kinds, `adds` within the side-size cap). `FightAction` gains one field, `Event` (null for an ordinary action): `phase:<kind>`, `vent:recoil|stun|enrage|convert`, `check_failed`, `channel_broken`, `channel_strike`, with the unit in `Actor` and the amount where there is one. Playback also reads `FightUnit.Entropy`, `BattleSim.NextVent(unit)` (the vent kind the gauge fires next: convert with `entropy_convert`, else the rotation by vent count; `GainEntropy` uses the same function), `FightState.PeakCorruption` and `BattleSim.CorruptionLevel(rules, ticks)` from the live state; observers only read, as before.

## 7. Account and rewards

`AccountState.Tower` (`TowerProgress`, saved; account save schema v4 added it, v5 the tickets; an older save migrates with no progress and no tickets). Floors are counted as floors cleared:

| Field | Meaning |
|---|---|
| `Day` | UTC day of the last attempt, -1 before the first; a later day resets the next two |
| `AttemptsUsed` | That day's attempts; the next attempt's book rolls use it |
| `ClearedToday` | Most floors one attempt cleared that day (shown, pays nothing) |
| `ClearedEver` | Most floors one attempt ever cleared (a floor above it is a first clear) |
| `Tickets` | Tickets held, not counting free ones due and not yet granted |
| `TicketPieces` | Pieces toward the next ticket, `0` to `TOWER_TICKET_PIECES - 1` |
| `GrantDay` | UTC day the free tickets were last granted up to, -1 before the first |

**Tickets (kj, 2026-10-31).**

- **Free daily ticket.** `TOWER_ATTEMPTS_PER_DAY` (1) for each UTC day since `GrantDay` (one day's worth on the first visit), added only while the stock is below `TOWER_TICKET_CAP` (5): a missed day is not lost, up to the cap. The cap counts every ticket held, but never removes one: tickets from runs can take the stock above it, and then no free ticket is added. The grant is lazy: `AccountOps.TowerTickets(c, a, day)` counts held plus due tickets (read only), and `RunTower` grants the due ones before spending **(agent: lazy, so a day number is only needed by the Dungeon commands; a stock filled by runs before the visit gets fewer free tickets, which is what the cap means)**.
- **Ground tickets.** `FinishRun` adds the sealed grade's `fight_ranks.ticket_pieces` (0 for a grade no longer in content) and turns every `TOWER_TICKET_PIECES` (4) pieces into a ticket, not capped. `FinishResult.TicketPieces` and `TicketsEarned` report it. Abandoned runs earn nothing.

`AccountOps.RunTower(c, a, vaultIndices, day)` returns a `TowerRunResult` (the `TowerResult`, shards, books by floor, first clears, tickets left). Rejected commands throw and change nothing: no ticket (`AccountOps.TowerTickets`), not 1 to `MON_SIDE_SIZE` distinct valid Vault indices, a negative day, or a day before `Day` **(agent: the clock went back; refusing it stops replaying an earlier day's book rolls)**. A run in progress does not block it: an attempt never touches the active run. It runs `TowerSim`, grants the free tickets due, spends one, counts the attempt, then pays **every floor cleared, on every attempt** (kj, 2026-10-31: grinding tickets is for more rewards, not only to beat a best):

- **Shards:** `TOWER_SHARDS_PER_FLOOR` per floor.
- **Skill Books:** on each floor cleared, one roll with chance `TOWER_BOOK_CHANCE + TOWER_BOOK_CHANCE_PER_FLOOR x (floor - 1)`; on a hit, the book is drawn from `book_drops` rows with `min_floor <= floor`, by weight, so rarer books sit deeper. The roll's seed is `Derive(StreamSeed(seed, "drop"), floor)` for the day's first attempt, then derived again by the day's attempt number (`AttemptsUsed` before the attempt) for later ones (`TowerSim.BookDrop`): each ticket rolls afresh, and the books are reproducible from (day, attempt, floor). Granted through `GrantBooks`.
- **Star-Gems:** the first clear ever of a floor (above `ClearedEver`, reported in `TowerRunResult.FirstClears`) and each `TOWER_MILESTONE_EVERY` floor cleared pay gems **once Star-Gems exist**: T9 scrapped the species gacha and added no gems, so what gems buy is open (#49, #77; `specs/18` section 1). Until then the Dungeon pays shards and books only. Amounts are decided with the gem economy (question 5); whether milestones pay per ticket or once a day is decided then too.

A fight is still a pure function of (content, team, day, slot order), so the same team on the same day clears the same floors every ticket; only the book rolls differ. The lineup changes each UTC day.

The test button that grants books (`specs/13` T3) stays as a debug-only control now that the tower grants them **(agent)**: the home screen shows it only with `?debug` in the page URL, or after five quick taps on the header's build stamp (remembered on the device in `localStorage['idle-rng-debug']`; five more taps turn it off). The bridge command `debugGrantBook` is unchanged.

The day number is read from the device clock by the caller. The sim cannot tell if the clock is wrong, so a player who moves it forward can claim more days. Accepted while the game is single-player and offline-first; the server-checked day arrives with T12 (`specs/10`) **(agent)**.

## 8. Web client

**Built (first version).** A **Dungeon** card on the home screen's Dungeon tab (the tab shows the tickets ready as a count; the card shows tickets ready, pieces toward the next, today's best, and how tickets are earned) opens the Dungeon screen, whose Back returns to that tab; everything comes through the bridge (`AccountCmd` `towerInfo` and `runTower`, `web/wasm/BRIDGE.md`), and the page computes no rule. The day is the UTC day number from the device clock (`run.towerDay()`).

- Status line: tickets (held plus the free ones due, `towerInfo.tickets`), pieces, floors cleared today and ever. The enter button is disabled with no ticket and says how to get one.
- The grade screen shows the pieces the sealed run earned, any ticket they completed and the progress toward the next (`finishRun`'s `ticketPieces`, `ticketsEarned`).
- Team (issue #98): `MON_SIDE_SIZE` slots in order, each with up, down and remove (slot order breaks ATB ties and sets default targets, section 2; a pick goes into the first free slot), then the Vault with its search, sort and filters (`web/vault.js`, the Vault's remembered sort and filters) and a checkbox per monster. Enter runs one attempt.
- **Climb reel** (kj, 2026-11-03: "play out like the slot machine end of run screen"): right after Enter, the attempt plays out on the gauntlet's reel (`specs/19` section 7) in the modal over the screen: the floors fought spin up from floor 1 and ease out onto the floor that stopped the team (a full clear lands on a "Dungeon cleared" row), each with a sprite from the boss's name (cosmetic) and marquee bosses starred; up to two floors not reached show "?". Then the comic defeat with the whole team (launched or turned to ash, from the attempt hash; a full clear hops), "N floors cleared / Fell on floor N to <boss>", and three buttons: **Watch floor N** (the deciding floor's playback), **Damage** (closes onto the damage chart below, every floor added up) and **See the loot** (closes onto the result card, whose banner then slams in; the Endless Tower's says **Continue**). Tapping the reel or Skip lands it; with `prefers-reduced-motion` it opens landed. It ignores the fight speed, as the gauntlet wheel does. A long climb spins through its last 100 floors only.
- Result card: where the team fell (or a full clear), the shards paid, the Skill Books dropped and on which floor, first clears (the loot as a reveal: a banner, gold on a full clear, that animates once per attempt when the player first sees it, then a face-down card each for the shards and every book plus one for the first clears, glowing its rarity colour; the player flips them by tapping or with Reveal all, a book wobbling and bursting as it turns; flipped cards stay face up for that attempt), every floor fought (won or lost, phases entered, vents, peak corruption) with a **Watch** button, and the last floor's fight as a collapsible log in which the tower events (phase entered, entropy vent, shield check failed, channel broken or landed) are highlighted lines.
- **Damage chart** (2026-11-03, kj asked): a "Damage chart" button at the top of the result card (and Damage on the climb reel) opens an optional view of the team: for each monster in slot order, a bar for the damage it dealt, the damage it took (HP lost, after shields) and, when the team healed at all, the healing it did, all on one scale, each with its share of the team's total, plus the team's totals. A picker shows every floor fought (the default) or one floor. The numbers are the fight tallies (`specs/16` section 6.3), kept per floor in `TowerFloor.Team` (`TeamTally`: dealt, taken, healed; not hashed) and sent as each floor's `team` (`web/wasm/BRIDGE.md`), so the page sums and draws only. The Endless Tower result has the same chart (`specs/28` section 9, step 5).
- Today's floors: each floor's boss, element, marquee bosses in gold, their phases in order (each opens its tooltip) and the floor level.
- **Floor playback** (issue #98): a floor's fight plays in the rank fight's view (`fightPlayView`; 1x, 2x or Skip, the same remembered speed): every unit's HP and skill cooldown tiles, and for each unit its entropy gauge (out of `ENTROPY_CAP`) with the vent that is next, a corruption meter (level out of the level the time limit reaches), and a gold banner for each tower event. A split phase's adds appear when they join. The climb itself plays out on the reel (above), not as a fight; the reel's Watch plays the last floor (the one that decided the attempt) and the result card's Watch any floor fought, at 1x when the speed is Skip, and the view steps to the floor before or after. Skip shows the floor's end. Every number is the engine's per-action snapshot (`web/wasm/BRIDGE.md`, `en`, `vent`, `corruption`); the next vent is `BattleSim.NextVent`, so the page computes no rule.
- **Bridge (agent):** the last floor's fight comes with `runTower`; any other floor comes from the read-only `towerFloor(day, team, floor)`, which replays the attempt and logs only that floor. Chosen over putting every floor's log in `runTower`: a 20-floor attempt with per-action snapshots would be megabytes in a result the page keeps, while players watch one or two floors. The replay is the same fight (a result is a pure function of content, team, day and slot order, section 2); each floor's fight `hash` is in both results and the browser test checks they match. The page calls it only for an attempt already made; since the sim runs on the device, it does not stop a player from trying a team on the day's floors before spending an attempt (as with the clock, section 7, the server check comes with T12).
- **Tooltips** (`BattleText.TowerTips`, RunContent `towerTips`): the entropy gauge (`tower.entropy.*`), each vent kind (`vent.<kind>.*`; convert uses `rule.entropy_convert.desc`), the corruption meter (`tower.corruption.*`, its start and step given in turns) and each phase kind (`phase.<kind>.*`), constants filled in (content 3.10).

Issue #98's polish is all built. Not planned yet: a practice mode (question 3).

## 9. Determinism and tests

- No clock, IO, reflection or `Math.Pow`; every draw comes from a named stream (`specs/00` section 5). New streams: `tower` (lineup), and the per-floor `tower_fight` and `drop` seeds.
- `TowerBattleTests` (step 1): each phase kind changes the fight as its row says and fires once; entropy gains, decays, vents in the fixed rotation and `entropy_convert` shields instead; corruption level and its multipliers; tower rules off leave fights unchanged.
- `TowerTests` (step 2): content loads; the lineup is fixed for a day, differs across days, puts marquees on milestones and uses the whole regular pool before repeating; boss scaling (adds skip the HP multiplier); the same (team, day) gives the same result and hash, different days differ, slot order matters, duplicate monster ids are fine; bad teams are rejected; a strong team clears and sees phases on every marquee floor; observers change nothing; drops are fixed by (day, floor), only eligible books drop, and the rate follows the constants.
- `TowerAccountTests` (step 3): an attempt pays every cleared floor (shards, the floor's book, first clears) and matches the sim; a second ticket the same day pays again with the second attempt's book rolls and no first clears; one free ticket a day, banked up to the cap and never cutting tickets from runs; a refused attempt changes nothing; an earlier day, a bad team and a negative day are refused; sealed runs add their grade's pieces and make tickets; a run in progress is untouched. `TowerTests`: book drops are fixed by (day, attempt, floor) and a later attempt rolls afresh. `AccountSaveTests`: tower progress and tickets round-trip, and v3 and v4 saves migrate.
- `golden/tower-vectors.txt` (`TowerVectors`): each species after a `focus` bot run at seed 7, alone and up to four together, on days 0 and 20391: the lineup, then floor, cleared, vents, phases, hash. Regenerated through the bot CLI (`golden --update`), never by hand. The balance harness (`balance` verb, `specs/09` section 7) sends bot-trained monsters, alone and as a team, up the tower over many days; there is no separate `tower` verb **(agent)**.
- Whether floors are fair is balance work: the `balance` report measures floors cleared, clear rate, shards and books per policy and team; targets are to be agreed with kj first (#89, `CLAUDE.md`).

## 10. Build order

Each step merges on its own with `./check.sh` green.

1. **Engine hooks** (`BattleSim`): `TowerRules`, entropy, the four phase kinds, the clock, tests. Rank fights and the gauntlet unchanged. **Done** (`TowerBattleTests`; no golden moved).
2. **Tower sim and content:** the three tables, constants, validators in both `content_build.py` and `ContentDb`, `TowerSim`, goldens. **Done** (content 3.6, `TowerTests`, `golden/tower-vectors.txt`).
3. **Account:** `AccountState.Tower`, `RunTower`, rewards, save schema v4, tests. **Done** (`TowerAccountTests`, `AccountSaveTests`). Tickets (question 7): save schema v5, content 3.16. **Done**.
4. **Web:** the tower screen and the bridge. **Done** (section 8): the first version, then floor playback with the entropy and corruption meters and tooltips, the team picker and the debug-only book button (issue #98).

## 11. Questions for kj

Recommendations first. **Answered (kj, 2026-10-31):** questions 1, 2 and 7. The rest can wait.

1. **Team rule. Answered:** teams of 1 to 4 Vault monsters, no Mercenaries. (Alternative not taken: build the Mercenaries of `specs/12` section 7 so a team is always four.)
2. **Floor model. Answered:** full heal each floor, 3 attempts a day (replaced by tickets, question 7). (Alternative not taken: HP carries over.)
3. **A practice mode** (an undated tower with no rewards, always available) so there is something to try with a new team when no ticket is left? Recommended: yes, later; it is the same sim with a free seed.
4. **Entropy on bosses.** By default bosses build none. Should marquee bosses have entropy so they can be stunned or enraged on purpose? Recommended: no for the MVP.
5. **Gems and book rarity** wait for the monetization call (`specs/13` section 9 item 4, R1.9 in `specs/20`): T9 scrapped the gacha, so there are no gems yet, and Skill Books are the premium gate. The placeholders here only prove the pipeline.
6. **Daily reset time.** UTC midnight (recommended) or the player's local midnight.
7. **Tickets. Answered:** rename the Daily Tower to the Dungeon; one free ticket a day, banked up to 5; sealed runs earn ticket pieces by grade (E 0, D 1, C 1, B 2, A 3, S 4; 4 pieces make a ticket); every ticket pays full rewards with fresh book rolls. (Alternatives not taken: once-a-day rewards with tickets as retries; a ticket only at grade B or better; a ticket chance by grade; a use-it-or-lose-it or uncapped free ticket.)
8. **The Dungeon leaderboard** (`specs/27` section 3.3 lists "Dungeon best floor per day"; blocks that board, step 5 of `specs/27` section 10). Open:
   - *Score and ties.* Floors cleared in one attempt are capped at `TOWER_FLOORS` (20), and every player faces the same floors (a public seed, `specs/27` section 9), so the top of a day's board would be a crowd at 20 ranked only by who committed first after 00:00 UTC. Recommended: rank by floors cleared, then a finer score from the attempt the sim already has (for example the fight time on the last floor fought: less is better), ties to the earliest; or, if a board of the capped Dungeon is not worth it, no Dungeon board (the Endless Tower season board is the uncapped ranking).
   - *Period.* One board per UTC day of the attempt (`runTower`'s day, which may be yesterday's just after midnight), kept for 7 days (recommended), or per week like the Endless season.
   - *Left out:* accounts that used a debug command, as every board (`specs/27` section 6.3).
