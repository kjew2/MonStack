using System;
using System.Threading.Tasks;
using IdleRng.Server;
using IdleRng.Sim.Tower;
using Unity.Services.CloudCode.Apis.Extensions;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudSave.Api;

namespace MonStackAccount
{
    /// <summary>Registers what the module's constructor takes (Cloud Code's dependency injection).</summary>
    public sealed class ModuleSetup : ICloudCodeSetup
    {
        public void Setup(ICloudCodeConfig config)
        {
            config.AddCloudSaveDataClient();
        }
    }

    /// <summary>
    /// The account server on UGS Cloud Code (specs/27 section 10 step 4): a thin shell over <c>IdleRng.Server</c>. Each call
    /// builds an <see cref="AccountServer"/> over Cloud Save for the calling player and hands it the request. The player is the
    /// one the Cloud Code context names (the signed-in caller's token); no endpoint takes a player id from the request.
    /// </summary>
    public sealed class AccountModule
    {
        readonly Func<IExecutionContext, ICloudSave> _save;
        readonly ModuleSettings _settings;

        /// <summary>On Cloud Code: Cloud Save through the SDK, settings from the build.</summary>
        public AccountModule(ICloudSaveDataApi cloudSave)
            : this(ctx => new UgsCloudSave(cloudSave, ctx), ModuleSettings.FromAssembly())
        {
            if (cloudSave == null) throw new ArgumentNullException(nameof(cloudSave));
        }

        /// <summary>For the tests: any Cloud Save and settings.</summary>
        internal AccountModule(Func<IExecutionContext, ICloudSave> save, ModuleSettings settings)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        AccountServer Server(IExecutionContext context, out string playerId)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            playerId = context.PlayerId;
            if (string.IsNullOrEmpty(playerId)) throw new InvalidOperationException("this call needs a signed-in player");
            var save = _save(context);
            return new AccountServer(ModuleContent.Db, new CloudSaveAccountStore(save), _settings.ServerConfig(),
                new CloudSaveLeaderboardStore(save), new CloudSaveProfileStore(save));
        }

        /// <summary>
        /// Open the account (specs/27 section 5.1): <c>{ deviceId, claim }</c>. Creates it on the first call. The account comes back
        /// as AccountSave text, gzip then base64.
        /// </summary>
        [CloudCodeFunction("Open")]
        public async Task<OpenReply> Open(IExecutionContext context, OpenRequest request)
        {
            var server = Server(context, out var player);
            if (request == null) throw new ArgumentException("an open request is required");
            return OpenReply.From(await server.OpenAsync(player, request).ConfigureAwait(false));
        }

        /// <summary>Re-check and keep the commands since the last commit (specs/27 section 4): <c>{ deviceId, fromSeq, fromHash, commands, toHash, contentVersion }</c>.</summary>
        [CloudCodeFunction("Commit")]
        public async Task<CommitReply> Commit(IExecutionContext context, CommitRequest request)
        {
            var server = Server(context, out var player);
            return CommitReply.From(await server.CommitAsync(player, request).ConfigureAwait(false));
        }

        /// <summary>
        /// The Endless Tower season board as the caller sees it (specs/28 section 6.1): <paramref name="season"/> (negative: the
        /// server's current season) and up to <paramref name="top"/> rows (1 to 100).
        /// </summary>
        [CloudCodeFunction("EndlessBoard")]
        public async Task<BoardReply> EndlessBoard(IExecutionContext context, int season, int top)
        {
            var server = Server(context, out var player);
            if (season < 0) season = EndlessSim.SeasonOf(AccountServer.DayOf(_settings.ServerConfig().NowMs()));
            if (top <= 0) top = IdleRng.Server.EndlessBoard.DefaultTop;
            return BoardReply.From(await server.Board.ReadAsync(player, season, top).ConfigureAwait(false), player);
        }

        /// <summary>
        /// A gauntlet board as the caller sees it (specs/19 section 9): <paramref name="species"/> (a species id; empty for the
        /// overall board) and up to <paramref name="top"/> rows (1 to 100). A species not in the content is refused.
        /// </summary>
        [CloudCodeFunction("GauntletBoard")]
        public async Task<GauntletBoardReply> GauntletBoard(IExecutionContext context, string species, int top)
        {
            var server = Server(context, out var player);
            if (top <= 0) top = IdleRng.Server.GauntletBoard.DefaultTop;
            string sp = string.IsNullOrEmpty(species) ? null : species;
            return GauntletBoardReply.From(await server.Gauntlet.ReadAsync(player, sp, top).ConfigureAwait(false), player);
        }

        /// <summary>
        /// A player's profile as other players see it (specs/27 section 3.3.2): <paramref name="of"/> names whose (a UGS player id;
        /// empty for the caller's own). The caller must be signed in; the id only says whose profile to read, never who calls.
        /// </summary>
        [CloudCodeFunction("Profile")]
        public async Task<ProfileReply> Profile(IExecutionContext context, string of)
        {
            var server = Server(context, out var player);
            string whose = string.IsNullOrEmpty(of) ? player : of;
            if (whose.Length > 128) throw new ArgumentException("not a player id");
            return ProfileReply.From(await server.Profiles.ReadAsync(whose).ConfigureAwait(false));
        }

        /// <summary>
        /// Nothing but proof the worker is up and its content loaded: the keep-warm schedule's target (issue #109). Needs no
        /// player, reads no data.
        /// </summary>
        [CloudCodeFunction("Warm")]
        public WarmReply Warm() => new WarmReply
        {
            ContentVersion = ModuleContent.Db.Version,
            ServerSec = _settings.ServerConfig().NowMs() / 1000,
        };
    }
}
