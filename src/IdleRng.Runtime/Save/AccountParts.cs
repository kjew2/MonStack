using System;
using System.Collections.Generic;
using System.Text;
using IdleRng.Sim;
using IdleRng.Sim.Account;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IdleRng.Runtime.Save
{
    /// <summary>
    /// The account as the server stores it (specs/27 section 3): one JSON item per part, split by which commands write it, plus
    /// one item per Vault entry. Each part's JSON is what the account save writes for those fields (same serializer and
    /// settings on every host), so a field added to AccountState lands in a part without a hand-kept field list; a test checks
    /// that every field is mapped. <see cref="Join"/> loads the parts back through AccountSave, so the run in progress is
    /// rebuilt and checked exactly as on a load.
    /// </summary>
    public static class AccountParts
    {
        public const string Training = "training", Wallet = "wallet", Collection = "collection", Progress = "progress",
            Dungeon = "dungeon", Endless = "endless", Rulebooks = "rulebooks", Vault = "vault", EntryPrefix = "mon.";

        /// <summary>The fields of AccountState each part holds (SchemaVersion and ContentVersion belong to the server's head).</summary>
        public static readonly IReadOnlyDictionary<string, string[]> Fields = new Dictionary<string, string[]>
        {
            [Training] = new[] { nameof(AccountState.Seed), nameof(AccountState.RunsStarted), nameof(AccountState.TalentsOff), nameof(AccountState.Vendor) },
            [Wallet] = new[] { nameof(AccountState.Shards), nameof(AccountState.Books) },
            [Collection] = new[] { nameof(AccountState.OwnedSpecies), nameof(AccountState.Talents) },
            [Progress] = new[] { nameof(AccountState.Achievements), nameof(AccountState.Records), nameof(AccountState.Name), nameof(AccountState.Fortuna), nameof(AccountState.Lineages) },
            [Dungeon] = new[] { nameof(AccountState.Tower) },
            [Endless] = new[] { nameof(AccountState.Endless) },
            [Rulebooks] = new[] { nameof(AccountState.Rulebooks) },
        };

        /// <summary>AccountState fields that are not in a part's field list: the head's metadata, and the two kept in their own shape.</summary>
        public static readonly string[] Unlisted = { nameof(AccountState.SchemaVersion), nameof(AccountState.ContentVersion),
            nameof(AccountState.Active), nameof(AccountState.Vault) };

        /// <summary>The item key of a Vault entry.</summary>
        public static string EntryKey(int id) => EntryPrefix + id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The part flag of an item key (every mon.&lt;id&gt; is AccountPart.Entries).</summary>
        public static AccountPart PartOf(string key)
        {
            switch (key)
            {
                case Training: return AccountPart.Training;
                case Wallet: return AccountPart.Wallet;
                case Collection: return AccountPart.Collection;
                case Progress: return AccountPart.Progress;
                case Dungeon: return AccountPart.Dungeon;
                case Endless: return AccountPart.Endless;
                case Rulebooks: return AccountPart.Rulebooks;
                case Vault: return AccountPart.Vault;
            }
            if (key != null && key.StartsWith(EntryPrefix, StringComparison.Ordinal)) return AccountPart.Entries;
            throw new ArgumentException("not an account part: " + key);
        }

        /// <summary>
        /// The account as items: compact JSON by key, in key order. The run in progress goes into training as its replay recipe;
        /// the vault part holds the entries' ids in Vault order, the locked ids and each entry's hash. Throws ArgumentException
        /// when Vault entry ids are not unique and non-negative (only an account migrated from an old local save can have them).
        /// </summary>
        public static SortedDictionary<string, string> Split(AccountState a)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            var acct = JObject.FromObject(a, SaveSerializer.Serializer);
            var items = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in Fields)
            {
                var part = new JObject();
                foreach (var f in kv.Value) if (acct[f] != null) part[f] = acct[f].DeepClone();   // left out = its starting value
                if (kv.Key == Training && a.Active != null) part["Run"] = AccountSave.Snapshot(a.Active);
                items[kv.Key] = Text(part);
            }

            var order = new JArray();
            var locked = new JArray();
            var hashes = new JArray();
            var seen = new HashSet<int>();
            foreach (var e in a.Vault)
            {
                if (e.Id < 0 || !seen.Add(e.Id)) throw new ArgumentException("Vault entry ids must be unique and non-negative (id " + e.Id + ")");
                var o = JObject.FromObject(e, SaveSerializer.Serializer);
                o.Remove(nameof(VaultEntry.Locked));   // the player's flag lives in the index, so an entry item never changes
                string text = Text(o);
                items[EntryKey(e.Id)] = text;
                order.Add(e.Id);
                if (e.Locked) locked.Add(e.Id);
                hashes.Add(ShortHash(text));
            }
            items[Vault] = Text(new JObject { ["Order"] = order, ["Locked"] = locked, ["Hashes"] = hashes });
            return items;
        }

        /// <summary>
        /// The oldest save schema items can be stored in: v10 gave the parts their shape (a sealed monster as its result with an
        /// <c>Id</c>, the run in progress as its state), and the migrations before it rebuild fields the items no longer hold.
        /// </summary>
        public const int FirstSchema = 10;

        /// <summary>
        /// The account from its items, written in the current save schema. Every part must be present, and every entry the vault
        /// part lists, matching its hash. Loads through AccountSave.Decode, so <paramref name="notice"/> is that of a load (a run
        /// in progress dropped under new rules, a content version change). Throws SaveInvalidException for a missing or damaged item.
        /// </summary>
        public static AccountState Join(ContentDb c, IReadOnlyDictionary<string, string> items, out string notice) =>
            Join(c, items, AccountState.CurrentSchemaVersion, out notice);

        /// <summary>
        /// The account from items written in save schema <paramref name="schema"/> (the server's <c>head.Schema</c>, specs/27
        /// section 7): the items are joined into a save body of that schema, which AccountSave.Decode migrates forward exactly as
        /// it migrates a device's save (specs/06 section 5), so <see cref="Split"/> of the result gives the items in the current
        /// schema. Throws SaveInvalidException for a schema before <see cref="FirstSchema"/> or newer than the current one.
        /// </summary>
        public static AccountState Join(ContentDb c, IReadOnlyDictionary<string, string> items, int schema, out string notice)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (schema < FirstSchema || schema > AccountState.CurrentSchemaVersion)
                throw new SaveInvalidException("account parts in schema v" + schema + " are not supported (v" + FirstSchema + " to v" + AccountState.CurrentSchemaVersion + ")");
            JObject Part(string key)
            {
                if (!items.TryGetValue(key, out var t) || t == null) throw new SaveInvalidException("account part missing: " + key);
                try { return JObject.Parse(t); }
                catch (JsonException e) { throw new SaveInvalidException("account part " + key + " is not JSON: " + e.Message, e); }
            }
            var acct = new JObject();
            JToken run = null;
            foreach (var kv in Fields)
            {
                var part = Part(kv.Key);
                foreach (var f in kv.Value) if (part[f] != null) acct[f] = part[f].DeepClone();
                if (kv.Key == Training) run = part["Run"]?.DeepClone();
            }

            var vault = Part(Vault);
            var order = vault["Order"] as JArray ?? throw new SaveInvalidException("vault order missing");
            var hashes = vault["Hashes"] as JArray ?? throw new SaveInvalidException("vault hashes missing");
            if (hashes.Count != order.Count) throw new SaveInvalidException("vault order and hashes differ in length");
            int Id(JToken t) => t != null && t.Type == JTokenType.Integer ? (int)t : throw new SaveInvalidException("vault ids must be whole numbers");
            var lockedIds = new HashSet<int>();
            if (vault["Locked"] is JArray lk) foreach (var t in lk) lockedIds.Add(Id(t));
            var entries = new JArray();
            for (int i = 0; i < order.Count; i++)
            {
                int id = Id(order[i]);
                string key = EntryKey(id);
                if (!items.TryGetValue(key, out var text) || text == null) throw new SaveInvalidException("Vault entry missing: " + key);
                if (ShortHash(text) != (string)hashes[i]) throw new SaveInvalidException("Vault entry " + key + " does not match its hash");
                var e = JObject.Parse(text);
                e[nameof(VaultEntry.Locked)] = lockedIds.Contains(id);
                entries.Add(e);
            }
            acct[nameof(AccountState.Vault)] = entries;
            acct[nameof(AccountState.SchemaVersion)] = schema;
            acct[nameof(AccountState.ContentVersion)] = c.Version;

            var body = new JObject
            {
                ["Kind"] = AccountSave.Kind,
                ["SchemaVersion"] = schema,
                ["Account"] = acct,
            };
            if (run is JObject) body["Run"] = run;
            return AccountSave.Decode(c, SaveSerializer.EncodeBody(Text(body)), out notice);
        }

        /// <summary>
        /// The first 16 hex characters of an entry's hash, as the vault index keeps them (kj, 2026-11-01: enough to catch a damaged
        /// item; the full hash would be 64).
        /// </summary>
        public static string ShortHash(string json) => Hash(json).Substring(0, 16);

        /// <summary>SHA-256 (hex) of an item's JSON text.</summary>
        public static string Hash(string json) => Sha256.Hex(Sha256.Compute(Encoding.UTF8.GetBytes(json ?? "")));

        /// <summary>
        /// The account hash (specs/27 section 3.2): SHA-256 over the sorted "key hash" lines of every part except the entries,
        /// which the vault part pins by their own hashes.
        /// </summary>
        public static string AccountHash(IReadOnlyDictionary<string, string> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in items) if (!kv.Key.StartsWith(EntryPrefix, StringComparison.Ordinal)) hashes[kv.Key] = Hash(kv.Value);
            return AccountHashOf(hashes);
        }

        /// <summary>
        /// The account hash from the parts' own hashes (part key to <see cref="Hash"/> of its text, entries left out), for a host
        /// that holds the hashes of parts it has not loaded (the server's head, specs/27 section 4).
        /// </summary>
        public static string AccountHashOf(IReadOnlyDictionary<string, string> partHashes)
        {
            if (partHashes == null) throw new ArgumentNullException(nameof(partHashes));
            var keys = new List<string>(partHashes.Keys);
            keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (var k in keys) sb.Append(k).Append(' ').Append(partHashes[k]).Append('\n');
            return Hash(sb.ToString());
        }

        static string Text(JToken t) => t.ToString(Formatting.None);
    }
}
