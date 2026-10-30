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
    /// { "Kind": "monster_account", "SchemaVersion": 1, "Account": { ...AccountState without Active... },
    ///   "ActiveRun": { "SpeciesId", "Seed", "Books", "Mods", "Log", "Hash" } | null }
    /// </code>
    /// The run in progress is stored as its replay recipe and rebuilt by TrainingRun.Replay on load; the stored hash catches
    /// a recipe that no longer replays to the same run (for example after a rules change).
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
        /// is intact but not a loadable account at this schema (wrong kind, newer schema, active run that no longer replays).
        /// </summary>
        public static AccountState Decode(ContentDb c, byte[] file)
        {
            var (version, body) = SaveSerializer.Decode(file);
            if ((string)body["Kind"] != Kind) throw new SaveInvalidException($"not an account save (Kind '{body["Kind"]}')");
            if (version != AccountState.CurrentSchemaVersion)
                throw new SaveInvalidException($"account schema v{version} is not supported (current v{AccountState.CurrentSchemaVersion})");
            if (!(body["Account"] is JObject acct)) throw new SaveInvalidException("Account section missing");
            AccountState a;
            try { a = acct.ToObject<AccountState>(SaveSerializer.Serializer); }
            catch (JsonException e) { throw new SaveInvalidException("Account does not deserialise: " + e.Message, e); }
            if (a == null) throw new SaveInvalidException("Account is null");

            if (body["ActiveRun"] is JObject run)
            {
                try
                {
                    var r = run.ToObject<RunRecipe>(SaveSerializer.Serializer);
                    a.Active = TrainingRun.Replay(c, r.SpeciesId, r.Seed, r.Log, r.Books, r.Mods);
                    if (r.Hash != TrainingRun.StateHash(a.Active)) throw new SaveInvalidException("the active run no longer replays to its saved hash");
                }
                catch (SaveInvalidException) { throw; }
                catch (Exception e) when (e is JsonException || e is ArgumentException || e is InvalidOperationException || e is KeyNotFoundException)
                {
                    throw new SaveInvalidException("the active run does not replay: " + e.Message, e);
                }
            }
            return a;
        }

        public static void Write(ISaveStore store, AccountState a) => store.Write(Encode(a));

        /// <summary>
        /// Load from the primary slot, falling back to the backup when the primary is corrupt or invalid (the bad slot is
        /// quarantined). Null when there is no account yet. Throws SaveInvalidException when neither slot loads.
        /// </summary>
        public static AccountState Load(ContentDb c, ISaveStore store, long nowServerSec)
        {
            Exception first = null;
            foreach (var slot in new[] { SaveSlot.Primary, SaveSlot.Backup })
            {
                var bytes = store.Read(slot);
                if (bytes == null) continue;
                try { return Decode(c, bytes); }
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
