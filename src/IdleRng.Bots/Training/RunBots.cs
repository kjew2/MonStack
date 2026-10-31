using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using IdleRng.Bots.Report;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;

namespace IdleRng.Bots.Training
{
    /// <summary>A bot that plays a training run (specs/14 section 9): the next command, or null when it has nothing left to do.</summary>
    public interface IRunPolicy
    {
        string Name { get; }
        RunCommand Next(ContentDb c, RunState s);
    }

    /// <summary>
    /// Resolve offers, use books, buy nodes once the cycle reaches <c>spendFrom</c>, fight when due, rest below a threshold
    /// energy fraction, else drill the stat the pick function chooses. Without <c>chase</c>: take offers into an empty slot (a
    /// chase tree overwrites the least-invested slot that is not one of its prerequisites; otherwise discard), use every book,
    /// buy the first affordable next node in slot order. With <c>chase</c> (issue #111) the policy works toward its
    /// <see cref="ChaseTarget"/>: it uses only the species' own book; a draft that holds a tree the target needs gives that tree,
    /// and a needed tree, an own book tree or a chase tree overwrites the least-invested slot the target does not need; it buys
    /// the target's nodes first (prerequisites in condition order, then the chase tree) and other nodes only while the points
    /// left plus the cycle points still to come cover what the target needs; and it drills the target's unmet stats before the
    /// pick function.
    /// </summary>
    public sealed class ThresholdRunPolicy : IRunPolicy
    {
        readonly double _restBelow;
        readonly int _spendFrom;
        readonly Func<ContentDb, RunState, string> _pick;
        readonly bool _chase;

        public ThresholdRunPolicy(string name, double restBelow, Func<ContentDb, RunState, string> pick, int spendFrom = 1, bool chase = false)
        {
            Name = name; _restBelow = restBelow; _pick = pick; _spendFrom = spendFrom; _chase = chase;
        }

        public string Name { get; }

        public RunCommand Next(ContentDb c, RunState s)
        {
            if (s.Offers.Count > 0) return ResolveOffer(c, s);
            foreach (var b in s.Books)
            {
                if (_chase && !ChaseTarget.IsOwnBook(c, s.SpeciesId, b)) continue;
                if (ChaseTarget.Slot(s, c.SkillBooks[b].TreeId) < 0) return new RunCommand(RunCommandKind.UseBook) { Book = b };
            }
            var target = _chase ? ChaseTarget.Of(c, s) : null;
            if (s.Cycle >= _spendFrom || s.Phase == RunPhase.Finished)
            {
                var spend = target == null ? FirstAffordable(c, s, 0) : PlannedSpend(c, s, target);
                if (spend != null) return spend;
            }
            if (s.Phase == RunPhase.Finished) return null;
            if (s.Phase == RunPhase.AwaitFight) return new RunCommand(RunCommandKind.Fight);
            if (TrainingRun.EnergyFraction(c.Constants, s.Energy) < _restBelow) return new RunCommand(RunCommandKind.Rest);
            if (target != null)
                foreach (var (stat, _) in target.StatGoals)
                    if (s.Stats.Get(stat) < c.Constants.StatMax) return new RunCommand(RunCommandKind.Drill, stat);
            return new RunCommand(RunCommandKind.Drill, _pick(c, s));
        }

        /// <summary>The first slot whose next node is affordable with <paramref name="reserve"/> SP left over.</summary>
        static RunCommand FirstAffordable(ContentDb c, RunState s, int reserve)
        {
            for (int i = 0; i < s.Slots.Length; i++)
            {
                var ts = s.Slots[i];
                if (ts == null) continue;
                var nodes = c.Trees[ts.TreeId].Nodes;
                if (ts.Bought < nodes.Count && s.Sp - nodes[ts.Bought].Cost >= reserve) return new RunCommand(RunCommandKind.SpendPoint) { Slot = i };
            }
            return null;
        }

