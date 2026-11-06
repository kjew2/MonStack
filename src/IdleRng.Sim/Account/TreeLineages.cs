using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using IdleRng.Sim.Training;

namespace IdleRng.Sim.Account
{
    /// <summary>Two inherited trees per synthesis, with one growing focus and monster-held seeded progress.</summary>
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
        public static SortedDictionary<string, FortunaLineage> CloneProgress(SortedDictionary<string, FortunaLineage> progress)
        {
            if (progress == null) return null;
            var copy = new SortedDictionary<string, FortunaLineage>(StringComparer.Ordinal);
            foreach (var kv in progress) if (kv.Value != null) copy[kv.Key] = kv.Value.Clone();
            return copy;
        }
        public static bool HasProgress(FortunaLineage p) => p != null && (p.Rank > 1 || p.Points > 0 || p.Failures > 0 || p.Attempts > 0);
        public static int CompareProgress(FortunaLineage x, FortunaLineage y)
        {
            int rank = x.Rank.CompareTo(y.Rank);
            if (rank != 0) return rank;
            int points = x.Points.CompareTo(y.Points);
            return points != 0 ? points : x.Failures.CompareTo(y.Failures);
        }
        /// <summary>The monster's own inheritance; no account-wide ledger is consulted or mutated.</summary>
        public static FortunaLineage Carried(ContentDb c, VaultEntry e, string tree)
        {
            if (e.Lineages != null && e.Lineages.TryGetValue(tree, out var progress) && progress != null) return progress.Clone();
            int rank = e.Lineage?.TreeId == tree ? Math.Max(1, e.Lineage.Rank) : 1;
            foreach (var t in e.Trees) if (t.TreeId == tree && t.Roll != null) rank = Math.Max(rank, t.Roll.LineageRank);
            return new FortunaLineage { Rank = rank };
        }
        /// <summary>Preview a tree's inheritance, including a stronger unassigned legacy reserve if one remains.</summary>
        public static FortunaLineage Inheritance(ContentDb c, AccountState a, VaultEntry e, string tree)
        {
            var own = Carried(c, e, tree);
            if (a.Lineages != null && a.Lineages.TryGetValue(tree, out var reserve) && reserve != null && CompareProgress(reserve, own) > 0)
            {
                own = reserve.Clone();
                if (own.Seed == 0) own.Seed = StreamNames.StreamSeed(a.Seed, "lineage." + tree);
            }
            if (own.Seed == 0) own.Seed = StreamNames.StreamSeed(a.Seed, "lineage." + tree + "." + e.Id);
            return own;
        }
        public static bool CanCarry(ContentDb c, VaultEntry e, string tree)
        {
            if (e == null || tree == null || !c.Trees.TryGetValue(tree, out var t) || t.Kind != TreeKind.Generic) return false;
            if (e.Lineages != null && e.Lineages.TryGetValue(tree, out var p) && p != null) return true;
            int earliest = -1;
            for (int i = 0; i < t.Nodes.Count; i++) if (t.Nodes[i].Kind == NodeKind.Skill) { earliest = i + 1; break; }
            return earliest > 0 && e.Trees.Any(n => n.TreeId == tree && n.Bought >= earliest);
        }
        public static bool EligibleTree(ContentDb c, VaultEntry e, string tree) => CanCarry(c, e, tree);
        public static bool EligiblePrimary(ContentDb c, VaultEntry e, string tree) => e != null && e.Id >= 0 && !e.Locked && CanCarry(c, e, tree);
        public static IEnumerable<string> SupportingTrees(ContentDb c, VaultEntry e, string focus) =>
            Trees(c).Where(t => t.Id != focus && EligiblePrimary(c, e, t.Id)).Select(t => t.Id).OrderBy(id => id, StringComparer.Ordinal);
        public static RunState Breed(ContentDb c, AccountState a, int primary, int supporting, string tree, IEnumerable<string> off = null, string supportingTree = null)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (tree == null || !c.Trees.TryGetValue(tree, out var def) || def.Kind != TreeKind.Generic)
                throw new ArgumentException("Choose a generic tree lineage.");
            if (primary < 0 || primary >= a.Vault.Count || supporting < 0 || supporting >= a.Vault.Count || primary == supporting)
                throw new ArgumentException("Choose two different Vault parents.");
            var parent = a.Vault[primary];
            var partner = a.Vault[supporting];
            if (parent.Locked || partner.Locked) throw new InvalidOperationException("Unlock both parents before synthesis.");
            if (parent.Id < 0 || partner.Id < 0 || parent.Id == partner.Id)
                throw new InvalidOperationException("Parents need distinct certified run IDs.");
            if (!EligiblePrimary(c, parent, tree)) throw new InvalidOperationException("The primary parent must carry or have learned " + c.Text(def.NameKey) + "'s first skill node.");
            supportingTree = supportingTree ?? SupportingTrees(c, partner, tree).FirstOrDefault();
            if (supportingTree == null || supportingTree == tree || !EligiblePrimary(c, partner, supportingTree))
                throw new InvalidOperationException("The supporting parent must contribute a different eligible generic tree.");
            if (a.Shards < Cost(c)) throw new InvalidOperationException("Not enough Training Shards to synthesize.");
            if (c.Constants.TreeSlots < 4) throw new InvalidOperationException("Synthesis needs two inherited tree slots.");

