# 16 - Monster Battle

Status: **live (T1 of `specs/13`)**. Used by the training run's rank fights (`specs/14` section 5); the gauntlet (T4), towers and PvP will call it too. Code: `src/IdleRng.Sim/Combat`. Tests: `src/IdleRng.Sim.Tests/BattleSimTests.cs`. Golden: `golden/battle-sim-vectors.txt`.

It replaced the idle game's 4v1 boss battle, which was removed with that game at `specs/13` T7; it was rebuilt from the same design (ATB gauge, kit vocabulary, damage formula, triggers).

> **Numbers are placeholders.** Every formula constant here is a placeholder until balance resumes (`CLAUDE.md`).

## 1. Shape

- Two sides, A and B, of 1 to `MON_SIDE_SIZE` (4) units each. 1v1 (gauntlet, in-run Tactical Challenges), 4v1 (Daily Tower bosses) and 4v4 (Endless Tower, PvP) are all the same engine.
- Turn order is an ATB gauge; actions are automatic (default AI, section 5.2). Gambit rulebooks (`specs/12` section 8.1) will replace the AI per unit later.
- Pure and deterministic: same fighters, rules and random draws give the same fight on the client, the bots and a server.

## 2. API

```csharp
FightRules rules = FightRules.FromContent(contentDb);           // plain data; a server can hold its own copy
FightResult r    = BattleSim.Resolve(rules, sideA, sideB, seed, onAction, onStart);   // whole fight on a fresh stream
FightState s     = BattleSim.Start(rules, sideA, sideB);           // or step it for animation:
FightOutcome o   = BattleSim.Step(rules, s, rng, onAction);        // one tick
string h         = BattleSim.StateHash(s);                         // canonical SHA-256 of the whole fight
```

- `Fighter` is the input snapshot: `Id` (unique in the fight), `Element`, `Stats` (section 3), `Skills`, `Triggers`, `Rules`. A fight keeps a reference, so fighters must not change during it.
- `onAction` (optional) is called with each `FightAction` as it happens; `onStart` (optional) gets the new `FightState` before the first tick, so an observer can read live HP and cooldowns inside `onAction`. Observers only read: watching a fight never changes it (`BattleSimTests.OnStart_ExposesTheLiveState_WithoutChangingTheFight`). The web bridge uses both to snapshot HP and cooldowns per action for playback (`web/wasm/BRIDGE.md`). How fast a client replays a fight is presentation, not a rule.
- `Resolve` seeds a stream named `battle_sim` with `seed`. Callers derive the seed from their own stream (the run engine from `training`, a PvP server from the match), so a fight never advances a game stream by an unknown amount.
- `FightState` is a plain mutable graph with `Clone()`; a clone plus the same draws continues identically, so a fight can be saved mid-way.
- `Start` validates and throws `ArgumentException`: side sizes, unique non-empty ids, finite stats `>= 0`, known stat ids for skills, known rule modifier ids, non-negative power, cooldown and duration.

## 3. Stat sheet

Eight trained stats (kj, 2026-10-30), on the 0-999 training scale:

| Stat | Battle use |
|---|---|
| `HP` | Max HP = `max(1, HP x MON_HP_PER_POINT)` |
| `STR` | Physical damage: basic attack and STR-scaled skills |
| `INT` | Magical damage: INT-scaled skills; the basic attack is magical when INT > STR |
| `WIS` | Mitigates magical damage; WIS-scaled skills (heals, shields) are magical if they deal damage |
| `DEX` | Evasion of physical hits, both sides' DEX count |
| `DEF` | Mitigates physical damage |
| `SPD` | ATB gauge fill per tick, at least `MON_SPD_MIN` |
| `LUK` | Crit chance |

Effects can also add to `crit_rate` and `crit_dmg` directly. The current value of a trained stat is `base x max(STAT_FLOOR_FRAC, 1 + sum of effect fractions)`: however many debuffs stack, it never drops below `STAT_FLOOR_FRAC` (0.1) of its base. A permanent HP effect (a passive) multiplies max HP and current HP by `max(STAT_FLOOR_FRAC, 1 + fraction)`.

The player reads this table in each stat's tooltip (section 9).

## 4. Turn order

Each tick:

1. The first tick only: every unit's `BattleStart` triggers fire (passives), in unit order. If that wipes a side, the fight ends there (side A counts as acting).
2. Every living unit adds `max(MON_SPD_MIN, SPD)` to its gauge.
3. While a living unit has gauge `>= BATTLE_GAUGE`: the highest gauge acts and pays `BATTLE_GAUGE`. Ties go by unit order: side A slot 0..n, then side B slot 0..n. After each action, a side with no living unit loses; if one action wipes both sides, the acting side wins.
4. Timed effects, shields and taunts age by one tick.
5. If the tick count reaches `MON_TIME_LIMIT_TICKS`, the fight ends `TimedOut`. Callers decide what a timeout means (a loss for the challenger, proposed).

