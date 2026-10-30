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

Everything below runs on your own computer. No Claude session is needed; the only thing tied to Claude is the artifact link in option A.

The page can play on two engines that run the same game:

- **C# engine**: the real `IdleRng.Sim` (the code the Unity client and the bots use), compiled to WebAssembly by `src/IdleRng.Web`. Needs a one-time build.
- **JS engine**: `web/sim.js`, a hand-written copy used as a fallback and for quick tests. No build. Its tuning differs slightly from the C# core (see `web/wasm/BRIDGE.md`).

Settings (the cog, top right) shows which engine is running, and has a button to switch. Each engine keeps its own save.

### A. Quickest: JS engine, nothing to install

- Open https://claude.ai/artifact/7mExS6YSTJpZMzFRbWDWHX while signed in to claude.ai (private to the owner; a copy that changes only when Claude republishes it), or
- open `web/index.html` straight from a clone in a desktop browser.

### B. Play on the C# engine on your computer

One-time setup:

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), Node 18+ and Python 3.
2. Install the WebAssembly build tools: `dotnet workload install wasm-tools` (may need `sudo` on Linux/macOS).

Then, from the repo root:

```
web/wasm/build.sh                 # first build takes 1-3 minutes; later ones are faster
cd web && python3 -m http.server 8000
```

Open http://localhost:8000 . With the bundle built, the page picks the C# engine by itself; Settings should say "Engine: C# (wasm)". Add `?engine=js` or `?engine=wasm` to force one. Rebuild with `web/wasm/build.sh` after pulling changes to `src/` or `Content/`.

It must be served over HTTP: browsers refuse to load WebAssembly from a `file://` page, so opening `index.html` directly always gives the JS engine. On Windows, run `build.sh` from Git Bash or WSL.

If you see "C# engine unavailable, using JS engine": the bundle is missing (run the build), the page was opened as a file, or the build failed (rerun it and read the error; the usual cause is the missing `wasm-tools` workload).

### C. On a phone

The repo is private, so GitHub cannot serve the page (raw links show source, and GitHub Pages on a private repo needs a paid plan and publishes the site publicly).

1. **Over Wi-Fi from your computer** (C# or JS engine, always matches your clone):
   1. Do option B (or skip the build for the JS engine), and start the server with `cd web && python3 -m http.server 8000`.
   2. Find the computer's local IP: macOS `ipconfig getifaddr en0`, Linux `hostname -I`, Windows `ipconfig` (IPv4 Address).
   3. On the phone, on the same Wi-Fi, open `http://<that-ip>:8000` (for example `http://192.168.1.20:8000`). If it does not load, allow Python through the computer's firewall.
   4. Optional: Add to Home Screen from the browser's share menu for a full-screen icon.
2. **The artifact link** from option A (JS engine only).
3. **Android only, one file:** `node web/build.js` makes `web/dist/idle-rng.html`; copy it to the phone and open it from Files with Chrome (JS engine only). iPhone Safari does not run scripts in local HTML files.

Progress is saved in the browser, separately for each address and engine. Fonts load from Google Fonts; offline the page falls back to system fonts and still plays.

### Tests for the web build

- `node web/test.js`, `node web/bot.js 30 7`: JS engine spec vectors and 30-day bot.
- `node web/tools/engine-test.js`: the engine switch layer.
- `node web/wasm/test/run-wasm.js` (after `web/wasm/build.sh`): plays the page on the C# engine in headless Chromium.
- `./check.sh` runs all of these plus the .NET checks.
