# 27 - Server Authority: the Account Data Model and Checkpoints

> **Status: design, not built** (kj, 2026-11-01). kj chose a server-held account over a client-uploaded cloud save, answered this spec's questions, then compared every way of preventing abuse against server calls and latency (section 8) and chose **option E**: the client plays, the server re-checks at checkpoints, and the remaining risk (a script replaying a run to keep its best outcome, measured in section 8.2) is accepted. The draft where the server plays every command is in git history. Supersedes cloud save as a client upload (`specs/08` section 5.2) and, for the trainer, the idle game's segment certification (`specs/10` section 4). Issues: goal #15, #25 (cloud save), #19 (accounts), #21 (server time), #33 (save trust fields), #28 (server tests and deploy).

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

Pruned as in section 3.5, the Vault is about 37 KB and the whole account under 50 KB.

## 3. The data model

### 3.1 Parts

All parts are Cloud Save player items in the **private** access class: only the module (its service token) reads or writes them; the client gets its account through the module (section 5.1).

| Part | Holds | Bound | Written by |
|---|---|---|---|
| `head` | The root: `seq`, the account hash, each part's key and hash, the holder device, schema and content version, `debugUsed` | Fixed, under 2 KB | Every commit |
| `training` | `Seed`, `RunsStarted`, `TalentsOff` (the next run's loadout), the run in progress as its replay recipe, and the post-run vendor while it is open | One run's log (about 100 commands), or `VENDOR_OFFERS` offers | `startRun`, every run command, `finishRun` (opens the vendor), `abandon`, `setLoadout`, `buyOffer`, `rerollVendor`, `closeVendor` |
| `wallet` | `Shards`, `Books` (later any premium currency, section 3.4) | Content size | `startRun` (books leave), `finishRun`, `abandon`, `buyOffer`, `rerollVendor`, `dismantle`, `runTower` |
| `collection` | `OwnedSpecies`, `Talents` (ranks) | Content size | `finishRun` (unlocks), `buyOffer` |
| `progress` | `Achievements`, `Records` | Content size | `finishRun` |
| `dungeon` | `Tower`: tickets, pieces, grant day, attempts, bests | Fixed | `runTower`, `finishRun` (pieces), the daily grant |
| `vault` | The Vault index: entry ids in Vault order, the locked ids, each entry's hash | `VAULT_CAP` ids | `finishRun`, `dismantle`, `lock` |
| `mon.<id>` | One sealed monster as its result: species, seed, grade, rank, end, cycles, fights, final stats, trees (bought nodes, rolls, fusions), books used, gauntlet floor, killer and title, the run and gauntlet hashes, seal time | One per entry, about 0.75 KB | `finishRun` creates; `dismantle` deletes; never changed |

Rules behind the split:
- **By writer set.** Each part changes for one reason. A run command touches only `training`; a vendor buy touches `wallet`, `collection` and `training`. A commit rewrites only the parts its commands changed, so a checkpoint in the middle of a run writes `training` and `head`, a few KB.
- **Immutable things are their own items.** A sealed monster never changes (`specs/18` section 6), so it is written once. `Locked` is the player's flag, not part of the monster (it is outside `RunHash` already), so it lives in the `vault` index.
- **A part is bounded by content or capped by a rule.** New fields join the part whose writer set they share; a field that would grow without a bound goes to section 3.4.

Vault entry ids: an entry is named by the index of the run that made it (`RunsStarted` when it started), unique in the account. The sim gains `VaultEntry.Id`; accounts are wiped (decision 9), so no migration is needed. Commands keep addressing entries by Vault position, which is deterministic in a replay.

### 3.2 Hashes

- **Part hash**: SHA-256 of the part's canonical binary encoding (`Replay/Canonical.cs`, as `TrainingRun.StateHash` and `BattleSim.StateHash` do: no JSON text, raw double bits), shared code in `IdleRng.Sim` (`AccountHash`).
- **Entry hash**: SHA-256 over the entry's fields, excluding `Locked`. The `vault` part hash covers the ids, the locked set and every entry hash, so it pins every entry without loading them.
- **Account hash**: SHA-256 over the sorted `(part name, part hash)` pairs. The client computes it after its last pending command and sends it with the commit; the server computes it after re-applying them. Equal hashes mean equal accounts, with no field list to keep in step.

Because the `vault` part pins entries by hash, the server loads only the entries a commit's commands read, not the whole Vault.

### 3.3 Projections (read by others, never the truth)

Written by the module after an accepted commit, rebuildable from the parts, allowed to lag, never read back by a commit:

| Projection | Where | Holds | For |
|---|---|---|---|
| `profile` | Cloud Save **public** player item | Display name (later), best grade, best Dungeon floor, achievement count, the showcase monster's result | Other players, profile cards |
| Leaderboards | UGS Leaderboards | Dungeon best floor per day, later PvP rating | Rankings |
| `pvp.team` | Cloud Save **protected** player item, indexed (T12) | The defending team: entry ids, results, the rating bracket | Matchmaking across players (`specs/10` section 9) |

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
| The run in progress | Until it seals or is abandoned | Its recipe (seed and log, about 90 commands) in `training`; cleared at seal |
| The gauntlet floors | Never stored | The wheel replays them from the fighter and seed (`GauntletSim.Run`) and checks `GauntletHash` |
| Dungeon attempts | Never stored | A result is a pure function of the team, day and content (`specs/21` section 1); only rewards and bests are kept in `dungeon` |
| The post-run vendor | Only while its screen is open | Not available between runs (kj, 2026-11-01, `specs/18` section 4); while open it sits in `training` |

Kept although derivable: the owned species (from the achievements), so a later content change cannot take a species away.

What this gives up: a sealed monster can no longer be replayed step by step (balance questions use the bot CLI, `specs/09`). Built from its stored result, a sealed monster keeps the stats it was sealed with under later rule changes, as `specs/12` section 6 intends; skills, triggers and rules are still read from content by node id, so a skill's tuning applies to old monsters, and a removed node id is still a content MAJOR (`E090`).

## 4. Commits

A commit carries the commands since the last accepted one. Commands are the bridge's own (`web/wasm/BRIDGE.md`): account commands `startRun`, `finishRun`, `abandon`, `buyOffer`, `rerollVendor`, `closeVendor`, `setLoadout`, `dismantle`, `lock`, `runTower`, and run commands `drill`, `rest`, `fight`, `take`, `discard`, `reroll`, `spend`, `book`, each `{ k, a }` with the arguments as JSON. Read-only calls (`gauntlet`, `towerInfo`, `towerFloor`) are not commands. The switch that applies them moves from `IdleRng.Web.RunHost` into `IdleRng.Sim.Account.AccountCommands.Apply(content, account, command)`, so the browser, the module and the tests run one dispatcher. Each command declares the parts it reads and writes (`AccountCommands.Touches`), checked by a test that applies every command with the other parts absent.

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

- **Copy-on-write.** Part keys carry the commit that wrote them (`wallet.000124`). New versions are written first; nothing refers to them until `head` swaps, and `head` is one item, so a commit lands whole or not at all. A crash between leaves unreferenced keys, deleted by the next commit or open. New `mon.<id>` items are written before `head` the same way.
- **Idempotent.** A commit that repeats an accepted one (same `fromSeq` and `toHash`, `head` already at `seq + 1` with that hash) returns the stored `Accepted`, so a retry after a lost answer is safe.
- **Size.** A commit holds at most `COMMIT_MAX_COMMANDS` (placeholder 500) and at most one `finishRun`. It writes at most the 6 parts below `head`, `head` itself and one new entry.
- **Cost of a check.** Re-applying a whole run is about 90 commands, each rank fight a `BattleSim.Resolve` (0.1 to 1.3 ms in the spike); a `runTower` replays up to the Dungeon's floors. Milliseconds, inside Cloud Code's 15 s limit.

| Rejection | Meaning | Client does |
|---|---|---|
| `NotHolder` | Another device took the account | Signs out (section 6.1) |
| `Stale` | The server is past `fromSeq` (a second tab, or a restored old copy) | Opens the account again (section 5.1) and drops its pending commands |
| `ContentStale` | Client and module run different rules or content | "A new version is out", reloads; the pending commands are re-applied by the new build (section 7) |
| `Mismatch`, `Refused` | A command was refused, or the result differs (an edited client or save, or a determinism bug) | Reverts to the server's account, reports the first differing command for diagnosis. Never a ban (`specs/10` section 4.6) |
| Network, timeout, 429, 5xx | Not a verdict | Keeps the pending commands; retries with backoff; play waits as in section 5.2 |

## 5. The client

### 5.1 Open

`Open { deviceId, claim }` returns the account (all parts and every `mon.*` item, assembled by the module into the `AccountSave` text the engine loads, gzip then base64: a few KB), `seq`, `hash`, the holder, the server time and day.

- No `head` yet: the module creates the account (**genesis**): it draws the account seed, so seeds are never client-chosen (`specs/10` section 3), and writes the parts at `seq` 0.
- `claim: true` makes this device the holder. The client claims on a Google sign-in and on a guest's first open; an ordinary page open does not claim, so a device signed out by another one cannot take the account back without signing in again.
- **The game waits for it** (decision 3): "Connecting..." until `Open` answers, with a 20 s timeout (above a cold Cloud Code worker's 12 to 15 s, `specs/08` section 10.1) and Try again. If the server's `seq` equals the device's base, the device keeps its state and commits its pending commands; otherwise the server's account replaces the device's copy and the pending commands are dropped.

### 5.2 Checkpoints (when the client commits)

No timer. The client commits:
- after a command that changes something besides the run in progress: `startRun` (so the server counts the run before it is played), `finishRun`, `abandon`, `buyOffer`, `rerollVendor`, `closeVendor`, `dismantle`, `lock`, `runTower`, `setLoadout`;
- when the page is hidden (`visibilitychange`), so the run in progress continues on another device;
- when `CHECKPOINT_RUN_COMMANDS` (placeholder 25) run commands are pending;
- on open, if commands are pending.

One commit is in flight at a time. Taps within a run do not wait for it. **Play waits** ("Connecting...") when a commit cannot land and either an account-level command is next (its commit is the checkpoint) or the pending run commands reach `CHECKPOINT_RUN_COMMANDS` twice over: so at most about 50 taps of a run are ever unchecked, and nothing that changes the account outside the run happens before the server has it.

### 5.3 What the device keeps

| Key | Holds |
|---|---|
| `idle-rng-account-v1` (existing) | The `AccountSave` text the engine loads: the server's account plus the pending commands applied. A cache, replaced by every `Open` |
| `monstack-sync-v1` (new) | `deviceId` (random, made once), `baseSeq`, `baseHash`, the pending commands |
| `monstack-auth-v1` (existing) | The session (`web/auth.js`) |

On the first open of this build the device's old account (`idle-rng-account-v1` from before the server, its `-unloadable` backups and the older keys) is deleted before the server account is loaded (decision 9). Only one tab plays: a second tab sees the first through a `BroadcastChannel` and says the game is open elsewhere; a second tab that commits anyway gets `Stale`.

### 5.4 The local test server

`check.sh`, `./play.sh` and kj's phone artifact (which cannot rely on reaching UGS) run the same server code inside the page: `IdleRng.Server` compiled into the WebAssembly build with an in-memory store kept in `localStorage`, behind the same `Open` and `Commit` calls. The page uses it when served from anything but the GitHub Pages address and says "Local test build" in the header. It has no anti-cheat value **(agent: keeps the browser checks and the phone artifact working without a network; kj can veto)**.

## 6. Rules the server enforces

### 6.1 One holder

`head.holder` is `{ deviceId, sinceSec }`. A commit or open from another device returns `NotHolder`. That device's client forgets its session (`MonStackAuth.signOut`), drops its pending commands, starts a new guest (a new account by genesis) and says "Signed out: your account was opened on another device. Sign in to continue here." Commands it played after the takeover are not applied (kj's rule: the newest sign-in wins).

### 6.2 Inputs the client supplies

Commands are choices, except two values from the device:

| Input | Command | Server rule |
|---|---|---|
| Seal time | `finishRun(sealedUtcMs)` | Display only. Accepted if not after the server's time plus `CLOCK_SLACK_SEC` (placeholder 300); else `Refused` |
| Dungeon day | `runTower(day, team)` | The day the server gave the client at its last `Open` or commit (the device clock is not used), accepted if it is the server's day or the day before (a commit landing just after midnight). The daily free ticket follows from the day, so a moved device clock grants nothing (#21) |

### 6.3 Debug commands

`debugGrantBook` and any later debug command are refused unless the player id is on the module's debug list (configuration, not code; kj's ids). An account that used one carries `head.debugUsed` for good; leaderboards and PvP leave it out.

