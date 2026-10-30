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
    /// <summary>A bot that plays a training run (specs/14 section 9): picks the next command for a run state.</summary>
    public interface IRunPolicy
    {
        string Name { get; }
        RunCommand Next(ContentDb c, RunState s);
    }

    /// <summary>Rest below a threshold energy fraction, otherwise drill the stat the pick function chooses. Always fights when due.</summary>
    public sealed class ThresholdRunPolicy : IRunPolicy
    {
        readonly double _restBelow;
        readonly Func<ContentDb, RunState, string> _pick;

        public ThresholdRunPolicy(string name, double restBelow, Func<ContentDb, RunState, string> pick)
        {
            Name = name; _restBelow = restBelow; _pick = pick;
        }

        public string Name { get; }

        public RunCommand Next(ContentDb c, RunState s)
        {
            if (s.Phase == RunPhase.AwaitFight) return new RunCommand(RunCommandKind.Fight);
            if (TrainingRun.EnergyFraction(c.Constants, s.Energy) < _restBelow) return new RunCommand(RunCommandKind.Rest);
            return new RunCommand(RunCommandKind.Drill, _pick(c, s));
        }

        public static string Lowest(ContentDb c, RunState s) => StatIds.Trained.OrderBy(id => s.Stats.Get(id)).First();

        /// <summary>The species' best-growth stat that is not capped yet (ties in sheet order).</summary>
        public static string BestGrowth(ContentDb c, RunState s)
        {
            var g = c.Species[s.SpeciesId].Growth;
            var open = StatIds.Trained.Where(id => s.Stats.Get(id) < c.Constants.StatMax).ToList();
            return (open.Count > 0 ? open : StatIds.Trained.ToList()).OrderByDescending(id => g.Get(id)).First();
        }
    }

    public static class RunPolicies
    {
        public static readonly string[] Names = { "balanced", "focus", "reckless" };

        public static IRunPolicy Create(string name)
        {
            switch (name)
            {
                case "balanced": return new ThresholdRunPolicy(name, 0.4, ThresholdRunPolicy.Lowest);
                case "focus": return new ThresholdRunPolicy(name, 0.3, ThresholdRunPolicy.BestGrowth);
                case "reckless": return new ThresholdRunPolicy(name, -1, ThresholdRunPolicy.BestGrowth);
                default: throw new ArgumentException($"unknown run policy '{name}' (one of {string.Join(", ", Names)})");
            }
        }
    }

    public static class RunBot
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Play a whole run. <paramref name="log"/> gets one line per command when not null.</summary>
        public static RunState Play(ContentDb c, IRunPolicy policy, string speciesId, ulong seed, Action<string> log = null)
        {
            var s = TrainingRun.Start(c, speciesId, seed);
            int guard = c.Constants.RunCycles * (c.Constants.RunAp + 1) + 1;
            for (int i = 0; i < guard && s.Phase != RunPhase.Finished; i++)
            {
                var cmd = policy.Next(c, s);
                string line;
                switch (cmd.Kind)
                {
                    case RunCommandKind.Drill:
                    {
                        int cycle = s.Cycle;
                        var r = TrainingRun.Drill(c, s, cmd.Stat);
                        line = string.Format(Inv, "c{0,2} drill {1,-3} {2,-7} +{3,-3} -> {4,5:0} energy {5,5:0}{6}{7}", cycle, r.Stat, r.Tier, r.Gain,
                            r.NewValue, r.EnergyAfter, r.Injured ? " INJURED" : "",
                            string.Concat(r.Milestones.Select(m => $" milestone {m.Stat}>={m.Threshold}")));
                        break;
                    }
                    case RunCommandKind.Rest:
                    {
                        int cycle = s.Cycle;
                        var r = TrainingRun.Rest(c, s);
                        line = string.Format(Inv, "c{0,2} rest  +{1:0} energy {2,5:0}", cycle, r.Restored, r.EnergyAfter);
                        break;
                    }
                    default:
                    {
                        int cycle = s.Cycle;
                        var r = TrainingRun.Fight(c, s);
                        line = string.Format(Inv, "c{0,2} fight vs {1} {2} in {3} ticks -> rank {4}", cycle, c.FightRanks[r.RankBefore].Id,
                            r.Won ? "WON" : "lost", r.Fight.Ticks, c.FightRanks[r.RankAfter].Id);
                        break;
                    }
                }
                log?.Invoke(line);
            }
            if (s.Phase != RunPhase.Finished) throw new InvalidOperationException($"run did not finish within {guard} commands");
            return s;
        }

        public static string Summary(ContentDb c, RunState s) =>
            string.Format(Inv, "end={0} cycle={1} rank={2} fights={3}/{4} drills={5} rests={6} energy={7:0} milestones={8}\n  stats {9}",
                s.End, s.Cycle, c.FightRanks[s.Rank].Id, s.FightsWon, s.FightsFought, s.Drills, s.Rests, s.Energy, s.Milestones.Count,
                string.Join(" ", StatIds.Trained.Select(id => $"{id} {s.Stats.Get(id):0}")));

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
            foreach (var sp in c.SpeciesList)
                foreach (var name in RunPolicies.Names)
                {
                    var s = Play(c, RunPolicies.Create(name), sp.Id, seed);
                    sb.Append('\n').Append(sp.Id).Append(' ').Append(name).Append(" seed=").Append(seed).Append('\n');
                    sb.Append("  ").Append(Summary(c, s)).Append('\n');
                    sb.Append("  hash ").Append(TrainingRun.StateHash(s)).Append('\n');
                }
            return sb.ToString();
        }
    }
}
