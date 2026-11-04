# 00 - Overview and Shared Contracts

Name: **MonStack** (kj, 2026-10-31; a store-name and trademark check is still open, #47). Code, assemblies, paths and save keys still say `IdleRng` / `idle-rng`, a leftover from the idle game this project started as, until the one mechanical rename (`specs/20` R0.3). The target design is `specs/12`; this file is the contract the other specs build on. If another spec disagrees with a number or name here, this file wins.

History: the project began as a mobile idle game (stage push, gear, gacha). It pivoted to the monster trainer on 2026-10-30 and the idle game was removed in `specs/13` phase T7. Git history holds it; the specs that described only it (`02` power curve, `04` economy, `07` Unity client, `11` party and builds) were deleted with it.

## 1. What exists

| Part | Where | Spec |
|---|---|---|
| Monster battle: sides of 1 to 4, eight-stat sheet, ATB, triggers, rule modifiers, gambit rulebooks | `src/IdleRng.Sim/Combat` | `16`, `28` |
| Training run: action-point cycles of Drill and Rest, rank fights, milestones | `src/IdleRng.Sim/Training` | `14` |
| Skill trees: 4 slots, skill points, offers, chase trees | `src/IdleRng.Sim/Training` | `15` |
| Account: Vault (cap, lock, dismantle), Training Shards, achievements and species unlocks, the post-run talent vendor, run modifiers, abandon | `src/IdleRng.Sim/Account` | `18` |
| Breeding and I–X lineages for all 40 generic trees | `src/IdleRng.Sim/Account/TreeLineages.cs` | `30` |
| End-of-run gauntlet and Legacy Titles | `src/IdleRng.Sim/Gauntlet` | `19` |
| Dungeon (built as the Daily Tower): daily lineup of 4v1 boss floors, tickets (one free a day, pieces from sealed runs) and rewards (shards, Skill Book drops, the premium gate), a first web screen | `src/IdleRng.Sim/Tower`, `AccountOps.RunTower`, `web/run.js` | `21` |
| Endless Tower: 4v4 climb of a weekly season, squad shapes, elites, stacking mutators (sim only so far) | `src/IdleRng.Sim/Tower/EndlessSim.cs` | `28` |
| Content database and pipeline | `content_src/` -> `Content/` -> `ContentDb` | `05` |
| Account save lineage and stores | `src/IdleRng.Runtime/Save` | `06`, `18` |
| Bots and golden files | `src/IdleRng.Bots` | `09` |
| Web client on the C# core in WebAssembly | `web/`, `src/IdleRng.Web` | `web/README.md` |

Not built yet (designed in `specs/12`, ordered in `specs/13`): the Endless Tower's account, rewards and screens (`28`; its gambit rulebook, mutators and the climb are built in the sim), the rulebook editor, async PvP and its server (`10`), monetization. `08` sketches the backend seams.

## 1a. Scope

| Tag | Meaning | Content |
|---|---|---|
| **P0** | The MVP, a single-player web demo (`specs/12` section 12.1): T0 to T7 built and live, T9 done, T10 to build (T9 and T10 moved up from P1 by kj, 2026-10-31) | Phases T0 to T7 of `specs/13` (battle, training run, skill trees, Vault, account and talents, web client, idle game removed), plus T9 species unlocks by achievement and the post-run vendor (done; the species gacha was scrapped) and T10 Dungeon |
| **P1** | Toward 1.0 on Steam and the web (`specs/12` section 12.2) | T8 gauntlet wheel screen (done), T11 Endless Tower and gambits, T12 async PvP and server, T13 monetization and platform builds |
| **P2** | After 1.0, or parked until a platform decision | iOS and a native client, live operations |

The priority labels on GitHub issues use the same tags (`specs/13` section 5).

## 2. Constants

Every number is a row in `content_src/` (`specs/05`), read through `ContentDb`; code never holds a balance literal. All values are **placeholders** until balance resumes (`CLAUDE.md`). The groups and where each is specified:

| Group | Constants | Spec |
|---|---|---|
| Battle | `BATTLE_GAUGE`, `SPD_REF`, `ELEMENT_STRONG_MULT`, `ELEMENT_WEAK_MULT`, `BASIC_ATTACK_POWER`, `AI_HEAL_BELOW`, `AI_SHIELD_BELOW` (unused), `MON_*`, `STAT_FLOOR_FRAC`, `EFFECT_STACK_CAP` | `16` section 7 |
| Training run | `RUN_CYCLES`, `RUN_AP`, `RUN_ENERGY_*`, `DRILL_*`, `REST_ENERGY`, `INJURY_*` (chance, cycles skipped, energy on return), `STAT_MAX`, `FIGHT_EVERY` | `14` section 8 |
| Skill trees | `SP_PER_CYCLE`, `SP_PER_WIN`, `TREE_SLOTS`, `DRAFT_SIZE`, `TREE_REFUND`, the rolled Awakening tree constants `ROLL_*` (section 4.1), the chase capstone constants `MON_ECHO_POWER`, `MON_TIDE_REGEN`, `MON_TIDE_SHIELD_CAP`, `MON_DRAIN_FRAC`, `MON_FIRE_PIERCE`, `MON_DOUBLE_TURNS` | `15` section 9 |
| Account | `SHARDS_PER_CYCLE`, `SHARDS_PER_RANK` (section 5), `VAULT_CAP` and the per-grade `fight_ranks.dismantle_shards` refund (section 7) and `fight_ranks.ticket_pieces` (`specs/21` section 7), `VENDOR_OFFERS`, `REROLL_BASE`, `REROLL_STEP`, `LEGENDARY_SLOTS` and the `vendor_rarities` table (section 4), the `achievements` table (section 8) | `18` |
| Breeding / lineage | `LINEAGE_*`, `MON_LINEAGE_*`, `MON_FORTUNE_BANK_STEP`, `MON_FORTUNE_BANK_CAP` | `30` sections 2–6 |
| Gauntlet | `GAUNTLET_FLOORS`, `GAUNTLET_BASE`, `GAUNTLET_GROWTH`, `GAUNTLET_BOSS_EVERY`, `GAUNTLET_BOSS_MULT` | `19` section 4 |
| Dungeon | `TOWER_*`, `ENTROPY_*`, `CORRUPT_*` | `21` section 5 |
| Profile and boards (not balance, so in code) | `NAME_MIN` 3 and `NAME_MAX` 16 (`DisplayName`), `ENDLESS_BOARD_TOP` 20 (`EndlessBoard.DefaultTop`, at most 100 a read), `GAUNTLET_BOARD_TOP` 20 (`GauntletBoard.DefaultTop`, at most 100 a read); the server's limits `COMMIT_MAX_COMMANDS`, `COMMIT_MAX_CLIMBS` and `CLOCK_SLACK_SEC` are `ServerConfig` settings | `28` section 6, `27` sections 4 and 6 |

## 3. Vocabulary

| Term | Meaning |
|---|---|
| **Species** | A kind of monster: element, base stats, growth multipliers, two native trees (native-only: never offered by Awakenings), two chase trees |
| **Stats** | HP, STR, INT, WIS, DEX, DEF, SPD, LUK on a 0 to `STAT_MAX` scale |
| **Run** | One monster trained for `RUN_CYCLES` cycles |
| **Cycle** | `RUN_AP` taps of Drill or Rest, then a rank fight |
| **Rank** | E to S: how far the monster has climbed in its rank fights; the MVP grade |
| **Ladder** | A run's rank fight opponents, one per rank, rolled at the start from each rank's pool (`rank_enemies`, `specs/14` section 5); a loss is a rematch |
| **Tree, node, chase trait** | `specs/15` |
| **Lineage rank** | Account-unlocked tree quality captured by newly bred offspring; I–X for the 40 generic trees (`specs/30`), separate from bought nodes and fight rank |
| **Vault** | The account's finished, frozen monsters |
| **Training Shards** | The soft currency, paid at the end of a run; buys talent ranks from the vendor and funds breeding |
| **Talent** | A permanent account upgrade that changes the next runs (`RunModifiers`) |
| **Vendor** | The post-run shop: after each sealed run, `VENDOR_OFFERS` random talent offers whose rarity odds follow the run's grade |
| **Achievement** | An account goal checked when a run is sealed; some unlock a species. A new account owns only the starter species |
| **Skill Book** | An item that opens a book tree in a run; the premium gate, dropped by the Dungeon (T10) |
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
specs/                       00 to 30 (the numbers present are the ones that exist); index in specs/README.md
content_src/*.csv            the content source of truth (specs/05)
Content/*.json               built by tools/content_build.py, committed, checked for drift
golden/                      generated reference outputs (bot CLI only)
tools/content_build.py       CSV -> JSON converter and validator
tools/test_content_build.py  tests for the converter: previous-build rules (E090, E091), rank pool validation
src/IdleRng.Sim/             pure sim: Combat, Training, Account, Gauntlet, Content, Rng, Replay (hashing)
src/IdleRng.Runtime/         save format, stores, account save
src/IdleRng.Bots/            run bots, golden files, the `train` and `golden` verbs
src/IdleRng.Server/          the account server core: open and commit over a store (specs/27)
src/IdleRng.Web/             .NET 8 WebAssembly host: the bridge to the browser UI
server/ugs/                  UGS Cloud Code modules (.NET 10): MonStackAccount, the account server; deploy.sh (specs/27 step 4)
web/                         the monster trainer UI (index.html) and the wasm build
.github/workflows/check.yml  CI: the full ./check.sh on every PR and push to main (specs/09 section 6)
.github/workflows/pages.yml  deploys the Cloud Code module, then the site to GitHub Pages, after a green check on main
```

## 6. Decisions log

Decisions made before the pivot that still shape the code:

1. **Curves are evaluated by repeated multiplication**, never `Math.Pow`, so output is byte-identical across platforms.
2. **The sim stays free of IO, clocks and JSON.** `IdleRng.Runtime` holds the serializer and stores.
3. **Online play is built on server verification, not a server sim** (`specs/10`): the engine stays on the device; a server replays command logs to certify, and resolves PvP battles with the same `IdleRng.Sim` source. For the trainer (kj, 2026-11-01) the server holds the account, partitioned by domain, and accepts the client's commands at checkpoints only when re-applying them gives the same hash; the game needs a connection, and replaying a run locally to keep its best outcome is an accepted, measured risk (`specs/27`).
4. **Pivot decisions** (kj, 2026-10-30) are recorded where they apply: `specs/12` (design), `specs/13` (plan and answers), `specs/14` to `19` and `21` (rules).