            var focus = Inheritance(c, a, parent, tree);
            var secondary = Inheritance(c, a, partner, supportingTree);
            var focusRoll = InheritedRoll(c, tree, focus.Rank);
            var secondaryRoll = InheritedRoll(c, supportingTree, secondary.Rank);
            bool claimFocus = a.Lineages != null && a.Lineages.TryGetValue(tree, out var focusReserve) && focusReserve != null && CompareProgress(focusReserve, Carried(c, parent, tree)) > 0;
            bool claimSecondary = a.Lineages != null && a.Lineages.TryGetValue(supportingTree, out var secondaryReserve) && secondaryReserve != null && CompareProgress(secondaryReserve, Carried(c, partner, supportingTree)) > 0;
            int high = Math.Max(primary, supporting), low = Math.Min(primary, supporting);
            var higher = a.Vault[high]; var lower = a.Vault[low];
            a.Vault.RemoveAt(high); a.Vault.RemoveAt(low);
            RunState s;
            try
            {
                // StartRun performs all of its validations before changing the account. Only the post-sacrifice Vault counts.
                s = AccountOps.StartRun(c, a, parent.SpeciesId, talentsOff: off);
            }
            catch
            {
                a.Vault.Insert(low, lower); a.Vault.Insert(high, higher);
                throw;
            }
            a.Shards -= Cost(c);
            a.Rulebooks?.Remove(parent.Id); a.Rulebooks?.Remove(partner.Id);
            if (claimFocus) a.Lineages.Remove(tree);
            if (claimSecondary) a.Lineages.Remove(supportingTree);
            s.Lineage = new LineageRun { TreeId = tree, Rank = focus.Rank, Primary = parent.Id, Supporting = partner.Id,
                SupportingTreeId = supportingTree, Progress = new SortedDictionary<string, FortunaLineage>(StringComparer.Ordinal)
                { [tree] = focus, [supportingTree] = secondary } };
            s.Slots[2] = new TreeSlot { TreeId = tree, Roll = focusRoll };
            s.Slots[3] = new TreeSlot { TreeId = supportingTree, Roll = secondaryRoll };
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
            if (s.Lineage.Progress == null) s.Lineage.Progress = new SortedDictionary<string, FortunaLineage>(StringComparer.Ordinal);
            if (!s.Lineage.Progress.TryGetValue(s.Lineage.TreeId, out var p) || p == null)
                s.Lineage.Progress[s.Lineage.TreeId] = p = new FortunaLineage { Rank = Math.Max(1, s.Lineage.Rank) };
            if (p.Seed == 0) p.Seed = StreamNames.StreamSeed(a.Seed, "lineage." + s.Lineage.TreeId + "." + s.Lineage.Primary);
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
                    var rng = new RngStream(stream, p.Seed, (ulong)p.Attempts);
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
