# 16 - Monster Battle

Status: **live (T1 of `specs/13`)**. Used by the training run's rank fights (`specs/14` section 5); the gauntlet (T4), towers and PvP will call it too. Code: `src/IdleRng.Sim/Combat`. Tests: `src/IdleRng.Sim.Tests/BattleSimTests.cs`, `GambitTests.cs`. Golden: `golden/battle-sim-vectors.txt`.

It replaced the idle game's 4v1 boss battle, which was removed with that game at `specs/13` T7; it was rebuilt from the same design (ATB gauge, kit vocabulary, damage formula, triggers).

> **Numbers are placeholders.** Every formula constant here is a placeholder until balance resumes (`CLAUDE.md`).

## 1. Shape

- Two sides, A and B, of 1 to `MON_SIDE_SIZE` (4) units each at the start (units that join mid-fight, section 6.4, are adds; `arise`'s risen allies may take a side past it). 1v1 (gauntlet, in-run Tactical Challenges), 4v1 (Dungeon bosses) and 4v4 (Endless Tower, PvP) are all the same engine.
- Turn order is an ATB gauge; actions are automatic (default AI, section 5.2). A unit may carry a gambit rulebook (`Fighter.Gambits`, `specs/28` section 4), checked before the default AI; with none it plays exactly as the default AI.
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
| `DEX` | Evasion (physical hits miss, magical hits graze), both sides' DEX count; crit damage (`MON_CRIT_DMG_PER_DEX` per point). Retuned in content 4.6 (kj, 2026-10-31, `specs/25` section 6) |
| `DEF` | Mitigates physical damage |
| `SPD` | ATB gauge fill per tick, at least `MON_SPD_MIN` |
| `LUK` | Crit chance; fortune: rerolls the unit's own failed evade and failed crit, and makes a crit against it roll again (content 4.6, `specs/25` section 6) |

Effects can also add to `crit_rate` and `crit_dmg` directly. The current value of a trained stat is `base x max(STAT_FLOOR_FRAC, 1 + sum of effect fractions)`: however many debuffs stack, it never drops below `STAT_FLOOR_FRAC` (0.1) of its base. HP effects work on max HP (issue #83, kj 2026-10-31): a permanent HP effect (a passive) multiplies base max HP by `max(STAT_FLOOR_FRAC, 1 + fraction)`, and max HP is `base max HP x max(STAT_FLOOR_FRAC, 1 + sum of timed HP effect fractions)`. Whenever max HP changes (a passive, a timed HP buff or debuff landing or expiring), current HP is scaled with it, so the unit keeps its fraction of max HP: a full unit stays full, and an expiring buff cannot kill.

**Stacking** (issue #83, kj 2026-10-31): a unit carries at most `EFFECT_STACK_CAP` (3) copies of one timed effect, counted per source skill and stat. A copy added at the cap is not added: instead the copy with the fewest ticks left gets the new duration (if that is longer). Permanent effects (passives, boss phase buffs) are not capped.

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
- Buff/debuff: `Power` as a fraction of base for trained stats, added directly for crit stats; `Duration` turns. Passive: the same, permanent, at battle start. HP buffs, debuffs and passives change max HP and scale current HP with it (section 3).

### 5.2 Default AI

Both sides use the same AI, so PvP stays symmetric. On its action a unit fires its `OnAction` triggers, ticks its cooldowns down by one, then uses the first skill that is ready and would do something. Rescue skills (heal, shield) are considered first, in list order, so a hurt ally is not left waiting behind a damage skill; then every other skill, in list order:

- damage: an enemy is alive; heal: a living target is missing at least the HP the heal would restore now (`Scales x Power`, after the tower's heal cut), so none of it is wasted (kj on issue #94, 2026-10-31: use it once it can land in full, to make the most of the cooldown); a heal too big for that (more than `1 - AI_HEAL_BELOW` of the target's max HP) waits until the target is at or below `AI_HEAL_BELOW` of max HP instead (`missing >= min(heal, (1 - AI_HEAL_BELOW) x max HP)`, and the target is hurt). The "would the ally die before my next action" case is not considered. The targets checked are the skill's own (self for a self heal, every living ally for a lowest-ally or all-allies heal); shield: an enemy is alive and one of the skill's targets (self, the lowest ally, or any living ally for all-allies) has less shield than this cast would give (`Scales x Power`, after the tower's cut). A shield protects HP not lost yet, so unlike a heal it does not wait for damage: it goes up before the first hit and again once it has broken or worn below a fresh one (kj, 2026-10-31: the heal's rationale does not apply to shields; this replaced the old `AI_SHIELD_BELOW` HP threshold); taunt: not already taunting and a living ally to protect (alone, a taunt changes nothing; content 4.7, issue #117); buff: the target lacks this skill's effect; debuff: a living enemy lacks it.
- A skill used with `Cooldown` N is ready again on the Nth action after it (cooldown 0 or 1: every action).

Otherwise it fires `OnBasicAttack` triggers and makes a basic attack: `BASIC_ATTACK_POWER x max(STR, INT)`, magical when INT is higher.

A unit with a gambit rulebook checks its rules first, after the cooldown tick; the first rule that fires is its action, and if none does the default AI above acts, skipping the skills any rule names (`specs/28` section 4.3).

**Targeting:** a single-target enemy action hits a taunting enemy (lowest slot) if there is one, else the acting gambit rule's pick, when it has one (`lowest_hp` or `highest_hp`, no draw; `specs/28` section 4.2), else a random living enemy (one draw; no draw when only one is alive). A lowest-ally heal goes to the ally with the lowest HP fraction among those missing its full amount (the rule above; lowest slot on a tie), else, when none is (a triggered heal), to the ally with the lowest HP fraction. A single-target debuff (an action or a trigger) first narrows the choice to the living enemies that lack its effect, when any does, and applies the same rule among them: so it skips a taunter that already has it (issue #83, kj 2026-10-31).

### 5.3 A hit

```
evade   = clamp(MON_EVADE_BASE + (DEX_target - DEX_attacker) x MON_EVADE_PER_DEX, 0, MON_EVADE_CAP)
fortune = min(MON_FORTUNE_CAP, LUK / (LUK + MON_FORTUNE_K))                                    0 at LUK 0
crit    = clamp(MON_CRIT_BASE + LUK x MON_CRIT_PER_LUK + crit_rate effects, 0, MON_CRIT_CAP)
damage  = raw x (graze ? MON_GRAZE_MULT : 1) x (crit ? max(1, MON_CRIT_DMG + DEX_attacker x MON_CRIT_DMG_PER_DEX + crit_dmg effects) : 1)
          x element x K / (K + DEF or WIS)
K      = MON_DEF_K
element = ELEMENT_STRONG_MULT if the attack's element beats the target's, ELEMENT_WEAK_MULT if the reverse, else 1
```

- **Avoid roll** (kj, 2026-10-31, `specs/25`): every hit rolls against the target's evade chance. A physical hit that rolls under it is evaded: it deals nothing. A magical hit that rolls under it is **grazed**: it deals `MON_GRAZE_MULT` of its damage, cannot crit, is not an evade (no `OnEvade`, no evade tally; it counts in the target's `Grazes`) and otherwise lands like any hit. `umbral_veil` turns the target's grazes into evades.
- **Fortune** (the same decision): a roll a unit wants (its own evade when it is hit, its own crit when it hits) that fails is rerolled once with chance = its fortune. A crit that lands against a unit rolls again with chance = that unit's fortune, and stands only if the new roll also crits. `FightAction.Lucky` marks a hit whose outcome a reroll changed (an evade or graze saved, a crit gained or denied).
- Draw order: evade (when its chance is above zero), then on a failed evade the target's fortune draw (when its fortune is above zero) and, on a success there, the evade again; then, unless evaded or grazed, the crit roll (when its chance is above zero), on a failed crit the attacker's fortune draw and maybe the crit again, and on a landed crit the target's fortune draw and maybe the crit again. An evaded hit deals nothing and skips the crit roll.
- The shield absorbs first, then HP. Damage dealt is capped at the target's remaining HP (no overkill in tallies).
- After the hit: the target's `OnHitTaken` triggers, then the attacker's `OnCrit` triggers on a crit. An evade fires the target's `OnEvade` triggers.
- Rule hooks (section 6.4) sit at fixed points of a hit: before it resolves (it may be cancelled), the **conversion step** (once per hit, before the avoid roll and every multiplier), the outgoing and incoming multipliers (after crit, element and `dmg_dealt`, before mitigation), then the landed and taken hooks after the shield and HP, before the `OnHitTaken` triggers. All are empty until the idea-pool lanes fill them.

## 6. Triggers and rule modifiers

### 6.1 Triggers

`FightTrigger`: `BattleStart`, `OnAction`, `OnBasicAttack`, `OnCrit`, `OnHitTaken`, `OnEvade`. A fighter's `Triggers` list pairs a trigger with a `CombatSkill`; when the trigger fires, the skill does what it would do as an action (a damage trigger hits, a buff trigger buffs). Passives in `Skills` count as `BattleStart` triggers. Tree nodes and chase traits (T3) will produce these entries. A dead unit's triggers do not fire. Triggered effects ignore cooldowns and the AI's conditions, so a triggered buff or debuff adds another stack each time it fires, up to `EFFECT_STACK_CAP` (section 3). The loop guards of section 6.4 limit what a triggered effect can fire in turn.

### 6.2 Rule modifiers

Behaviour a table row cannot express is coded once and referenced by id from content (`specs/13` section 7). `Start` rejects unknown ids.

| Id | Effect |
|---|---|
| `counter_mitigated` | After taking a single-target physical hit and surviving, counterattack the attacker at once for `MON_COUNTER_MULT` times the damage DEF mitigated, as true damage (no evade, crit, element or mitigation; shields still absorb), regardless of gauge. The drafts' *Heavy Armor* + *Unbreakable Will* passive (`specs/12` section 5.3); the *Unbreakable Will* chase capstone. |
| `echo_cast` | Every active damage, heal or shield skill the unit uses is cast again at once, at `MON_ECHO_POWER` of its power, within the same action. Passives, triggered effects and basic attacks do not echo. Its mirror twin also takes the first `MON_ECHO_DECOYS` hits aimed at it each fight (any hit, physical or magical: the hit is logged as evaded, then `rule:echo_cast` with the decoys left; hashed only for units with the rule). The *Arcane Overflow* chase capstone (`specs/15`). |
| `tidal_overflow` | At the start of each of its actions (before its `OnAction` triggers) the unit heals `MON_TIDE_REGEN` of its max HP. Any healing it receives beyond max HP (this regeneration, heal skills, `abyssal_drain`) becomes shield, up to `MON_TIDE_SHIELD_CAP` of its max HP; the shield is the unit's one shield pool, so a timed shield's expiry clears it too and a bigger skill shield is not cut down. Only HP counts as healing done. Corruption scales the regeneration like any heal. Logged as the event `rule:tidal_overflow`. The *Living Ocean* chase capstone. |
| `abyssal_drain` | Each hit the unit lands (skills, basic attacks, triggered damage) heals it for `MON_DRAIN_FRAC` of the HP damage the hit dealt (after shields; may be above 1), right after the hit and before the target's `OnHitTaken` triggers. Draining beyond max HP becomes shield up to `MON_DRAIN_SHIELD_CAP` of max HP (the larger of that and `tidal_overflow`'s cap when the unit has both). Counters, channel strikes and other true damage do not drain. Logged as `rule:abyssal_drain`. The *Abyssal Bloom* chase capstone. |
| `fire_pierce` | The unit's `fire` damage is multiplied by 1 + `MON_FIRE_BONUS`, ignores `MON_FIRE_PIERCE` of the target's DEF or WIS and is never resisted: the element multiplier is at least 1 (a strong matchup still counts). The *Inferno Sovereign* chase capstone. |
| `double_turn_start` | Each of the unit's first `MON_DOUBLE_TURNS` actions of the fight is followed at once by an extra full action (cooldowns tick, `OnAction` triggers fire, the AI picks again) without spending gauge, within `MON_EXTRA_ACTIONS_CAP` (section 6.4). Logged as `rule:double_turn_start` before the extra action. The *Starlit Ascension* chase capstone. |
| `root_rebirth` | Once per fight, damage from another unit (a hit, a counter or a channel strike; not its own entropy recoil) that would bring the unit to 0 HP leaves it at `MON_REBIRTH_HEAL` of its max HP instead; no kill is counted. Logged as `rule:root_rebirth` after the action that triggered it, with the HP it came back with. Its remaining rebirth is hashed only for units that carry the rule. The *World Root* chase capstone (Mandrake). |
| `wail_stagger` | At the start of each of the unit's actions (before its `OnAction` triggers), every living enemy loses `MON_WAIL_STAGGER` of a full ATB gauge (never below 0) and takes `MON_WAIL_WITHER` of its own max HP as true damage. Logged as `rule:wail_stagger` per enemy with the damage dealt. The *Mandragora Scream* chase capstone (Mandrake). |
| `umbral_veil` | Magical hits that roll under the unit's evade chance are evaded instead of grazed, gains `MON_VEIL_EVADE_BONUS` evade chance, and its evade chance is capped at `MON_VEIL_EVADE_CAP` instead of `MON_EVADE_CAP`. The *Umbral Veil* chase capstone (Shade Hound). |
| `night_feast` | The unit starts every fight with `MON_FEAST_START` damage dealt (like a `dmg_dealt` effect), and each hit it lands that deals damage adds `MON_FEAST_DMG` more damage dealt and `MON_FEAST_STR` of its base STR for the rest of the fight, with no cap (they count like permanent effects, outside `EFFECT_STACK_CAP`). Logged as `rule:night_feast` with the STR plus damage bonus so far; hashed only for units that carry the rule. The *Night Feast* chase capstone (Shade Hound). |
| `entropy_convert` | Tower fights only (`specs/21` section 3.1): instead of venting its entropy gauge, the unit gains a shield. Inert in other fights. |
| `iron_reflexes` | Idea-pool lane A (#134), the *Iron Reflexes* generic capstone (DEX). In the conversion step (section 6.4), a hit on the unit has an avoid chance of 0: it is never evaded or grazed, and this wins over `umbral_veil` (whose bonus and cap then act on nothing). A physical hit on it is mitigated by DEF + `MON_IRON_DEX` x DEX; a magical one by WIS as before (agent: like PoE's armour, the DEX only guards against physical hits). Logged once per fight at the start as `rule:iron_reflexes` with the DEF its DEX adds. Modes: the same for every hit in 1v1, 4v1 and 4v4 (each target of an area hit converts on its own); allies are unaffected. |
| `elemental_avatar` | Idea-pool lane A (#134), the *Elemental Avatar* generic capstone (INT). In the conversion step every hit the unit deals (skills, basic attacks, triggered hits, echoes) takes the unit's own element, so the element matchup and `fire_pierce` read that element; then, with the outgoing multipliers, the hit deals x(1 + `MON_AVATAR_BONUS`). Counters, `wail_stagger` and channel strikes are not hits and keep no element. A unit with no element is unaffected. Logged once per fight at the start as `rule:elemental_avatar` with the bonus. Modes: in 1v1 the matchup is fixed per opponent, good or bad; in 4v1 it is set against the boss's element; in 4v4 an area hit meets each target's element separately. |
| `fortress_fist` | Idea-pool lane A (#134), the *Fortress Fist* generic capstone (DEF). In the conversion step the unit's STR and INT count as 0 for damage: a basic attack and a skill scaling STR or INT bring no damage of their own (a basic attack is then physical, since INT is not above STR), skills scaling any other stat keep theirs, and every hit it deals adds `MON_FORTRESS_DEF` x its DEF to the raw damage, before crit, element and mitigation. Heals and shields that scale from STR or INT are unchanged (agent decision, #134). Counters and other true damage are not hits. Logged once per fight at the start as `rule:fortress_fist` with the damage each hit adds. Modes: the same in 1v1, 4v1 and 4v4; an area skill adds the DEF part to each target's hit. |
| `blood_magic` | Idea-pool lane A (#134), the *Blood Magic* generic capstone (HP). The unit's active skills (not passives) have no cooldown: each is ready whatever its cooldown, and casting leaves it at 0. Each cast as an action (default AI or gambit; an `echo_cast` repeat is part of the cast) costs `MON_BLOOD_COST` of max HP after the cast, never taking the unit below 1 HP. The cost is not damage: shields do not take it, and it fires no hooks, triggers or tallies (`OnHitTaken`, `night_feast` and later lane B's `resentment` do not see it). A skill is not ready, for the AI and gambits alike, when the cost would leave the unit under `MON_BLOOD_FLOOR` of max HP; it then uses basic attacks. Triggered skills and basic attacks cost nothing. Logged as `rule:blood_magic` after each cast with the HP it cost. Modes: in 1v1 it front-loads its skills and falls back to basic attacks; in 4v1 and 4v4 an ally's heals refill the HP it spends, so it casts for longer. |
| `resentment` | Idea-pool lane B (#135), the *Resentment* generic capstone (HP). `MON_RESENT_FRAC` of the HP damage an enemy deals the unit (after shields and `mana_ward`'s ward, up to the HP it had: no overkill) goes into a red pool, which adds to its STR as a flat amount (after its STR effects and floor). Each of its own actions removes `MON_RESENT_DECAY` of the pool, after the action. Only damage from the other side counts: its own entropy recoil and lane A's `blood_magic` cost (not damage) do not. Logged as `rule:resentment` with the pool after each gain; the pool is hashed only for carriers. Modes: 1v1 (run fights, gauntlet) banks the one opponent's hits; 4v1 (Dungeon) banks the boss's hits, area ones included, and resets each floor (a fresh fight); 4v4 (Endless, PvP) banks hits from any enemy. |
| `vantage` | Idea-pool lane B (#135), the *Vantage* generic capstone (SPD). While the unit is below `MON_VANTAGE_HP` of its max HP, an enemy's single-target damaging hit aimed at it (a skill or basic attack as the enemy's action, or its `echo_cast` repeat) first meets a basic attack from the unit on the attacker, once per enemy action (the echo is the same action). If that strike kills the attacker, or the unit falls meanwhile (its entropy recoil after the strike, in a Dungeon fight), the hit is cancelled. The strike is a counter under the guards of section 6.4: it never provokes `vantage` or `counter_mitigated`, triggered hits and area hits never provoke it, and it adds entropy like a counter in Dungeon fights. It counts in the unit's `counters` tally. Logged as `rule:vantage` (amount 0) before the strike. Modes: 1v1 answers each of the opponent's single-target actions; 4v1, the boss's single-target ones (never its area attacks); 4v4, several enemies acting in a row each provoke one strike. |
| `deaths_door` | Idea-pool lane B (#135), the *Death's Door* generic capstone (LUK). Once per fight, a blow that would kill the unit (after `root_rebirth`, which goes first: agent decision, #135) leaves it at 1 HP, on Death's Door; no kill is counted. While on the door, each later blow that would kill it is survived at 1 HP with a chance of `MON_DOOR_START` minus `MON_DOOR_STEP` per blow already survived there: one draw per such blow, carriers only, and no draw once the chance is 0. Any damage counts as a blow: hits, counters, `wail_stagger` and other true damage, channel strikes and its own entropy recoil (through the would-die hook). Logged as `rule:deaths_door` with the odds of each roll (1 for the free save); the door state is hashed only for carriers. Modes: the same in 1v1, 4v1 and 4v4; in the Dungeon it resets each floor. The chance falls to 0 within `MON_DOOR_START / MON_DOOR_STEP` rolls, so it cannot stall a fight. |
| `barricade` | Idea-pool lane B (#135), the *Barricade* generic capstone (DEF). Every timed shield the unit gains (shield skills, its own or an ally's, and `entropy_convert`) never expires and adds to the shield it has, up to `MON_BARRICADE_CAP` of its max HP (a single shield bigger than that still counts whole). Overhealing that becomes shield (`tidal_overflow`, `abyssal_drain`) fills up to the larger of `MON_BARRICADE_CAP` and that rule's own cap. Logged as `rule:barricade` with the shield after each gain. Modes: 1v1 and 4v1 as above; in 4v4 an ally's area shield stacks on the carrier only. Hits still break the shield, so it does not stall fights (checked in `balance`: gauntlet and Dungeon fight lengths and timeouts). |
| `mana_ward` | Idea-pool lane B (#135), the *Mana Ward* generic capstone (WIS). At the start of each fight (before the `BattleStart` triggers) the unit gets a ward of `MON_WARD_WIS` x its WIS, a pool apart from the shield. The ward takes `MON_WARD_SHARE` of the HP damage of every blow (after the shield, before HP and `resentment`) until it runs out. While the ward holds, each of the unit's actions ticks its cooldowns `MON_WARD_CDR` more (whole ticks; the rest carries to its next action). Logged as `rule:mana_ward` with the ward left, at the start and after each blow it takes; hashed only for carriers. The fight UI shows the ward on the HP bar (◈). Modes: the same in 1v1, 4v1 and 4v4; in the Dungeon it refills each floor. |
| `lone_wolf` | Idea-pool lane E (#138), the *Lone Wolf* generic capstone (STR). Decided once, at the start of the fight (before the `BattleStart` triggers): if the unit's side has exactly one unit, every trained stat of the unit (max HP too) rises by `MON_LONE_BONUS` for the fight; otherwise every one falls by `MON_LONE_MALUS`. Both are permanent effects (source `rule:lone_wolf`), so `STAT_FLOOR_FRAC` still holds under the malus and the HP change keeps the unit at full HP. A unit that joins later (an `arise` risen ally, a split phase's add) does not change it. Logged as `rule:lone_wolf` with the change (+0.4 or -0.2). Modes: 1v1 (run fights, gauntlet) always the bonus; 4v1 (Dungeon) the bonus only for a monster sent in alone, else the malus (the boss is alone but carries no trees); 4v4 (Endless, PvP) always the malus. A Vault-identity choice: strong in the run and the gauntlet, weak in teams. |
| `arise` | Idea-pool lane E (#138), the *Arise* generic capstone (INT). Once per fight, the first enemy the unit kills rises on the unit's side, a mid-fight unit (section 6.4) with `MON_ARISE_STATS` of the fallen unit's trained stats, its element, skills and triggers (its passives apply as it joins), but no rule modifiers, boss phases or gambit rulebook (the default AI). Its id is the fallen unit's plus `#risen`. The kill is the unit's own blow (`KilledBy`); the unit must be alive when the death is processed (after the action). It does not happen when the fallen unit was the last one keeping its side alive (the fight is over), and a risen unit never rises again; neither spends the rise. The risen ally is an add: it never keeps its side alive ("last unit standing"), its kills and tallies are its own (the run, the gauntlet and the Dungeon's team tallies read only the monsters), and it is outside `MON_SIDE_SIZE` (agent decision, #138: the room check would make the rule do nothing in the Dungeon and in full 4v4 teams; at most one risen ally per carrier). Gambit conditions and targets see it as any unit. Logged as `rule:arise` (actor the carrier, target the risen id, amount `MON_ARISE_STATS`) as it joins, before its passives; `AriseUsed` is hashed for carriers and a risen unit hashes a marker. Modes: 1v1 never fires (the only kill is the last enemy); 4v1, a split phase's add killed while the boss lives rises; 4v4, the first of the carrier's kills before the last enemy. |
| `last_rite` | Idea-pool lane E (#138), the *Last Rite* generic capstone (HP). When the unit dies (the death hook; a `root_rebirth` or `deaths_door` save is not a death), each living ally on its side (a risen ally or an add too) gains `MON_RITE_SHARE` of the unit's base stats divided by the number of those allies, for the rest of the fight: a flat amount on each trained stat (after effects, like `StatFlat`), and for HP the max HP that share of its HP stat gives (`MaxHp`), added to max and current HP alike (it stays under later timed HP effects). Base stats are the unit's trained stats, before effects. Several carriers falling in one action feed only the allies still alive. Logged as `rule:last_rite` with each ally's share; the gains are hashed for the units that have them. Modes: 1v1 never fires (no ally); 4v1, a fallen monster feeds the others; 4v4, the same on either side. |

The entropy gauge, boss phases and the environmental clock (`specs/12` section 8.3, `specs/13` T10) are built: `FightRules.Tower` (`TowerRules`), `Fighter.Phases` and the tower fields of `FightState` and `FightUnit`, specified in `specs/21` section 3. They are off unless the caller sets `Tower`, so existing fights and hashes do not move. The Endless Tower's mutators (`FightRules.Mutators`, `specs/28` section 3.2) are off the same way. Two more bonus-only effect stats come with them, `dmg_dealt` and `entropy_gain` (`StatIds`), valid for skills and tree nodes like `crit_rate`.

### 6.3 Tallies

Each unit records actions, damage dealt and taken, healing done, crits, evades (hits it evaded), grazes (magical hits it grazed; not evades, so "evade N attacks" does not count them), kills, counters and damage dealt per element. The run engine reads them for playstyle conditions such as "deal X fire damage" or "evade N attacks" (`specs/12` section 5.5).

### 6.4 Trigger and rule guards (issue #133, 2026-11-03)

Loop guards, so that triggers and the idea-pool rules (epic #132) cannot chain without end. They are written once, in `BattleGuards.cs`, and every new rule goes through them.

- **No effect fires its own kind.** While a trigger of one kind runs (`BattleStart`, `OnAction`, `OnBasicAttack`, `OnCrit`, `OnHitTaken`, `OnEvade`), no trigger of that same kind fires, on any unit: an `OnCrit` effect that crits does not fire `OnCrit` again, and a hit dealt by an `OnHitTaken` effect does not fire the target's `OnHitTaken` triggers. Rule procs that act like triggers have their own kinds: a **counter** (`counter_mitigated`; lane B's `vantage`) is never countered, and an **echo** (`echo_cast`) is never echoed again. Lanes add their proc kinds to `GuardKind`.
- **Depth cap.** A trigger, or a counter, runs one level deeper than the effect that caused it; one that would go past `MON_TRIGGER_DEPTH` levels does not fire (the action itself is level 0). An echo is part of its cast and adds no level. With the placeholder 2, an action's crit can fire `OnCrit` (level 1) whose hit fires the target's `OnHitTaken` (level 2), and nothing more.
- **Extra actions cap.** After the action its gauge paid for, a unit takes at most `MON_EXTRA_ACTIONS_CAP` extra actions in a row (`double_turn_start` first, then the lanes' sources in lane order). Each extra action is logged by the rule that granted it.
- **Conversions once, before multipliers.** A hit's conversion step (section 5.3) runs once per hit, before the avoid roll, crit, element, `dmg_dealt`, mutator and rule multipliers. It may rewrite the hit's raw damage, damage type, element, the target's avoid chance and the stat that mitigates it; nothing after it converts again.

**Rule hooks.** `BattleSim` is a partial class. The pipeline calls named hooks at fixed points, and each idea-pool lane fills its own partial methods in `Combat/Rules/LaneA.cs` to `LaneE.cs` (declared in `Combat/Rules/RuleHooks.cs`; an unfilled one compiles away). Hooks, called lane A to E: `OnFightStart` (first step, before the `BattleStart` triggers), `BeforeAction` (may skip the action) and `AfterAction`, `ExtraAction` (grants an extra action, under the cap), `OnGaugeTick` (the tick's gauge fill), `BeforeHitResolves` (may cancel a hit), `ConvertHit` (the conversion step), `ModifyOutgoing` and `ModifyIncoming`, `OnHitLanded` and `OnHitTaken`, `ModifyHpDamage` (any damage, after the shield, before HP), `OnWouldDie` (a blow that would kill, after `root_rebirth`; also the unit's own entropy recoil, which `root_rebirth` does not cover), `StatFlat` (a flat amount added to a trained stat after its effects), `ShieldGain` (a skill's or `entropy_convert`'s timed shield: its size and timer) and `ShieldCap` (the overheal-to-shield cap), `SkillReady` (whether a skill may be used now), `OnSkillUsed`, `OnDeath` (once per unit, after the action that killed it; `KilledBy` names the killer), the state hash (`HashUnit`, `HashFight`), rule validation and loading the lane's constants, and cloning a unit's lane state. A rule runs only for units that carry it, draws only for them and hashes its state only for them, so a fight without the lane's rules plays out and hashes exactly as before. The training run has the same shape for lanes F and G (`specs/14` section 2).

**Mid-fight units.** A unit can join a fight after it started through `BattleSim.Summon` (lane E, #138): a split phase's adds (`specs/21` section 3.2) and lane E's `arise`. It takes the next slot on its side (after the side's dead units too, so slots stay unique), joins the end of `Units` (so it acts last in a tie and the state hash lists it last), starts with an empty gauge, full HP and its cooldowns ready, is an add (`IsAdd`: it never keeps its side alive, and the Dungeon leaves it out of its team tallies), and fires its own `BattleStart` triggers at once. The caller picks a new id and checks the side's room. A unit joining during the death step is visited by that same step (the death hooks run in `Units` order, by index).

Since the guards (content `6.4`), a `pyromancy` `wildfire` (`OnCrit`) hit that crits no longer fires `wildfire` again; that changed one Dungeon golden (drake `focus` seed 7: floor 9 to 8) and four Endless golden hashes. No other fight moved.

## 7. Constants

All placeholders (content `2.3`; since 3.0 only the groups here and in `specs/14` to `19` exist). Also read by the battle: `BATTLE_GAUGE`, `SPD_REF` (turn length), `ELEMENT_STRONG_MULT`, `ELEMENT_WEAK_MULT`, `BASIC_ATTACK_POWER`, `AI_HEAL_BELOW`, and the `elements` wheel (`AI_SHIELD_BELOW` is still in the table but unused since 2026-10-31: removing it needs a MAJOR content bump).

| Name | Value | Range | Use |
|---|---|---|---|
| `MON_HP_PER_POINT` | 10 | `> 0` | Max HP per HP point |
| `MON_DEF_K` | 300 | `> 0` | Mitigation constant: DEF or WIS 300 halves damage |
| `MON_EVADE_BASE` | 0.05 | `[0, 1]` | Evade chance at equal DEX |
| `MON_EVADE_PER_DEX` | 0.001 | `>= 0` | Evade per point of DEX difference (0.0005 before content `4.6`) |
| `MON_EVADE_CAP` | 0.5 | `[0, 1]` | Evade ceiling |
| `MON_CRIT_BASE` | 0.05 | `[0, 1]` | Crit chance at LUK 0 |
| `MON_CRIT_PER_LUK` | 0.0005 | `>= 0` | Crit chance per LUK |
| `MON_CRIT_CAP` | 0.75 | `[0, 1]` | Crit ceiling |
| `MON_CRIT_DMG` | 1.5 | `>= 1` | Crit damage multiplier before DEX |
| `MON_CRIT_DMG_PER_DEX` | 0.001 | `>= 0` | Crit damage multiplier per point of the attacker's DEX (content `4.6`) |
| `MON_GRAZE_MULT` | 0.25 | `[0, 1]` | Damage share of a grazed magical hit (content `4.6`) |
| `MON_FORTUNE_K` | 1000 | `> 0` | Fortune = LUK / (LUK + K): LUK 1000 gives half (content `4.6`) |
| `MON_FORTUNE_CAP` | 0.5 | `[0, 1]` | Fortune ceiling (content `4.6`) |
| `MON_SPD_MIN` | 10 | `> 0` | Minimum gauge fill per tick |
| `MON_TIME_LIMIT_TICKS` | 900 | `> 0` | Turn limit (90 s) |
| `MON_SIDE_SIZE` | 4 | `> 0` | Most units per side at the start (and with a split phase's adds; `arise`'s risen allies are outside it) |
| `STAT_FLOOR_FRAC` | 0.1 | `(0, 1]` | A trained stat never drops below this fraction of its base (section 3; content `3.2`) |
| `EFFECT_STACK_CAP` | 3 | `>= 1` | Most copies of one timed effect (same source skill and stat) on a unit; another refreshes the shortest (section 3; content `3.8`) |
| `MON_ECHO_POWER` | 1.0 | `[0, 1]` | `echo_cast` repeat power (content `2.5`; 4.4: 0.5 to 1.0) |
| `MON_TIDE_REGEN` | 0.02 | `[0, 1]` | `tidal_overflow` heal per own action, fraction of max HP (content `3.7`; 4.4: 0.05 to 0.02) |
| `MON_TIDE_SHIELD_CAP` | 0.3 | `[0, 1]` | `tidal_overflow` shield cap, fraction of max HP (content `3.7`) |
| `MON_DRAIN_FRAC` | 2.5 | `>= 0` | `abyssal_drain` share of damage dealt healed (content `3.7`; 4.4: 0.3 to 2.5) |
| `MON_FIRE_PIERCE` | 1.0 | `[0, 1]` | `fire_pierce` share of DEF or WIS ignored (content `3.7`; 4.4: 0.5 to 1.0) |
| `MON_DOUBLE_TURNS` | 2 | `>= 1` | `double_turn_start` doubled opening actions (content `3.7`; 4.4: 1 to 2) |
| `MON_REBIRTH_HEAL` | 0.05 | `(0, 1]` | `root_rebirth` HP after the killing blow, fraction of max HP (content `4.2`; 4.4: 0.4 to 0.05) |
| `MON_WAIL_STAGGER` | 0.6 | `[0, 1]` | `wail_stagger` gauge taken from every enemy per own action, fraction of a full gauge (content `4.2`; 4.4: 0.25 per hit to 0.6 per action) |
| `MON_VEIL_EVADE_CAP` | 0.65 | `[0, 1]` | `umbral_veil` evade cap (content `4.2`) |
| `MON_FEAST_STR` | 0.04 | `>= 0` | `night_feast` STR per landed hit, fraction of base (content `4.2`) |
| `MON_WAIL_WITHER` | 0.09 | `[0, 1]` | `wail_stagger` true damage to every enemy per own action, fraction of its max HP (content `4.4`) |
| `MON_COUNTER_MULT` | 2.0 | `>= 0` | `counter_mitigated` counter damage, multiple of the damage mitigated (content `4.4`) |
| `MON_FIRE_BONUS` | 1.5 | `>= 0` | `fire_pierce` extra fire damage, fire damage x (1 + this) (content `4.4`) |
| `MON_VEIL_EVADE_BONUS` | 0.2 | `[0, 1]` | `umbral_veil` flat evade chance added before its cap (content `4.4`) |
| `MON_FEAST_DMG` | 0.15 | `>= 0` | `night_feast` damage dealt added per landed hit (content `4.4`) |
| `MON_FEAST_START` | 1.5 | `>= 0` | `night_feast` damage dealt at the start of every fight (content `4.4`) |
| `MON_ECHO_DECOYS` | 3 | `>= 0` | `echo_cast` hits per fight the mirror twin takes (content `4.4`) |
| `MON_DRAIN_SHIELD_CAP` | 1.0 | `[0, 1]` | `abyssal_drain` overheal shield cap, fraction of max HP (content `4.4`) |
| `MON_TRIGGER_DEPTH` | 2 | `>= 1` | Loop guard: most trigger levels at once (section 6.4; content `6.4`) |
| `MON_EXTRA_ACTIONS_CAP` | 2 | `>= 0` | Loop guard: most extra actions after one gauge-paid action (section 6.4; content `6.4`) |
| `MON_IRON_DEX` | 0.6 | `>= 0` | `iron_reflexes`: share of DEX added to DEF against physical hits (idea-pool lane A) |
| `MON_AVATAR_BONUS` | 0.25 | `>= 0` | `elemental_avatar`: damage bonus of the unit's hits, x(1 + this) (idea-pool lane A) |
| `MON_FORTRESS_DEF` | 0.5 | `>= 0` | `fortress_fist`: share of DEF each hit adds to its raw damage (idea-pool lane A) |
| `MON_BLOOD_COST` | 0.06 | `[0, 1]` | `blood_magic`: HP cost of a cast, fraction of max HP (idea-pool lane A) |
| `MON_BLOOD_FLOOR` | 0.25 | `[0, 1]` | `blood_magic`: no cast when the cost would leave the unit under this fraction of max HP (idea-pool lane A) |
| `MON_RESENT_FRAC` | 0.5 | `>= 0` | `resentment`: share of the HP damage enemies deal the unit that goes into its red pool (idea-pool lane B) |
| `MON_RESENT_DECAY` | 0.25 | `[0, 1]` | `resentment`: share of the red pool each own action removes (idea-pool lane B) |
| `MON_VANTAGE_HP` | 0.5 | `[0, 1]` | `vantage`: the unit strikes first below this fraction of its max HP (idea-pool lane B) |
| `MON_DOOR_START` | 0.7 | `[0, 1]` | `deaths_door`: chance to survive the first killing blow on Death's Door (idea-pool lane B) |
| `MON_DOOR_STEP` | 0.1 | `[0, 1]` | `deaths_door`: chance lost per killing blow already survived there (idea-pool lane B) |
| `MON_BARRICADE_CAP` | 0.6 | `[0, 1]` | `barricade`: stacked shield cap, and overheal-to-shield cap, fraction of max HP (idea-pool lane B) |
| `MON_WARD_WIS` | 1.5 | `>= 0` | `mana_ward`: ward at fight start, multiple of WIS (idea-pool lane B) |
| `MON_WARD_SHARE` | 0.3 | `[0, 1]` | `mana_ward`: share of each blow's HP damage the ward takes (idea-pool lane B) |
| `MON_WARD_CDR` | 0.5 | `>= 0` | `mana_ward`: extra cooldown ticks per own action while the ward holds (idea-pool lane B) |
| `MON_LONE_BONUS` | 0.4 | `>= 0` | `lone_wolf`: every stat's rise when alone on its side at fight start (idea-pool lane E) |
| `MON_LONE_MALUS` | 0.2 | `[0, 1]` | `lone_wolf`: every stat's fall with allies at fight start (idea-pool lane E) |
| `MON_ARISE_STATS` | 0.3 | `[0, 1]` | `arise`: the risen ally's stats, fraction of the fallen enemy's (idea-pool lane E) |
| `MON_RITE_SHARE` | 0.3 | `>= 0` | `last_rite`: share of the fallen carrier's base stats its living allies split (idea-pool lane E) |

Range violations are `E043` in both `tools/content_build.py` and `ContentDb` (the idea-pool lanes' constants: in `tools/content_build.py` only, and `BattleSim.Start` through each lane's rule validation). `BattleSim.Start` also rejects a hand-built `FightRules` whose rule modifier values are outside these ranges (`DoubleTurns` may be 0 there: the rule does nothing).

## 8. Determinism and goldens

- No clock, IO, reflection or `Math.Pow`; every draw comes from the `IRandomSource` passed in (`specs/00` section 5).
- `BattleSim.StateHash` is SHA-256 over a canonical encoding of the whole fight (`StateHasher` conventions; version `HashVersion` = 2: v2 added `UnitTally.Grazes`). Changing the encoding bumps the version.
- `golden/battle-sim-vectors.txt` resolves nine fixed fights (1v1 twice, 4v1, 4v4, 2v3, a 1v1 and two 2v2 of chase capstone carriers, and a 4v4 with gambit rulebooks), plus one per idea-pool lane with its capstone carriers (lane A: "4v4 conversion capstones", lane B: "4v4 survival capstones") with placeholder fighters defined in `src/IdleRng.Bots/Golden/BattleVectors.cs`, printing outcome, ticks, each unit's HP and tallies, and the hash. It covers every skill kind, a trigger, every chase capstone rule modifier and every gambit condition and target pick. Any rule or content change that moves it is regenerated with `golden --update` and noted in the merge commit.
- Idea-pool lane B (#135) adds the vector "4v4 survival capstones" (seed 11): carriers of `resentment`, `vantage` with `deaths_door`, `barricade` (with an all-allies shield skill) and `mana_ward` against a brawler, a mage, a support and a healer; every one of the five rules fires in it.
- Idea-pool lane E (#138) adds the vector "4v4 team and mode capstones" (seed 12): carriers of `lone_wolf` (with allies: its malus), `arise` and `last_rite` (a fragile one, so it falls) plus a brawler against a brawler, a mage, a support and a healer; all three rules fire, and a risen ally joins mid-fight (it is listed last, on side A).

## 9. Descriptions (tooltips)

The web client explains stats and abilities in tooltips (issue #80). The words are content strings and every number comes from the data or formula it describes, so the text cannot drift from the fight. `BattleText` (`src/IdleRng.Sim/Combat/BattleText.cs`) builds them; `BattleTextTests` checks that every placeholder resolves.

- **Formulas.** The fight's own formulas are public helpers on `BattleSim` (`MaxHp`, `GaugeFill`, `BasicAttackRaw`, `BasicAttackMagical`, `EvadeChance`, `CritChance`, `Mitigate`); the battle and the tooltips both call them.
- **Stats.** `stat.<id>.name` ("Strength"), `stat.<id>.desc` (what the stat does, with constants such as `{MON_DEF_K}` filled in) and `stat.<id>.now` (what the current value does against the coming opponent: `{max_hp}`, `{basic}`, `{basic_type}`, `{def_taken}`, `{wis_taken}`, `{evade}`, `{foe_evade}`, `{acts}` actions per turn, `{crit}`, `{crit_dmg}` (the crit multiplier from DEX), `{fortune}`, `{foe}`), under the heading `tip.now`. "Now" lines use the raw stat sheets: before buffs, debuffs and passives. `stat.<id>.label` names a stat inside a sentence where the upper-case id would read wrong (`hp`: "max HP", `crit_rate`, `crit_dmg`).
- **Skills.** `mskill.<id>.desc` is a template over the skill's own row: `{power}` (or `{power:pct}`), `{scales}`, `{stat}`, `{cooldown}`, `{duration}`, `{ticks}`, `{element}`. A tree node's tooltip adds tags (kind, physical or magical, element, trigger, chase trait) and facts from the row and the rules above: effect, amount, damage type, element, target, duration, cooldown and the AI condition for active skills, when it fires for triggers and passives (`trigger.<id>.name`, `tip.fires.<id>`), stacking for triggered buffs and debuffs (`tip.stacks.triggered` names `{EFFECT_STACK_CAP}`), and the node's SP cost. A rank fight opponent's skill (`specs/14` section 5) gets the same tooltip without the node parts (no trigger, chase tag or SP cost): `BattleText.SkillTip`. The vocabulary is `tip.*`, with constants filled in (`tip.effect.debuff` names `{STAT_FLOOR_FRAC:pct}`). Abilities have no energy cost: energy is a training resource (`specs/14`); a skill's cost is its node's SP.
- **Rules.** `rule.<id>.desc`, with constants filled in (`echo_cast` names `{MON_ECHO_POWER:pct}`). A rule modifier's log event (`FightAction.Event` `rule:<id>`) shows under `rule.<id>.name` in the battle log. An optional `rule.<id>.log` (the idea-pool rules) words the event's key number for the log line, as `{amount}` or `{amount:pct}` ("rolls 70% odds to hold on at 1 HP").

Template syntax and the constant lookup are `specs/05` section 5.

## 10. Open

- What a timeout means per mode (proposed: the challenger loses; rank fights already treat it as a loss).
- Whether skills get their own elements in content (supported by the engine; content decides at T3).
