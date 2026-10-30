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
- Every `GAUNTLET_BOSS_EVERY`-th floor is a boss floor; bosses rotate through the boss rows, other floors through the normal rows, in CSV order.
- An enemy's stats are its row's eight weights x the floor level. Enemies have no skills yet (basic attacks).

Content table `gauntlet_enemies`: `id`, `name_key`, `element`, `boss`, and a weight per trained stat. Placeholders: Slimeling, Imp, Golem, Wisp, Shade; bosses Apex Chimera, Void Hydra, Storm Titan (names from the drafts).

## 3. Legacy Titles

Content table `legacy_titles`: `min_floor` (strictly increasing, first row 1) and `title_key`. The title text may contain `{killer}`, filled with the killer's name by `GauntletSim.TitleText` (empty for a cleared tower). The UI shows it as "Floor N: <title>" (`specs/12` section 6: "Floor 83: Hydra Food"); **(agent)** a cleared tower has no losing floor, so it reads "All 100 floors: Tower Breaker". The bridge builds both strings (`web/wasm/BRIDGE.md`). Placeholder bands: Punching Bag (1), Warm-Up Act (10), Respectable Brawler (25), Professional Punching Bag (42), {killer} Food (60), {killer}'s Nightmare (80), God-Slayer (95), Tower Breaker (101 = cleared).

## 4. Content rules (content `2.7`)

| Constant | Value | Range |
|---|---|---|
| `GAUNTLET_FLOORS` | 100 | `> 0` |
| `GAUNTLET_BASE` | 60 | `> 0` |
| `GAUNTLET_GROWTH` | 1.03 | `>= 1` |
| `GAUNTLET_BOSS_EVERY` | 10 | `> 0` |
| `GAUNTLET_BOSS_MULT` | 1.2 | `> 0` |

`gauntlet_enemies`: at least one normal enemy and one boss (`E044`); element ids (`E023`); weights `>= 0` (`E043`). `legacy_titles`: first `min_floor` 1 (`E045`), strictly increasing (`E032`). Checked in `tools/content_build.py` (`validate_gauntlet`) and `ContentDb.BuildGauntlet`.

## 5. Result

`GauntletSim.Run(content, fighter, seed)` returns `FloorReached`, `Cleared`, `KillerId`, `TitleKey`, the list of floors fought (enemy, boss flag, won, ticks, fight hash) for the wheel animation, and a SHA-256 `Hash` over all of it (`HashVersion` 1).

## 6. Golden

`golden/gauntlet-vectors.txt`: each species after a `focus` bot run at seed 7, sent up the tower with its run seed: floor, cleared, killer, title, hash.

## 7. Web client (the wheel)

After `finishRun` seals the monster, `web/run.js` asks the bridge for the entry's climb (`gauntlet(i)` replays `GauntletSim.Run(c, entry.Fighter(c), entry.Seed)`; its hash equals the stored `GauntletHash`) and shows the wheel before the grade screen:

- A slowing reel of the floors fought, highest on top: it starts on floor 1 and eases out (about 1.6 to 5 s, longer for higher climbs) onto the floor that beat the monster, with the enemy's name under the pointer. Floors won are ticked; up to two unfought floors above show "?". A cleared tower lands on a "Tower cleared" row.
- Then the comic defeat: the killer lunges and the monster is launched off screen or turned to ash (picked from the result hash, cosmetic only); a cleared tower gets a victory hop. The Legacy Title shows as "Floor N: <title>", and a button goes on to the grade screen.
- Tapping the wheel or the Skip button lands it at once. With `prefers-reduced-motion` the wheel opens already landed and the defeat is a still pose.
- The grade screen and every Vault card show "Floor N: <title>"; the grade screen can replay the wheel. The rank stays the grade (kj, 2026-10-30). Entries sealed before the gauntlet existed have no title and show none.

## 8. Follow-ups

- Done: `Vault.Seal` runs the gauntlet with the run's final fighter and the run seed, and stores `GauntletFloor`, `GauntletCleared`, `GauntletKiller`, `LegacyTitle` (string key) and `GauntletHash` on the `VaultEntry`. The wheel gets the floor list by running `GauntletSim.Run(c, entry.Fighter(c), entry.Seed)` again; the result is identical.
- Pay shards by floor instead of by rank once the gauntlet replaces the rank as the grade (kj's call; the rank stays the MVP grade).
- Done: the wheel spin and the comic defeat animation in the web client (section 7).
- Enemy skills and boss phases (they share the Daily Tower work, `specs/13` T10).
