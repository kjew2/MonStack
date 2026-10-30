# Idle RNG (working title) - prototype specs

Mobile idle game in the Sword x Staff / Maple Idle mould: auto-combat pushes stages, stages drop currency, currency feeds parallel RNG upgrade systems, the player's job is deciding where to spend.

**Pivot planned:** the project is moving to a fast monster-raising game (training runs, skill trees, towers). The target design is `specs/12`, the plan `specs/13`. The specs below still describe the code until each phase lands.

Start with `specs/00-overview.md`. It holds the canonical constants and the contracts every other spec follows.

| Spec | Covers |
|---|---|
| `specs/00-overview.md` | Scope (P0/P1/P2), canonical constants, vocabulary, cross-spec contracts |
| `specs/01-gacha-math.md` | Rates, pity, 10-pull guarantee, dupe economy, RNG commitment, test vectors |
| `specs/02-power-curve-math.md` | Enemy/gold curves, the four power layers, wall condition, bot results, tuning levers |
| `specs/03-sim-core-combat.md` | Pure-C# simulation assembly: tick loop, combat, RNG streams, public API |
| `specs/04-economy-sinks.md` | Currencies, stat levels, gear drops/enhancement, daily sources (class advance replaced by promotion, `specs/11`) |
| `specs/05-content-data-schema.md` | Table schemas, sheet-to-JSON pipeline, validation, `ContentDb` API |
| `specs/06-save-and-offline.md` | Save schema v1, migrations, offline catch-up, clock seam, integrity |
| `specs/07-client-unity-prototype.md` | Unity project layout, screens, view-models, event wiring, placeholder art |
| `specs/08-backend-seams.md` | `IClock` / `IGachaService` / `ISaveStore` contracts, local impls, future remote |
| `specs/09-test-harness-and-bots.md` | Headless bot runner, acceptance table, determinism tests, CI |
| `specs/10-online-competitive.md` | Trust model for accounts, IAP, leaderboards and async PvP: server-verified progress, genesis seeds, command log, fairness levers |
| `specs/11-party-and-builds.md` | Gameplay overhaul (#60): 4-companion party, stat sheet, roles, elements, promotion, 4v1 battle (phases 3-5 cancelled by the pivot) |
| `specs/12-monster-trainer-design.md` | Pivot target (not built): monster raising with 12-epoch training runs, skill trees and chase traits, gauntlet, towers, async PvP |
| `specs/13-pivot-transition.md` | Plan for moving this repo from the idle game to `specs/12`: what carries over, phases, open questions |
| `specs/14-training-run.md` | Training run (pivot T2): AP cycles of Drill and Rest, drill rolls, energy and injury, rank fights, milestones |
| `specs/15-skill-trees.md` | Skill trees (pivot T3): 4 slots, skill points, nodes with ~75% filler, milestone/book/chase offers, overwriting, chase unlock and re-lock |
| `specs/16-battle.md` | Monster battle engine (pivot T1): sides of 1-4, eight-stat sheet, ATB, default AI, triggers, rule modifiers |

Checks: `./check.sh` runs every check in the repo (`--fast` for the inner loop, `--no-dotnet` without the .NET SDK). Agent rules live in `CLAUDE.md`.

Tools:

- `tools/gacha_model.py` - exact pity distribution and consolidated rates. `python3 tools/gacha_model.py`
- `tools/power_curve_model.py` - greedy F2P bot, 30 days, prints the day table. `python3 tools/power_curve_model.py`

Both run on Python 3.7+ with no dependencies.

## Running the web prototype

The web prototype is the real C# game core (`src/IdleRng.Sim`, the code the Unity client and the bots use), compiled to WebAssembly by `src/IdleRng.Web` and played through the browser UI in `web/`. Everything runs on your own computer; no Claude session is needed.

### One-time setup

**1. Check what you already have:**

```
./play.sh --check
```

It lists each dependency as `ok` or `MISSING`, with the install command for your OS next to anything missing. Run it again after installing until it says "All set".

**2. Install anything missing.** You need four things; the fifth is optional:

| Dependency | Why | macOS | Windows (PowerShell) | Ubuntu / Debian |
|---|---|---|---|---|
| Python 3 | serves the page | `brew install python` | `winget install Python.Python.3.12` | `sudo apt install python3` |
| Node.js 18+ | packs the game content | `brew install node` | `winget install OpenJS.NodeJS.LTS` | `sudo apt install nodejs` |
| .NET 8 SDK | builds the C# engine | [installer](https://dotnet.microsoft.com/download/dotnet/8.0) | `winget install Microsoft.DotNet.SDK.8` | `sudo apt install dotnet-sdk-8.0` |
| WebAssembly tools | compile C# for the browser | `sudo dotnet workload install wasm-tools` | `dotnet workload install wasm-tools` (as administrator) | `sudo dotnet workload install wasm-tools` |
| qrencode (optional) | QR code for the phone link | `brew install qrencode` | not needed | `sudo apt install qrencode` |

Install them top to bottom: the WebAssembly tools need the .NET SDK first. On macOS, `brew` is [Homebrew](https://brew.sh); the official installers from python.org and nodejs.org work too. Ubuntu's `nodejs` package must be version 18 or newer (`node --version`); older releases need the installer from nodejs.org. On Windows, run `./play.sh` from Git Bash (comes with [Git for Windows](https://git-scm.com/download/win)) or WSL; open a new terminal after installing so it sees the new tools.

### Play

From the repo root:

```
./play.sh
```

It checks the prerequisites (and tells you the command to fix anything missing), builds the C# engine only when `src/` or `Content/` changed since the last build (the first build takes 1-3 minutes), starts a local server, prints the link for this computer and for your phone, and opens your browser. Stop it with Ctrl+C. Settings in the game (the cog, top right) shows "Engine: C# (wasm)" and the content version.

Options: `--rebuild` forces a build, `--port 8080` picks another port (it moves to the next free one if the port is taken), `--no-open` skips opening a browser, `--local` refuses connections from other devices. If `qrencode` is installed it also prints a QR code for the phone link.

By hand, the same thing is `web/wasm/build.sh` then `cd web && python3 -m http.server 8000`.

It must be served over HTTP: browsers refuse to load WebAssembly from a `file://` page, so opening `index.html` directly shows an error telling you to start the server. On Windows, run `build.sh` from Git Bash or WSL.

If the page says "The C# engine did not load", it names the cause: the bundle is not built (run `./play.sh --rebuild`), the page was opened as a file, or loading failed (rerun the build and read its output; the usual cause is the missing `wasm-tools` workload).

### On a phone

The repo is private, so GitHub cannot serve the page (raw links show source, and GitHub Pages on a private repo needs a paid plan and publishes the site publicly). Serve it from your computer:

1. Run `./play.sh` on the computer. It prints an "On your phone" link such as `http://192.168.1.20:8000`.
2. Open that link on the phone, on the same Wi-Fi. If it does not load, allow Python through the computer's firewall.
3. Optional: Add to Home Screen from the browser's share menu for a full-screen icon.

Progress is saved in the browser, separately for each address. Fonts load from Google Fonts; offline the page falls back to system fonts and still plays.

### Tests for the web build

- `node web/wasm/test/run-wasm.js` (after `web/wasm/build.sh`): plays the page on the C# engine in headless Chromium: ticks, tabs, a summon, save and reload, and the file:// error.
- Game rules are tested in C# (`src/IdleRng.Sim.Tests`); `./check.sh` runs everything, including the web build and browser test in full mode.
