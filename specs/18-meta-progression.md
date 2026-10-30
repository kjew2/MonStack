# 18 - Account and Meta-Progression

Status: **live as an engine (T5 of `specs/13`)**. Code: `src/IdleRng.Sim/Account` (account rules), `src/IdleRng.Sim/Training/RunState.cs` (`RunModifiers`), `src/IdleRng.Runtime/Save/AccountSave.cs` (save). Tests: `src/IdleRng.Sim.Tests/AccountTests.cs`, `src/IdleRng.Runtime.Tests/AccountSaveTests.cs`. The web client wires it in through its bridge (T6).

The design is `specs/12` sections 7, 9 and 10. The idle game's `GameState` and save are untouched (it is removed at T7).

> **Numbers and content are placeholders.** Payouts, talents, ranks and costs are placeholders until balance resumes (`CLAUDE.md`).

## 1. Account

`AccountState` holds, between runs:

| Field | Meaning |
|---|---|
| `Seed`, `RunsStarted` | Run seeds derive from these (section 2) |
| `Shards` | Training Shards, the soft currency (`specs/12` section 10.2) |
| `OwnedSpecies` | Species the player can train. **(agent)** All MVP species from the start until the gacha (`specs/13` T9) |
| `Books` | Skill books by id and count |
| `Talents` | Talent ranks by id |
| `Vault` | Finished monsters (`VaultEntry`, `specs/13` T4), oldest first |
| `Active` | The run in progress, or null |

Gems (premium currency) arrive with the gacha (T9).

## 2. Commands (`AccountOps`)

| Command | Rule |
|---|---|
| `New(seed)` | Every species owned; no shards, books, talents or Vault entries |
| `StartRun(species, books)` | No run in progress; the species is owned; the books are in the inventory and leave it. The run seed is `Derive(StreamSeed(seed, "run"), RunsStarted)`; `RunsStarted` goes up by one. The run starts with the account's talent bonuses (section 3) |
| `FinishRun(sealedUtcMs)` | The active run is over (completed or injured). Pays shards (section 5), returns unused books to the inventory **(agent)**, seals the monster into the Vault (`Vault.Seal`) and clears the active run |
| `AbandonRun()` | Ends the active run in any phase with no payout and no Vault entry; unused books return to the inventory (a misclick costs nothing). `RunsStarted` stays counted, so the next run gets a new seed and abandoning cannot reroll a run. (Agreed between the two building sessions, 2026-10-30; the web client's Abandon button calls it.) |
| `BuyTalent(talent)` | Not maxed; enough shards. Costs shards, adds a rank. Applies from the next run: a run keeps the bonuses it started with |
| `GrantBooks(book, count)` | Adds (or removes) books. The Daily Tower will grant books through it (T10); tests and debug use it now |

Rejected commands throw and change nothing. The sim has no clock: the caller passes the seal time.

## 3. Run modifiers

`TrainingRun.Start(content, species, seed, books, mods)` takes a `RunModifiers` (null = none). It is stored on `RunState.Mods`, hashed into `StateHash` (run hash version 3), and must be passed to `Replay` again; the account save does that (section 6).

| Field | Effect | Default |
|---|---|---|
| `DrillCostMult` | Multiplies `DRILL_ENERGY_COST` (`TrainingRun.DrillCost(c, run)` gives the run's cost, for the UI) | 1 |
| `StatFloorMult` | Multiplies the species' base stats at the start (floored, capped at `STAT_MAX`) | 1 |
| `DraftSizeBonus` | Added to `DRAFT_SIZE` | 0 |
| `StartSp` | Extra skill points at the start | 0 |

## 4. Talent tree

Content table `talents`: `id`, `name_key`, `desc_key`, `effect` (`drill_cost`, `stat_floor`, `draft_size`, `start_sp`), `per_rank`, `max_rank`, `cost_base`, `cost_step`. Rank `r + 1` costs `cost_base + cost_step x r` shards. A talent at rank `r` adds `per_rank x r` to its modifier field (`drill_cost` adds to the multiplier, so it is negative).

Placeholder talents (the drafts' examples, `specs/12` section 9):

| Talent | Effect per rank | Max | Cost |
|---|---|---|---|
| Frugal Drills | drills cost 5% less energy | 5 | 20, 40, 60, ... |
| Strong Stock | base stats +10% | 3 | 30, 60, 90 |
| Wide Scouting | +1 tree per milestone draft | 2 | 50, 100 |
| Head Start | +1 starting skill point | 3 | 25, 50, 75 |

A flat list for now; a branching talent tree is a later content change. Rules (`E043`, both `tools/content_build.py` and `ContentDb`): `max_rank >= 1`, costs `>= 0`; `drill_cost` per rank `<= 0` and the multiplier stays `>= 0` at max rank; `stat_floor` per rank `>= 0`; `draft_size` and `start_sp` per rank a whole number `>= 0`.

## 5. Shard payout

`Payout = SHARDS_PER_CYCLE x cycles played + SHARDS_PER_RANK x rank index reached` (E = 0). Injured runs pay too (kj, `specs/12` section 4.5).

| Constant | Value | Range |
|---|---|---|
| `SHARDS_PER_CYCLE` | 5 | `>= 0` |
| `SHARDS_PER_RANK` | 10 | `>= 0` |

## 6. Save

A new save lineage (kj: clean break, `specs/13` section 1), in its own store slot (`AccountSave.SlotName` = `account`), using the idle save's file shape (header with CRC, JSON body) and its two-slot rotation:

```
{ "Kind": "monster_account", "SchemaVersion": 1,
  "Account": { ...AccountState without Active... },
  "ActiveRun": { "SpeciesId", "Seed", "Books", "Mods", "Log", "Hash" } | null }
```

- The run in progress is stored as its replay recipe and rebuilt with `TrainingRun.Replay` on load. The stored hash must match the replayed run, otherwise the save is invalid (it no longer replays, for example after a rules change).
- `Load` falls back to the backup slot when the primary is corrupt or invalid and quarantines the bad slot. No slot: a new account is the caller's job.
- Vault entries are stored as they are (with their own replay recipe and hash).
- Schema migrations for this lineage start at v1 when a field changes.

## 7. Open

- What else shards buy, and gems (T9).
- A branching talent tree instead of a list.
- Retiring or deleting Vault entries.
