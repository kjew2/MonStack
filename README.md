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

Tools:

- `tools/gacha_model.py` - exact pity distribution and consolidated rates. `python3 tools/gacha_model.py`
- `tools/power_curve_model.py` - greedy F2P bot, 30 days, prints the day table. `python3 tools/power_curve_model.py`

Both run on Python 3.7+ with no dependencies.

Playable prototype:

- `web-proto/index.html` - throwaway single-file browser version of the core loop (combat, boss walls, ATK levels, gear, hero summons with pity, party, class advances, dailies) using the `specs/00` numbers, with playtest speed controls. Open it in any browser. It is for judging feel only: the real rules live in the C# sim, this copy will drift, and it uses unseeded `Math.random` instead of the committed RNG streams.

### Playing the prototype on a phone

The repo is private, so GitHub cannot serve the page directly (raw file links show source text, and GitHub Pages on a private repo needs a paid plan and publishes the site publicly). Use one of these:

1. **Claude artifact link (easiest).** Open https://claude.ai/artifact/SqKqU5vPNmNyw6b5juCijL in the phone's browser while signed in to claude.ai. It is private to the owner. It is a separate copy: changes to `web-proto/index.html` reach it only when Claude republishes it.
2. **From a computer on the same Wi-Fi (always matches the repo).**
   1. On the computer: `cd web-proto && python3 -m http.server 8000`
   2. Find the computer's local IP: macOS `ipconfig getifaddr en0`, Linux `hostname -I`, Windows `ipconfig` (IPv4 Address).
   3. On the phone, open `http://<that-ip>:8000` (for example `http://192.168.1.20:8000`). If it does not load, allow Python through the computer's firewall.
   4. Optional: in the browser's share menu, choose Add to Home Screen for a full-screen icon.
3. **Android only: open the file directly.** Download `web-proto/index.html` to the phone (GitHub app or website, Download raw file), then open it from Files with Chrome. iPhone Safari and Files do not run scripts in local HTML files, so use option 1 or 2 there.

Progress is saved in the browser, separately for each address, so the artifact link and the Wi-Fi address each keep their own save. Pixel fonts load from Google Fonts; offline the page falls back to system fonts and still plays.
