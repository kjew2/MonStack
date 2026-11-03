# 06 - Saves

Scope: how the game state is written to disk or browser storage and read back, in `IdleRng.Runtime` (`src/IdleRng.Runtime/Save`). The only save is the account (`specs/18` section 6). This spec was the idle game's save and offline catch-up spec; the profile schema, migrations, offline catch-up, in-app idle ticks and the clock seam went with that game at `specs/13` T7.

## 1. Design goals

1. A crash or power cut at any instant leaves at least one complete previous save.
2. A damaged file is detected, never half-loaded, and never overwrites a good one.
3. Saves are small, readable JSON: pasteable in a bug report, diffable, hand-editable in a fixture.
4. A run in progress is saved as its replay recipe, so a load rebuilds it exactly or fails loudly (`specs/18` section 6).
5. No clock reads in the save code: callers pass the time (for quarantine file names).

## 2. File format

One ASCII header line, then a UTF-8 JSON body without BOM:

```
IDLERNG-SAVE fmt=json crc32=1a2b3c4d len=3917\n
{ ...exactly len bytes of JSON... }
```

- `fmt` is the body encoding (`json`).
- `crc32` is CRC-32 (IEEE, the zip polynomial) of the body bytes, 8 lowercase hex characters.
- `len` is the body byte count.
- The schema version is **not** in the header: `SchemaVersion` inside the body is authoritative.

`SaveSerializer.Decode` rejects the file (`SaveCorruptException`) when the header is malformed, fewer or more bytes than `len` follow, the CRC differs, or the body is not JSON. A truncated write therefore fails fast.

JSON conventions (`SaveSerializer.Settings`): property names as declared (PascalCase); **lean** (save v10, kj 2026-11-01): a `null`, or a value equal to the field's starting value (what a new object holds: `0`, `false`, an `Id` of `-1`, a multiplier of `1`), is left out, and a missing field reads back as that starting value (`SaveContractResolver`, no per-field attributes); enums as member names, `ulong` seeds and counters named `*Seed` as 16-character lowercase hex strings (exact past 2^53), doubles round-trip exactly, `MissingMemberHandling.Ignore` on read, invariant culture, `DateParseHandling.None`.

## 3. Stores

`ISaveStore` is raw bytes in, raw bytes out, two slots, with the header and CRC checks left to the caller:

| Operation | Contract |
|---|---|
| `Write(bytes)` | Atomic and rotating: afterwards Primary holds the new bytes and Backup what Primary held. A failure throws `SaveWriteException` and changes nothing |
| `Read(slot)` | The bytes, or null when the slot is empty |
| `Quarantine(slot, nowSec)` | Moves a bad slot aside for diagnosis; never loaded again |
| `DeleteAll()` | Test and reset hook |

Implementations: `FileSaveStore(directory, slotName)` (files `<slot>.sav`, `<slot>.prev.sav`, `<slot>.tmp`: write the tmp with fsync, rename sav to prev, rename tmp to sav; a leftover tmp is deleted on open; quarantine files are `<slot>.corrupt-<unix>-<slot>.sav`) and `MemorySaveStore` (tests and bots). The account uses the slot name `account` (`AccountSave.SlotName`). The browser client stores the same bytes as text under the `localStorage` key `idle-rng-account-v1` (`web/run-engine.js`).

## 4. Loading

`AccountSave.Load(content, store, nowSec)` tries Primary, then Backup. A slot that is corrupt (`SaveCorruptException`) or invalid (`SaveInvalidException`: wrong `Kind`, newer schema) is quarantined and the next slot is tried. No slot present gives null (the caller creates a new account); slots present but none loadable throws `SaveInvalidException`.

## 5. Schema versions

`AccountState.CurrentSchemaVersion` is 12. When a field changes, bump it and add a forward-only migration step on the `JObject` between `Decode` and materialisation (`AccountSave.Migrate`); a step uses frozen literals, never `ContentDb`, and sets both `SchemaVersion` fields to the version it reaches.

