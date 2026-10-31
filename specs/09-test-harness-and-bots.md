# 09 - Test Harness, Bots and Golden Files

Scope: the headless bot project `IdleRng.Bots` (`src/IdleRng.Bots`), the tests, the golden files and `check.sh`. This spec was the idle game's balance harness (day-by-day bots, an acceptance table and a 20-seed sweep); that went with the idle game at `specs/13` T7. Balance work resumed on 2026-10-31 (`CLAUDE.md`); the run-balance harness on the bots is section 7.

## 1. Design goals

1. The bots drive the same public commands as the UI (`TrainingRun.*`, `AccountOps.*`); they have no private shortcut into the sim.
2. Deterministic to the byte for a given seed and content, on every machine.
3. Fast enough that the whole check runs in under a minute.
4. A rules or content change that moves behaviour is a reviewable diff: the golden files change in the same commit.

## 2. Layout

```
src/IdleRng.Bots/
  Program.cs                CLI (section 5)
  Training/RunBots.cs       IRunPolicy, ThresholdRunPolicy, RunPolicies, RunBot (Play, Summary, Vectors)
  Golden/GoldenFiles.cs     the list of golden files, compare and write
  Golden/BattleVectors.cs   fixed fights through BattleSim
  Golden/GauntletVectors.cs each species' bot-trained monster up the gauntlet
  Golden/TowerVectors.cs    the same monsters, alone and together, up the Dungeon on two days
  Balance/BalanceHarness.cs the run-balance harness: options, samples, per-cell and tower summaries (section 7)
  Balance/Distribution.cs   mean, sd, min, max and nearest-rank p10/p50/p90 of one metric
  Balance/BalanceWriter.cs  the balance report as a text table, JSON or long-form CSV
  Report/TableWriter.cs     how a content version and hash are printed
src/IdleRng.Bots.Tests/     RunBotTests, BalanceTests
```

## 3. Golden files

`golden/` holds generated reference output of fixed commands. **Never open, grep or hand-edit them**; regenerate through the CLI. `golden --check` regenerates each in memory and compares bytes.

| File | What it pins |
|---|---|
| `golden/battle-sim-vectors.txt` | Five fixed fights (1v1, 4v1, 4v4, 2v3) with every skill kind, a trigger and a rule modifier: outcome, ticks, tallies, hash (`specs/16` section 8) |
| `golden/training-run-vectors.txt` | Every species x run policy at seed 7, plus a run with every book: summary and run hash (`specs/14` section 9) |
| `golden/gauntlet-vectors.txt` | Each species after a `focus` bot run, sent up the gauntlet: floor, killer, title, hash (`specs/19` section 6) |
| `golden/tower-vectors.txt` | The same monsters, alone and up to four together, up the Dungeon on days 0 and 20391: lineup, floor, cleared, vents, phases, hash (`specs/21` section 9) |

Each file's first line carries the content version and short hash (`content=3.0/b7ba908c`). Any change to a `content_src/` file changes the hash, so every golden is regenerated and the merge commit says so.

On a mismatch `golden --check` prints the first differing lines and a verdict:

- **content changed**: the header hash differs from the loaded content. If the change is intended, `golden --update`.
- **sim or bot behaviour changed with identical content**: a determinism or logic regression unless a sim change in the same commit explains it. Do not update until the cause is understood.

Regenerate with `dotnet run -c Release --project src/IdleRng.Bots -- golden --update`.

## 4. Bots

