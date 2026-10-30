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

Browser prototype:

- `web/` - playable phone-browser build of the P0 loop for feel and balance testing. Its game core (`web/sim.js`) mirrors the planned C# sim: seeded RNG streams, no clock reads, every number from `web/content.js`. It passes the spec test vectors (`node web/test.js`) and includes a 30-day bot (`node web/bot.js`). See `web/README.md`. The real product is still the Unity client in `specs/07`.

### Playing the prototype on a phone

The repo is private, so GitHub cannot serve the page directly (raw file links show source text, and GitHub Pages on a private repo needs a paid plan and publishes the site publicly). Use one of these:

1. **Claude artifact link (easiest).** Open https://claude.ai/artifact/7mExS6YSTJpZMzFRbWDWHX in the phone's browser while signed in to claude.ai. It is private to the owner. It is a separate copy: changes in `web/` reach it only when Claude republishes it.
2. **From a computer on the same Wi-Fi (always matches the repo).**
   1. On the computer: `cd web && python3 -m http.server 8000`
   2. Find the computer's local IP: macOS `ipconfig getifaddr en0`, Linux `hostname -I`, Windows `ipconfig` (IPv4 Address).
   3. On the phone, open `http://<that-ip>:8000` (for example `http://192.168.1.20:8000`). If it does not load, allow Python through the computer's firewall.
   4. Optional: in the browser's share menu, choose Add to Home Screen for a full-screen icon.
3. **Android only: open one file directly.** Run `node web/build.js` to make the single-file build `web/dist/idle-rng.html`, copy it to the phone, then open it from Files with Chrome. iPhone Safari and Files do not run scripts in local HTML files, so use option 1 or 2 there.

Progress is saved in the browser, separately for each address, so the artifact link and the Wi-Fi address each keep their own save. Fonts load from Google Fonts; offline the page falls back to system fonts and still plays.
