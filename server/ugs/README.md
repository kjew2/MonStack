# Unity Gaming Services: Cloud Code modules

C# modules for UGS Cloud Code. They target .NET 10 (Cloud Code's runtime) and compile the game's own sources by project
reference, so the server runs the same `IdleRng.Sim` as the browser. Design: `specs/27` (the account server) and `specs/08`
section 10 (the vendor and the spike).

| Path | What |
|---|---|
| `MonStackAccount/` | **The account server** (`specs/27` section 10 step 4): a thin shell over `src/IdleRng.Server` with Cloud Save stores. Endpoints `Open`, `Commit`, `EndlessBoard`, `GauntletBoard`, `Profile` and `Warm`. |
| `MonStackAccount.Tests/` | Its handlers in process over an in-memory Cloud Save with Cloud Save's write-lock rules (issue #28). In `check.sh`. |
| `keep_warm.py` | Creates or removes the keep-warm schedule and trigger (below). `test_keep_warm.py` tests it in `check.sh`. |
| `deploy.sh` | Publishes a module for linux-x64, zips `<Module>.ccm` and creates or updates it through the Cloud Code Admin API. `--check` only builds and packages. |
| `IdleRngBattle/`, `SpikeClient/` | The spike of issue #17 (`specs/08` section 10.1): `ResolveFight` and its test script. Kept for re-measuring; not deployed by CI. |

## MonStackAccount