        /// <summary>
        /// The target's next affordable node (prerequisites in condition order, then the chase tree); else a filler node, if the
        /// points left after it plus the cycle points still to come cover what the target needs. Once the run is finished, a
        /// target whose nodes are all unaffordable or bought can no longer progress, so nothing is held back.
        /// </summary>
        static RunCommand PlannedSpend(ContentDb c, RunState s, ChaseTarget target)
        {
            foreach (var (tree, need) in target.Prereqs.Concat(new[] { (target.Tree, target.Tree.Nodes.Count) }))
            {
                int slot = ChaseTarget.Slot(s, tree.Id);
                if (slot < 0 || s.Slots[slot].Bought >= need) continue;
                if (tree.Nodes[s.Slots[slot].Bought].Cost <= s.Sp) return new RunCommand(RunCommandKind.SpendPoint) { Slot = slot };
            }
            // No node the target needs is affordable now. Once the run is finished SP only goes down, so the target cannot
            // progress any further and nothing is held back.
            if (s.Phase == RunPhase.Finished) return FirstAffordable(c, s, 0);
            int future = Math.Max(0, c.Constants.RunCycles - s.Cycle) * c.Constants.SpPerCycle;
            int reserve = Math.Max(0, target.RemainingSp - future);
            return FirstAffordable(c, s, reserve);
        }

        RunCommand ResolveOffer(ContentDb c, RunState s)
        {
            var offer = s.Offers[0];
            if (offer.Source == OfferSource.Fusion)   // fusing frees a slot and keeps both parents: always take it
            {
                var fusion = c.Trees[offer.Trees[0]];
                return new RunCommand(RunCommandKind.TakeTree) { Choice = 0, Slot = Array.FindIndex(s.Slots, x => x != null && x.TreeId == fusion.FuseA) };
            }
            int empty = Array.IndexOf(s.Slots, null);
            if (!_chase)
            {
                if (empty >= 0) return new RunCommand(RunCommandKind.TakeTree) { Choice = 0, Slot = empty };
                if (offer.Source != OfferSource.Chase) return new RunCommand(RunCommandKind.DiscardOffer);
                return Overwrite(c, s, 0, Prereqs(c, c.Trees[offer.Trees[0]]));
            }

            // Planned: a tree the target needs, the species' own book tree or an own chase tree is "wanted" and may overwrite filler.
            var target = ChaseTarget.Of(c, s);
            var keep = target?.Needed ?? new HashSet<string>(StringComparer.Ordinal);
            int wanted = offer.Trees.FindIndex(id => keep.Contains(id));
            if (wanted < 0 && offer.Source != OfferSource.Milestone) wanted = 0;
            if (wanted < 0)
                return empty >= 0 ? new RunCommand(RunCommandKind.TakeTree) { Choice = 0, Slot = empty } : new RunCommand(RunCommandKind.DiscardOffer);
            if (empty >= 0) return new RunCommand(RunCommandKind.TakeTree) { Choice = wanted, Slot = empty };
            var protect = new HashSet<string>(keep, StringComparer.Ordinal);
            var placed = c.Trees[offer.Trees[wanted]];
            if (placed.Kind == TreeKind.Chase) protect.UnionWith(Prereqs(c, placed));
            return Overwrite(c, s, wanted, protect);
        }

        /// <summary>Take offer tree <paramref name="choice"/> over the least-invested slot that is neither a chase tree nor protected; else discard.</summary>
        static RunCommand Overwrite(ContentDb c, RunState s, int choice, HashSet<string> protect)
        {
            int best = -1;
            for (int i = 0; i < s.Slots.Length; i++)
            {
                var t = c.Trees[s.Slots[i].TreeId];
                if (t.Kind == TreeKind.Chase || protect.Contains(t.Id)) continue;
                if (best < 0 || s.Slots[i].Bought < s.Slots[best].Bought) best = i;
            }
            return best < 0 ? new RunCommand(RunCommandKind.DiscardOffer) : new RunCommand(RunCommandKind.TakeTree) { Choice = choice, Slot = best };
        }

        static HashSet<string> Prereqs(ContentDb c, TreeDef chase)
        {
            var prereqs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cd in chase.Conditions)
            {
                if (cd.Type == ConditionType.TreeMaxed) prereqs.Add(cd.Ref);
                if (cd.Type == ConditionType.NodeOwned) prereqs.Add(c.TreeNodes[cd.Ref].TreeId);
            }
            return prereqs;
        }

        public static string Lowest(ContentDb c, RunState s) => StatIds.Trained.OrderBy(id => s.Stats.Get(id)).First();

        /// <summary>The best-growth stat (species plus tree growth nodes) that is not capped yet (ties in sheet order).</summary>
        public static string BestGrowth(ContentDb c, RunState s)
        {
            var open = StatIds.Trained.Where(id => s.Stats.Get(id) < c.Constants.StatMax).ToList();
            return (open.Count > 0 ? open : StatIds.Trained.ToList()).OrderByDescending(id => TrainingRun.Growth(c, s, id)).First();
        }

