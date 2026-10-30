# Browser prototype

A playable, phone-sized UI for feel and balance testing, running the **real C# game core** (`src/IdleRng.Sim`) compiled to WebAssembly. The native product is still the Unity client in `specs/07`; this page and Unity drive the same engine.

To play: `./play.sh` from the repo root (details in the root README, "Running the web prototype"). `index.html` is the monster trainer (the game the project is moving to, `specs/13`); `idle.html` is the old idle prototype, reached from the trainer's header and removed at T7.

| File | What it is |
|---|---|
| `idle.html`, `app.js` | Idle prototype UI shell: ticks the engine, sends commands, reads queries, drains events. No game rules. |
| `index.html`, `run.js` | Monster trainer screens: species pick, Drill/Rest, rank fights with the battle log, tree drafts and buying, grade screen with shard payout, talents, Vault list. No game rules. |
| `run-engine.js` | Facade over the C# trainer engine (`IdleRng.Web.Engine.Run*`/`Account*`): the account save (shards, talents, Vault, active run). |
| `engine.js` | Facade over the C# engine (`IdleRng.Web.Engine`): caches state per tick batch, memoises queries, wraps saves. |
| `format.js` | Big-number display (`1.23K`, `45.6M`, `123aa`); same output as the C# `BigNumber.Short`. |
| `sprites.js` | Seeded 12x12 pixel sprites for heroes, player and enemies. Cosmetic only, never touches the game's RNG. |
| `wasm/build.sh` | Builds `src/IdleRng.Web` and lays out `wasm/dist/` (gitignored). |
| `wasm/artifact-files.py` | Prints the `files` map for republishing the phone artifact (root README, "On a phone"). |
| `wasm/pack-content.js` | Bundles `Content/*.json` into the content pack the engine loads and hash-checks. |
| `wasm/BRIDGE.md` | The JS <-> C# contract: exports, JSON shapes, events. |
| `wasm/test/run-wasm.js` | Headless-Chromium end-to-end test of the idle page on the built bundle. |
| `wasm/test/run-trainer.js` | Headless-Chromium test that plays a full monster-trainer run through the buttons. |

## Where things come from

- **Rules and numbers**: the C# core and `Content/*.json` (built from `content_src/` by `tools/content_build.py`). Change a number in `content_src/`, rebuild content and the bundle, reload.
- **Labels and display numbers** (hero names, slot names, pity limits, costs shown on buttons): `Engine.UiContent()`, read from the same `ContentDb`. The UI never evaluates a game formula itself.
- **Saves**: the C# save format (`IdleRng.Runtime` `SaveSerializer`) inside a small JSON envelope, in `localStorage['idle-rng-wasm-save-v1']`. Saves from the old JS engine are rejected with a message.

The cog opens playtest tools: speed x1/x5/x20/x100 (moves the game clock too, so chests, offline time and daily resets come faster), clock skips (+1 h, +8 h, +1 day), gold and gem grants, stage jump, save export/import, and reset.

## Known limits

- **Permanently unranked.** It creates its own random seeds, so it can never be certified for leaderboards or PvP (`specs/10` section 3). That is right for a playtest build; a ranked browser client needs a server genesis.
- **Needs HTTP.** WebAssembly does not load from `file://`. The install-free way to play is the claude.ai artifact link in the root README ("On a phone"), published from `web/index.html` plus the files listed by `wasm/artifact-files.py`.
- The core has the command log, `StateHash` and `Replay` (`specs/03` section 22, `specs/10` section 6), but the bridge does not expose them yet.
