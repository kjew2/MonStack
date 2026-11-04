using System;
using System.Linq;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using IdleRng.Sim.Training;

namespace IdleRng.Sim.Account
{
    /// <summary>Account-wide unlocks; an offspring captures its rank at birth. Specs/29.</summary>
    public sealed class FortunaLineage
    {
        public int Rank { get; set; } = 1;
        public int Points { get; set; }
        public int Failures { get; set; }
        public int Attempts { get; set; }
        public int LastAward { get; set; }
        public string LastResult { get; set; }
    }

    /// <summary>Compatibility surface for pre-I–X Fortune callers.</summary>
    public static class FortunaBreeding
    {
        public const string Tree = "fortune";
        public static int Rank(AccountState a) => TreeLineages.GetLedger(a, Tree).Rank;
        public static int Cost(ContentDb c) => TreeLineages.Cost(c);
        public static int Chance(ContentDb c, FortunaLineage p) => TreeLineages.Chance(c, p);
        public static int Required(ContentDb c, FortunaLineage p) => TreeLineages.Required(c, p);
        public static bool EligiblePrimary(VaultEntry e) => e.Trees.Any(t => t.TreeId == Tree && t.Bought >= 2);
        public static RunState Breed(ContentDb c, AccountState a, int primary, int supporting, System.Collections.Generic.IEnumerable<string> off = null)
            => TreeLineages.Breed(c, a, primary, supporting, Tree, off);
        public static int PointsFor(ContentDb c, int deepest) => TreeLineages.PointsFor(c, Tree, deepest);
        public static void Award(ContentDb c, AccountState a, RunState s) => TreeLineages.Award(c, a, s);
        public static string Roman(int rank) => TreeLineages.Roman(rank);
    }
}
