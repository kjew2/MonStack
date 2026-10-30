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

JSON conventions (`SaveSerializer.Settings`): property names as declared (PascalCase), explicit `null`s, enums as member names, `ulong` seeds and counters named `*Seed` as 16-character lowercase hex strings (exact past 2^53), doubles round-trip exactly, `MissingMemberHandling.Ignore` on read, invariant culture, `DateParseHandling.None`.

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

`AccountSave.Load(content, store, nowSec)` tries Primary, then Backup. A slot that is corrupt (`SaveCorruptException`) or invalid (`SaveInvalidException`: wrong `Kind`, newer schema, an active run that no longer replays to its stored hash) is quarantined and the next slot is tried. No slot present gives null (the caller creates a new account); slots present but none loadable throws `SaveInvalidException`.

## 5. Schema versions

`AccountState.CurrentSchemaVersion` is 1. When a field changes after release, bump it and add a forward-only migration step on the `JObject` between `Decode` and materialisation; a step uses frozen literals, never `ContentDb`. There are no migrations yet and no released saves.

A save written by a newer schema is not loaded (`SaveInvalidException`) and never overwritten. A content change that breaks an active run's replay is reported the same way; the Vault keeps its entries' replay recipes so they stay verifiable.

## 6. Tests

`src/IdleRng.Runtime.Tests`: `SaveSerializerTests` (header, CRC, truncation, hex seeds), `SaveStoreTests` (rotation, tmp cleanup, crash between renames, quarantine), `AccountSaveTests` (round trip, backup fallback, rejection).

## 7. Open

- Cloud save (`specs/10`, `specs/08`): upload, restore and the conflict rule.
- A protobuf body if size ever matters (the header's `fmt` field reserves the switch).
