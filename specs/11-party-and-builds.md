# 11 - Party and Builds Overhaul

Scope: the gameplay overhaul tracked in issue #60. It turns the hero layer into a party of companions with their own stats, roles and elements, moves fights toward a 4v1 turn-based auto-battle, and replaces main-stat gear with affix gear and build-defining uniques.

This spec is built in phases. Section 2 says which phase is live. Where a section here disagrees with an older spec, this spec wins and the older spec points here.

> **Systems before balance.** Every number in this spec is a placeholder until the systems are finalized (`CLAUDE.md`, "Current focus"). Do not tune them; mark new ones as placeholders.

## 1. Decisions (kj, 2026-10-30)

From #60:

1. **Gear is per companion.** Each of the 4 active companions gets its own gear slots (phase 3).
2. **No player character.** The party is 4 companions. Class advance is replaced by companion promotion (section 6).
3. **Normal stages stay fast auto-farming.** Only bosses and daily dungeons become 4v1 turn battles (phase 2).
4. **Systems first, balance later.**

From #63:

5. **Five elements:** Fire, Water, Nature, Light, Dark (section 4.2).
6. **Five roles:** Tank, Striker, Caster, Healer, Support (section 4.1).
7. **Promotion is per companion**, unlocked by stage milestones. **Each promotion adds one new skill** to that companion's kit.
8. **Max promotions by rarity:** C 0, R 1, E 2, L 3, M 3. L and M share the cap so the gap between them stays small.
9. **About 20 distinct kits.** Low-rarity kits are small and simple; only Legendary and Mythic get fully fleshed-out kits.

## 2. Phases and status

| Phase | Content | Issues | Status |
|---|---|---|---|
| 1 | Party of 4, companion stat sheet, roles, elements, 20-companion pool, per-companion promotion, farming DPS from the stat sheet | #62, #63, part of #59 | **Live** |
| 2 | 4v1 ATB battle for bosses (dungeons later); kits take effect; HP/DEF/SPD used in combat; crit rolled per hit | #61, rest of #59 | **Live** |
| 3 | Per-companion gear slots, prefix/suffix affixes with roll ranges, slot-bound enhancement | #57, #55 | Planned |
| 4 | Daily dungeons, build-defining uniques target-farmed there | #56, #64 | Planned |
| 5 | Stat upgrade overhaul (multiple gold tracks, milestones, auto-buy) | #58 | Planned |

While phases 1-2 are live and later phases are not:

- Gear is still the party-wide `GearMult` of `specs/04` section 5 (one set of 6 slots shared by the whole party).
- Normal stages use the farming DPS of section 7; bosses use the 4v1 battle of section 8.
- The gold stat layer (`specs/04` section 4) and gear multiply the whole party's ATK in farming, and HP/ATK/DEF in battle.

## 3. Stat sheet

Every companion has the same six stats. Later phases add damage buckets from affixes and uniques (section 11).

| Stat | Meaning | Phase 1 use |
|---|---|---|
| `HP` | Health in battle | Display only |
| `ATK` | Damage per basic hit | Farming DPS |
| `DEF` | Damage mitigation in battle | Display only |
| `SPD` | Turn speed in battle; hits per second in farming relative to `SPD_REF` | Farming DPS |
| `CRIT_RATE` | Chance a hit crits, clamped to `[0, 1]` | Farming DPS (expected value) |
| `CRIT_DMG` | Damage multiplier on a crit, `>= 1` | Farming DPS (expected value) |

Rule for combining bonuses (applies from phase 2 on, stated now so content is shaped for it): bonuses of the same kind add together inside a bucket; different buckets multiply. Crit rate and SPD get caps or diminishing returns in phase 2 so they can't run away.

### 3.1 Companion stats at a level and promotion

`L` is the companion's level from dupes (`HERO_LVL_CUM`, `specs/01` section 7). `P` is its promotion count (section 6).

```
LevelMult(L)  = 1 + HERO_LVL_BONUS * (L - 1)          // L >= 1
PromoMult(P)  = product of promotions[1..P].mult        // 1 when P = 0

HP(c)  = c.hp  * LevelMult(L) * PromoMult(P)
ATK(c) = c.atk * LevelMult(L) * PromoMult(P)
DEF(c) = c.def * LevelMult(L) * PromoMult(P)
SPD(c), CRIT_RATE(c), CRIT_DMG(c) = content values, not scaled by level or promotion
```

## 4. Companions

