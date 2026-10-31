# UGS Cloud Code spike (issue #17)

Can a Unity Gaming Services Cloud Code C# module run `IdleRng.Sim` unchanged and resolve battles to the same hash as the
client? Results and the decision live in `specs/08` section 10.1. Nothing here is part of `check.sh`: it needs credentials.

| Path | What |
|---|---|
| `IdleRngBattle/` | The module (net10.0). References `src/IdleRng.Sim` as a project, so the server compiles the client's sources. One endpoint: `ResolveFight(rules, sideA, sideB, seed)`. |
| `deploy.sh` | Publishes for linux-x64, zips `IdleRngBattle.ccm` and creates or updates the module through the Cloud Code Admin API. |
| `SpikeClient/` | The test script: 7 `BattleVectors` fights and 40 Daily Tower floors. `local` checks them in process and regenerates the golden files on .NET 10; `remote` calls the deployed module as an anonymous player and measures latency. |

## Run it

Needs the .NET 10 SDK (`apt-get install dotnet-sdk-10.0` on Ubuntu 24.04; `global.json` here selects it) and these
environment variables: `UGS_CLI_SERVICE_KEY_ID`, `UGS_CLI_SERVICE_SECRET_KEY`, `UGS_CLI_PROJECT_ID`, `UGS_CLI_ENVIRONMENT_NAME`.
The network must allow `services.api.unity.com` (admin API), `player-auth.services.api.unity.com` (sign-in) and
`cloud-code.services.api.unity.com` (calling the module).

```bash
server/ugs/deploy.sh
```

```bash
cd server/ugs/SpikeClient && dotnet run -c Release -- local
```

```bash
cd server/ugs/SpikeClient && dotnet run -c Release -- remote
```

The first `remote` call after a deploy, or after 15 minutes without traffic, is the cold start.
