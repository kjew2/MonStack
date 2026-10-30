# Idle RNG (working title) - prototype specs

Mobile idle game in the Sword x Staff / Maple Idle mould: auto-combat pushes stages, stages drop currency, currency feeds parallel RNG upgrade systems, the player's job is deciding where to spend.

Start with `specs/00-overview.md`. It holds the canonical constants and the contracts every other spec follows.

| Spec | Covers |
|---|---|
| `specs/00-overview.md` | Scope (P0/P1/P2), canonical constants, vocabulary, cross-spec contracts |
| `specs/01-gacha-math.md` | Rates, pity, 10-pull guarantee, dupe economy, RNG commitment, test vectors |
| `specs/02-power-curve-math.md` | Enemy/gold curves, the four power layers, wall condition, bot results, tuning levers |
| `specs/03-sim-core-combat.md` | Pure-C# simulation assembly: tick loop, combat, RNG streams, public API |
| `specs/04-economy-sinks.md` | Currencies, stat levels, gear drops/enhancement, class advance, daily sources |
| `specs/05-content-data-schema.md` | Table schemas, sheet-to-JSON pipeline, validation, `ContentDb` API |
| `specs/06-save-and-offline.md` | Save schema v1, migrations, offline catch-up, clock seam, integrity |
| `specs/07-client-unity-prototype.md` | Unity project layout, screens, view-models, event wiring, placeholder art |
| `specs/08-backend-seams.md` | `IClock` / `IGachaService` / `ISaveStore` contracts, local impls, future remote |
| `specs/09-test-harness-and-bots.md` | Headless bot runner, acceptance table, determinism tests, CI |
| `specs/10-online-competitive.md` | Trust model for accounts, IAP, leaderboards and async PvP: server-verified progress, genesis seeds, command log, fairness levers |

Checks: `./check.sh` runs every check in the repo (`--fast` for the inner loop, `--no-dotnet` without the .NET SDK). Agent rules live in `CLAUDE.md`.

Tools:

- `tools/gacha_model.py` - exact pity distribution and consolidated rates. `python3 tools/gacha_model.py`
- `tools/power_curve_model.py` - greedy F2P bot, 30 days, prints the day table. `python3 tools/power_curve_model.py`

Both run on Python 3.7+ with no dependencies.

## Running the web prototype

The web prototype is the real C# game core (`src/IdleRng.Sim`, the code the Unity client and the bots use), compiled to WebAssembly by `src/IdleRng.Web` and played through the browser UI in `web/`. Everything runs on your own computer; no Claude session is needed.

### One-time setup

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), Node 18+ and Python 3.
2. Install the WebAssembly build tools: `dotnet workload install wasm-tools` (may need `sudo` on Linux/macOS).

### Build and play

From the repo root:

```
web/wasm/build.sh                 # first build takes 1-3 minutes; later ones are faster
cd web && python3 -m http.server 8000
```

Open http://localhost:8000 . Settings (the cog, top right) shows "Engine: C# (wasm)" and the content version. Rebuild with `web/wasm/build.sh` after pulling changes to `src/` or `Content/`.

It must be served over HTTP: browsers refuse to load WebAssembly from a `file://` page, so opening `index.html` directly shows an error telling you to start the server. On Windows, run `build.sh` from Git Bash or WSL.

If the page says "The C# engine did not load", it names the cause: the bundle is not built (run `web/wasm/build.sh`), the page was opened as a file, or loading failed (rerun the build and read its output; the usual cause is the missing `wasm-tools` workload).

### On a phone

The repo is private, so GitHub cannot serve the page (raw links show source, and GitHub Pages on a private repo needs a paid plan and publishes the site publicly). Serve it from your computer:

1. Build and start the server as above.
2. Find the computer's local IP: macOS `ipconfig getifaddr en0`, Linux `hostname -I`, Windows `ipconfig` (IPv4 Address).
3. On the phone, on the same Wi-Fi, open `http://<that-ip>:8000` (for example `http://192.168.1.20:8000`). If it does not load, allow Python through the computer's firewall.
4. Optional: Add to Home Screen from the browser's share menu for a full-screen icon.

Progress is saved in the browser, separately for each address. Fonts load from Google Fonts; offline the page falls back to system fonts and still plays.

### Tests for the web build

- `node web/wasm/test/run-wasm.js` (after `web/wasm/build.sh`): plays the page on the C# engine in headless Chromium: ticks, tabs, a summon, save and reload, and the file:// error.
- Game rules are tested in C# (`src/IdleRng.Sim.Tests`); `./check.sh` runs everything, including the web build and browser test in full mode.
