# 09 - Test Harness, Bots and Golden Files

Scope: the headless bot project `IdleRng.Bots` (`src/IdleRng.Bots`), the tests, the golden files and `check.sh`. This spec was the idle game's balance harness (day-by-day bots, an acceptance table and a 20-seed sweep); that went with the idle game at `specs/13` T7. Balance work resumed on 2026-10-31 (`CLAUDE.md`); a run-balance harness is to be built on the bots below.

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
  Report/TableWriter.cs     how a content version and hash are printed
src/IdleRng.Bots.Tests/     RunBotTests
```

## 3. Golden files

`golden/` holds generated reference output of fixed commands. **Never open, grep or hand-edit them**; regenerate through the CLI. `golden --check` regenerates each in memory and compares bytes.

| File | What it pins |
|---|---|
| `golden/battle-sim-vectors.txt` | Five fixed fights (1v1, 4v1, 4v4, 2v3) with every skill kind, a trigger and a rule modifier: outcome, ticks, tallies, hash (`specs/16` section 8) |
| `golden/training-run-vectors.txt` | Every species x run policy at seed 7, plus a run with every book: summary and run hash (`specs/14` section 9) |
| `golden/gauntlet-vectors.txt` | Each species after a `focus` bot run, sent up the gauntlet: floor, killer, title, hash (`specs/19` section 6) |

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
dotnet run -c Release --project src/IdleRng.Bots -- golden --check | --update [--golden-dir path] [--content dir]
```

`train` prints every command, a summary and the run hash. `--content` defaults to the repo's `Content/`.

## 6. Tests and `check.sh`

`./check.sh` runs, in order: the content build with no drift (`tools/content_build.py --check`), the builder's tests (`tools/test_content_build.py`), the content id check against the merge base with `origin/main` (`E090`, `E091`, `specs/05` section 4; skipped locally when `origin/main` is missing, an error in CI), the .NET build, all tests, `golden --check`, then (when the wasm-tools workload is installed) the WebAssembly build and the headless-browser tests: one plays a whole run through the buttons (`web/wasm/test/run-trainer.js`), the other checks each main screen at phone width (no horizontal scroll, no console error), a tree overwrite, save/reload/resume with the same run hash, and a stale or damaged save (`web/wasm/test/flows.js`). `--fast` skips the golden tests and the golden check; `--no-wasm` skips the web steps; `--no-dotnet` runs only the content check (a partial pass: say so). A change is not done while any check fails.

When the `CI` environment variable is set (anything but empty or `false`; GitHub Actions sets `CI=true`), a missing wasm-tools workload in full mode is an error, not a skip, so CI cannot pass without the web steps. `--no-wasm` still skips them on purpose.

**CI.** `.github/workflows/check.yml` runs the full `./check.sh` on every pull request, every push to `main` and on manual dispatch, on `ubuntu-latest`. It installs Python 3.12, Node 22, the .NET 8 SDK (in its own directory, so newer SDKs on the runner image are not selected and `wasm-tools` matches `net8.0`), the wasm-tools workload, and Playwright (pinned in the workflow, installed outside the repo and found through `NODE_PATH`, since the repo has no `package.json`) with Chromium. NuGet packages are cached. Making the check required for merging is a branch-protection setting on GitHub, not part of the repo.

`RunBotTests` plays every policy on every species, with and without books, and checks that the command log replays to the same hash.

## 7. Open

- A run-balance harness (score distributions over many seeds per policy and species) when balance resumes.
