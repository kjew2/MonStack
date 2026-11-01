# 27 - Server Authority: the Server Plays Every Command

> **Status: design, not built** (kj, 2026-11-01). kj chose the server-held account over a client-uploaded cloud save, then answered this spec's questions: wipe existing accounts, debug only for listed players, **rolls done by the server and never previewable**, **no offline play**. Those answers make the server play every state change (section 1); the earlier draft, where the client played and the server re-checked at checkpoints, is in git history. Supersedes cloud save as a client upload (`specs/08` section 5.2) and, for the trainer, the idle game's segment certification (`specs/10` section 4). Issues: goal #15, #25 (cloud save), #19 (accounts), #21 (server time), #33 (save trust fields), #28 (server tests and deploy).

Scope: how the player's account is modelled and stored on the server, how the client asks the server to act, and how randomness stays hidden. PvP battle calls stay in `specs/08` section 10.3; purchases in section 6.

## 1. Decisions

1. **The server plays every state change** (kj, 2026-11-01: "the rolls should be done by the server and not previewable"). The client sends one command (a drill, a draft pick, a fight, a vendor buy, a Dungeon attempt); the server applies it with `IdleRng.Sim` in a Cloud Code C# module (`specs/08` section 10.1), stores the result and returns it; the client shows it. The client never computes an outcome. This replaces `specs/00` decisions log item 3 ("verification, not a server sim") for the trainer.
2. **Seeds never leave the server, and draws do not reveal them** (section 4). The account seed is drawn by the server; responses carry results (a fight's events for playback, the gauntlet floors), never a seed. The generator becomes one-way, because today's SplitMix64 is invertible: one exact draw gives back its stream's seed (section 4.1).
3. **Online only** (kj, 2026-11-01: no offline play). Without a connection the game shows "Connecting..." and retries; nothing is played on the device.
4. **One device holds the account at a time** (kj, 2026-11-01: "log out the first client instead"). Signing in takes the account; the device that held it is signed out at its next call. No conflict prompt, no merge.
5. **The data is partitioned by domain** (kj, 2026-11-01), each part its own item and hash, under one write-locked root item that makes a change atomic (section 3).
6. **Store results, not history** (kj, 2026-11-01: "prune the model"). A sealed monster is kept as what it became, not the commands that made it (section 3.5).
7. **Unbounded data never lives in a part** (section 3.4).
8. **Existing accounts are wiped** (kj, 2026-11-01). Every player starts a new server account; the device's local saves are deleted (section 5.4).
9. **Debug commands only for listed players** (kj, 2026-11-01), section 6.3.

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

All parts are Cloud Save player items in the **private** access class: only the module (its service token) reads or writes them. The client sees its account only as the views the module returns (section 5.1), with every seed removed.

| Part | Holds | Bound | Written by |
|---|---|---|---|
| `head` | The root: `seq`, each part's key and hash, the holder device, schema and content version, the last command's id and response (section 4.3), `debugUsed` | Fixed, a few KB | Every command |
| `training` | `Seed` (the account seed), `RunsStarted`, `TalentsOff` (the next run's loadout), the run in progress as its replay recipe, and the post-run vendor while it is open | One run's log (about 100 commands), or `VENDOR_OFFERS` offers | `startRun`, every run command, `finishRun` (opens the vendor), `abandon`, `setLoadout`, `buyOffer`, `rerollVendor`, `closeVendor` |
| `wallet` | `Shards`, `Books` (later any premium currency, section 3.4) | Content size | `startRun` (books leave), `finishRun`, `abandon`, `buyOffer`, `rerollVendor`, `dismantle`, `runTower` |
| `collection` | `OwnedSpecies`, `Talents` (ranks) | Content size | `finishRun` (unlocks), `buyOffer` |
| `progress` | `Achievements`, `Records` | Content size | `finishRun` |
| `dungeon` | `Tower`: tickets, pieces, grant day, attempts, bests | Fixed | `runTower`, `finishRun` (pieces), the daily grant |
| `vault` | The Vault index: entry ids in Vault order, the locked ids, each entry's hash | `VAULT_CAP` ids | `finishRun`, `dismantle`, `lock` |
| `mon.<id>` | One sealed monster as its result: species, seed, grade, rank, end, cycles, fights, final stats, trees (bought nodes, rolls, fusions), books used, gauntlet floor, killer and title, the run and gauntlet hashes, seal time | One per entry, about 0.75 KB | `finishRun` creates; `dismantle` deletes; never changed |

Rules behind the split:
- **By writer set.** Each part changes for one reason. A run command touches only `training`; a vendor buy touches `wallet`, `collection` and `training`. A command rewrites only the parts it changed, so a drill writes `training` and `head`.
- **Immutable things are their own items.** A sealed monster never changes (`specs/18` section 6), so it is written once. `Locked` is the player's flag, not part of the monster (it is outside `RunHash` already), so it lives in the `vault` index.
- **A part is bounded by content or capped by a rule.** New fields join the part whose writer set they share; a field that would grow without a bound goes to section 3.4.

Vault entry ids: an entry is named by the index of the run that made it (`RunsStarted` when it started), unique in the account. The sim gains `VaultEntry.Id`. Accounts are wiped (decision 8), so no migration is needed. Commands keep addressing entries by Vault position.

### 3.2 Hashes

- **Part hash**: SHA-256 of the part's canonical binary encoding (`Replay/Canonical.cs`, as `TrainingRun.StateHash` and `BattleSim.StateHash` do), shared code in `IdleRng.Sim` (`AccountHash`).
- **Entry hash**: SHA-256 over the entry's fields, excluding `Locked`; the `vault` part hash covers the ids, the locked set and every entry hash.
- **Account hash**: SHA-256 over the sorted `(part name, part hash)` pairs, kept in `head`.

The server no longer needs the hashes to check a client (the client computes nothing); they detect a damaged or half-written part when it is read, let support compare two accounts, and let the module load only the `mon.<id>` items a command needs while still pinning the rest.

### 3.3 Projections (read by others, never the truth)

Written by the module after a command, rebuildable from the parts, allowed to lag, never read back by a command:

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
| Command journal (audit, support) | Analytics events (`specs/08` section 8), not Cloud Save | The analytics provider's |
| Seasons (T12) | `season.<n>`, frozen when it ends | Kept |

Cloud Save allows 2000 keys per player per access class; at `VAULT_CAP` 50 the parts use about 60.

### 3.5 What is not kept

| Data | Kept | Why it can go |
|---|---|---|
| A sealed monster's replay log (`Log`, about 5.5 KB of its 6.7 KB), `Mods` and `Ladder` | No | Only `VaultEntry.Fighter` reads them, to rebuild the final stats and bought nodes the entry already stores (`TrainingRun.MonsterFighter` takes nothing else; the stored trees keep its order, absorbed trees before their slot). The fighter is built from the entry instead. Nobody needs to re-check a sealed run: the server played it |
| Commands | No | Applied and answered; only the last one's id and response stay in `head` for retries (section 4.3); a sample may go to analytics |
| The run in progress | Until it seals or is abandoned | Its recipe (seed and log, about 90 commands) in `training`; cleared at seal |
| The gauntlet floors | Never stored | The server replays them on request for the wheel (`gauntlet(id)`, section 4.4) |
| Dungeon attempts | Never stored | Rewards and bests are kept in `dungeon`; floor playback is replayed on request (`towerFloor`) |
| The post-run vendor | Only while its screen is open | Not available between runs (kj, 2026-11-01, `specs/18` section 4); while open it sits in `training` |

Kept although derivable: the owned species (from the achievements), so a later content change cannot take a species away.

What this gives up: a sealed monster can no longer be replayed step by step (balance questions use the bot CLI, `specs/09`). Built from its stored result, a sealed monster keeps the stats it was sealed with under later rule changes, as `specs/12` section 6 intends; skills, triggers and rules are still read from content by node id, so a skill's tuning applies to old monsters, and a removed node id is still a content MAJOR (`E090`).

## 4. Hidden rolls

### 4.1 Why seeds alone are not enough

Every stream draws `ToUnit(Derive(seed, counter))`, and `Derive` and `StreamSeed` are SplitMix64 steps, which are bijections (`specs/03` section 5.2). A drill's gain is a draw scaled into the tier's range, so the client sees almost all 53 bits of one draw; trying the 2^11 missing bits and inverting `Mix` gives back the stream's seed, and with it every later drill of the run. The same holds for a revealed fight seed (it inverts to the run seed) and for the Dungeon, whose daily seed comes from the public constant `TOWER_SEED` (`specs/21` section 2), so any future day's fights can be computed today.

### 4.2 The rule

1. **Draws are one-way.** `RngStream.NextDouble` becomes `ToUnit(PRF(seed, counter))` with a keyed pseudorandom function: SipHash-2-4 keyed by the stream seed (integer arithmetic only, so exact in WebAssembly and on every .NET runtime, `specs/00` contract 1; about 20 ns a draw). A draw then says nothing about its seed or the next draw. `StreamSeed` and `Derive` for child seeds (run, fight, vendor, tower floor) use the same function. Every golden file is regenerated once (the distributions are unchanged; every seeded vector moves) and `balance` is re-run to confirm the numbers stay where they were. `specs/03` section 5 is rewritten with the change.
2. **The account seed** is drawn by the module from a cryptographic source at account creation, stored in `training`, never sent.
3. **The Dungeon's daily seed** is `PRF(TOWER_SECRET, day)`, with `TOWER_SECRET` a server secret (UGS secret manager), replacing `TOWER_SEED`. The server reveals a day's lineup (bosses and phases, `towerInfo`) only from that day on, and the fights only as results.
4. **Responses carry results, never seeds.** Views have `Seed` fields removed (the run, Vault entries, the vendor); fight playback is the event list the bridge already returns; the gauntlet wheel and Dungeon floor playback are server reads (section 4.4).
5. **A roll is final once made.** A command's result is stored before it is returned, and a resent command gets the stored result (section 4.3), so dropping the connection cannot re-roll.

What the client may still show: odds computed from the visible state (the drill odds panel, vendor rarity odds). Those are probabilities, not outcomes.

## 5. The protocol

### 5.1 Calls

All calls go to the module with the player's id token (`web/auth.js`); the module knows the player from the token.

| Call | Does | Returns |
|---|---|---|
| `Open { deviceId, claim }` | Loads the account, or creates it (genesis: a new seed, the starter species, `seq` 0). `claim: true` makes this device the holder | `seq`, the account view (the bridge's `AccountView` without seeds) and the run view (`RunState` without seeds), content version, server time and day |
| `Act { deviceId, seq, id, command }` | Applies one command (section 5.2) | The bridge's result JSON for that command (`web/wasm/BRIDGE.md`), the new `seq`, and the views of what changed |
| `Read { deviceId, query }` | Read only: `gauntlet(vaultId)`, `towerInfo()`, `towerFloor(team, floor)` | The floors or lineup, as results |

Commands are the bridge's own: account commands `startRun`, `finishRun`, `abandon`, `buyOffer`, `rerollVendor`, `closeVendor`, `setLoadout`, `dismantle`, `lock`, `runTower`, and run commands `drill`, `rest`, `fight`, `take`, `discard`, `reroll`, `spend`, `book`. Two arguments the client sends today are dropped: `finishRun`'s seal time and `runTower`'s day both come from the server clock (#21). The switch that applies them moves from `IdleRng.Web.RunHost` into `IdleRng.Sim.Account.AccountCommands.Apply(content, account, command)`, so the module, the local test server (section 5.5) and the tests run one dispatcher, and each command declares the parts it reads and writes (`AccountCommands.Touches`, tested by applying every command with the other parts absent).

### 5.2 One command

```
client                                          module
  tap -> Act { deviceId, seq, id, command } ->
                                                read head + the parts the command touches (one batch get)
                                                holder == deviceId?         else NotHolder
                                                head.seq == seq?            else: head.lastId == id ? stored response : Stale
                                                content version matches?    else ContentStale
                                                debug command from a listed player?  else Refused
                                                apply (AccountCommands.Apply) with the server's time and day
                                                write changed parts under new keys + head (seq+1, last id and response),
                                                  head with its write lock (one batch set)
  <- result, seq+1, changed views               later: delete superseded keys, update projections
```

- **Atomic.** Changed parts are written under new keys (`training.000124`) in the same batch as `head`, and only `head` names them; the batch is conditioned on `head`'s write lock. If the batch is refused (another writer) nothing references the new keys. Unreferenced keys are swept later (every `SWEEP_EVERY` commands, placeholder 50, and on open), so a command costs one read and one write request.
- **Ordered.** `seq` makes commands strictly sequential: a second tab or a stale page gets `Stale` and reloads its views.

### 5.3 Retries

A command carries a client id (random per command). If the answer is lost, the client resends the same `{seq, id}`; the module finds `head.lastId == id` and returns the stored response without applying it again. So a timeout never applies a command twice and never re-rolls (section 4.2 item 5).

| Answer | Meaning | Client does |
|---|---|---|
| `NotHolder` | Another device took the account | Signs out (section 6.1) |
| `Stale` | The account moved on (another tab, or a lost answer older than the last command) | Calls `Open` and redraws |
| `ContentStale` | The module and the page run different builds | "A new version is out", reloads the page |
| `Refused` | The command is not allowed now (the bridge's reasons: not enough shards, no run, ...) or a debug command from an unlisted player | Shows the reason, as today |
| Network, timeout, 429, 5xx | No verdict | Resends the same command with backoff; the screen shows the command as pending |

### 5.4 The client

- **A view, not an engine.** The page keeps its WebAssembly build for content and display (`RunContent`, tooltips, odds, fight playback from events), but every state change is an `Act`. `web/run-engine.js` keeps its interface (`run.cmd`, `run.acct`, `run.account`, `run.state`) but sends the command to the module instead of the local host, so those calls become asynchronous: every place in `web/run.js` that acts on a command's result awaits it. That is the largest client change.
- **One command in flight.** A tap while a command is pending is ignored; the tapped control shows it is waiting. Animations (the drill result, the fight) start when the answer arrives.
- **Latency.** A warm call is about 65 ms in the spike (`specs/08` section 10.1) plus a Cloud Save read and write, so about 100 to 200 ms a tap. A cold worker takes 12 to 15 s: `Open` shows "Connecting...", waits up to 20 s and retries once; a scheduled keep-warm call is added once there are players (#109).
- **Nothing on the device but the session.** The account lives only on the server. On the first open of this build the page deletes the old local saves (`idle-rng-account-v1`, its `-unloadable` backups and the older keys) after the server account is open (decision 8), and keeps only `monstack-auth-v1` (the session) and `monstack-device-v1` (a random `deviceId`).
- **Offline.** No connection: "Connecting..." with Try again; the game does not play.

### 5.5 The local test server

`check.sh`, `./play.sh` and kj's phone artifact (which cannot rely on reaching UGS) run the same server code inside the page: `IdleRng.Server` compiled into the WebAssembly build with an in-memory store kept in `localStorage`, behind the same `Open`/`Act`/`Read` calls. The page uses it when served from anything but the GitHub Pages address, and says "Local test build" in the header. It has no anti-cheat value and is not the live game **(agent: keeps the browser checks and the phone artifact working without a network; kj can veto)**.

## 6. Rules the server enforces

### 6.1 One holder

`head.holder` is `{ deviceId, sinceSec }`. A call from another device returns `NotHolder`. That device's client forgets its session (`MonStackAuth.signOut`), starts a new guest (a new account by genesis) and says "Signed out: your account was opened on another device. Sign in to continue here." `Open` claims (`claim: true`) on a Google sign-in and on a guest's first open; an ordinary page open does not claim, so a signed-out device cannot take the account back without signing in.

### 6.2 Time

The module's clock is the only clock: the seal time on `finishRun`, the Dungeon day for `runTower` and the daily free ticket (#21; `specs/08` section 3's offline bound is no longer needed, since nothing plays offline).

### 6.3 Debug commands

`debugGrantBook` and any later debug command are refused unless the player id is on the module's debug list (configuration, not code; kj's ids). An account that used one carries `head.debugUsed` for good, and leaderboards and PvP leave it out.

## 7. Versions and deploys

- The module is built from the same commit as the site and compiles the same `IdleRng.Sim` and `Content/`. Every call carries the page's content version; the module answers `ContentStale` to any other.
- `.github/workflows/pages.yml` deploys the module (`server/ugs/deploy.sh`, with a UGS service account and `TOWER_SECRET` as secrets: a one-time setup by kj), then the site. Pages open during the gap get `ContentStale` and reload.
- A run in progress that no longer replays under new rules is dropped by the module exactly as `specs/06` section 5 drops it today (the same load code), with the notice shown on the next `Open`.

## 8. Limits and cost

| Limit | Value | Here |
|---|---|---|
| Cloud Save, per player per access class | 5 MB, 2000 keys | About 40 KB and 60 keys at a full Vault |
| Cloud Code call | 15 s; response 2 MiB | A command takes milliseconds (a drill replays the run's recipe, at most about 90 commands with their fights) |
| Requests per player | 600 a minute (Cloud Code and Cloud Save each) | A command is 1 Cloud Code call with 1 Cloud Save read and 1 write; one command in flight caps a fast player near 5 a second, 300 a minute |
| Cold start | 12 to 15 s | `Open` waits up to 20 s with one retry |

**Cost changes by an order of magnitude.** At 10 k daily players and about 500 commands a player a day (5 runs of about 90, plus the account): about 150 M Cloud Code calls a month, about 225 USD of invocations at the October 2026 list price plus compute, and about 300 M Cloud Save requests at its own meter. Expect a few hundred USD a month, against about 15 for the checkpoint design. The fallback in `specs/08` section 10.2 (one hosted .NET service, the run kept in memory between taps, one database write per command) costs about 20 to 100 USD a month at that load and answers faster, at the price of running a server. Build on UGS (no operations), measure at step 5 of section 10, and revisit if the bill or the tap latency is wrong. Re-check the pricing pages before launch.

## 9. Threats left

- **Bots playing legal moves** are playing (`specs/10` section 1, non-goals). The server can rate-limit commands per player.
- **Reading the content.** The client knows every table, so it can show exact odds; that is information, not a preview.
- **A compromised server or leaked secrets** (`TOWER_SECRET`, the service account): out of scope (`specs/10` section 1).

## 10. Build order

Each step merges on its own with `./check.sh` green.

1. **One-way draws.** SipHash-based `RngStream` and seed derivation (section 4.2), `specs/03` section 5 rewritten, golden files regenerated, `balance` compared before and after.
2. **Sim.** `AccountCommands.Apply` and `Touches` (the bridge's switch moved), `AccountHash`, `VaultEntry.Id` and the pruned entry (`VaultEntry.Fighter` from the stored result, with a test that it equals the replayed fighter for every bot policy and species before the log goes), the part split in `IdleRng.Runtime` (`AccountParts`), views without seeds, finish time and Dungeon day as server inputs.
3. **Server core.** A netstandard2.1 library `IdleRng.Server`: `Open`, `Act`, `Read` against an `IAccountStore` with an in-memory store; tests for every answer in section 5.3, retries, the holder rule, the sweep, the debug list. In `check.sh` on .NET 8 (#28).
4. **Client on the local test server.** `web/run-engine.js` over `Open`/`Act`/`Read`; the server core in the page (section 5.5); one command in flight; the browser checks run through it.
5. **Module and deploy.** `server/ugs/MonStackAccount`, a net10.0 shell over `IdleRng.Server` with a Cloud Save store and the server-secret Dungeon seed; the deploy step in `pages.yml` (kj adds the secrets); the page uses it on Pages; the wipe of old local saves. Measure tap latency and cost here (section 8).
6. **Projections:** `profile`, then Leaderboards for the Dungeon.

## 11. kj's answers (2026-11-01)

1. Accounts from before the server: **wiped** (decision 8).
2. Debug controls on the live game: **yes**, only for listed players (section 6.3).
3. Look-ahead: **not accepted**; rolls are done by the server and not previewable (section 4).
4. Offline play: **no** (decision 3).

Left for kj, none blocking: the cost and latency check at build step 5 (section 8), and the local test build for the artifact (section 5.5).
