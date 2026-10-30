using System.Globalization;
using System.Text;
using IdleRng.Bots.Report;
using IdleRng.Bots.Training;
using IdleRng.Sim.Content;
using IdleRng.Sim.Gauntlet;
using IdleRng.Sim.Training;

namespace IdleRng.Bots.Golden
{
    /// <summary>
    /// golden/gauntlet-vectors.txt (specs/19 section 6): each species' monster after a `focus` bot run at seed 7, sent up the
    /// tower with its run seed. Moves when the gauntlet, the run, the battle or their content change.
    /// </summary>
    public static class GauntletVectors
    {
        public static string Generate(ContentDb c)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("# gauntlet vectors, hash v").Append(GauntletSim.HashVersion)
              .Append(" content=").Append(TableWriter.ContentTag(c.Version, c.ContentHash)).Append('\n');
            foreach (var sp in c.SpeciesList)
            {
                var run = RunBot.Play(c, RunPolicies.Create("focus"), sp.Id, 7);
                var r = GauntletSim.Run(c, TrainingRun.MonsterFighter(c, run), run.Seed);
                sb.Append(string.Format(inv, "\n{0} focus seed=7 rank={1}\n  floor={2} cleared={3} killer={4} title={5}\n  hash {6}\n",
                    sp.Id, c.FightRanks[run.Rank].Id, r.FloorReached, r.Cleared, r.KillerId ?? "-", r.TitleKey, r.Hash));
            }
            return sb.ToString();
        }
    }
}
