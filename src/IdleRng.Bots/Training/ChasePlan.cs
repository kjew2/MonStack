using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;

namespace IdleRng.Bots.Training
{
    /// <summary>
    /// The chase target of a run (specs/15 section 5, issue #111): one of the run species' own chase trees whose capstone is not
    /// bought yet and whose prerequisite trees can still be had, read from content (chase_conditions), never hard-coded.
    /// </summary>
    public sealed class ChaseTarget
    {
        public TreeDef Tree { get; private set; }

        /// <summary>Prerequisite trees with the nodes the conditions need (tree_maxed: all; node_owned: up to that node).</summary>
        public List<(TreeDef Tree, int Nodes)> Prereqs { get; } = new List<(TreeDef, int)>();

        /// <summary>Stats still below what a condition needs, with the value needed, in condition order. A drafted prerequisite
        /// that is not slotted yet adds its offer stat up to the next milestone that would draft it.</summary>
        public List<(string Stat, double Value)> StatGoals { get; } = new List<(string, double)>();

        /// <summary>SP still needed: unbought prerequisite nodes plus the chase tree's unbought nodes.</summary>
        public int RemainingSp { get; private set; }

        /// <summary>Trees the target needs (its prerequisites and the chase tree itself): never overwritten for filler.</summary>
        public HashSet<string> Needed { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// The target now: a slotted own chase tree whose capstone is not bought; else, among the feasible own chase trees, the
        /// one with the fewest stat goals (drills it still needs), then the fewest SP still needed, then content order. Null when none.
        /// </summary>
        public static ChaseTarget Of(ContentDb c, RunState s)
        {
            ChaseTarget best = null;
            foreach (var t in c.TreeList)
            {
                if (t.Kind != TreeKind.Chase || t.Species != s.SpeciesId) continue;
                var x = Build(c, s, t);
                if (x == null) continue;
                if (Slot(s, t.Id) >= 0) return x;
                if (best == null || x.StatGoals.Count < best.StatGoals.Count
                    || x.StatGoals.Count == best.StatGoals.Count && x.RemainingSp < best.RemainingSp) best = x;
            }
            return best;
        }

        /// <summary>The target for tree t, or null when its capstone is bought already or a prerequisite cannot be had.</summary>
        static ChaseTarget Build(ContentDb c, RunState s, TreeDef t)
        {
            int own = Slot(s, t.Id);
            if (own >= 0 && s.Slots[own].Bought >= t.Nodes.Count) return null;
            var x = new ChaseTarget { Tree = t };
            x.Needed.Add(t.Id);
            x.RemainingSp = Cost(t, own >= 0 ? s.Slots[own].Bought : 0, t.Nodes.Count);
            foreach (var cd in t.Conditions)
            {
                switch (cd.Type)
                {
                    case ConditionType.TreeMaxed:
                    case ConditionType.NodeOwned:
                    {
                        var p = cd.Type == ConditionType.TreeMaxed ? c.Trees[cd.Ref] : c.Trees[c.TreeNodes[cd.Ref].TreeId];
                        int need = cd.Type == ConditionType.TreeMaxed ? p.Nodes.Count : c.TreeNodes[cd.Ref].Index + 1;
                        int slot = Slot(s, p.Id);
                        if (slot < 0 && !Obtainable(c, s, p, x)) return null;
                        int bought = slot >= 0 ? s.Slots[slot].Bought : 0;
                        int k = x.Prereqs.FindIndex(q => q.Tree.Id == p.Id);   // two conditions on one tree: the deeper one counts
                        if (k >= 0)
                        {
                            if (need <= x.Prereqs[k].Nodes) break;
                            x.RemainingSp -= Cost(p, bought, x.Prereqs[k].Nodes);
                            x.Prereqs[k] = (p, need);
                        }
                        else x.Prereqs.Add((p, need));
                        x.Needed.Add(p.Id);
                        x.RemainingSp += Cost(p, bought, need);
                        break;
                    }
                    case ConditionType.StatAtLeast:
                        if (s.Stats.Get(cd.Ref) < cd.Amount) x.StatGoals.Add((cd.Ref, cd.Amount));
                        break;
                }
            }
            return x;
        }

        /// <summary>
        /// Whether a prerequisite tree that is not slotted can still be had: it is pending in an offer; a book tree whose book
        /// the run still holds; or a generic tree a milestone not yet crossed would draft (its offer stat goes into StatGoals).
        /// A native tree that left its slot cannot come back.
        /// </summary>
        static bool Obtainable(ContentDb c, RunState s, TreeDef p, ChaseTarget x)
        {
            if (s.Offers.Any(o => o.Trees.Contains(p.Id))) return true;
            switch (p.Kind)
            {
                case TreeKind.Book: return s.Books.Any(b => c.SkillBooks[b].TreeId == p.Id);
                case TreeKind.Generic:
                {
                    var stats = p.OfferStat != null ? new[] { p.OfferStat } : StatIds.Trained;
                    foreach (int m in c.StatMilestones)
                    {
                        if (m < p.OfferThreshold || m > c.Constants.StatMax) continue;
                        foreach (var st in stats)
                            if (!s.Milestones.Exists(h => h.Stat == st && h.Threshold == m))
                            {
                                if (p.OfferStat != null && s.Stats.Get(st) < m) x.StatGoals.Add((st, m));
                                return true;
                            }
                    }
                    return false;
                }
                default: return false;
            }
        }

        static int Cost(TreeDef t, int from, int to)
        {
            int sp = 0;
            for (int i = from; i < to && i < t.Nodes.Count; i++) sp += t.Nodes[i].Cost;
            return sp;
        }

        public static int Slot(RunState s, string treeId) => Array.FindIndex(s.Slots, x => x != null && x.TreeId == treeId);

        /// <summary>The species' own books: those whose tree is a prerequisite of one of its own chase trees.</summary>
        public static bool IsOwnBook(ContentDb c, string speciesId, string bookId)
        {
            string tree = c.SkillBooks[bookId].TreeId;
            foreach (var t in c.TreeList)
            {
                if (t.Kind != TreeKind.Chase || t.Species != speciesId) continue;
                foreach (var cd in t.Conditions)
                    if (cd.Type == ConditionType.TreeMaxed && cd.Ref == tree
                        || cd.Type == ConditionType.NodeOwned && c.TreeNodes[cd.Ref].TreeId == tree) return true;
            }
            return false;
        }
    }
}
