using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using IdleRng.Sim.Content;
using IdleRng.Sim.Gauntlet;
using IdleRng.Sim.Training;

namespace IdleRng.Bots.Training
{
    /// <summary>
    /// Where bot monsters stall in the gauntlet (specs/19 section 6, issue #99): every species x policy x seed plays a run,
    /// climbs the tower, and the report gives the floor spread, the share of stalls on each floor digit (floor mod 10) and
    /// the share on boss floors. A first piece of the run-balance harness (specs/09 section 7).
    /// </summary>
    public static class GauntletSpread
    {
        public sealed class Result
        {
            public List<int> Floors { get; } = new List<int>();
            public int Cleared { get; set; }
            public int OnBoss { get; set; }
            public int[] ByDigit { get; } = new int[10];
        }

        public static Result Collect(ContentDb c, IEnumerable<string> policies, int seeds)
        {
            var r = new Result();
            foreach (var sp in c.SpeciesList)
                foreach (var name in policies)
                    for (int i = 1; i <= seeds; i++)
                    {
                        var run = RunBot.Play(c, RunPolicies.Create(name), sp.Id, (ulong)i);
                        var g = GauntletSim.Run(c, TrainingRun.MonsterFighter(c, run), run.Seed);
                        r.Floors.Add(g.FloorReached);
                        if (g.Cleared) { r.Cleared++; continue; }
                        r.ByDigit[g.FloorReached % 10]++;
                        if (GauntletSim.IsBoss(c, g.FloorReached)) r.OnBoss++;
                    }
            return r;
        }

        public static string Report(Result r)
        {
            var inv = CultureInfo.InvariantCulture;
            var sorted = r.Floors.OrderBy(f => f).ToList();
            int n = sorted.Count, stalls = n - r.Cleared;
            int Pct(double p) => sorted[(int)((n - 1) * p)];
            string Share(int k) => stalls == 0 ? "-" : (100.0 * k / stalls).ToString("0", inv) + "%";
            var sb = new StringBuilder();
            sb.Append(string.Format(inv, "monsters {0}  cleared {1}  floor min {2} p25 {3} median {4} p75 {5} max {6}\n",
                n, r.Cleared, sorted[0], Pct(0.25), Pct(0.5), Pct(0.75), sorted[n - 1]));
            sb.Append("stalls on boss floors ").Append(Share(r.OnBoss)).Append('\n');
            sb.Append("stalls by floor mod 10:");
            for (int d = 0; d < 10; d++) sb.Append(' ').Append(d).Append('=').Append(Share(r.ByDigit[d]));
            sb.Append('\n');
            return sb.ToString();
        }
    }
}