"Companion" is the player-facing word. Code and older specs still say "hero" (`HeroDef`, `HeroState`, `heroes.csv`); the two mean the same thing.

### 4.1 Roles

| Role | Identity | Placeholder stat shape |
|---|---|---|
| Tank | Taunts, soaks damage, scales with DEF/HP | High HP and DEF, low ATK, slow |
| Striker | Single-target burst | High ATK and crit |
| Caster | AoE and skill damage | High ATK, low HP |
| Healer | Heals and cleanses | Medium HP, low ATK |
| Support | Buffs and debuffs | Fast, low ATK |

In phase 1 roles only change the stat shape. A party with no healer or tank stays viable for farming; boss battles (phase 2) are where composition matters.

### 4.2 Elements

Fire beats Nature, Nature beats Water, Water beats Fire. Light and Dark each beat the other. The advantage multiplier and how bosses get elements are phase 2 decisions (`elements.strong_vs` already holds the wheel).

### 4.3 Pool

20 companions in the standard pool: C 5, R 5, E 4, L 4, M 2. Rates, pity and the 10-pull guarantee are unchanged (`specs/01`). The roster with roles and elements is `content_src/heroes.csv`.

### 4.4 Kits (phase 2 content)

Kit size grows with rarity, and each promotion adds exactly one skill:

| Rarity | Base kit | Max promotions | Kit at max promotion |
|---|---|---|---|
| C | Basic attack + 1 simple skill | 0 | 2 actions |
| R | Basic attack + 1 simple skill | 1 | 3 actions |
| E | Basic attack + 1 skill | 2 | 4 actions |
| L | Basic attack + 2 skills + passive | 3 | Fully fleshed kit |
| M | Basic attack + 2 skills + passive | 3 | Fully fleshed kit |

Low-rarity skills are one simple effect each (e.g. "hit twice", "heal lowest ally").

**Skeleton kits (kj, 2026-10-30): don't overthink kits yet.** Every companion has a kit in `content_src/skills.csv` that roughly fits its role and element, so the battle has something to run. Kits are filled from templates:

| Part | Who gets it | Template |
|---|---|---|
| Base skill | everyone | by role: Striker Heavy Strike, Tank Taunt, Caster Arc Burst, Healer Mend, Support Rally |
| Element skill | L, M | by element: Flame Burst, Tidal Crash, Thorn Lash, Radiant Beam, Shadow Rend |
| Passive | L, M | by role: Killer Instinct, Iron Skin, Arcane Mind, Grace, Swiftness |
| Promotion skills 1-3 | up to the rarity cap | by role, e.g. Striker Focus / Execute / Blade Flurry |

So kit sizes are C 1, R 2, E 3, L 6, M 6 skills (plus the basic attack every companion has). Names and numbers repeat across companions of the same role on purpose. Later passes overhaul kits one companion at a time, starting with L and M; a kit can change freely as long as the content rules in section 10 hold.

Kits take effect in the boss battle (section 8). The Party screen lists each skill and whether its promotion has unlocked it.

### 4.5 Starter companion

Exactly one companion has `starter = true` (Pell, a Common Striker). `GameState.NewProfile` grants one copy and puts it in party slot 0, so a new profile always has a party and `Dps > 0` without tutorial grants. Pulling the starter later levels it like any companion.

## 5. Party

- `HERO_SLOTS = 4`.
- `AutoEquipBestHeroes` fills the party with the 4 owned companions with the highest `FarmDps` (section 7), ties broken by id ordinal. Promotion changes `FarmDps`, so auto-equip re-picks after a promotion when it is on.
- Manual equip rules are unchanged (`specs/03` section 9), except that `UnequipHero` rejects emptying the last occupied slot (`Reject.LastCompanion`): party DPS comes only from companions, so an empty party could never kill anything.

## 6. Promotion (replaces class advance)

Content table `promotions` (replaces `class_advances`):

| index | unlock_stage | mult | cost_kills | Notes |
|---|---|---|---|---|
| 1 | 50 | 1.5 | 300 | Placeholder |
| 2 | 150 | 1.5 | 300 | Placeholder |
| 3 | 300 | 1.5 | 300 | Placeholder |

`hero_tiers.max_promotions`: C 0, R 1, E 2, L 3, M 3.

