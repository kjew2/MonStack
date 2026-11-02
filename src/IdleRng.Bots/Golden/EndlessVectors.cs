using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    /// golden/endless-vectors.txt (specs/28 section 8): the first MON_SIDE_SIZE species' monsters after a `focus` bot run at
    /// seed 7 climb the Endless Tower in two fixed seasons, once on the default AI and once with a rulebook on every monster.
    /// Each season prints its first two bands and first mutators, then each climb's floor, what stopped it and its hash.
    /// Moves when the climb, the battle, the run or their content change.
    /// </summary>
    public static class EndlessVectors
    {
        static readonly int[] Seasons = { 0, 2913 };

        public static string Generate(ContentDb c)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("# endless vectors, hash v").Append(EndlessSim.HashVersion)
              .Append(" content=").Append(TableWriter.ContentTag(c.Version, c.ContentHash)).Append('\n');
            var species = c.SpeciesList.Take(c.Constants.MonSideSize).Select(s => s.Id).ToList();
            var team = species.Select(sp => TrainingRun.MonsterFighter(c, RunBot.Play(c, RunPolicies.Create("focus"), sp, 7))).ToList();
            var ruled = team.Select(Ruled).ToList();

            foreach (int season in Seasons)
            {
                sb.Append(string.Format(inv, "\nseason {0}\n", season));
                foreach (var p in EndlessSim.Lineup(c, season, 2 * c.Constants.EndlessBand))
                    sb.Append(string.Format(inv, "  floor {0,2} {1,-8} {2}\n", p.Floor, p.Elite ? "elite" : p.ShapeId, string.Join(",", p.EnemyIds)));
                sb.Append("  mutators ").Append(string.Join(",", EndlessSim.SeasonMutators(c, season, 5).Select(m => m.Id))).Append('\n');
                foreach (var (name, t) in new[] { ("default AI", team), ("rulebooks", ruled) })
                {
                    var r = EndlessSim.Run(c, t, season);
                    var last = r.Floors[r.Floors.Count - 1];
                    int elites = r.Floors.Count(f => f.Elite && f.Won), vents = r.Floors.Sum(f => f.Vents);
                    sb.Append(string.Format(inv, "  {0} ({1}) focus seed=7: floor={2} capped={3} stopped={4} elites={5} vents={6}\n    hash {7}\n",
                        string.Join("+", species), name, r.FloorReached, r.Capped, r.Capped ? "-" : last.ShapeId, elites, vents, r.Hash));
                }
            }
            return sb.ToString();
        }

        /// <summary>The monster with a simple rulebook: its first damage skill aimed at the weakest enemy, from three enemies up the AoE one.</summary>
        static Fighter Ruled(Fighter f)
        {
            var copy = f.WithId(f.Id);
            copy.Gambits = new List<GambitRule>();
            var aoe = f.Skills.FirstOrDefault(s => s.Kind == SkillKind.Damage && s.Target == SkillTarget.AllEnemies);
            var single = f.Skills.FirstOrDefault(s => s.Kind == SkillKind.Damage && s.Target == SkillTarget.Enemy);
            if (aoe != null) copy.Gambits.Add(new GambitRule(GambitConditions.EnemiesAtLeast, 3, aoe.Id));
            if (single != null) copy.Gambits.Add(new GambitRule(GambitConditions.Always, 0, single.Id, GambitTargets.LowestHp));
            copy.Gambits.Add(new GambitRule(GambitConditions.Always, 0, GambitRule.Basic, GambitTargets.LowestHp));
            return copy;
        }
    }
}