| Endpoint | Params (JSON `params` of the Cloud Code call) | Returns |
|---|---|---|
| `Open` | `request: { deviceId, claim }` | `{ code, accepted, account, notice, seq, hash, created, contentVersion, serverSec, day }`; `account` is the `AccountSave` text, gzip then base64 |
| `Commit` | `request: { deviceId, fromSeq, fromHash, commands: [{ k, a }], toHash, contentVersion }` | `{ code, accepted, index, reason, seq, hash, serverSec, day }` |
| `EndlessBoard` | `season` (negative: the server's current one), `top` (0: 20; at most 100) | `{ season, total, top: [{ rank, name, floor, you }], you, fallbackName }` |
| `GauntletBoard` | `species` (a species id; empty for the overall board; another id is refused), `top` (0: 20; at most 100) | `{ species, total, top: [{ rank, name, species, speciesName, floor, cleared, label, you }], you, fallbackName }` (`specs/19` section 9; `species` left out for the overall board) |
| `Profile` | `of`: whose profile (a UGS player id; empty for the caller's own) | `{ found, profile }`, the profile as its lean JSON (`specs/27` section 3.3.2) |
| `Warm` | none | `{ contentVersion, serverSec }`: the keep-warm target, needs no player |

`code` is `None` for an accepted call, else `NotHolder`, `Stale`, `ContentStale`, `Mismatch` or `Refused` (`specs/27` section 4).
The player is always the one the Cloud Code context names (the caller's token): no endpoint takes a player id. The page calls
the module with `web/ugs-transport.js`.

Storage (Cloud Save **private** items, which only the module's service token reads or writes, except the profile):
- The profile: the player's **protected** item `profile` (only the module writes it; the player may read it), stored as a JSON
  object so it can be indexed when profiles are queried across players.
- The account: the calling player's items under the keys `IdleRng.Server` picks (`head`, `<part>_<seq>_<tag>`,
  `mon-<id>_<seq>_<tag>`, `specs/27` section 4). `head` is replaced only under its Cloud Save write lock; a lost swap is `Stale`.
  Cloud Save has no create-if-absent write, so creating `head` checks it is absent, writes, and reads it back (see
  `CloudSaveExtensions.TryReplaceAsync`; a race inside one round trip is harmless, `specs/27` section 10 step 4).
- The Endless Tower season board: custom entity `endless-<season>`, items `rows-0` to `rows-f` (a player's shard by the SHA-256
  of their id), each `{ playerId: [floor, atMs] }`; names on custom entity `board-names`, items `names-0` to `names-f`. Cloud
  Save rather than UGS Leaderboards: no board to create ahead of each week, no admin key in the module (`specs/27` 10 step 4).
- Content: `Content/*.json` of the same commit, embedded in the module. Debug list: `MONSTACK_DEBUG_PLAYERS` at build time
  (`specs/27` section 6.3).

No dashboard setup is expected beyond the service account below: the spike needed nothing enabled for Cloud Code, and private
items are read by key, so they need no index. The first remote run will confirm it for Cloud Save.

## Deploy (GitHub Pages workflow)

`.github/workflows/pages.yml` runs after a green `check` on `main`: its `module` job tests the module and deploys it with
`deploy.sh`, then the `deploy` job publishes the site. Without the secrets below the `module` job logs a notice ("Cloud Code
module not deployed") and the site deploys anyway.

**One-time setup (kj):**
1. Unity Cloud dashboard, project `e83a4b44-27a7-4f76-8769-08380b340736` (the one `web/auth.js` signs into): Administration >
   Service Accounts > New. Give it the project roles **Cloud Code Editor** and **Unity Environments Viewer** (deploy.sh lists
   the environments to find the id of `production`), and for the keep-warm schedule **Scheduler Configuration Editor** and
   **Triggers Configuration Editor** (LiveOps). Add a key; note the key id and secret.
2. GitHub, repository Settings > Secrets and variables > Actions > **Secrets**: `UGS_SERVICE_KEY_ID` (the key id) and
   `UGS_SERVICE_SECRET_KEY` (the secret).
3. Optional, same page > **Variables**: `MONSTACK_DEBUG_PLAYERS` (kj's UGS player ids, separated by spaces or commas; the
   account sheet shows a player's id), `UGS_PROJECT_ID` and `UGS_ENVIRONMENT` (default the project above and `production`,
   which must be the environment `web/auth.js` signs into: a token only works in its own environment).
4. Re-run the `pages` workflow (Actions > pages > Run workflow), or merge to `main`.

## Run it by hand

Needs a .NET 10 SDK. Do not make it the machine's default `dotnet` where the .NET 8 WebAssembly checks run (CLAUDE.md lessons):
install it into its own directory and pass its path.

```bash
# Ubuntu 24.04 without the dot.net script: unpack the distro packages into a private directory instead of installing them.
mkdir -p ~/.dotnet10-debs && cd ~/.dotnet10-debs && apt-get download dotnet-sdk-10.0 dotnet-runtime-10.0 aspnetcore-runtime-10.0 \
  dotnet-host-10.0 dotnet-hostfxr-10.0 dotnet-targeting-pack-10.0 aspnetcore-targeting-pack-10.0 dotnet-apphost-pack-10.0 dotnet-templates-10.0
for f in *.deb; do dpkg-deb -x "$f" root; done && ln -sfn ~/.dotnet10-debs/root/usr/lib/dotnet ~/.dotnet10
# or: bash dotnet-install.sh --channel 10.0 --install-dir ~/.dotnet10 --no-path
```

`./check.sh` then finds `~/.dotnet10/dotnet` (or set `MONSTACK_DOTNET10`). By hand:

```bash
DOTNET_ROOT=~/.dotnet10 ~/.dotnet10/dotnet test server/ugs/MonStackAccount.Tests -c Release -p:MonStackBuildSuffix=-net10
DOTNET=~/.dotnet10/dotnet DOTNET_ROOT=~/.dotnet10 server/ugs/deploy.sh --check          # package only
DOTNET=~/.dotnet10/dotnet DOTNET_ROOT=~/.dotnet10 server/ugs/deploy.sh MonStackAccount  # needs the variables below
```

Deploying needs `UGS_CLI_SERVICE_KEY_ID`, `UGS_CLI_SERVICE_SECRET_KEY`, `UGS_CLI_PROJECT_ID` and `UGS_CLI_ENVIRONMENT_NAME`. The
network must allow `services.api.unity.com` (admin API), `player-auth.services.api.unity.com` (sign-in) and
`cloud-code.services.api.unity.com` (calling the module).

The spike: `server/ugs/deploy.sh IdleRngBattle`, then `cd server/ugs/SpikeClient && dotnet run -c Release -- local` (or
`remote`). The first call after a deploy, or after 15 minutes without traffic, is the cold start.

## Keep-warm (issue #109): on

A cold worker takes 12 to 15 s to answer (`specs/08` section 10.1). A UGS schedule, `monstack-keep-warm`, emits an event every 10
minutes (`*/10 * * * *`, UTC) and a trigger of the same name runs `MonStackAccount/Warm` on it, so a worker stays up. About
4.3 k calls and 1 to 2 minutes of compute a month, inside Cloud Code's free tier (1 M calls, 20 hours); check the Scheduler and
Triggers prices on the UGS pricing page. It keeps one worker warm: a burst of traffic that starts more can still meet a cold one,
and so does the first call after each deploy.

`keep_warm.py` creates both through the Scheduler and Triggers admin APIs, or fixes them (a schedule cannot be edited, so a wrong
one is deleted and made again); a run with both right changes nothing. `--off` removes both. `pages.yml` runs it after it
deploys the module, unless the repo variable `MONSTACK_KEEP_WARM` is `off` (then it removes them). It needs the service account
roles **Scheduler Configuration Editor** and **Triggers Configuration Editor** (setup step 1); without them the step warns
("Keep-warm not set") and the site still deploys. `test_keep_warm.py` runs it against a fake admin API in `check.sh`.

By hand, with the deploy variables above: `python3 server/ugs/keep_warm.py` (or `--off`).
