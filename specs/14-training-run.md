# 14 - Training Run

Status: **live (T2 of `specs/13`)**, played in the web client (T6); a finished run's monster is sealed into the account's Vault (T5, `specs/18`). Code: `src/IdleRng.Sim/Training`. Tests: `src/IdleRng.Sim.Tests/TrainingRunTests.cs`, `src/IdleRng.Bots.Tests/RunBotTests.cs`. Golden: `golden/training-run-vectors.txt`.

The design is `specs/12` section 4; the cycle flow is kj's (2026-10-30). Skill points, tree offers and skill trees are `specs/15`.

> **Numbers are placeholders.** Every constant and table value here is a placeholder until balance resumes (`CLAUDE.md`).

## 1. Flow

```
Start(species, seed)
  cycle 1..RUN_CYCLES:
    RUN_AP taps, each one:  Drill(stat)  -> tier roll, gain roll, maybe injury (ends the run)
                            Rest         -> + energy
    AP spent: if cycle % FIGHT_EVERY == 0 -> Fight (1v1 vs the current rank's opponent; win = rank up)
  after the last cycle (and its fight): Finished, Completed
```

Every tap gives instant feedback (the command's result object). Fights are resolved in one call; the UI animates them from the action callback.

## 2. State and commands

`RunState` is a plain mutable graph (saveable, `Clone()`): species id, run seed, the eight stats, energy, cycle (1-based), AP left, rank index, phase, end reason, the `training` stream, counters (fights fought and won, drills, rests), milestone hits and the command log.

| Phase | Allowed command |
|---|---|
| `Training` | `Drill(stat)`, `Rest` |
| `AwaitFight` | `Fight` |
| `Finished` | none (`End` is `Completed` or `Injured`) |

Tree commands (`SpendPoint`, `TakeTree`, `DiscardOffer`, `UseBook`; `specs/15` section 3) work in every phase. `Drill`, `Rest` and `Fight` also wait until no tree offer is pending. A command that is not allowed throws `InvalidOperationException`; an unknown stat throws `ArgumentException`. Only accepted commands enter the log.

## 3. Species

Content table `species`: `id`, `name_key`, `element`, `base_<stat>` and `grow_<stat>` for each of the eight stats. A run starts with the species' base stats. The growth multiplier scales every drill gain on that stat. Three placeholder species (slime, drake, sprite) match the MVP's three (`specs/12` section 12.1); their native trees arrive in T3.

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
- An injury still applies the drill's gain, then ends the run (`Injured`, no fight). `specs/12` section 4.5: the monster is still vaulted and the run still pays shards (T4/T5).
- One energy meter stands in for the drafts' energy plus fatigue (`specs/12` section 13 item 16).

**Rest:** `energy = min(RUN_ENERGY_MAX, energy + REST_ENERGY)`. No draw.

Both cost 1 AP. When AP reach 0 the run goes to `AwaitFight` if a fight is due, else straight to the next cycle (or `Finished` after the last cycle).

## 5. Rank fights

- Content table `fight_ranks`: `id`, `sort_order` (0 = lowest, contiguous), `name_key`, `element` and the eight stats of the opponent at that rank. Placeholder ranks E, D, C, B, A, S.
- `Fight` builds the monster as a `Fighter` (species element, current stats, and the skills of its bought tree nodes, `specs/15` section 2) and the rank's opponent, and resolves a 1v1 `BattleSim` fight (`specs/16`).
- A win (`SideAWon`) promotes the monster one rank and pays `SP_PER_WIN`; at the top rank it stays. A loss or a timeout changes nothing (placeholder; no penalty yet). The fight's tallies add to the run tally (`specs/15` section 6).
- Fight seed: `SplitMix64.Derive(StreamSeed(runSeed, "fight"), fightIndex)`, where `fightIndex` is the number of fights fought before it. Fights never advance the `training` stream.
- `FIGHT_EVERY` (placeholder 1) sets how often fights come; kj suggested every other cycle if every cycle feels too frequent.
- A rank up is called "promotion" in the drafts; code and specs say `Rank`.

## 6. Milestones

Content table `stat_milestones`: thresholds, strictly increasing (300, 500, 700, 900). When a drill (or a flat stat node) takes a stat from below a threshold to at or above it, the run records a `MilestoneHit` (stat, threshold, cycle), once per stat and threshold, returns it in the result and opens a milestone draft (`specs/15` section 4).

## 7. Determinism, replay, hash

- Pure: no clock, IO or `Math.Pow`. All randomness is the `training` and `draft` streams plus derived fight seeds.
- `TrainingRun.Replay(content, species, seed, log, books)` rebuilds a run from its command log; tests check it gives the same `StateHash` for every species, bot and book list.
- `TrainingRun.StateHash` is SHA-256 over a canonical encoding of the run (`StateHasher` conventions; `HashVersion` 3 since the run modifiers of `specs/18` section 3, which `Start` and `Replay` take as an optional last argument).

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
| `STAT_MAX` | 999 | `> 0` |
| `FIGHT_EVERY` | 1 | `> 0` |

| Table | Rules |
|---|---|
| `species` | at least one row (`E044`); `element` is an element id (`E023`); base stats in `[0, STAT_MAX]`, growth `> 0` (`E043`) |
| `drill_tiers` | exactly `fail`, `success`, `super` (`E044`); `0 <= gain_min <= gain_max` (`E043`). Placeholders: fail 0-2, success 8-15, super 20-30 |
| `fight_ranks` | at least one row (`E044`); `sort_order` exactly `0..N-1` (`E047`); element id (`E023`); stats in `[0, STAT_MAX]` (`E043`) |
| `stat_milestones` | thresholds `> 0`, strictly increasing (`E032`) |

## 9. Bots and goldens

- `src/IdleRng.Bots/Training/RunBots.cs`: four policies. `balanced` rests below 40% energy and drills the lowest stat; `focus` rests below 30% and drills the best-growth stat that is not capped; `reckless` never rests; `hoarder` is `focus` but saves skill points until cycle 9. All fight when a fight is due; tree handling is `specs/15` section 8.
- CLI: `dotnet run --project src/IdleRng.Bots -- train --species drake --policy hoarder --seed 7 [--books book_echoes]` prints every command, the summary and the hash.
- `golden/training-run-vectors.txt`: every species x policy at seed 7, plus `focus` with every book, summary and hash. Regenerate with `golden --update` when the run rules, the battle or their content change.

## 10. Open

- Retiring a monster early, and any penalty for losing a rank fight.
- The rank reached is the MVP's end-of-run grade (kj, 2026-10-30); the Vault entry (T4) is `specs/13`.