```
Promote(heroId):
  hero must exist in content (else ArgumentOutOfRange), be owned (else HeroNotOwned)
  next = Promotions[heroId] + 1
  if next > max_promotions[tier]:            reject PromotionMaxed
  if Stage < promotions[next].unlock_stage:  reject PromotionLocked
  cost = promotions[next].cost_kills * GOLD(promotions[next].unlock_stage)
  if Gold < cost:                            reject NotEnoughGold
  Gold -= cost; Promotions[heroId] = next; mark DPS dirty
  if AutoEquipBest: re-pick the party
  publish CompanionPromoted(heroId, next, cost)        // after the re-pick, so listeners see the final party
```

- Cost reads the milestone's stage, not the current stage, so catching up on old promotions gets cheap as you push. Gold is a placeholder currency; promotion moves to dungeon materials in phase 4.
- `Stage` is the current stage, which in phase 1 is the highest stage reached (`specs/04` section 12 item 7).
- Promotion ignores the party: a benched companion can be promoted.
- Each promotion multiplies HP, ATK and DEF by its `mult` and unlocks one kit skill (section 4.4).
- Command kind `Promote` (A = hero index in `ContentDb.HeroList`), appended to `CommandKind` (`specs/10` section 6).
- Event `CompanionPromoted(Tick, Hero, Promotions, Cost)` takes the `ClassAdvanced` event's mask bit; it is a save trigger like `HeroPulled`.

Bot policy (`specs/09`): each decision first promotes the party member with the highest `FarmDps` gain per gold whose promotion is affordable (at most one per decision, ties by id ordinal), then buys stat levels as before. Promotion goes first because the greedy stat buy leaves less than one promotion's cost almost every decision.

## 7. Farming DPS

```
CritFactor(c) = 1 + clamp(CRIT_RATE(c), 0, 1) * (CRIT_DMG(c) - 1)
FarmDps(c)    = ATK(c) * CritFactor(c) * SPD(c) / SPD_REF
PartyDps      = sum over party of FarmDps(c)
Dps           = PartyDps * StatMult * GearMult
HitDamage     = Dps * ATTACK_INTERVAL_TICKS / TICK_HZ
```

- The combat loop of `specs/03` section 7 is unchanged: one hit of `HitDamage` every `ATTACK_INTERVAL_TICKS`. Crit is folded in as its expected value, so farming draws no RNG and no stream counter changes.
- Bosses do not use `HitDamage`: they are fought in the 4v1 battle (section 8).
- An empty party has `Dps = 0`. The starter grant (section 4.5) and the "equipped heroes owned" invariant keep a normal profile above zero; `Validate()` still requires `Dps > 0`.

### 7.1 Layers for reporting (`specs/09` section 6.3, AC2)

The tuning table keeps four layers plus a base so AC2 still means something:

```
Base       = FarmDps of the starter at level 1, promotion 0        (content constant in effect)
HeroMult   = PartyDps at promotion 0 / Base                        (party, levels, crit, speed)
PromoMult  = PartyDps / PartyDps at promotion 0                    (replaces ClassMult)
StatMult, GearMult as before
Dps        = Base * HeroMult * PromoMult * StatMult * GearMult
```

`SimQuery.ClassMult` is renamed `PromoMult`. The bot table's `class` column is `promo`.

## 8. Boss battle: 4v1 ATB (phase 2, #61)

Decided (kj, 2026-10-30): ATB gauge; lose on wipe or turn limit, free retry; DEF as diminishing %; elements x1.3 / x0.8 with the boss element rotating every 10 stages. Numbers are placeholders.

### 8.1 Flow

- The 10th enemy of a boss stage starts a battle instead of the timer DPS check. `CombatState.Battle` holds it, the boss timer (`boss_rules.time_ticks`) is the turn limit, and the battle runs inside the normal tick loop, one `BattleEngine.Step` per tick.
- **Win:** boss HP reaches 0. Gold, drops, first clear and stage advance as before.
- **Lose:** the party is wiped, or the timer runs out. `BossFailed.Wiped` says which. The stage walls as before; `AttemptBoss` retries any time.
- The party is snapshotted when the battle starts. Pulls, promotions and stat buys during a fight apply to the next fight.
- The battle is saved with the state (`CombatState.Battle`, hashed by `StateHash`), so a reload mid-fight continues identically. A save with a boss but no battle (older v2) starts the battle on load.
- Offline catch-up never fights bosses (unchanged).

### 8.2 Units and stats

