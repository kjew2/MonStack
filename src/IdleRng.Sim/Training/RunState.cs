using System.Collections.Generic;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;

namespace IdleRng.Sim.Training
{
    /// <summary>Where a run is (specs/14 section 2). Tree commands (specs/15) work in every phase.</summary>
    public enum RunPhase
    {
        /// <summary>Action points left: Drill or Rest.</summary>
        Training,

        /// <summary>Action points spent and a rank fight is due: Fight.</summary>
        AwaitFight,

        Finished,
    }

    public enum RunEnd { None, Completed, Injured }

    public enum RunCommandKind { Drill, Rest, Fight, TakeTree, DiscardOffer, SpendPoint, UseBook }

    /// <summary>One accepted command, with only the arguments its kind uses.</summary>
    public sealed class RunCommand
    {
        public RunCommand() { }
        public RunCommand(RunCommandKind kind, string stat = null) { Kind = kind; Stat = stat; }

        public RunCommandKind Kind { get; set; }

        /// <summary>Drill: the stat.</summary>
        public string Stat { get; set; }

        /// <summary>TakeTree: index into the offer's trees.</summary>
        public int Choice { get; set; }

        /// <summary>TakeTree, SpendPoint: tree slot.</summary>
        public int Slot { get; set; }

        /// <summary>UseBook: book id.</summary>
        public string Book { get; set; }

        public RunCommand Clone() => (RunCommand)MemberwiseClone();
    }

    /// <summary>
    /// Account bonuses a run starts with (specs/18 section 3): the talent tree's effects, captured at Start so a replay with the
    /// same modifiers rebuilds the run exactly. The defaults change nothing.
    /// </summary>
    public sealed class RunModifiers
    {
        /// <summary>Multiplies DRILL_ENERGY_COST. 1 = no change.</summary>
        public double DrillCostMult { get; set; } = 1;

        /// <summary>Multiplies the species' base stats at the start (capped at STAT_MAX). 1 = no change.</summary>
        public double StatFloorMult { get; set; } = 1;

        /// <summary>Added to DRAFT_SIZE for milestone drafts.</summary>
        public int DraftSizeBonus { get; set; }

        /// <summary>Extra skill points at the start of the run.</summary>
        public int StartSp { get; set; }

        public RunModifiers Clone() => (RunModifiers)MemberwiseClone();
    }

    /// <summary>A trained stat crossed a stat_milestones threshold (specs/14 section 6). Each pair is recorded once.</summary>
    public sealed class MilestoneHit
    {
        public string Stat { get; set; }
        public int Threshold { get; set; }
        public int Cycle { get; set; }
    }

    /// <summary>A tree in one of the monster's slots and how many of its nodes are bought (in order).</summary>
    public sealed class TreeSlot
    {
        public string TreeId { get; set; }
        public int Bought { get; set; }

        /// <summary>Flat points this tree's Stat nodes actually added, per trained stat (StatIds.Trained order); removed with the tree.</summary>
        public double[] FlatAdded { get; set; } = new double[StatIds.Trained.Length];

        public TreeSlot Clone()
        {
            var c = (TreeSlot)MemberwiseClone();
            c.FlatAdded = (double[])FlatAdded.Clone();
            return c;
        }
    }

    public enum OfferSource { Milestone, Book, Chase }

    /// <summary>
    /// A pending tree offer (specs/15 section 4): pick one tree into a slot, or discard. Milestone drafts hold up to DRAFT_SIZE
    /// trees; book and chase offers hold one.
    /// </summary>
    public sealed class TreeOffer
    {
        public OfferSource Source { get; set; }
        public List<string> Trees { get; set; } = new List<string>();

        /// <summary>Milestone drafts: the stat and threshold that caused it.</summary>
        public string Stat { get; set; }
        public int Threshold { get; set; }

        public TreeOffer Clone()
        {
            var c = (TreeOffer)MemberwiseClone();
            c.Trees = new List<string>(Trees);
            return c;
        }
    }

