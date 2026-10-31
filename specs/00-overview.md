# 00 - Overview and Shared Contracts

Working title: **Idle RNG** (a leftover from the idle game this project started as; the game is now a monster raising game and has no name yet, #47). The target design is `specs/12`; this file is the contract the other specs build on. If another spec disagrees with a number or name here, this file wins.

History: the project began as a mobile idle game (stage push, gear, gacha). It pivoted to the monster trainer on 2026-10-30 and the idle game was removed in `specs/13` phase T7. Git history holds it; the specs that described only it (`02` power curve, `04` economy, `07` Unity client, `11` party and builds) were deleted with it.

## 1. What exists

| Part | Where | Spec |
|---|---|---|
| Monster battle: sides of 1 to 4, eight-stat sheet, ATB, triggers, rule modifiers | `src/IdleRng.Sim/Combat` | `16` |
| Training run: action-point cycles of Drill and Rest, rank fights, milestones | `src/IdleRng.Sim/Training` | `14` |
| Skill trees: 4 slots, skill points, offers, chase trees | `src/IdleRng.Sim/Training` | `15` |
| Account: Vault, Training Shards, talents, run modifiers, abandon | `src/IdleRng.Sim/Account` | `18` |
| End-of-run gauntlet and Legacy Titles | `src/IdleRng.Sim/Gauntlet` | `19` |
| Content database and pipeline | `content_src/` -> `Content/` -> `ContentDb` | `05` |
| Account save lineage and stores | `src/IdleRng.Runtime/Save` | `06`, `18` |
| Bots and golden files | `src/IdleRng.Bots` | `09` |
| Web client on the C# core in WebAssembly | `web/`, `src/IdleRng.Web` | `web/README.md` |

Not built yet (designed in `specs/12`, ordered in `specs/13`): the species gacha (`01` has the math), the Daily and Endless Towers, the gambit rulebook, async PvP and its server (`10`), monetization. `08` sketches the backend seams.

## 1a. Scope

| Tag | Meaning | Content |
|---|---|---|
| **P0** | The MVP, a single-player web demo (`specs/12` section 12.1): built and live | Phases T0 to T7 of `specs/13`: battle, training run, skill trees, Vault, account and talents, web client, idle game removed |
| **P1** | Toward 1.0 on Steam and the web (`specs/12` section 12.2) | T8 gauntlet wheel screen (engine done), T9 species gacha, T10 Daily Tower, T11 Endless Tower and gambits, T12 async PvP and server, T13 monetization and platform builds |
| **P2** | After 1.0, or parked until a platform decision | iOS and a native client, live operations |

The priority labels on GitHub issues use the same tags (`specs/13` section 5).

## 2. Constants

Every number is a row in `content_src/` (`specs/05`), read through `ContentDb`; code never holds a balance literal. All values are **placeholders** until balance resumes (`CLAUDE.md`). The groups and where each is specified:

| Group | Constants | Spec |
|---|---|---|
| Battle | `BATTLE_GAUGE`, `SPD_REF`, `ELEMENT_STRONG_MULT`, `ELEMENT_WEAK_MULT`, `BASIC_ATTACK_POWER`, `AI_HEAL_BELOW`, `AI_SHIELD_BELOW`, `MON_*`, `STAT_FLOOR_FRAC` | `16` section 7 |
| Training run | `RUN_CYCLES`, `RUN_AP`, `RUN_ENERGY_*`, `DRILL_*`, `REST_ENERGY`, `INJURY_*`, `STAT_MAX`, `FIGHT_EVERY` | `14` section 8 |
| Skill trees | `SP_PER_CYCLE`, `SP_PER_WIN`, `TREE_SLOTS`, `DRAFT_SIZE`, `MON_ECHO_POWER` | `15` section 9 |
| Account | `SHARDS_PER_CYCLE`, `SHARDS_PER_RANK` | `18` section 5 |
| Gauntlet | `GAUNTLET_FLOORS`, `GAUNTLET_BASE`, `GAUNTLET_GROWTH`, `GAUNTLET_BOSS_EVERY`, `GAUNTLET_BOSS_MULT` | `19` section 4 |

## 3. Vocabulary

| Term | Meaning |
|---|---|
| **Species** | A kind of monster: element, base stats, growth multipliers, two native trees |
| **Stats** | HP, STR, INT, WIS, DEX, DEF, SPD, LUK on a 0 to `STAT_MAX` scale |
| **Run** | One monster trained for `RUN_CYCLES` cycles |
| **Cycle** | `RUN_AP` taps of Drill or Rest, then a rank fight |
| **Rank** | E to S: how far the monster has climbed in its rank fights; the MVP grade |
| **Tree, node, chase trait** | `specs/15` |
| **Vault** | The account's finished, frozen monsters |
| **Training Shards** | The soft currency, paid at the end of a run; buys talents |
| **Talent** | A permanent account upgrade that changes the next runs (`RunModifiers`) |
| **Gauntlet** | The end-of-run tower the finished monster climbs; where it falls sets its Legacy Title |

## 4. Cross-spec contracts

1. **The sim is pure C#** (`IdleRng.Sim`): no `UnityEngine`, no IO, no JSON library, no reflection, no clock reads, no `Math.Pow/Exp/Log` (`specs/03`). Code that needs IO or a serializer lives in `IdleRng.Runtime`.
2. **Every RNG consumer gets its own named stream** `(seed, counter)`; the counter lives in the state it belongs to (`specs/03` section 5.5).
3. **Content is data**: every number is a table row or constant (`specs/05`).
4. **Replay is exact**: a run, a fight and a gauntlet each rebuild from their inputs to the same hash (`specs/03` section 1). Saves store replay recipes rather than derived state where they can (`specs/18` section 6).
5. **Server-sensitive operations go through an interface** when the backend exists (`specs/08`); competitive surfaces never read a value the client merely claims (`specs/10`).
6. **The golden files are the determinism test** for rules and content: any change to rules, content or hashing regenerates them through the bot CLI (`specs/09` section 3).

## 5. Layout

```
CLAUDE.md, README.md, check.sh, play.sh
specs/                       00 to 19 (the numbers present are the ones that exist)
content_src/*.csv            the content source of truth (specs/05)
Content/*.json               built by tools/content_build.py, committed, checked for drift
golden/                      generated reference outputs (bot CLI only)
tools/content_build.py       CSV -> JSON converter and validator
tools/gacha_model.py         exact pity math for the species gacha (specs/01)
src/IdleRng.Sim/             pure sim: Combat, Training, Account, Gauntlet, Content, Rng, Replay (hashing)
src/IdleRng.Runtime/         save format, stores, account save
src/IdleRng.Bots/            run bots, golden files, the `train` and `golden` verbs
src/IdleRng.Web/             .NET 8 WebAssembly host: the bridge to the browser UI
web/                         the monster trainer UI (index.html) and the wasm build
.github/workflows/check.yml  CI: the full ./check.sh on every PR and push to main (specs/09 section 6)
```

## 6. Decisions log

Decisions made before the pivot that still shape the code:

1. **Curves are evaluated by repeated multiplication**, never `Math.Pow`, so output is byte-identical across platforms.
2. **The sim stays free of IO, clocks and JSON.** `IdleRng.Runtime` holds the serializer and stores.
3. **Online play is built on server verification, not a server sim** (`specs/10`): the engine stays on the device; a server replays command logs to certify, and resolves PvP battles with the same `IdleRng.Sim` source.
4. **Pivot decisions** (kj, 2026-10-30) are recorded where they apply: `specs/12` (design), `specs/13` (plan and answers), `specs/14` to `19` (rules).