A "turn" for durations is `BATTLE_GAUGE / SPD_REF` ticks (10 ticks, one action at SPD 100).

## 5. Actions

### 5.1 Skills

`CombatSkill` uses the kit vocabulary the idle game's battle introduced: `Kind` (damage, heal, shield, taunt, buff, debuff, passive), `Target` (enemy, all_enemies, self, lowest_ally, all_allies), `Stat` (what a buff/debuff/passive changes), `Scales` (the trained stat that sets the amount; `hp` means max HP), `Power`, `Cooldown` (own actions), `Duration` (turns), and an optional `Element` (default: the user's).

- Damage scaled by INT or WIS is **magical**; everything else is **physical**.
- Heal: `Scales x Power` to the target(s), capped at max HP. Shield: absorbs `Scales x Power` for `Duration` turns. Taunt: single-target enemy actions must target this unit for `Duration` turns.
- Buff/debuff: `Power` as a fraction of base for trained stats, added directly for crit stats; `Duration` turns. Passive: the same, permanent, at battle start. A permanent HP passive raises max and current HP together.

### 5.2 Default AI

Both sides use the same AI, so PvP stays symmetric. On its action a unit fires its `OnAction` triggers, ticks its cooldowns down by one, then uses the first skill that is ready and would do something. Rescue skills (heal, shield) are considered first, in list order, so a hurt ally is not left waiting behind a damage skill; then every other skill, in list order:

- damage: an enemy is alive; heal: an ally is below `AI_HEAL_BELOW` of max HP; shield: below `AI_SHIELD_BELOW`; taunt: not already taunting; buff: the target lacks this skill's effect; debuff: a living enemy lacks it.
- A skill used with `Cooldown` N is ready again on the Nth action after it (cooldown 0 or 1: every action).

Otherwise it fires `OnBasicAttack` triggers and makes a basic attack: `BASIC_ATTACK_POWER x max(STR, INT)`, magical when INT is higher.

**Targeting:** a single-target enemy action hits a taunting enemy (lowest slot) if there is one, else a random living enemy (one draw; no draw when only one is alive). Placeholder until gambits.

### 5.3 A hit

```
evade  = clamp(MON_EVADE_BASE + (DEX_target - DEX_attacker) x MON_EVADE_PER_DEX, 0, MON_EVADE_CAP)   physical only
crit   = clamp(MON_CRIT_BASE + LUK x MON_CRIT_PER_LUK + crit_rate effects, 0, MON_CRIT_CAP)
damage = raw x (crit ? max(1, MON_CRIT_DMG + crit_dmg effects) : 1) x element x K / (K + DEF or WIS)
K      = MON_DEF_K
element = ELEMENT_STRONG_MULT if the attack's element beats the target's, ELEMENT_WEAK_MULT if the reverse, else 1
```

- Draw order: the evade roll, then the crit roll, each only when its chance is above zero. An evaded hit deals nothing and skips the crit roll.
- The shield absorbs first, then HP. Damage dealt is capped at the target's remaining HP (no overkill in tallies).
- After the hit: the target's `OnHitTaken` triggers, then the attacker's `OnCrit` triggers on a crit. An evade fires the target's `OnEvade` triggers.

## 6. Triggers and rule modifiers

### 6.1 Triggers

`FightTrigger`: `BattleStart`, `OnAction`, `OnBasicAttack`, `OnCrit`, `OnHitTaken`, `OnEvade`. A fighter's `Triggers` list pairs a trigger with a `CombatSkill`; when the trigger fires, the skill does what it would do as an action (a damage trigger hits, a buff trigger buffs). Passives in `Skills` count as `BattleStart` triggers. Tree nodes and chase traits (T3) will produce these entries. A dead unit's triggers do not fire. Triggered effects ignore cooldowns and the AI's conditions, so a triggered buff or debuff adds another stack each time it fires.

### 6.2 Rule modifiers

Behaviour a table row cannot express is coded once and referenced by id from content (`specs/13` section 7). `Start` rejects unknown ids.

