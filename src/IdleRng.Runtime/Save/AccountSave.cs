using System;
using System.Collections.Generic;
using IdleRng.Sim.Account;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IdleRng.Runtime.Save
{
    /// <summary>
    /// The monster-trainer account save (specs/18 section 6): a new save lineage in its own store slot (specs/13 section 1).
    /// Same file shape as SaveSerializer (header line with CRC, then JSON):
    /// <code>
    /// { "Kind": "monster_account", "SchemaVersion": 3, "Account": { ...AccountState without Active... },
    ///   "ActiveRun": { "SpeciesId", "Seed", "Books", "Mods", "Log", "Hash" } | null }
    /// </code>
    /// The run in progress is stored as its replay recipe and rebuilt by TrainingRun.Replay on load; the stored hash catches
    /// a recipe that no longer replays to the same run (for example after a rules change). Such a run is dropped with a
    /// notice and the rest of the account still loads (specs/06 section 5).
    /// </summary>
    public static class AccountSave
    {
        public const string Kind = "monster_account";

        /// <summary>Store slot name for FileSaveStore, so the account never shares files with the idle save (slot0).</summary>
        public const string SlotName = "account";

        public static byte[] Encode(AccountState a)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            var acct = JObject.FromObject(a, SaveSerializer.Serializer);
            acct.Remove(nameof(AccountState.Active));
            var body = new JObject
            {
                ["Kind"] = Kind,
                ["SchemaVersion"] = AccountState.CurrentSchemaVersion,
                ["Account"] = acct,
                ["ActiveRun"] = a.Active == null ? JValue.CreateNull() : Recipe(a.Active),
            };
            return SaveSerializer.EncodeBody(body.ToString(SaveSerializer.Indented ? Formatting.Indented : Formatting.None));
        }

        /// <summary>
        /// Decode and rebuild an account. Throws SaveCorruptException for a damaged file, SaveInvalidException for a file that
        /// is intact but not a loadable account at this schema (wrong kind, newer schema).
        /// <paramref name="notice"/> is null for a clean load, or a sentence for the player when the load had to give something up
        /// (specs/06 section 5): a run in progress that no longer replays under the current rules is dropped, its books return to
        /// the inventory and the rest of the account (shards, talents, Vault) loads as saved.
        /// </summary>
        public static AccountState Decode(ContentDb c, byte[] file, out string notice)
        {
            notice = null;
            var (version, body) = SaveSerializer.Decode(file);
            if ((string)body["Kind"] != Kind) throw new SaveInvalidException($"not an account save (Kind '{body["Kind"]}')");
            int savedVersion = version;
            if (version >= 1 && version < AccountState.CurrentSchemaVersion) version = Migrate(body, version);
            if (version != AccountState.CurrentSchemaVersion)
                throw new SaveInvalidException($"account schema v{version} is not supported (current v{AccountState.CurrentSchemaVersion})");
            if (!(body["Account"] is JObject acct)) throw new SaveInvalidException("Account section missing");
            AccountState a;
            try { a = acct.ToObject<AccountState>(SaveSerializer.Serializer); }
            catch (JsonException e) { throw new SaveInvalidException("Account does not deserialise: " + e.Message, e); }
            if (a == null) throw new SaveInvalidException("Account is null");
            if (savedVersion < 3) AccountOps.RebuildRecords(a);   // v2 -> v3: records did not exist; rebuilt from the Vault
            if (a.Achievements == null) a.Achievements = new List<string>();   // a null in the file reads as none
            if (a.Records == null) AccountOps.RebuildRecords(a);

            if (body["ActiveRun"] is JObject run)
            {
                RunRecipe r = null;
                try
                {
                    r = run.ToObject<RunRecipe>(SaveSerializer.Serializer);
                    a.Active = TrainingRun.Replay(c, r.SpeciesId, r.Seed, r.Log, r.Books, r.Mods);
                    if (r.Hash != TrainingRun.StateHash(a.Active)) throw new InvalidOperationException("the replay hash differs from the saved one");
                }
                catch (Exception e)
                {
                    // Any failure here means the recipe no longer describes a run under today's content and rules (an id removed,
                    // a rule changed). That must not cost the player the account.
                    a.Active = null;
                    if (r?.Books != null) foreach (var b in r.Books) a.Books[b] = a.BookCount(b) + 1;
                    notice = "Your run in progress was saved under older rules and could not continue. It was dropped and its books returned. (" + e.Message + ")";
                }
            }
            // An achievement added to content since the save is granted when the records already meet it (specs/18 section 8).
            AccountOps.CheckAchievements(c, a);
            if (notice == null && !string.IsNullOrEmpty(a.ContentVersion) && !c.IsCompatibleWith(a.ContentVersion))
                notice = $"This account was saved with content {a.ContentVersion}; the game is now on {c.Version}.";
            a.ContentVersion = c.Version;
            return a;
        }

        public static AccountState Decode(ContentDb c, byte[] file) => Decode(c, file, out _);

        /// <summary>
        /// Forward-only schema migrations (specs/06 section 5) on the decoded body, one step per version, frozen literals only
        /// (never ContentDb). Returns the version reached.
        /// </summary>
        static int Migrate(JObject body, int version)
        {
            if (!(body["Account"] is JObject acct)) throw new SaveInvalidException("Account section missing");
            if (version == 1)
            {
                // v1 -> v2 (issue #82): Vault entries gain Locked, false for every entry sealed before the lock existed.
                if (acct["Vault"] is JArray vault)
                    foreach (var e in vault)
                        if (e is JObject o && o["Locked"] == null) o["Locked"] = false;
                version = 2;
            }
            if (version == 2)
            {
                // v2 -> v3 (issue #73): achievements, records and the post-run vendor. Species already owned stay owned (accounts
                // from before T9 owned every species); Decode rebuilds the records from the Vault after deserialising.
                acct["Achievements"] = new JArray();
                acct.Remove("Records");
                acct["Vendor"] = JValue.CreateNull();
                version = 3;
            }
            body["SchemaVersion"] = version;
            acct["SchemaVersion"] = version;
            return version;
        }

        public static void Write(ISaveStore store, AccountState a) => store.Write(Encode(a));

        /// <summary>
        /// Load from the primary slot, falling back to the backup when the primary is corrupt or invalid (the bad slot is
        /// quarantined). Null when there is no account yet. Throws SaveInvalidException when neither slot loads.
        /// </summary>
        public static AccountState Load(ContentDb c, ISaveStore store, long nowServerSec) => Load(c, store, nowServerSec, out _);

        /// <summary>Load with the player-facing notice of <see cref="Decode(ContentDb, byte[], out string)"/>.</summary>
        public static AccountState Load(ContentDb c, ISaveStore store, long nowServerSec, out string notice)
        {
            notice = null;
            Exception first = null;
            foreach (var slot in new[] { SaveSlot.Primary, SaveSlot.Backup })
            {
                var bytes = store.Read(slot);
                if (bytes == null) continue;
                try { return Decode(c, bytes, out notice); }
                catch (Exception e) when (e is SaveCorruptException || e is SaveInvalidException)
                {
                    first = first ?? e;
                    store.Quarantine(slot, nowServerSec);
                }
            }
            if (first != null) throw new SaveInvalidException("no account slot loads: " + first.Message, first);
            return null;
        }

        static JObject Recipe(RunState s)
        {
            // Books brought into the run = the unused ones plus the ones the log used (UseBook removes by id, so the order of
            // the replayed list leaves the unused books in the same order).
            var books = new List<string>(s.Books);
            foreach (var l in s.Log) if (l.Kind == RunCommandKind.UseBook) books.Add(l.Book);
            var r = new RunRecipe { SpeciesId = s.SpeciesId, Seed = s.Seed, Books = books, Mods = s.Mods, Log = s.Log, Hash = TrainingRun.StateHash(s) };
            return JObject.FromObject(r, SaveSerializer.Serializer);
        }

        /// <summary>What it takes to rebuild a run: TrainingRun.Replay's arguments plus the expected hash.</summary>
        public sealed class RunRecipe
        {
            public string SpeciesId { get; set; }
            public ulong Seed { get; set; }
            public List<string> Books { get; set; } = new List<string>();
            public RunModifiers Mods { get; set; } = new RunModifiers();
            public List<RunCommand> Log { get; set; } = new List<RunCommand>();
            public string Hash { get; set; }
        }
    }
}
