# Browser prototype

A lightweight, playable build of the P0 loop for feel and balance testing on a phone. It is a throwaway client, not the product: the native app is still the Unity client in `specs/07`. What is meant to carry over is the game core.

| File | What it is |
|---|---|
| `content.js` | Every balance number from `specs/00` section 2. Change numbers here. |
| `sim.js` | Pure game core, a mirror of the planned C# `IdleRng.Sim`. No DOM, no clock reads, no `Math.random`, no `Math.pow`. |
| `app.js`, `index.html` | UI shell: ticks the core, sends commands, draws state. No game rules. |
| `test.js` | Checks the core against the specs' published vectors (SplitMix64, FNV, pity table, consolidated rates, 10-pull guarantee, dupe levels, offline cap). |
| `bot.js` | Greedy F2P bot (`specs/04` section 9) playing 30 days on the real core in under a second. |
| `sprites.js` | Seeded 12x12 pixel sprites for heroes, player and enemies (ported from the earlier `web-proto` build). Cosmetic only. |
| `build.js` | Bundles everything into one file, `dist/idle-rng.html`. |

## Run it

```
node web/test.js          # spec vectors
node web/bot.js 30 7      # days, seed: day table against the specs/00 targets
node web/build.js         # one-file build in web/dist/idle-rng.html
```

To play, open `web/index.html` (or the built file) in a browser. No server or install needed. Progress saves to the browser's local storage.

The cog opens playtest tools: speed x1/x5/x20/x100 (moves the game clock too, so chests, offline time and daily resets come faster), clock skips (+1 h, +8 h, +1 day), gold and gem grants, stage jump, save export/import, and reset.

## Porting to the Unity build

Keep these true and the C# port is a translation:

1. Game rules live only in `sim.js`. The UI calls commands and reads queries.
2. Numbers come from `content.js`, whose keys are the `specs/00` constant names. These become the JSON content tables of `specs/05`.
3. Randomness goes through the named streams, three draws per pull, one per kill, two per drop, one per enhance (`specs/03` section 5.5). `sim.validate()` checks the counters.
4. `test.js` vectors are the same ones `IdleRng.Sim.Tests` must pass, so both builds can be checked against each other. A fixed seed plus the bot's command sequence is a ready-made golden run.

## Known differences from the specs

- Replaced gear is discarded, not sold (`specs/03` section 8.2; `specs/00` mentions a sell value).
- No P1 systems (substats, shard shop, pets, dungeons).
- The bot plays one 2-hour session per day, as `specs/09` section 4 does, so it collects one idle chest per day. Gem income lands at about 160 to 290 per day against the 300 target, the gap `specs/04` section 7.4 and section 14 already describe. Stage targets still land within 20%.
