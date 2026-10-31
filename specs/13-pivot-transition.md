# 13 - Transition Plan: Idle RNG to Monster Trainer

Status: **MVP done (T1-T7); T8 and T9 done.** The idle game is removed (T7, 2026-10-30). Sections 2 to 4 describe the plan as it was made and what carried over; section 6 has each phase's result. How the repo moved from the idle stage-push game to the monster-raising game in `specs/12`. Section 9 lists the decisions kj needs to make before the phases they block.

## 1. Approach

**Pivot in place, keep the build green, cut over at the end** (kj, 2026-10-30).

- Same repo. The valuable parts here are infrastructure, not the idle loop: the deterministic pure-C# core, named RNG streams, command log and state hash, content pipeline, save system, bot harness, WebAssembly prototype, gacha math and the 4v1 ATB battle. A new repo would mean rebuilding or copying all of it.
- New systems are built beside the idle loop, in new folders, with their own tests. The idle game keeps building and passing `./check.sh` until the web prototype switches to the new loop (phase T6). Then the idle systems are deleted in one change (T7).
- **No save migration from the idle game.** There are no released players (TestFlight, #11, is not done). The new game starts a new save lineage under a new save key; old idle saves are ignored, not migrated (kj, 2026-10-30).
- **No rename yet.** `IdleRng` stays in namespaces, assemblies and paths until the game has a name (#47). Then one mechanical rename. Renaming twice is churn.
- **Balance resumed (2026-10-31).** Every number in the new systems is still first-pass (`CLAUDE.md`). The idle balance criteria AC1-AC5 retire with the idle loop.
- **Web first** (kj, 2026-10-30; client technology was left to the agent). The call: the web client (`web/` on the C# core through WebAssembly) is the game's client, not just a prototype. Reasons: the drafts target Steam and web; the game is menu- and number-heavy with light animation, which HTML does well; the WebAssembly bridge already runs the real C# core and plays on a phone through `./play.sh`; the Unity project has never been opened in Unity (#10) and needs a Mac to build. Steam can ship the same web build in a desktop wrapper (Tauri or Electron) and mobile through a web-view wrapper, both decided at T13. The Unity client is paused, not deleted; reopen it only if a native mobile build becomes a priority and the wrappers fall short.

## 2. What carries over

| Area | Today | In the new game | Action |
|---|---|---|---|
| Pure sim rules (`specs/00` section 5.1) | `IdleRng.Sim`: no IO, clock, JSON, reflection or `Math.Pow` | Same rules for the run engine, battles and gauntlet | Keep |
| RNG streams | `Rng/SplitMix64.cs`, named `(seed, counter)` streams | New streams: `training`, `draft`, `fight`, `battle_sim`, `gauntlet`, `run` (`specs/03` section 5.5); the idle streams were removed | Keep, new streams |
| Command log, replay, `StateHash` | `Replay/`, `Simulation.Replay.cs` | Every run decision is a command, so a run replays exactly; PvP and certification rely on it | Idea kept, code replaced: `TrainingRun.Replay` and the run/fight/gauntlet hashes; only the canonical hash writer survives from `Replay/` |
| Battle engine | `Battle/BattleEngine.cs`: 4v1 ATB, skill vocabulary, triggers | One engine for 1v1 (gauntlet, in-run fights), 4v1 (Daily Tower) and 4v4 (Endless, PvP). Becomes the `BattleSim.Resolve` of `specs/10` section 9.2 | Rebuilt as `IdleRng.Sim.Combat` (T1, `specs/16`) from the same ATB, damage and effect design; the idle engine retires at T7 |
| Skill vocabulary and triggers | `skills.csv` kinds/targets, `BattleTrigger` | Tree nodes, chase traits and rule-breaking passives are written in it; the trigger list grows | Adapt |
| Elements | `elements.csv`, strong/weak wheel | Species and skill types | Keep (section 9 item 9) |
| Gacha math | `Gacha/Gacha.cs`, `specs/01`, pity, 10-pull guarantee, vectors | None: the species gacha was scrapped at T9 (kj, 2026-10-30); species unlock from achievements | The idle code and vectors were deleted at T7; `tools/gacha_model.py` was deleted at T9 and `specs/01` marked retired |
| Content pipeline | `content_src/*.csv` -> `tools/content_build.py` -> `Content/*.json` -> `ContentDb` | New tables: species, stats, train actions, milestones, trees, nodes, chase conditions, books, talents, ladder bosses, floors | Keep pipeline, new tables |
| Save system | `IdleRng.Runtime/Save`: serializer, CRC, self-check, migrations, stores | New `AccountState` + `RunState` schema, new lineage | Keep machinery, new schema |
| Backend seams | `IClock`, `IGachaService`, `ISaveStore`, `IdleRng.Remote` | Same seams; PvP adds `IPvpService` | `specs/08` kept as a design; `IdleRng.Remote`, the clocks and the services wiring were deleted at T7 (`ISaveStore` stays in `IdleRng.Runtime`) |
| Online trust model | `specs/10` | Fits better than before: async 4v4 PvP on stored teams, server-resolved battles | Keep, rewrite the snapshot types |
| Bot harness | `IdleRng.Bots`: runner, policies, golden files, report writer | Bots play runs (random, greedy-stat, point-hoarder) and report run scores and gauntlet floors; goldens cover runs and battles | Replaced: the idle day-runner, sweep and acceptance went at T7; run bots and goldens stay |
| `check.sh`, golden workflow, bench | | Same gates; the bench measures battle and run throughput | Kept, minus the idle tick benchmark and the balance sweep (a battle/run benchmark can return when speed matters) |
| Web prototype | `web/` + `src/IdleRng.Web` WebAssembly bridge | Run screen, drafts, tree view, battle log, ladder/gauntlet result | Keep bridge, new UI |
| Unity client | `unity/IdleRngClient` | Later, if mobile stays a target | Deleted at T7: every screen was an idle-game screen on the idle `Simulation`. Git history has it |

## 3. What retires

Deleted at T7 (2026-10-30), when the web client had been on the new loop for a while:

- Stage push, kill counter, spawn loop, farming DPS (`specs/03` section 7, `specs/11` section 7).
- Gold, stat levels (`specs/04` section 4), gear drops and enhancement (`specs/04` section 5), promotion (`specs/11` section 6).
- Offline catch-up, idle chest, offline gold (`specs/06` section 9, `specs/04`).
- The idle boss battle (`src/IdleRng.Sim/Battle`, `specs/11` section 8), replaced by `IdleRng.Sim.Combat` (`specs/16`).
- Daily quests as written (the Daily Tower replaces them; section 9 item 10).
- Stage-target acceptance AC1-AC5 and `progress_targets`, the power-curve model (`tools/power_curve_model.py`, #66), `specs/02`.
- Idle save migrations v1 -> v2.

## 4. Spec changes

Result at T7: `00`, `03`, `05`, `06` and `09` were rewritten; `02`, `04`, `07` and `11` were deleted; `01`, `08` and `10` kept with a status banner (designs for the gacha and PvP). The plan:

| Spec | Fate |
|---|---|
| `00-overview` | Rewritten at T7 for the new game: scope, constants, vocabulary. Contracts in section 5 mostly survive. |
| `01-gacha-math` | Kept at T7 for the species gacha; **retired at T9** when kj scrapped it. |
| `02-power-curve-math` | Retired. A run-balance model replaces it when balance resumes. |
| `03-sim-core-combat` | Rewritten: run engine instead of the tick loop; RNG, numeric model, determinism and command sections kept. |
| `04-economy-sinks` | Rewritten: shards, gems, books, talent tree. |
| `05-content-data-schema` | Pipeline sections kept; table sections replaced. |
| `06-save-and-offline` | Save machinery kept; schema replaced; offline catch-up removed. |
| `07-client-unity-prototype` | Marked paused; a web client spec replaces it for the MVP. |
| `08-backend-seams` | Kept; `IPvpService` added at T12. |
| `09-test-harness-and-bots` | Rewritten around run policies and run reports. |
| `10-online-competitive` | Kept; snapshot types updated for Vault teams. |
| `11-party-and-builds` | Phases 3-5 cancelled. The battle, kit vocabulary and elements carry into the new battle spec; the rest retires. |
| `12-monster-trainer-design` | The target design. Each built system moves out of it into its own rules spec. |

New rules specs were written as phases landed, one per system: `14-training-run`, `15-skill-trees`, `16-battle`, `18-meta-progression`, `19-gauntlet`, `21-daily-tower` (the Vault is in `13` T4 and `18`; `17` was never used). Each says what is live.

## 5. Priorities and issue triage (applied 2026-10-30 at kj's request)

Priorities after the pivot, used by the `priority:` labels on GitHub issues:

| Priority | Scope | Tracking issue |
|---|---|---|
| **P0** | The MVP: `specs/12` section 12.1, phases T0-T7, plus T9 and T10 (moved up from P1 by kj, 2026-10-31) | #69 (T7: #71, T9: #73, T10: #74) |
| **P1** | Toward 1.0 on Steam and the web: `specs/12` section 12.2, phases T8, T11-T13, plus the launch goals | #70 (T8: #72, T11-T13: #75-#77) |
| **P2** | After 1.0, or parked until a platform decision (iOS/TestFlight, Unity client, live ops) | none |

Triage applied:

| Issue | Result |
|---|---|
| #60 overhaul goal | Closed, superseded by #69 and #70 |
| #55, #57 affix gear, slot enhancement; #56 daily dungeons, #64 uniques; #58 stat upgrades; #63 companion roles | Closed: no gear or companions in the new design; training, trees and the Daily Tower replace them |
| #59 multiplying stats | Closed as folded into `specs/15` and `specs/16` |
| #41 AC4 walls, #66 power-curve model | Closed: they measure the idle loop |
| #54 App Store launch goal | Closed as a duplicate of #70; its launch goals #45-#53 moved under #70 |
| #15, #17-#28, #32, #33 server and trust | P1, under T12 (#76); re-scope when PvP is planned |
| #38 CI workflow | P1 |
| #6, #10, #11 iOS / Unity, #23 Unity pending-pull flow, #52 live ops | P2 |
| #29 remote config | P2 (unchanged) |

## 6. Phases

MVP is T0-T7 plus T9 and T10 (matches `specs/12` section 12.1; T9 and T10 were moved up from P1 by kj, 2026-10-31). Each phase merges on its own with `./check.sh` green.

### T0 - Decisions and freeze

- kj answers the blocking questions in section 9.
- Stop work on idle systems (#55-#59, #64). Done in this change via `CLAUDE.md`.
- Apply the issue triage (section 5) and open a pivot goal issue with one sub-issue per phase. Done 2026-10-30 (#69, #70).

### T1 - Generalized battle core

- A new engine, `IdleRng.Sim.Combat.BattleSim`, takes two sides of 1-4 units each (`specs/16`). It is built beside the idle boss battle (`IdleRng.Sim.Battle`), which stays untouched until T7 deletes it: porting the idle fight would need a save migration and golden churn for code that is about to go.
- A monster stat sheet: HP, STR, INT, WIS, DEX, DEF, SPD, LUK (kj, 2026-10-30), turned into battle values by placeholder formulas.
- Pure `BattleSim.Resolve(rules, sideA, sideB, seed)`, usable by the run engine, gauntlet and later PvP.
- Hooks for rule modifiers (chase traits) and for the entropy gauge, phase shifts and environmental clock (built later, T10).
- Exit: 1v1, 4v1 and 4v4 battles run deterministically, with tests and goldens.

### T2 - Training run engine

**Done** (`specs/14`). kj's cycle flow (2026-10-30): cycles of action points, each tap a Drill (two rolls: tier, then exact gain) or a Rest (energy), then a 1v1 rank fight on the T1 engine; a win promotes the rank.

- `IdleRng.Sim.Training`: `RunState`, commands `Drill(stat)`, `Rest`, `Fight`, a command log, `Replay` and `StateHash`. Turn-based, not ticked.
- One energy meter (fail odds and injury from low energy), stat cap, milestone detection at 300/500/700/900 for T3's tree offers. Stream `training`; fight seeds derive from the run seed.
- Content `2.4`: `species` (3 placeholders), `drill_tiers`, `fight_ranks` (E-S), `stat_milestones`, run constants.
- Bots: `balanced`, `focus`, `reckless` run policies, the `train` CLI verb, `golden/training-run-vectors.txt`.
- Not yet: skill points (T3), retiring early, tree drafts on milestones (T3).

### T3 - Skill trees

**Done** (`specs/15`). kj (2026-10-30): SP per cycle plus a bonus per rank fight win; trees are lines of nodes with about 75% filler and the chase trait last; points in an overwritten tree are lost and a chase tree whose prerequisite is overwritten locks again; chase trees' capstones are much stronger and more unique.

- Content `2.5`: `monster_skills`, `trees`, `tree_nodes`, `chase_conditions`, `skill_books`, species native trees, SP and slot constants. Placeholder content: 3 species x 2 native trees, 10 generic trees, 1 book tree, 2 chase trees.
- Run commands `SpendPoint`, `TakeTree`, `DiscardOffer`, `UseBook`; milestone drafts on a `draft` stream; chase unlock and re-lock; run tallies from rank fights; the monster's fight kit from its bought nodes.
- New rule modifier `echo_cast` (Arcane Overflow's capstone).
- Books are passed into `TrainingRun.Start` until the Daily Tower drops them (T10).

### T4 - Run end: grade, Vault

**Done** (`Training/VaultEntry.cs`). The rank reached in the run's rank fights (E-S) is the end-of-run grade, with a stat breakdown (kj, 2026-10-30). The boss-tower gauntlet with the wheel spin and Legacy Titles is T8, after the MVP.

- `Vault.Seal(content, run, sealedUtcMs)` freezes a finished run into a `VaultEntry`: species, grade (rank id), end reason, cycles, fights, final stats, slotted trees with bought nodes, the seal time (caller-supplied; the sim has no clock), the run hash, and the seed, books and command log. `Fighter(content)` rebuilds the battle snapshot by replaying the log, so an entry can be re-checked against its hash.
- Unspent skill points are lost at sealing. Tree commands still work after the run ends, so the player can spend them first.
- Injured runs are vaulted too. Storing entries in the account is T5; until then the web page keeps them in their own storage key.
- Exit: a finished run produces a grade and a Vault entry (`VaultTests`).

### T5 - Account and meta-progression

**Engine done** (`specs/18`). Built in parallel with the T6 web slice by a second session; the web page wires the account in next.

- `AccountState` and `AccountOps`: owned species (all MVP species until T9, which made them unlock from achievements), the Vault (`VaultEntry`, T4), Training Shards, skill books, talent ranks, the run in progress. Start and finish runs, shard payout (cycles played and rank reached), buy talents (from T9, through the post-run vendor).
- `RunModifiers` carry talent effects into a run (drill cost, base stats, draft size, starting SP), stored on the run and the Vault entry so replays match.
- Content `2.6`: `talents` (4 placeholder talents) and shard constants.
- `AccountSave`: a new save lineage in its own store slot; the active run is saved as its replay recipe.
- Exit met in tests: run -> shards -> talent -> the next run differs, saved and reloaded (`AccountTests`, `AccountSaveTests`).

### T6 - Web prototype on the new loop

**Done.** First built ahead of T5 at kj's request (2026-10-30), then moved onto the account (`specs/18`). `web/index.html` plays on a phone: species pick, Drill and Rest with the odds and drill cost shown, rank fights played back action by action (the ability in use, each skill's cooldown as a square tile whose grey overlay recedes downward, issue #85, and live HP; 1x, 2x or Skip, remembered on the device; issue #79) then the battle log, skill point gains shown as they arrive (HUD float-up, fight result badge, unspent SP on the Trees tab, log lines; issue #86), tree drafts into slots, the tree view with buying, the end screen with the grade, stat breakdown, shard payout and achievements earned, the post-run vendor (T9; it replaced the talent shop), locked species with their unlock condition, the achievement list, the skill book inventory and the Vault list (N/cap, lock, dismantle with a confirm; #82; sort, filters, search, a detail sheet and a compare of two, #81, `specs/18` section 9); sealing a monster spins the gauntlet wheel (T8, `specs/19` section 7). The save is the C# `AccountSave` (account, shards, talents, Vault and the active run as its replay recipe) in one browser key. The bridge is `IdleRng.Web.Engine.Run*`/`Account*` (`web/wasm/BRIDGE.md`). Abandoning a run pays and seals nothing and returns unused books (`AccountOps.AbandonRun`, `specs/18`).

- `src/IdleRng.Web` bridge exposes the run engine; `web/` gets the run screen, drafts, tree view, Tactical Challenge battle log, ladder/grade screen, Vault, talent tree.
- The idle UI was removed from `web/` at T7.
- Exit: kj can play full runs on a phone browser via `./play.sh`.

### T7 - Remove the idle game

**Done** (kj, 2026-10-30: "remove the old idle game"). One change, so the build stayed green.

- Deleted from `IdleRng.Sim`: the tick loop and `Simulation`, `GameState` and its parts, the events outbox, command log and idle state hash, the idle 4v1 boss battle, the idle gacha code, the clocks, `BigNumber`, the debug commands. Kept: `Combat`, `Training`, `Account`, `Gauntlet`, `Content`, `Rng`, and the canonical hash writer (`Replay/Canonical.cs`).
- Deleted: `IdleRng.Remote` and its tests, `IdleRng.Runtime`'s `Session`, `Services` and the idle save system (`SaveSystem`, migrations, self-check, `SaveMeta`; `SaveSerializer` and `SaveConfig` trimmed to the account lineage), the idle bot runner, policies, sweep, acceptance checker and their tests, the whole `unity/` project, `Fixtures/Gacha`, `tools/power_curve_model.py`.
- Content `3.0` (MAJOR: tables removed): the idle tables (`banners`, `boss_rules`, `curves`, `daily_quests`, `gear_*`, `hero_*`, `heroes`, `progress_targets`, `promotions`, `roles`, `skills`, `tutorial_grants`), their constants and strings. `elements` stays.
- Web: `idle.html`, `app.js`, `format.js`, the idle bridge (`Host.cs`, the idle exports), `MAPPING.md` and `run-wasm.js`; `engine.js` is now only the wasm loader.
- `check.sh`: no balance sweep, no idle bench, no idle browser test. The golden files left are the battle, training-run and gauntlet vectors.
- Specs rewritten or deleted as listed in section 4.
- Not done, deliberately: the species gacha code (planned for T9, then scrapped; `tools/gacha_model.py` went at T9) and any rename of `IdleRng`.

### T8-T13 (T9 and T10 are P0, the rest P1)

| Phase | Content |
|---|---|
| T8 | **Done** (`specs/19`; `Vault.Seal` stores floor, killer and Legacy Title; the web client spins the wheel after sealing and shows "Floor N: title" on the grade screen and Vault cards). End-of-run gauntlet (kj, 2026-10-30): the finished monster is simulated up a boss tower, the wheel spin lands on the floor and enemy that beat it, Legacy Title (`specs/12` section 6) |
| T9 | **Done** (`specs/18` sections 4 and 8; issue #73). Re-scoped by kj (2026-10-30): the species gacha is scrapped. A new account owns the starter species (slime); the others unlock from account achievements (content table `achievements`, checked at seal and on load). The always-on talent shop is replaced by a post-run vendor: `VENDOR_OFFERS` (5) random talent offers per sealed run, rarity odds by the run's grade (`vendor_rarities`), bought with Training Shards, gone when the next run starts. Skill Books become the premium gate (dropped by the Daily Tower, T10; selling them is open, #49 and #77). Account save v3. Web: locked species with their condition, achievement list and notices, the vendor screen after the grade screen. `tools/gacha_model.py` deleted, `specs/01` retired, Genetic Aptitude dropped |
| T10 | **P0.** Rules written in `specs/21` (not built; questions 1 and 2 there block the content step). Daily Tower: 4v1 floors, boss phases, entropy gauge, environmental clock, Skill Book drops |
| T11 | Endless Tower: 4v4, mutators, gambit rulebook editor |
| T12 | Async 4v4 PvP on `specs/10`: accounts, server-resolved battles, cosmetic rewards |
| T13 | Monetization: opt-in ads, ad-removal pass, cosmetics; platform builds |

## 7. Risks

- **The run might not be fun at 12 epochs.** T2-T6 get it in kj's hands as fast as possible; T6 is the real test. Keep T2-T5 thin.
- **Rule-breaking passives vs a data-driven engine.** "Counterattacks deal damage equal to mitigated defense, bypassing range and speed rules" needs code, not a table row. Plan for a small set of coded rule modifiers referenced by id from content, and accept that each new chase trait may need code.
- **Determinism in battles with gambits.** Gambit evaluation must be pure and ordered; it belongs in the battle engine, not the UI.
- **Strangler overhead** (gone): two loops lived in one codebase for T1-T6 and the idle loop was kept frozen; T7 deleted it.

## 8. Not decided by this plan

Balance of any kind, art direction, the game's name, store presence. Those stay with their goal issues.

## 9. Questions for kj

Blocking questions first. Each has a recommendation.

Answered (kj, 2026-10-30): pivot in place, no rename yet; tree slots are 2 native + 2 empty, any new tree may fill or overwrite any slot (`specs/12` section 5.1); web first (section 1); clean save break; stats HP, STR, INT, WIS, DEX, DEF, SPD, LUK; failed runs end on injury, the monster is still vaulted and shards are still paid. Second round: only Drill and Rest, AP cycles ending in a rank fight (`specs/12` section 4.2); points in an overwritten tree are lost and a chase tree whose prerequisite is overwritten locks again; every tree has a chase trait, and chase trees' capstones are much stronger and more unique. Third round: SP per cycle plus a per-win bonus; trees of nodes with about 75% filler; the rank reached is the MVP grade, with the simulated boss-tower gauntlet and wheel spin after the MVP (T8). Fourth round: a Vault entry keeps final stats, slotted trees with bought nodes, rank, species and run date, frozen so it replays; unspent skill points are lost at sealing; the run screen is built ahead of T5 so kj can play it early. Vault management (#82): a cap and dismantling only, placeholder cap 50, refunds by grade, a finished run always seals over the cap and the next run waits until the player dismantles down to it, a lock protects favourites; expansions wait for monetization (`specs/18` section 7).

Open, blocking first. Each has a recommendation.

1. **What overwriting takes away** (`specs/15` section 4, agent decision): the overwritten tree's flat stat boosts are removed too. Not blocking; easy to change.
2. **One energy meter** (`specs/12` section 13 item 16): drafts had energy plus fatigue; T2 uses energy only. Recommend keeping one meter. Not blocking.
3. **Roles and elements.** The T3 placeholder content keeps the 5 elements and has no fixed roles (roles come from trees). Confirm. Not blocking.
4. **Monetization** (`specs/12` section 13 item 10): gem sales or not, and how Skill Books (the premium gate since T9) are sold (#49, #77). Not blocking until T13.
