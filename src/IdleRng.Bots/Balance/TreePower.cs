using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Content;
using IdleRng.Sim.Gauntlet;
using IdleRng.Sim.Training;

namespace IdleRng.Bots.Balance
{
    /// <summary>One generic tree's capstone over the focus runs of one species, or of every species ("all").</summary>
    public sealed class TreePowerSummary
    {
        public string TreeId { get; set; }

        /// <summary>The tree's offer stat, or "any".</summary>
        public string Stat { get; set; }

        /// <summary>A species id, or <see cref="TreePower.AllSpecies"/> for the pooled row.</summary>
        public string Species { get; set; }
        public int N { get; set; }

        /// <summary>Gauntlet floor with every node of the tree but the capstone bought.</summary>
        public Distribution BaseFloor { get; set; }

        /// <summary>Floors the capstone adds on top of that (same run, same seed).</summary>
        public Distribution Gain { get; set; }

        /// <summary>"above" when the mean gain beats the guideline's top (TreePower.GuideMax), else empty.</summary>
        public string Flag => Gain.N > 0 && Gain.Mean > TreePower.GuideMax ? "above" : "";
    }

    /// <summary>
    /// Tree power (specs/09 section 7): for every generic tree whose capstone is a rule node, the gauntlet floors buying that
    /// capstone adds to a run's monster that holds the rest of the tree (chase trees taken out, as chase power does), over each
    /// species' <c>focus</c> runs. A report only: the guideline is the in-run chase target (specs/15 section 5.5), and a tree
    /// above it is flagged, never checked.
    /// </summary>
    public static class TreePower
    {
        public const string Policy = "focus";
        public const string AllSpecies = "all";

        /// <summary>The guideline: an in-run chase tree adds 5-8 floors, and a generic capstone should add fewer (specs/15 section 5.5).</summary>
        public const int GuideMin = 5, GuideMax = 8;

        /// <summary>Generic trees whose last node is a rule, in content order.</summary>
        public static List<TreeDef> Trees(ContentDb c) =>
            c.TreeList.Where(t => t.Kind == TreeKind.Generic && t.Nodes.Count > 0 && t.Nodes[t.Nodes.Count - 1].Kind == NodeKind.Rule).ToList();

        /// <summary>
        /// The gauntlet floor of the run's monster (chase trees out) holding <paramref name="tree"/> without its capstone, and with
        /// it, on the run seed.
        /// </summary>
        public static (int Base, int With) Measure(ContentDb c, RunState s, TreeDef tree)
        {
            int n = tree.Nodes.Count;
            int without = GauntletSim.Run(c, BalanceHarness.BuildFighter(c, s, new[] { (tree.Id, n - 1) }), s.Seed).FloorReached;
            int with = GauntletSim.Run(c, BalanceHarness.BuildFighter(c, s, new[] { (tree.Id, n) }), s.Seed).FloorReached;
            return (without, with);
        }

        /// <summary>
        /// Per tree (content order), one row per species (in <paramref name="species"/> order) and then the pooled row.
        /// <paramref name="samples"/>[t][k] is tree t on the k-th focus run; <paramref name="runSpecies"/>[k] is that run's species.
        /// </summary>
        public static List<TreePowerSummary> Summarise(IReadOnlyList<TreeDef> trees, IReadOnlyList<string> species,
                                                       IReadOnlyList<string> runSpecies, IReadOnlyList<(int Base, int With)>[] samples)
        {
            if (samples.Length != trees.Count) throw new ArgumentException("one sample list per tree");
            var list = new List<TreePowerSummary>();
            for (int t = 0; t < trees.Count; t++)
            {
                if (samples[t].Count != runSpecies.Count) throw new ArgumentException("one sample per focus run");
                string stat = trees[t].OfferStat ?? "any";
                foreach (var sp in species.Concat(new[] { AllSpecies }))
                {
                    var mine = Enumerable.Range(0, runSpecies.Count).Where(k => sp == AllSpecies || runSpecies[k] == sp).Select(k => samples[t][k]).ToList();
                    if (sp != AllSpecies && mine.Count == 0) continue;
                    list.Add(new TreePowerSummary
                    {
                        TreeId = trees[t].Id, Stat = stat, Species = sp, N = mine.Count,
                        BaseFloor = Distribution.Of(mine.Select(x => x.Base)),
                        Gain = Distribution.Of(mine.Select(x => x.With - x.Base)),
                    });
                }
            }
            return list;
        }
    }
}
