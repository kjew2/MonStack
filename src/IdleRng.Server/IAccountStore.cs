using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IdleRng.Server
{
    /// <summary>The <c>head</c> item as stored, with the write lock that must match to replace it.</summary>
    public sealed class StoredHead
    {
        public StoredHead(string json, string writeLock)
        {
            Json = json ?? throw new ArgumentNullException(nameof(json));
            WriteLock = writeLock ?? throw new ArgumentNullException(nameof(writeLock));
        }

        public string Json { get; }
        public string WriteLock { get; }
    }

    /// <summary>
    /// Where one player's account items live (specs/27 section 3): Cloud Save's private player items on UGS (step 4), memory in
    /// tests and in the page's local test server. Item keys are opaque to the store. Only <c>head</c> is written with a lock;
    /// every other item is written once under a fresh key and deleted when nothing refers to it (copy-on-write, section 4).
    /// </summary>
    public interface IAccountStore
    {
        /// <summary>The head item, or null when the player has no account yet.</summary>
        Task<StoredHead> ReadHeadAsync(string playerId);

        /// <summary>
        /// Write the head if its lock still equals <paramref name="expectedLock"/> (null: only if no head exists yet). Returns
        /// false, writing nothing, when another write got there first.
        /// </summary>
        Task<bool> WriteHeadAsync(string playerId, string json, string expectedLock);

        /// <summary>The items with these keys; a key with no item is left out of the result.</summary>
        Task<IReadOnlyDictionary<string, string>> ReadAsync(string playerId, IReadOnlyCollection<string> keys);

        /// <summary>Write items (never <c>head</c>), replacing any with the same key.</summary>
        Task WriteAsync(string playerId, IReadOnlyDictionary<string, string> items);

        /// <summary>Delete items; a key with no item is ignored.</summary>
        Task DeleteAsync(string playerId, IReadOnlyCollection<string> keys);

        /// <summary>Every item key the player has, <c>head</c> included.</summary>
        Task<IReadOnlyList<string>> ListKeysAsync(string playerId);
    }

    /// <summary>
    /// An account store in memory, for tests and the local test server (specs/27 section 5.4). Thread-safe. Its lock is a
    /// counter, like Cloud Save's opaque write lock.
    /// </summary>
    public sealed class MemoryAccountStore : IAccountStore
    {
        public const string HeadKey = "head";

        readonly object _gate = new object();
        readonly Dictionary<string, Dictionary<string, string>> _players = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        readonly Dictionary<string, long> _locks = new Dictionary<string, long>(StringComparer.Ordinal);
        long _nextLock;

        Dictionary<string, string> Items(string playerId)
        {
            if (playerId == null) throw new ArgumentNullException(nameof(playerId));
            if (!_players.TryGetValue(playerId, out var items)) _players[playerId] = items = new Dictionary<string, string>(StringComparer.Ordinal);
            return items;
        }

        public Task<StoredHead> ReadHeadAsync(string playerId)
        {
            lock (_gate)
            {
                var items = Items(playerId);
                return Task.FromResult(items.TryGetValue(HeadKey, out var json)
                    ? new StoredHead(json, _locks[playerId].ToString(System.Globalization.CultureInfo.InvariantCulture))
                    : null);
            }
        }

        public Task<bool> WriteHeadAsync(string playerId, string json, string expectedLock)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            lock (_gate)
            {
                var items = Items(playerId);
                bool exists = items.ContainsKey(HeadKey);
                string current = exists ? _locks[playerId].ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
                if (expectedLock == null ? exists : current != expectedLock) return Task.FromResult(false);
                items[HeadKey] = json;
                _locks[playerId] = ++_nextLock;
                return Task.FromResult(true);
            }
        }

        public Task<IReadOnlyDictionary<string, string>> ReadAsync(string playerId, IReadOnlyCollection<string> keys)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            lock (_gate)
            {
                var items = Items(playerId);
                var found = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var k in keys) if (k != null && items.TryGetValue(k, out var v)) found[k] = v;
                return Task.FromResult<IReadOnlyDictionary<string, string>>(found);
            }
        }

        public Task WriteAsync(string playerId, IReadOnlyDictionary<string, string> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            lock (_gate)
            {
                var stored = Items(playerId);
                foreach (var kv in items)
                {
                    if (kv.Key == HeadKey) throw new ArgumentException("the head is written with WriteHeadAsync");
                    stored[kv.Key] = kv.Value ?? throw new ArgumentException("null item " + kv.Key);
                }
                return Task.CompletedTask;
            }
        }

        public Task DeleteAsync(string playerId, IReadOnlyCollection<string> keys)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            lock (_gate)
            {
                var stored = Items(playerId);
                foreach (var k in keys)
                {
                    if (k == HeadKey) throw new ArgumentException("the head is never deleted");
                    if (k != null) stored.Remove(k);
                }
                return Task.CompletedTask;
            }
        }

        public Task<IReadOnlyList<string>> ListKeysAsync(string playerId)
        {
            lock (_gate)
                return Task.FromResult<IReadOnlyList<string>>(Items(playerId).Keys.OrderBy(k => k, StringComparer.Ordinal).ToList());
        }
    }
}