`IRunPolicy.Next(content, run)` returns the next command or null when it has nothing left. `ThresholdRunPolicy` resolves offers (empty slot first; chase trees overwrite the least-invested slot that is not one of their prerequisites; otherwise discard), uses books at once, buys the first affordable next node once the cycle reaches `spendFrom`, fights when due, rests below an energy threshold and otherwise drills the stat a pick function chooses. Its `chase` knob (issue #111) makes it plan toward the species' own chase capstone instead (`ChaseTarget`, `specs/15` section 8). Policies: `balanced`, `focus`, `reckless`, `hoarder`, which do not plan for chase trees, and `chaser`, which is `focus` with `chase` on (`specs/15` section 8). `RunBot.Play` plays a whole run, optionally logging a line per command and passing every state to an observer (the balance report's `ChaseTracker`).

## 5. CLI

```
dotnet run -c Release --project src/IdleRng.Bots -- train  --species ID --policy P --seed N [--books a,b]
dotnet run -c Release --project src/IdleRng.Bots -- balance [--seeds N] [--seed-base N] [--species a,b] [--policies a,b]
                                                            [--books none|all|a,b] [--no-tower] [--tower-day N]
                                                            [--no-book-canary] [--strict] [--format text|json|csv] [--jobs N] [--out path]
dotnet run -c Release --project src/IdleRng.Bots -- golden --check | --update [--golden-dir path] [--content dir]
```

`train` prints every command, a summary and the run hash. `balance` prints the run-balance report (section 7). `--content` defaults to the repo's `Content/`.

## 6. Tests and `check.sh`

`./check.sh` runs, in order: the content build with no drift (`tools/content_build.py --check`), the builder's tests (`tools/test_content_build.py`), the content id check against the merge base with `origin/main` (`E090`, `E091`, `specs/05` section 4; skipped locally when `origin/main` is missing, an error in CI), the .NET build, a `balance --seeds 2` smoke run in each output form (section 7; it checks that the verb runs and its JSON parses, never a number: a sanity-check WARN prints but exits 0, and `check.sh` never passes `--strict`), all tests, `golden --check`, then (when the wasm-tools workload is installed) the WebAssembly build and the headless-browser tests: one plays a whole run through the buttons (`web/wasm/test/run-trainer.js`), the other checks each main screen at phone width (no horizontal scroll, no console error), a tree overwrite, save/reload/resume with the same run hash, and a stale or damaged save (`web/wasm/test/flows.js`). `--fast` skips the golden tests and the golden check (the balance smoke run stays); `--no-wasm` skips the web steps; `--no-dotnet` runs only the content check (a partial pass: say so). A change is not done while any check fails.

When the `CI` environment variable is set (anything but empty or `false`; GitHub Actions sets `CI=true`), a missing wasm-tools workload in full mode is an error, not a skip, so CI cannot pass without the web steps. `--no-wasm` still skips them on purpose.

**CI.** `.github/workflows/check.yml` runs the full `./check.sh` on every pull request, every push to `main` and on manual dispatch, on `ubuntu-latest`. It installs Python 3.12, Node 22, the .NET 8 SDK (in its own directory, so newer SDKs on the runner image are not selected and `wasm-tools` matches `net8.0`), the wasm-tools workload, and Playwright (pinned in the workflow, installed outside the repo and found through `NODE_PATH`, since the repo has no `package.json`) with Chromium. NuGet packages are cached. Making the check required for merging is a branch-protection setting on GitHub, not part of the repo.

`RunBotTests` plays every policy on every species, with and without books, and checks that the command log replays to the same hash; it also checks that `chaser` uses only its species' own book and that `ChaseTarget` reads its goals and SP from the chase conditions in content. `BalanceTests` covers the harness: the distribution maths (nearest-rank percentiles in integer arithmetic, empty input), the per-cell, chase capstone and tower aggregation on hand-made samples, that `ChaseTracker` agrees with the finished run, that a report is byte-identical across repeats and `--jobs` values, that a cell matches single runs and what `Vault.Seal` and `AccountOps.Payout` record, option validation, and that the JSON parses (with the chase block) and every CSV row has every column. `BalanceChecksTests` feeds hand-made reports to the sanity checks and asserts each PASSes, WARNs (at and either side of its threshold) or SKIPs, that only `--strict` turns a WARN into a non-zero exit, and that the harness runs the book canary only when the main pass brings no books.

## 7. Run-balance harness

**A report, not a gate, and not a golden.** `balance` measures the placeholder numbers and runs a few **loose sanity checks** over them (below); it exits 0 whatever they say unless `--strict` is passed. There are **no balance targets** (issue #89, kj 2026-10-31): meta-progression is still being added and will move the tuning, so for now the harness only asks "is anything absolutely unplayable?". Tight targets wait until kj asks for them, and any large retune still needs targets agreed with kj first (`CLAUDE.md`).

**What it plays.** For every species x run policy (default: every species in content order and every policy in section 4), `--seeds` runs (default 200) on seeds `--seed-base` (default 1), `--seed-base` + 1, ... Each run is `RunBot.Play` from a fresh account (no talents, `RunModifiers` defaults), bringing the `--books` (default none; `all` = every skill book), then sealed the way `AccountOps.FinishRun` seals it: `AccountOps.Payout` shards and `GauntletSim.Run` on the run seed. Unless `--no-tower`, each policy's monsters then climb the Dungeon (`TowerSim.Run`): each species alone, and one of each species (the first `MON_SIDE_SIZE`) from the same seed as one team, on day `--tower-day` (default 0) + the seed's index, so the lineups vary across the sample. A tower sample's shards are `TOWER_SHARDS_PER_FLOOR` x floors cleared (a first attempt that day) and its books the `TowerSim.BookDrop` hits on the cleared floors.

**What it reports.** Per species x policy:

| Section | Metrics |
|---|---|
| Grades | % of runs ending at each rank (E to S), mean rank index (E = 0), p10/p50/p90 |
| Runs | injury rate (runs with at least one injury), injuries per run and whole cycles sat out (`specs/14` section 4.1), rank fights won and lost, fight win rate (won / fought over the cell), shard payout |
| Rank fights by opponent | per rank pool monster (`specs/14` section 5): the share of runs whose ladder rolled it and, per cell, the rank fights fought against it and the share won. The text table sums every policy per species. A report only, no check reads it |
| Gauntlet | floor reached (`GAUNTLET_FLOORS` + 1 = cleared), clear rate, boss-floor stall rate (share of the runs that did not clear that ended on a boss floor; about 1/`GAUNTLET_BOSS_EVERY` when the curve is smooth, `specs/19` section 2), stall floors (of the runs that did not clear, how many ended on each floor reached mod `GAUNTLET_BOSS_EVERY`; x0 = boss floors; issue #99), the killers that ended most runs |
| Chase capstones | capstone rate (runs that end with a chase tree's capstone, its last node, bought) and the capstone cycle (per capstoned run, the earliest cycle a capstone it ended with was bought); per chase tree of the species: unlock rate (every condition held at once at some point), capstone rate and cycle, and each condition's hit rate (it held at some point of the run). Issue #111; a report only, no check reads it |
| Chase power | per cell: fusion power (per fusing pair the species can hold, the floors the fused tree adds on top of both parents maxed; the mean over those pairs), and the gauntlet floor of each run's monster with its chase trees taken out (their flat stats too), and the floors its species' in-run, or book-gated, chase tree adds when bought in full on top (stats, skills and capstone; same seed). The yardstick for the power ladder of `specs/15` section 5.5; the `capstone-ladder` check reads it |
| Dungeon | per policy x team: floors cleared, clear rate, shards, books |
| Book canary | per species x policy: mean rank and capstone rate with every skill book vs none, and the canary pass's chase capstone table (below) |

Every spread is a `Distribution`: mean, population standard deviation, min, max and nearest-rank p10/p50/p90 (the value at sorted index `ceil(p x n / 100) - 1`, computed in integers).

**Book canary.** When the main pass brings no books (the default), the harness replays every species x policy on the same seeds with every skill book (no tower), for the `books` check. `--no-book-canary` skips it; with `--books` set it is not run and the check SKIPs.

**Sanity checks.** kj's stance (issue #89, 2026-10-31): keep the harness very loose, a guard against "absolutely unplayable" only, and tighten it after meta-progression lands. Some species are meant to be inherently stronger (later unlocks), so there is **no species-parity check**, and one species maxing out is fine. Injury is being reworked separately, so it is checked only for "not instant or total". Each check is PASS, WARN or SKIP (what it needs was not run, e.g. `--no-tower` or a `--policies` filter without `focus`), with the worst measured value and where it comes from. The thresholds are named constants in `BalanceChecks` (harness code, not content: they judge the numbers, they are not game numbers).

| Id | PASS when | Threshold |
|---|---|---|
| `progress-fights` | under `focus`, every species' median rank fights won is at least the threshold | >= 1 |
| `progress-grade` | under `focus`, every species ends at the lowest grade (E) in under the threshold share of runs | < 90% |
| `not-all-s` | under `focus`, not every species ends at the top grade (S) in all of its runs (one maxed species is intended; all of them is "nothing left to progress") | share of maxed species < 1 |
| `policy-matters` | `focus` mean rank >= `balanced` mean rank for every species (no margin) | >= 0 |
| `injury-resting` | no cell whose runs rest (any run has a Rest) is injured in more than the threshold share of runs | <= 50% |
| `injury-total` | in every cell, the mean share of a run's cycles lost to injury (injured cycles plus whole cycles sat out, `specs/14` section 4.1) is under the threshold. Replaced `injury-instant` (runs ended by injury by cycle 2), which the injury rework made impossible | < 90% |
| `gauntlet-progress` | every species has a policy whose median gauntlet floor is past the first boss floor (`GAUNTLET_BOSS_EVERY`) | > 10 |
| `gauntlet-not-solved` | no cell clears the gauntlet in more than the threshold share of runs | <= 50% |
| `capstone-ladder` | under `focus`, every species' book chase tree adds more gauntlet floors than its in-run one (kj, 2026-10-31: the book chase is the rarest and strongest; `specs/15` section 5.5) | > 0 |
| `tower-reachable` | the full `focus` team (one of each species) clears at least floor 1 on more than the threshold share of days | > 50% |
| `tower-not-solved` | the full `focus` team does not clear the tower on every day | < 100% |
| `books` | bringing every book lowers no cell's mean rank by more than the threshold (a canary for broken books) | >= -1.5 |

They are a report until the first retune lands (kj's answer to Q3 on #89), then a soft warning: `check.sh` never fails on one (the smoke run prints a one-line `# sanity checks:` summary to stderr). `--strict` (opt-in, not wired into `check.sh`) exits 1 when any check WARNs; a SKIP never fails. At content 3.10, 200 seeds: every check passed except `injury-instant` (reckless runs ended by injury by cycle 2 in about 93% of runs). At content 3.12 (the injury rework), 200 seeds: all 11 checks pass; `injury-total` is highest for reckless (about 65% of a run's cycles lost to four injuries), and no resting policy is ever injured. Chase capstones at content 3.13, 200 seeds, no books (issue #111): the four non-planning policies reach one only as slime (`hoarder` 69%, `focus` 3.5%); `chaser` reaches one in 100% of slime, 92.5% of drake and 100% of sprite runs, bought in cycle 11.4-11.6 on average, but slime `chaser` ends at B where `focus` ends at S (the capstone path costs it trees that fight better). Rank fight pools at content 4.3, 200 seeds, no books: all 11 checks pass; mean ranks moved by at most about 0.7 per cell (drake `chaser` 3.41 to 2.18 is the exception: its all-DEF plan meets casters that ignore DEF, which the flat opponents never were; issue #114), Mandrake (added alongside) dropped 0.3-1.3 (its low damage cannot outlast the C healers; #114), Shade Hound held, and the grades now spread instead of landing on one rank per cell. Chase power retune at content 4.4, 200 seeds, no books: all 12 checks pass; averaged over `focus`, `hoarder`, `balanced` and `chaser`, the in-run chase trees add 5.6-7.7 floors and the book chase trees 10.8-12.0 (targets 5-8 and 10-15). Single cells still stray (slime `chaser` gains more from its in-run tree, drake `focus` and `hoarder` 3.3 in-run), and the full `focus` Dungeon team clears 95% of days.

**Output.** `--format text` (default) prints the sanity checks first, then one aligned table per section with a header naming the content version and hash, the seeds and the books; `json` gives the same report as one object (`content`, `options`, `ranks`, `checks` as `{id, status, value, limit, n, rule, detail}` with `value` null on a SKIP, `cells`, `tower`, `bookCells`, every spread as `{n, mean, sd, min, p10, p50, p90, max}`; a cell's `gauntletStallMod` is the stall-floor counts, index i = floor reached mod `GAUNTLET_BOSS_EVERY`; each cell and book cell carries `capstoned`, `capstoneRate`, `capstoneCycle` and `chase` as `[{tree, unlocked, unlockRate, capstones, capstoneRate, capstoneCycle, conditions: [{id, label, held, heldRate}]}]`; each cell carries `capstoneBase`, `capstoneGainRun`, `capstoneGainBook` and `fusionGain` (chase power, as spreads) and `opponents` as `[{id, rank, rolled, fought, won, winRate}]`); `csv` gives long form, one metric per row: `section,species,policy,team,metric,n,mean,sd,min,p10,p50,p90,max` (rates and grade shares put the fraction in `mean`; gauntlet `stall_mod_<i>` is the share of the runs that did not clear (`n`) ending on a floor with that remainder; section `books` holds the canary's grades; section `chase` (`books_chase` for the canary) holds `capstone_rate`, `capstone_cycle`, then per chase tree `<tree>_unlock_rate`, `<tree>_capstone_rate`, `<tree>_capstone_cycle` and per condition `<condition id>_held_rate`; section `capstone` holds `base_floor`, `gain_run`, `gain_book` and `gain_fusion` (chase power); section `opponent` holds per pool monster `<id>_rolled_rate` (share of the cell's runs, `n`) and `<id>_win_rate` (`n` = rank fights against it); section `check` has two rows per check, metric `<id>` with the measured value and `<id>_warn` with 1 on WARN and 0 on PASS, both empty on a SKIP). `--out` writes to a file instead of stdout.

**Deterministic.** The same content and arguments give the same bytes. `--jobs` (default the processor count) runs samples in parallel; results are collected by index, so it never changes the report. 200 seeds of every species and policy, with the tower, take a few seconds.

**`check.sh`** runs `balance --seeds 2` in all three forms (section 6) so the verb cannot rot; no number in it can fail the build (a WARN prints, the exit stays 0).
