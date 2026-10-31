# Web client

The playable, phone-sized client for the monster trainer. It runs the **real C# game core** (`src/IdleRng.Sim`) compiled to WebAssembly by `src/IdleRng.Web`; the JavaScript here is a UI shell and holds no game rules.

To play: `./play.sh` from the repo root (details in the root README, "Running the web client"), or the phone artifact link in the root README.

| File | What it is |
|---|---|
| `index.html`, `run.js` | The trainer screens: species pick, Drill and Rest with the odds shown, rank fights played back action by action (ability in use, skill cooldowns, live HP; speed 1x/2x/Skip, `FIGHT_STEP_MS` is a placeholder) then the battle log, tree drafts and buying, the gauntlet wheel after sealing (`specs/19` section 7), the grade screen with shard payout and Legacy Title, talents, the Vault list (count against the cap, lock toggle, dismantle with a confirm; a run start is blocked while over the cap). No game rules. |
| `tooltips.js` | Tap-to-open tooltips for stats and abilities (`role="tooltip"`, long press, Escape). Text and numbers come from the engine (`wasm/BRIDGE.md`, "Tooltips"). |
| `run-engine.js` | Facade over the C# trainer bridge (`IdleRng.Web.Engine.Run*` / `Account*`): the account save (shards, talents, Vault, active run). |
| `engine.js` | Loader for the .NET runtime and the content pack. |
| `sprites.js` | Seeded 12x12 pixel sprites drawn on canvas. Cosmetic only, never touches the game's RNG. |
| `wasm/build.sh` | Builds `src/IdleRng.Web` and lays out `wasm/dist/` (gitignored). |
| `wasm/artifact-files.py` | Prints the `files` map for republishing the phone artifact (root README, "On a phone"). |
| `wasm/pack-content.js` | Bundles `Content/*.json` into the content pack the engine loads and hash-checks. |
| `wasm/BRIDGE.md` | The JS <-> C# contract: exports and JSON shapes. |
| `wasm/test/run-trainer.js` | Headless-Chromium test that plays a full run through the buttons (part of `./check.sh`). |

## Where things come from

- **Rules and numbers**: the C# core and `Content/*.json` (built from `content_src/` by `tools/content_build.py`). Change a number in `content_src/`, rebuild content and the bundle, reload.
- **Names and descriptions**: `Content/strings.json` through the bridge. The UI never evaluates a game formula itself: tooltip numbers (crit chance at this LUK, damage taken at this DEF) are computed by the C# battle formulas (`specs/16` section 9).
- **Saves**: the account save (`specs/06`, `specs/18` section 6) as text in `localStorage['idle-rng-account-v1']`. A save that no longer loads is kept under `idle-rng-account-v1-unloadable`, never overwritten.

## Known limits

- **Permanently unranked.** It creates its own random seeds, so it can never be certified for leaderboards or PvP (`specs/10` section 3). That is right for a playtest build; a ranked client needs a server genesis.
- **Needs HTTP.** WebAssembly does not load from `file://`. The install-free way to play is the claude.ai artifact link in the root README ("On a phone"), published from `web/index.html` plus the files listed by `wasm/artifact-files.py`.
