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
  Golden/TowerVectors.cs    the same monsters, alone and together, up the Daily Tower on two days
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
| `golden/tower-vectors.txt` | The same monsters, alone and up to four together, up the Daily Tower on days 0 and 20391: lineup, floor, cleared, vents, phases, hash (`specs/21` section 9) |

Each file's first line carries the content version and short hash (`content=3.0/b7ba908c`). Any change to a `content_src/` file changes the hash, so every golden is regenerated and the merge commit says so.

On a mismatch `golden --check` prints the first differing lines and a verdict:

- **content changed**: the header hash differs from the loaded content. If the change is intended, `golden --update`.
- **sim or bot behaviour changed with identical content**: a determinism or logic regression unless a sim change in the same commit explains it. Do not update until the cause is understood.

Regenerate with `dotnet run -c Release --project src/IdleRng.Bots -- golden --update`.

## 4. Bots

`IRunPolicy.Next(content, run)` returns the next command or null when it has nothing left. `ThresholdRunPolicy` resolves offers (empty slot first; chase trees overwrite the least-invested slot that is not one of their prerequisites; otherwise discard), uses books at once, buys the first affordable next node once the cycle reaches `spendFrom`, fights when due, rests below an energy threshold and otherwise drills the stat a pick function chooses. Policies: `balanced`, `focus`, `reckless`, `hoarder` (`specs/15` section 8). `RunBot.Play` plays a whole run, optionally logging a line per command.

## 5. CLI

```
dotnet run -c Release --project src/IdleRng.Bots -- train  --species ID --policy P --seed N [--books a,b]
dotnet run -c Release --project src/IdleRng.Bots -- balance [--seeds N] [--seed-base N] [--species a,b] [--policies a,b]
                                                            [--books none|all|a,b] [--no-tower] [--tower-day N]
                                                            [--format text|json|csv] [--jobs N] [--out path]
dotnet run -c Release --project src/IdleRng.Bots -- golden --check | --update [--golden-dir path] [--content dir]
```

`train` prints every command, a summary and the run hash. `balance` prints the run-balance report (section 7). `--content` defaults to the repo's `Content/`.

## 6. Tests and `check.sh`

`./check.sh` runs, in order: the content build with no drift (`tools/content_build.py --check`), the builder's tests (`tools/test_content_build.py`), the content id check against the merge base with `origin/main` (`E090`, `E091`, `specs/05` section 4; skipped locally when `origin/main` is missing, an error in CI), the .NET build, a `balance --seeds 2` smoke run in each output form (section 7; it checks that the verb runs and its JSON parses, never a number), all tests, `golden --check`, then (when the wasm-tools workload is installed) the WebAssembly build and the headless-browser tests: one plays a whole run through the buttons (`web/wasm/test/run-trainer.js`), the other checks each main screen at phone width (no horizontal scroll, no console error), a tree overwrite, save/reload/resume with the same run hash, and a stale or damaged save (`web/wasm/test/flows.js`). `--fast` skips the golden tests and the golden check (the balance smoke run stays); `--no-wasm` skips the web steps; `--no-dotnet` runs only the content check (a partial pass: say so). A change is not done while any check fails.

When the `CI` environment variable is set (anything but empty or `false`; GitHub Actions sets `CI=true`), a missing wasm-tools workload in full mode is an error, not a skip, so CI cannot pass without the web steps. `--no-wasm` still skips them on purpose.

**CI.** `.github/workflows/check.yml` runs the full `./check.sh` on every pull request, every push to `main` and on manual dispatch, on `ubuntu-latest`. It installs Python 3.12, Node 22, the .NET 8 SDK (in its own directory, so newer SDKs on the runner image are not selected and `wasm-tools` matches `net8.0`), the wasm-tools workload, and Playwright (pinned in the workflow, installed outside the repo and found through `NODE_PATH`, since the repo has no `package.json`) with Chromium. NuGet packages are cached. Making the check required for merging is a branch-protection setting on GitHub, not part of the repo.

`RunBotTests` plays every policy on every species, with and without books, and checks that the command log replays to the same hash. `BalanceTests` covers the harness: the distribution maths (nearest-rank percentiles in integer arithmetic, empty input), the per-cell and tower aggregation on hand-made samples, that a report is byte-identical across repeats and `--jobs` values, that a cell matches single runs and what `Vault.Seal` and `AccountOps.Payout` record, option validation, and that the JSON parses and every CSV row has every column.

## 7. Run-balance harness

**A report, not a gate, and not a golden.** `balance` measures the placeholder numbers; it has no targets and exits 0 whatever they say. **Targets are to be agreed with kj** (issue #89) before any large retune (`CLAUDE.md`); once agreed they go in a table here, and only then could a check compare against them.

**What it plays.** For every species x run policy (default: every species in content order and every policy in section 4), `--seeds` runs (default 200) on seeds `--seed-base` (default 1), `--seed-base` + 1, ... Each run is `RunBot.Play` from a fresh account (no talents, `RunModifiers` defaults), bringing the `--books` (default none; `all` = every skill book), then sealed the way `AccountOps.FinishRun` seals it: `AccountOps.Payout` shards and `GauntletSim.Run` on the run seed. Unless `--no-tower`, each policy's monsters then climb the Daily Tower (`TowerSim.Run`): each species alone, and one of each species (the first `MON_SIDE_SIZE`) from the same seed as one team, on day `--tower-day` (default 0) + the seed's index, so the lineups vary across the sample. A tower sample's shards are `TOWER_SHARDS_PER_FLOOR` x floors cleared (a first attempt that day) and its books the `TowerSim.BookDrop` hits on the cleared floors.

**What it reports.** Per species x policy:

| Section | Metrics |
|---|---|
| Grades | % of runs ending at each rank (E to S), mean rank index (E = 0), p10/p50/p90 |
| Runs | injury rate (an injury ends the run), cycles played, rank fights won and lost, fight win rate (won / fought over the cell), shard payout |
| Gauntlet | floor reached (`GAUNTLET_FLOORS` + 1 = cleared), clear rate, the killers that ended most runs |
| Daily Tower | per policy x team: floors cleared, clear rate, shards, books |

Every spread is a `Distribution`: mean, population standard deviation, min, max and nearest-rank p10/p50/p90 (the value at sorted index `ceil(p x n / 100) - 1`, computed in integers).

**Output.** `--format text` (default) prints one aligned table per section with a header naming the content version and hash, the seeds and the books; `json` gives the same report as one object (`content`, `options`, `ranks`, `cells`, `tower`, every spread as `{n, mean, sd, min, p10, p50, p90, max}`); `csv` gives long form, one metric per row: `section,species,policy,team,metric,n,mean,sd,min,p10,p50,p90,max` (rates and grade shares put the fraction in `mean`). `--out` writes to a file instead of stdout.

**Deterministic.** The same content and arguments give the same bytes. `--jobs` (default the processor count) runs samples in parallel; results are collected by index, so it never changes the report. 200 seeds of every species and policy, with the tower, take a few seconds.

**`check.sh`** runs `balance --seeds 2` in all three forms (section 6) so the verb cannot rot; no number in it can fail the build.
