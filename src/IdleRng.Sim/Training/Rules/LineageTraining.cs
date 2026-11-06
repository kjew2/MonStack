using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Training
{
    /// <summary>Run-local milestone choices. Never contributes account lineage points.</summary>
    public sealed class LineageTrainingState
    {
        public string Choice { get; set; }
        public string Stat { get; set; }
        public string SourceStat { get; set; }
        public string CoreStat { get; set; }
        public int Charge { get; set; }
        public int Rhythm { get; set; }
        public bool Used { get; set; }
        public bool Armed { get; set; }
        public double Excess { get; set; }
        public double Adaptation { get; set; }
        public double RedirectGrowth { get; set; }
        public LineageTrainingState Clone() => (LineageTrainingState)MemberwiseClone();
    }

    public static partial class TrainingRun
    {
        // Each tree executes the established rules with its own counters. The temporary context
        // is always restored, including when a choice is rejected; it never changes birth rolls.
        sealed class SecondaryLineageContext : IDisposable
        {
            readonly RunState _run;
            readonly string _tree;
            readonly int _rank;
            readonly LineageTrainingState _state;
            public SecondaryLineageContext(RunState run, string tree, int rank)
            {
                _run = run; _tree = run.Lineage.TreeId; _rank = run.Lineage.Rank; _state = run.LineageTraining;
                run.Lineage.TreeId = tree; run.Lineage.Rank = rank;
                run.LineageTraining = run.SecondaryLineageTraining;
            }
            public void Dispose()
            {
                _run.SecondaryLineageTraining = _run.LineageTraining;
                _run.LineageTraining = _state;
                _run.Lineage.TreeId = _tree; _run.Lineage.Rank = _rank;
            }
        }
        static SecondaryLineageContext LTSecondary(RunState s)
        {
            string tree = s.Lineage?.SupportingTreeId;
            if (tree == null || tree == s.Lineage.TreeId) return null;
            int rank = s.Lineage.Progress != null && s.Lineage.Progress.TryGetValue(tree, out var progress) && progress != null ? progress.Rank : 1;
            return new SecondaryLineageContext(s, tree, rank);
        }
        public static void LineageChoice(ContentDb c, RunState s, string choice, string stat = null)
        {
            int split = choice?.IndexOf(':') ?? -1;
            if (split < 0) { LineageChoiceCurrent(c, s, choice, stat); return; }
            string tree = choice.Substring(0, split);
            LTRequire(tree == s.Lineage?.SupportingTreeId);
            using (var context = LTSecondary(s))
            {
                LTRequire(context != null);
                LineageChoiceCurrent(c, s, choice.Substring(split + 1), stat);
                var command = s.Log[s.Log.Count - 1]; command.LineageAction = choice; s.Log[s.Log.Count - 1] = command;
            }
        }
        public static IEnumerable<(string Choice, string Stat, string Label, string Description)> AvailableLineageChoices(ContentDb c, RunState s)
        {
            if (s.Lineage == null) yield break;
            foreach (var item in AvailableLineageChoicesCurrent(c, s)) yield return item;
            if (s.Lineage.SupportingTreeId == null) yield break;
            var secondary = s.Clone();
            using (var context = LTSecondary(secondary))
                if (context != null)
                    foreach (var item in AvailableLineageChoicesCurrent(c, secondary))
                        yield return (secondary.Lineage.TreeId + ":" + item.Choice, item.Stat,
                            c.Text(c.Trees[secondary.Lineage.TreeId].NameKey) + ": " + item.Label, item.Description);
        }
        public static string LineageSummary(ContentDb c, RunState s)
        {
            string focus = LineageSummaryCurrent(c, s);
            if (s.Lineage?.SupportingTreeId == null) return focus;
            var copy = s.Clone();
            using (var context = LTSecondary(copy))
            {
                if (context == null) return focus;
                return c.Text(c.Trees[s.Lineage.TreeId].NameKey) + ": " + focus + " "
                    + c.Text(c.Trees[copy.Lineage.TreeId].NameKey) + ": " + LineageSummaryCurrent(c, copy);
            }
        }
        static void LineageGrowth(ContentDb c, RunState s, string stat, ref double growth)
        {
            LineageGrowthCurrent(c, s, stat, ref growth);
            using (var context = LTSecondary(s)) if (context != null) LineageGrowthCurrent(c, s, stat, ref growth);
        }
        static void LineageDrillGain(ContentDb c, RunState s, string stat, DrillTier tier, ref double gain)
        {
            LineageDrillGainCurrent(c, s, stat, tier, ref gain);
            using (var context = LTSecondary(s)) if (context != null) LineageDrillGainCurrent(c, s, stat, tier, ref gain);
        }
        static void LineageInjury(ContentDb c, RunState s, double energy, ref double chance)
        {
            LineageInjuryCurrent(c, s, energy, ref chance);
            using (var context = LTSecondary(s)) if (context != null) LineageInjuryCurrent(c, s, energy, ref chance);
        }
        static void LineageDrillDone(ContentDb c, RunState s, DrillResult result, double energyBefore)
        {
            LineageDrillDoneCurrent(c, s, result, energyBefore);
            using (var context = LTSecondary(s)) if (context != null) LineageDrillDoneCurrent(c, s, result, energyBefore);
        }
        static void LineageCycleStart(ContentDb c, RunState s)
        {
            LineageCycleStartCurrent(c, s);
            using (var context = LTSecondary(s)) if (context != null) LineageCycleStartCurrent(c, s);
        }
        static void LineageCoreCost(ContentDb c, RunState s, ref double eat)
        {
            LineageCoreCostCurrent(c, s, ref eat);
            using (var context = LTSecondary(s)) if (context != null) LineageCoreCostCurrent(c, s, ref eat);
        }
        static void LineageDevour(ContentDb c, RunState s, ref string stat, ref double amount)
        {
            LineageDevourCurrent(c, s, ref stat, ref amount);
            using (var context = LTSecondary(s)) if (context != null) LineageDevourCurrent(c, s, ref stat, ref amount);
        }
        static void LineageRankStart(ContentDb c, RunState s, Fighter monster, Fighter opponent)
        {
            LineageRankStartCurrent(c, s, monster, opponent);
            using (var context = LTSecondary(s)) if (context != null) LineageRankStartCurrent(c, s, monster, opponent);
        }
        static void LineageRankResult(ContentDb c, RunState s, RankFightResult result)
        {
            LineageRankResultCurrent(c, s, result);
            using (var context = LTSecondary(s)) if (context != null) LineageRankResultCurrent(c, s, result);
        }
        static void LineageKitExtra(ContentDb c, RunState s, List<string> extra)
        {
            LineageKitExtraCurrent(c, s, extra);
            using (var context = LTSecondary(s)) if (context != null) LineageKitExtraCurrent(c, s, extra);
        }
        static double LTGain(ContentDb c) => c.ConstantValues["LINEAGE_TRAIN_GAIN"];
        static double LTCap(ContentDb c) => c.ConstantValues["LINEAGE_TRAIN_CAP"];
        static int LTCharge(ContentDb c) => (int)c.ConstantValues["LINEAGE_TRAIN_CHARGE"];
        static bool LTActive(ContentDb c, RunState s, string tree, int rank = 5) =>
            s.Lineage != null && s.Lineage.TreeId == tree && s.Lineage.Rank >= rank
            && AllTrees(s).Any(t => t.TreeId == tree && (t.Roll?.LineageRank ?? 0) >= rank
                && t.Bought >= c.Trees[tree].Nodes.Count) && HasRunRule(c, s, tree);
        static LineageTrainingState LTState(RunState s) => s.LineageTraining ?? (s.LineageTraining = new LineageTrainingState());
        static void LTRequire(bool value) { if (!value) throw new InvalidOperationException("Lineage choice is unavailable or lacks its required charge."); }
        static bool LTOrdinaryCap(ContentDb c, string id) => id != null && c.TreeNodes.TryGetValue(id, out var n)
            && c.Trees[n.TreeId].Kind != TreeKind.Chase && c.Trees[n.TreeId].Kind != TreeKind.Fusion
            && n.Index == c.Trees[n.TreeId].Nodes.Count - 1 && n.TreeId != "mastery";

        /// <summary>Explicit milestone decisions and resource exchanges; choices never draw RNG or award lineage points.</summary>
        static void LineageChoiceCurrent(ContentDb c, RunState s, string choice, string stat = null)
        {
            LTRequire(s.Phase != RunPhase.Finished && s.Lineage != null && LTActive(c, s, s.Lineage.TreeId));
            var d = s.LineageTraining ?? new LineageTrainingState();
            string tree = s.Lineage.TreeId;
            switch (tree)
            {
                case "heirloom":
                    LTRequire(StatIds.IsTrained(stat));
                    if (choice == "nominate")
                    {
                        LTRequire(d.Stat == null && AllTrees(s).Any(t => Nodes(c, t).Take(t.Bought).Any(n => n.Kind == NodeKind.GrowthUse && n.Stat == stat)));
                        d.Stat = stat;
                    }
                    else
                    {
                        LTRequire(choice == "redirect" && s.Lineage.Rank >= 10 && d.Stat != null && stat != d.Stat && !d.Used);
                        d.SourceStat = d.Stat;
                        d.RedirectGrowth = Math.Min(LTCap(c), d.Charge * LTGain(c)) * LTGain(c);
                        d.Stat = stat; d.Used = true;
                    }
                    break;
                case "mastery":
                    LTRequire(!d.Used && LTOrdinaryCap(c, stat) && (choice == "preserve" || choice == "pursue"));
                    LTRequire(choice == "preserve" ? s.Mastered != null && s.Mastered.Contains(stat)
                        : AllTrees(s).Any(t => t.TreeId == c.TreeNodes[stat].TreeId && t.Bought > c.TreeNodes[stat].Index));
                    d.Choice = choice; d.Stat = stat; d.Used = true;
                    break;
                case "limit_break":
                    LTRequire(s.Lineage.Rank >= 10 && !d.Used && (choice == "offense" || choice == "protection"));
                    d.Choice = choice; d.Used = true;
                    break;
                case "iron_vow":
                    LTRequire(choice == "recover" && s.Lineage.Rank >= 10 && d.Rhythm >= LTCharge(c) && s.Phase == RunPhase.Training);
                    d.Rhythm = 0; s.Energy = Math.Min(c.Constants.RunEnergyMax, s.Energy + c.Constants.RunEnergyMax * LTCap(c));
                    break;
                case "cursed_mark":
                    LTRequire(choice == "specialize" && StatIds.IsTrained(stat) && stat != StatIds.Def && !d.Used);
                    d.Stat = stat; d.Used = true;
                    break;
                case "devourer":
                    LTRequire((choice == "absorb" || (choice == "adapt" && s.Lineage.Rank >= 10)) && StatIds.IsTrained(stat));
                    d.Choice = choice; d.Stat = stat;
                    break;
                case "second_breath":
                    LTRequire(choice == "stabilize" && s.Lineage.Rank >= 10 && d.Charge >= LTCharge(c) && !d.Armed);
                    d.Charge -= LTCharge(c); d.Armed = true;
                    break;
                case "ascetic":
                    LTRequire(s.Phase == RunPhase.Training || choice == "release");
                    if (choice == "bank") { LTRequire(d.Charge == 0 && s.ApLeft > 1); s.ApLeft--; d.Charge = 1; }
                    else if (choice == "release") { LTRequire(d.Charge > 0); s.ApLeft++; d.Charge = 0; s.Phase = RunPhase.Training; }
                    else { LTRequire(choice == "exchange" && s.Lineage.Rank >= 10 && !d.Used && s.ApLeft > 1); s.ApLeft--; s.Sp += c.Constants.SpPerWin; d.Used = true; }
                    break;
                case "hungry_core":
                    LTRequire(s.Cycle < c.Constants.RunCycles && (choice == "modest" || choice == "heavy" || (choice == "specialize" && s.Lineage.Rank >= 10 && StatIds.IsTrained(stat))));
                    // Feeding is committed before the next cycle; no free retuning of a paid meal.
                    LTRequire(!d.Used);
                    d.Choice = choice; d.Stat = stat; d.Used = true;
                    break;
                default: throw new InvalidOperationException("This lineage has no training choice.");
            }
            s.LineageTraining = d;
            s.Log.Add(new RunCommand(RunCommandKind.LineageChoice, stat) { LineageAction = choice });
        }

        /// <summary>Host-driven actionable choices; validation uses a clone and consumes no production state or RNG.</summary>
        static IEnumerable<(string Choice, string Stat, string Label, string Description)> AvailableLineageChoicesCurrent(ContentDb c, RunState s)
        {
            if (s.Lineage == null) yield break;
            var candidates = new List<(string Choice, string Stat, string Label, string Description)>();
            void Add(string action, string label, string description, string stat = null) => candidates.Add((action, stat, label, description));
            switch (s.Lineage.TreeId)
            {
                case "heirloom":
                    foreach (string stat in StatIds.Trained) {
                        Add("nominate", "Keep " + stat.ToUpperInvariant(), "Commit a bought keepsake stat; its drills slowly build bounded extra growth.", stat);
                        Add("redirect", "Redirect to " + stat.ToUpperInvariant(), "Once per run, move a small share of built keepsake growth to this stat.", stat);
                    }
                    break;
                case "mastery":
                    foreach (var t in c.Trees.Values) {
                        string id = t.Nodes[t.Nodes.Count - 1].Id;
                        Add("preserve", "Preserve " + System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(t.Id.Replace('_', ' ')), "Commit to one mastered ordinary capstone; other retained mastered capstones stop applying.", id);
                        Add("pursue", "Pursue " + System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(t.Id.Replace('_', ' ')), "Commit to a bought ordinary capstone; each rank victory advances its mastery one extra step.", id);
                    }
                    break;
                case "limit_break":
                    Add("offense", "Offense", "Once per run, commit stored excess training to STR in rank fights.");
                    Add("protection", "Protection", "Once per run, commit stored excess training to DEF in rank fights.");
                    break;
                case "iron_vow": Add("recover", "Recover through discipline", "Spend " + LTCharge(c) + " rhythm to restore up to " + (c.Constants.RunEnergyMax * LTCap(c)).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " energy. This is not Rest and preserves the vow."); break;
                case "cursed_mark":
                    foreach (string stat in StatIds.Trained) Add("specialize", "Curse " + stat.ToUpperInvariant(), "Commit this non-DEF specialization; rank opponents lose more of this stat.", stat);
                    break;
                case "devourer":
                    foreach (string stat in StatIds.Trained) {
                        Add("absorb", "Absorb into " + stat.ToUpperInvariant(), "Direct the next devoured growth into this stat. Cycle upkeep stays unchanged.", stat);
                        Add("adapt", "Adapt " + stat.ToUpperInvariant(), "Trade part of future permanent devoured growth for a bounded bonus in the next rank fight.", stat);
                    }
                    break;
                case "second_breath": Add("stabilize", "Stabilize next drill", "Spend " + LTCharge(c) + " reserve charges to prevent injury on the next drill; failure and Super chances stay unchanged."); break;
                case "ascetic":
                    Add("bank", "Bank an action", "Save one AP within this cycle; release it before the cycle ends or lose it.");
                    Add("release", "Release saved action", "Return the saved AP to this cycle.");
                    Add("exchange", "Exchange action for SP", "Once per run, exchange one AP for the normal victory SP amount. Rank wins still grant no SP.");
                    break;
                case "hungry_core":
                    Add("modest", "Modest feeding", "Next cycle, pay less upkeep for a smaller fed rank-fight bonus. Starvation still disables the core.");
                    Add("heavy", "Heavy feeding", "Next cycle, pay more upkeep for a larger fed rank-fight bonus. Starvation still disables the core.");
                    foreach (string stat in StatIds.Trained) Add("specialize", "Feed " + stat.ToUpperInvariant(), "Next cycle, keep normal upkeep and move part of the other fed stat bonuses into this stat.", stat);
                    break;
            }
            foreach (var item in candidates) {
                bool valid;
                try { LineageChoice(c, s.Clone(), item.Choice, item.Stat); valid = true; }
                catch (InvalidOperationException) { valid = false; }
                if (valid) yield return item;
            }
        }

        static string LineageSummaryCurrent(ContentDb c, RunState s)
        {
            var d = s.LineageTraining;
            if (s.Lineage == null || d == null) return "Choose a milestone direction when its capstone is active.";
            string Name(string id) => id == StatIds.Hp ? "Health" : id == StatIds.Str ? "Strength" : id == StatIds.Int ? "Intelligence"
                : id == StatIds.Wis ? "Wisdom" : id == StatIds.Dex ? "Dexterity" : id == StatIds.Def ? "Defense" : id == StatIds.Spd ? "Speed" : id == StatIds.Luk ? "Luck" : "Unchosen";
            switch (s.Lineage.TreeId)
            {
                case "heirloom": return Name(d.Stat) + " heirloom; " + d.Charge + " inherited drills" + (d.Used ? "; growth redirect used." : ".");
                case "mastery": return d.Choice == null ? "Choose a mastery commitment." : (d.Choice == "preserve" ? "Preserving one mastered capstone." : "Pursuing a new mastered capstone.");
                case "limit_break": return "Stored excess: " + d.Excess.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "; " + (d.Choice == "offense" ? "Strength manifestation." : "Defense manifestation.");
                case "iron_vow": return "Training rhythm: " + d.Rhythm + "/" + LTCharge(c) + ". Rest remains forbidden.";
                case "cursed_mark": return Name(d.Stat) + " curse specialization. Defense drills remain disabled.";
                case "devourer": return Name(d.Stat) + (d.Choice == "adapt" ? " adaptation" : " absorption") + "; stored adaptation: " + d.Adaptation.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ".";
                case "second_breath": return "Refund reserve: " + d.Charge + "/" + LTCharge(c) + (d.Armed ? "; next drill stabilized." : ".");
                case "ascetic": return "Saved cycle actions: " + d.Charge + (d.Used ? "; SP exchange used." : "; SP exchange unused.") + " Rank wins still grant no SP.";
                case "hungry_core": return (s.CoreDormant ? "Starved core." : "Fed core.") + " Next feeding: " + (d.Choice ?? "normal") + (d.Choice == "specialize" ? " (" + Name(d.Stat) + ")" : "") + ".";
                default: return "No training milestone choice.";
            }
        }

        static void LineageGrowthCurrent(ContentDb c, RunState s, string stat, ref double growth)
        {
            var d = s.LineageTraining;
            if (d == null) return;
            if (LTActive(c, s, "heirloom"))
            {
                double bonus = Math.Min(LTCap(c), d.Charge * LTGain(c));
                if (d.SourceStat == null && stat == d.Stat) growth += bonus;
                if (d.SourceStat != null && stat == d.SourceStat) growth += bonus - d.RedirectGrowth;
                if (d.SourceStat != null && stat == d.Stat) growth += d.RedirectGrowth;
            }
            if (LTActive(c, s, "mastery", 10) && d.Choice == "preserve" && s.Mastered != null && s.Mastered.Contains(d.Stat)) growth += LTGain(c);
        }

        static void LineageDrillGainCurrent(ContentDb c, RunState s, string stat, DrillTier tier, ref double gain)
        {
            if (LTActive(c, s, "iron_vow")) gain = Math.Floor(gain * (1 + Math.Min(LTCap(c), (s.LineageTraining?.Rhythm ?? 0) * LTGain(c))));
            if (LTActive(c, s, "limit_break") && s.Stats.Get(stat) >= c.Constants.StatMax
                && AllTrees(s).Any(t => HoldsRule(c, t, RuleModifiers.LimitBreak) && OwnStat(c, t) == stat))
            {
                var d = LTState(s);
                d.Excess = Math.Min(LTCap(c) * c.Constants.StatMax, d.Excess + gain * LTGain(c));
            }
        }

        static void LineageInjuryCurrent(ContentDb c, RunState s, double e, ref double chance)
        {
            if (LTActive(c, s, "second_breath", 10) && s.LineageTraining?.Armed == true) chance = 0;
        }

        static void LineageDrillDoneCurrent(ContentDb c, RunState s, DrillResult result, double energyBefore)
        {
            if (LTActive(c, s, "heirloom") && s.LineageTraining != null && (s.LineageTraining.SourceStat ?? s.LineageTraining.Stat) == result.Stat)
            { var d = LTState(s); d.Charge = Math.Min(LTCharge(c), d.Charge + 1); }
            if (LTActive(c, s, "iron_vow"))
            { var d = LTState(s); d.Rhythm = d.Stat == result.Stat ? Math.Min(LTCharge(c), d.Rhythm + 1) : 1; d.Stat = result.Stat; }
            if (LTActive(c, s, "second_breath"))
            {
                var d = LTState(s); d.Armed = false;
                if (result.Refunded > 0) d.Charge = Math.Min(LTCharge(c), d.Charge + 1);
            }
        }

        static void LineageCycleStartCurrent(ContentDb c, RunState s)
        {
            if (LTActive(c, s, "ascetic") && s.LineageTraining != null) s.LineageTraining.Charge = 0;
            if (LTActive(c, s, "hungry_core") && s.LineageTraining != null) s.LineageTraining.Used = false;
        }

        static void LineageCoreCostCurrent(ContentDb c, RunState s, ref double eat)
        {
            if (!LTActive(c, s, "hungry_core")) return;
            var d = LTState(s);
            string choice = d.Choice;
            d.SourceStat = choice; d.CoreStat = d.Stat;
            eat *= choice == "heavy" ? 1 + LTCap(c) : choice == "modest" ? 1 - LTCap(c) : 1;
        }

        static void LineageDevourCurrent(ContentDb c, RunState s, ref string stat, ref double amount)
        {
            if (!LTActive(c, s, "devourer") || s.LineageTraining?.Stat == null) return;
            var d = s.LineageTraining;
            stat = d.Stat;
            if (d.Choice == "adapt" && s.Lineage.Rank >= 10)
            { d.Adaptation = Math.Min(c.Constants.StatMax * LTCap(c), d.Adaptation + amount * LTCap(c)); amount *= 1 - LTCap(c); }
            amount = Math.Floor(amount);
        }

        static void LineageRankStartCurrent(ContentDb c, RunState s, Fighter monster, Fighter opponent)
        {
            var d = s.LineageTraining;
            if (d == null) return;
            if (LTActive(c, s, "iron_vow")) { d.Rhythm = 0; d.Stat = null; }
            if (LTActive(c, s, "cursed_mark") && d.Stat != null) Set(opponent.Stats, d.Stat, opponent.Stats.Get(d.Stat) * (1 - LTGain(c)));
            if (LTActive(c, s, "limit_break") && d.Excess > 0)
            {
                string stat = d.Choice == "offense" ? StatIds.Str : StatIds.Def;
                Set(monster.Stats, stat, monster.Stats.Get(stat) + d.Excess);
            }
            if (LTActive(c, s, "devourer", 10) && d.Adaptation > 0)
            { Set(monster.Stats, d.Stat, monster.Stats.Get(d.Stat) + d.Adaptation); d.Adaptation = 0; }
            if (LTActive(c, s, "hungry_core") && CoreFed(c, s))
            {
                double delta = c.ConstantValues["HUNGRY_STAT"] * LTCap(c);
                string mode = d.SourceStat;
                foreach (string stat in StatIds.Trained)
                {
                    double add = mode == "heavy" ? delta : mode == "modest" ? -delta : 0;
                    if (mode == "specialize" && s.Lineage.Rank >= 10) add = stat == d.CoreStat ? delta * (StatIds.Trained.Length - 1) : -delta;
                    Set(monster.Stats, stat, Math.Max(0, monster.Stats.Get(stat) + add));
                }

            }
        }

        static void LineageRankResultCurrent(ContentDb c, RunState s, RankFightResult result)
        {
            var d = s.LineageTraining;
            if (!result.Won || !LTActive(c, s, "mastery") || d?.Choice != "pursue") return;
            foreach (var t in AllTrees(s))
                if (t.TreeId == c.TreeNodes[d.Stat].TreeId && t.Bought > c.TreeNodes[d.Stat].Index)
                {
                    t.MasteryWins++;
                    if (t.MasteryWins >= new LaneFConstants(c).MasteryWins)
                    { if (s.Mastered == null) s.Mastered = new List<string>(); if (!s.Mastered.Contains(d.Stat)) s.Mastered.Add(d.Stat); }
                }
        }

        static void LineageKitExtraCurrent(ContentDb c, RunState s, List<string> extra)
        {
            if (LTActive(c, s, "mastery") && s.LineageTraining?.Choice == "preserve" && s.Mastered != null)
                extra.RemoveAll(id => s.Mastered.Contains(id) && id != s.LineageTraining.Stat);
        }

        static void LineageHash(CanonicalWriter w, RunState s)
        {
            LineageTrainingHash(w, s.LineageTraining, "lineage_training");
            LineageTrainingHash(w, s.SecondaryLineageTraining, "secondary_lineage_training");
        }
        static void LineageTrainingHash(CanonicalWriter w, LineageTrainingState d, string marker)
        {
            if (d == null) return;
            w.Str(marker); w.Str(d.Choice); w.Str(d.Stat); w.Str(d.SourceStat); w.Str(d.CoreStat);
            w.I32(d.Charge); w.I32(d.Rhythm); w.Bool(d.Used); w.Bool(d.Armed);
            w.F64(d.Excess); w.F64(d.Adaptation); w.F64(d.RedirectGrowth);
        }
    }
}
