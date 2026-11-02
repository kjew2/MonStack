# 28 - Endless Tower and gambits

Status: **design, steps 1 and 2 built** (T11 of `specs/13`, issue #75). The gambit rulebook (section 4) and the mutators (section 3.2) are live in the battle engine; the climb, its content, the account and the web screens are not built yet. kj answered the flow questions on 2026-11-02 (section 10); sections 5 and 6 are still a proposal.

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

1. **Team.** Four distinct Vault monsters in slot order, each with its rulebook (section 4.4). Proposed **(agent)**: exactly four, since 4v4 is the mode's identity; fewer may climb but are not helped.
2. **Climb.** Floor 1, 2, 3 ... each a 4v4 fight with a full heal between floors (kj; as the Dungeon, `specs/21` question 2). The attempt ends at the first lost or timed-out floor. A safety cap `ENDLESS_MAX_FLOORS` (placeholder 500) stops the sim if a team can never lose.
3. **Season.** The floors, enemy squads and mutators come from a **season seed** that changes weekly (kj). Inside a season the climb is deterministic: the same team and rulebooks reach the same floor. Changing a rulebook or the team and trying again is the loop.
4. **Attempts.** Unlimited and free (kj). Rewards come only from **new** floors: the first time a season's floor is cleared pays (section 6), so repeating an attempt pays nothing and there is nothing to farm or ration. This needs no ticket or timer.
5. **Result.** The best floor this season and ever, the floor and enemy that stopped the team, and playback of any floor (as the Dungeon's).

## 3. Floors, squads and mutators (kj, 2026-11-02: questions 4 and 5)

### 3.1 Squads

kj: squads and elites, and "squads should also pose a challenge: the team must be well rounded to deal with both" AoE and single-target. So a squad is not four random enemies: it is built from a **shape** that asks a question one kind of team cannot answer.

- New table `endless_enemies` (an enemy per row: element, a role tag, eight stat weights, skills, and its rulebook as gambit columns, so enemies use gambits too and the player can read them). Placeholder roles: `minion` (low HP, real damage, many of them), `tank` (taunt, high HP and DEF), `striker`, `caster`, `healer`, `support`.
- New table `endless_shapes` **(agent)**: each row lists four role slots and the question it asks. Placeholders:

  | Shape | Slots | Beats | Answer |
  |---|---|---|---|
  | `swarm` | minion x4 | Single-target teams: four damage dealers, each low on HP | AoE |
  | `bulwark` | tank, healer, striker, striker | AoE teams: the healer outheals spread damage, the tank soaks | Focus fire on the healer (`lowest_hp` / `highest_hp` picks, burst) |
  | `fortress` | tank (x2 HP), minion x3 | Teams that cannot finish one huge target | Single-target damage once the minions are down |
  | `mixed` | striker, caster, healer, support | Nothing in particular | A rounded team |

- Each floor's shape comes from the season's `endless` stream, rotated so every band of 10 floors holds at least one `swarm` and one of `bulwark` or `fortress` **(agent)**: a team strong at only one of AoE and single-target stalls inside the first bands. Each slot then draws an enemy of its role.
- Every `ENDLESS_ELITE_EVERY`-th floor (placeholder 10) is an **elite** floor: one elite unit (a Dungeon marquee boss at side scale, without the 4v1 HP multiplier, with its phases) and three of a shape's other slots, so the elite floor too asks for both kinds of damage.
- Level: `ENDLESS_BASE x ENDLESS_GROWTH^(floor - 1)` by repeated multiplication, stats = weights x level, as the Dungeon (`specs/21` section 4).

### 3.2 Mutators

A mutator is a rule change for every unit on both sides, listed before the climb and on every floor. kj: one new mutator joins every `ENDLESS_MUTATOR_EVERY` floors (placeholder 10), drawn from the season's stream without repeats, and they stack, so floor 1-9 has none, floor 10-19 one, 20-29 two, and so on. The season's list is fixed and shown in advance, so the player can plan rulebooks for it.

New table `mutators` (`id`, `kind`, `value`, `element` where the kind needs it; step 3). **Built (step 2):** the engine side, `FightRules.Mutators` (`FightMutators`, null = none). `FightMutators.Add(kind, value, element)` folds each mutator in and rejects an unknown kind or a value out of its range; mutators stack (multipliers multiply; crit bonuses, clock ticks and start shields add). With none, or an empty set, a fight is unchanged (`MutatorTests`).

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

### 4.4 Where rulebooks live (question 6)

Proposed: one rulebook per Vault monster, at most `GAMBIT_SLOTS` (placeholder 6) rules, used wherever that monster fights in a team (Endless Tower, the Dungeon, and as the PvP defense). Rank fights and the gauntlet stay on the default AI (the run is about training, not tactics). Editing a rulebook is free and does not change the sealed monster's result or hash (the Vault entry stays frozen; the rulebook is account data beside it).

### 4.5 The editor on a phone (step 5)

Each monster's rulebook is a list of rule cards, one line each: **IF** [condition chip] [value] **THEN** [skill chip] **AT** [target chip]. Tapping a chip opens a picker sheet; the skill picker lists the monster's own skills with their tooltips (`specs/16` section 9). Rules reorder with up and down buttons (no drag, so it works with one thumb). A "test" button replays the floor that stopped the team with the edited rulebooks, as a preview with no reward. During playback the acting rule's card lights up.

## 5. Account and server (proposal)

- `AccountState.Endless`: the season id, best floor this season and ever, floors already paid this season. Rulebooks per Vault entry id (`specs/27`: entries have stable `Id`s). A new account save version.
- `AccountOps.RunEndless(team, season)`: plays the climb, pays new floors, records the bests. Under `specs/27` (option E) this is a checkpoint command: the server re-runs the climb from the team's Vault entries, their rulebooks and the season, so nothing the client reports is trusted. A climb is many fights, so its cost on the server is measured before it ships (`specs/27` section 9, issue #109).
- The season is the UTC week number from server time (#21), like the Dungeon's day.

## 6. Rewards (proposal, question 7)

- Training Shards for each floor cleared for the first time this season: `ENDLESS_SHARDS_BASE + floor x ENDLESS_SHARDS_PER_FLOOR` (placeholders), more on elite floors.
- Star-Gems on milestone floors **once gems exist** (`specs/12` section 10.2; #49, #77), as the Dungeon.
- Achievements on best floor ever (a species unlock condition can use it, `specs/18` section 8).
- A season leaderboard needs the server (T12) and comes with PvP.

## 7. Content (step 3)

New tables `endless_enemies` and `mutators`, constants `ENDLESS_*` and `GAMBIT_SLOTS`, strings for enemy and mutator names and the gambit vocabulary (`gambit.when.<id>`, `gambit.target.<id>`). New tables are a MINOR content bump. Validation in both `tools/content_build.py` and `ContentDb`, reusing existing error codes as `specs/21` section 5 does.

## 8. Determinism and tests

- No clock, IO, reflection or `Math.Pow`. New stream: `endless` (season lineup and mutators), seeded from the season; each floor's fight seed derives from it.
- `MutatorTests` (step 2, built): an empty set changes nothing; each kind does what its row says and only that; mutators stack; bad ones are rejected, including an element table written past `Add`.
- `GambitTests` (step 1, built): an unused rulebook leaves the fight unchanged; a named skill waits for its rule; rules go in order and skip a skill on cooldown; `first_actions`; `lowest_hp` and `highest_hp` picks with no draw and `auto` keeping it; a taunt still forces; a heal rule fires before the full-amount wait; bad rulebooks are rejected; a cloned fight with gambits continues identically; tower teams keep their rulebooks.
- `golden/battle-sim-vectors.txt` has a ninth fight, "4v4 gambits vs default AI", covering every condition and target pick (step 1).
- Later: `EndlessTests` and `golden/endless-vectors.txt` (step 3), the balance harness climbing with bot teams and default rulebooks (step 3; targets agreed with kj first).

## 9. Build order

Each step merges on its own with `./check.sh` green.

1. **Gambit engine** in `BattleSim`: conditions, actions, target picks, validation, tests, a golden fight. **Done.**
2. **Mutator engine**: `FightRules.Mutators`, off by default so no golden moved; `MutatorTests`. **Done.**
3. **Endless sim and content**: enemies, shapes, scaling, season lineup, mutators, `EndlessSim.Run`, goldens, balance harness. Question 3 (team size) is still open but easy to change.
4. **Account**: rulebooks per Vault entry, season records, rewards, save version, server command. Needs questions 6 and 7.
5. **Web**: team setup (the Dungeon's picker), the rulebook editor, the climb with playback and the mutator list.

## 10. Questions for kj

Recommendations first. **Answered (kj, 2026-11-02):** questions 1, 2, 4 and 5. Question 3 does not block (easy to change); 6 and 7 block step 4.

1. **Season and attempts. Answered:** weekly season, unlimited free attempts, rewards only for floors new this season. (Alternatives not taken: tickets like the Dungeon; a daily tower.)
2. **Floor model. Answered:** full heal each floor. (Alternative not taken: HP carries over.)
3. **Team size.** Exactly four (recommended) or 1 to 4 as the Dungeon.
4. **Mutators. Answered:** one more every 10 floors from a weekly draw, stacking, shown in advance. (Alternatives not taken: one fixed set from floor 1; opt-in mutators for rewards.)
5. **Enemies. Answered:** generic squads with an elite every 10 floors, and squads must challenge the team so it has to be well rounded, handling both AoE and single-target (section 3.1: squad shapes). (Alternatives not taken: mirror teams; Dungeon bosses only.)
6. **Rulebooks.** One per Vault monster, up to 6 rules, used in every team mode (recommended). Alternatives: one per team; unlockable rule slots as a progression (as FF12's gambit slots); the single pre-fight rule of `specs/22`.
7. **Rewards.** Shards for new floors per season, gems once they exist (recommended). Should the Endless Tower drop Skill Books too, or stay the Dungeon's exclusive?
8. **Gambit semantics (step 1, built as agent decisions, easy to change):** a named skill is used only by its rules; rules never waste an action on a skill with nothing to do; taunts still force targets. Confirm or change.