## 7. Versions and deploys

- The module is built from the same commit as the site and compiles the same `IdleRng.Sim` and `Content/`. Every commit carries the client's content version; the module accepts only its own.
- `.github/workflows/pages.yml` deploys the module (`server/ugs/deploy.sh`, with a UGS service account as a GitHub secret: a one-time setup by kj), then the site. In the gap, `ContentStale` makes the client keep its pending commands and retry after a reload.
- After a reload onto new rules, the client rebuilds its state from the server's account plus the pending commands under the new engine. Commands that no longer apply are dropped: the player loses at most what was played since the last checkpoint.
- A run in progress that no longer replays under new rules is dropped on the server exactly as `specs/06` section 5 drops it on the device (the same load code), so both sides agree.

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

- **Replay** (accepted, decision 2): a script plays a run several ways from its known seed and sends the best; about half a grade on average (section 8.2). Where it would matter is competitive play: when PvP or leaderboards ship (T12), revisit with the measured gap, the per-cycle seeds of section 8.2, or statistical flags.
- **Dungeon preview**: the daily seed comes from the public constant `TOWER_SEED` (`specs/21` section 2), so a player can try every team against today's or a future day's floors before spending a ticket. Part of the accepted replay risk; a server-secret daily seed (`PRF(secret, day)` sent only on that day) closes the future-day part cheaply if kj wants it.
- **Bots playing legal moves** are playing (`specs/10` section 1, non-goals).