```
power      = StatMult * GearMult                        (gold stat layer x gear; split per stat in phases 3/5)
companion  = HP/ATK/DEF from section 3.1 x power; SPD, crit as in content
boss       = HP  = hp_mult  * HP(s)                     (unchanged boss HP)
             ATK = atk_mult * HP(s), DEF = def_mult * HP(s), SPD = boss_rules.spd, no crit
boss element = elements[(s / BOSS_EVERY) mod 5] in sort order
```

### 8.3 ATB and actions

- Each tick every living unit adds its current SPD to its gauge. A unit at or above `BATTLE_GAUGE` acts and pays `BATTLE_GAUGE`; highest gauge first, ties to the lower party slot, boss last. At SPD 100 a unit acts once per second. A "turn" for durations is `BATTLE_GAUGE / SPD_REF` ticks.
- **Companion AI:** cooldowns tick down by one per own action. It uses the first unlocked, ready kit skill (kit order, passives skipped) that would do something: damage always; heal when an ally is below `AI_HEAL_BELOW` of max HP; shield below `AI_SHIELD_BELOW`; taunt when not taunting; buff/debuff when the target does not already carry that skill's effect. Otherwise a basic attack of `BASIC_ATTACK_POWER x ATK`.
- **Boss:** every `aoe_every`-th action hits the whole living party for `aoe_power x ATK`; otherwise one target: a taunting companion if any, else a random living one (one `battle` draw).

### 8.4 Damage and effects

```
damage = scaled * crit * element * K / (K + DEF_target)        then the target's shield absorbs first
scaled = power x (the skill's `scales` stat, current value)
crit   = CRIT_DMG with chance CRIT_RATE (one `battle` draw per hit by a unit that can crit)
element= ELEMENT_STRONG_MULT if attacker beats defender, ELEMENT_WEAK_MULT if defender beats attacker, else 1
K      = DEF_K_MULT * HP(s)
```

- Heal: `scaled` to the weakest ally (by HP fraction) or all allies, capped at max HP. Shield: absorbs `scaled` for `duration` turns. Taunt: the boss must target this companion for `duration` turns.
- Buff/debuff: `power` as a fraction of base for ATK/DEF/SPD (floored at 10% of base), added directly for crit rate and crit damage; lasts `duration` turns.
- Passive: the same effect, permanent, applied at battle start. An HP passive raises max and current HP together; HP is passive-only (`E054`).

### 8.5 Triggers (hook for uniques, #64)

Every effect goes through one function (`BattleEngine.Apply`) using the kit-skill vocabulary. Triggered effects are collected per unit by `BattleEngine.CollectTriggers` and fired at `BattleStart`, `OnAction`, `OnBasicAttack`, `OnCrit` and `OnHitTaken`. Phase 2 registers kit passives at `BattleStart`; uniques add their entries there, derived from the unit's snapshot, so a saved battle re-derives the same triggers after a reload.

### 8.6 Determinism and queries

- New RNG stream `battle` (added to `specs/00` section 5 contract 2). Crit rolls and boss targeting are its only draws; farming still draws nothing.
- `SimQuery.BossClearable` runs the whole battle on copies (a cloned stream) and reports whether the current party would win; it changes no state and is cached until the build, stage or `battle` counter changes. Bots retry a walled boss when it is true.
- Saves from before the `battle` stream existed stay valid: the save loader requires only the original five streams and the sim creates `battle` when missing. A boss with no saved battle starts a fresh battle with the full turn limit.
- Event `BattleAction` (opt-in, like `HitLanded`) reports each action; damage to the boss also publishes `HitLanded` for damage floaters.

## 9. Save schema v2

`GameState.CurrentSchemaVersion` and `SaveSchema.Current` go to 2. Migration v1 -> v2 (`specs/06` section 8 rules: literals frozen in the step, no ContentDb):

1. **Removed companions.** The v1 pool had 54 heroes; v2 keeps `hero_c_01..05`, `hero_r_01..05`, `hero_e_01..04`, `hero_l_01..04`, `hero_m_01..02`. Copies of any other id are converted to `Currencies.Shards` at the v1 shard value per copy (C 1, R 5, E 25, L 100, M 500, by the tier letter in the id) and the id is removed.
2. **Party.** Removed ids in `Heroes.Equipped` become null; non-null entries are compacted to the front and the array is cut to 4.
3. **Promotions.** `Heroes.Promotions` is created. Each kept, owned companion gets `min(Progression.ClassAdvances, cap)` with cap C 0, R 1, E 2, L 3, M 3, so a migrated player keeps most of their class power. `Progression.ClassAdvances` is removed.
4. **Starter.** If no kept companion is owned after step 1, grant one copy of `hero_c_01` and put it in slot 0.

