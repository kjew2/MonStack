using System.Collections.Generic;
using System.Globalization;
using System.Text;
using IdleRng.Bots.Report;
using IdleRng.Bots.Training;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Tower;
using IdleRng.Sim.Training;

namespace IdleRng.Bots.Golden
{
    /// <summary>
    /// golden/tower-vectors.txt (specs/21 section 9): each species' monster after a `focus` bot run at seed 7, alone and all of
    /// them as one team, sent up the Daily Tower on two fixed days. Moves when the tower, the battle, the run or their content change.
    /// </summary>
    public static class TowerVectors
    {
        static readonly int[] Days = { 0, 20391 };

        public static string Generate(ContentDb c)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("# tower vectors, hash v").Append(TowerSim.HashVersion)
              .Append(" content=").Append(TableWriter.ContentTag(c.Version, c.ContentHash)).Append('\n');
            var monsters = new List<(string id, Fighter f)>();
            foreach (var sp in c.SpeciesList)
                monsters.Add((sp.Id, TrainingRun.MonsterFighter(c, RunBot.Play(c, RunPolicies.Create("focus"), sp.Id, 7))));
            var teams = new List<(string name, Fighter[] team)>();
            foreach (var m in monsters) teams.Add((m.id, new[] { m.f }));
            var all = new List<Fighter>();
            var names = new List<string>();
            foreach (var m in monsters) { if (all.Count < c.Constants.MonSideSize) { all.Add(m.f); names.Add(m.id); } }
            teams.Add((string.Join("+", names), all.ToArray()));

            foreach (int day in Days)
            {
                sb.Append(string.Format(inv, "\nday {0} lineup", day));
                foreach (var p in TowerSim.Lineup(c, day)) sb.Append(' ').Append(p.BossId).Append(p.Marquee ? "*" : "");
                sb.Append('\n');
                foreach (var t in teams)
                {
                    var r = TowerSim.Run(c, t.team, day);
                    int vents = 0;
                    var phases = new List<string>();
                    foreach (var f in r.Floors) { vents += f.Vents; foreach (var p in f.Phases) phases.Add(f.Floor + ":" + p); }
                    sb.Append(string.Format(inv, "  {0} focus seed=7: floor={1} cleared={2} vents={3} phases={4}\n    hash {5}\n",
                        t.name, r.FloorReached, r.Cleared, vents, phases.Count == 0 ? "-" : string.Join(",", phases), r.Hash));
                }
            }
            return sb.ToString();
        }
    }
}
