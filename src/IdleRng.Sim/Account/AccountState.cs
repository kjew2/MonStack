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
        /// <summary>
        /// The account save lineage (specs/18 section 6). v2: Vault entries carry Locked. v3: achievements, records and the post-run
        /// vendor. v4: Dungeon progress. v5: Dungeon tickets. v6: the vendor's reroll count (AccountSave migrates v1 to v6).
        /// </summary>
        public const int CurrentSchemaVersion = 6;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string ContentVersion { get; set; } = "";

        /// <summary>Account seed: every run seed derives from it and RunsStarted.</summary>
        public ulong Seed { get; set; }

        public int RunsStarted { get; set; }
        public long Shards { get; set; }

        /// <summary>Species the player can train, in content order: the starter plus those achievements unlocked (specs/18 section 8).</summary>
        public List<string> OwnedSpecies { get; set; } = new List<string>();

        /// <summary>Skill books by id and count.</summary>
        public SortedDictionary<string, int> Books { get; set; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Talent ranks by id; a missing id is rank 0.</summary>
        public SortedDictionary<string, int> Talents { get; set; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Finished monsters, oldest first.</summary>
        public List<VaultEntry> Vault { get; set; } = new List<VaultEntry>();

        /// <summary>Achievement ids earned, in the order they were earned (specs/18 section 8).</summary>
        public List<string> Achievements { get; set; } = new List<string>();

        /// <summary>Lifetime bests the achievements are checked against.</summary>
        public AccountRecords Records { get; set; } = new AccountRecords();

        /// <summary>The post-run vendor's offers from the last sealed run, or null (specs/18 section 4). Gone when the next run starts.</summary>
        public VendorState Vendor { get; set; }

        /// <summary>Dungeon tickets, attempts and bests (specs/21 section 7).</summary>
        public TowerProgress Tower { get; set; } = new TowerProgress();

        /// <summary>The run in progress, or null.</summary>
        public RunState Active { get; set; }

        public int TalentRank(string id) => Talents.TryGetValue(id, out var r) ? r : 0;
        public int BookCount(string id) => Books.TryGetValue(id, out var n) ? n : 0;
    }

    /// <summary>Lifetime records of sealed runs (abandoned runs do not count): what achievements are checked against.</summary>
    public sealed class AccountRecords
    {
        public int RunsFinished { get; set; }

        /// <summary>Highest grade index (rank) a sealed run reached; -1 before the first.</summary>
        public int BestRank { get; set; } = -1;

        /// <summary>Most gauntlet floors a sealed monster cleared.</summary>
        public int BestFloorsCleared { get; set; }

        /// <summary>Highest final value of each stat over sealed monsters, by stat id.</summary>
        public SortedDictionary<string, double> BestStats { get; set; } = new SortedDictionary<string, double>(StringComparer.Ordinal);

        public double BestStat(string id) => BestStats.TryGetValue(id, out var v) ? v : 0;
    }

    /// <summary>
    /// Dungeon progress (specs/21 section 7; "tower" in code). Each attempt spends one ticket and pays every floor it clears.
    /// Floors are counted as floors cleared.
    /// </summary>
    public sealed class TowerProgress
    {
        /// <summary>UTC day number of the last attempt; -1 before the first. A later day resets AttemptsUsed and ClearedToday.</summary>
        public int Day { get; set; } = -1;

        /// <summary>Attempts made on Day. The next attempt's Skill Book rolls use it (TowerSim.BookDrop), so each ticket rolls afresh.</summary>
        public int AttemptsUsed { get; set; }

        /// <summary>Most floors one attempt cleared on Day.</summary>
        public int ClearedToday { get; set; }

        /// <summary>Most floors one attempt ever cleared (a floor above it is a first clear).</summary>
        public int ClearedEver { get; set; }

        /// <summary>Tickets held, not counting free daily tickets not yet granted (AccountOps.TowerTickets counts those too).</summary>
        public int Tickets { get; set; }

        /// <summary>Ticket pieces from sealed runs toward the next ticket, 0 to TOWER_TICKET_PIECES - 1.</summary>
        public int TicketPieces { get; set; }

        /// <summary>UTC day the free daily tickets were last granted up to; -1 before the first.</summary>
        public int GrantDay { get; set; } = -1;
    }

    /// <summary>What one Dungeon attempt did and paid (AccountOps.RunTower).</summary>
    public sealed class TowerRunResult
    {
        public IdleRng.Sim.Tower.TowerResult Result { get; set; }
        public long Shards { get; set; }

        /// <summary>Skill Books dropped, by the floor that dropped each (floor order).</summary>
        public List<(int Floor, string Book)> Books { get; set; } = new List<(int, string)>();

        /// <summary>Floors cleared for the first time ever (they will pay Star-Gems once gems exist, specs/21 section 7).</summary>
        public List<int> FirstClears { get; set; } = new List<int>();

        /// <summary>Tickets left after this attempt.</summary>
        public int TicketsLeft { get; set; }
    }

    /// <summary>The post-run vendor (specs/18 section 4): offers rolled when a run is sealed, rarity odds by its grade.</summary>
    public sealed class VendorState
    {
        /// <summary>Grade id of the run that rolled these offers.</summary>
        public string Grade { get; set; }

        /// <summary>Rerolls bought this visit (issue #104): the next one's price and the key of its roll. 0 when rolled at seal time.</summary>
        public int Rerolls { get; set; }

        public List<VendorOffer> Offers { get; set; } = new List<VendorOffer>();
    }

    /// <summary>
    /// One vendor offer: one rank of one talent, at its normal cost worked out when bought. Rarity is the tier it was drawn from
    /// (the talent's own tier; an offer rolled before issue #102 may carry an older rarity).
    /// </summary>
    public sealed class VendorOffer
    {
        public string Talent { get; set; }
        public string Rarity { get; set; }
        public bool Bought { get; set; }
    }

    /// <summary>What finishing a run paid out.</summary>
    public sealed class FinishResult
    {
        public long Shards { get; set; }
        public VaultEntry Entry { get; set; }

        /// <summary>Books brought into the run but not used, now back in the inventory.</summary>
        public List<string> BooksReturned { get; set; } = new List<string>();

        /// <summary>Achievements this run earned, in content order.</summary>
        public List<string> Achievements { get; set; } = new List<string>();

        /// <summary>Species those achievements unlocked.</summary>
        public List<string> SpeciesUnlocked { get; set; } = new List<string>();

        /// <summary>Dungeon ticket pieces the run's grade earned (fight_ranks.ticket_pieces).</summary>
        public int TicketPieces { get; set; }

        /// <summary>Whole Dungeon tickets those pieces completed.</summary>
        public int TicketsEarned { get; set; }
    }
}
