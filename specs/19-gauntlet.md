# 19 - End-of-Run Gauntlet

Status: **live (T8 of `specs/13`)**, post-MVP. `Vault.Seal` runs it for every sealed monster; the web client spins the wheel after sealing (section 7). Code: `src/IdleRng.Sim/Gauntlet/GauntletSim.cs`. Tests: `src/IdleRng.Sim.Tests/GauntletTests.cs`. Golden: `golden/gauntlet-vectors.txt`.

The design is `specs/12` section 6: the finished monster is simulated up a boss tower, a slowing wheel lands on the floor and the enemy that beat it, and the monster gets a Legacy Title (kj, 2026-10-30: the gauntlet and the wheel spin come after the MVP, where the rank is the grade).

> **Numbers and content are placeholders** until balance resumes (`CLAUDE.md`).

## 1. Flow

```
for floor 1..GAUNTLET_FLOORS:
    fight the floor's enemy 1v1 (BattleSim), monster at full HP
    win -> next floor;  loss or timeout -> stop: FloorReached = floor, Killer = that enemy
all floors won -> Cleared, FloorReached = GAUNTLET_FLOORS + 1, no killer
Legacy Title = the last legacy_titles row with min_floor <= FloorReached
```

- **(agent)** The monster starts every floor at full HP: each floor is a fresh fight, so the result is "the first floor this build cannot beat". Carrying HP between floors would make it an endurance test instead; easy to change.
- The monster fights with its full kit (`TrainingRun.MonsterFighter` or `VaultEntry.Fighter`).
- Seeds: floor `f` uses `Derive(StreamSeed(seed, "gauntlet"), f)`. For a finished run the seed is the run seed, so the result is fixed for a monster and replays exactly.

## 2. Floors and enemies

