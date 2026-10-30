## Notes: classification of the divergences

Raw C# vs recorded sim.js is about 65% exact, because the core differs from sim.js on purpose or in small ways. With the known
differences patched into an in-memory copy of sim.js (`aligned-js.js`, nothing on disk changes) the exact-match rate is 94%, and every
remaining difference falls into one of the causes below, except the one item under "sim.js bug".

### Intentional or tuning differences (C# is the source of truth)
- `BASE_ATK` 20 (constants.json) vs 10 (content.js): DPS, hits to kill, kill times, boss clearability, therefore nearly every later number.
- Boss rule `intro` (boss_rules.json): boss HP x4 below stage 60, x10 from 60. sim.js uses x10 everywhere.
- Gear drops auto-sell: a replaced piece or an unequipped drop is sold for `GEAR_SELL_KILLS * Gold(tier) * rarityMult`; sim.js keeps nothing and sells nothing. Adapter adds `soldFor` to the `gearDropped` event.
- `AutoEquipBest` (default on): the core re-picks the party after every pull; `equipHero`/`unequipHero` turn it off, `autoEquip` turns it on and fills the party positionally (sim.js keeps heroes that stay in the best six in their slot and never auto-equips).
- Wall clock is whole seconds (`SyncWallClock(long sec)`, offline credit from integer seconds, chest timer in seconds). The adapter floors `nowMs`. sim.js is millisecond precise.
- Quest labels (`Recruit 3 heroes` vs `Summon 3 heroes`) and hero display names (`Ash` vs `Pell the Farmhand`) come from different text sources. The UI takes names from `engine.heroById` (JS content), so only the `name` field in pull results and the quest `label` differ.
- Hero ids: the core uses `hero_c_01`; the adapter maps to `C01` (the ids the UI and sprites use).
- Last-bit floating point: the core evaluates curves as `Base * (growth^k by repeated multiplication from 1)`, sim.js as `Base*g*g*...`; the core's `BaseLplus` is `0.025 + 0.005` (0.030000000000000002), sim.js has the literal `0.03`. Both change a few values by one ulp (`hp`, `gold`, `statCost`, `pityRate`, everything derived). The aligned reference reproduces both.
- `profileId`: the core stores a Guid; a non-Guid id from JS is hashed into one. `contentVersion` is `1.0` vs `0.1.0-web`.

### Smaller core-vs-sim.js behaviours seen in the residue
- `state.combat.enemyHp` is 0 after a kill or `debugSetStage`; sim.js leaves the negative overkill (or the previous enemy's HP after `debugSetStage`).
- `debugSetStage` to a stage below a class threshold lowers `classAdvances` in C# (recomputed from the stage); sim.js never lowers it. Affects DPS afterwards (class-advance fixture, steps 28 onwards).
- `classMult(k)` for k above the number of defined advances (4) clamps in C#; sim.js keeps multiplying (the fixtures probe `classMult(5)`).
- `hitsToKill` / `killTimeSeconds` saturate at `int.MaxValue` hits for absurd arguments (1e30); sim.js returns the huge float.
- Event order within a tick: the core publishes `FirstClear` before `BossCleared`; the adapter swaps them back to sim.js order (`bossCleared`, `firstClear`). No other ordering difference was found.

### sim.js bug (not an adapter or C# issue)
- `gearDropped.piece` in sim.js is the very object stored in `state.gear[slot]`. When the piece is enhanced before the event is drained, the drained event shows the later `enh` (4 occurrences in `bot-30-day`, e.g. enh 9 where the piece dropped at +0). C# pieces are immutable records, so the event keeps the value at drop time. Harmless for the UI; fix in sim.js by copying the piece into the event (`Object.assign({}, piece)`).

### Not covered by the fixtures
`lplus` (life stat) is counted by the adapter during the session only; the core does not store it, so it restarts from 0 after a reload.