| Step | Change |
|---|---|
| v1 -> v2 | Vault entries gain `Locked` (`false`), issue #82 (`specs/18` section 7) |
| v2 -> v3 | `Achievements` (empty), `Records` and `Vendor` (null), issue #73 (`specs/18` sections 4 and 8). Owned species and talents are kept. After materialisation `Decode` rebuilds `Records` from the Vault entries (`AccountOps.RebuildRecords`) |
| v3 -> v4 | `Tower` (Dungeon progress: `Day` -1, no attempts, nothing cleared), `specs/21` section 7. A `Tower` read as null is replaced by the same empty progress |
| v4 -> v5 | `Tower` gains `Tickets` 0, `TicketPieces` 0 and `GrantDay` -1 (no tickets yet; the first free ticket is due on the next visit), `specs/21` section 7 |
| v5 -> v6 | `Vendor` (when not null) gains `Rerolls` 0, issue #104 (`specs/18` section 4) |
| v6 -> v7 | `TalentsOff` (empty: every owned talent on), issue #106 (`specs/18` section 4) |
| v7 -> v8 | The run in progress and every Vault entry gain `Mods.LegacyDrafts` `true` (a missing `Mods` becomes `{ LegacyDrafts: true }`): they drafted fixed Awakening trees, so they replay that way; new runs roll them (issue #121, `specs/15` section 4.1) |
| v8 -> v9 | Vault entries keep their result, not their replay (`specs/27` section 3.5): each gains `Id`, the index of the run that made it, found from its seed (`AccountOps.RunSeed(account seed, i) == entry seed` for `i < RunsStarted`; -1 when none matches), and loses `Log`, `Mods` and `Ladder` |
| v9 -> v10 | Vault entries keep only their result (`specs/27` section 3.5, kj 2026-11-01): `Seed`, `RunHash`, `GauntletHash` and `GauntletCleared` are removed (cleared is read from the result: a title and no killer). The body's run in progress moves from `ActiveRun` (a replay recipe) to `Run` (the run's state without its log): a v9 recipe is replayed once on load, checked against its stored hash (`TrainingRun.LegacyRecipeHash`), and written back as a state. JSON is lean from v10 (section 2) |
| v10 -> v11 | `Endless` (Endless Tower progress: `Season` -1, no climbs, nothing cleared) and `Rulebooks` (empty), `specs/28` section 5. Both are their starting values, which lean JSON writes as nothing, so the step only moves the version; a null in the file reads as the same empty values |
| v11 -> v12 | `Name`, the display name (null: none), `specs/28` section 6. Null is its starting value, so the step only moves the version |

A save written by a newer schema is not loaded (`SaveInvalidException`) and never overwritten.

**The server's stored parts migrate through the same steps** (issue #127, `specs/27` section 7). The server stores the account as parts under `head.Schema`; `AccountParts.Join(content, items, schema)` reassembles them into a body of that schema and `Decode` runs the steps above, so a device's save and the server's parts have one migration path. Parts exist from v10 (`AccountParts.FirstSchema`: the shape they have since the monsters keep only their result), so a step from v10 on must work on a body assembled from parts. A new step therefore needs no server code: the next commit after the bump rewrites every part in the new schema.

**When content or rules change under a save** (decided 2026-10-31; **(agent)**, kj can veto). A rules or content change must never cost the player the account:

- *Run in progress.* Since v10 it is stored as its state (`Run`, the `RunState` without its command log), so a rules or content tuning change does not stop it: it carries on under the new rules from where it was. It is dropped only when the state refers to an id no longer in content (species, ladder enemy, tree, offer or book; `AccountSave.CheckRun`). A v9 save's recipe can still stop replaying (an id removed, a rule or the run hash changed). In both cases `Decode` drops the run, returns every book it brought in to the inventory (as `AbandonRun` does), keeps `RunsStarted` (the next run gets a new seed) and loads the rest of the account. `Decode`/`Load` report a `notice` sentence for the player; the web client shows it (on the server, the open that dropped it; the next commit stores the account as dropped, `specs/27` section 7). Nothing is quarantined for this. The rank pools (content 4.3) are not such a change: a recipe without a `Ladder` replays as a legacy run against the flat ranks with its version 5 hash (`specs/18` section 6).
- *Vault entries.* They store their result (grade, final stats, trees, gauntlet floor, killer, title and hash) as plain data, so listing and displaying them never replays anything. Only the parts that replay (`Fighter`, the gauntlet wheel) can stop working for an entry sealed under older rules; those return an error for that entry and nothing else is affected. The entry's `RunHash` and `GauntletHash` say which rules produced it. Entries are never deleted or rewritten by a load.
- *Content version.* The account stores the `ContentVersion` it was last loaded with. A load under a different MAJOR adds a notice and re-stamps it. A MAJOR bump (`specs/05` section 4, `E090`) is where a real migration is written, if one is worth it.
- *Newer schema* is the one case that still refuses: the file is kept and never overwritten.

**The browser keeps no save of its own** (since `specs/27` step 3, 2026-11-03). The account lives on the server; `localStorage['idle-rng-account-v1']` is only a cache of the server's account plus the commands not yet committed, replaced by every open, so a damaged copy on the device costs nothing. The saves kept aside before the server (issue #93: `idle-rng-account-v1-unloadable` and `-<ms>` keys) are deleted with the old account on the first open of that build (`specs/27` decision 9, section 5.3).

## 6. Tests

`src/IdleRng.Runtime.Tests`: `SaveSerializerTests` (header, CRC, truncation, hex seeds), `SaveStoreTests` (rotation, tmp cleanup, crash between renames, quarantine), `AccountSaveTests` (round trip, backup fallback, rejection, the v1 -> v2 to v8 -> v9 migrations, tower progress, vendor reroll and talent loadout round trips).

## 7. Open

- The account on the server (`specs/27`, issue #25): the browser's save becomes a cache of the server's account plus the commands not yet accepted, replaced on every open; existing local saves are wiped (kj, 2026-11-01); Vault entries gain `Id` and lose their replay log, `Mods` and `Ladder` (`specs/27` sections 3.1 and 3.5). Design only.
- A protobuf body if size ever matters (the header's `fmt` field reserves the switch).
