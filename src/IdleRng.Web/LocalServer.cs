using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IdleRng.Runtime.Save;
using IdleRng.Server;
using IdleRng.Sim.Account;
using IdleRng.Sim.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IdleRng.Web
{
    /// <summary>
    /// The local test server (specs/27 section 5.4): the server core (IdleRng.Server.AccountServer) run inside the page, over a
    /// store the page keeps in localStorage. Stateless: every call takes the store's text and returns it changed, so a second tab
    /// or a reload always sees the last write. The JSON shapes are the transport contract of web/sync.js (web/wasm/BRIDGE.md,
    /// "Sync transport"), the same a remote endpoint answers with. No anti-cheat value: the page holds the server's store.
    /// </summary>
    internal static class LocalServer
    {
        /// <summary>
        /// One call: <paramref name="op"/> is "open", "commit" or "install" (a test and development hook: store an account as the
        /// player's new head). Returns {"response": {...}, "store": "..."}; a server error comes back as {"error"} with the store
        /// unchanged.
        /// </summary>
        public static string Call(ContentDb db, string op, string playerId, string requestJson, string storeText, double nowMs)
        {
            var store = LocalStore.Parse(storeText);
            var req = string.IsNullOrWhiteSpace(requestJson) ? new JObject() : JObject.Parse(requestJson);
            long now = (long)Math.Floor(nowMs);
            var cfg = new ServerConfig { NowMs = () => now };
            cfg.DebugPlayers.Add(playerId ?? "");   // debug commands are allowed on the local test server (no anti-cheat value)
            var server = new AccountServer(db, store, cfg);
            JObject res;
            switch (op)
            {
                case "open": res = Open(server, playerId, req); break;
                case "commit": res = Commit(server, playerId, req); break;
                case "install": res = Install(db, store, cfg, playerId, req, now); break;
                default: throw new ArgumentException("unknown local server call '" + op + "'");
            }
            return new JObject { ["response"] = res, ["store"] = store.ToText() }.ToString(Formatting.None);
        }

        static T Wait<T>(Task<T> t) => t.GetAwaiter().GetResult();   // the store is in memory: every task has completed

        static JObject Open(AccountServer server, string playerId, JObject req)
        {
            var r = Wait(server.OpenAsync(playerId, new OpenRequest { DeviceId = (string)req["deviceId"], Claim = req.Value<bool?>("claim") ?? false }));
            return new JObject
            {
                ["code"] = r.Code.ToString(), ["account"] = r.Account, ["notice"] = r.Notice, ["seq"] = r.Seq, ["hash"] = r.Hash,
                ["created"] = r.Created, ["contentVersion"] = r.ContentVersion, ["serverSec"] = r.ServerSec, ["day"] = r.Day,
            };
        }

        static JObject Commit(AccountServer server, string playerId, JObject req)
        {
            var c = new CommitRequest
            {
                DeviceId = (string)req["deviceId"],
                FromSeq = req.Value<int?>("fromSeq") ?? -1,
                FromHash = (string)req["fromHash"],
                ToHash = (string)req["toHash"],
                ContentVersion = (string)req["contentVersion"],
                Commands = null,
            };
            if (req["commands"] is JArray cmds)
            {
                c.Commands = new List<WireCommand>();
                foreach (var t in cmds)
                    c.Commands.Add(t is JObject o ? new WireCommand { K = (string)o["k"], A = o["a"] as JArray ?? new JArray() } : null);
            }
            var r = Wait(server.CommitAsync(playerId, c));
            return new JObject
            {
                ["code"] = r.Code.ToString(), ["index"] = r.Index, ["reason"] = r.Reason, ["seq"] = r.Seq, ["hash"] = r.Hash,
                ["serverSec"] = r.ServerSec, ["day"] = r.Day,
            };
        }

        /// <summary>The content marker of an installed head: not the server's, so the next commit loads and rewrites every part.</summary>
        public const string InstalledContent = "local-install";

        /// <summary>
        /// Store an account (AccountSave text, {account, deviceId}) as the player's new head, one commit past the old one and held
        /// by the device, so the device's next open replaces its copy. A test and development hook of the local test server only:
        /// the browser checks seed accounts this way. The run in progress is stored as written, so a run that no longer loads is
        /// dropped by the next open as on a real server after a content change. Vault entries need unique ids (AccountParts.Split).
        /// </summary>
        static JObject Install(ContentDb db, LocalStore store, ServerConfig cfg, string playerId, JObject req, long now)
        {
            if (string.IsNullOrEmpty(playerId)) throw new ArgumentException("a player id is required");
            string device = (string)req["deviceId"];
            if (string.IsNullOrEmpty(device)) throw new ArgumentException("a device id is required");
            var (_, body) = SaveSerializer.Decode(Encoding.UTF8.GetBytes((string)req["account"] ?? ""));
            var run = body["Run"];
            body.Remove("Run");
            var account = AccountSave.Decode(db, SaveSerializer.EncodeBody(body.ToString(Formatting.None)), out _);
            var items = AccountParts.Split(account);
            if (run is JObject)
            {
                var training = JObject.Parse(items[AccountParts.Training]);
                training["Run"] = run;
                items[AccountParts.Training] = training.ToString(Formatting.None);
            }

            var prev = Wait(store.ReadHeadAsync(playerId));
            int seq = prev == null ? 0 : AccountHead.FromJson(prev.Json).Seq + 1;
            string tag = (cfg.Random() & 0xFFFFFFFFUL).ToString("x8", CultureInfo.InvariantCulture);
            var head = new AccountHead
            {
                Seq = seq,
                Holder = new Holder { DeviceId = device, SinceSec = now / 1000 },
                Schema = AccountState.CurrentSchemaVersion,
                Content = InstalledContent,
            };
            var write = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in items)
            {
                if (kv.Key.StartsWith(AccountParts.EntryPrefix, StringComparison.Ordinal))
                {
                    int id = int.Parse(kv.Key.Substring(AccountParts.EntryPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture);
                    string key = AccountHead.ItemKey(AccountHead.EntryName(id), seq, tag);
                    head.Entries[id] = key;
                    write[key] = kv.Value;
                }
                else
                {
                    string key = AccountHead.ItemKey(kv.Key, seq, tag);
                    head.Parts[kv.Key] = key;
                    head.Hashes[kv.Key] = AccountParts.Hash(kv.Value);
                    write[key] = kv.Value;
                }
            }
            head.Hash = AccountParts.AccountHashOf(head.Hashes);
            store.WriteAsync(playerId, write).GetAwaiter().GetResult();
            if (!Wait(store.WriteHeadAsync(playerId, head.ToJson(), prev?.WriteLock))) throw new InvalidOperationException("the account changed while it was installed");
            return new JObject { ["seq"] = head.Seq, ["hash"] = head.Hash };   // the old items are swept by the next open
        }
    }

    /// <summary>
    /// The local test server's account store: MemoryAccountStore's rules (a head replaced only under its lock, a counter) over
    /// data that round-trips through text, so the page can keep it in localStorage between calls.
    /// </summary>
    internal sealed class LocalStore : IAccountStore
    {
        const string HeadKey = "head";

        sealed class Player
        {
            public long Lock;
            public readonly SortedDictionary<string, string> Items = new SortedDictionary<string, string>(StringComparer.Ordinal);
        }

        readonly SortedDictionary<string, Player> _players = new SortedDictionary<string, Player>(StringComparer.Ordinal);
        long _next;

        /// <summary>The store from its text ({"next", "players": {id: {"lock", "items": {key: text}}}}); empty text is an empty store.</summary>
        public static LocalStore Parse(string text)
        {
            var s = new LocalStore();
            if (string.IsNullOrWhiteSpace(text)) return s;
            var o = JObject.Parse(text);
            s._next = o.Value<long?>("next") ?? 0;
            if (o["players"] is JObject players)
                foreach (var p in players.Properties())
                {
                    var pl = new Player { Lock = p.Value.Value<long?>("lock") ?? 0 };
                    if (p.Value["items"] is JObject items) foreach (var it in items.Properties()) pl.Items[it.Name] = (string)it.Value;
                    s._players[p.Name] = pl;
                }
            return s;
        }

        public string ToText()
        {
            var players = new JObject();
            foreach (var kv in _players)
            {
                var items = new JObject();
                foreach (var it in kv.Value.Items) items[it.Key] = it.Value;
                players[kv.Key] = new JObject { ["lock"] = kv.Value.Lock, ["items"] = items };
            }
            return new JObject { ["next"] = _next, ["players"] = players }.ToString(Formatting.None);
        }

        Player Of(string playerId)
        {
            if (playerId == null) throw new ArgumentNullException(nameof(playerId));
            if (!_players.TryGetValue(playerId, out var p)) _players[playerId] = p = new Player();
            return p;
        }

        static string LockText(long n) => n.ToString(CultureInfo.InvariantCulture);

        public Task<StoredHead> ReadHeadAsync(string playerId)
        {
            var p = Of(playerId);
            return Task.FromResult(p.Items.TryGetValue(HeadKey, out var json) ? new StoredHead(json, LockText(p.Lock)) : null);
        }

        public Task<bool> WriteHeadAsync(string playerId, string json, string expectedLock)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var p = Of(playerId);
            bool exists = p.Items.ContainsKey(HeadKey);
            if (expectedLock == null ? exists : !exists || LockText(p.Lock) != expectedLock) return Task.FromResult(false);
            p.Items[HeadKey] = json;
            p.Lock = ++_next;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyDictionary<string, string>> ReadAsync(string playerId, IReadOnlyCollection<string> keys)
        {
            var p = Of(playerId);
            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var k in keys) if (k != null && p.Items.TryGetValue(k, out var v)) found[k] = v;
            return Task.FromResult<IReadOnlyDictionary<string, string>>(found);
        }

        public Task WriteAsync(string playerId, IReadOnlyDictionary<string, string> items)
        {
            var p = Of(playerId);
            foreach (var kv in items)
            {
                if (kv.Key == HeadKey) throw new ArgumentException("the head is written with WriteHeadAsync");
                p.Items[kv.Key] = kv.Value ?? throw new ArgumentException("null item " + kv.Key);
            }
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string playerId, IReadOnlyCollection<string> keys)
        {
            var p = Of(playerId);
            foreach (var k in keys)
            {
                if (k == HeadKey) throw new ArgumentException("the head is never deleted");
                if (k != null) p.Items.Remove(k);
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListKeysAsync(string playerId) =>
            Task.FromResult<IReadOnlyList<string>>(Of(playerId).Items.Keys.ToList());
    }
}
