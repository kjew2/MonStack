using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using IdleRng.Sim.Training;

namespace IdleRng.Sim.Account
{
    /// <summary>One inherited focus per offspring, with independent account ledgers and seeded breakthrough streams.</summary>
    public static class TreeLineages
    {
        public const int MaxRank = 10;
        public static FortunaLineage GetLedger(AccountState a, string tree) => tree == "fortune" ? a.Fortuna :
            a.Lineages != null && a.Lineages.TryGetValue(tree, out var p) && p != null ? p : new FortunaLineage();
        public static FortunaLineage Ledger(AccountState a, string tree)
        {
            if (a.Lineages == null) a.Lineages = new SortedDictionary<string, FortunaLineage>(StringComparer.Ordinal);
            if (!a.Lineages.TryGetValue(tree, out var p) || p == null)
                a.Lineages[tree] = p = new FortunaLineage();
            return p;
        }
        public static IEnumerable<TreeDef> Trees(ContentDb c) => c.Trees.Values.Where(t => t.Kind == TreeKind.Generic);
        public static int Cost(ContentDb c) => (int)c.ConstantValues["LINEAGE_BREED_COST"];
        public static bool Milestone(FortunaLineage p) => p.Rank == 4 || p.Rank == 9;
        public static int BaseChance(ContentDb c, FortunaLineage p) => p.Rank == 9 ? (int)c.ConstantValues["LINEAGE_X_CHANCE"] : (int)c.ConstantValues["LINEAGE_V_CHANCE"];
        public static int Pity(ContentDb c, FortunaLineage p) => (int)c.ConstantValues[p.Rank == 9 ? "LINEAGE_X_PITY" : "LINEAGE_V_PITY"];
        public static int Chance(ContentDb c, FortunaLineage p)
        {
            if (!Milestone(p)) return 100;
            if (p.Failures >= (p.Rank == 9 ? 5 : 4)) return 100;
            return Math.Min(100, BaseChance(c, p) + p.Failures * Pity(c, p));
        }
        public static int AttemptsLeft(ContentDb c, FortunaLineage p) => !Milestone(p) ? 0 : Math.Max(1, (p.Rank == 9 ? 6 : 5) - p.Failures);
        public static int Required(ContentDb c, FortunaLineage p)
        {
            if (p.Rank >= MaxRank) return 0;
            int full = (int)c.ConstantValues["LINEAGE_TO_" + (p.Rank + 1)];
            return p.Failures == 0 ? full : (int)Math.Ceiling(full * c.ConstantValues["LINEAGE_RETRY_FRAC"]);
        }
        public static double FillerBonus(ContentDb c, int rank) => Math.Min(4, Math.Max(0, rank - 1)) * c.ConstantValues["LINEAGE_FILLER_PER_RANK"]
            + Math.Max(0, Math.Min(MaxRank, rank) - 5) * c.ConstantValues["LINEAGE_FILLER_LATE_PER_RANK"];
        public static bool EligiblePrimary(ContentDb c, VaultEntry e, string tree)
        {
            if (!c.Trees.TryGetValue(tree, out var t) || t.Kind != TreeKind.Generic) return false;
            int earliest = -1;
            for (int i = 0; i < t.Nodes.Count; i++) if (t.Nodes[i].Kind == NodeKind.Skill) { earliest = i + 1; break; }
            return earliest > 0 && e.Id >= 0 && e.Trees.Any(n => n.TreeId == tree && n.Bought >= earliest);
        }
        public static RunState Breed(ContentDb c, AccountState a, int primary, int supporting, string tree, IEnumerable<string> off = null)
        {
            if (tree == null || !c.Trees.TryGetValue(tree, out var def) || def.Kind != TreeKind.Generic)
                throw new ArgumentException("Choose a generic tree lineage.");
            if (primary < 0 || primary >= a.Vault.Count || supporting < 0 || supporting >= a.Vault.Count || primary == supporting)
                throw new ArgumentException("Choose two different Vault parents.");
            var parent = a.Vault[primary];
            if (!EligiblePrimary(c, parent, tree)) throw new InvalidOperationException("The primary parent must have learned " + c.Text(def.NameKey) + "'s first skill node.");
            if (parent.Id < 0 || a.Vault[supporting].Id < 0 || parent.Id == a.Vault[supporting].Id)
                throw new InvalidOperationException("Parents need distinct certified run IDs.");
            if (a.Shards < Cost(c)) throw new InvalidOperationException("Not enough Training Shards to breed.");
            if (c.Constants.TreeSlots < 3) throw new InvalidOperationException("Breeding needs an empty inherited tree slot.");
            // Do not create the account ledger until StartRun has passed its own validation.
            int rank = GetLedger(a, tree).Rank;
            var roll = InheritedRoll(c, tree, rank);
            var s = AccountOps.StartRun(c, a, parent.SpeciesId, talentsOff: off);
            a.Shards -= Cost(c);
            s.Lineage = new LineageRun { TreeId = tree, Rank = rank, Primary = parent.Id, Supporting = a.Vault[supporting].Id };
            s.Slots[2] = new TreeSlot { TreeId = tree, Roll = roll };
            return s;
        }
        static TreeRoll InheritedRoll(ContentDb c, string tree, int rank)
        {
            var t = c.Trees[tree]; var ns = t.Nodes;
            var roll = new TreeRoll { Stat = t.OfferStat ?? ns.FirstOrDefault(n => TrainingRun.IsFiller(n.Kind))?.Stat ?? "luk", LineageRank = rank,
                NodeStats = new string[ns.Count], Amounts = new double[ns.Count] };
            for (int i = 0; i < ns.Count; i++) if (TrainingRun.IsFiller(ns[i].Kind))
            {
                roll.NodeStats[i] = ns[i].Stat;
                roll.Amounts[i] = ns[i].Amount * (1 + FillerBonus(c, rank));
            }
            return roll;
        }
        /// <summary>Weighted largest remainders share the pre-capstone budget, with earlier nodes breaking rounding ties.</summary>
        public static int PointsFor(ContentDb c, string tree, int deepest)
        {
            var ns = c.Trees[tree].Nodes;
            int budget = (int)c.ConstantValues["LINEAGE_PRE_CAPSTONE_POINTS"];
            if (deepest >= ns.Count) return (int)c.ConstantValues["LINEAGE_CAPSTONE_POINTS"] + budget;
            int[] weights = ns.Take(ns.Count - 1).Select(n => (int)c.ConstantValues[n.Kind == NodeKind.Skill ? "LINEAGE_SKILL_POINTS" : "LINEAGE_NODE_POINTS"]).ToArray();
            int total = weights.Sum();
            if (total == 0) return 0;
            int[] points = weights.Select(weight => budget * weight / total).ToArray();
            foreach (int i in Enumerable.Range(0, weights.Length).OrderByDescending(i => budget * weights[i] % total).ThenBy(i => i).Take(budget - points.Sum()))
                points[i]++;
            return points.Take(Math.Max(0, deepest)).Sum();
        }
        public static void Award(ContentDb c, AccountState a, RunState s)
        {
            if (s.Lineage == null) return;
            var p = Ledger(a, s.Lineage.TreeId);
            p.LastAward = 0; p.LastResult = null;
            if (p.Rank >= MaxRank) return;
            p.LastAward = PointsFor(c, s.Lineage.TreeId, s.Lineage.Deepest);
            p.Points += p.LastAward;
            while (p.Rank < MaxRank && p.Points >= Required(c, p))
            {
                p.Points -= Required(c, p);
                bool success = true;
                if (Milestone(p))
                {
                    string stream = "lineage." + s.Lineage.TreeId;
                    var rng = new RngStream(stream, StreamNames.StreamSeed(a.Seed, stream), (ulong)p.Attempts);
                    success = rng.NextDouble() * 100 < Chance(c, p);
                    p.Attempts++;
                }
                if (success)
                {
                    p.Rank++; p.Failures = 0;
                    p.LastResult = c.Text(c.Trees[s.Lineage.TreeId].NameKey) + " " + Roman(p.Rank) + " unlocked";
                }
                else { p.Failures++; p.LastResult = "Breakthrough missed. Next chance: " + Chance(c, p) + "%."; }
            }
        }
        public static string Roman(int rank) => new[] { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" }[rank];
    }
}