## 10. Content tables (phases 1-2)

Changes to `specs/05` section 5. `content_version` goes to `2.0` (MAJOR: heroes removed, `class_advances` removed).

`constants`: `BASE_ATK` removed; `SPD_REF` (double, `> 0`) added.

`heroes` (replaces `base_pct`):

| Column | Type | Constraint |
|---|---|---|
| `id`, `tier`, `pool_id`, `name_key`, `desc_key`, `icon_id` | as before | |
| `role` | ref(roles) | `E023` if unknown. |
| `element` | ref(elements) | `E023` if unknown. |
| `hp`, `atk`, `def`, `spd` | double | `> 0` (`E043`). |
| `crit_rate` | double | `[0, 1]` (`E043`). |
| `crit_dmg` | double | `>= 1` (`E043`). |
| `starter` | bool | Exactly one row is `true` (`E062`). |

`hero_tiers` gains `max_promotions` (int, `0 ..` row count of `promotions`, `E043`).

`roles`: `id` (key), `sort_order` (int, `0..N-1`), `name_key`, `desc_key`.

`elements`: `id` (key), `sort_order` (int, `0..N-1`), `name_key`, `display_color` (color), `strong_vs` (ref(elements)).

Phase 2 adds constants `BATTLE_GAUGE`, `DEF_K_MULT`, `ELEMENT_STRONG_MULT`, `ELEMENT_WEAK_MULT`, `BASIC_ATTACK_POWER`, `AI_HEAL_BELOW`, `AI_SHIELD_BELOW` and `boss_rules` columns `atk_mult`, `def_mult`, `spd`, `aoe_every`, `aoe_power` (content `2.2`, section 8).

`skills` (skeleton kits, section 4.4): `id` (key), `hero_id` (ref(heroes), `E023`), `slot` (int, `0..N-1` contiguous per hero, `E060`), `unlock_promotion` (int; 0 = base kit), `name_key`, `desc_key`, `kind` (enum `damage`, `heal`, `shield`, `taunt`, `buff`, `debuff`, `passive`), `target` (enum `enemy`, `all_enemies`, `self`, `lowest_ally`, `all_allies`), `stat` (stat id, required for buff/debuff/passive), `scales` (stat id, required for damage/heal/shield), `power` (double `>= 0`: multiplier of `scales`, or the buff/debuff fraction), `cooldown` (int turns; 0 iff passive), `duration` (int turns). Each companion needs at least one base skill and exactly one skill per promotion `1..max_promotions` (`E063`). Adding the table took content to `2.1`.

`promotions` (replaces `class_advances`): `index` (key, `1..N` contiguous, `E060`), `unlock_stage` (int, strictly increasing, `E032`), `mult` (double, `> 0`), `cost_kills` (double, `>= 0`), `name_key`.

## 11. Later phases (design so far)

- **Phase 3, gear (#57, #55).** Each companion gets its own slots (count open, 4-5 suggested). Pieces roll prefixes and suffixes with tiered roll ranges on a `gear_affix` stream. Enhancement belongs to the companion's slot, not the item.
- **Phase 4, dungeons and uniques (#56, #64).** Daily tickets; each unique drops from one dungeon with bad-luck protection; dungeon materials replace gold for promotion and enhancement.
- **Phase 5, stat tracks (#58).** Party-wide gold tracks for HP/ATK/DEF/SPD/crit with milestones and auto-buy.

## 12. Known gaps

- `tools/power_curve_model.py` still models the pre-overhaul layers (6 heroes, base DPS%, class advance), issue #66. It is rebuilt when balance resumes; until then the C# bot harness is the only measurement.
- Acceptance after phase 1 (20 seeds x 30 days): AC1 stage targets pass on days 1, 7 and 30. AC2 fails because the promotion layer is about 2-3% of ln(DPS) and 0 before the first promotion. AC3 fails on one seed (first wall at stage 50). AC4 is the pre-existing #41. These are balance outcomes of placeholder numbers; they are reported, not tuned. While the overhaul's systems are being finalized, AC1-AC5 are report-only (`advisory` in `acceptance-targets.json`, kj 2026-10-30): imbalance never blocks a phase. AC6 (determinism and goldens) still blocks.
- Acceptance results move with every phase. Report them in the PR; do not retune (`CLAUDE.md`).
