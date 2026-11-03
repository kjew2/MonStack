using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;

namespace IdleRng.Sim.Training
{
    // Idea-pool lane F: tree and kit build (#139; specs/15 section 11). Run rules: they change how the monster is built, not the
    // fight. A rule counts while its capstone is bought in a slotted tree ("held"); its rule id in the fight kit does nothing.
    //   mimic_core   KitExtra: the capstone node of the tree in the slot just before it joins the kit, bought or not (none in
    //                slot 0 or after an empty slot; chase and fusion capstones are never copied).
    //   heirloom     ModifyDrillGain counts the drills of each bought growth_use node's stat; ModifyGrowth adds the node's amount
    //                plus KEEPSAKE_STEP per KEEPSAKE_DRILLS of those drills, up to KEEPSAKE_MAX (the capstone doubles the step).
    //   mastery      OnRankFightResult: a win counts for every slotted tree with its capstone bought; at MASTERY_WINS it is
    //                mastered. KitExtra: a mastered tree no longer held keeps its capstone node in the kit, last.
    //   limit_break  RollOwnStat rolls the tree's own stat (one draw on the draft stream, only for this tree); ModifyStatCap
    //                raises that stat's cap to LIMIT_BREAK_MAX; OnTreeRemoved brings a stat back down to its cap when it leaves.
    // TODO(#163): the balance tree-power section measures fight capstones only, so it reads 0 for these run rules.
    public static partial class TrainingRun
    {
        /// <summary>Lane F's constants (specs/15 section 11), checked when read.</summary>
        internal readonly struct LaneFConstants
        {
            public readonly double KeepsakeStep, KeepsakeMax, LimitBreakMax;
            public readonly int KeepsakeDrills, MasteryWins;

            public LaneFConstants(ContentDb c)
            {
                var k = c.ConstantValues;
                KeepsakeStep = k["KEEPSAKE_STEP"];
                KeepsakeDrills = (int)k["KEEPSAKE_DRILLS"];
                KeepsakeMax = k["KEEPSAKE_MAX"];
                MasteryWins = (int)k["MASTERY_WINS"];
                LimitBreakMax = k["LIMIT_BREAK_MAX"];
                if (!(KeepsakeStep >= 0 && KeepsakeDrills >= 1 && KeepsakeMax >= 0 && MasteryWins >= 1 && LimitBreakMax >= c.Constants.StatMax))
                    throw new InvalidOperationException("lane F run rule constants out of range (specs/15 section 11)");
            }
        }

        /// <summary>Whether the slotted tree has every node bought and its capstone is the rule <paramref name="ruleId"/>.</summary>
        static bool HoldsRule(ContentDb c, TreeSlot ts, string ruleId)
        {
            if (ts == null) return false;
            var nodes = c.Trees[ts.TreeId].Nodes;
            var last = nodes[nodes.Count - 1];
            return ts.Bought >= nodes.Count && last.Kind == NodeKind.Rule && last.RuleId == ruleId;
        }

        /// <summary>Whether a slotted tree holds the capstone rule <paramref name="ruleId"/> (bought).</summary>
        public static bool RunHolds(ContentDb c, RunState s, string ruleId)
        {
            foreach (var ts in s.Slots) if (HoldsRule(c, ts, ruleId)) return true;
            return false;
        }

        /// <summary>A tree whose capstone mimic_core copies and mastery keeps: not a chase or fusion tree (their reachability targets hold).</summary>
        static bool CapstoneShareable(TreeDef t) => t.Kind != TreeKind.Chase && t.Kind != TreeKind.Fusion;

        /// <summary>A slotted tree's own stat: its roll's, else its offer_stat, else the stat of its first filler node (specs/15 section 4.1).</summary>
        public static string OwnStat(ContentDb c, TreeSlot ts)
        {
            if (ts.Roll != null) return ts.Roll.Stat;
            var t = c.Trees[ts.TreeId];
            return t.OfferStat ?? t.Nodes.FirstOrDefault(n => IsFiller(n.Kind))?.Stat;
        }

        // ------------------------------------------------------------------ limit_break

        static partial void RollOwnStat_F(ContentDb c, RngStream r, TreeDef t, ref string own)
        {
            var last = t.Nodes[t.Nodes.Count - 1];
            if (last.Kind != NodeKind.Rule || last.RuleId != RuleModifiers.LimitBreak) return;
            var ids = StatIds.Trained;
            own = ids[Math.Min(ids.Length - 1, (int)(r.NextDouble() * ids.Length))];
        }

        static partial void ModifyStatCap_F(ContentDb c, RunState s, string stat, ref double cap)
        {
            foreach (var ts in s.Slots)
                if (HoldsRule(c, ts, RuleModifiers.LimitBreak) && OwnStat(c, ts) == stat)
                {
                    cap = Math.Max(cap, new LaneFConstants(c).LimitBreakMax);
                    return;
                }
        }

        /// <summary>A Limit Break that leaves takes its raised cap with it: a stat above its cap now comes back down to it.</summary>
        static partial void OnTreeRemoved_F(ContentDb c, RunState s)
        {
            foreach (var id in StatIds.Trained)
            {
                double cap = StatCap(c, s, id);
                if (s.Stats.Get(id) > cap) Set(s.Stats, id, cap);
            }
        }

        // ------------------------------------------------------------------ heirloom