| Id | Effect |
|---|---|
| `counter_mitigated` | After taking a single-target physical hit and surviving, counterattack the attacker at once for the damage DEF mitigated, as true damage (no evade, crit, element or mitigation; shields still absorb), regardless of gauge. The drafts' *Heavy Armor* + *Unbreakable Will* passive (`specs/12` section 5.3); the *Unbreakable Will* chase capstone. |
| `echo_cast` | Every active damage, heal or shield skill the unit uses is cast again at once, at `MON_ECHO_POWER` of its power, within the same action. Passives, triggered effects and basic attacks do not echo. The *Arcane Overflow* chase capstone (`specs/15`). |
| `tidal_overflow` | At the start of each of its actions (before its `OnAction` triggers) the unit heals `MON_TIDE_REGEN` of its max HP. Any healing it receives beyond max HP (this regeneration, heal skills, `abyssal_drain`) becomes shield, up to `MON_TIDE_SHIELD_CAP` of its max HP; the shield is the unit's one shield pool, so a timed shield's expiry clears it too and a bigger skill shield is not cut down. Only HP counts as healing done. Corruption scales the regeneration like any heal. Logged as the event `rule:tidal_overflow`. The *Living Ocean* chase capstone. |
| `abyssal_drain` | Each hit the unit lands (skills, basic attacks, triggered damage) heals it for `MON_DRAIN_FRAC` of the HP damage the hit dealt (after shields), right after the hit and before the target's `OnHitTaken` triggers. Counters, channel strikes and other true damage do not drain. Logged as `rule:abyssal_drain`. The *Abyssal Bloom* chase capstone. |
| `fire_pierce` | The unit's `fire` damage ignores `MON_FIRE_PIERCE` of the target's DEF or WIS and is never resisted: the element multiplier is at least 1 (a strong matchup still counts). The *Inferno Sovereign* chase capstone. |
| `double_turn_start` | Each of the unit's first `MON_DOUBLE_TURNS` actions of the fight is followed at once by an extra full action (cooldowns tick, `OnAction` triggers fire, the AI picks again) without spending gauge. Logged as `rule:double_turn_start` before the extra action. The *Starlit Ascension* chase capstone. |
| `entropy_convert` | Tower fights only (`specs/21` section 3.1): instead of venting its entropy gauge, the unit gains a shield. Inert in other fights. |

The entropy gauge, boss phases and the environmental clock (`specs/12` section 8.3, `specs/13` T10) are built: `FightRules.Tower` (`TowerRules`), `Fighter.Phases` and the tower fields of `FightState` and `FightUnit`, specified in `specs/21` section 3. They are off unless the caller sets `Tower`, so existing fights and hashes do not move. Two more bonus-only effect stats come with them, `dmg_dealt` and `entropy_gain` (`StatIds`), valid for skills and tree nodes like `crit_rate`.

### 6.3 Tallies

Each unit records actions, damage dealt and taken, healing done, crits, evades (hits it evaded), kills, counters and damage dealt per element. The run engine reads them for playstyle conditions such as "deal X fire damage" or "evade N attacks" (`specs/12` section 5.5).

## 7. Constants

All placeholders (content `2.3`; since 3.0 only the groups here and in `specs/14` to `19` exist). Also read by the battle: `BATTLE_GAUGE`, `SPD_REF` (turn length), `ELEMENT_STRONG_MULT`, `ELEMENT_WEAK_MULT`, `BASIC_ATTACK_POWER`, `AI_HEAL_BELOW`, `AI_SHIELD_BELOW`, and the `elements` wheel.

| Name | Value | Range | Use |
|---|---|---|---|
| `MON_HP_PER_POINT` | 10 | `> 0` | Max HP per HP point |
| `MON_DEF_K` | 300 | `> 0` | Mitigation constant: DEF or WIS 300 halves damage |
| `MON_EVADE_BASE` | 0.05 | `[0, 1]` | Evade chance at equal DEX |
| `MON_EVADE_PER_DEX` | 0.0005 | `>= 0` | Evade per point of DEX difference |
| `MON_EVADE_CAP` | 0.5 | `[0, 1]` | Evade ceiling |
| `MON_CRIT_BASE` | 0.05 | `[0, 1]` | Crit chance at LUK 0 |
| `MON_CRIT_PER_LUK` | 0.0005 | `>= 0` | Crit chance per LUK |
| `MON_CRIT_CAP` | 0.75 | `[0, 1]` | Crit ceiling |
| `MON_CRIT_DMG` | 1.5 | `>= 1` | Crit damage multiplier |
| `MON_SPD_MIN` | 10 | `> 0` | Minimum gauge fill per tick |
| `MON_TIME_LIMIT_TICKS` | 900 | `> 0` | Turn limit (90 s) |
| `MON_SIDE_SIZE` | 4 | `> 0` | Most units per side |
| `STAT_FLOOR_FRAC` | 0.1 | `(0, 1]` | A trained stat never drops below this fraction of its base (section 3; content `3.2`) |
| `MON_ECHO_POWER` | 0.5 | `[0, 1]` | `echo_cast` repeat power (content `2.5`) |
| `MON_TIDE_REGEN` | 0.05 | `[0, 1]` | `tidal_overflow` heal per own action, fraction of max HP (content `3.6`) |
| `MON_TIDE_SHIELD_CAP` | 0.3 | `[0, 1]` | `tidal_overflow` shield cap, fraction of max HP (content `3.6`) |
| `MON_DRAIN_FRAC` | 0.3 | `[0, 1]` | `abyssal_drain` share of damage dealt healed (content `3.6`) |
| `MON_FIRE_PIERCE` | 0.5 | `[0, 1]` | `fire_pierce` share of DEF or WIS ignored (content `3.6`) |
| `MON_DOUBLE_TURNS` | 1 | `>= 1` | `double_turn_start` doubled opening actions (content `3.6`) |

