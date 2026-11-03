# 28 - Endless Tower and gambits

Status: **steps 1 to 4 built** (T11 of `specs/13`, issue #75). The gambit rulebook (section 4), the mutators (section 3.2) and the climb with its content (`EndlessSim`, sections 3 and 7) are live in the sim, with goldens and a balance report; the account side (section 5: rulebooks per Vault monster, season records, rewards, save v11, the `setRulebook` and `runEndless` commands on the server) is built and on the browser bridge. The web screens (step 5) are not built yet. kj answered every question on 2026-11-02 (section 10).

Source: `specs/12` sections 8.1 and 8.2 ("4v4, escalating, with mutators"; "teams act on an IF/THEN rulebook (gambits), e.g. If enemy HP < 30% -> priority: high-damage burst skill"; the defender sets it for PvP defense), `specs/22` section 3 (FF12 gambits as agency in an auto-battler), `specs/13` section 7 (gambit evaluation is pure and ordered, inside the battle engine).

> **Numbers and content are placeholders** until balance targets are agreed with kj (`CLAUDE.md`). Anything marked **(agent)** is a decision made while writing, easy to veto.

## 1. What it is

The Endless Tower is the team mode with no ceiling: a Vault team of four climbs 4v4 floors that grow stronger without end, while **mutators** bend the fight rules more the higher it goes. The player's lever is the **rulebook**: a short IF/THEN list per monster that replaces "whatever the default AI does" with a plan.

It answers different questions from the other modes:

| Mode | Format | What it tests |
|---|---|---|
| Gauntlet (`specs/19`) | 1 monster vs 100 floors | How far one build goes |
| Dungeon (`specs/21`) | 1-4 monsters vs a boss, daily lineup | Single-target damage, survival, boss phases |
| **Endless Tower** | 4 monsters vs 4 enemies, no top floor, mutators | Team synergy, turn order, AoE, rulebooks |
| PvP (T12) | 4v4 vs a stored team and its rulebooks | Rulebook tactics |

Goals: a reason to raise *different* monsters (a team, not one carry), a long-term number to push (best floor), and a place where the rulebook matters before PvP depends on it.

## 2. Flow (kj, 2026-11-02: questions 1 and 2)

1. **Team.** Exactly four (`MON_SIDE_SIZE`) distinct Vault monsters in slot order, each with its rulebook (section 4.4; kj, 2026-11-02). `EndlessSim.Team` refuses any other size.
2. **Climb.** Floor 1, 2, 3 ... each a 4v4 fight with a full heal between floors (kj; as the Dungeon, `specs/21` question 2). The attempt ends at the first lost or timed-out floor. A safety cap `ENDLESS_MAX_FLOORS` (placeholder 500) stops the sim if a team can never lose.
3. **Season.** The floors, enemy squads and mutators come from a **season seed** that changes weekly (kj). Inside a season the climb is deterministic: the same team and rulebooks reach the same floor. Changing a rulebook or the team and trying again is the loop.
4. **Attempts.** Unlimited and free (kj). Rewards come only from **new** floors: the first time a season's floor is cleared pays (section 6), so repeating an attempt pays nothing and there is nothing to farm or ration. This needs no ticket or timer.
5. **Result.** The best floor this season and ever, the floor and enemy that stopped the team, and playback of any floor (as the Dungeon's).

## 3. Floors, squads and mutators (kj, 2026-11-02: questions 4 and 5)

### 3.1 Squads

kj: squads and elites, and "squads should also pose a challenge: the team must be well rounded to deal with both" AoE and single-target. So a squad is not four random enemies: it is built from a **shape** that asks a question one kind of team cannot answer.

- New table `endless_enemies` (an enemy per row: element, a role tag, eight stat weights, skills, and its rulebook as gambit columns, so enemies use gambits too and the player can read them). Placeholder roles: `minion` (low HP, real damage, many of them), `tank` (taunt, high HP and DEF), `striker`, `caster`, `healer`, `support`.
- New table `endless_shapes` **(agent)**: each row lists `MON_SIDE_SIZE` role slots, the question it asks (`check`: `aoe`, `focus` or `open`) and `anchor_hp`, slot 0's HP multiplier. Placeholders (content 6.1):

  | Shape | Check | Slots | Beats | Answer |
  |---|---|---|---|---|
  | `swarm` | aoe | minion x4 | Single-target teams: four damage dealers, each low on HP | AoE |
  | `bulwark` | focus | tank, healer, striker, caster | AoE teams: the healer outheals spread damage, the tank soaks | Focus fire on the healer (`lowest_hp` / `highest_hp` picks, burst) |
  | `fortress` | focus | tank (x3 HP), minion x3 | Teams that cannot finish one huge target | Single-target damage once the minions are down |
  | `mixed` | open | striker, caster, healer, support | Nothing in particular | A rounded team |
  | `skirmish` | open | striker x2, support, minion | Slow teams | Speed, burst |

- **Bands** of `ENDLESS_BAND` floors (placeholder 10): band b is floors b x B + 1 to (b + 1) x B. Each band has its own stream (`endless`, seeded `Derive(season seed, b)`), so a floor's squad does not depend on how far anyone climbed. On it, in order: the regular floor (one of the first B - 1) that gets an `aoe` shape, a different one that gets a `focus` shape (**agent**: a team strong at only one of AoE and single-target stalls inside the first bands), then floor by floor its shape (from the aoe shapes, the focus shapes or all of them; one draw) and one enemy of each slot's role (one draw each, among that role's rows in CSV order).
- The band's last floor is **elite**: slot 0 is a Dungeon marquee boss (drawn), at the floor's level with HP x `ENDLESS_ELITE_HP_MULT` (placeholder 2) instead of the Dungeon's 4v1 multiplier, with its phases (a split phase only fills dead slots, `specs/21` section 3.2); slots 1 to 3 are a drawn shape's.
- Level: `ENDLESS_BASE x ENDLESS_GROWTH^(floor - 1)` by repeated multiplication (placeholders 60 and 1.05), stats = weights x level, slot 0's HP also x `anchor_hp`. Enemies are named `e<slot>_<id>` and carry their content rulebooks; the team is `t<slot>_<id>`.

### 3.2 Mutators

A mutator is a rule change for every unit on both sides, listed before the climb and on every floor. kj: one new mutator joins every `ENDLESS_BAND` floors, and they stack: floor f has the season's first f / `ENDLESS_BAND` in force, so floors 1-9 have none, 10-19 one, 20-29 two, and so on (the elite floor that ends a band already has the band's new one). The season's list (`EndlessSim.SeasonMutators`) is drawn on its own stream (`endless_mutators`) without repeats until the table is used up, then it refills in CSV order. It is fixed and shown in advance, so the player can plan rulebooks for it.

Table `mutators` (`id`, `name_key`, `kind`, `value`, `element` only for `element_mult`; content 6.1): nine placeholders, one or two of each kind. **Built (step 2):** the engine side, `FightRules.Mutators` (`FightMutators`, null = none). `FightMutators.Add(kind, value, element)` folds each mutator in and rejects an unknown kind or a value out of its range; mutators stack (multipliers multiply; crit bonuses, clock ticks and start shields add). With none, or an empty set, a fight is unchanged (`MutatorTests`).

| Kind | Effect | Value | Rewards |
|---|---|---|---|
| `heal_mult` | Every heal and shield the environmental clock scales (skills, `tidal_overflow`, `abyssal_drain`, a boss's shield-check heal) x value | `>= 0` | Damage over sustain |
| `aoe_mult` | All-enemy hits x value | `>= 0` | AoE builds |
| `single_mult` | Single-target hits (skills, basic attacks, triggers) x value | `>= 0` | Focus fire |
| `element_mult` | One element's hits x value | `>= 0`, with an element | That element |
| `speed_mult` | Every unit's ATB gauge fill x value | `> 0` | Faster fights, turn order |
| `crit_bonus` | Crit chance + value, before the crit cap | `[-1, 1]` | LUK builds |
| `corruption_early` | The environmental clock (`specs/21` section 3.3) starts value ticks earlier, never before tick 0; tower fights only | whole, `>= 0` | Burst over stall |
| `start_shield` | Every unit starts with value x its max HP as shield, until broken or replaced | `[0, 1]` | Sustained damage |

The damage multipliers apply in the hit after the element and damage-dealt bonuses, before mitigation (`specs/16` section 5.3). Counters, channel strikes, entropy recoil and `wail_stagger` are not hits and are not scaled. `BattleSim.HealMult` stays corruption only (the web's corruption meter reads it). The tooltips (`specs/16` section 9) do not read mutators yet; the web step adds the mutator list.

### 3.3 Tower mechanics

Proposed: the Endless Tower fights with `FightRules.Tower` on (entropy gauge and environmental clock, `specs/21` section 3), so the anti-trivialization rules hold here too. Elite units may carry their boss phases.

## 4. Gambit rulebooks (built)

A **rulebook** is an ordered list of rules on a fighter (`Fighter.Gambits`, `GambitRule`). Each rule is IF *condition* THEN *action* AT *target*. The engine is pure and deterministic; rulebooks are input, like skills, so a fight's hash covers what they did, not the rulebook itself.

### 4.1 Conditions

| Id | Value | Holds when |
|---|---|---|
| `always` | unused | Always |
| `self_hp_below` | HP fraction in (0, 1] | The unit's HP fraction is below the value |
| `ally_hp_below` | HP fraction in (0, 1] | Some living ally (the unit included) is below the value |
| `enemy_hp_below` | HP fraction in (0, 1] | Some living enemy is below the value |
| `enemies_at_least` | whole count >= 1 | At least that many enemies are alive |
| `first_actions` | whole count >= 1 | This is one of the unit's first N actions of the fight |

### 4.2 Actions and targets

- The action is one of the unit's own active skills by id (not a passive), or `basic` (the basic attack). A skill whose id is `basic` cannot be named.
- The target pick applies to every single-target enemy pick the unit makes during that action (its skill, the `echo_cast` repeat and any trigger that fires within the action): `auto` (the default rule: a random living enemy, one draw), `lowest_hp` (the living enemy with the lowest HP fraction) or `highest_hp` (the highest), lowest slot on a tie and with no random draw. A taunting enemy is still forced, as for the default AI (`specs/16` section 5.2). A single-target debuff still prefers enemies that lack its effect, then applies the pick among them. Heals, shields and buffs keep their own targets (a lowest-ally heal goes to the ally with the lowest HP fraction).

### 4.3 How a unit acts with a rulebook

On its action, after its `OnAction` triggers and the cooldown tick (`specs/16` section 5.2):

1. The rules are checked in order. A rule **fires** when its condition holds and its action can go now: `basic` always can; a skill must be off cooldown and have something to do. For a damage or shield skill that is a living enemy; for a heal, a hurt target among its own targets; a taunt, buff or debuff uses the default AI's check (it is not cast where it changes nothing). So the rule replaces the default AI's *timing* (a heal rule fires before the heal can land in full), but never wastes an action on nothing.
2. The first rule that fires is the action. It is logged as a `gambit` event (`FightAction.Event`, Amount = the rule's index from 0) just before the action itself, so playback can show "Rule 2".
3. If no rule fires, the default AI acts, **except that a skill some rule names is used only by its rules** **(agent)**. That makes "save the burst for an enemy below 30%" possible with one rule, while skills the player did not write a rule for keep working as before. A rulebook with no rules is exactly the default AI (`GambitTests`, and the earlier battle goldens did not move).

`BattleSim.Start` rejects an unknown condition or target, a value out of range, a null rule, and an action that is not one of the unit's active skills or `basic`.

### 4.4 Where rulebooks live (kj, 2026-11-02: question 6)

One rulebook per Vault monster, at most `GAMBIT_SLOTS` (placeholder 6) rules, used wherever that monster fights in a team (Endless Tower, the Dungeon, and as the PvP defense). Rank fights and the gauntlet stay on the default AI (the run is about training, not tactics). Editing a rulebook is free and does not change the sealed monster's result or hash (the Vault entry stays frozen; the rulebook is account data beside it).

**Built (step 4):** `AccountState.Rulebooks`, rulebook text (section 7) by Vault entry id. `AccountOps.SetRulebook(index, text)` (command `setRulebook`) stores the canonical text (`GambitText.Format`; empty text clears it) after `CheckRulebook`: it parses, holds at most `GAMBIT_SLOTS` rules, and every action is `basic` or one of the monster's active skills. Allowed at any time, also during a run. `AccountOps.TeamFighter(index)` is the entry's fighter with its rulebook; the Dungeon (`RunTower` and its playback) and the Endless Tower build their teams through it, so a rulebook changes a Dungeon attempt too. Dismantling a monster removes its rulebook. An entry with no `Id` (only an old local save could have one) cannot hold a rulebook.

### 4.5 The editor on a phone (step 5)

Each monster's rulebook is a list of rule cards, one line each: **IF** [condition chip] [value] **THEN** [skill chip] **AT** [target chip]. Tapping a chip opens a picker sheet; the skill picker lists the monster's own skills with their tooltips (`specs/16` section 9). Rules reorder with up and down buttons (no drag, so it works with one thumb). A "test" button replays the floor that stopped the team with the edited rulebooks, as a preview with no reward. During playback the acting rule's card lights up.

**Built (step 5, the editor):** `web/rulebook.js` (`RulebookView.open(ctx, vaultIndex, opts)`), opened from the Vault detail sheet and the Endless screen. Values are picked from 10% to 100% in steps of 10% (an HP fraction) or 1 to 6 (a count); a new rule is `always > basic`. A saved rule naming a skill the monster no longer has shows as a warning card to fix or delete (fights skip it). Test runs `endlessPreview` with the draft in this monster's slot and the others' saved rulebooks, plays it back with the acting rule's card lit until the monster's next action, and pays nothing. Checked by `web/wasm/test/rulebook.js`.

## 5. Account and server (step 4, built)

- `AccountState.Endless` (`EndlessProgress`): `Season` of the last climb (-1 before the first), `Climbs` this season, `ClearedSeason` (most floors one climb cleared this season) and `ClearedEver`. Floors are cleared from floor 1 up and pay once a season, so the floors already paid this season are 1 to `ClearedSeason` **(agent: no separate paid list)**. Rulebooks per Vault entry id (section 4.4). Save schema v11 (`specs/06` section 5); on the server they are the `endless` and `rulebooks` parts (`specs/27` section 3.1).
- `AccountOps.RunEndless(team, season)` (command `runEndless(season, vaultIndices[])`): exactly `MON_SIDE_SIZE` distinct Vault positions, each with its rulebook; free and unlimited. Plays `EndlessSim.Run`, pays each floor above the season's best (section 6), records the bests, counts the climb and checks the achievements. A later season resets the season fields; an earlier one than the last climb's is refused (the clock went back). A run in progress does not block it. Under `specs/27` (option E) it is a checkpoint command: the server re-runs the climb from the team's Vault entries, their rulebooks and the season, so nothing the client reports is trusted.
- **Cost on the server** (`specs/27` section 4, issue #109): measured 2026-11-03 on .NET 8 Release, bot teams (`focus` and `balanced`, one of each of the first four species) climbing 24 to 32 floors take 7 to 25 ms a climb (0.2 to 0.8 ms a floor), so a climb to `ENDLESS_MAX_FLOORS` stays under 0.5 s, far inside Cloud Code's 15 s limit. A commit carries at most `COMMIT_MAX_CLIMBS` (placeholder 10) climbs **(agent)**.
- The season is `EndlessSim.SeasonOf(day)`: the UTC week, starting Monday 00:00 UTC (day 0, 1970-01-01, was a Thursday: season = (day + 3) / 7) **(agent: weeks start on Monday)**. The server accepts the season of its own day or of the day before (#21), like the Dungeon's day (`specs/27` section 6.2).
- Bridge (`web/wasm/BRIDGE.md`): the commands `setRulebook` and `runEndless` (with the deciding floor's fight, `final`), the reads `endlessInfo(season)` (season records, the season's mutators with their effect in words, and the next `ENDLESS_BAND` floors' squads and shards), `endlessFloor(season, team, floor)` (any floor's fight for playback) and `endlessPreview(season, team, floor, rulebooks)` (that floor with edited rulebooks, the editor's test: it pays and saves nothing), each Vault entry's `rulebook` and active `skills`, and the editor's vocabulary in the content (`gambits`: conditions, targets and the basic attack with names and descriptions, strings `gambit.*`, content 6.3).

## 6. Rewards (kj, 2026-11-02: question 7)

- Training Shards for each floor cleared for the first time this season: floor(`ENDLESS_SHARDS_BASE` + floor x `ENDLESS_SHARDS_PER_FLOOR`), times `ENDLESS_ELITE_SHARD_MULT` on an elite floor (`AccountOps.EndlessFloorShards`; placeholders 2, 0.5 and 2: floors 1 to 30 pay about 330 shards a season, two or three runs' worth). `EndlessRunResult` lists the floors paid (`NewFloors`) and the first clears ever (`FirstClears`, for the gems below).
- Star-Gems on milestone floors **once gems exist** (`specs/12` section 10.2; #49, #77), as the Dungeon.
- **No Skill Books**: they stay the Dungeon's (kj).
- Achievements on best floor ever: achievement kind `endless_floor` (`specs/18` section 8) reads `Endless.ClearedEver`; placeholder `spire_20` (Spire Breaker, 20 floors in one climb, unlocks no species). Checked after each climb and when an account loads.
- A season leaderboard needs the server (T12) and comes with PvP.

## 7. Content (content 6.1, MINOR; step 3; 6.2 added the rewards and `GAMBIT_SLOTS`, step 4)

| Table | Columns |
|---|---|
| `endless_enemies` | `id`, `name_key`, `element`, `role` (`minion`, `tank`, `striker`, `caster`, `healer`, `support`), eight stat weights, `skills` (ids, `\|`-separated, AI priority order), `gambits` (the enemy's rulebook as text, below; may be empty) |
| `endless_shapes` | `id`, `name_key`, `check` (`aoe`, `focus`, `open`), `slots` (`MON_SIDE_SIZE` roles, `\|`-separated), `anchor_hp` |
| `mutators` | `id`, `name_key`, `kind` (section 3.2), `value`, `element` (only for `element_mult`) |

Placeholders: 12 enemies (two per role, kits from existing `monster_skills`), 5 shapes, 9 mutators; strings `endless.<id>.name`, `endless.shape.<id>.name`, `mutator.<id>.name`.

**Rulebook text** (`GambitText.Parse` and `Format`; `parse_gambits` in `tools/content_build.py`): rules split by `|`, each `when[:value]>action[@target]`, for example `enemy_hp_below:0.4>night_fang@lowest_hp|always>basic`. No target means `auto`; `always` takes no value.

| Constant | Value | Range |
|---|---|---|
| `ENDLESS_SEED` | 20261102 | whole number |
| `ENDLESS_BASE` | 60 | `> 0` |
| `ENDLESS_GROWTH` | 1.05 | `>= 1` |
| `ENDLESS_BAND` | 10 | `>= 3` (room for an aoe, a focus and an elite floor) |
| `ENDLESS_ELITE_HP_MULT` | 2 | `> 0` |
| `ENDLESS_MAX_FLOORS` | 500 | `> 0` |
| `ENDLESS_SHARDS_BASE` | 2 | `>= 0` (content 6.2) |
| `ENDLESS_SHARDS_PER_FLOOR` | 0.5 | `>= 0` |
| `ENDLESS_ELITE_SHARD_MULT` | 2 | `>= 1`, whole number |
| `GAMBIT_SLOTS` | 6 | `> 0`, whole number |

Validation, in both `tools/content_build.py` (`validate_endless`) and `ContentDb.BuildEndless`, reusing existing codes: element and skill references, and a gambit action that is not one of the enemy's active skills (`E023`); a rulebook that does not parse, negative weights, a shape without exactly `MON_SIDE_SIZE` slots, `anchor_hp <= 0`, a mutator value out of its kind's range or an element on the wrong kind, the constants above (`E043`); an unknown slot role (`E021`); a role no enemy fills, no `aoe` or no `focus` shape, no mutators (`E044`). The rewards' constants and `GAMBIT_SLOTS` out of range are `E043` too (content 6.2).

## 8. Determinism and tests

- No clock, IO, reflection or `Math.Pow`. New streams: `endless` (one per band: `Derive(season seed, band)`), `endless_mutators` (the season's mutator list) and `endless_fight` (each floor's fight seed, `Derive(StreamSeed(season seed, "endless_fight"), floor)`). The season seed is `Derive(StreamSeed(ENDLESS_SEED, "endless"), season)`.
- `EndlessSim.Run(content, team, season, onAction, onFloorStart)` returns `EndlessResult`: floor reached, capped, each floor (shape, elite, won, ticks, phases, vents, peak corruption, fight hash) and a hash (`HashVersion` 1). `Lineup`, `Band`, `SeasonMutators`, `Squad`, `Rules` and `Level` serve the preview screen. `EndlessSim.FightFloor(content, team, season, floor)` fights one floor on its own: every floor starts at full HP from its own seed with the season's mutators so far, so it is the fight the climb fought there (`EndlessTests` checks every floor's hash against a climb); playback and the rulebook test use it.
- `MutatorTests` (step 2, built): an empty set changes nothing; each kind does what its row says and only that; mutators stack; bad ones are rejected, including an element table written past `Add`.
- `GambitTests` (step 1, built): an unused rulebook leaves the fight unchanged; a named skill waits for its rule; rules go in order and skip a skill on cooldown; `first_actions`; `lowest_hp` and `highest_hp` picks with no draw and `auto` keeping it; a taunt still forces; a heal rule fires before the full-amount wait; bad rulebooks are rejected; a cloned fight with gambits continues identically; tower teams keep their rulebooks.
- `golden/battle-sim-vectors.txt` has a ninth fight, "4v4 gambits vs default AI", covering every condition and target pick (step 1).
- `EndlessTests` (step 3, built): content loads and is validated; levels; every band has an aoe and a focus floor and ends on an elite, every slot's enemy has the slot's role, and floors carry floor / band mutators; the lineup is fixed for a season, differs across seasons and is prefix-stable; season mutators do not repeat until the table is used up; squads scale, carry rulebooks and boost the anchor and the elite; a climb is deterministic, stops at the first loss, needs exactly four, changes with rulebooks, stops at the safety cap and gets its first mutator at the first full band; rulebook text parses, formats and rejects bad rules. `tools/test_content_build.py` mirrors the validation.
- `EndlessAccountTests` (step 4, built): the season is the UTC week from Monday; floor shards follow the formula and elite floors pay more; a climb matches the sim, pays each floor once a season, pays again in a later season with no first clears below the best ever, and keeps the season best; bad teams and an earlier season are refused and change nothing; the `endless_floor` achievement; rulebooks stored as canonical text by entry id without touching the entry, refused when too long, naming a skill the monster lacks, or not parsing; rulebooks change the Endless climb and the Dungeon attempt and leave with a dismantled monster; the commands parse and apply like `AccountOps`. Also `AccountPartsTests` (both commands change and read only what they declare), `AccountSaveTests` (round trip, v10 to v11) and `AccountServerTests` (both commands in their own commits, the season rule over Monday midnight, `COMMIT_MAX_CLIMBS`).
- `golden/endless-vectors.txt` (`EndlessVectors`): the first four species after a `focus` run at seed 7, in seasons 0 and 2913, on the default AI and with a simple rulebook each (listed in `specs/09` section 3).
- The balance report (`specs/09` section 7) climbs with each policy's one-of-each-species team and reports floors, the capped rate and what stopped the climb (shape check, elite or capped). First measurement (content 6.1, 8 seeds): bot teams reach floors 22 to 36; `focus` and elite floors stop most climbs and `swarm` (aoe) floors none, so the aoe question is too easy for now. Targets are agreed with kj before any retune.

## 9. Build order

Each step merges on its own with `./check.sh` green.

1. **Gambit engine** in `BattleSim`: conditions, actions, target picks, validation, tests, a golden fight. **Done.**
2. **Mutator engine**: `FightRules.Mutators`, off by default so no golden moved; `MutatorTests`. **Done.**
3. **Endless sim and content**: enemies, shapes, scaling, season lineup, mutators, `EndlessSim.Run`, goldens, balance report. **Done** (content 6.1, `EndlessTests`, `golden/endless-vectors.txt`).
4. **Account**: rulebooks per Vault entry, season records, rewards, save version, server command. **Done** (content 6.2, save v11, `EndlessAccountTests`).
5. **Web**: team setup (the Dungeon's picker), the rulebook editor, the climb with playback and the mutator list.

## 10. Questions for kj

**All answered (kj, 2026-11-02).** Question 8 stands as built until kj changes it.

1. **Season and attempts. Answered:** weekly season, unlimited free attempts, rewards only for floors new this season. (Alternatives not taken: tickets like the Dungeon; a daily tower.)
2. **Floor model. Answered:** full heal each floor. (Alternative not taken: HP carries over.)
3. **Team size. Answered:** exactly four. (Alternative not taken: 1 to 4 as the Dungeon.)
4. **Mutators. Answered:** one more every 10 floors from a weekly draw, stacking, shown in advance. (Alternatives not taken: one fixed set from floor 1; opt-in mutators for rewards.)
5. **Enemies. Answered:** generic squads with an elite every 10 floors, and squads must challenge the team so it has to be well rounded, handling both AoE and single-target (section 3.1: squad shapes). (Alternatives not taken: mirror teams; Dungeon bosses only.)
6. **Rulebooks. Answered:** one per Vault monster, up to 6 rules, used in every team mode. (Alternatives not taken: one per team; unlockable rule slots; the single pre-fight rule of `specs/22`.)
7. **Rewards. Answered:** shards for new floors per season, gems once they exist, no Skill Books (they stay the Dungeon's).
8. **Gambit semantics (step 1, built as agent decisions, easy to change):** a named skill is used only by its rules; rules never waste an action on a skill with nothing to do; taunts still force targets. Confirm or change.
