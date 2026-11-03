# 14 - Training Run

Status: **live (T2 of `specs/13`)**, played in the web client (T6); a finished run's monster is sealed into the account's Vault (T5, `specs/18`). Code: `src/IdleRng.Sim/Training`. Tests: `src/IdleRng.Sim.Tests/TrainingRunTests.cs`, `src/IdleRng.Bots.Tests/RunBotTests.cs`. Golden: `golden/training-run-vectors.txt`.

The design is `specs/12` section 4; the cycle flow is kj's (2026-10-30). Skill points, tree offers and skill trees are `specs/15`.

> **Numbers are placeholders.** Every constant and table value here is a placeholder until balance resumes (`CLAUDE.md`).

## 1. Flow

```
Start(species, seed)  -> rolls the rank ladder: one opponent per rank from that rank's pool (section 5)
  cycle 1..RUN_CYCLES:
    RUN_AP taps, each one:  Drill(stat)  -> tier roll, gain roll, maybe injury (section 4.1: loses the rest of the cycle,
                                            its fight and the next INJURY_SKIP_CYCLES cycles)
                            Rest         -> + energy
    AP spent: if cycle % FIGHT_EVERY == 0 -> Fight (1v1 vs the ladder's opponent at the current rank; win = rank up,
                                                    loss = the same opponent next time)
  after the last cycle (and its fight): Finished, Completed
```

