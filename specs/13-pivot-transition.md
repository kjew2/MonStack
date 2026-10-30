# 13 - Transition Plan: Idle RNG to Monster Trainer

Status: **plan, not started.** How the repo moves from the idle stage-push game (`specs/00`-`11`) to the monster-raising game in `specs/12`. Section 9 lists the decisions kj needs to make before the phases they block.

## 1. Approach

**Pivot in place, keep the build green, cut over at the end** (proposed).

- Same repo. The valuable parts here are infrastructure, not the idle loop: the deterministic pure-C# core, named RNG streams, command log and state hash, content pipeline, save system, bot harness, WebAssembly prototype, gacha math and the 4v1 ATB battle. A new repo would mean rebuilding or copying all of it.
- New systems are built beside the idle loop, in new folders, with their own tests. The idle game keeps building and passing `./check.sh` until the web prototype switches to the new loop (phase T6). Then the idle systems are deleted in one change (T7).
- **No save migration from the idle game.** There are no released players (TestFlight, #11, is not done). The new game starts a new save lineage under a new save key; old idle saves are ignored, not migrated (proposed, section 9 item 4).
- **No rename yet.** `IdleRng` stays in namespaces, assemblies and paths until the game has a name (#47). Then one mechanical rename. Renaming twice is churn.
- **Balance stays on hold.** Every number in the new systems is a placeholder (`CLAUDE.md`). The idle balance criteria AC1-AC5 retire with the idle loop.
- **Web first.** The web prototype is the play surface for the MVP. The Unity client is paused until the run loop feels right (section 9 item 3).

## 2. What carries over

| Area | Today | In the new game | Action |
|---|---|---|---|
| Pure sim rules (`specs/00` section 5.1) | `IdleRng.Sim`: no IO, clock, JSON, reflection or `Math.Pow` | Same rules for the run engine, battles and gauntlet | Keep |
| RNG streams | `Rng/SplitMix64.cs`, named `(seed, counter)` streams | New streams: `training`, `draft`, `gauntlet`, `tower`; `battle` kept; `gacha_hero` becomes the species banner | Keep, add streams |
| Command log, replay, `StateHash` | `Replay/`, `Simulation.Replay.cs` | Every run decision is a command, so a run replays exactly; PvP and certification rely on it | Keep |
| Battle engine | `Battle/BattleEngine.cs`: 4v1 ATB, skill vocabulary, triggers | One engine for 1v1 (gauntlet, in-run fights), 4v1 (Daily Tower) and 4v4 (Endless, PvP). Becomes the `BattleSim.Resolve` of `specs/10` section 9.2 | Adapt: N-vs-M sides, monster stat sheet, rule-modifier hooks |
| Skill vocabulary and triggers | `skills.csv` kinds/targets, `BattleTrigger` | Tree nodes, chase traits and rule-breaking passives are written in it; the trigger list grows | Adapt |
| Elements | `elements.csv`, strong/weak wheel | Species and skill types | Keep (section 9 item 9) |
| Gacha math | `Gacha/Gacha.cs`, `specs/01`, pity, 10-pull guarantee, vectors | Species banner; dupes feed aptitude ranks instead of levels | Keep math, change rewards |
| Content pipeline | `content_src/*.csv` -> `tools/content_build.py` -> `Content/*.json` -> `ContentDb` | New tables: species, stats, train actions, milestones, trees, nodes, chase conditions, books, talents, ladder bosses, floors | Keep pipeline, new tables |
| Save system | `IdleRng.Runtime/Save`: serializer, CRC, self-check, migrations, stores | New `AccountState` + `RunState` schema, new lineage | Keep machinery, new schema |
| Backend seams | `IClock`, `IGachaService`, `ISaveStore`, `IdleRng.Remote` | Same seams; PvP adds `IPvpService` | Keep |
| Online trust model | `specs/10` | Fits better than before: async 4v4 PvP on stored teams, server-resolved battles | Keep, rewrite the snapshot types |
| Bot harness | `IdleRng.Bots`: runner, policies, golden files, report writer | Bots play runs (random, greedy-stat, point-hoarder) and report run scores and gauntlet floors; goldens cover runs and battles | Adapt |
| `check.sh`, golden workflow, bench | | Same gates; the bench measures battle and run throughput | Keep |
| Web prototype | `web/` + `src/IdleRng.Web` WebAssembly bridge | Run screen, drafts, tree view, battle log, ladder/gauntlet result | Keep bridge, new UI |
| Unity client | `unity/IdleRngClient` | Later, if mobile stays a target | Pause |

## 3. What retires

Deleted in T7 once the new loop is playable:

- Stage push, kill counter, spawn loop, farming DPS (`specs/03` section 7, `specs/11` section 7).
- Gold, stat levels (`specs/04` section 4), gear drops and enhancement (`specs/04` section 5), promotion (`specs/11` section 6).
- Offline catch-up, idle chest, offline gold (`specs/06` section 9, `specs/04`).
- Daily quests as written (the Daily Tower replaces them; section 9 item 10).
- Stage-target acceptance AC1-AC5 and `progress_targets`, the power-curve model (`tools/power_curve_model.py`, #66), `specs/02`.
- Idle save migrations v1 -> v2.

## 4. Spec changes

| Spec | Fate |
|---|---|
| `00-overview` | Rewritten at T7 for the new game: scope, constants, vocabulary. Contracts in section 5 mostly survive. |
| `01-gacha-math` | Kept; section 7 (hero dupes) becomes species dupes and aptitude ranks. |
| `02-power-curve-math` | Retired. A run-balance model replaces it when balance resumes. |
| `03-sim-core-combat` | Rewritten: run engine instead of the tick loop; RNG, numeric model, determinism and command sections kept. |
| `04-economy-sinks` | Rewritten: shards, gems, books, talent tree. |
| `05-content-data-schema` | Pipeline sections kept; table sections replaced. |
| `06-save-and-offline` | Save machinery kept; schema replaced; offline catch-up removed. |
| `07-client-unity-prototype` | Marked paused; a web client spec replaces it for the MVP. |
| `08-backend-seams` | Kept; `IPvpService` added at T11. |
| `09-test-harness-and-bots` | Rewritten around run policies and run reports. |
| `10-online-competitive` | Kept; snapshot types updated for Vault teams. |
| `11-party-and-builds` | Phases 3-5 cancelled. The battle, kit vocabulary and elements carry into the new battle spec; the rest retires. |
| `12-monster-trainer-design` | The target design. Each built system moves out of it into its own rules spec. |

New rules specs get written as phases start, one per system: `14-training-run`, `15-skill-trees`, `16-battle` (generalized), `17-gauntlet-and-vault`, `18-meta-progression`. Each says what is live, like `specs/11` does.

## 5. Issue triage (proposed, applied once kj confirms)

| Issue | Proposed |
|---|---|
| #60 overhaul goal | Close as superseded by the pivot; open a new pivot goal issue. |
| #55, #57 affix gear, slot enhancement | Close: no gear in the new design. |
| #56 daily dungeons, #64 uniques | Close; the Daily Tower and Skill Books replace them. |
| #58 stat upgrade overhaul | Close; training replaces gold stat levels. |
| #59 multiplying stats | Fold into the training-run and battle specs. |
| #63 companion stats, roles | Close; species and trees replace it. |
| #41 AC4 walls, #66 power-curve model | Close: they measure the idle loop. |
| #6, #10, #11 iOS / Unity | Park until platform is decided (section 9 item 3). |
| #15, #17-#19, #21-#29, #32, #33 server and trust | Keep; re-scope when PvP is planned (T11). Most are game-agnostic. |
| #38 CI workflow | Keep. |
| #45-#54 launch goals | Keep; revisit names and monetization (#47, #49) against `specs/12`. |

## 6. Phases

MVP is T0-T7 (matches `specs/12` section 12.1). Each phase merges on its own with `./check.sh` green.

### T0 - Decisions and freeze

- kj answers the blocking questions in section 9.
- Stop work on idle systems (#55-#59, #64). Done in this change via `CLAUDE.md`.
- Apply the issue triage (section 5) and open a pivot goal issue with one sub-issue per phase.

### T1 - Generalized battle core

- `BattleEngine` takes two sides of 1-4 units each instead of party-vs-boss. The idle boss fight keeps working as a 4v1 call (golden files may move; regenerate).
- A `MonsterUnit` stat sheet: STR, INT, WIS, DEX, DEF, SPD (+ HP, section 9 item 5). Map to the current HP/ATK/DEF/SPD/crit with placeholder formulas.
- Pure `BattleSim.Resolve(sideA, sideB, rules, seed)` with its own stream, usable by the run engine, gauntlet and later PvP.
- Hooks for rule modifiers (chase traits) and for the entropy gauge, phase shifts and environmental clock (built later, T9).
- Exit: 1v1, 4v1 and 4v4 battles run deterministically, with tests and goldens.

### T2 - Training run engine

- `RunState` and a `Run` command set: `Train(action, stat)`, `Rest`, `Challenge`, `PickDraft(i)`, `SpendPoint(node)`, `Retire`. Run decisions are turn-based, not ticked.
- 12 epochs, energy, fatigue, injury risk, stat gains, skill points, milestone detection at 300/500/700/900. Streams `training` and `draft`.
- Content: `species`, `stats`, `train_actions`, `milestones`, run constants.
- Exit: a bot can play a full run headless; replaying its command log gives the same `StateHash`.

### T3 - Skill trees

- Content: `trees`, `tree_nodes`, `chase_conditions`, `skill_books`. Nodes grant skills, stats or rule modifiers in the T1 vocabulary.
- Native trees, milestone drafts into the draft slot, chase traits at each capstone, in-run chase trees, playstyle counters from Tactical Challenge fights. Book-gated trees use a debug grant until T9 produces books.
- MVP content: 3 species, 6 native trees, 2 chase trees (`specs/12` section 12.1).
- Exit: the point-dump path (`specs/12` section 5.4) is reachable in a scripted test.

### T4 - Run end: ladder, grade, Vault

- MVP 3-boss ladder and S/A/B grade with stat breakdown. The 100-floor gauntlet and Legacy Titles follow in the same spec once the ladder works (the wheel and defeat animation are web UI).
- Vault: certified monsters stored with their final build snapshot.
- Exit: a finished run produces a grade and a Vault entry.

### T5 - Account and meta-progression

- `AccountState`: owned species, Vault, shards, gems, books, talents. New save lineage and save key.
- Talent tree content and effects (energy cost, stat floors, draft slots).
- Exit: run -> shards -> talent -> next run is measurably different, saved and reloaded.

### T6 - Web prototype on the new loop

- `src/IdleRng.Web` bridge exposes the run engine; `web/` gets the run screen, drafts, tree view, Tactical Challenge battle log, ladder/grade screen, Vault, talent tree.
- The idle UI is removed from `web/` here.
- Exit: kj can play full runs on a phone browser via `./play.sh`.

### T7 - Remove the idle game

- Delete the systems in section 3, their content tables, tests, bots, acceptance targets and goldens. Regenerate goldens.
- Rewrite `specs/00`, `03`, `04`, `05`, `06`, `09` for the new game. Update `README.md` and `CLAUDE.md`.
- Bot harness reports run outcomes instead of day tables.
- Exit: the repo contains one game. MVP done.

### Post-MVP (toward 1.0)

| Phase | Content |
|---|---|
| T8 | Species gacha UI and dupes -> aptitude ranks (reuses `Gacha.cs`, pity, vectors) |
| T9 | Daily Tower: 4v1 floors, boss phases, entropy gauge, environmental clock, Skill Book drops |
| T10 | Endless Tower: 4v4, mutators, gambit rulebook editor |
| T11 | Async 4v4 PvP on `specs/10`: accounts, server-resolved battles, cosmetic rewards |
| T12 | Monetization: opt-in ads, ad-removal pass, cosmetics; platform builds |

## 7. Risks

- **The run might not be fun at 12 epochs.** T2-T6 get it in kj's hands as fast as possible; T6 is the real test. Keep T2-T5 thin.
- **Rule-breaking passives vs a data-driven engine.** "Counterattacks deal damage equal to mitigated defense, bypassing range and speed rules" needs code, not a table row. Plan for a small set of coded rule modifiers referenced by id from content, and accept that each new chase trait may need code.
- **Determinism in battles with gambits.** Gambit evaluation must be pure and ordered; it belongs in the battle engine, not the UI.
- **Strangler overhead.** Two loops in one codebase for T1-T6. Keep the idle loop frozen (no fixes beyond keeping it green) so the overhead stays small.

## 8. Not decided by this plan

Balance of any kind, art direction, the game's name, store presence. Those stay with their goal issues.

## 9. Questions for kj

Blocking questions first. Each has a recommendation.

1. **Confirm the pivot approach** (in place, strangler, no rename yet; section 1). Blocks T1.
2. **Tree slot layout** (`specs/12` section 13 item 2). Recommend 2 native + 1 draft + 1 chase, chase trait at every capstone. Blocks T3.
3. **Platform.** Drafts say Steam and web; the repo targets iOS. Recommend web-first for the MVP and pausing Unity/iOS (#6, #10, #11). Blocks T6 (the web part is safe either way).
4. **Clean save break**, no idle -> monster migration. Recommend yes. Blocks T5.
5. **Stat list.** Recommend STR, INT, WIS, DEX, DEF, SPD + HP. Blocks T1/T2.
6. **Epoch actions.** Heavy Drill / Rest / Tactical Challenge as the starting set, one drill per stat? Blocks T2.
7. **Failed runs** (`specs/12` section 13 item 9). Recommend: injury ends the run early, monster still vaulted, shards still paid. Blocks T2/T4.
8. **Apply the issue triage** in section 5 (closing #55-#59, #63, #64, #41, #66, superseding #60). Not blocking.
9. **Roles and elements.** Recommend keeping 5 elements, dropping fixed roles. Not blocking until T3 content.
10. **Monetization** (`specs/12` section 13 item 10): gem sales or not. Not blocking until T12.
