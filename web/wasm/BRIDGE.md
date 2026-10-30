# Engine bridge contract (JS UI <-> C# core)

> **REVISION (read first).** While this contract was being drafted, main gained a complete C# proof of concept (`src/IdleRng.Sim`, `IdleRng.Runtime`, `IdleRng.Bots`, tests, Unity client, `Content/*.json` tables). That code is the real backend and **the source of truth**. Everything below that says "WS-A writes the C# core" or "bit for bit identical to `web/sim.js`" is superseded:
> - **No new C# sim.** The wasm host (`src/IdleRng.Web`) wraps the existing `Simulation`, `ContentDb` loader and `IdleRng.Runtime` types. Changes to existing projects must be additive (new public members, new files). Do not delete or rewrite existing files; `dotnet test` in `src/` must stay green.
> - **`web/sim.js` is a fallback and a UI reference, not a spec.** It differs from the C# core on purpose (`BASE_ATK` 20 vs 10, the `intro` boss rule with 4x HP below stage 60, possibly more). Parity fixtures are therefore a **diff report**, not a pass/fail gate: generate it, list every divergence and classify it as an intentional tuning difference or a suspected bug in either side. Align `web/content.js`/`web/sim.js` to the C# tuning only if it is a small, clearly-correct change (report it; do not do it silently).
> - `Content/content.json` (WS-B) is a JS-reference export only; the C# loader reads the existing `Content/*.json` tables. Do not feed `content.json` to C#.
> - The host's job is an **adapter**: map the existing `Simulation` state, queries, commands and events onto the JS-shaped `UiState`, query names, command results and event objects defined below, so `web/engine.js` (WS-D) works unchanged.
> - Additions from specs/03 section 22 and specs/10 (command log, `StateHash`, `Replay`) are out of scope for this pass unless already present; list what is missing.

Goal: the browser UI plays through the real C# `IdleRng.Sim` compiled to WebAssembly, instead of `web/sim.js`. `web/sim.js` stays as a fallback and as the reference the C# core is tested against. This file is the contract all workstreams build to. If a workstream must change it, it edits this file and says so in its report; it does not diverge silently.

## Layout and ownership

| Path | Owner | Notes |
|---|---|---|
| `src/IdleRng.Sim/` | WS-A | Pure C# core. `netstandard2.1`, `LangVersion 9.0`, no package references, no `UnityEngine`, no JSON, no reflection, no clock reads, no `Math.Pow/Exp/Log`, no `System.Random` (specs/03 sections 2, 4). Defines `ContentDb` and `GameState` plain classes. |
| `src/IdleRng.Sim.Tests/` | WS-A | `net8.0`, NUnit 3.14. |
| `Content/content.json` | WS-B | Generated from `web/content.js`. Single source of numbers for C#. |
| `web/tools/`, `Fixtures/Parity/` | WS-B | Exporter, drift check, parity fixture generator and fixtures. |
| `web/engine.js`, `web/app.js`, `web/index.html`, `web/build.js` | WS-D | Backend-agnostic facade, JS backend, WASM backend loader, UI switch. |
| `src/IdleRng.Runtime/` | WS-C (phase 2) | `ContentLoader` (JSON -> `ContentDb`), `SaveSerializer`. `netstandard2.1`, Newtonsoft.Json 13.0.3. |
| `src/IdleRng.Web/`, `web/wasm/build.sh`, `web/wasm/dist/` (gitignored) | WS-C (phase 2) | .NET 8 `Microsoft.NET.Sdk.WebAssembly` host exposing `Engine`. |

Nobody runs `git add/commit/push/checkout`. The orchestrator commits. Touch only your own paths.

Toolchain (already installed): `dotnet` 8.0.133 on PATH, workloads `wasm-tools` and `wasm-experimental`, NuGet reachable. Node 18+ and Playwright/Chromium are available. A probe `dotnet publish -c Release` of a `wasmbrowser` template project succeeds in about 3 minutes.

## What is being matched

