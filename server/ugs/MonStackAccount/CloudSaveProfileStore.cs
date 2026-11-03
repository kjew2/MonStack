using System;
using System.Threading.Tasks;
using IdleRng.Server;

namespace MonStackAccount
{
    /// <summary>
    /// The <c>profile</c> projection in Cloud Save (specs/27 section 3.3.2): the player's <b>protected</b> item <c>profile</c>, which
    /// only the server writes and the player may read. Stored as a JSON object rather than JSON text, so it can be indexed when
    /// profiles are queried across players (T12). A projection: replaced whole, no write lock.
    /// </summary>
    public sealed class CloudSaveProfileStore : IProfileStore
    {
        public const string Key = "profile";

        readonly ICloudSave _save;

        public CloudSaveProfileStore(ICloudSave save) { _save = save ?? throw new ArgumentNullException(nameof(save)); }

        public Task WriteAsync(string playerId, PlayerProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            return _save.SetAsync(CloudSaveOwner.PlayerProtected(playerId), Key, profile.ToJson(), null);
        }

        public async Task<PlayerProfile> ReadAsync(string playerId)
        {
            var it = await _save.GetOneAsync(CloudSaveOwner.PlayerProtected(playerId), Key).ConfigureAwait(false);
            return it == null ? null : PlayerProfile.FromJson(it.Value);
        }
    }
}