- Floor level: `GAUNTLET_BASE x GAUNTLET_GROWTH^(floor - 1)` by repeated multiplication (no `Math.Pow`), times `GAUNTLET_BOSS_MULT` on boss floors.
- **Every floor is boss level (kj, 2026-10-31, issue #99).** With a boss spike (it was x1.2 plus heavier boss weights), a monster that beat a boss walked through the next nine floors, so about 90% of climbs ended on a floor ending in 0. Now `GAUNTLET_BOSS_MULT` is 1.0, `GAUNTLET_BASE` is the old boss level (60 x 1.2 = 72), and each normal enemy's weights add up to about the boss average (within 5%, tested), with its own stat profile. The curve is smooth; where a monster stalls depends on its level and on which enemy profile beats its build. Boss rows stay as named enemies on boss floors. The `balance` report (`specs/09` section 7) gives the boss-floor stall rate and a histogram of the stall floor mod `GAUNTLET_BOSS_EVERY`.
- Every `GAUNTLET_BOSS_EVERY`-th floor is a boss floor; bosses rotate through the boss rows, other floors through the normal rows, in CSV order.
- An enemy's stats are its row's eight weights x the floor level. Enemies have no skills yet (basic attacks).

Content table `gauntlet_enemies`: `id`, `name_key`, `element`, `boss`, and a weight per trained stat. Placeholders: Slimeling, Imp, Golem, Wisp, Shade; bosses Apex Chimera, Void Hydra, Storm Titan (names from the drafts).

## 3. Legacy Titles

Content table `legacy_titles`: `min_floor` (strictly increasing, first row 1) and `title_key`. The title text may contain `{killer}`, filled with the killer's name by `GauntletSim.TitleText` (empty for a cleared tower). The UI shows it as "Floor N: <title>" (`specs/12` section 6: "Floor 83: Hydra Food"); **(agent)** a cleared tower has no losing floor, so it reads "All 100 floors: Tower Breaker". The bridge builds both strings (`web/wasm/BRIDGE.md`). Placeholder bands: Punching Bag (1), Warm-Up Act (10), Respectable Brawler (25), Professional Punching Bag (42), {killer} Food (60), {killer}'s Nightmare (80), God-Slayer (95), Tower Breaker (101 = cleared).

## 4. Content rules (content `2.7`)

| Constant | Value | Range |
|---|---|---|
| `GAUNTLET_FLOORS` | 100 | `> 0` |
| `GAUNTLET_BASE` | 72 | `> 0` |
| `GAUNTLET_GROWTH` | 1.03 | `>= 1` |
| `GAUNTLET_BOSS_EVERY` | 10 | `> 0` |
| `GAUNTLET_BOSS_MULT` | 1.0 | `> 0` |

`gauntlet_enemies`: at least one normal enemy and one boss (`E044`); element ids (`E023`); weights `>= 0` (`E043`). `legacy_titles`: first `min_floor` 1 (`E045`), strictly increasing (`E032`). Checked in `tools/content_build.py` (`validate_gauntlet`) and `ContentDb.BuildGauntlet`.

## 5. Result

`GauntletSim.Run(content, fighter, seed)` returns `FloorReached`, `Cleared`, `KillerId`, `TitleKey`, the list of floors fought (enemy, boss flag, won, ticks, fight hash) for the wheel animation, and a SHA-256 `Hash` over all of it (`HashVersion` 1).

## 6. Golden

`golden/gauntlet-vectors.txt`: each species after a `focus` bot run at seed 7, sent up the tower with its run seed: floor, cleared, killer, title, hash.

Spread over many seeds: the `balance` report's Gauntlet section (`specs/09` section 7): floor reached, clear rate, boss-floor stall rate and killers. Measured for issue #99 (20 seeds x every species and policy): before, median floor 30 and 92% of stalls on boss floors; after, median 29 and 9% (about one tenth, as a smooth curve should give).

## 7. Web client (the wheel)

After `finishRun` seals the monster, `web/run.js` shows the wheel before the grade screen, from the climb `finishRun` returns (the gauntlet `Vault.Seal` just ran, floor by floor). **It is shown once (kj, 2026-11-01):** the floors are not stored and nothing replays them later; the Vault entry keeps only the result (floor, killer, Legacy Title).

- A slowing reel of the floors fought, highest on top: it starts on floor 1 and eases out (about 1.6 to 5 s, longer for higher climbs) onto the floor that beat the monster, with the enemy's name under the pointer. Floors won are ticked; up to two unfought floors above show "?". A cleared tower lands on a "Tower cleared" row.
- Then the comic defeat: the killer lunges and the monster is launched off screen or turned to ash (picked from the result hash, cosmetic only); a cleared tower gets a victory hop. The Legacy Title shows as "Floor N: <title>", and a button goes on to the grade screen.
- The reel is shared: Dungeon attempts and Endless Tower climbs play out on it too (`specs/21` section 8).
- Tapping the wheel or the Skip button lands it at once. With `prefers-reduced-motion` the wheel opens already landed and the defeat is a still pose.
- The grade screen and every Vault card show "Floor N: <title>"; there is no replay of the wheel. The rank stays the grade (kj, 2026-10-30). Entries sealed before the gauntlet existed have no title and show none.

## 8. Follow-ups

- Done: `Vault.Seal` runs the gauntlet with the run's final fighter and the run seed, and stores `GauntletFloor`, `GauntletCleared`, `GauntletKiller`, `LegacyTitle` (string key) and `GauntletHash` on the `VaultEntry`. The wheel gets the floor list from that same run, returned once with `finishRun` (`FinishResult.Gauntlet`); it is never replayed (kj, 2026-11-01).
- Pay shards by floor instead of by rank once the gauntlet replaces the rank as the grade (kj's call; the rank stays the MVP grade).
- Done: the wheel spin and the comic defeat animation in the web client (section 7).
- Enemy skills and boss phases (they share the Dungeon work, `specs/13` T10; boss skills and phases are designed in `specs/21`).

## 9. Gauntlet boards (kj, 2026-11-03)

kj's answers: one row per monster (a player can hold several), all time, an overall board and one per species, and rows stay when the monster is dismantled.

- **Boards:** `gauntlet` (every species) and `gauntlet-<species>`. A row is one sealed monster: its trainer's display name (or the generated one, `specs/28` section 6.1), species, floor reached and Legacy Title, shown as the Vault card's label ("Floor 42: Professional Punching Bag", "All 100 floors: Tower Breaker"). Ranked by the floor reached (a cleared tower is GAUNTLET_FLOORS + 1, above every floor), **ties to whoever the server first saw with it** (the time of the commit that put the row there), then the row id. Rows carry no player ids.
- **Written only by the server** (`specs/27` section 3.3), as the Endless board: after it accepts a commit holding a `finishRun`, `AccountServer` puts every Vault monster with a gauntlet result on the overall board and its species' board (`GauntletBoard.RecordAsync`, keep-best per row: a monster already there changes nothing). A monster sealed and dismantled inside one commit still gets its row. A monster sealed before the boards existed joins at its trainer's next seal **(agent, 2026-11-03: no backfill job; kj can veto)**. Best effort after the commit lands. A seal also refreshes the shown name.
- **Rows stay** when the monster is dismantled: the board is a record. The page's local board (below) holds the Vault only, so there a dismantled monster is gone.
- **Left out:** an account that used a debug command (`specs/27` section 6.3) leaves every gauntlet board at that commit and is never written again.
- **Store:** `ILeaderboardStore.SubmitRowsAsync` (rows the player names: the row id is `<player>:<entry id>`, the tag keeps species, title key and killer as sealed so a content change never rewrites a row); `RemoveAsync` takes every row the player owns and `EntryAsync` gives their best row. On UGS, `CloudSaveLeaderboardStore` keeps such a row as `[floor, atMs, player, tag]` in its owner's shard (Endless rows keep `[floor, atMs]`).
- **Reads:** `GauntletBoard.ReadAsync(player, species|null, top)`: the best `GAUNTLET_BOARD_TOP` (placeholder 20, at most 100) rows, the reader's best row also when below them, and how many rows. The module's `GauntletBoard` endpoint (`server/ugs/README.md`); the bridge's `gauntletBoard` and `readBoard` (`web/wasm/BRIDGE.md`).
- **The page:** shown in the Rankings tab (`specs/28` section 6.1). Off Pages (the local test server) the board holds this account's Vault only (`local: true`).

Tests: `src/IdleRng.Server.Tests/GauntletBoardTests.cs` (rows, ties, reads, seals write a row per monster, dismantled rows stay, debug accounts leave), `server/ugs/MonStackAccount.Tests` (the Cloud Save store equal to the memory store with many rows per player, the endpoint).