`web/sim.js` is the behavioural reference. The C# core must reproduce it **bit for bit** for the same seed, content and call sequence: same `double` operations in the same order, same RNG draws in the same order, same event sequence. `web/test.js` (19 checks, run with `node web/test.js`) and `web/bot.js` define expected behaviour; `specs/03`, `01`, `04`, `06`, `10` define intent. Where `sim.js` and a spec disagree, match `sim.js` and list the difference in your report.

## The bridge

One static class `IdleRng.Web.Engine`, all members `[JSExport]`, all arguments and returns `string`, `int` or `double` (JSON text for structured data). One engine instance per page.

```
Init(string contentJson, string saveJson, string profileSeedHex, double nowMs, string profileId) : void
    saveJson empty => new profile (applies tutorial grants, first-day login: same as Sim.newProfile).
    profileSeedHex: up to 16 hex chars, a ulong. Ignored when saveJson is non-empty.
Advance(int ticks) : void                      // == sim.advance
State() : string                               // JSON, UiState (below)
Drain() : string                               // JSON array of events, oldest first; clears the queue
Query(string name, string argsJson) : string   // JSON value
Cmd(string name, string argsJson) : string     // JSON result object
Export() : string                              // save JSON (C# schema; not interchangeable with the JS save)
SetEmitHits(int on) : void
Version() : string                             // e.g. "IdleRng.Sim 0.1.0 netstandard2.1"
```

`argsJson` is a JSON array of positional arguments, `"[]"` for none. Unknown names return `{"error":"unknown"}` for `Cmd` and `null` for `Query`; they never throw across the boundary. A thrown C# exception must be caught in `Engine` and returned as `{"error":"<message>"}`.

### Query names (name -> args -> result), all mirror the `Sim` method of the same name in `web/sim.js`

`hp(stage)`, `bossHp(stage)`, `gold(stage)`, `statCost(n)`, `statMult(n?)`, `gearTier(stage)`, `gearBasePct(piece)`, `gearEffPct(piece)`, `gearSlotMult(slot)`, `gearMult()`, `heroLevel(copies)`, `heroPct(id)`, `heroMult()`, `classMult(k?)`, `dps()`, `hitDamage()`, `hitsToKill(hp)`, `killTimeSeconds(stage)`, `isBossStage()`, `bossClearable()`, `needDps()`, `enhanceCost()`, `enhanceSuccess(level)`, `pityRate(n)`, `freePullsRemaining()`, `chestReadyAtMs()`, `chestReady()`, `bulkCost(n, m)`, `maxAffordable(gold?)`, `pullPrice(count)`, `bestParty()`, `autoEquipWouldChange()`, `nowMs()`, `validate()` (array of error strings), `consolidatedRates()`.
A `piece` argument is `{"tier":int,"rarity":int,"enh":int}`. Optional args may be omitted or `null`.

### Command names (name -> args -> result object, same shape as `sim.js` returns)

`buyStatLevels(m)`, `buyStatLevelsMax()` (returns a bare JSON number), `enhance(slot)`, `attemptBoss()`, `pull(count)` (result `{ok,results:[{id,name,tier,isNew,levelBefore,levelAfter,shards,pity,forced}],paidWith}` or `{ok:false,reason}`), `equipHero(slot,id)`, `unequipHero(slot)`, `autoEquip()` (bare number), `claimChest()`, `syncWallClock(nowMs)`, `debugGrantGold(n)`, `debugGrantGems(n)`, `debugSetStage(stage)`.

### UiState (what `State()` returns)

