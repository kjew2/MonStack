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

    public static class FortunaBreeding
    {
        public const string Tree = "fortune";
        public static int Rank(AccountState a) => a.Fortuna.Rank;
        public static int Cost(ContentDb c) => (int)c.ConstantValues["LINEAGE_BREED_COST"];
        public static int Chance(ContentDb c, FortunaLineage p) => Math.Min(100,
            (int)c.ConstantValues["LINEAGE_V_CHANCE"] + p.Failures * (int)c.ConstantValues["LINEAGE_V_PITY"]);
        public static int Required(ContentDb c, FortunaLineage p)
        {
            if (p.Rank >= 5) return 0;
            int full = (int)c.ConstantValues["LINEAGE_TO_" + (p.Rank + 1)];
            return p.Failures == 0 ? full : (int)Math.Ceiling(full * c.ConstantValues["LINEAGE_RETRY_FRAC"]);
        }
        public static bool EligiblePrimary(VaultEntry e) => e.Trees.Any(t => t.TreeId == Tree && t.Bought >= 2);

        /// <summary>All checks precede StartRun; once it succeeds the remaining work cannot reject.</summary>
        public static RunState Breed(ContentDb c, AccountState a, int primary, int supporting, System.Collections.Generic.IEnumerable<string> off = null)
        {
            if (primary < 0 || primary >= a.Vault.Count || supporting < 0 || supporting >= a.Vault.Count || primary == supporting)
                throw new ArgumentException("Choose two different Vault parents.");
            var parent = a.Vault[primary];
            if (!EligiblePrimary(parent)) throw new InvalidOperationException("The primary parent must have learned Fortune's skill node.");
            if (parent.Id < 0 || a.Vault[supporting].Id < 0 || parent.Id == a.Vault[supporting].Id)
                throw new InvalidOperationException("Parents need distinct certified run IDs.");
            if (a.Shards < Cost(c)) throw new InvalidOperationException("Not enough Training Shards to breed.");
            if (c.Constants.TreeSlots < 3) throw new InvalidOperationException("Breeding needs an empty inherited tree slot.");
            var rank = Rank(a);
            var roll = InheritedRoll(c, rank);
            var s = AccountOps.StartRun(c, a, parent.SpeciesId, talentsOff: off);
            a.Shards -= Cost(c);
            s.Lineage = new LineageRun { Rank = rank, Primary = parent.Id, Supporting = a.Vault[supporting].Id };
            s.Slots[2] = new TreeSlot { TreeId = Tree, Roll = roll };
            return s;
        }

        static TreeRoll InheritedRoll(ContentDb c, int rank)
        {
            var ns = c.Trees[Tree].Nodes;
            var roll = new TreeRoll { Stat = "luk", LineageRank = rank, NodeStats = new string[ns.Count], Amounts = new double[ns.Count] };
            for (int i = 0; i < ns.Count; i++)
                if (TrainingRun.IsFiller(ns[i].Kind))
                {
                    roll.NodeStats[i] = ns[i].Stat;
                    roll.Amounts[i] = ns[i].Amount * (1 + (rank - 1) * c.ConstantValues["LINEAGE_FILLER_PER_RANK"]);
                }
            return roll;
        }

        public static int PointsFor(ContentDb c, int deepest)
        {
            var ns = c.Trees[Tree].Nodes;
            int points = 0;
            for (int i = 0; i < Math.Min(deepest, ns.Count); i++)
                points += (int)c.ConstantValues[i == ns.Count - 1 ? "LINEAGE_CAPSTONE_POINTS" : ns[i].Kind == NodeKind.Skill ? "LINEAGE_SKILL_POINTS" : "LINEAGE_NODE_POINTS"];
            return points;
        }

        /// <summary>Called only while sealing an active bred run, before that run is cleared.</summary>
        public static void Award(ContentDb c, AccountState a, RunState s)
        {
            if (s.Lineage == null) return;
            var p = a.Fortuna;
            p.LastAward = PointsFor(c, s.Lineage.Deepest);
            p.LastResult = null;
            if (p.Rank >= 5) return;
            p.Points += p.LastAward;
            while (p.Rank < 5 && p.Points >= Required(c, p))
            {
                p.Points -= Required(c, p);
                bool success = true;
                if (p.Rank == 4)
                {
                    var rng = new RngStream("lineage.fortune", StreamNames.StreamSeed(a.Seed, "lineage.fortune"), (ulong)p.Attempts);
                    success = rng.NextDouble() * 100 < Chance(c, p);
                    p.Attempts++;
                }
                if (success) { p.Rank++; p.Failures = 0; p.LastResult = "Fortune " + Roman(p.Rank) + " unlocked"; }
                else { p.Failures++; p.LastResult = "Breakthrough missed. Next chance: " + Chance(c, p) + "%."; }
            }
        }
        public static string Roman(int rank) => new[] { "", "I", "II", "III", "IV", "V" }[rank];
    }
}
