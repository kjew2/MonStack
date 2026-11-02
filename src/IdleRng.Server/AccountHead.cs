using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

namespace IdleRng.Server
{
    /// <summary>The device that holds the account (specs/27 section 6.1).</summary>
    public sealed class Holder
    {
        public string DeviceId { get; set; }
        public long SinceSec { get; set; }
    }

    /// <summary>
    /// The root item (specs/27 section 3.1): the commit number and account hash, where each item of this commit lives and each
    /// part's hash, the holder, the schema and content versions, and whether a debug command was ever used. Replacing it under
    /// its write lock is the commit.
    /// </summary>
    public sealed class AccountHead
    {
        /// <summary>The commit number: 0 at genesis, +1 for every accepted commit.</summary>
        public int Seq { get; set; }

        /// <summary>The account hash (AccountParts.AccountHashOf the part hashes).</summary>
        public string Hash { get; set; }

        /// <summary>The account hash before the last commit, so a retry of it is recognised (section 4, idempotent).</summary>
        public string PrevHash { get; set; }

        /// <summary>Part name (training, wallet, ...) to the stored key of its current version.</summary>
        public SortedDictionary<string, string> Parts { get; set; } = new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Part name to the SHA-256 of its text.</summary>
        public SortedDictionary<string, string> Hashes { get; set; } = new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Vault entry id to the stored key of its item (an entry never changes, so its key stays until it is dismantled).</summary>
        public SortedDictionary<int, string> Entries { get; set; } = new SortedDictionary<int, string>();

        public Holder Holder { get; set; }

        /// <summary>The save schema the parts are written in (AccountState.CurrentSchemaVersion).</summary>
        public int Schema { get; set; }

        /// <summary>The content version the parts were last written under.</summary>
        public string Content { get; set; }

        /// <summary>A debug command was applied to this account (section 6.3): leaderboards and PvP leave it out.</summary>
        public bool DebugUsed { get; set; }

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            Culture = CultureInfo.InvariantCulture,
        };

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None, Settings);

        public static AccountHead FromJson(string json)
        {
            AccountHead h;
            try { h = JsonConvert.DeserializeObject<AccountHead>(json, Settings); }
            catch (JsonException e) { throw new AccountDamagedException("the head item is not readable: " + e.Message, e); }
            if (h == null || h.Hash == null || h.Parts == null || h.Hashes == null || h.Entries == null || h.Holder?.DeviceId == null)
                throw new AccountDamagedException("the head item is incomplete");
            return h;
        }

        /// <summary>
        /// The stored key of a part or entry written by commit <paramref name="seq"/>: <c>&lt;name&gt;_&lt;seq&gt;_&lt;tag&gt;</c>, where the
        /// tag is random per commit attempt, so two attempts from the same base never write the same key. Letters, digits, '-'
        /// and '_' only (Cloud Save's key alphabet).
        /// </summary>
        public static string ItemKey(string name, int seq, string tag) =>
            name + "_" + seq.ToString(CultureInfo.InvariantCulture) + "_" + tag;

        /// <summary>The stored name of a Vault entry (its key without the commit and tag).</summary>
        public static string EntryName(int id) => "mon-" + id.ToString(CultureInfo.InvariantCulture);

        /// <summary>The commit number a stored key was written by, or -1 for a key not in <see cref="ItemKey"/> form.</summary>
        public static int SeqOf(string key)
        {
            if (key == null) return -1;
            int tag = key.LastIndexOf('_');
            if (tag <= 0) return -1;
            int seq = key.LastIndexOf('_', tag - 1);
            if (seq < 0) return -1;
            return int.TryParse(key.Substring(seq + 1, tag - seq - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1;
        }
    }
}
