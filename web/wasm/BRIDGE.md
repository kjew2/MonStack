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
AccountLoad(string saveText) : string          // AccountSave text -> account; {"error"} when damaged or from a newer schema; a run that no longer replays is dropped and the view carries "notice"
AccountSave() : string                         // AccountSave text (header line + JSON; the active run is its replay recipe)
AccountView() : string                         // account JSON (below)
AccountCmd(string name, string argsJson) : string
RunState() : string                            // the active run's state, or "null"
RunCmd(string name, string argsJson) : string  // result object; {"ok":false,"reason"} when the engine refuses
```

Account commands (`AccountOps`): `startRun(species, books[])`, `finishRun(utcMs)` (pays shards, seals the Vault entry, returns `{shards, entry, booksReturned}`), `buyTalent(id)`, `abandon` (pays and seals nothing; unused books return to the inventory), `dismantle(vaultIndex)` (removes that entry, `{refund}`; refused when locked), `lock(vaultIndex, bool)`, `debugGrantBook(id, n)`, and the read-only `gauntlet(vaultIndex)`: the floors that Vault entry fought, for the wheel (`specs/19` section 7), by replaying `GauntletSim.Run(c, entry.Fighter(c), entry.Seed)`: `{floorReached, cleared, killer|null, hash, floors[{floor, enemy, boss, won, ticks}]}`. `hash` equals the entry's `gauntlet.hash`; `web/run-engine.js` calls it as `run.gauntlet(i)`, without saving.
Run commands (`TrainingRun`): `drill(stat)` (tier, gain, new value, energy, injured, milestones), `rest`, `fight` (below), `take(choice, slot)`, `discard`, `spend(slot)`, `book(id)`.

`fight` returns `{won, rankBefore, rankAfter, sp, outcome, ticks, units[{id, side, hp, maxHp, damage, crits, evades, skills[{name, kind, cooldown}], cd[]}], actionCount, actions[{actor, target, amount, crit, evaded, aoe, counter, skill?, skillId?, kind?, si, hp[], cd[]}]}`. `units` is the fight's unit order (side A, then B); `skills` is a unit's skill list in AI order, passives included, and `cd` its cooldowns left at the end (own actions until ready, indexed like `skills`). Each action carries a snapshot the bridge reads from the live fight as the sim reports it (`BattleSim.Resolve`'s `onStart` hook, `specs/16` section 2): `hp` is every unit's HP after it (indexed like `units`), `cd` the actor's cooldowns after it, and `si` the index in the actor's `skills` of the skill it used (matched by skill id, so an echo counts as that skill; -1 for basic attacks, counters and triggered effects not in its list). Before the first action every unit is at its `maxHp` and before its own first action every skill of a unit is ready. `actions` is capped at 400 of `actionCount`. The UI replays this log at a presentation-only pace (`FIGHT_STEP_MS` in `web/run.js`, a placeholder; 1x, 2x or Skip, kept in `localStorage['idle-rng-fight-speed']`) and computes nothing from it.

`RunContent()`: `{version, consts{runCycles, runAp, energyMax, fightEvery, statMax, treeSlots, drillEnergyCost, restEnergy, spPerCycle, spPerWin, superP, shardsPerCycle, shardsPerRank, vaultCap}, stats[], milestones[], elements{id:{name,color}}, species[{id,name,element,base,growth,trees}], ranks[{id,name,letter,element,stats}], trees{id:{id,name,kind,nodes[{id,cost,kind,name,desc,last}],source (where the tree comes from, in words),conditions[text],conditionTrees[treeId]}}, books{id:{name,tree}}, talents[{id,name,desc,effect,maxRank}], gauntlet{floors, bossEvery, enemies{id:{name,element,boss}}}, statInfo{id:{name,desc}}, nodeTips{nodeId:tip}, skillNode{skillId:nodeId}}` (tooltips, below).

`AccountView()`: `{notice?, seed, runsStarted, shards, hasRun, owned[], books{id:n}, talents[{id,rank,nextCost|null}], mods{drillCostMult,statFloorMult,draftSizeBonus,startSp}, vaultCap, vaultOver, vault[{species,seed,grade,rank,end,cycles,fightsFought,fightsWon,hash,sealedUtcMs,locked,refund,stats,trees[{id,bought}],gauntlet}]}` (the replay log stays in the save, not the view). `vaultOver` is how many entries are above `vaultCap` (`startRun` is refused while it is above 0, `specs/18` section 7); `refund` is the shards dismantling that entry pays; a `vaultIndex` is the entry's position in `vault` (oldest first). `gauntlet` is `{floor, cleared, killer|null, killerName|null, title, label, hash}`, or null for an entry sealed before the gauntlet existed: `title` is the Legacy Title with `{killer}` filled (`GauntletSim.TitleText`), `label` the line the UI shows, "Floor 42: Professional Punching Bag" (a cleared tower: "All 100 floors: Tower Breaker").

`RunState()`: `{species, seed, phase (training|awaitfight|finished), end, cycle, ap, energy, sp, rank, fightsFought, fightsWon, drills, rests, fightDue, drillCost, odds{fail,super,success,injury}, stats, growth, opponent{name,element,stats}, slots[null|{tree,bought}], offers[{source,stat,threshold,trees[]}], books[], chase[{tree,unlocked,met[]}], tally, milestonesHit[], hash, nowTitle, statNow{id:text}}`; `opponent` also carries `nowTitle` and `statNow`. Odds, costs, growth, chase conditions and every label come from the C# side; the UI computes nothing.

**Tooltips** (`specs/16` section 9; drawn by `web/tooltips.js`). All text, built by `BattleText` from content strings and the battle formulas; the UI only lays it out.
- `statInfo[id]`: the stat's `name` and `desc` (what it does, constants filled in).
- `statNow[id]` and `nowTitle` on the run state: what the monster's current value does against the coming rank opponent ("Takes 68% of physical damage"), before buffs and passives; `opponent.statNow` and `opponent.nowTitle` are the same for the opponent against the monster.
- `nodeTips[nodeId]`: `{title, text, tags[], facts[[label, value]]}` for every tree node: a skill's effect, amount, damage type, element, target, duration, cooldown or trigger, AI condition and SP cost; a rule's description; for a filler node, what its stat does.
- `skillNode[skillId]`: the node that grants a skill, so a skill named in the fight log opens that node's tooltip.

`web/run-engine.js` wraps this (`RunEngine.create`) and keeps the whole save in `localStorage['idle-rng-account-v1']`. A save that does not load is kept under `idle-rng-account-v1-unloadable`, never overwritten.

## Not exposed yet

The core's command log, `StateHash` and `Replay` (`specs/03` section 1, `specs/10` section 6) are not exposed through the bridge yet; they become relevant once the web client talks to a certifier (`specs/10` section 4).
