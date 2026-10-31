using System.Collections.Generic;
using IdleRng.Sim.Training;

namespace IdleRng.Bots.Training
{
    /// <summary>One rank fight of a run: the pool monster fought (null in a legacy run) and whether the monster won.</summary>
    public sealed class FightRecord
    {
        public string Opponent { get; set; }
        public int Rank { get; set; }
        public bool Won { get; set; }
    }

    /// <summary>
    /// Watches a run for the balance report's opponent table (specs/09 section 7): each rank fight and the ladder's monster it
    /// was against. Feed it every state of the run (<see cref="RunBot.Play"/>'s observer): a fight shows as FightsFought going up,
    /// fought at the rank the state had before it.
    /// </summary>
    public sealed class FightTracker
    {
        int _fought, _won, _rank;

        public List<FightRecord> Fights { get; } = new List<FightRecord>();

        public void Observe(RunState s)
        {
            if (s.FightsFought > _fought)
                Fights.Add(new FightRecord
                {
                    Rank = _rank, Opponent = s.Ladder.Count == 0 ? null : s.Ladder[_rank], Won = s.FightsWon > _won,
                });
            _fought = s.FightsFought;
            _won = s.FightsWon;
            _rank = s.Rank;
        }
    }
}
