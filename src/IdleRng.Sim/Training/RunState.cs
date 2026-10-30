using System.Collections.Generic;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;

namespace IdleRng.Sim.Training
{
    /// <summary>Where a run is (specs/14 section 2).</summary>
    public enum RunPhase
    {
        /// <summary>Action points left: Drill or Rest.</summary>
        Training,

        /// <summary>Action points spent and a rank fight is due: Fight.</summary>
        AwaitFight,

        Finished,
    }

    public enum RunEnd { None, Completed, Injured }

    public enum RunCommandKind { Drill, Rest, Fight }

    /// <summary>One accepted command. Stat is the drilled stat id for Drill, null otherwise.</summary>
    public sealed class RunCommand
    {
        public RunCommand() { }
        public RunCommand(RunCommandKind kind, string stat = null) { Kind = kind; Stat = stat; }

        public RunCommandKind Kind { get; set; }
        public string Stat { get; set; }
    }

    /// <summary>A trained stat crossed a stat_milestones threshold (specs/14 section 6). Tree offers read these (T3).</summary>
    public sealed class MilestoneHit
    {
        public string Stat { get; set; }
        public int Threshold { get; set; }
        public int Cycle { get; set; }
    }

    /// <summary>
    /// A training run in progress (specs/14). Plain mutable graph like GameState: saveable, and replaying Log from the same
    /// species and seed rebuilds it exactly (TrainingRun.Replay).
    /// </summary>
    public sealed class RunState
    {
        public string SpeciesId { get; set; }

        /// <summary>Run seed; the training stream and every fight seed derive from it.</summary>
        public ulong Seed { get; set; }

        public MonsterStats Stats { get; set; } = new MonsterStats();
        public double Energy { get; set; }

        /// <summary>1-based cycle number.</summary>
        public int Cycle { get; set; }
        public int ApLeft { get; set; }

        /// <summary>Index into ContentDb.FightRanks.</summary>
        public int Rank { get; set; }

        public RunPhase Phase { get; set; }
        public RunEnd End { get; set; }

        /// <summary>Drill and injury rolls.</summary>
        public RngStream Training { get; set; } = new RngStream();

        public int FightsFought { get; set; }
        public int FightsWon { get; set; }
        public int Drills { get; set; }
        public int Rests { get; set; }

        public List<MilestoneHit> Milestones { get; set; } = new List<MilestoneHit>();
        public List<RunCommand> Log { get; set; } = new List<RunCommand>();

        public RunState Clone()
        {
            var c = (RunState)MemberwiseClone();
            c.Stats = Stats.Clone();
            c.Training = Training.Clone();
            c.Milestones = new List<MilestoneHit>(Milestones.Count);
            foreach (var m in Milestones) c.Milestones.Add(new MilestoneHit { Stat = m.Stat, Threshold = m.Threshold, Cycle = m.Cycle });
            c.Log = new List<RunCommand>(Log.Count);
            foreach (var l in Log) c.Log.Add(new RunCommand(l.Kind, l.Stat));
            return c;
        }
    }

    /// <summary>What a drill did, for instant UI feedback.</summary>
    public sealed class DrillResult
    {
        public string Stat { get; set; }
        public DrillTier Tier { get; set; }

        /// <summary>Points added (after the growth multiplier and the STAT_MAX cap).</summary>
        public double Gain { get; set; }
        public double NewValue { get; set; }
        public double EnergyAfter { get; set; }
        public bool Injured { get; set; }
        public List<MilestoneHit> Milestones { get; set; } = new List<MilestoneHit>();
    }

    public sealed class RestResult
    {
        public double Restored { get; set; }
        public double EnergyAfter { get; set; }
    }

    /// <summary>A rank fight: the battle plus the rank before and after.</summary>
    public sealed class RankFightResult
    {
        public int RankBefore { get; set; }
        public int RankAfter { get; set; }
        public bool Won { get; set; }
        public FightResult Fight { get; set; }
    }
}