The same shape and field names as `sim.state` in `web/sim.js`, camelCase, minus `rng` internals and with seeds omitted:
`{ schema, contentVersion, profileId, createdUtcMs, tick, stage:{stage,killsInStage,boss,bossFailsOnStage}, combat:{phase,spawnTimer,enemyHp,enemyMaxHp,enemyIsBoss,attackTimer,bossTimer}, cur:{gold,gems,shards,freePullsDaily,freePullsBanked}, statLevel, gear:[null|{tier,rarity,enh}] (6), heroes:{owned:{id:copies},party:[null|id] (6)}, banner:{pity,totalPulls}, classAdvances, daily:{dayKey,progress:[],rewarded:[]}, prog:{highestFirstClearBoss,chestTimerStartMs,tutorialGranted}, clock:{lastSyncUtcMs,tickAtLastSync}, life:{kills,bossKills,bossFails,gearDrops,enhAttempts,enhSuccesses,goldEarned,offlineGold,firstWallStage,pulls,lplus} }`.
`boss` is `"none"|"pending"|"walled"`; `phase` is `"spawning"|"fighting"`. Numbers are JSON numbers; doubles are written in .NET/JS shortest round-trip form so they parse back to the identical double. `owned` and `heroes.owned` key order is sorted by id.

### Events (what `Drain()` returns)

Same objects as `sim.js` emits (`type`, `tick`, plus the fields in `web/sim.js` for each type): `hit`, `spawn`, `kill`, `bossCleared`, `bossFailed`, `firstClear`, `stageAdvanced`, `classAdvanced`, `gearDropped`, `questCompleted`, `chestClaimed`, `dayRolledOver`, `offlineGold`, `clockWentBackwards`, `statLeveled`, `enhanced`, `pulled`, `stageCap`. Same order within a tick. `hit` events are only emitted after `SetEmitHits(1)`.

### JS-side facade (`web/engine.js`)

Exposes an object with the **same members `web/app.js` uses on `sim` today** (`state`, `heroById`, every method listed above plus `advance`, `drain`, `emitHits`, `newProfile`-equivalent boot). Two backends: `js` (wraps `web/sim.js` directly) and `wasm` (calls `Engine`). In the wasm backend `state` is a cached object refreshed from `State()` at most once per `advance`/command batch, never per property read. Formatting (`formatShort`) and hero names/tiers/sprites stay in JS; formatter parity is covered by fixtures (WS-B).

Backend selection: `?engine=js|wasm` URL parameter or `localStorage['idle-rng-engine']`; default `wasm` if `web/wasm/dist` loaded successfully, else `js`. A visible chip in Settings shows the active backend and `Version()`. The two backends use **different localStorage save keys**; importing a save from the other backend fails with a clear message.

## Parity fixtures

`Fixtures/Parity/*.json` (WS-B): `{ name, content: "Content/content.json", seedHex, nowMs, steps: [ {op:"advance",n} | {op:"cmd",name,args} | {op:"sync",nowMs} | {op:"state"} | {op:"drain"} | {op:"query",name,args} ], expect: [ <one entry per state/drain/query/cmd step: the JS result> ] }`. Generated by running `web/sim.js`; consumed by C# tests (WS-A for the sim-level runner, WS-C for the full `Engine` and wasm run). Comparison is exact on every number and string. Scenarios must cover: first minute of play; 10 000-tick fights with gear drops; a boss fail and retry; wall farming; stat/enhance/equip/pull(1)/pull(10) commands; hard pity; offline catch-up (8 h cap) and day rollover; clock going backwards; class advance via `debugSetStage`; a 30-day greedy bot run with a state snapshot at each day boundary.

## Specs 03 and 10 additions the C# core must include (WS-A)

`ICommandLog` with `CommandRecord(Tick, Kind, A, B, LedgerId)`, `Simulation.StateHash()` (SHA-256 over a canonical encoding: fixed field order, maps sorted by key, doubles as raw IEEE bits), and `Simulation.Replay(...)`; see `specs/03` section 22 and `specs/10` sections 4 to 6. The bridge does not expose them yet.

## Definition of done (whole effort)

1. `dotnet test` passes in `src/IdleRng.Sim.Tests` (spec vectors, replay equivalence, parity fixtures).
2. `node web/test.js`, `node web/bot.js`, and the content drift check pass.
3. `web/wasm/build.sh` produces `web/wasm/dist/`, and a headless-Chromium run of the page with `?engine=wasm` plays, saves, reloads, and matches the JS backend on the parity fixtures.
4. The page falls back to the JS backend with a visible notice if the wasm bundle fails to load.
