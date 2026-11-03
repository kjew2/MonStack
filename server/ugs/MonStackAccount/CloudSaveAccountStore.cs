using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IdleRng.Server;

namespace MonStackAccount
{
    /// <summary>
    /// One player's account items in Cloud Save (specs/27 section 3.1): private player items under the keys the server core
    /// chooses (<c>head</c>, then <c>&lt;part&gt;_&lt;seq&gt;_&lt;tag&gt;</c> and <c>mon-&lt;id&gt;_&lt;seq&gt;_&lt;tag&gt;</c>,
    /// section 4). Only <c>head</c> is written under its Cloud Save write lock; that swap is the commit. Every other item is
    /// written once without a lock under a fresh key.
    /// </summary>
    public sealed class CloudSaveAccountStore : IAccountStore
    {
        public const string HeadKey = MemoryAccountStore.HeadKey;

        readonly ICloudSave _save;

        public CloudSaveAccountStore(ICloudSave save) { _save = save ?? throw new ArgumentNullException(nameof(save)); }

        public async Task<StoredHead> ReadHeadAsync(string playerId)
        {
            var it = await _save.GetOneAsync(CloudSaveOwner.Player(playerId), HeadKey).ConfigureAwait(false);
            if (it == null) return null;
            // A head without a lock could only be replaced unconditionally, which would make every commit race-blind.
            if (string.IsNullOrEmpty(it.WriteLock)) throw new InvalidOperationException("Cloud Save returned the head without a write lock");
            return new StoredHead(it.Value, it.WriteLock);
        }

        public Task<bool> WriteHeadAsync(string playerId, string json, string expectedLock)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            return _save.TryReplaceAsync(CloudSaveOwner.Player(playerId), HeadKey, json, expectedLock);
        }

        public async Task<IReadOnlyDictionary<string, string>> ReadAsync(string playerId, IReadOnlyCollection<string> keys)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            var found = await _save.GetManyAsync(CloudSaveOwner.Player(playerId), keys).ConfigureAwait(false);
            return found.ToDictionary(kv => kv.Key, kv => kv.Value.Value, StringComparer.Ordinal);
        }

        public async Task WriteAsync(string playerId, IReadOnlyDictionary<string, string> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var owner = CloudSaveOwner.Player(playerId);
            var batch = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in items)
            {
                if (kv.Key == HeadKey) throw new ArgumentException("the head is written with WriteHeadAsync");
                batch[kv.Key] = kv.Value ?? throw new ArgumentException("null item " + kv.Key);
                if (batch.Count == CloudSaveLimits.ItemsPerBatch)
                {
                    await _save.SetManyAsync(owner, batch).ConfigureAwait(false);
                    batch = new Dictionary<string, string>(StringComparer.Ordinal);
                }
            }
            if (batch.Count > 0) await _save.SetManyAsync(owner, batch).ConfigureAwait(false);
        }

        public async Task DeleteAsync(string playerId, IReadOnlyCollection<string> keys)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            var owner = CloudSaveOwner.Player(playerId);
            foreach (var k in keys)
            {
                if (k == HeadKey) throw new ArgumentException("the head is never deleted");
                if (k != null) await _save.DeleteAsync(owner, k).ConfigureAwait(false);
            }
        }

        public async Task<IReadOnlyList<string>> ListKeysAsync(string playerId) =>
            (await _save.AllKeysAsync(CloudSaveOwner.Player(playerId)).ConfigureAwait(false)).OrderBy(k => k, StringComparer.Ordinal).ToList();
    }
}