        /// <summary>Counts the drill for every bought growth_use node of the drilled stat (it raises their growth from the next drill on).</summary>
        static partial void ModifyDrillGain_F(ContentDb c, RunState s, string stat, DrillTier tier, ref double gain)
        {
            foreach (var ts in s.Slots)
            {
                if (ts == null) continue;
                var nodes = Nodes(c, ts);
                for (int i = 0; i < ts.Bought; i++)
                {
                    if (nodes[i].Kind != NodeKind.GrowthUse || nodes[i].Stat != stat) continue;
                    if (ts.KeepsakeDrills == null) ts.KeepsakeDrills = new int[nodes.Count];
                    ts.KeepsakeDrills[i]++;
                }
            }
        }

        static partial void ModifyGrowth_F(ContentDb c, RunState s, string stat, ref double growth)
        {
            LaneFConstants? k = null;
            bool doubled = false;
            foreach (var ts in s.Slots)
            {
                if (ts == null) continue;
                var nodes = Nodes(c, ts);
                for (int i = 0; i < ts.Bought; i++)
                {
                    if (nodes[i].Kind != NodeKind.GrowthUse || nodes[i].Stat != stat) continue;
                    if (k == null) { k = new LaneFConstants(c); doubled = RunHolds(c, s, RuleModifiers.Heirloom); }
                    growth += nodes[i].Amount + KeepsakeBonus(k.Value, ts.KeepsakeDrills == null ? 0 : ts.KeepsakeDrills[i], doubled);
                }
            }
        }

        /// <summary>A growth_use node's extra growth after <paramref name="drills"/> drills of its stat: KEEPSAKE_STEP (doubled by the heirloom capstone) per KEEPSAKE_DRILLS, up to KEEPSAKE_MAX.</summary>
        internal static double KeepsakeBonus(LaneFConstants k, int drills, bool doubled) =>
            Math.Min(k.KeepsakeMax, (drills / k.KeepsakeDrills) * k.KeepsakeStep * (doubled ? 2 : 1));

        /// <summary>The extra growth a slotted tree's growth_use node <paramref name="index"/> has reached now (the UI shows it).</summary>
        public static double KeepsakeBonus(ContentDb c, RunState s, TreeSlot ts, int index) =>
            KeepsakeBonus(new LaneFConstants(c), ts.KeepsakeDrills == null || index >= ts.KeepsakeDrills.Length ? 0 : ts.KeepsakeDrills[index],
                          RunHolds(c, s, RuleModifiers.Heirloom));

        // ------------------------------------------------------------------ mastery

        static partial void OnRankFightResult_F(ContentDb c, RunState s, RankFightResult result)
        {
            if (!result.Won || !RunHolds(c, s, RuleModifiers.Mastery)) return;
            int need = new LaneFConstants(c).MasteryWins;
            foreach (var ts in s.Slots)
            {
                if (ts == null) continue;
                var t = c.Trees[ts.TreeId];
                var cap = t.Nodes[t.Nodes.Count - 1];
                if (!CapstoneShareable(t) || ts.Bought < t.Nodes.Count || (cap.Kind == NodeKind.Rule && cap.RuleId == RuleModifiers.Mastery)) continue;
                if (s.Mastered != null && s.Mastered.Contains(cap.Id)) continue;
                ts.MasteryWins++;
                if (ts.MasteryWins < need) continue;
                if (s.Mastered == null) s.Mastered = new List<string>();
                s.Mastered.Add(cap.Id);
            }
        }

        // ------------------------------------------------------------------ the kit: mimic_core, then mastery

        static partial void KitExtra_F(ContentDb c, RunState s, List<string> extra)
        {
            for (int i = 1; i < s.Slots.Length; i++)
            {
                var prev = s.Slots[i - 1];
                if (prev == null || !HoldsRule(c, s.Slots[i], RuleModifiers.MimicCore)) continue;
                var t = c.Trees[prev.TreeId];
                if (CapstoneShareable(t)) extra.Add(t.Nodes[t.Nodes.Count - 1].Id);
            }
            if (s.Mastered == null || !RunHolds(c, s, RuleModifiers.Mastery)) return;
            foreach (var id in s.Mastered)
            {
                var n = c.TreeNodes[id];
                // Still in the kit through its own tree: nothing to keep.
                if (AllTrees(s).Any(ts => ts.TreeId == n.TreeId && ts.Bought > n.Index)) continue;
                extra.Add(id);
            }
        }

        // ------------------------------------------------------------------ hash

        /// <summary>heirloom's drill counts and mastery's wins and mastered capstones, only when the run has any.</summary>
        static partial void HashRun_F(CanonicalWriter w, RunState s)
        {
            bool any = s.Mastered != null || AllTrees(s).Any(ts => ts.KeepsakeDrills != null || ts.MasteryWins != 0);
            if (!any) return;
            w.Str("lane_f");
            foreach (var ts in s.Slots)
            {
                w.Bool(ts != null);
                if (ts == null) continue;
                foreach (var part in ts.Absorbed.Append(ts))
                {
                    w.I32(part.MasteryWins);
                    w.I32(part.KeepsakeDrills?.Length ?? -1);
                    if (part.KeepsakeDrills != null) foreach (var d in part.KeepsakeDrills) w.I32(d);
                }
            }
            w.I32(s.Mastered?.Count ?? -1);
            if (s.Mastered != null) foreach (var id in s.Mastered) w.Str(id);
        }
    }
}