        /// <summary>Drill <paramref name="stat"/> until it is capped, then fall back to the best-growth stat.</summary>
        public static Func<ContentDb, RunState, string> Mono(string stat) =>
            (c, s) => s.Stats.Get(stat) < c.Constants.StatMax ? stat : BestGrowth(c, s);
    }

    public static class RunPolicies
    {
        public static readonly string[] Names = { "balanced", "focus", "reckless", "hoarder", "chaser" };

        /// <summary>Opt-in policies, not in <see cref="Names"/>: <c>mono-&lt;stat&gt;</c> puts every drill into one stat (focus otherwise).</summary>
        public const string MonoPrefix = "mono-";

        /// <summary>Cycle from which the hoarder spends: it banks points for a late point dump (specs/12 section 5.4).</summary>
        public const int HoarderSpendFrom = 9;

        /// <summary>
        /// The first four do not plan for chase trees, so what they measure (spread training, best-growth focus, no rest, a late
        /// point dump) stays comparable. <c>chaser</c> is <c>focus</c> planning toward its species' own chase capstone (issue #111).
        /// </summary>
        public static IRunPolicy Create(string name)
        {
            if (name != null && name.StartsWith(MonoPrefix, StringComparison.Ordinal) && StatIds.IsTrained(name.Substring(MonoPrefix.Length)))
                return new ThresholdRunPolicy(name, 0.3, ThresholdRunPolicy.Mono(name.Substring(MonoPrefix.Length)));
            switch (name)
            {
                case "balanced": return new ThresholdRunPolicy(name, 0.4, ThresholdRunPolicy.Lowest);
                case "focus": return new ThresholdRunPolicy(name, 0.3, ThresholdRunPolicy.BestGrowth);
                case "reckless": return new ThresholdRunPolicy(name, -1, ThresholdRunPolicy.BestGrowth);
                case "hoarder": return new ThresholdRunPolicy(name, 0.3, ThresholdRunPolicy.BestGrowth, HoarderSpendFrom);
                case "chaser": return new ThresholdRunPolicy(name, 0.3, ThresholdRunPolicy.BestGrowth, chase: true);
                default: throw new ArgumentException($"unknown run policy '{name}' (one of {string.Join(", ", Names)}, or {MonoPrefix}<stat>)");
            }
        }
    }

    public static class RunBot
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// Play a whole run. <paramref name="log"/> gets one line per command when not null; <paramref name="observe"/> gets the
        /// state at the start and after every command (the balance report's ChaseTracker).
        /// </summary>
        public static RunState Play(ContentDb c, IRunPolicy policy, string speciesId, ulong seed, Action<string> log = null,
                                    IEnumerable<string> books = null, Action<RunState> observe = null, RunModifiers mods = null)
        {
            var s = TrainingRun.Start(c, speciesId, seed, books, mods);
            observe?.Invoke(s);
            const int guard = 100000;
            for (int i = 0; ; i++)
            {
                if (i >= guard) throw new InvalidOperationException($"run did not finish within {guard} commands");
                var cmd = policy.Next(c, s);
                if (cmd == null) break;
                int cycle = s.Cycle;
                string line;
                switch (cmd.Kind)
                {
                    case RunCommandKind.Drill:
                    {
                        var r = TrainingRun.Drill(c, s, cmd.Stat);
                        line = string.Format(Inv, "c{0,2} drill {1,-3} {2,-7} +{3,-3} -> {4,5:0} energy {5,5:0}{6}{7}", cycle, r.Stat, r.Tier, r.Gain,
                            r.NewValue, r.EnergyAfter, r.Injury == null ? "" : r.Injury.BackCycle > 0 ? $" INJURED (back in cycle {r.Injury.BackCycle})" : " INJURED (out for the rest of the run)",
                            string.Concat(r.Milestones.Select(m => $" milestone {m.Stat}>={m.Threshold}")));
                        break;
                    }
                    case RunCommandKind.Rest:
                    {
                        var r = TrainingRun.Rest(c, s);
                        line = string.Format(Inv, "c{0,2} rest  +{1:0} energy {2,5:0}", cycle, r.Restored, r.EnergyAfter);
                        break;
                    }
                    case RunCommandKind.Fight:
                    {
                        var r = TrainingRun.Fight(c, s);
                        line = string.Format(Inv, "c{0,2} fight vs {1} ({5}) {2} in {3} ticks -> rank {4}", cycle, c.FightRanks[r.RankBefore].Id,
                            r.Won ? "WON" : "lost", r.Fight.Ticks, c.FightRanks[r.RankAfter].Id, r.OpponentId ?? "flat");
                        break;
                    }
                    case RunCommandKind.TakeTree:
                    {
                        var offer = s.Offers[0];
                        var r = TrainingRun.TakeTree(c, s, cmd.Choice, cmd.Slot);
                        line = string.Format(Inv, "c{0,2} take  {1} ({2} offer: {3}) -> slot {4}{5}", cycle, r.Placed, offer.Source,
                            string.Join(" ", offer.Trees), cmd.Slot, r.Removed.Count > 0 ? " removed " + string.Join(" ", r.Removed) + " refund " + r.Refunded : "");
                        break;
                    }
                    case RunCommandKind.DiscardOffer:
                    {
                        var offer = s.Offers[0];
                        TrainingRun.DiscardOffer(c, s);
                        line = string.Format(Inv, "c{0,2} discard {1} offer: {2}", cycle, offer.Source, string.Join(" ", offer.Trees));
                        break;
                    }
                    case RunCommandKind.SpendPoint:
                    {
                        var r = TrainingRun.SpendPoint(c, s, cmd.Slot);
                        line = string.Format(Inv, "c{0,2} spend {1} ({2}) sp left {3}{4}", cycle, r.Node.Id, r.Node.Kind, s.Sp,
                            string.Concat(r.Milestones.Select(m => $" milestone {m.Stat}>={m.Threshold}")));
                        break;
                    }
                    case RunCommandKind.RerollDraft:
                    {
                        var before = string.Join(" ", s.Offers[0].Trees);
                        TrainingRun.RerollDraft(c, s);
                        line = string.Format(Inv, "c{0,2} reroll draft: {1} -> {2}", cycle, before, string.Join(" ", s.Offers[0].Trees));
                        break;
                    }
                    default:
                    {
                        TrainingRun.UseBook(c, s, cmd.Book);
                        line = string.Format(Inv, "c{0,2} book  {1}", cycle, cmd.Book);
                        break;
                    }
                }
                log?.Invoke(line);
                observe?.Invoke(s);
            }
            if (s.Phase != RunPhase.Finished) throw new InvalidOperationException("policy stopped before the run finished");
            return s;
        }

        public static string Summary(ContentDb c, RunState s) =>
            string.Format(Inv, "end={0} cycle={1} rank={2} fights={3}/{4} drills={5} rests={6} energy={7:0} milestones={8} sp={9} injuries={12}\n  stats {10}\n  trees {11}",
                s.End, s.Cycle, c.FightRanks[s.Rank].Id, s.FightsWon, s.FightsFought, s.Drills, s.Rests, s.Energy, s.Milestones.Count, s.Sp,
                string.Join(" ", StatIds.Trained.Select(id => $"{id} {s.Stats.Get(id):0}")),
                string.Join(" ", s.Slots.Select(t => t == null ? "-" : $"{t.TreeId}:{t.Bought}/{c.Trees[t.TreeId].Nodes.Count}")),
                s.Injuries.Count == 0 ? "0" : string.Join(",", s.Injuries.Select(i => $"c{i.Cycle}")));

        /// <summary>
        /// golden/training-run-vectors.txt (specs/14 section 9): every species x run policy at seed 7, summary and hash. Moves
        /// whenever the run rules, the battle or their content change.
        /// </summary>
        public static string Vectors(ContentDb c)
        {
            const ulong seed = 7;
            var sb = new StringBuilder();
            sb.Append("# training-run vectors, hash v").Append(TrainingRun.HashVersion)
              .Append(" content=").Append(TableWriter.ContentTag(c.Version, c.ContentHash)).Append('\n');
            var allBooks = c.SkillBooks.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            foreach (var sp in c.SpeciesList)
            {
                foreach (var name in RunPolicies.Names) Append(sp.Id, name, null);
                Append(sp.Id, "focus", allBooks);
            }
            return sb.ToString();

            void Append(string species, string name, List<string> books)
            {
                var s = Play(c, RunPolicies.Create(name), species, seed, null, books);
                sb.Append('\n').Append(species).Append(' ').Append(name).Append(" seed=").Append(seed);
                if (books != null) sb.Append(" books=").Append(string.Join(",", books));
                sb.Append('\n').Append("  ").Append(Summary(c, s)).Append('\n');
                sb.Append("  hash ").Append(TrainingRun.StateHash(s)).Append('\n');
            }
        }
    }
}
