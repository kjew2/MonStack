# Engine bridge contract (browser UI <-> C# core)

The browser UI in `web/` plays through the C# `IdleRng.Sim` compiled to WebAssembly. `src/IdleRng.Web` is an **adapter** over `AccountOps`, `TrainingRun`, `ContentDb` and the account save in `IdleRng.Runtime`: it maps them onto the JSON shapes below and adds no game rules. Changes to the core stay in the core; this file changes only when the bridge does. (The idle game's bridge, `Init`/`Advance`/`State`/`Query`/`Cmd`, was removed with that game at `specs/13` T7.)

Build: `web/wasm/build.sh` (packs `Content/*.json` with `pack-content.js`, publishes `src/IdleRng.Web`, writes `web/wasm/dist/`). Test: `node web/wasm/test/run-trainer.js`.

`web/engine.js` is the loader: `IdleEngine.loadWasm(opts)` fetches `wasm/dist/_framework/dotnet.js`, starts the runtime and returns `{ host, contentPack }`; if loading fails it rejects with a message the UI shows (bundle not built, page opened as a file, or the load error). `web/run-engine.js` builds the facade on top.

## The bridge

The static class `IdleRng.Web.Engine` exposes the monster trainer (`specs/14`, `15`, `18`, `19`) through `src/IdleRng.Web/RunHost.cs`, which holds the player's `AccountState` and its active run. Every member is `[JSExport]`: strings, ints and doubles only, JSON text for structure, errors come back as `{"error":"..."}`, nothing throws across the boundary except `RunInit` (bad content).

```
RunInit(string contentJson) : void
RunContent() : string                          // JSON labels and tables, never changes in a session (below)
AccountNew(string seedHex) : string            // a new account (every species owned, no shards); returns AccountView
AccountLoad(string saveText) : string          // AccountSave text -> account; {"error"} when damaged or the active run no longer replays
AccountSave() : string                         // AccountSave text (header line + JSON; the active run is its replay recipe)
AccountView() : string                         // account JSON (below)
AccountCmd(string name, string argsJson) : string
RunState() : string                            // the active run's state, or "null"
RunCmd(string name, string argsJson) : string  // result object; {"ok":false,"reason"} when the engine refuses
```

Account commands (`AccountOps`): `startRun(species, books[])`, `finishRun(utcMs)` (pays shards, seals the Vault entry, returns `{shards, entry, booksReturned}`), `buyTalent(id)`, `abandon` (pays and seals nothing; unused books return to the inventory), `debugGrantBook(id, n)`.
Run commands (`TrainingRun`): `drill(stat)` (tier, gain, new value, energy, injured, milestones), `rest`, `fight` (won, rank before/after, SP, units' final HP and tallies, the action log capped at 400; a skill action carries `skill` (name) and `skillId`), `take(choice, slot)`, `discard`, `spend(slot)`, `book(id)`.

`RunContent()`: `{version, consts{runCycles, runAp, energyMax, fightEvery, statMax, treeSlots, drillEnergyCost, restEnergy, spPerCycle, spPerWin, superP, shardsPerCycle, shardsPerRank}, stats[], milestones[], elements{id:{name,color}}, species[{id,name,element,base,growth,trees}], ranks[{id,name,letter,element,stats}], trees{id:{id,name,kind,nodes[{id,cost,kind,name,desc,last}],source (where the tree comes from, in words),conditions[text],conditionTrees[treeId]}}, books{id:{name,tree}}, talents[{id,name,desc,effect,maxRank}], statInfo{id:{name,desc}}, nodeTips{nodeId:tip}, skillNode{skillId:nodeId}}` (tooltips, below).

`AccountView()`: `{seed, runsStarted, shards, hasRun, owned[], books{id:n}, talents[{id,rank,nextCost|null}], mods{drillCostMult,statFloorMult,draftSizeBonus,startSp}, vault[{species,seed,grade,rank,end,cycles,fightsFought,fightsWon,hash,sealedUtcMs,stats,trees[{id,bought}]}]}` (the replay log stays in the save, not the view).

`RunState()`: `{species, seed, phase (training|awaitfight|finished), end, cycle, ap, energy, sp, rank, fightsFought, fightsWon, drills, rests, fightDue, drillCost, odds{fail,super,success,injury}, stats, growth, opponent{name,element,stats}, slots[null|{tree,bought}], offers[{source,stat,threshold,trees[]}], books[], chase[{tree,unlocked,met[]}], tally, milestonesHit[], hash, nowTitle, statNow{id:text}}`; `opponent` also carries `nowTitle` and `statNow`. Odds, costs, growth, chase conditions and every label come from the C# side; the UI computes nothing.

**Tooltips** (`specs/16` section 9; drawn by `web/tooltips.js`). All text, built by `BattleText` from content strings and the battle formulas; the UI only lays it out.
- `statInfo[id]`: the stat's `name` and `desc` (what it does, constants filled in).
- `statNow[id]` and `nowTitle` on the run state: what the monster's current value does against the coming rank opponent ("Takes 68% of physical damage"), before buffs and passives; `opponent.statNow` and `opponent.nowTitle` are the same for the opponent against the monster.
- `nodeTips[nodeId]`: `{title, text, tags[], facts[[label, value]]}` for every tree node: a skill's effect, amount, damage type, element, target, duration, cooldown or trigger, AI condition and SP cost; a rule's description; for a filler node, what its stat does.
- `skillNode[skillId]`: the node that grants a skill, so a skill named in the fight log opens that node's tooltip.

`web/run-engine.js` wraps this (`RunEngine.create`) and keeps the whole save in `localStorage['idle-rng-account-v1']`. A save that does not load is kept under `idle-rng-account-v1-unloadable`, never overwritten.

## Not exposed yet

The core's command log, `StateHash` and `Replay` (`specs/03` section 1, `specs/10` section 6) are not exposed through the bridge yet; they become relevant once the web client talks to a certifier (`specs/10` section 4).