    /// <summary>What the monster did across the run's rank fights; chase conditions read these (specs/15 section 5).</summary>
    public sealed class RunTally
    {
        public double DamageDealt { get; set; }
        public int Evades { get; set; }
        public int Crits { get; set; }
        public int Kills { get; set; }
        public SortedDictionary<string, double> DamageByElement { get; set; } = new SortedDictionary<string, double>(System.StringComparer.Ordinal);

        public RunTally Clone()
        {
            var c = (RunTally)MemberwiseClone();
            c.DamageByElement = new SortedDictionary<string, double>(DamageByElement, System.StringComparer.Ordinal);
            return c;
        }
    }

    /// <summary>
    /// A training run in progress (specs/14, specs/15). Plain mutable graph: saveable, and replaying Log from
    /// the same species, seed and books rebuilds it exactly (TrainingRun.Replay).
    /// </summary>
    public sealed class RunState
    {
        public string SpeciesId { get; set; }

        /// <summary>Account bonuses captured at Start (specs/18). Never null.</summary>
        public RunModifiers Mods { get; set; } = new RunModifiers();

        /// <summary>Run seed; the training and draft streams and every fight seed derive from it.</summary>
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

        /// <summary>Milestone draft picks.</summary>
        public RngStream Draft { get; set; } = new RngStream();

        public int FightsFought { get; set; }
        public int FightsWon { get; set; }
        public int Drills { get; set; }
        public int Rests { get; set; }

        /// <summary>Unspent skill points.</summary>
        public int Sp { get; set; }

        /// <summary>Length TREE_SLOTS; null = empty.</summary>
        public TreeSlot[] Slots { get; set; } = new TreeSlot[0];

        /// <summary>Pending offers, oldest first. Drill, Rest and Fight wait until this is empty.</summary>
        public List<TreeOffer> Offers { get; set; } = new List<TreeOffer>();

        /// <summary>Skill books the player brought into the run, not yet used.</summary>
        public List<string> Books { get; set; } = new List<string>();

        /// <summary>Chase trees offered since they last unlocked; cleared for a tree when its conditions stop holding.</summary>
        public List<string> ChaseOffered { get; set; } = new List<string>();

        public RunTally Tally { get; set; } = new RunTally();

        public List<MilestoneHit> Milestones { get; set; } = new List<MilestoneHit>();
        public List<RunCommand> Log { get; set; } = new List<RunCommand>();

        public RunState Clone()
        {
            var c = (RunState)MemberwiseClone();
            c.Stats = Stats.Clone();
            c.Mods = Mods.Clone();
            c.Training = Training.Clone();
            c.Draft = Draft.Clone();
            c.Slots = new TreeSlot[Slots.Length];
            for (int i = 0; i < Slots.Length; i++) c.Slots[i] = Slots[i]?.Clone();
            c.Offers = Offers.ConvertAll(o => o.Clone());
            c.Books = new List<string>(Books);
            c.ChaseOffered = new List<string>(ChaseOffered);
            c.Tally = Tally.Clone();
            c.Milestones = Milestones.ConvertAll(m => new MilestoneHit { Stat = m.Stat, Threshold = m.Threshold, Cycle = m.Cycle });
            c.Log = Log.ConvertAll(l => l.Clone());
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
        public int SpGained { get; set; }
        public FightResult Fight { get; set; }
    }

    /// <summary>A bought node and its immediate effect.</summary>
    public sealed class SpendResult
    {
        public TreeNodeDef Node { get; set; }

        /// <summary>Flat points a Stat node added (after the cap); 0 for other kinds.</summary>
        public double StatGain { get; set; }
        public List<MilestoneHit> Milestones { get; set; } = new List<MilestoneHit>();
    }

    /// <summary>A tree placed from an offer, and every tree that left a slot because of it (overwritten, or a chase tree that locked again).</summary>
    public sealed class TakeResult
    {
        public string Placed { get; set; }
        public List<string> Removed { get; set; } = new List<string>();
    }
}