Range violations are `E043` in both `tools/content_build.py` and `ContentDb`. `BattleSim.Start` also rejects a hand-built `FightRules` whose rule modifier values are outside these ranges (`DoubleTurns` may be 0 there: the rule does nothing).

## 8. Determinism and goldens

- No clock, IO, reflection or `Math.Pow`; every draw comes from the `IRandomSource` passed in (`specs/00` section 5).
- `BattleSim.StateHash` is SHA-256 over a canonical encoding of the whole fight (`StateHasher` conventions; version `HashVersion` = 1). Changing the encoding bumps the version.
- `golden/battle-sim-vectors.txt` resolves seven fixed fights (1v1 twice, 4v1, 4v4, 2v3, a 1v1 and a 2v2 of chase capstone carriers) with placeholder fighters defined in `src/IdleRng.Bots/Golden/BattleVectors.cs`, printing outcome, ticks, each unit's HP and tallies, and the hash. It covers every skill kind, a trigger and every chase capstone rule modifier. Any rule or content change that moves it is regenerated with `golden --update` and noted in the merge commit.

## 9. Descriptions (tooltips)

The web client explains stats and abilities in tooltips (issue #80). The words are content strings and every number comes from the data or formula it describes, so the text cannot drift from the fight. `BattleText` (`src/IdleRng.Sim/Combat/BattleText.cs`) builds them; `BattleTextTests` checks that every placeholder resolves.

- **Formulas.** The fight's own formulas are public helpers on `BattleSim` (`MaxHp`, `GaugeFill`, `BasicAttackRaw`, `BasicAttackMagical`, `EvadeChance`, `CritChance`, `Mitigate`); the battle and the tooltips both call them.
- **Stats.** `stat.<id>.name` ("Strength"), `stat.<id>.desc` (what the stat does, with constants such as `{MON_DEF_K}` filled in) and `stat.<id>.now` (what the current value does against the coming opponent: `{max_hp}`, `{basic}`, `{basic_type}`, `{def_taken}`, `{wis_taken}`, `{evade}`, `{foe_evade}`, `{acts}` actions per turn, `{crit}`, `{foe}`), under the heading `tip.now`. "Now" lines use the raw stat sheets: before buffs, debuffs and passives. `stat.<id>.label` names a stat inside a sentence where the upper-case id would read wrong (`hp`: "max HP", `crit_rate`, `crit_dmg`).
- **Skills.** `mskill.<id>.desc` is a template over the skill's own row: `{power}` (or `{power:pct}`), `{scales}`, `{stat}`, `{cooldown}`, `{duration}`, `{ticks}`, `{element}`. A tree node's tooltip adds tags (kind, physical or magical, element, trigger, chase trait) and facts from the row and the rules above: effect, amount, damage type, element, target, duration, cooldown and the AI condition for active skills, when it fires for triggers and passives (`trigger.<id>.name`, `tip.fires.<id>`), stacking for triggered buffs, and the node's SP cost. The vocabulary is `tip.*`, with constants filled in (`tip.effect.debuff` names `{STAT_FLOOR_FRAC:pct}`). Abilities have no energy cost: energy is a training resource (`specs/14`); a skill's cost is its node's SP.
- **Rules.** `rule.<id>.desc`, with constants filled in (`echo_cast` names `{MON_ECHO_POWER:pct}`). A rule modifier's log event (`FightAction.Event` `rule:<id>`) shows under `rule.<id>.name` in the battle log.

Template syntax and the constant lookup are `specs/05` section 5.

## 10. Open

- What a timeout means per mode (proposed: the challenger loses; rank fights already treat it as a loss).
- Gambit rulebooks replace the default AI (T11).
- Whether skills get their own elements in content (supported by the engine; content decides at T3).
