# Web client

The playable, phone-sized client for the monster trainer. It runs the **real C# game core** (`src/IdleRng.Sim`) compiled to WebAssembly by `src/IdleRng.Web`; the JavaScript here is a UI shell and holds no game rules.

To play: `./play.sh` from the repo root (details in the root README, "Running the web client"), or the phone artifact link in the root README.

| File | What it is |
|---|---|
| `index.html`, `run.js` | The trainer screens: species pick (locked species with their unlock condition; `specs/18` section 8), Drill and Rest with the odds shown (one divided row per stat with its Drill button centred, issue #96; each drill pops its "+x" gain styled by the tier: a drooping grey-blue handwritten fail, a green success, a loud bouncing gold super, issue #95), rank fights played back action by action (ability in use, a square tile per skill whose grey overlay snaps full on use and recedes downward as its cooldown runs out (issue #97), live HP; speed 1x/2x/Skip, `FIGHT_STEP_MS` is a placeholder) then the battle log, skill point gains made visible (a float-up and pulse at the HUD SP count, a badge on the fight result, the unspent count on the Trees tab, a log line per gain), tree drafts and buying, the gauntlet wheel after sealing (`specs/19` section 7), the Daily Tower screen with floor playback in the rank fight's view plus entropy gauges, the next vent, a corruption meter and their tooltips, and its team picker (slots in order, the Vault's search, sort and filters; `specs/21` section 8), the debug-only "Test: add a book" button (`?debug` or five taps on the build stamp), the grade screen with shard payout, Legacy Title and achievements earned, the post-run vendor (talent offers with rarities; `specs/18` section 4), talent ranks, the achievement list, and the Vault section's place on the home screen (`vault.js` draws it). No game rules. |
| `vault.js` | The Vault list (`specs/18` sections 7 and 9): count against the cap, lock toggle, dismantle with a confirm (a run start is blocked while over the cap); sort, filters, search, a detail sheet and a side-by-side compare. `VaultView.browse` lends the search, sort and filters to the Daily Tower team picker. Lock and dismantle always use the real Vault index. The sort and filters are remembered in `localStorage['idle-rng-vault-view']`. |
| `tooltips.js` | Tap-to-open tooltips for stats and abilities (`role="tooltip"`, long press, Escape). Text and numbers come from the engine (`wasm/BRIDGE.md`, "Tooltips"). |
| `run-engine.js` | Facade over the C# trainer bridge (`IdleRng.Web.Engine.Run*` / `Account*`): the account save (shards, talents, achievements, vendor offers, Vault, active run). |
| `engine.js` | Loader for the .NET runtime and the content pack. |
| `sprites.js` | Seeded 12x12 pixel sprites drawn on canvas. Cosmetic only, never touches the game's RNG. |
| `wasm/build.sh` | Builds `src/IdleRng.Web` and lays out `wasm/dist/` (gitignored). |
| `wasm/publish-artifact.sh` | Builds and prints the whole Artifact publish call for republishing the phone artifact (root README, "On a phone"). |
| `wasm/artifact-files.py` | Prints the `files` map that call uses; fails if a listed file is missing. |
| `wasm/pack-content.js` | Bundles `Content/*.json` into the content pack the engine loads and hash-checks. |
| `wasm/BRIDGE.md` | The JS <-> C# contract: exports and JSON shapes. |
| `wasm/test/run-trainer.js` | Headless-Chromium test that plays a full run through the buttons and browses a varied Vault (part of `./check.sh`). |
| `wasm/test/flows.js` | Headless-Chromium test of the other flows: no horizontal scroll or console error on each main screen at 390x844, a tree draft overwriting a slot, save/reload/resume with the same run hash, and a stale or damaged save (part of `./check.sh`). |
| `wasm/test/harness.js` | Shared helpers for the browser tests (phone-sized page, save encoding, play loop, layout check). |

## Where things come from

- **Rules and numbers**: the C# core and `Content/*.json` (built from `content_src/` by `tools/content_build.py`). Change a number in `content_src/`, rebuild content and the bundle, reload.
- **Names and descriptions**: `Content/strings.json` through the bridge. The UI never evaluates a game formula itself: tooltip numbers (crit chance at this LUK, damage taken at this DEF) are computed by the C# battle formulas (`specs/16` section 9).
- **Saves**: the account save (`specs/06`, `specs/18` section 6) as text in `localStorage['idle-rng-account-v1']`. A save that no longer loads is kept aside under `idle-rng-account-v1-unloadable-<ms>` (the oldest and the two newest are kept, `specs/06` section 5).

## Known limits

- **Permanently unranked.** It creates its own random seeds, so it can never be certified for leaderboards or PvP (`specs/10` section 3). That is right for a playtest build; a ranked client needs a server genesis.
- **Needs HTTP.** WebAssembly does not load from `file://`. The install-free way to play is the claude.ai artifact link in the root README ("On a phone"), published from `web/index.html` plus the files listed by `wasm/artifact-files.py`.
