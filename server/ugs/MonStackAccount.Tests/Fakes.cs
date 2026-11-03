using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.CloudCode.Core;

namespace MonStackAccount.Tests
{
    /// <summary>
    /// Cloud Save in memory, with its rules as the module relies on them: private items per player or custom entity, keys of
    /// letters, digits, '-' and '_' (at most 255), an opaque write lock that changes on every write, a locked write that fails
    /// with a conflict once the lock moved on, an unlocked write that replaces anything, at most 20 keys a read and 20 items a
    /// batch, and key listings in pages (small here, so paging is exercised). Hooks run before an operation to stage races.
    /// </summary>
    internal sealed class FakeCloudSave : ICloudSave
    {
        public const int KeyPage = 7;

        sealed class Stored { public string Value; public string Lock; }

        readonly object _gate = new object();
        readonly Dictionary<CloudSaveOwner, SortedDictionary<string, Stored>> _data = new Dictionary<CloudSaveOwner, SortedDictionary<string, Stored>>();
        long _locks;

        /// <summary>Runs (once, then clears itself) before the next operation it matches: "get", "keys", "set", "setMany", "delete".</summary>
        public readonly List<(string Op, string Key, Func<Task> Hook)> Before = new List<(string, string, Func<Task>)>();

        /// <summary>Every call, as "op owner key".</summary>
        public readonly List<string> Calls = new List<string>();

        /// <summary>When set, a set or batch whose key matches throws (a crash or a lost connection mid-commit).</summary>
        public Func<string, bool> FailWrite;

        SortedDictionary<string, Stored> Items(CloudSaveOwner o)
        {
            if (!_data.TryGetValue(o, out var d)) _data[o] = d = new SortedDictionary<string, Stored>(StringComparer.Ordinal);
            return d;
        }

        static void CheckKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 255 || key.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_')))
                throw new ArgumentException("Cloud Save rejects the key '" + key + "'");
        }

        /// <summary>A protected item holds a JSON object (UgsCloudSave writes it parsed), so it reads back as compact JSON.</summary>
        static string Stores(CloudSaveOwner owner, string value) =>
            owner.Protected ? Newtonsoft.Json.Linq.JObject.Parse(value).ToString(Newtonsoft.Json.Formatting.None) : value;

        async Task Hook(string op, string key)
        {
            Func<Task> h = null;
            lock (_gate)
            {
                int i = Before.FindIndex(b => b.Op == op && (b.Key == null || b.Key == key));
                if (i >= 0) { h = Before[i].Hook; Before.RemoveAt(i); }
            }
            if (h != null) await h();
        }

        public async Task<IReadOnlyList<SavedItem>> GetAsync(CloudSaveOwner owner, IReadOnlyList<string> keys)
        {
            if (keys.Count > CloudSaveLimits.KeysPerRead) throw new ArgumentException("Cloud Save reads at most 20 keys at once");
            foreach (var k in keys) CheckKey(k);
            await Hook("get", keys.Count == 1 ? keys[0] : null);
            lock (_gate)
            {
                Calls.Add("get " + owner + " " + string.Join(",", keys));
                var d = Items(owner);
                return keys.Where(d.ContainsKey).Select(k => new SavedItem(k, d[k].Value, d[k].Lock)).ToList();
            }
        }

        public async Task<IReadOnlyList<string>> KeysAsync(CloudSaveOwner owner, string after)
        {
            await Hook("keys", null);
            lock (_gate)
            {
                Calls.Add("keys " + owner);
                return Items(owner).Keys.Where(k => after == null || string.CompareOrdinal(k, after) > 0).Take(KeyPage).ToList();
            }
        }

        public async Task<string> SetAsync(CloudSaveOwner owner, string key, string value, string writeLock)
        {
            CheckKey(key);
            if (value == null) throw new ArgumentNullException(nameof(value));
            await Hook("set", key);
            lock (_gate)
            {
                Calls.Add("set " + owner + " " + key + (writeLock == null ? "" : " locked"));
                if (FailWrite != null && FailWrite(key)) throw new InvalidOperationException("Cloud Save unreachable");
                var d = Items(owner);
                if (writeLock != null && (!d.TryGetValue(key, out var cur) || cur.Lock != writeLock)) throw new WriteConflictException(key);
                string l = "lock" + (++_locks).ToString(CultureInfo.InvariantCulture);
                d[key] = new Stored { Value = Stores(owner, value), Lock = l };
                return l;
            }
        }

        public async Task SetManyAsync(CloudSaveOwner owner, IReadOnlyDictionary<string, string> items)
        {
            if (items.Count > CloudSaveLimits.ItemsPerBatch) throw new ArgumentException("Cloud Save batches at most 20 items");
            foreach (var k in items.Keys) CheckKey(k);
            await Hook("setMany", null);
            lock (_gate)
            {
                Calls.Add("setMany " + owner + " " + string.Join(",", items.Keys));
                if (FailWrite != null && items.Keys.Any(FailWrite)) throw new InvalidOperationException("Cloud Save unreachable");
                var d = Items(owner);
                foreach (var kv in items) d[kv.Key] = new Stored { Value = Stores(owner, kv.Value ?? throw new ArgumentNullException(kv.Key)), Lock = "lock" + (++_locks).ToString(CultureInfo.InvariantCulture) };
            }
        }

        public async Task DeleteAsync(CloudSaveOwner owner, string key)
        {
            CheckKey(key);
            await Hook("delete", key);
            lock (_gate)
            {
                Calls.Add("delete " + owner + " " + key);
                Items(owner).Remove(key);
            }
        }

        // ------------------------------------------------------------------ test access

        public List<string> Keys(CloudSaveOwner owner) { lock (_gate) return Items(owner).Keys.ToList(); }

        public string Value(CloudSaveOwner owner, string key) { lock (_gate) return Items(owner).TryGetValue(key, out var s) ? s.Value : null; }

        /// <summary>Write behind the module's back (another call's write landing, or a damaged item).</summary>
        public void Put(CloudSaveOwner owner, string key, string value) { lock (_gate) Items(owner)[key] = new Stored { Value = value, Lock = "lock" + (++_locks).ToString(CultureInfo.InvariantCulture) }; }

        /// <summary>A copy of every item, to show a call changed nothing.</summary>
        public Dictionary<string, string> Snapshot()
        {
            lock (_gate)
                return _data.SelectMany(o => o.Value.Select(kv => (o.Key + "/" + kv.Key, kv.Value.Value))).ToDictionary(x => x.Item1, x => x.Item2);
        }
    }

    /// <summary>A Cloud Code call's context as the module sees it: who called, with which tokens.</summary>
    internal sealed class FakeContext : IExecutionContext
    {
        public FakeContext(string playerId) { PlayerId = playerId; UserId = playerId; }

        public string ProjectId { get; set; } = "00000000-0000-0000-0000-000000000001";
        public string PlayerId { get; set; }
        public string EnvironmentId { get; set; } = "00000000-0000-0000-0000-000000000002";
        public string EnvironmentName { get; set; } = "test";
        public string AccessToken { get; set; } = "player-token";
        public string UserId { get; set; }
        public string Issuer { get; set; } = "test";
        public string ServiceToken { get; set; } = "service-token";
        public string AnalyticsUserId { get; set; }
        public string UnityInstallationId { get; set; }
        public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
        public string ScopeId { get; set; }
        public int CallDepth { get; set; }
        public ISession Session { get; set; }
    }
}