## 10. Build order

Each step merges on its own with `./check.sh` green.

1. **Sim.** `AccountCommands.Apply` and `Touches` (the bridge's switch moved), `AccountHash` (parts, entries, account), `VaultEntry.Id` and the pruned entry (`VaultEntry.Fighter` from the stored result, with a test that it equals the replayed fighter for every bot policy and species before the log goes; `specs/06` section 5 and `specs/18` section 6 updated then), the part split in `IdleRng.Runtime` (`AccountParts.Split` and `Join`); tests: declared touches, split then join round trips, hashes equal across a split.
2. **Server core.** A netstandard2.1 library `IdleRng.Server` with `Open` and `Commit` against an `IAccountStore` (the parts, write-locked `head`) and an in-memory store; tests for every rejection, copy-on-write cleanup, idempotent retries, the holder rule, the input rules, the debug list. In `check.sh` on .NET 8 (#28).
3. **Client on the local test server.** `web/sync.js`: the pending log, checkpoints, the waits of section 5.2, open and claim, sign-out on `NotHolder`, the one-tab rule, the wipe of old saves; the server core in the page (section 5.4); the account sheet shows the sync state. Browser checks for two devices, takeover, a lost connection, a rejection and stale content, in `check.sh`.
4. **Module and deploy.** `server/ugs/MonStackAccount`, a net10.0 shell over `IdleRng.Server` with a Cloud Save store; the deploy step in `pages.yml` (kj adds the secret); the page uses it on Pages.
5. **Projections:** `profile`, then Leaderboards for the Dungeon.

## 11. kj's answers (2026-11-01)

1. Accounts from before the server: **wiped** (decision 9).
2. Debug controls on the live game: **yes**, only for listed players (section 6.3).
3. Look-ahead: first "rolls done by the server and not previewable"; after comparing the options (section 8), **option E, accepting replay** as "good enough" (decision 2).
4. Offline play: **no** (decision 3).

Left for kj, none blocking: the local test build for the artifact (section 5.4), and whether to close the Dungeon's future-day preview (section 9).
