# web/tools

All scripts are plain Node (no dependencies); run from the repo root.

| command | what it does |
|---|---|
| `node web/tools/export-content.js` | writes `Content/content.json` from `web/content.js` |
| `node web/tools/check-content.js` | drift check (file vs. content.js) plus invariant validation; exit 1 on failure |
| `node web/tools/gen-parity.js [outDir]` | runs the scenarios on `web/sim.js`, writes `Fixtures/Parity/*.json` and `format-vectors.json`; deterministic (run twice, bytes identical) |
| `node web/tools/verify-parity.js [dir]` | replays each fixture on the JS core with `Content/content.json` and deep-compares with `expect`; exit 1 on mismatch |

`parity-lib.js` is the shared step executor (record and replay). Schema: `Fixtures/Parity/README.md`, `Content/README.md`.
Regenerate fixtures after any change to `sim.js` or `content.js` (export-content first). `PARITY_BOT_DAYS=14` shortens the bot fixture.
