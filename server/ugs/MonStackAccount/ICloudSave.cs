using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MonStackAccount
{
    /// <summary>Whose items: a player's (the account, specs/27 section 3.1) or a custom entity's (game-wide data such as a board).</summary>
    public readonly struct CloudSaveOwner : IEquatable<CloudSaveOwner>
    {
        CloudSaveOwner(string id, bool custom) { Id = id; Custom = custom; }

        public string Id { get; }

        /// <summary>True for a custom entity (a custom id), false for a player.</summary>
        public bool Custom { get; }

        public static CloudSaveOwner Player(string playerId) =>
            new CloudSaveOwner(string.IsNullOrEmpty(playerId) ? throw new ArgumentException("a player id is required") : playerId, false);

        public static CloudSaveOwner Entity(string customId) =>
            new CloudSaveOwner(string.IsNullOrEmpty(customId) ? throw new ArgumentException("a custom id is required") : customId, true);

        public bool Equals(CloudSaveOwner o) => Id == o.Id && Custom == o.Custom;
        public override bool Equals(object obj) => obj is CloudSaveOwner o && Equals(o);
        public override int GetHashCode() => (Id ?? "").GetHashCode() * 2 + (Custom ? 1 : 0);
        public override string ToString() => (Custom ? "custom:" : "player:") + Id;
    }

    /// <summary>One stored item: its text and its write lock.</summary>
    public sealed class SavedItem
    {
        public SavedItem(string key, string value, string writeLock)
        {
            Key = key;
            Value = value;
            WriteLock = writeLock;
        }

        public string Key { get; }
        public string Value { get; }
        public string WriteLock { get; }
    }

    /// <summary>A write named a write lock that is no longer the item's (Cloud Save answers HTTP 409).</summary>
    public sealed class WriteConflictException : Exception
    {
        public WriteConflictException(string key) : base("write lock conflict on " + key) { }
    }

    /// <summary>
    /// The slice of UGS Cloud Save the module uses: <b>private</b> items only (the access class only the server reads and writes,
    /// specs/27 section 3.3.1), of a player or of a custom entity. Values are JSON text stored as a JSON string, so a stored part
    /// reads back byte for byte and its hash holds. Write-lock semantics are Cloud Save's: a write that names a lock replaces the
    /// item only while the lock is current, and one that names none replaces it unconditionally (or creates it). Implemented by
    /// <see cref="UgsCloudSave"/> on Cloud Code and by an in-memory fake in the tests.
    /// </summary>
    public interface ICloudSave
    {
        /// <summary>The items with these keys (at most <see cref="CloudSaveLimits.KeysPerRead"/>); a missing key is left out.</summary>
        Task<IReadOnlyList<SavedItem>> GetAsync(CloudSaveOwner owner, IReadOnlyList<string> keys);

        /// <summary>One page of the owner's keys, after <paramref name="after"/> (null for the first page); an empty page ends it.</summary>
        Task<IReadOnlyList<string>> KeysAsync(CloudSaveOwner owner, string after);

        /// <summary>
        /// Write one item. With <paramref name="writeLock"/>: only if it is still the item's lock, else WriteConflictException.
        /// Without: unconditionally. Returns the item's new write lock.
        /// </summary>
        Task<string> SetAsync(CloudSaveOwner owner, string key, string value, string writeLock);

        /// <summary>Write up to <see cref="CloudSaveLimits.ItemsPerBatch"/> items unconditionally.</summary>
        Task SetManyAsync(CloudSaveOwner owner, IReadOnlyDictionary<string, string> items);

        /// <summary>Delete one item; a missing one is fine.</summary>
        Task DeleteAsync(CloudSaveOwner owner, string key);
    }

    /// <summary>Request sizes the stores keep to (Cloud Save's documented page and batch sizes).</summary>
    public static class CloudSaveLimits
    {
        public const int KeysPerRead = 20;
        public const int ItemsPerBatch = 20;
    }

    /// <summary>Helpers over <see cref="ICloudSave"/> shared by the account store and the board store.</summary>
    public static class CloudSaveExtensions
    {
        /// <summary>Read any number of keys, <see cref="CloudSaveLimits.KeysPerRead"/> at a time.</summary>
        public static async Task<Dictionary<string, SavedItem>> GetManyAsync(this ICloudSave save, CloudSaveOwner owner, IEnumerable<string> keys)
        {
            var found = new Dictionary<string, SavedItem>(StringComparer.Ordinal);
            var batch = new List<string>(CloudSaveLimits.KeysPerRead);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var k in keys)
            {
                if (k == null || !seen.Add(k)) continue;
                batch.Add(k);
                if (batch.Count == CloudSaveLimits.KeysPerRead) await Flush().ConfigureAwait(false);
            }
            await Flush().ConfigureAwait(false);
            return found;

            async Task Flush()
            {
                if (batch.Count == 0) return;
                foreach (var it in await save.GetAsync(owner, batch.ToArray()).ConfigureAwait(false))
                    if (it != null && it.Value != null) found[it.Key] = it;
                batch.Clear();
            }
        }

        /// <summary>One item, or null.</summary>
        public static async Task<SavedItem> GetOneAsync(this ICloudSave save, CloudSaveOwner owner, string key)
        {
            var r = await save.GetManyAsync(owner, new[] { key }).ConfigureAwait(false);
            return r.TryGetValue(key, out var it) ? it : null;
        }

        /// <summary>Every key the owner has, following the pages.</summary>
        public static async Task<List<string>> AllKeysAsync(this ICloudSave save, CloudSaveOwner owner)
        {
            var all = new List<string>();
            string after = null;
            for (int guard = 0; guard < 10_000; guard++)
            {
                var page = await save.KeysAsync(owner, after).ConfigureAwait(false);
                if (page == null || page.Count == 0) return all;
                all.AddRange(page);
                string last = page[page.Count - 1];
                if (last == after) return all;
                after = last;
            }
            throw new InvalidOperationException("the key listing of " + owner + " did not end");
        }

        /// <summary>
        /// Replace an item only if its write lock is still <paramref name="expectedLock"/>; with null, only create it. Returns false,
        /// having changed nothing, when another write got there first.
        ///
        /// Cloud Save has no create-if-absent write (a write without a lock replaces whatever is there), so a create checks that
        /// the key is absent, writes without a lock, then reads the item back and keeps the write only if its lock is still the one
        /// this write got. Two creates racing inside one round trip can both pass: the later one replaces the earlier. For the
        /// account head that is a genesis race (two first opens at the same moment), where the overwritten account had no commit
        /// yet and its device gets Stale and re-opens; for a board shard it loses a row until that player's next climb
        /// (agent, 2026-11-03: specs/27 section 10 step 4).
        /// </summary>
        public static async Task<bool> TryReplaceAsync(this ICloudSave save, CloudSaveOwner owner, string key, string value, string expectedLock)
        {
            if (expectedLock != null)
            {
                try
                {
                    await save.SetAsync(owner, key, value, expectedLock).ConfigureAwait(false);
                    return true;
                }
                catch (WriteConflictException) { return false; }
            }
            if (await save.GetOneAsync(owner, key).ConfigureAwait(false) != null) return false;
            string mine = await save.SetAsync(owner, key, value, null).ConfigureAwait(false);
            var now = await save.GetOneAsync(owner, key).ConfigureAwait(false);
            return now != null && now.WriteLock == mine;
        }
    }
}
