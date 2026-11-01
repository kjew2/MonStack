# 27 - Server Authority: the Account Data Model and Checkpoints

> **Status: design, not built** (kj, 2026-11-01: "option 2", the server owns the account; this spec first, for review). Supersedes cloud save as a client upload (`specs/08` section 5.2) and, for the trainer, the idle game's segment certification (`specs/10` section 4). Issues: goal #15, #25 (cloud save), #19 (accounts), #21 (server time), #33 (save trust fields, folded into #25), #28 (server tests and deploy).

Scope: how the player's account is modelled and stored on the server, how the client sends what it did, and how the server checks it. PvP battle calls stay in `specs/08` section 10.3; purchases in section 6.

## 1. Decisions

1. **The server holds the account.** The client keeps playing on its own engine with no wait (offline included), records the commands it applied, and sends them at checkpoints. The server re-applies them with the same `IdleRng.Sim` code (a Cloud Code C# module, `specs/08` section 10.1) and accepts the result only if its state hash matches the client's. The server never trusts a value the client computed; it trusts only the commands, which are player choices (`specs/00` decisions log item 3: verification, not a server sim of every tick).
2. **One device holds the account at a time** (kj, 2026-11-01: "log out the first client instead"). Signing in on a device takes the account; the device that held it is signed out at its next server call. There is no conflict prompt and no merge.
3. **The data is partitioned by domain**, not stored as one save blob (kj, 2026-11-01). Each part is its own item with its own hash; the account hash is a hash of the part hashes. Section 3.
4. **Commits are copy-on-write under one root item.** New part versions are written under new keys, then the `head` item is swapped with a write lock; that swap is the commit (section 4). It does not depend on multi-item atomicity.
5. **Store results, not history** (kj, 2026-11-01: "prune the model"). A sealed monster is kept as what it became (stats, trees, gauntlet result), not as the commands that made it; commands are kept only until the server accepts them, and the run in progress only until it seals. Section 3.5.
6. **Unbounded data never lives in a part.** Anything that grows without a bound (match history, purchase records) is one item per record with a retention rule, or lives in a service built for it (section 3.4).

## 2. Measurements (2026-11-01, content 5.x)

An account after 50 sealed runs (the `balanced` bot, the starter species), compact JSON:

| Field | Bytes | Grows with |
|---|---|---|
| `Vault` (50 entries) | 334,549 | Sealed runs, capped by `VAULT_CAP` (50; expansions planned, `specs/18` section 7) |
| One Vault entry | about 6,700 | Fixed per entry: its `Log` (the replay recipe, about 5,500) is 80% of it; `Mods` about 400; the summary the Vault screen shows (grade, stats, trees, gauntlet, title, hashes) about 800 |
| `Records`, `Vendor`, `Tower`, `Achievements` | 164, 318, 104, 30 | Content size (achievements, stats) |
| `OwnedSpecies`, `Books`, `Talents`, `TalentsOff`, seed, counters | under 100 together | Content size |
| A mid-run save (whole account, one run in progress) | 12,760 (indented) | The run's log: about 90 commands for a whole run |

The whole save is 784 KB indented, 21 KB gzipped. Nothing outside the Vault is large today; the reason to partition is what each part does (who writes it, how often, who reads it) and what future features add, not today's bytes.

## 3. The data model

### 3.1 Parts

All parts are Cloud Save player items in the **private** access class: only the server (the module's service token) reads or writes them; the client reads its account through the module (section 5).

| Part | Holds | Bound | Written by |
|---|---|---|---|
| `head` | The root: `seq`, the account hash, each part's key and hash, the holder device, lineage, schema and content version, the last server day the account saw (section 6.3) | Fixed, under 2 KB | Every commit |
| `training` | `Seed`, `RunsStarted`, `TalentsOff` (the next run's loadout), the run in progress as its replay recipe, and the post-run vendor while it is open | One run's log (about 100 commands), or `VENDOR_OFFERS` offers | `startRun`, every run command, `finishRun` (opens the vendor), `abandon`, `setLoadout`, `buyOffer`, `rerollVendor`, `closeVendor` |
| `wallet` | `Shards`, `Books` (later any premium currency, section 3.4) | Content size | `startRun` (books leave), `finishRun`, `abandon`, `buyOffer`, `rerollVendor`, `dismantle`, `runTower` |
| `collection` | `OwnedSpecies`, `Talents` (ranks) | Content size | `finishRun` (unlocks), `buyOffer` |
| `progress` | `Achievements`, `Records` | Content size | `finishRun` |
| `dungeon` | `Tower`: tickets, pieces, grant day, attempts, bests | Fixed | `runTower`, `finishRun` (pieces), the daily grant |
| `vault` | The Vault index: entry ids in Vault order, the locked ids, each entry's hash | `VAULT_CAP` ids | `finishRun`, `dismantle`, `lock` |
| `mon.<id>` | One sealed monster as its result: species, seed, grade, rank, end, cycles, fights, final stats, trees (bought nodes, rolls, fusions), books used, gauntlet floor, killer and title, the run and gauntlet hashes, seal time. No replay log (section 3.5) | One per entry, about 0.75 KB | `finishRun` creates; `dismantle` deletes; never changed |

Rules behind the split:
- **By writer set.** Each part changes for one reason. A run command touches only `training`; a vendor buy touches `wallet`, `collection` and `training`. A commit rewrites only the parts its commands changed (section 4), so a checkpoint in the middle of a run writes `training` and `head`, a few KB.
- **Immutable things are their own items.** A sealed monster never changes (`specs/18` section 6), so it is written once and never rewritten. `Locked` is the player's flag, not part of the monster (it is outside `RunHash` already), so it moves to the `vault` index and the entry items stay immutable.
- **A part is either bounded by content or capped by a rule.** New fields join the part whose writer set they share; a field that would grow without a bound goes to section 3.4 instead.

Vault entry ids: an entry is named by the index of the run that made it (`RunsStarted` when it started), which is unique in the account. The sim gains `VaultEntry.Id` (save schema v9); the migration finds each existing entry's index from its seed (`AccountOps.RunSeed(Seed, i) == entry.Seed` for `i < RunsStarted`). Commands keep addressing entries by Vault position, which is deterministic in a replay.

### 3.2 Hashes

- **Part hash**: SHA-256 of the part's canonical binary encoding (`Replay/Canonical.cs`, as `TrainingRun.StateHash` and `BattleSim.StateHash` do: no JSON text, raw double bits), computed by shared code in `IdleRng.Sim` (`AccountHash.Part(name, state)`).
- **Entry hash**: SHA-256 over the entry's fields, excluding `Locked`. The `vault` part hash covers the ids, the locked set and every entry hash, so it pins every entry without loading them.
- **Account hash**: SHA-256 over the sorted `(part name, part hash)` pairs. The client computes it after its last pending command and sends it with the commit; the server computes it after re-applying them. Equal hashes mean equal accounts, with no field list to keep in step.

Because the `vault` part pins entries by hash, the server loads only the entries a commit's commands read (section 4), not the whole Vault. The account stays cheap to check as the Vault grows.

### 3.3 Projections (read by others, never the truth)

Written by the module after an accepted commit, rebuildable from the parts at any time, allowed to lag, never read back by a commit:

| Projection | Where | Holds | For |
|---|---|---|---|
| `profile` | Cloud Save **public** player item | Display name (later), best grade, best Dungeon floor, achievement count, the showcase monster's summary | Other players, profile cards |
| Leaderboards | UGS Leaderboards | Dungeon best floor per day, later PvP rating | Rankings (cross-player reads belong in a service built for them) |
| `pvp.team` | Cloud Save **protected** player item, indexed (T12) | The defending team: entry ids, summaries, the rating bracket | Matchmaking queries across players (`specs/10` section 9) |

### 3.4 Data that grows without a bound

None exists yet. When a feature adds it:

| Kind | Model | Retention |
|---|---|---|
| PvP match history (T12) | One item per match, `match.<seq>`, written by the battle call | Keep the last `PVP_HISTORY_KEEP` (placeholder 50), delete older |
| Purchases and grants (T13) | One record per transaction id, server-side, idempotent (`specs/08` section 6) | Kept (refunds, chargebacks) |
| Premium currency (if it ships) | UGS Economy balances, or a `wallet` field credited only by the purchase handler | |
| Commit journal (audit, desync reports) | Analytics events (`specs/08` section 8), not Cloud Save | The analytics provider's |
| Seasons (T12) | `season.<n>` per season, frozen when it ends | Kept |

Cloud Save allows 2000 keys per player per access class; at `VAULT_CAP` 50 the parts use about 60.

### 3.5 What is not kept

Measured on the 50-run account of section 2: the Vault drops from about 335 KB to about 37 KB, and the whole account from 784 KB (indented) to under 50 KB.

| Data | Kept | Why it can go |
|---|---|---|
| A sealed monster's replay log (`Log`, about 5.5 KB of its 6.7 KB), `Mods` and `Ladder` | No | Only `VaultEntry.Fighter` reads them, and only to rebuild the final stats and bought nodes the entry already stores (`TrainingRun.MonsterFighter` takes nothing else; the entry's trees are stored in the same order, absorbed trees before their slot). The fighter is built from the entry instead. Re-checking a sealed run is not needed: the server re-applied every command before it sealed (section 6.2) |
| Commands | Until accepted | A commit's commands are applied, then dropped. A sample may go to analytics for desync reports (`specs/08` section 8) |
| The run in progress | Until it seals or is abandoned | Stored as its recipe (seed and log, about 90 commands at most) in `training`, because the server continues it from there; cleared at seal |
| The gauntlet floors | Never stored | The wheel replays them from the fighter and seed (`GauntletSim.Run`) and checks `GauntletHash` |
| Dungeon attempts | Never stored | A result is a pure function of the team, day and content (`specs/21` section 1); only its rewards and bests are kept in `dungeon` |
| The post-run vendor | Only while its screen is open | kj (2026-11-01): the vendor exists from sealing a run until the player leaves its screen (`closeVendor`, `specs/18` section 4); it is not available between runs, so it needs no part of its own. While open it sits in `training` (a few hundred bytes) |

Kept although derivable: the owned species (from the achievements). A few bytes, and storing them means a later content change cannot take a species away.

What this gives up: a sealed monster can no longer be replayed run step by run step (to debug a balance question, use the bot CLI, `specs/09`), and an imported account's monsters can never be checked (question 1). It also changes how an old monster survives a rules change: today `Fighter` replays the log under the current rules, so a drill retune would quietly change a sealed monster's stats in battle; built from the stored result, a sealed monster keeps the stats it was sealed with, as `specs/12` section 6 intends ("a Vault entry keeps final stats"). Skills, triggers and rules are still read from content by node id, so a skill's tuning applies to old monsters, and a removed node id is still a content MAJOR (`E090`).

## 4. Commits

A commit carries the commands since the last accepted one. Commands are the bridge's own (`web/wasm/BRIDGE.md`): account commands `startRun`, `finishRun`, `abandon`, `buyOffer`, `rerollVendor`, `closeVendor`, `setLoadout`, `dismantle`, `lock`, `runTower`, and run commands `drill`, `rest`, `fight`, `take`, `discard`, `reroll`, `spend`, `book`, each `{ k, a }` with the arguments as JSON. Read-only calls (`gauntlet`, `towerInfo`, `towerFloor`) are not commands. The switch that applies them moves from `IdleRng.Web.RunHost` into `IdleRng.Sim.Account.AccountCommands.Apply(content, account, command)`, so the browser, the module and the tests run one dispatcher. Each command also declares the parts it reads and writes (`AccountCommands.Touches`), checked by a test that applies every command against an account where the other parts are absent.

```
client                                              module (Cloud Code)
  apply command locally, append to pending
  at a checkpoint (section 5.2):
  Commit { deviceId, fromSeq, fromHash, commands, toHash, contentHash, build } ->
                                                    read head (with its write lock)
                                                    holder == deviceId?           else NotHolder
                                                    seq == fromSeq, hash == fromHash?  else Stale
                                                    contentHash == the module's?  else ContentStale
                                                    read the parts the commands touch, and the
                                                      mon items they reference
                                                    apply each command (AccountCommands.Apply),
                                                      with the server's inputs (section 6.3)
                                                    account hash == toHash?        else Mismatch
                                                    write changed parts under new keys
                                                    write head (seq+1, new keys and hashes) with its write lock
                                                    delete the superseded keys; write projections
  <- Accepted { seq, hash, serverSec, day }  |  Rejected { code, index, reason }
```

- **Copy-on-write.** Part keys carry the commit that wrote them (`wallet.000124`). The new versions are written first; nothing refers to them until `head` swaps, and `head` is one item, so a commit either lands whole or not at all. A crash after the part writes leaves unreferenced keys; the next commit or open deletes keys `head` does not name. New `mon.<id>` items are written before `head` the same way.
- **Idempotent.** A commit that repeats an accepted one (same `fromSeq` and `toHash`, `head` already at `seq + 1` with that hash) returns the stored `Accepted`, so a retry after a lost answer is safe.
- **Size.** A commit holds at most `COMMIT_MAX_COMMANDS` (placeholder 500) and at most one `finishRun`; the client splits a longer offline backlog at seals. A commit then writes at most the 6 parts below `head`, `head` itself and one new entry.
- **Cost of a check.** Re-applying a whole run is about 90 commands, with each rank fight a `BattleSim.Resolve` (0.1 to 1.3 ms in the spike); a `runTower` replays up to the Dungeon's floors. Milliseconds, well inside Cloud Code's 15 s call limit.

| Rejection | Meaning | Client does |
|---|---|---|
| `NotHolder` | Another device took the account | Signs out (section 6.1) |
| `Stale` | The server is past `fromSeq` (a second tab, or the device restored an old copy) | Opens the account again (section 5.1) and drops its pending commands, keeping its local save aside |
| `ContentStale` | Client and module run different rules or content | Shows "A new version is out", reloads; the pending commands are re-applied by the new build (section 7) |
| `Mismatch`, `Refused` | A command was refused, or the result differs (an edited client or save, or a determinism bug) | Reverts to the server's account, keeps the local save aside (`specs/06` section 5), reports the first differing command for diagnosis. Never a ban (`specs/10` section 4.6) |
| `Debug` | A debug command from an account not on the debug list (question 2) | As `Refused` |
| Network, timeout, 429, 5xx | Not a verdict | Keeps the pending commands; retries with backoff |

## 5. The client

### 5.1 Open

`Open { deviceId, claim }` returns the account (all parts and every `mon.*` item, assembled by the module into the `AccountSave` text the engine loads, gzip then base64: a few KB for a full Vault), `seq`, `hash`, the holder, the server time and day.

- No `head` yet: the module creates the account (**genesis**): it draws the account seed, so seeds are never client-chosen (`specs/10` section 3), and writes the parts at `seq` 0 with lineage `server`. Or, for a device that already has a local account from before this spec, imports it (question 1).
- `claim: true` makes this device the holder. The client claims on a Google sign-in and on a guest's first open; an ordinary page open does not claim, so a device that was signed out by another one cannot take the account back without signing in again.
- The page never waits for it: the game loads the local save at once (as now), and the open runs beside it with a 20 s timeout (above a cold Cloud Code worker's 12 to 15 s, `specs/08` section 10.1) and one retry. If the server's `seq` and hash equal the local base, the pending commands are committed; if the server is ahead, its account replaces the local one (the local save is kept aside) and the page reloads it.

### 5.2 Checkpoints (when the client commits)

No timer. The client commits:
- after a command that changes something besides the run in progress: `startRun`, `finishRun`, `abandon`, `buyOffer`, `rerollVendor`, `closeVendor`, `dismantle`, `lock`, `runTower`, `setLoadout`;
- when the page is hidden (`visibilitychange`), so the run in progress continues on another device;
- when `CHECKPOINT_RUN_COMMANDS` (placeholder 25) run commands are pending, so a long run is never far behind;
- on open, if commands are pending.

One commit is in flight at a time; commands applied meanwhile wait for the next. Run commands alone never start a commit before that count, so taps cost no network.

### 5.3 What the device keeps

| Key | Holds |
|---|---|
| `idle-rng-account-v1` (existing) | The `AccountSave` text the engine loads: the server's account plus the pending commands applied. An offline cache |
| `monstack-sync-v1` (new) | `deviceId` (random, made once), `baseSeq`, `baseHash`, the pending commands |
| `monstack-auth-v1` (existing) | The session (`web/auth.js`) |

Only one tab plays: a second tab sees the first through a `BroadcastChannel` and says the game is open elsewhere, so two tabs never apply commands on one base.

## 6. Rules the server enforces

### 6.1 One holder

`head.holder` is `{ deviceId, sinceSec }`. A commit or open from another device returns `NotHolder`. That device's client then forgets its session (`MonStackAuth.signOut`), keeps its local save and pending commands aside, starts a new guest (a fresh account by genesis), and says "Signed out: your account was opened on another device. Sign in to continue here." Commands it played after the takeover are not applied to the account (kj's rule: the newest sign-in wins, so nothing merges).

### 6.2 Lineage

`head.lineage` is `server` (genesis) or `imported` (question 1). PvP (T12) admits Vault entries of `server` accounts only, unless kj decides otherwise with `specs/08` open decision 3. Every entry of a `server` account was sealed by a server re-apply, so it needs no further check before PvP.

### 6.3 Inputs the client still supplies

Commands are choices, except two values from the device:

| Input | Command | Server rule |
|---|---|---|
| Seal time | `finishRun(sealedUtcMs)` | Display only. Accepted if not after the server's time plus `CLOCK_SLACK_SEC` (placeholder 300); else `Refused` |
| Dungeon day | `runTower(day, team)` | `day <= server day`, and `day <= head.lastDay + 1` (the offline bound of `specs/08` section 3, now enforced); `head.lastDay` moves to the server day on every accepted open or commit. The daily free ticket follows from the day, so a moved device clock grants nothing |

### 6.4 Debug commands

`debugGrantBook` and any later debug command are refused unless the player id is on the module's debug list (configuration, not code), and an account that used one carries `head.debugUsed` for good (question 2).

## 7. Versions and deploys

- The module is built from the same commit as the site and compiles the same `IdleRng.Sim` and `Content/`. Every commit carries the client's content hash and build; the module accepts only its own.
- `.github/workflows/pages.yml` deploys the module (`server/ugs/deploy.sh`, needing a UGS service account as GitHub secrets: a one-time setup by kj), then the site. During the minutes between, a client and the module can disagree; `ContentStale` makes the client keep its pending commands and retry after a reload.
- After a reload onto new rules, the client rebuilds its state from the server's account plus the pending commands under the new engine. Commands that no longer apply (a rule changed under them) are dropped and the local save is kept aside: the player loses at most what was played since the last checkpoint, which section 5.2 keeps short.
- A run in progress that no longer replays under new rules is dropped on the server exactly as `specs/06` section 5 drops it on the device (the same `AccountSave` load code), so both sides agree.

## 8. Limits and cost

| Limit | Value | Here |
|---|---|---|
| Cloud Save, per player per access class | 5 MB, 2000 keys | Full Vault: about 40 KB, about 60 keys; room for Vault expansions far past 50 |
| Cloud Code call | 15 s; response 2 MiB | A commit re-applies in milliseconds; an open returns a few KB |
| Cloud Code and Cloud Save rate | 600 requests per minute per player | A commit is 1 call; inside it 2 to 4 Cloud Save reads and 2 to 4 writes |
| Cold start | 12 to 15 s (`specs/08` section 10.1) | Client timeouts above 15 s with one retry; the game never waits |

Load at 10 k daily players, assuming 30 commits and 2 opens a player a day: about 10 M Cloud Code calls a month, about 15 USD beyond the free tier at the October 2026 list price (`specs/08` section 10.1), plus Cloud Save's own meter. Re-check the pricing pages before launch.

## 9. Threats left

- **Look-ahead.** The client holds the account seed and the whole engine, so a modified client can try a choice, see the roll, and undo it locally before sending (a drill's tier, a draft, a vendor reroll). The server sees only legal choices. Hiding the rolls means a server call per random event, which ends offline play. Accepted for now: it changes only the player's own progress; PvP fairness is weighed at T12 (question 3).
- **Bots playing legal moves** are playing (`specs/10` section 1, non-goals).
- **Imported accounts** were never checked (question 1).

## 10. Build order

Each step merges on its own with `./check.sh` green.

1. **Sim.** `AccountCommands.Apply` and `Touches` (the bridge's switch moved), `AccountHash` (parts, entries, account), `VaultEntry.Id` and the pruned entry (save schema v9: entries gain `Id` and lose `Log`, `Mods` and `Ladder`; `VaultEntry.Fighter` builds from the stored stats and trees, with a test that it equals the replayed fighter for every bot policy and species before the log is dropped; `specs/06` section 5 and `specs/18` section 6 updated then), the part split as a mapping in `IdleRng.Runtime` (`AccountParts.Split` and `Join`), tests: every command's declared touches, split then join round trips, hashes equal across a split.
2. **Server core.** A netstandard2.1 library `IdleRng.Server` with `Open` and `Commit` against an `IAccountStore` (the parts, write-locked `head`), and an in-memory store; tests for every rejection, copy-on-write cleanup, idempotent retries, the holder rule, the day bound. Runs in `check.sh` on .NET 8 (#28).
3. **Module.** `server/ugs/MonStackAccount`: a thin net10.0 shell over `IdleRng.Server` with a Cloud Save store; the deploy step in `pages.yml` (kj adds the UGS secrets).
4. **Client.** `web/sync.js`: the pending log, checkpoints, open and claim, sign-out on `NotHolder`, the one-tab rule; the account sheet shows the sync state. A browser check with a fake module (two devices, takeover, offline backlog, rejection, stale content), wired into `check.sh`.
5. **Server time** (#21): the day from the module, the bound in section 6.3.
6. **Projections**: `profile`, then Leaderboards for the Dungeon.

## 11. Questions for kj

1. **Accounts from before the server.** Recommendation: import each device's local account once, on its first open, with lineage `imported` (playable everywhere, kept out of PvP unless you decide otherwise). The alternative is a fresh account for everyone (a clean break, as at T7).
2. **Debug controls on the live game.** Recommendation: refused by the server except for player ids on a debug list (yours), and such accounts marked `debugUsed`.
3. **Look-ahead** (section 9). Recommendation: accept it until PvP shows a need.
4. **Offline play.** Recommendation: as designed, everything plays offline and is checked at the next checkpoint; a refusal rolls back to the last accepted state. The stricter choice is to need a connection to start or seal a run, which bounds what a refusal can undo.
