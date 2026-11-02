# 08 - Backend Seams

> **Status (rewritten for the trainer on 2026-11-01):** no backend is built beyond player accounts: guest and Google sign-in for the web client (section 4, issue #19, built 2026-11-01). The account server of `specs/27` is being built: its core (`src/IdleRng.Server`, open and commit against a store, tested in `check.sh`) exists, not yet deployed or used by the client. Besides that, what exists is the UGS Cloud Code spike in `server/ugs/` (section 10.1), which picked the vendor, and the local save store in `IdleRng.Runtime` (`specs/06`). The rest of this spec is the design for when async PvP (T12, issue #76) and purchases (T13, #77) need a server. The idle game's seams (`IGachaService`, the gem ledger, offline gold, the idle chest, `IdleRng.Remote`) were removed at `specs/13` T7 and from this spec with this rewrite; they are in git history.

Scope: what the server will own, where the calls go, and what the client does when the network is absent. The battle rules a server must run are `specs/16` and `specs/21`; the account save is `specs/06` and `specs/18` section 6; the trust model for competitive play is `specs/10`. Issues: goal #15, children #19, #21, #25, #26, #27, #28, #29, #109, #122.

## 1. Principle and layout

1. **The sim never touches a clock or the network.** `IdleRng.Sim` is pure: callers pass the Dungeon day (`specs/21` section 1) and every other input. Adding a server adds callers, never a branch inside the sim on "local or remote".
2. **One engine on every host.** The server compiles `IdleRng.Sim` from the same sources as the browser client and the bot CLI (a project reference, proven by the spike in section 10.1). No game rule is written twice.
3. **The browser client owns the network.** The client is `web/` around the WebAssembly build of the C# core (`specs/13` section 1). Network calls (sign-in, Cloud Code, Cloud Save) are plain HTTPS `fetch` calls from the client's JavaScript, the same layer that already keeps the account in `localStorage` (`web/run-engine.js`). The C# core stays a pure function of what it is given **(agent: the C# core has no network today and the wasm runtime would gain nothing from it; kj can veto)**.

| Part | Contains | Status |
|---|---|---|
| `IdleRng.Sim` | The rules: training runs, the gauntlet, the Vault, the vendor, the Dungeon (`TowerSim`), battles (`BattleSim`). Shared with the server. | Built |
| `IdleRng.Runtime` | The account save: `AccountSave`, `ISaveStore` with `FileSaveStore` and `MemorySaveStore` (`specs/06`). | Built |
| `IdleRng.Web` + `web/` | The browser client. Today: the account in `localStorage`, the Dungeon day from the device clock. Later: the server calls in this spec. | Built (local only) |
| `server/ugs/IdleRngBattle` | The Cloud Code C# module. Today one spike endpoint, `ResolveFight` (section 10.1). Becomes the T12 PvP module (section 10.3). | Spike |
| Server tests | An in-process fake of the module's handlers so `dotnet test` and `check.sh` cover the remote paths without credentials (#28). | Not started |

## 2. Summary

| Seam | Today (local only) | With the server | Owns | Issue |
|---|---|---|---|---|
| Time | Device UTC day (`run.towerDay()` in `web/run-engine.js`) | Server time, synced (section 3) | The Dungeon's free daily ticket | #21 |
| Account | **Built:** UGS guest sign-in on launch, Google linking on the web (section 4; Google confirmed on Pages by kj, 2026-11-01); the save is not bound to it yet | Steam linking on the Steam build; the account held by the server (`specs/27`) | Identity, restore on another device | #19 |
| Save | `localStorage` in the browser, `FileSaveStore` in tools (`specs/06`) | The server holds the account and re-checks the client's commands at checkpoints; the device keeps a cache and the pending commands (`specs/27`) | The account; one device at a time | #25 |
| PvP battles | None | Cloud Code module (section 10.3) | Matchmaking, battle results, rating | #76 |
| Purchases | None | Server-validated (section 6), only if purchases ship | Grants for money | #26 |
| Remote config | None | Content overlay (section 7) | Tuning without a release (P2) | #29 |
| Analytics | None | Queued upload (section 8) | Tuning data, plausibility flags | #27 |

A failed remote call always reports one of these kinds, so the client handles errors one way (section 9 gives the per-operation policy):

| Kind | Meaning | Retry |
|---|---|---|
| `Offline` | No connectivity, DNS failure | Yes |
| `Timeout` | No answer in time (HTTP 408 included) | Yes |
| `Rejected` | The server refused the request (other 4xx); carries the body's `code` | No |
| `Conflict` | The server holds a newer version (409) | No: resolve, then retry |
| `Internal` | 5xx or anything unexpected | Yes |

## 3. Server time (the Dungeon day)

**Simplified by `specs/27`** (a connection is needed): the Dungeon day is the one the server gave at the last open or commit, and the seal time is bounded by the server's clock (`specs/27` section 6.2); the device clock and the offline bound below no longer apply once it is built.

The only clock rule the trainer has: the Dungeon grants `TOWER_ATTEMPTS_PER_DAY` free tickets per UTC day, banked up to `TOWER_TICKET_CAP` (`specs/21` section 7). The account remembers `Tower.GrantDay`, the last day it granted. Today the day is `floor(Date.now() / 86400000)` from the device, so moving the device clock forward grants tickets.

The design (#21):

```
offset   = serverSec - monotonicSec at the last successful sync   (null before the first)
nowSec   = offset != null ? monotonicSec + offset : deviceSec
day      = floor(nowSec / 86400)
dayUsed  = min(day, lastSyncedDay + 1) when not synced this session, else day
dayUsed  = max(dayUsed, account.Tower.GrantDay)                  never backwards
```

- **Sync** at page open, when the page becomes visible again, and every 15 minutes while visible. One round trip: the server time from a one-line Cloud Code script (UGS has no time endpoint, section 10), with the midpoint of the request as the estimate. Between syncs time follows the browser's monotonic clock (`performance.now()`), so changing the device clock mid-session does nothing.
- **Offline**: the day may move at most one past the last day a sync confirmed (`lastSyncedDay`, kept with the account). A player offline for a week loses tickets they could have banked rather than letting a moved clock mint them; the cap is 5 anyway **(agent: the bound from #21; kj can veto)**.
- **Never backwards**: the day never reads below `Tower.GrantDay`, so moving the clock back neither repeats nor undoes a grant.
- `TowerSim` and `AccountOps` do not change: they already take the day as an argument.

## 4. Accounts

**Status: built for the web client (2026-11-01), Google only.** kj (2026-11-01): start with Google; Steam waits for the Steam build (T13), other web providers wait for a request. `web/auth.js` holds the logic, `web/account.js` the header button and the account sheet, `web/wasm/test/auth.js` the browser check (a fake Unity Authentication and a fake Google script, part of `check.sh`).

1. **Guest sign-in** on first open, so play never waits for a login. The session token is kept beside the save (`localStorage['monstack-auth-v1']`), not in it.
2. **Link Google** from the account sheet. Linking keeps the guest's player id; signing in on a second device gets the same player back. Restoring the account there is the server-held account (`specs/27`).
3. **Ids.** The UGS player id is the account's identity. It is stored in the save as a nullable `AccountId` when cloud save lands (a schema step, `specs/06` section 5); the save is unchanged until then, and signing in, switching player or signing out never touches it.

### 4.1 Flow

Unity Authentication through its REST API (`https://player-auth.services.api.unity.com/v1`, headers `ProjectId` and `UnityEnvironment`; no Unity SDK, CORS allows any origin):

| When | Call | Result |
|---|---|---|
| First launch | `POST /authentication/anonymous` | A guest player. The session token is stored on the device. |
| Later launches | `POST /authentication/session-token {sessionToken}` | The same player, fresh id token (1 h, kept in memory, refreshed a minute before it expires). The response's `user.externalIds` says whether Google is linked. |
| Google on a guest | `POST /authentication/link/google.com {token}`, Bearer the guest's id token | The guest becomes the Google account's player (same player id). |
| Google already has a player (a second device) | The link fails, then `POST /authentication/external-token/google.com {token, signInOnly: true}` | This device signs in to that player; its guest is abandoned (it owns nothing server-side yet). If that sign-in fails too, the link error is shown. |
| Google with no session (the first sign-in failed) | `external-token/google.com {token, signInOnly: false}` | Signs in to, or creates, the Google account's player. |
| Sign out | none | The stored session is forgotten and a new guest starts. Google gets the linked player back. |

The Google ID token comes from Google Identity Services (`https://accounts.google.com/gsi/client`, popup mode), loaded only when the account sheet opens. The client reads the token's email for display only; the server verifies the token.

Failures (section 9): a network failure (10 s timeout) leaves the game playing, shows Offline in the header with Try again in the sheet, and keeps the stored session for the next launch. A session the server rejects (401 or 400) is dropped and a new guest started.

### 4.2 Hosting and configuration

Google sign-in needs a fixed web origin that loads Google's script, which the claude.ai phone artifact cannot (it loads scripts only from a few CDNs). kj (2026-11-01): the game is hosted on GitHub Pages at `https://kjew2.github.io/MonStack/`. `.github/workflows/pages.yml` deploys it after the check workflow passes on `main`; `web/wasm/site.sh` lays out the same files the artifact publishes. The artifact stays the phone test build; its account sheet points to the Pages address for Google.

Configuration is public identifiers in `web/auth.js` `CONFIG`: the UGS project id and environment (`production`) and the Google OAuth web client id. With no client id the sheet says Google sign-in is not set up. One-time setup outside the repo (kj):

1. GitHub: Settings > Pages > Source: GitHub Actions.
2. Google Cloud console: an OAuth client of type Web application, authorised JavaScript origins `https://kjew2.github.io` (and `http://localhost:8000` for `./play.sh`), plus the OAuth consent screen.
3. UGS dashboard: Authentication > Identity providers > Add Google, with that client id. Until then UGS answers Google calls with "id provider not found".
4. The client id goes into `CONFIG.googleClientId`. Done 2026-11-01: `1042534114217-99fsfrksjmfo8l44kehq6h511bvhe20b.apps.googleusercontent.com` (kj created the OAuth client).

Decision for kj (section 12): whether PvP needs a linked login.

## 5. Saves

### 5.1 The local store (built)

`ISaveStore` is raw bytes in and out with two rotating slots; the format, the loading rules and the schema steps are `specs/06`. The browser keeps the same bytes as text under one `localStorage` key. Nothing here waits on the network, and nothing will: the local write always happens first.

### 5.2 The account on the server (#25)

Replaced (kj, 2026-11-01) by `specs/27`: the server holds the account, partitioned by domain into Cloud Save items, and re-applies the client's commands at checkpoints, accepting them only when the hashes match (option E, chosen by kj after comparing the options in `specs/27` section 8; the game needs a connection). One device holds the account at a time, so there is no conflict rule. The client-uploaded save copy first designed here (newest wins, a prompt when the loser is ahead) is in git history.

## 6. Purchases (#26, only if they ship)

What MonStack sells, if anything, is open (#49; Skill Books are the candidate premium item, `specs/18`). If purchases ship:

- The server validates every purchase (the Steam microtransaction API on Steam, the payment provider's signed webhook on the web) and grants exactly once per transaction id; a replayed receipt returns the original grant.
- The client saves the intent before it starts a purchase and resolves it at the next open, so a crash mid-purchase neither loses nor doubles a grant. That is a save field, added with the feature.
- Whatever is bought is server-held from then on: the save mirrors the server balance and the server wins on disagreement (`specs/10` section 8).
- The refund and chargeback rule is written before launch (#26).

## 7. Remote config (#29, P2)

A full set of content tables for one `content_version` (`specs/05`), fetched and cached, so tuning can change without a release. Rules:

1. Applied at page open only, before the content is loaded; never mid-run.
2. It goes through the content validator; a rejected overlay falls back to the shipped tables.
3. A minimum build gates it.
4. **It must not change how anything already sealed replays.** The run in progress replays against the rules and content it was made with, and a Vault entry's gauntlet wheel replays its floors (`specs/06` section 5, `specs/18` section 6). An overlay that would break a replay is a content MAJOR (`E090`) and needs a release, not an overlay.
5. Ranked PvP pins one content version per season (`specs/10` section 11), so an overlay applies to ranked play only at a season change.

## 8. Analytics (#27)

A queued sink in the client: events are kept in browser storage up to a cap (oldest dropped first) and uploaded in batches. The event list is written with #27; the starting set is runs started and sealed (species, grade, gauntlet floor), Dungeon attempts and floors cleared, vendor buys and rerolls, and session length. Each event carries the content version and whether debug was used. The bot CLI's `balance` verb (`specs/09` section 7) is the reference the live numbers are compared with. Provider and web consent are kj's (#27, #50).

## 9. Offline behaviour

Rule: single-player never waits on the network. Only what the server owns needs it, and that never half-applies.

| Operation | Without a connection | Why |
|---|---|---|
| Training runs, the gauntlet, the Vault, the vendor, achievements | **Needs the connection** (kj, 2026-11-01): plays on the device, but waits ("Connecting...") when a checkpoint cannot land (`specs/27` section 5.2) | The server re-checks every command |
| Dungeon attempts | Play as today with the tickets held | Rewards are single-player (section 11) |
| The free daily ticket | At most one day past the last synced day (section 3) | The device clock is the one input a player changes for free |
| Save | Local write; pending commands kept on the device | A save never fails because the network did |
| Load | Waits for the server (up to 20 s for a cold worker, Try again; `specs/27` section 5.1) | The server's account replaces the device's cache |
| PvP | **Needs the connection.** The button says so. | The server resolves the battle |
| Purchases | **Need the connection**; a started one resolves on the next open | Validation is server-side |
| Analytics | Queued, oldest dropped past the cap | Never block, never grow unbounded |

## 10. Server runtime: Unity Gaming Services

The comparison that picked the vendor. It was first written for the idle game (gacha pulls, cloud saves, about 6 M API calls a month at 10 k daily players); section 10.1 redoes the cost for server-resolved PvP. Prices are list prices as understood in October 2026; re-check each vendor's pricing page before signing and treat the cost row as an order of magnitude.

| | Unity Gaming Services (UGS) | PlayFab (Microsoft) | Firebase + Cloud Functions | Nakama (Heroic Labs) |
|---|---|---|---|---|
| Auth | Unity Authentication: anonymous, Apple, Google Play, custom OpenID. Free. | Anonymous device id, Apple, Google, many others. Included. | Firebase Auth: anonymous, Apple, Google. Free at this scale. | Device, email, Apple, Google, custom. Included. |
| Cloud save | Cloud Save: per-player key-value JSON, plus file storage for larger blobs. The account save fits a single item. | Player Data (title-scoped key-value, JSON values) and Entity Objects. Fits. | Firestore document per profile. Fits. Optimistic concurrency via document versions. | Storage engine: collections of JSON objects per user with a version for optimistic writes and read/write permissions. Fits well; the version field maps directly onto a write-locked root item (`specs/27` section 5.2). |
| Server time | No dedicated endpoint; a one-line Cloud Code script returning `Date.now()`. | `GetTime` client API, built in. | No dedicated endpoint; a Cloud Function, or `serverTimestamp()` on a write. | No dedicated endpoint; a one-line RPC. |
| Remote config | Remote Config service: key-value JSON with campaigns and overrides. Free. | Title Data: key-value JSON; no staged rollout without CloudScript. | Remote Config: strong, with conditions and percentage rollouts. Free. | None built in; a storage object with public read, or an RPC. |
| Receipt validation | Economy service redeems Apple and Google Play purchases server-side and grants configured rewards. | `ValidateIOSReceipt`, `ValidateGooglePlayPurchase`, built in. | Not built in; write it in a Cloud Function against the store APIs (about a day), or use RevenueCat. | `ValidatePurchaseApple` / `ValidatePurchaseGoogle` built in, including subscriptions. |
| Server runtime for battles (section 1 rule 2) | Cloud Code: JavaScript, or C# modules (Cloud Code Modules run .NET). The C# module compiles `IdleRng.Sim` from the shared sources (section 10.1). | CloudScript (JavaScript) or Azure Functions (C# supported). Azure Functions gives the shared-source path. | Cloud Functions: Node.js, Python, Go. No .NET; the battle rules would be ported to Go and held to the golden files. | Go plugins, TypeScript or Lua runtime. A Go port of the battle rules keeps exact uint64 arithmetic for the generator; TypeScript needs BigInt for it and is slower. |
| Analytics | UGS Analytics, free tier adequate at this scale. | PlayStream events and basic dashboards; export for real analysis. | Firebase Analytics / Google Analytics, free, with BigQuery export. Best of the four. | None; bring your own (the file sink plus an export job). |
| Estimated monthly cost at 10 k DAU | Low. The services used here have free tiers sized for small titles; Cloud Code and Cloud Save are metered beyond them. Estimate 0 to 50 USD. | Standard tier has a base fee (about 99 USD) plus meters; estimate 150 to 400 USD. | Blaze pay-as-you-go. Firestore reads/writes at this volume are a few dollars; Functions within or just past the free tier; estimate 20 to 60 USD. | Self-hosted: software is Apache-2.0, infrastructure for one small VM plus managed Postgres about 50 to 150 USD. Heroic Cloud managed hosting starts in the low hundreds. |
| Lock-in and risk | Single-vendor with the engine. Unity's commercial terms have changed abruptly before. Every call goes through the client's network layer (section 1 rule 3) and the module, so a swap is confined to those. | Mature, stable pricing; heavier SDK; Azure account required. | Google lock-in on Firestore data model; everything else is portable. | No lock-in; we own the server. Ops burden: upgrades, backups, on-call. |

Recommendation for v1: **Unity Gaming Services**, confirmed by the spike in section 10.1 (Cloud Code modules run `IdleRng.Sim` unchanged). If Unity's terms move, the first fallback is plain hosted .NET (section 10.2), which also runs `IdleRng.Sim` unchanged; **Nakama** (a Go port) is the second.

Reasons (revised 2026-10-31 for the web-first client: the trainer ships as WebAssembly in a browser, `specs/13`, not as a Unity build):
1. The services are plain HTTPS APIs (anonymous sign-in, Cloud Code, Cloud Save, Remote Config), so a browser client calls them with `fetch` and needs no Unity SDK. The "one SDK already in the Unity project" argument no longer applies; what remains is no ops.
2. Cloud Code C# modules compile `IdleRng.Sim` from the same sources as the client (a project reference, section 10.1), so server-resolved battles (T12 async PvP, `specs/10`) need no port and no cross-language vector file. That is the single largest correctness risk in adding a server, and UGS and plain hosted .NET are the two options that remove it. The gacha `Pull` this was first argued for was cut at T9.
3. Cost at 10 k DAU is the lowest of the options (section 10.1).
4. Remote config is built in. Receipt validation through Economy only matters if paid items return.

Against PlayFab: the base fee and the Azure Functions deployment pipeline are more than this project needs at 10 k DAU. Against Firebase: no .NET runtime, so battles would be a Go or TypeScript port held to the golden vectors. Against Nakama as first choice: the ops burden lands on a team that has none, and we gain nothing from owning the server until leaderboards and guilds.

### 10.1 Spike: `BattleSim.Resolve` in a UGS Cloud Code module (issue #17, 2026-10-31)

**Status: done (pass).** The module is built and deployed, and all 47 fights resolve to the same hash, outcome and ticks in process, across .NET 8 and .NET 10, and remotely as an anonymous player calling the deployed endpoint. The remote run was on 2026-10-31 from a Claude cloud session (`server/ugs/SpikeClient remote`, see `server/ugs/README.md`).

What is in the repo (not in `check.sh`, because it needs credentials):
- `server/ugs/IdleRngBattle`: the module. A net10.0 class library with a `ProjectReference` to `src/IdleRng.Sim` (netstandard2.1), so the server compiles the same sources: no copy, no port, no change to `IdleRng.Sim`. One endpoint, `ResolveFight(FightRules rules, Fighter[] sideA, Fighter[] sideB, string seed)`, returns outcome, ticks, `BattleSim.StateHash`, the hash version, the time inside `Resolve` and the worker's runtime and culture.
- `server/ugs/deploy.sh`: `dotnet publish -c Release -r linux-x64`, zip to `IdleRngBattle.ccm`, create or update the module through the Cloud Code Admin API with the service account (Basic auth on `services.api.unity.com`). It uses `curl` instead of the `ugs` CLI: the CLI's npm installer downloads its binary from GitHub's release API and failed on the API's rate limit.
- `server/ugs/SpikeClient`: the test script. It builds 49 fights: the 9 `BattleVectors` fights (the ninth, with gambit rulebooks, added after the spike ran), and every floor of the `TowerVectors` all-species team run on days 0 and 20391 (40 floors, all four boss phase kinds and an entropy vent; each floor's rebuilt hash is checked against `TowerSim.Run`). `local` regenerates all four golden files on the module's runtime and binds each fight's JSON params into the module the way Cloud Code does (by parameter name, Newtonsoft.Json); `remote` signs in anonymously, calls the deployed endpoint for each fight, and times 20 warm calls of the smallest and the largest fight.

Results:

| Check | Result |
|---|---|
| Module target framework | .NET 10 (Unity's "Package code" page for Cloud Code modules). `IdleRng.Sim` loads as-is; the Cloud Code Core package (0.0.9) targets netstandard2.1. |
| Deploy | Create (HTTP 201) and update (HTTP 200) through the Admin API. Unity parsed the endpoint and its parameter types (`FightRules`, `Fighter[]`, `Fighter[]`, `String`). Nothing needed enabling in the dashboard. |
| Package size | `IdleRngBattle.ccm` 392 KB of the 10 MB module limit (6 files: the module, `IdleRng.Sim.dll` 200 KB, Newtonsoft.Json 712 KB uncompressed, Cloud Code Core, DI abstractions). |
| Golden files on .NET 10 | All four (`battle-sim`, `training-run`, `gauntlet`, `tower`) identical to the committed files written by the .NET 8 client build. |
| In-process, through Cloud Code's JSON binding | 47 of 47 hashes, outcomes and ticks match `IdleRng.Sim`. `Resolve` takes 0.1 to 1.3 ms per fight in this cloud container (the slowest includes JIT warm-up). |
| Remote, anonymous player | **47 of 47** hashes, outcomes and ticks match `IdleRng.Sim`. Anonymous sign-in 0.7 s. The worker reports .NET 10.0.13 on Ubuntu 24.04 x64 with the invariant culture. |
| Time inside `Resolve` on the worker | Median 0.1 ms, worst 5.8 ms over the 47 fights (the worst includes JIT warm-up). |
| Warm round trip, 20 calls | Smallest fight (1.4 KB of params): median 62 ms, worst 74 ms. Largest (5.1 KB, a phased boss floor): median 66 ms, worst 183 ms. Measured from a cloud container, so the round trip is mostly network and Cloud Code overhead, not the sim. |
| Cold start | First call right after a deploy: 14.9 s. First call after 16 minutes without traffic: 12.0 s. Sign-in and warm calls were the same on both runs, and both runs matched 47 of 47. |

Blockers for the shared code: none. Notes for T12:
- **Floating point.** The sim uses only `+ - * /`, comparisons and `Math.Min`, `Max`, `Floor` and `Round`; it avoids `Math.Pow` and transcendental functions by design. Those operations are exact under IEEE 754 on every .NET runtime, which is why the golden files match across .NET 8 and .NET 10. Keep it that way: a `Math.Pow`, `Exp` or `Log` in the sim could differ between the browser (WebAssembly) and the server.
- **Culture.** `StateHash` is a SHA-256 of a binary encoding of the state (raw double bits, no text formatting), so the worker's culture cannot move it. The reply still reports the culture.
- **Seed.** A 64-bit seed travels as a decimal string: JSON numbers in a JavaScript client lose precision above 2^53.
- **Input size.** Fight params are 1.4 to 5.1 KB of JSON (a four-monster team against a phased boss), far below any limit. Enums travel as integers and computed properties (`CombatSkill.Magical`, `TowerRules.Valid`) are ignored on the way in.
- **The browser build (not part of the spike).** The WebAssembly build of the same sources is held to the same golden files by `check.sh` (issue #122, 2026-11-01): `web/wasm/test/golden.js` loads the wasm engine in headless Chromium, calls `Engine.GoldenVectors()` (`web/wasm/BRIDGE.md`), which runs the bot CLI's own generators (`IdleRng.Bots.Golden.GoldenFiles.All`: battle, training-run, gauntlet, tower and endless vectors) on the bundled content, and compares each file byte for byte with `golden/`. So .NET 8, .NET 10 and the browser resolve every vector fight, run, gauntlet, Dungeon climb and Endless Tower climb to the same hashes, and a wasm-only drift fails `check.sh` the day it lands, before T12 has the browser replay server-resolved fights. It takes about 3 s in the interpreter build.
- **Cold start against the 15 s limit.** A cold worker took 12 to 15 s to answer, against a 15 s per-call limit, and the sim is not the cause (0.1 ms). T12 must plan for it: a client timeout above 15 s with one retry (a deterministic fight gives the same result again), and a scheduled keep-warm call once there are players. TODO(#109)
- **Limits** (Cloud Code docs): 15 s per call, 512 MB per worker, 2 MiB response, 600 requests per minute per player, 20 modules per project.

PvP load and cost at 10 k daily players. Assumptions: 10 ranked async PvP fights per player per day, each resolved by one module call, and 3 Dungeon attempts certified server-side (one call per attempt if the endpoint runs the whole climb). That is about 130 k calls a day, 4 M a month.

| Meter | Free each month (UGS pricing page, October 2026) | Our use | Cost beyond the free tier |
|---|---|---|---|
| Invocations | 1 M | 4 M | 3 M x 1.50 USD per M = 4.50 USD |
| Compute | 20 hours | 4 M x 10 to 30 ms billed (assumed: the sim itself takes about 0.1 ms per fight, but billing is per invocation's scheduled time, which the spike cannot see) = 11 to 33 hours | 0 to 13 hours x 0.072 USD per hour = 0 to 1 USD |
| Egress | 1 GiB | 4 M x about 400 B = 1.5 GiB | 0.5 GiB x 0.25 USD = 0.15 USD |

About 5 to 10 USD a month for the battles, plus sign-in and Cloud Save, which have their own free tiers. Re-check against the pricing page before launch; a forum post reports that compute is billed by CPU time scheduled rather than wall time. The spike could not confirm it: the dashboard's usage page is the place to check once there is real traffic.

Decision (2026-10-31): **UGS Cloud Code stands as the v1 server for battles.** Every remote hash matches with no change to `IdleRng.Sim`, warm calls take about 65 ms, and the PvP load costs about 5 to 10 USD a month. The one caveat is the 12 to 15 s cold start, which T12 handles in the client (#109). The fallbacks in 10.2 stay on file for the vendor-terms risk in section 10.

### 10.2 Fallbacks (if Unity's terms move)

- **Nakama** (Go plugin): the battle rules ported to Go and held to the golden vectors. A second implementation to keep in step with every sim change, plus server ops.
- **Plain hosted .NET** (for example a small ASP.NET Core service on a container host, not compared in the table above). Runs `IdleRng.Sim` unchanged like Cloud Code, with no vendor runtime limits, and suits a web client (plain HTTPS and CORS). Costs one small always-on container (about 5 to 20 USD a month) and the ops Cloud Code removes: deploys, TLS, scaling, and our own auth or a third-party one.


### 10.3 PvP battle calls (T12, #76)

The spike's `ResolveFight` takes the fighters as the client sends them: right for proving determinism, wrong for PvP, where a client could send any stats. The T12 module keeps the engine and changes what it trusts. The contract is written with the `specs/10` rewrite at T12; what is fixed now:

- **The server builds both teams from stored Vault entries**, never from fighter stats a client sends. A team is named by Vault entry ids; the server already holds those entries, sealed by re-checked commands (`specs/27`), so it builds each fighter from the stored result. A fight then names the two stored teams.
- **The server picks the battle seed** and returns it with the result, so the client can replay the fight for the animation and get the same hash (#122 makes sure the browser build agrees).
- **Seeds travel as decimal strings** (JavaScript numbers lose precision past 2^53).
- **Cold start** (#109): a cold worker takes 12 to 15 s, against Cloud Code's 15 s call limit. The client waits more than 15 s, shows a "resolving" state, and retries once on a timeout (a deterministic fight gives the same result again). A scheduled keep-warm call is added once there are players.
- **Idempotent submits**: a resubmitted fight request (same id) returns the stored result and changes no rating.

## 11. What the client keeps, and what the server owns

**Updated for the trainer** (kj, 2026-11-01, `specs/27`): the client still runs everything below, but the server holds the account and re-checks every command at checkpoints before it counts. The table lists what the client runs:

| Domain | Specifically | Why the client keeps it |
|---|---|---|
| Training runs | Drills, rests, rank fights, trees, the run in progress | The game must play offline; a run replays from its recipe (`specs/18` section 6), so a server can check any sealed run later |
| The gauntlet and the Vault | Floor reached, wheel, title, sealing, dismantling | Derived from the run; stored with its hash |
| Shards, talents, the vendor, achievements, species unlocks | | Only affect the player's own progress |
| Dungeon play | Attempts, floors cleared, ticket pieces, Skill Book drops | A result is a pure function of content, team, day and order (`specs/21` section 1), checkable at T12 |

The server owns, once it exists:

| Domain | Why |
|---|---|
| Time for the free daily ticket | The device clock is the one input a player changes for free (section 3) |
| Account identity | Restore and PvP need one id the client cannot invent (section 4) |
| PvP teams, battles and rating | Other players see the result, so it cannot rest on a client claim (section 10.3, `specs/10`) |
| Purchases and anything bought | Money; the store APIs need a server secret (section 6) |

With `specs/27` built, the server owns the account itself and accepts only re-checked commands; the account seed comes from the server. Why the earlier split (client-held account) would be safe until T12: nothing single-player is shared between players. A player who edits their save harms only their own experience and the tuning data (flagged, section 8). The line moves where PvP reads single-player results: a Vault entry entered in PvP is replayed on the server before it counts, and any single-player reward that can be entered in PvP (Skill Books shape a monster) needs the same care. Which ones, and how a client-chosen account seed is handled (a player could try seeds offline for a lucky run), are part of the `specs/10` rewrite (open decision 3).

## 12. Open decisions

1. **Cloud-save conflict rule** (#25). Closed (kj, 2026-11-01): no conflicts; one device holds the account and a new sign-in signs the old device out (`specs/27` section 6.1).
2. **Analytics player id** (#27). Hash the player id with a per-build salt (nobody can follow a player across builds) or a fixed one (lifetime cohorts possible). Recommendation: fixed salt, kept out of the repo, decided with the consent question (#50).
3. **What PvP trusts from single-player** (#76, with the `specs/10` rewrite). Mostly answered by `specs/27`: every account starts from a server seed (old accounts are wiped) and every Vault entry was re-checked by the server; accounts that used debug commands are left out (`specs/27` section 6.3). Left: whether the accepted replay risk (`specs/27` section 8.2) is acceptable for PvP when T12 starts.
4. **Server-side plausibility** (#27). Superseded by `specs/27`: every command is re-applied at its checkpoint, so there is nothing further to re-check.
5. **Linked login for PvP** (#19, kj). Whether async PvP (T12) needs a linked Google (later Steam) account or a guest may play. Recommendation: require a link; a guest loses everything with the device, and a linked account is what a ban or a report attaches to. Not blocking until T12 PvP.
6. **A Nakama port's language** (only if the fallback in 10.2 is taken). Go, for exact uint64 arithmetic.

Closed: the backend vendor (2026-10-31, UGS, section 10.1); web login providers (2026-11-01, Google first, section 4). The idle game's decisions (local gacha fallback, the gem ledger split, `PendingReceipt`) went with it.
