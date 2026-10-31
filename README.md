# MonStack - monster trainer

A fast, systems-driven monster-raising game (the repo and code still use the old name Idle RNG, a leftover from the idle game this repo started as, which was removed on 2026-10-30). You train a monster in a 12-cycle run of Drill and Rest taps, fight rank fights, build four skill trees toward rule-breaking chase traits, then watch it climb the gauntlet tower. Finished monsters go into your Vault; runs pay Training Shards, and after each run a vendor offers random talent upgrades to buy with them. New species unlock from achievements. No story, no cutscenes.

The design is `specs/12`; the plan and what is built is `specs/13`. Start with `specs/00-overview.md`: it lists what exists and the contracts every spec follows.

| Spec | Covers |
|---|---|
| `specs/00-overview.md` | What exists, constant groups, vocabulary, cross-spec contracts, layout |
| `specs/01-gacha-math.md` | Retired: rates, pity and the 10-pull guarantee of the scrapped species gacha (kept as history) |
| `specs/03-sim-core-combat.md` | Sim design rules, numeric model, RNG and the streams in use |
| `specs/05-content-data-schema.md` | Content pipeline, conventions, error codes, `ContentDb` |
| `specs/06-save-and-offline.md` | Save file format, stores, loading, schema versions |
| `specs/08-backend-seams.md` | Backend interfaces for later (design only) |
| `specs/09-test-harness-and-bots.md` | Run bots, the run-balance report, golden files, `check.sh` |
| `specs/10-online-competitive.md` | Trust model for accounts, leaderboards and async PvP (design only) |
| `specs/12-monster-trainer-design.md` | The game design, merged from kj's drafts, with the decisions recorded |
| `specs/13-pivot-transition.md` | The pivot plan: phases T1-T13, what carries over, status, open questions |
| `specs/14-training-run.md` | Training run: AP cycles of Drill and Rest, drill rolls, energy and injury, rank fights, milestones |
| `specs/15-skill-trees.md` | Skill trees: 4 slots, skill points, nodes with ~75% filler, milestone/book/chase offers, overwriting, chase unlock and re-lock |
| `specs/16-battle.md` | Monster battle engine: sides of 1-4, eight-stat sheet, ATB, default AI, triggers, rule modifiers |
| `specs/18-meta-progression.md` | Account: species unlocks by achievement, Vault (cap, lock, dismantle), Training Shards, the post-run talent vendor, run modifiers, abandon, account save |
| `specs/19-gauntlet.md` | End-of-run gauntlet: 100 escalating floors, the floor and killer the wheel lands on, Legacy Titles |
| `specs/21-daily-tower.md` | Daily Tower (T10): daily 4v1 boss floors with a Vault team, entropy gauge, boss phases, environmental clock, shard and Skill Book rewards |
| `specs/20-roadmap.md` | Proposed roadmap: fixes, features and tech by priority, every open issue mapped |

Checks: `./check.sh` runs every check in the repo (`--fast` for the inner loop, `--no-dotnet` without the .NET SDK). CI (`.github/workflows/check.yml`) runs the full `./check.sh` on every pull request and push to `main`. Agent rules live in `CLAUDE.md`.

Tools (Python 3.7+, no dependencies):

- `tools/content_build.py` - builds `Content/` from `content_src/` and validates it (`--check` in `check.sh`).

Server spike: `server/ugs/` holds the UGS Cloud Code module that resolves battles with `IdleRng.Sim` (issue #17, `specs/08` section 10.1). It needs credentials and the .NET 10 SDK, so it is not part of `check.sh`; see `server/ugs/README.md`.

## Running the web client

The web client is the real C# game core (`src/IdleRng.Sim`, the code the bots use), compiled to WebAssembly by `src/IdleRng.Web` and played through the browser UI in `web/`. Everything runs on your own computer; no Claude session is needed.

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

It checks the prerequisites (and tells you the command to fix anything missing), builds the C# engine only when `src/` or `Content/` changed since the last build (the first build takes 1-3 minutes), starts a local server, prints the link for this computer and for your phone, and opens your browser. Stop it with Ctrl+C. 

Options: `--rebuild` forces a build, `--port 8080` picks another port (it moves to the next free one if the port is taken), `--no-open` skips opening a browser, `--local` refuses connections from other devices. If `qrencode` is installed it also prints a QR code for the phone link.

By hand, the same thing is `web/wasm/build.sh` then `cd web && python3 -m http.server 8000`.

It must be served over HTTP: browsers refuse to load WebAssembly from a `file://` page, so opening `index.html` directly shows an error telling you to start the server. On Windows, run `build.sh` from Git Bash or WSL.

If the page says "The C# engine did not load", it names the cause: the bundle is not built (run `./play.sh --rebuild`), the page was opened as a file, or loading failed (rerun the build and read its output; the usual cause is the missing `wasm-tools` workload).

### On a phone

The repo is private, so GitHub cannot serve the page (raw links show source, and GitHub Pages on a private repo needs a paid plan and publishes the site publicly). Two ways in:

**Claude artifact link (no computer needed).** Open https://claude.ai/artifact/LpdHtJ53ujKkXPizqPkkkg in the phone's browser while signed in to claude.ai. It opens the monster trainer. It is private to the owner and runs the same C# engine bundle as `./play.sh`. It is a copy: it changes only when Claude republishes it (`web/wasm/publish-artifact.sh` builds and prints the Artifact publish call: `web/index.html`, root = repo root, and the file map from `web/wasm/artifact-files.py`). The page header shows the commit and date it was built from (`wasm/dist/build-info.json`), so you can tell whether it is current; "dev" means no build stamp.

**From your computer, on the same Wi-Fi (always matches your checkout):**

1. Run `./play.sh` on the computer. It prints an "On your phone" link such as `http://192.168.1.20:8000`.
2. Open that link on the phone, on the same Wi-Fi. If it does not load, allow Python through the computer's firewall.
3. Optional: Add to Home Screen from the browser's share menu for a full-screen icon.

Progress is saved in the browser, separately for each address, so the artifact link and the Wi-Fi address each keep their own save. Fonts load from Google Fonts; offline the page falls back to system fonts and still plays.

### Tests for the web build

- `node web/wasm/test/run-trainer.js`: plays a whole monster-trainer run on `index.html` through the real buttons (drill, rest, fights, drafts, buying), seals it, checks the grade and Vault, reloads mid-run and after the end, and locks, dismantles and loads an over-cap v1 save.
- `node web/wasm/test/flows.js`: checks each main screen at 390x844 for horizontal scroll and console errors, overwrites a tree slot from a draft, reloads mid-run (also with a draft waiting) and resumes to the end with the same run hash, and loads a stale and a damaged save (notice, kept-aside backup, working account).
- Game rules are tested in C# (`src/IdleRng.Sim.Tests`); `./check.sh` runs everything, including the web build and the browser tests in full mode.