Every tap gives instant feedback (the command's result object). Fights are resolved in one call; the UI animates them from the action callback.

## 2. State and commands

`RunState` is a plain mutable graph (saveable, `Clone()`): species id, run seed, the eight stats, energy, cycle (1-based), AP left, rank index, the rank ladder (section 5), phase, end reason, the `training` stream, counters (fights fought and won, drills, rests), milestone hits, injuries (section 4.1) and the command log.

| Phase | Allowed command |
|---|---|
| `Training` | `Drill(stat)`, `Rest` |
| `AwaitFight` | `Fight` |
| `Finished` | none (`End` is `Completed`; `Injured` only on Vault entries sealed before issue #89, section 4.1) |

Tree commands (`SpendPoint`, `TakeTree`, `DiscardOffer`, `UseBook`; `specs/15` section 3; and `RerollDraft` with the Second Look talent, `specs/18` section 3) work in every phase. `Drill`, `Rest` and `Fight` also wait until no tree offer is pending. A command that is not allowed throws `InvalidOperationException`; an unknown stat throws `ArgumentException`. Only accepted commands enter the log.

**Run-rule hooks (issue #133).** `TrainingRun` is a partial class with named hooks for the idea-pool run rules (lanes F and G of epic #132), declared in `Training/Rules/RunHooks.cs` and filled in `Training/Rules/LaneF.cs` and `LaneG.cs`; an unfilled hook does nothing. Hooks: the kit (`BuildFighter`, after the bought nodes), growth (before the talents' factors), drill gain (before the cap), drill cost, the super chance (`RunSuperChance`) and the injury chance (`RunInjuryChance`; 0 draws no injury roll), the end of a drill (`OnDrillDone`, after its energy cost and Echo, before an injury or the AP), whether Rest is allowed (`CanRest`; `Rest` throws when it is not, and the UI locks the button), rest energy, cycle start, rank-fight start (both fresh fighters) and result, the stat cap (`StatCap`, `STAT_MAX` unless a rule raises it), each gauntlet floor's fresh enemy (`OnGauntletFloor`, called by `GauntletSim.Run`; only the fighter's rules tell what the monster holds there) and the run hash. After an injury the monster is set back to `INJURY_RECOVER_ENERGY` before its next cycle starts, so a cycle-start hook sees that energy. A rule's run state is saved as lean JSON and hashed only when the run has it, so other runs keep their hash.

## 3. Species

Content table `species`: `id`, `name_key`, `element`, `base_<stat>` and `grow_<stat>` for each of the eight stats. A run starts with the species' base stats. The growth multiplier scales every drill gain on that stat. Three placeholder species (slime, drake, sprite; named Coremote, Bastion Drake and Mirage Moth after the creature spike, `specs/24` section 6) match the MVP's three (`specs/12` section 12.1); their native trees arrive in T3. Content 4.2 added two more after the creature spike: the Mandrake (nature) and the Shade Hound (dark).

## 4. Drill and Rest

**Drill(stat)**, with `e = clamp(energy / RUN_ENERGY_MAX, 0, 1)` taken before the drill:

```
fail   = DRILL_FAIL_MIN + (DRILL_FAIL_MAX - DRILL_FAIL_MIN) * (1 - e)
tier   = draw u1:  u1 < fail -> Fail;  u1 < 1 - DRILL_SUPER_P -> Success;  else Super
raw    = gain_min + floor(u2 * (gain_max - gain_min + 1))           (drill_tiers row of the tier; draw u2)
gain   = floor(raw * species.grow_<stat>), stat capped at STAT_MAX
injury = INJURY_MAX_P * (INJURY_BELOW - e) / INJURY_BELOW when e < INJURY_BELOW, else 0   (draw u3 only when > 0)
energy = max(0, energy - DRILL_ENERGY_COST)
```

- Draws come from the run's `training` stream in the order u1, u2, u3.
- An injury still applies the drill's gain, then costs cycles (section 4.1). It no longer ends the run.
- One energy meter stands in for the drafts' energy plus fatigue (`specs/12` section 13 item 16).

**Rest:** `energy = min(RUN_ENERGY_MAX, energy + REST_ENERGY)`. No draw.

**Talents** (`specs/18` section 3) change these rules for a run: the fail and super chances, the drill cost (a free drill after a Super), the rest amount, an injury prevented, the action points of the last cycle and the value a milestone is crossed at. Without talents the rules here are exact.

Both cost 1 AP. When AP reach 0 the run goes to `AwaitFight` if a fight is due, else straight to the next cycle (or `Finished` after the last cycle).

### 4.1 Injury (kj, 2026-10-31, issue #89)

kj: "rework injury to be like, skip a fight cycle or something, less punishing and annoying, but would still basically brick ur build." An injury no longer ends the run; it costs cycles. **(agent)** The concrete rule:

1. The injuring drill keeps its gain (and any milestone it crossed, whose Awakening waits as usual).
2. The rest of the current cycle is lost: its remaining AP are gone and its rank fight (if one was due) is skipped. No promotion chance, no `SP_PER_WIN`.
3. The next `INJURY_SKIP_CYCLES` (placeholder 1) whole cycles are sat out: no drills, no rank fights and no `SP_PER_CYCLE` for them.
4. The monster is then back: energy is set to `INJURY_RECOVER_ENERGY` (placeholder 100, full) and the following cycle starts as usual (full AP, `SP_PER_CYCLE`).
5. If the lost cycles reach past the last cycle, the run finishes there as `Completed` (never `Injured`); an injury in the last cycle just loses that cycle's fight.

All of it happens inside the `Drill` command: no new phase or command, and no extra draw (the injury roll was u3). In cycle terms, an injury in cycle `c` makes the run resume in cycle `c + 1 + INJURY_SKIP_CYCLES`. Each injury is recorded as an `InjuryHit` (cycle injured, whole cycles sat out, cycle back or 0 when the run ran out) in `RunState.Injuries` and returned as `DrillResult.Injury`. Injuries can repeat. Lost cycles still count as cycles played for the shard payout (`specs/18` section 5): the cost of an injury is the build, not the shards.

Why: one tap still costs a fight and up to `RUN_AP - 1 + RUN_AP * INJURY_SKIP_CYCLES` actions (9 at the placeholders) of a 60-action run, plus the SP of the skipped cycle and of any win, so a habit of drilling at low energy cripples a monster (the `reckless` bot, section 9) without throwing the run away. Coming back at full energy keeps it readable ("sits out a cycle, comes back rested").

`RunEnd.Injured` stays in the enum for Vault entries sealed before this change; they fight with the stats and trees they were sealed with (entries keep their result, not a replay log, since schema v9). No achievement or payout depends on it.

## 5. Rank fights

kj (2026-10-31): "an RNG pool of monsters at each rank, and we can see the clear progression as we climb".

- Content table `fight_ranks` is the ladder of ranks: `id`, `sort_order` (0 = lowest, contiguous), `name_key`, `element`, the eight stats and `dismantle_shards` (`specs/18` section 7). Placeholder ranks E, D, C, B, A, S. The eight stats set the rank's **budget**: their sum (E 800, D 1280, C 1840, B 2480, A 3200, S 4000; the curve is unchanged from the flat opponents). They are also the flat opponent of a legacy run (below).
- Content table `rank_enemies` is the pools: each row is a monster of one rank's pool with its own `name_key`, `element`, eight stats that **sum to exactly the rank's budget** in its own shape (tank, glass cannon, evasive, caster, bruiser, healer and so on), 1-3 `skills` (`monster_skills` ids, `|`-separated, in AI priority order; only skills the battle already runs) and a pick `weight`. Every rank has at least one. Placeholder pools: 4 monsters at E, D, C, B and A, 3 at S, weight 1 each; higher ranks carry more skills (1 at E, 1-2 at D and C, 3 from B up) and the top rank's are the named beasts.
- **The ladder** (kj, 2026-10-31): `TrainingRun.Start` rolls one pool monster per rank, lowest rank first, on its own stream `ladder` (`StreamSeed(runSeed, "ladder")`; one draw per rank: the draw times the pool's total weight, walked through the cumulative weights in CSV order), so the `training`, `draft` and `fight` streams are untouched. `RunState.Ladder` holds the ids, indexed like `fight_ranks`; it is hashed (section 7) and saved with the run. The whole ladder is visible to the player from the start.
- `Fight` builds the monster as a `Fighter` (species element, current stats, and the skills of its bought tree nodes, `specs/15` section 2) and the ladder's opponent at the current rank (its element, stats and skills, `TrainingRun.OpponentFighter`), and resolves a 1v1 `BattleSim` fight (`specs/16`). The result names the opponent (`RankFightResult.OpponentId`).
- A win (`SideAWon`) promotes the monster one rank and pays `SP_PER_WIN`; at the top rank it stays. A loss or a timeout changes nothing: the next fight is a rematch with the same opponent (placeholder; no penalty yet), except that it pays the Grudge Match talent's SP (`specs/18` section 3). The fight's tallies add to the run tally (`specs/15` section 6).
- **Legacy runs.** A run started before the pools (content 4.3) has an empty ladder: it fights the flat `fight_ranks` row at each rank (no skills), as it did, and keeps its version 5 hash (section 7). Saved runs and Vault entries without a `Ladder` load as legacy runs (`specs/18` section 6).
- Fight seed: `SplitMix64.Derive(StreamSeed(runSeed, "fight"), fightIndex)`, where `fightIndex` is the number of fights fought before it. Fights never advance the `training` stream.
- `FIGHT_EVERY` (placeholder 1) sets how often fights come; kj suggested every other cycle if every cycle feels too frequent.
- A rank up is called "promotion" in the drafts; code and specs say `Rank`.
- **Presentation** (issue #126; kj, 2026-11-01, reworked 2026-11-02 and 2026-11-03): fights stay hands-off, with no pre-fight choice and no fight card (cut 2026-11-03). When the cycle's actions run out (and any tree offer is settled) a hype screen opens on its own: the two monsters face off with their names, ranks and elements and "VS", with one line under them for any element edge and the stakes (the next rank and `SP_PER_WIN` on a win, the rematch on a loss); the full matchup stays on the Ladder tab. It waits for a tap (its entrance ignores taps for its first moments, so a fast double tap on the last drill does not start the fight unseen). The tap resolves the fight: "Fight!" lands for a short beat (about half a second, a tap cuts it short; none at Skip), then the fight plays back on a stage that keeps the face-off: both monsters big under their names and HP, the current action called out, damage floating over the one hit, the skill tiles below; the result stays on that stage (`specs/13` T6). There is no step back from the hype screen: SP and books are spent before the cycle ends. It is presentation only: the fight is resolved at the tap, before the beat shows.
- **Tuning (agent, 2026-10-31).** Same budget is not same difficulty: a specialised shape plus skills beats a flat spread of the same sum by a wide margin, so the placeholder shapes keep the off-attack stat near the main one (the flat opponents' unused STR or INT was their slack), move only part of the budget, and put the rest in LUK. Shields (`harden`, `ward`, `brine_wall`) and stacked DEF passives were left out of the pools from D up because they time out a low-damage monster, and about one full caster per rank keeps DEF builds from meeting a wall at every rank (a DEF-only build still struggles against them: issue #114). Element counters stay (a nature monster is meant to be hard for a slime). The balance report's "Rank fights by opponent" table (`specs/09` section 7) shows each pool monster's win rate per species.

## 6. Milestones

Content table `stat_milestones`: thresholds, strictly increasing (since content 6.0 the single threshold 400, kj 2026-11-01: one Awakening per stat; it was 300, 500, 700, 900). When a drill (or a flat stat node) takes a stat from below a threshold to at or above it (the Early Bloom talent lowers the value it is crossed at, `specs/18` section 3), the run records a `MilestoneHit` (stat, threshold, cycle), once per stat and threshold, returns it in the result and opens an Awakening, the milestone tree offer (`specs/15` section 4).

## 7. Determinism, replay, hash

- Pure: no clock, IO or `Math.Pow`. All randomness is the `training`, `draft` (Awakening picks and tree rolls, `specs/15` section 4.1), `chase` (the legacy attunement roll, drawn only when a saved run replays with `ChaseChance` below 1, `specs/15` section 5.4) and `ladder` (read once at the start, section 5) streams plus derived fight seeds.
- `TrainingRun.Replay(content, species, seed, log, books, mods, ladder)` rebuilds a run from its command log; tests check it gives the same `StateHash` for every species, bot and book list. A saved run passes its stored ladder (empty for a legacy run); a null ladder rolls it from the seed again, which gives the same ladder while the pools are unchanged. A stored ladder must have one id per rank, each from that rank's pool, or `Replay` throws.
- `TrainingRun.StateHash` is SHA-256 over a canonical encoding of the run's state (`StateHasher` conventions; `HashVersion` 9 since save v10 (kj 2026-11-01, `specs/27` section 3.5): the command log is left out, so a run saved as its state hashes the same after a reload; `TrainingRun.LegacyRecipeHash` keeps the version 5 to 8 bytes, with the log, to check a pre-v10 save's recipe once; 8 since the legendary talents of `specs/18` section 3 (issue #103): a run with a legendary rule change appends those fields after the version 7 talent block under the version 8 magic, and every other run keeps its bytes; 7 since the talent catalog v2 of `specs/18` section 3 (issue #105): a run with one of those talents appends their modifier fields and the Momentum, Second Wind and Second Look counters after the ladder under the version 7 magic; a run without them keeps the exact version 6 (or legacy version 5) bytes, so saved runs and Vault entries keep their hashes; 6 since the rank ladder of section 5, which is appended at the end of the encoding; a legacy run with an empty ladder keeps the exact version 5 bytes and magic, so its stored hashes stay valid; 5 since the attunement roll replaced the draft-size modifier (kj, 2026-10-31), 4 since the injuries of section 4.1, 3 since the run modifiers of `specs/18` section 3, which `Start` and `Replay` take as an optional last argument). Since save v10 a saved run in progress stores no hash, so a hash version bump no longer drops it; only a pre-v10 recipe is checked against its stored hash.

## 8. Content

New in content `2.4`. Range errors are `E043` (and `E023`, `E032`, `E044`, `E047` as noted) in both `tools/content_build.py` (`validate_training`) and `ContentDb.BuildTraining`.

| Constant | Value | Range |
|---|---|---|
| `RUN_CYCLES` | 12 | `> 0` |
| `RUN_AP` | 5 | `> 0` |
| `RUN_ENERGY_MAX` | 100 | `> 0` |
| `RUN_ENERGY_START` | 100 | `[0, RUN_ENERGY_MAX]` |
| `DRILL_ENERGY_COST` | 20 | `>= 0` |
| `REST_ENERGY` | 50 | `>= 0` |
| `DRILL_FAIL_MIN` | 0.05 | `[0, 1]`, `<= DRILL_FAIL_MAX` |
| `DRILL_FAIL_MAX` | 0.6 | `[0, 1]`, `+ DRILL_SUPER_P <= 1` |
| `DRILL_SUPER_P` | 0.1 | `[0, 1]` |
| `INJURY_BELOW` | 0.25 | `[0, 1]` |
| `INJURY_MAX_P` | 0.4 | `[0, 1]` |
| `INJURY_SKIP_CYCLES` | 1 | `>= 0` (content 3.11) |
| `INJURY_RECOVER_ENERGY` | 100 | `[0, RUN_ENERGY_MAX]` (content 3.11) |
| `STAT_MAX` | 999 | `> 0` |
| `FIGHT_EVERY` | 1 | `> 0` |

| Table | Rules |
|---|---|
| `species` | at least one row (`E044`); `element` is an element id (`E023`); base stats in `[0, STAT_MAX]`, growth `> 0` (`E043`) |
| `drill_tiers` | exactly `fail`, `success`, `super` (`E044`); `0 <= gain_min <= gain_max` (`E043`). Placeholders: fail 0-2, success 8-15, super 20-30 |
| `fight_ranks` | at least one row (`E044`); `sort_order` exactly `0..N-1` (`E047`); element id (`E023`); stats in `[0, STAT_MAX]` (`E043`); `dismantle_shards` (the Vault refund for that grade, `specs/18` section 7) and `ticket_pieces` (Dungeon ticket pieces for that grade, `specs/21` section 7) `>= 0` (`E043`); every rank has a `rank_enemies` row (`E044`, empty pool) |
| `rank_enemies` (content 4.3) | `rank` is a `fight_ranks` id and `element` an element id (`E023`); stats in `[0, STAT_MAX]` and summing to the rank's budget within 1e-6 (`E043`); each `skills` id is a `monster_skills` id (`E023`); `weight > 0` (`E043`) |
| `stat_milestones` | thresholds `> 0`, strictly increasing (`E032`) |

## 9. Bots and goldens

- `src/IdleRng.Bots/Training/RunBots.cs`: five policies. `balanced` rests below 40% energy and drills the lowest stat; `focus` rests below 30% and drills the best-growth stat that is not capped; `reckless` never rests (so it is injured about four times a run and sits out about four cycles, section 4.1); `hoarder` is `focus` but saves skill points until cycle 9; `chaser` is `focus` planning toward its species' own chase capstone (`specs/15` section 8, issue #111). All fight when a fight is due; tree handling is `specs/15` section 8.
- CLI: `dotnet run --project src/IdleRng.Bots -- train --species drake --policy hoarder --seed 7 [--books book_echoes]` prints every command (a fight line names the ladder's opponent, `flat` in a legacy run), the summary and the hash.
- `golden/training-run-vectors.txt`: every species x policy at seed 7, plus `focus` with every book, summary and hash. Regenerate with `golden --update` when the run rules, the battle or their content change.

## 10. Open

- Retiring a monster early, and any penalty for losing a rank fight.
- The rank reached is the MVP's end-of-run grade (kj, 2026-10-30); the Vault entry (T4) is `specs/13`.
