using System;
using System.Collections.Generic;
using IdleRng.Sim.Training;

namespace IdleRng.Sim.Account
{
    /// <summary>
    /// The player's account between runs (specs/18): owned species, the Vault of finished monsters, Training Shards, skill
    /// books, talent ranks and the run in progress. Plain mutable graph; IdleRng.Runtime saves it (the active
    /// run as its replay recipe, so a load rebuilds it exactly).
    /// </summary>
    public sealed class AccountState
    {
        /// <summary>The account save lineage (specs/18 section 6). v2: Vault entries carry Locked (AccountSave migrates v1).</summary>
        public const int CurrentSchemaVersion = 2;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string ContentVersion { get; set; } = "";

        /// <summary>Account seed: every run seed derives from it and RunsStarted.</summary>
        public ulong Seed { get; set; }

        public int RunsStarted { get; set; }
        public long Shards { get; set; }

        /// <summary>Species the player can train, in content order. All MVP species until the gacha (specs/13 T9).</summary>
        public List<string> OwnedSpecies { get; set; } = new List<string>();

        /// <summary>Skill books by id and count.</summary>
        public SortedDictionary<string, int> Books { get; set; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Talent ranks by id; a missing id is rank 0.</summary>
        public SortedDictionary<string, int> Talents { get; set; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Finished monsters, oldest first.</summary>
        public List<VaultEntry> Vault { get; set; } = new List<VaultEntry>();

        /// <summary>The run in progress, or null.</summary>
        public RunState Active { get; set; }

        public int TalentRank(string id) => Talents.TryGetValue(id, out var r) ? r : 0;
        public int BookCount(string id) => Books.TryGetValue(id, out var n) ? n : 0;
    }

    /// <summary>What finishing a run paid out.</summary>
    public sealed class FinishResult
    {
        public long Shards { get; set; }
        public VaultEntry Entry { get; set; }

        /// <summary>Books brought into the run but not used, now back in the inventory.</summary>
        public List<string> BooksReturned { get; set; } = new List<string>();
    }
}
