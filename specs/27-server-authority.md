# 27 - Server Authority: the Account Data Model and Checkpoints

> **Status: being built** (design by kj, 2026-11-01). Build steps 1 (sim), 2 (server core, `src/IdleRng.Server`, 2026-11-02), 4 (the Cloud Code module `server/ugs/MonStackAccount` and its deploy step, 2026-11-03; deploying waits for kj's service account secret) and 3 (the client, `web/sync.js`, 2026-11-03: the UGS transport on GitHub Pages, the local test server elsewhere) are done, and of step 5 the `profile` projection (2026-11-03); the first remote run waits for the secret (section 10). kj chose a server-held account over a client-uploaded cloud save, answered this spec's questions, then compared every way of preventing abuse against server calls and latency (section 8) and chose **option E**: the client plays, the server re-checks at checkpoints, and the remaining risk (a script replaying a run to keep its best outcome, measured in section 8.2) is accepted. The draft where the server plays every command is in git history. Supersedes cloud save as a client upload (`specs/08` section 5.2) and, for the trainer, the idle game's segment certification (`specs/10` section 4). Issues: goal #15, #25 (cloud save), #19 (accounts), #21 (server time), #33 (save trust fields), #28 (server tests and deploy).

Scope: how the player's account is modelled and stored on the server, how the client sends what it did, and how the server checks it. PvP battle calls stay in `specs/08` section 10.3; purchases in section 6.

## 1. Decisions

1. **The server holds the account; the client plays and the server re-checks** (kj, 2026-11-01, option E of section 8). The client plays on its own engine with no wait, records the commands it applied, and sends them at checkpoints. The server re-applies them with the same `IdleRng.Sim` code (a Cloud Code C# module, `specs/08` section 10.1) and accepts the result only if its state hash matches the client's. The server trusts only the commands, which are player choices (`specs/00` decisions log item 3: verification, not a server sim).
2. **Replay is an accepted risk** (kj, 2026-11-01: "good enough"). The client knows its seeds, so a script can play a run several ways and send the best; the server cannot see that. Measured at about half a grade (section 8.2). Everything else (forged numbers, illegal moves, edited saves, chosen seeds, clock tricks) is stopped.
3. **A connection is needed** (kj, 2026-11-01: no offline play). The game opens only after the server answers, and play waits ("Connecting...") whenever a checkpoint it needs cannot land (section 5.2), so unchecked play never runs far ahead.
4. **One device holds the account at a time** (kj, 2026-11-01: "log out the first client instead"). Signing in takes the account; the device that held it is signed out at its next server call. No conflict prompt, no merge.
5. **The data is partitioned by domain** (kj, 2026-11-01), each part its own item with its own hash; the account hash is a hash of the part hashes (section 3).
6. **Commits are copy-on-write under one root item.** New part versions are written under new keys, then `head` is swapped with a write lock; that swap is the commit (section 4).
7. **Store results, not history** (kj, 2026-11-01: "prune the model"). A sealed monster is kept as what it became, not the commands that made it; commands are kept only until accepted, the run in progress only until it seals (section 3.5).
8. **Unbounded data never lives in a part** (section 3.4).
9. **Existing accounts are wiped** (kj, 2026-11-01). Every player starts a new server account; old local saves are deleted (section 5.3).
10. **Debug commands only for listed players** (kj, 2026-11-01), section 6.3.

## 2. Measurements (2026-11-01, content 5.x)

An account after 50 sealed runs (the `balanced` bot, the starter species), compact JSON:

| Field | Bytes | Grows with |
|---|---|---|
| `Vault` (50 entries) | 334,549 | Sealed runs, capped by `VAULT_CAP` (50; expansions planned, `specs/18` section 7) |
| One Vault entry | about 6,700 | Fixed per entry: its `Log` (the replay recipe, about 5,500) is 80% of it; `Mods` about 400; the result the Vault screen shows (grade, stats, trees, gauntlet, title, hashes) about 800 |
| `Records`, `Vendor`, `Tower`, `Achievements` | 164, 318, 104, 30 | Content size (achievements, stats) |
| `OwnedSpecies`, `Books`, `Talents`, `TalentsOff`, seed, counters | under 100 together | Content size |
| A mid-run save (whole account, one run in progress) | 12,760 (indented) | The run's log: about 90 commands for a whole run |

After the audit of 2026-11-01 (section 3.5; save v10), the same kind of account (50 monsters from mixed bot policies, a run in its last cycle) is **32.4 KB in 56 items**, down from 54.4 KB: `training` 6.2 KB to 1.4 KB, the `vault` index 3.5 KB to 1.1 KB, each monster 883 B to about 590 B, every other part under 250 B.

## 3. The data model

### 3.1 Parts

All parts are Cloud Save player items in the **private** access class: only the module (its service token) reads or writes them; the client gets its account through the module (section 5.1).

| Part | Holds | Bound | Written by |
|---|---|---|---|
| `head` | The root: `seq`, the account hash and the one before the last commit, each part's stored key and hash, each Vault entry's stored key, the holder device, schema and content version, `debugUsed` | About 2 KB at a full Vault | Every commit, and a claim (section 5.1) |
| `training` | `Seed`, `RunsStarted`, `TalentsOff` (the next run's loadout), the run in progress as its state (no command log), and the post-run vendor while it is open | About 1.4 KB at most | `startRun`, every run command, `finishRun` (opens the vendor), `abandon`, `setLoadout`, `buyOffer`, `rerollVendor`, `closeVendor` |
| `wallet` | `Shards`, `Books` (later any premium currency, section 3.4) | Content size | `startRun` (books leave), `finishRun`, `abandon`, `buyOffer`, `rerollVendor`, `dismantle`, `runTower`, `runEndless` |
| `collection` | `OwnedSpecies`, `Talents` (ranks) | Content size | `finishRun` (unlocks), `buyOffer`, `runEndless` (unlocks) |
| `progress` | `Achievements`, `Records` | Content size | `finishRun`, `runEndless` (achievements) |
| `dungeon` | `Tower`: tickets, pieces, grant day, attempts, bests | Fixed | `runTower`, `finishRun` (pieces), the daily grant |
| `endless` | `Endless`: the season of the last climb, climbs this season, floors cleared this season and ever (`specs/28` section 5) | Fixed | `runEndless` |
| `rulebooks` | `Rulebooks`: each Vault monster's gambit rulebook as text, by entry id (`specs/28` section 4.4); apart from the entries, which never change, and from the other parts, since the editor writes it often | At most `VAULT_CAP` rulebooks of `GAMBIT_SLOTS` rules, about 3 KB at 50 | `setRulebook`, `dismantle` (the monster's rulebook goes with it) |
| `vault` | The Vault index: entry ids in Vault order, the locked ids, each entry's short hash (16 hex characters) | `VAULT_CAP` ids, about 1.1 KB at 50 | `finishRun`, `dismantle`, `lock` |
| `mon.<id>` | One sealed monster as its result: species, grade, rank, end, cycles, fights, final stats, trees (bought nodes, rolls, fusions), books used, gauntlet floor, killer and Legacy Title, seal time | One per entry, about 0.6 KB | `finishRun` creates; `dismantle` deletes; never changed |

Rules behind the split:
- **By writer set.** Each part changes for one reason. A run command touches only `training`; a vendor buy touches `wallet`, `collection` and `training`. A commit rewrites only the parts its commands changed, so a checkpoint in the middle of a run writes `training` and `head`, a few KB.
- **Immutable things are their own items.** A sealed monster never changes (`specs/18` section 6), so it is written once. `Locked` is the player's flag, not part of the monster (it is outside `RunHash` already), so it lives in the `vault` index.
- **A part is bounded by content or capped by a rule.** New fields join the part whose writer set they share; a field that would grow without a bound goes to section 3.4.

Vault entry ids: an entry is named by the index of the run that made it (`RunsStarted` when it started), unique in the account. The sim gains `VaultEntry.Id`; accounts are wiped (decision 9), so no migration is needed. Commands keep addressing entries by Vault position, which is deterministic in a replay.

### 3.2 Hashes

- **Part hash**: SHA-256 of the part's compact JSON, exactly as the account save writes those fields (`IdleRng.Runtime.Save.AccountParts`: the same Newtonsoft serializer and settings in the browser and on the server) **(agent, 2026-11-01: instead of a hand-written binary encoding, so a field added to `AccountState` is covered without a second field list; a test fails when a field is in no part; kj can veto)**. Doubles are written in round-trip form, the same on every .NET runtime since 3.0.
- **Entry hash**: the first 16 hex characters of the SHA-256 of the entry's JSON without `Locked` (enough to catch a damaged item; kj 2026-11-01). The `vault` part lists the ids in Vault order, the locked ids and every entry hash, so it pins every entry without loading them; `Join` refuses an entry that does not match its hash.
- **Account hash**: SHA-256 over the sorted `key hash` lines of every part except the entries (which the vault part pins). The client computes it after its last pending command and sends it with the commit; the server computes it after re-applying them. Equal hashes mean equal accounts, with no field list to keep in step.

Because the `vault` part pins entries by hash, a commit loads only the parts its commands declare (`AccountCommands.Reads` and `Writes`); the parts it does not load are stood in for by a new account's, which cannot change the result (step 1's test proves it per command), and only loaded parts are written back. The index and its entries load together: a commit whose commands touch the Vault (`startRun`, `finishRun`, `dismantle`, `lock`, `runTower`, `setRulebook`, `runEndless`) loads every entry, and a commit of run or vendor commands loads none **(agent, 2026-11-02: simpler than loading single entries, at most about 30 KB; kj can veto)**.

### 3.3 Projections (read by others, never the truth)

Written by the module after an accepted commit, rebuildable from the parts, allowed to lag, never read back by a commit:

| Projection | Where | Holds | For |
|---|---|---|---|
| `profile` | Cloud Save **protected** player item, indexed; other players read it through a server call | Display name (the account's `Name`, `specs/28` section 6.1), best grade, best Dungeon floor, achievement count, the showcase monster's result. Built in the core (section 3.3.2); on UGS the protected player item `profile`, a JSON object (step 4) | Other players, profile cards |
| Leaderboards | `ILeaderboardStore`, with the player name; on UGS, Cloud Save private custom items (`CloudSaveLeaderboardStore`, step 4: a custom entity per board, `endless-<season>`, its rows in 16 shards, names on `board-names`) **(agent, 2026-11-03: in place of UGS Leaderboards, which would need a board created ahead of every week and the Leaderboards admin key inside the module to take a debug account off a board; kj can veto)** | Endless Tower season best (built in the core: `EndlessBoard`, written after an accepted commit with a climb, `specs/28` section 6.1), each sealed monster's gauntlet floor, overall and per species (`GauntletBoard`, written after an accepted commit with a seal, `specs/19` section 9; boards `gauntlet` and `gauntlet-<species>`), Dungeon best floor per day (waits on kj: `specs/21` section 11 question 8), later PvP rating | Rankings |
| `pvp.team` | Cloud Save **protected** player item, indexed (T12) | The defending team: entry ids, results, the rating bracket | Matchmaking across players (`specs/10` section 9) |

### 3.3.1 Access classes

Cloud Save has four per player: default and public (the player writes them), protected (only the server writes; the owner reads), private (only the server). Anything a player can write can be faked, so the game uses only the server-written classes **(kj, 2026-11-01)**:

| Class | Holds |
|---|---|
| Private | The account: every part of section 3.1 and every `mon.<id>` |
| Protected | The copies others see (`profile`, `pvp.team`), indexed for queries across players |
| Public, default | Nothing |

Game-wide data (a season's settings, a shared PvP pool, the season boards) goes on a custom entity (a custom id such as `season-3`) instead of a player, written only by the server, in the private class. Which classes can be indexed and queried across players is still to be confirmed against Unity's documentation when `profile` and `pvp.team` are built (step 5, T12): step 4 needs no index (the boards are read by key).

### 3.3.2 The profile (built in the core, 2026-11-03)

- **Holds** (`PlayerProfile`, lean JSON like the parts): `Name` (null: none), `BestGrade` (the fight_ranks id of `Records.BestRank`, null before the first seal) and `BestRank`, `BestDungeonFloor` (`Tower.ClearedEver`), `Achievements` (the count), `Showcase` (a Vault entry's result as its `mon.<id>` item holds it, without `Locked`; null for an empty Vault) and `AtSec` (server time of the commit that wrote it).
- **The showcase monster** is chosen by the server until the player can pick one: the Vault's best grade, then the highest gauntlet floor, then the earliest sealed (lowest id) **(agent, 2026-11-03: a projection is rebuilt at every commit that changes it, so the rule is cheap to change; kj can veto, or ask for a player-chosen showcase, which needs a command and a save field)**.
- **Written** by `AccountServer` (`Profiles`, over an `IProfileStore`; `MemoryProfileStore` in tests and the page) after an accepted commit that holds a command writing `progress`, `dungeon`, `vault` or the entries (`Profiles.Sources`), or that rewrites every part after a content or schema change. Such a commit also loads every one of those parts, so the profile is never built from a stand-in: a `runTower` loads `progress` (about 200 B) and a `setName` the `vault` index and entries **(agent: a projection is never read back, so it is rebuilt whole rather than patched)**. Best effort, like the boards: a failed write leaves it behind until the next such commit; a retried commit and an open write nothing. A debug account keeps its profile (section 6.3 leaves it off boards and PvP only).
- **Read** with `Profiles.ReadAsync(player)`: null before the first such commit; a player with no name shows the generated `Trainer XXXX` (`specs/28` section 6.1). On UGS (step 4) the store is `CloudSaveProfileStore` and the module's `Profile` endpoint reads it.

### 3.4 Data that grows without a bound

None exists yet. When a feature adds it:

| Kind | Model | Retention |
|---|---|---|
| PvP match history (T12) | One item per match, `match.<seq>` | The last `PVP_HISTORY_KEEP` (placeholder 50) |
| Purchases and grants (T13) | One record per transaction id, idempotent (`specs/08` section 6) | Kept (refunds, chargebacks) |
| Premium currency (if it ships) | UGS Economy balances, or a `wallet` field credited only by the purchase handler | |
| Commit journal (audit, desync reports) | Analytics events (`specs/08` section 8), not Cloud Save | The analytics provider's |
| Seasons (T12) | `season.<n>`, frozen when it ends | Kept |

Cloud Save allows 2000 keys per player per access class; at `VAULT_CAP` 50 the parts use about 60.

### 3.5 What is not kept

| Data | Kept | Why it can go |
|---|---|---|
| A sealed monster's replay log (`Log`, about 5.5 KB of its 6.7 KB), `Mods` and `Ladder` | No | Only `VaultEntry.Fighter` reads them, to rebuild the final stats and bought nodes the entry already stores (`TrainingRun.MonsterFighter` takes nothing else; the stored trees keep its order, absorbed trees before their slot). The fighter is built from the entry instead. A sealed run needs no re-check: the server re-applied its commands before it sealed |
| Commands | Until accepted | Applied, then dropped; a sample may go to analytics for desync reports |
| The run in progress | Until it seals or is abandoned, as its state | Its state (no command log, kj 2026-11-01: the server applies new commands to the state it holds) in `training`; cleared at seal |
| A sealed monster's seed, run hash, gauntlet hash and cleared flag | No (save v10) | Nothing replays a sealed monster or its gauntlet any more; cleared is read from the result (a title and no killer) |
| Values at their starting value, nulls | Left out of every item | Lean JSON (save v10, `specs/06` section 2); a missing field reads back as its starting value |
| The gauntlet floors | Never stored | Shown once by the wheel right after sealing, from the gauntlet the seal just ran; never replayed (kj, 2026-11-01) |
| Dungeon attempts | Never stored | A result is a pure function of the team, day and content (`specs/21` section 1); only rewards and bests are kept in `dungeon` |
| The post-run vendor | Only while its screen is open, in the session that sealed the run | Transient (kj, 2026-11-01, `specs/18` section 4): every load of the account closes an open vendor with a `closeVendor` command, so it never survives a reload or a move to another device. Between the commits of that session it sits in `training`, because the server must hold it to re-check a vendor buy |

Kept although derivable: the owned species (from the achievements), so a later content change cannot take a species away.

What this gives up: a sealed monster can no longer be replayed step by step (balance questions use the bot CLI, `specs/09`). Built from its stored result, a sealed monster keeps the stats it was sealed with under later rule changes, as `specs/12` section 6 intends; skills, triggers and rules are still read from content by node id, so a skill's tuning applies to old monsters, and a removed node id is still a content MAJOR (`E090`).

## 4. Commits

A commit carries the commands since the last accepted one. Commands are the bridge's own (`web/wasm/BRIDGE.md`): account commands `startRun`, `finishRun`, `abandon`, `buyOffer`, `rerollVendor`, `closeVendor`, `setLoadout`, `dismantle`, `lock`, `runTower`, `setRulebook`, `runEndless`, and run commands `drill`, `rest`, `fight`, `take`, `discard`, `reroll`, `spend`, `book`, each `{ k, a }` with the arguments as JSON. Read-only calls (`gauntlet`, `towerInfo`, `towerFloor`, `endlessInfo`) are not commands. The switch that applies them moves from `IdleRng.Web.RunHost` into `IdleRng.Sim.Account.AccountCommands.Apply(content, account, command)`, so the browser, the module and the tests run one dispatcher. Each command declares the parts it reads and writes (`AccountCommands.Touches`), checked by a test that applies every command with the other parts absent.

```
client                                              module (Cloud Code)
  apply command locally, append to pending
  at a checkpoint (section 5.2):
  Commit { deviceId, fromSeq, fromHash, commands, toHash, contentVersion } ->
                                                    read head (with its write lock)
                                                    holder == deviceId?           else NotHolder
                                                    seq == fromSeq, hash == fromHash?  else Stale
                                                    content version the module's?  else ContentStale
                                                    read the parts the commands touch, and the
                                                      mon items they reference
                                                    apply each command (AccountCommands.Apply),
                                                      checking the client's inputs (section 6.2)
                                                    account hash == toHash?        else Mismatch
                                                    write changed parts under new keys, then
                                                      head (seq+1, new keys and hashes) with its write lock
                                                    delete superseded keys; write projections
  <- Accepted { seq, hash, serverSec, day }  |  Rejected { code, index, reason }
```

- **Copy-on-write.** Stored keys carry the commit that wrote them and a random tag per attempt: `wallet_124_9f3a01c2`, and a Vault entry `mon-7_124_9f3a01c2` (entry 7, written once by commit 124; `head` maps each entry id to its key). The tag keeps two attempts from the same base (a second tab, a retry racing the original) from ever writing the same key **(agent, 2026-11-02: in place of `wallet.000124` and `mon.<id>`, which two racing attempts could overwrite under each other; letters, digits, `-` and `_` only, for Cloud Save's key alphabet; kj can veto)**. New versions are written first; nothing refers to them until `head` swaps, and `head` is one item, so a commit lands whole or not at all. After the swap the commit deletes the items it superseded (the old versions of the parts it rewrote, dismantled entries). A crash between leaves unreferenced keys: every open deletes the keys of commit `seq` or earlier that `head` does not name (keys of a later commit belong to one in flight and are left alone). A commit that loses the swap to another deletes what it wrote and returns `Stale`; so does one whose items were deleted under it by a commit that landed meanwhile, and an open in that case retries on the new `head`.
- **Idempotent.** A commit that repeats the last accepted one (same `fromSeq`, `fromHash` and `toHash`, `head` already at `fromSeq + 1` with that hash) returns `Accepted` with that `seq` and hash and applies nothing, so a retry after a lost answer is safe. An older one is `Stale`.
- **Content changes.** When `head` was written under other content than the module's, the next commit loads and rewrites every part, so whatever loading under the new content changed (a run in progress that no longer loads, an achievement added) is stored as the client's own load made it; `head` then carries the new version. An open does not write: it returns the account as loaded under the module's content, with the stored `seq` and hash as the client's base.
- **Save schema changes** (#127) are handled the same way. `head.Schema` names the save schema the parts are stored in; a load joins them into a save body of that schema and runs the device save's own forward migrations (`AccountParts.Join` with the stored schema, then `AccountSave.Migrate`, `specs/06` section 5), so an open returns the account migrated and the next commit loads and rewrites every part under the module's schema. Parts stored before v10 (`AccountParts.FirstSchema`, the shape they have had since) do not load (`AccountDamagedException`); none exist (decision 9). An account stored in a **newer** schema than the module reads (a module rolled back after a newer one wrote it) gets `ContentStale` on open and commit and nothing is written, not even a claim **(agent, 2026-11-03: the same answer as other content, since the client's remedy is the same, a reload once the deploy settles; kj can veto)**.
- **Size.** A commit holds at least one command, at most `COMMIT_MAX_COMMANDS` (placeholder 500) and at most one `finishRun`; anything else is `Refused` before a command is applied. It writes at most the 6 parts below `head`, `head` itself and one new entry. `COMMIT_MAX_COMMANDS` and `CLOCK_SLACK_SEC` are server configuration (`IdleRng.Server.ServerConfig`), not content constants **(agent, 2026-11-02: they bound the server, not the game; kj can veto)**.
- **Cost of a check.** Re-applying a whole run is about 90 commands, each rank fight a `BattleSim.Resolve` (0.1 to 1.3 ms in the spike); a `runTower` replays up to the Dungeon's floors. Milliseconds, inside Cloud Code's 15 s limit. An Endless Tower climb is one 4v4 fight per floor: measured 2026-11-03 on .NET 8 Release with bot teams (`focus` and `balanced`, 24 to 32 floors), 7 to 25 ms a climb, 0.2 to 0.8 ms a floor, so under 0.5 s even at `ENDLESS_MAX_FLOORS`. A commit carries at most `COMMIT_MAX_CLIMBS` (placeholder 10) climbs **(agent, 2026-11-03)**: a client sends each climb as its own checkpoint, so the cap only stops a scripted commit from tying up a call (worst case about 4 s).

| Rejection | Meaning | Client does |
|---|---|---|
| `NotHolder` | Another device took the account | Signs out (section 6.1) |
| `Stale` | The server is past `fromSeq` (a second tab, or a restored old copy) | Opens the account again (section 5.1) and drops its pending commands |
| `ContentStale` | Client and module run different rules or content, or the account is stored in a newer save schema than the module reads | "A new version is out", reloads; the pending commands are re-applied by the new build (section 7) |
| `Mismatch`, `Refused` | A command was refused, or the result differs (an edited client or save, or a determinism bug) | Reverts to the server's account, reports the first differing command for diagnosis. Never a ban (`specs/10` section 4.6) |
| Network, timeout, 429, 5xx | Not a verdict | Keeps the pending commands; retries with backoff; play waits as in section 5.2 |

## 5. The client

### 5.1 Open

`Open { deviceId, claim }` returns the account (all parts and every `mon.*` item, assembled by the module into the `AccountSave` text the engine loads, gzip then base64: a few KB), `seq`, `hash`, the holder, the server time and day.

- No `head` yet: the module creates the account (**genesis**): it draws the account seed, so seeds are never client-chosen (`specs/10` section 3), and writes the parts at `seq` 0.
- `claim: true` makes this device the holder. The client claims on a Google sign-in and on a guest's first open; an ordinary page open does not claim, so a device signed out by another one cannot take the account back without signing in again. Genesis makes the opening device the holder whether or not it claims. A claim rewrites `head` but is not a commit: `seq` and the hash stay **(agent, 2026-11-02)**. The answer never names the holding device, since a device id is what a commit is checked against.
- **The game waits for it** (decision 3): "Connecting..." until `Open` answers, with a 20 s timeout (above a cold Cloud Code worker's 12 to 15 s, `specs/08` section 10.1) and Try again. If the server's `seq` equals the device's base, the device keeps its state and commits its pending commands; otherwise the server's account replaces the device's copy and the pending commands are dropped.
- **As built** (`web/sync.js`, step 3) **(agent, 2026-11-03; kj can veto)**: the device always loads the server's account and applies its pending commands again on top, rather than trusting its cached copy (one path, and it is how a reload onto new rules works, section 7); a pending command the engine now refuses is dropped with everything after it. When the server is one commit past the base with the hash the device last sent (the commit landed, its answer was lost), the commands that commit carried are dropped and the rest kept. An open whose account carries a notice (a run dropped under new rules) shows it; as an open does not write, it shows again on the next open until a commit stores the account. After a Google sign-in the device claims: on a remote server, when the sign-in switched to another player, the page reloads and opens that player's account with `claim`; on the local test server the player does not follow the sign-in, so nothing changes.

### 5.2 Checkpoints (when the client commits)

No timer. The client commits:
- after a command that changes something besides the run in progress: `startRun` (so the server counts the run before it is played), `finishRun`, `abandon`, `buyOffer`, `rerollVendor`, `closeVendor`, `dismantle`, `lock`, `runTower`, `setLoadout`, `setRulebook`, `runEndless`;
- when the page is hidden (`visibilitychange`), so the run in progress continues on another device;
- when `CHECKPOINT_RUN_COMMANDS` (placeholder 25) run commands are pending;
- on open, if commands are pending.

One commit is in flight at a time. Taps within a run do not wait for it. **Play waits** ("Connecting...") when a commit cannot land and either an account-level command is next (its commit is the checkpoint) or the pending run commands reach `CHECKPOINT_RUN_COMMANDS` twice over: so at most about 50 taps of a run are ever unchecked, and nothing that changes the account outside the run happens before the server has it.

As built **(agent, 2026-11-03; kj can veto)**: the waiting command is not applied (it returns `{ok: false, waiting: true}` and the "Connecting..." overlay covers the page with Try again); the player taps it again once the overlay goes. The failure is learnt at a checkpoint, so the account-level command whose commit fails first is applied, and the next one waits. Play also waits for an in-flight commit before a second `finishRun` or more than `COMMIT_MAX_CLIMBS` climbs, which one commit cannot carry. A retry after a network failure resends the same commit (same base, commands and `toHash`; idempotent, section 4) with backoff (1 s doubling to 30 s), and commands played meanwhile go in the next one. `Stale`, `Mismatch`, `Refused` and `NotHolder` reload the page, which opens the server's account (for `Mismatch` and `Refused` with the pending commands dropped and the refused command kept in `monstack-sync-v1` for the account sheet; for `NotHolder` as a new guest); `ContentStale` shows "A new version is out" with a Reload button instead of reloading by itself, so a cached old build cannot loop.

### 5.3 What the device keeps

| Key | Holds |
|---|---|
| `idle-rng-account-v1` (existing) | The `AccountSave` text the engine loads: the server's account plus the pending commands applied. A cache, replaced by every `Open` |
| `monstack-sync-v1` (new) | `deviceId` (random, made once), `baseSeq`, `baseHash`, the pending commands; as built also the commit in flight (`inflight`: its base, command count and `toHash`, for the retry and for an open after a lost answer), whether the next open claims, and the last rejected commit (`lastReject`) |
| `monstack-auth-v1` (existing) | The session (`web/auth.js`) |

On the first open of this build the device's old account (`idle-rng-account-v1` from before the server, its `-unloadable` backups and the older keys) is deleted before the server account is loaded (decision 9). Only one tab plays: a second tab sees the first through a `BroadcastChannel` and says the game is open elsewhere; a second tab that commits anyway gets `Stale`. As built **(agent, 2026-11-03)**: the newer tab yields; its "Play here" button takes over, and the older tab then says the game was opened in another tab and stops playing (its own "Play here" reloads it).

### 5.4 The local test server

`check.sh`, `./play.sh` and kj's phone artifact (which cannot rely on reaching UGS) run the same server code inside the page: `IdleRng.Server` compiled into the WebAssembly build with an in-memory store kept in `localStorage`, behind the same `Open` and `Commit` calls. The page uses it when served from anything but the GitHub Pages address and says "Local test build" in the header. It has no anti-cheat value **(agent: keeps the browser checks and the phone artifact working without a network; kj can veto)**.

As built (step 3, 2026-11-03) **(agent; kj can veto)**: `LocalServerCall` in the bridge is stateless (it takes the store's text from `localStorage['monstack-local-server-v1']` and returns it changed), keyed by a player id made per browser (`monstack-local-player-v1`), allows debug commands, and answers synchronously. The page talks to it through the same transport interface a remote server plugs into (`web/wasm/BRIDGE.md`, "Sync transport"). On the GitHub Pages address the page uses the module instead (`web/ugs-transport.js`) and never falls back to the local test server: while the module is not deployed, or cannot be reached, the page stays on "Connecting..." with Try again (coordinator's instruction, 2026-11-03). A test and development hook, `install`, stores a given account as the player's new head; the browser checks seed accounts with it.

## 6. Rules the server enforces

### 6.1 One holder

`head.holder` is `{ deviceId, sinceSec }`. A commit or open from another device returns `NotHolder`. That device's client forgets its session (`MonStackAuth.signOut`), drops its pending commands, starts a new guest (a new account by genesis) and says "Signed out: your account was opened on another device. Sign in to continue here." Commands it played after the takeover are not applied (kj's rule: the newest sign-in wins).

### 6.2 Inputs the client supplies

Commands are choices, except three values from the device:

| Input | Command | Server rule |
|---|---|---|
| Seal time | `finishRun(sealedUtcMs)` | Display only. Accepted if not after the server's time plus `CLOCK_SLACK_SEC` (placeholder 300); else `Refused` |
| Dungeon day | `runTower(day, team)` | The day the server gave the client at its last `Open` or commit (the device clock is not used), accepted if it is the server's day or the day before (a commit landing just after midnight). The daily free ticket follows from the day, so a moved device clock grants nothing (#21). As built **(agent, 2026-11-03)**: the client carries the server's time forward with the page's monotonic clock (`performance.now()`), so a page left open past midnight moves to the next day, and asks the server again (an `Open` without claim) when the page is shown after 10 minutes hidden; the seal time and the Endless season come from the same clock |
| Endless season | `runEndless(season, team)` | `EndlessSim.SeasonOf` of the server's day (the UTC week from Monday 00:00 UTC, `specs/28` section 5), accepted if it is the season of the server's day or of the day before (a commit landing just after Monday midnight). Rewards follow from the season, so a moved device clock pays nothing twice |

### 6.3 Debug commands

`debugGrantBook` and any later debug command are refused unless the player id is on the module's debug list (configuration, not code; kj's ids). An account that used one carries `head.debugUsed` for good; leaderboards and PvP leave it out.

## 7. Versions and deploys

- The module is built from the same commit as the site and compiles the same `IdleRng.Sim` and `Content/`. Every commit carries the client's content version; the module accepts only its own.
- `.github/workflows/pages.yml` deploys the module (`server/ugs/deploy.sh`, with a UGS service account as a GitHub secret: a one-time setup by kj, `server/ugs/README.md`), then the site. In the gap, `ContentStale` makes the client keep its pending commands and retry after a reload. **Built (step 4):** the `module` job tests the module and deploys it; the site job needs it. Without the secrets it logs a notice and skips, and the site deploys anyway (it has no server to talk to until kj adds them) **(agent, 2026-11-03)**. The module embeds `Content/*.json` of the same commit.
- After a reload onto new rules, the client rebuilds its state from the server's account plus the pending commands under the new engine. Commands that no longer apply are dropped: the player loses at most what was played since the last checkpoint.
- A run in progress that no longer replays under new rules is dropped on the server exactly as `specs/06` section 5 drops it on the device (the same load code), so both sides agree.
- A save schema bump needs no server step: stored parts are migrated by the device save's own steps on load and rewritten at the next commit (section 4, #127). A module older than the stored schema answers `ContentStale` and writes nothing.

## 8. Limits, cost and the options

| Limit | Value | Here |
|---|---|---|
| Cloud Save, per player per access class | 5 MB, 2000 keys | About 40 KB and 60 keys at a full Vault |
| Cloud Code call | 15 s; response 2 MiB | A commit re-applies in milliseconds; an open returns a few KB |
| Requests per player | 600 a minute (Cloud Code and Cloud Save each) | A commit is 1 call with 2 to 4 Cloud Save reads and writes |
| Cold start | 12 to 15 s (`specs/08` section 10.1) | `Open` waits up to 20 s with Try again; a scheduled keep-warm call once there are players (#109) |

Load at 10 k daily players, assuming about 30 commits and 2 opens a player a day: about 10 M Cloud Code calls a month, about 15 USD beyond the free tier at the October 2026 list price, plus Cloud Save's own meter. Re-check the pricing pages before launch.

### 8.1 Options: preventing abuse at the lowest call cost and latency

Measured (2026-11-01, the five bot policies, 200 runs each): a run is 52 to 97 commands in 12 cycles. Only **drills (30 to 44) and fights (4 to 12) roll**. Rests (0 to 16) and skill-point spends (16 to 23) roll nothing, except a spend that crosses a stat milestone, which draws a tree offer. Tree takes and discards (about 2) roll nothing.

The bound: a roll the player must not know before choosing can only reach them after the choice, so every choice that reveals a roll costs one server round trip. Calls can only go down by having fewer such choices, by making each call cheaper, or by hiding the wait.

| Option | Server calls a run | Wait per tap | Cost at 10 k daily players | Abuse prevented | Price |
|---|---|---|---|---|---|
| **A. Server plays every command** (the second draft of this spec, in git history) | about 90 | 100 to 200 ms (UGS: call plus a Cloud Save read and write) | about 200 to 300 USD a month | All: forged state, previewed rolls, re-rolls, clock | None beyond cost and latency |
| **A1. A, with commands that roll nothing run on the device** and sent with the next command that rolls | about 56 | 0 for rests and spends; as A for drills and fights | about 40% below A | All: the server re-applies the batched commands and refuses the batch if any is illegal | The client keeps the run without its seeds and must know which commands draw (`TrainingRun.Draws`) |
| **B. A or A1 on our own server** (hosted .NET, the run held in memory, one connection per player) | as A or A1 | 30 to 80 ms (no storage on the path; writes batched) | about 50 to 100 USD a month, flat | All | Operations: deploys, TLS, scaling, a database and backups, verifying UGS tokens. A crash loses unsaved taps (a player cannot trigger it, so no re-roll) |
| **B2. A or A1 on cheaper serverless** (AWS Lambda for .NET with DynamoDB) | as A or A1 | as A, plus cold starts | about 40 to 80 USD a month | All | A second vendor beside UGS sign-in; a storage write per tap |
| **C. Dealt hands** (game design): each cycle the server deals that cycle's drill rolls face up (for example 4 tier cards), the player assigns them to stats and rests, and the cycle-end call resolves the fight and deals the next hand | about 12 to 15 | 0 inside a cycle; one wait per cycle, covered by the fight animation | about 25 to 40 USD a month on UGS | All: what the player sees before choosing is meant to be seen; nothing beyond the hand is known | A change to the training run (`specs/14`): the roll is known before the choice, so decisions become placement (a dice-placement game) rather than risk. Milestone offers drawn at cycle end |
| **D. Plan the cycle** (game design): the player submits the cycle's actions, or rules for them (the T11 gambit rulebook), and one call plays the cycle | about 12 to 15 | 0 while planning; results at once | about 25 to 40 USD a month | All | Less reactive play: the player cannot answer a roll inside the cycle |
| **E. Client plays, server re-checks at checkpoints** (sections 4 and 5: **chosen**) | 1 to 5 | 0 | about 15 USD a month | Forged state, re-rolls, clock. **Not previewed rolls** | Chosen by kj (2026-11-01), replay accepted (section 8.2) |
| **F. E with the seed hidden in the client** (obfuscated WebAssembly) | as E | 0 | as E | As E against casual players; nothing against anyone who reads memory | Ruled out: a deterrent, not prevention |
| **G. E with statistical detection** (flag players whose rolls land improbably well for their choices) | as E | 0 | as E | Detects preview after the fact; does not prevent it | Ruled out as the main defence; worth keeping for ranked play later |

Applies to A, A1 and B whatever is chosen: **hide the wait** by starting the drill or fight animation when the tap is sent and showing the result when the answer lands (a 300 ms animation hides a 200 ms call); keep one connection open (the browser reuses it); a scheduled keep-warm call against UGS cold starts (#109).

**Decision (kj, 2026-11-01): option E.** The agent had recommended A1 (no preview at all, about 56 calls a run); kj judged E's measured replay gap good enough for now. A1 and C stay on file for when competitive play raises the stakes.

### 8.2 What option E checks, and how far it can be hardened

What the server re-checks in E (the client plays, then sends its commands at checkpoints): the chain (the commit starts from the server's last state, so nothing forks or rolls back), every command's legality, and the result, by re-applying the commands with the server's copy of the seeds and comparing the account hash. That stops forged numbers (shards, stats, monsters, books), illegal moves, edited saves, a client-chosen account seed, and clock tricks (the server's time bounds the seal time and the Dungeon day).

What it cannot see: anything the client tried and threw away. The client holds the run's seed from the first tap, so a script (or a player copying the saved game in the browser's developer tools) can play the same run several ways and send only the best. Rolls do not depend on the choice (a drill's tier comes from the stream's next draw whatever the stat), so this is not luck the server could spot: the rolls are honest, the choices are prescient.

Measured (2026-11-01, 5 species x 100 seeds): the same run played by each of the five bot policies, keeping the best grade.

| | Mean grade (E = 0 ... S = 5) | A or S |
|---|---|---|
| `balanced` | 1.86 | 0% |
| `chaser` (the best single policy) | 3.08 | 35% |
| Best of the 5 on the same seed | 3.53 | 56% |

Half a grade over the best honest play, and A or S in 56% of runs instead of 35%, from only five tries; a script trying more variations gains more. Hardening E, in order of strength:

| Hardening | Calls a run | What is left |
|---|---|---|
| E as is | 1 to 5 | Whole-run replay (above) |
| Commit the run's start (the server counts it, so a run cannot be restarted for a new seed) | +1 | Same: the run is replayed locally, not restarted |
| **Per-cycle seeds**: the server sends cycle k's seed only after accepting cycle k-1's commands, and plays the rank fight itself. Needs one-way seed derivation (a keyed function such as SipHash in place of SplitMix64 for deriving child seeds, since SplitMix64 is invertible), or a cycle seed would give back the run seed | about 12 to 15 | Replay inside one cycle (about 4 drills): placing that cycle's Supers on the best stat. Only scripted players get it |
| Statistical flags (Supers landing on the player's best stat more often than chance) | 0 | Detects, does not prevent; fuzzy for a single player |

Per-cycle seeds cost the same calls as **C (dealt hands)** and leave exactly the knowledge C gives everyone (the cycle's rolls) to cheaters only. So if calls must stay near 12 a run, C is the fair form of hardened E; if the game must stay as it is with no preview at all, it is A1. kj chose plain E (section 1, decision 2).

## 9. Threats left

- **Replay** (accepted, decision 2): a script plays a run several ways from its known seed and sends the best; about half a grade on average (section 8.2). Where it would matter is competitive play: when PvP or ranked rewards ship (T12), revisit with the measured gap, the per-cycle seeds of section 8.2, or statistical flags. The Endless Tower season board (`specs/28` section 6.1) ships with this risk accepted (kj, 2026-11-03).
- **Dungeon preview**: the daily seed comes from the public constant `TOWER_SEED` (`specs/21` section 2), so a player can try every team against today's or a future day's floors before spending a ticket. Part of the accepted replay risk; a server-secret daily seed (`PRF(secret, day)` sent only on that day) closes the future-day part cheaply if kj wants it.
- **Bots playing legal moves** are playing (`specs/10` section 1, non-goals).

## 10. Build order

Each step merges on its own with `./check.sh` green.

1. **Sim. Done (2026-11-01).** `VaultEntry.Id` and the pruned entry (save schema v9; the fighter built from the stored result, proved equal to the run's fighter for every bot policy, species and book setup after a save round trip). `AccountCommands` (`AccountCommand.Parse` by bridge name over a host's `ICommandArgs`, `Apply`, the declared `Reads` and `Writes` parts); the bridge only formats results. `AccountParts` in `IdleRng.Runtime` (`Split`, `Join`, part, entry and account hashes). Tests: every field in a part, split then join gives the same items and hash, a missing part or an edited monster is refused, and every command changes only the parts it writes and gives the same result when the parts it does not declare are swapped for another account's (a removed declaration makes it fail).
2. **Server core. Done (2026-11-02).** `src/IdleRng.Server` (netstandard2.1): `AccountServer.OpenAsync` and `CommitAsync` over `IAccountStore` (a write-locked `head`, items written once under fresh keys, a key listing for the sweep) with `MemoryAccountStore`; `AccountHead` (the root item and the key form of section 4); `JsonArgs` (command arguments read by the bridge's rules with Newtonsoft); `ServerConfig` (the command cap, the clock slack, the debug list, injectable clock and random source). The caller authenticates and passes the player id. `AccountParts.AccountHashOf` gives the account hash from part hashes. Tests (`src/IdleRng.Server.Tests`, in `check.sh` on .NET 8 through the solution, #28): genesis with a server seed and its lost race, a whole run committed at checkpoints ending equal to the client item by item, every account command in its own commit, a mid-run checkpoint reading and writing only `training`, every rejection (no account, stale base or hash, other content, a forged result, illegal and malformed commands with their index, the command cap, a second `finishRun`) leaving the store unchanged, the retry of the last commit, the holder rule with claim and takeover, the seal-time slack, the Dungeon day, the debug list and `debugUsed`, a crash before the swap and the sweep, a lost swap, a commit landing during another call's load, a damaged item, and a content change rewriting every part. Parts stored under an older save schema load through the save's migrations and are rewritten at the next commit; a newer schema is `ContentStale` with nothing written (#127, 2026-11-03).
3. **Client on the local test server. Done (2026-11-03).** `web/sync.js`: the pending log, checkpoints, the waits of section 5.2, open and claim, sign-out on `NotHolder`, the one-tab rule, the wipe of old saves, the server's time for the Dungeon day, the Endless season and the seal time (#21); the transport interface (`web/wasm/BRIDGE.md`, "Sync transport"); the server core in the page (section 5.4: `src/IdleRng.Web/LocalServer.cs`, `AccountHash` and `LocalServerCall` on the bridge); the account sheet shows the sync state, the header "Local test build". On the GitHub Pages address the page loads `web/ugs-transport.js` and syncs with the module; it never falls back to the local test server there, so until the module is deployed it stays on "Connecting..." with Try again. Browser checks (`web/wasm/test/sync.js`, in `check.sh`): a server page and two devices through a test transport, the wipe, checkpoints, the server's clock, a lost connection (the run plays on, the next account command waits, then resumes), takeover and sign-out, a refused commit reverting to the server's account, stale content keeping the moves across the reload, one tab, and the local test build. The other browser checks seed accounts through the local test server's `install`.
4. **Module and deploy. Done (2026-11-03); the page uses it on Pages since step 3.** `server/ugs/MonStackAccount` (net10.0), a thin shell over `IdleRng.Server`: endpoints `Open` (the account gzip then base64), `Commit`, `EndlessBoard`, `GauntletBoard` (`specs/19` section 9), `Profile` (the season board as the caller sees it; a negative season is the server's) and `Warm` (the keep-warm target, #109); the player is the Cloud Code context's, never a request field. `CloudSaveAccountStore`: private player items under the core's keys, `head` replaced only under its Cloud Save write lock. Cloud Save has no create-if-absent write, so creating `head` checks it is absent, writes without a lock and reads it back, keeping the write only if the lock is still its own; two first opens inside one round trip can both pass, the later replacing the earlier, whose device then gets `Stale` or `NotHolder` on a fresh account with no commit lost **(agent, 2026-11-03, kj can veto)**. `CloudSaveLeaderboardStore` (section 3.3) and `CloudSaveProfileStore` (the protected player item `profile`, stored as a JSON object so it can be indexed later, section 3.3.2), read by the `Profile` endpoint (the caller's own, or another player's by id). The debug list (section 6.3) is built into the module from `MONSTACK_DEBUG_PLAYERS` (a GitHub Actions variable), not read at run time **(agent, 2026-11-03: no extra call per request; a change needs a redeploy; kj can veto)**. `server/ugs/deploy.sh [Module] [--check]`; the `module` job in `pages.yml` before the site (section 7). `web/ugs-transport.js`: `{ open, commit, endlessBoard, profile }` over the Cloud Code REST API with the token from `web/auth.js`, the account unpacked to text, verdicts returned and network, timeout, 401, 429 and 5xx thrown as retryable; loaded by `index.html`; `web/sync.js` takes it as its transport on Pages (step 3). Tests (#28): `server/ugs/MonStackAccount.Tests` (.NET 10) runs the handlers in process over an in-memory Cloud Save with its write-lock, key and batch rules: the head lock, a create overwritten before its read-back, chunked reads and batches, paged key listings, a lost swap (`Stale`, the loser's items deleted), a lost genesis, a crash before the swap and the sweep, open and commit through the module with every rejection as a code, the player from the context only, the debug list, the board written by a climb and read without player ids, the board store equal to `MemoryLeaderboardStore` over random operations and retrying a shard write it lost, the profile as a protected item read by its owner or by id, and the reply JSON. `web/wasm/test/ugs-transport.js` (Node). Both in `check.sh`; the module tests use a .NET 10 SDK kept apart from the default .NET 8 (`MONSTACK_DOTNET10`, else `~/.dotnet10/dotnet`; skipped locally without one, required in CI, which installs one beside .NET 8). Builds with .NET 10 go to `build/obj-net10` and `build/bin-net10` (`MonStackBuildSuffix`). Left: dev and prod environments and a tagged deploy to dev (#28), the keep-warm schedule (documented in `server/ugs/README.md`, not enabled, #109), and a first remote run once the secret is in.
5. **Projections:** `profile`, then Leaderboards for the Dungeon. The Endless Tower season board and display names are built in the core ahead of the rest (kj, 2026-11-03; `specs/28` section 6.1): `ILeaderboardStore` with `MemoryLeaderboardStore`, `EndlessBoard`, and the board written by `CommitAsync`; step 4 added the Cloud Save store and the read endpoint. **`profile` built in the core (2026-11-03, section 3.3.2):** `PlayerProfile`, `IProfileStore` with `MemoryProfileStore`, `Profiles`, written by `CommitAsync` (tests in `ProfileTests`: the server's profile equals the client's, a commit that does not declare `progress` still shows the name, a refused commit writes nothing); step 4 added the Cloud Save store and the `Profile` endpoint. The Dungeon board waits on kj (`specs/21` section 11 question 8).

## 11. kj's answers (2026-11-01)

1. Accounts from before the server: **wiped** (decision 9).
2. Debug controls on the live game: **yes**, only for listed players (section 6.3).
3. Look-ahead: first "rolls done by the server and not previewable"; after comparing the options (section 8), **option E, accepting replay** as "good enough" (decision 2).
4. Offline play: **no** (decision 3).

Left for kj, none blocking: the local test build for the artifact (section 5.4), whether to close the Dungeon's future-day preview (section 9), the showcase rule (section 3.3.2). Blocking the Dungeon board (step 5): what it ranks (`specs/21` section 11 question 8).
