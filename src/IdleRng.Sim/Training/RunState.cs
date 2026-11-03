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

    /// <summary>
    /// Why a run finished. <c>Injured</c> is kept only for Vault entries sealed before issue #89 (2026-10-31): an injury used
    /// to end the run. New runs always end <c>Completed</c>; an injury now costs cycles instead (specs/14 section 4.1).
    /// </summary>
    public enum RunEnd { None, Completed, Injured }

    public enum RunCommandKind { Drill, Rest, Fight, TakeTree, DiscardOffer, SpendPoint, UseBook, RerollDraft }

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

        /// <summary>
        /// Legacy, for replay only: the odds a run was attuned to its in-run chase tree, from when attunement existed (removed by
        /// kj, 2026-10-31; specs/15 section 5.4). Every new run has 1 (always attuned); a saved recipe from before keeps its odds
        /// so its log replays as it was played. No talent or constant sets it any more.
        /// </summary>
        public double ChaseChance { get; set; } = 1;

        /// <summary>Extra skill points at the start of the run.</summary>
        public int StartSp { get; set; }

        /// <summary>Added to REST_ENERGY (Deep Rest).</summary>
        public double RestEnergyBonus { get; set; }

        /// <summary>Added to the drill fail chance, never below 0 (Steady Hands; negative).</summary>
        public double FailChanceDelta { get; set; }

        /// <summary>Added to DRILL_SUPER_P (Super Set).</summary>
        public double SuperChanceBonus { get; set; }

        /// <summary>Multiplies the run's shard payout, rounded down (Scavenger). 1 = no change.</summary>
        public double ShardMult { get; set; } = 1;

        /// <summary>After a Super drill, this many following drills cost no energy (Momentum).</summary>
        public int MomentumDrills { get; set; }

        /// <summary>Skill points a lost rank fight pays (Grudge Match).</summary>
        public int LossSp { get; set; }

        /// <summary>
        /// Drills on the run's highest trained stat (ties all count) grow x(1 + this); every other stat grows x(1 - this / 2)
        /// (Specialist). 0 = off.
        /// </summary>
        public double SpecialistBonus { get; set; }

        /// <summary>Added to TREE_SLOTS (Fifth Slot).</summary>
        public int TreeSlotBonus { get; set; }

        /// <summary>Injuries prevented per run: the drill leaves the monster at 0 energy instead (Second Wind).</summary>
        public int InjuryShields { get; set; }

        /// <summary>Extra action points in the last cycle (Overtime).</summary>
        public int LastCycleAp { get; set; }

        /// <summary>Multiplies the stat milestone thresholds a stat must reach, rounded (Early Bloom). 1 = no change.</summary>
        public double MilestoneMult { get; set; } = 1;

        /// <summary>Milestone drafts the run may reroll (Second Look).</summary>
        public int DraftRerolls { get; set; }

        /// <summary>
        /// Legacy, for replay only: the run was started before rolled Awakening trees (kj, 2026-10-31, issue #121; specs/15
        /// section 4.1), so its Awakenings offer the fixed generic trees of the stat that crossed the milestone, as it was played.
        /// Set by the save migration to schema v8 on every saved run and Vault entry; never set for a new run.
        /// </summary>
        public bool LegacyDrafts { get; set; }

        // Legendary rule changes (specs/18 section 3, issue #103). Hashed only when a run uses one (run hash version 8).

        /// <summary>Drills never injure (Iron Constitution). The injury roll is still drawn, so the stream stays aligned.</summary>
        public bool InjuryImmune { get; set; }

        /// <summary>Multiplies what a rest restores, REST_ENERGY plus Deep Rest (Iron Constitution's drawback). 1 = no change.</summary>
        public double RestEnergyMult { get; set; } = 1;

        /// <summary>Drills never Fail: the fail chance is 0 (Gambler's Blood; its drawback multiplies DrillCostMult).</summary>
        public bool NeverFail { get; set; }

        /// <summary>Drill growth x(1 + this) on every trained stat but HP, and 0 on HP (Glass Cannon). 0 = off.</summary>
        public double GlassCannonBonus { get; set; }

        /// <summary>Each drill also adds this fraction of its gain (floored) to a random other trained stat (Echo Training). 0 = off.</summary>
        public double EchoFrac { get; set; }

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

        /// <summary>
        /// A fusion tree's two parents, as they were when they fused (specs/15 section 5.6): their bought nodes still count for the
        /// fighter and for chase conditions, and their flat stats stay. Empty for every other tree.
        /// </summary>
        public List<TreeSlot> Absorbed { get; set; } = new List<TreeSlot>();

        /// <summary>
        /// A rolled Awakening tree's rolls (specs/15 section 4.1): its filler nodes' stats and amounts and its capstone growth.
        /// Null for every other tree, which uses its content nodes as they are. Never changed once rolled, so clones share it.
        /// </summary>
        public TreeRoll Roll { get; set; }

        public TreeSlot Clone()
        {
            var c = (TreeSlot)MemberwiseClone();
            c.FlatAdded = (double[])FlatAdded.Clone();
            c.Absorbed = Absorbed.ConvertAll(a => a.Clone());
            return c;
        }
    }

    /// <summary>
    /// A rolled Awakening tree (kj, 2026-10-31, issue #121; specs/15 section 4.1): a premade generic tree whose nodes, stats and
    /// costs stay as in content; each filler node's amount, and a growth bonus its capstone adds to the tree's own stat, are
    /// rolled when the tree is offered. Never changed once rolled.
    /// </summary>
    public sealed class TreeRoll
    {
        /// <summary>The tree's own stat: its offer_stat, or for an any-stat tree the stat of its first filler node.</summary>
        public string Stat { get; set; }

        /// <summary>Per node index: a filler node's stat (its content stat); null for a skill, trigger or rule node.</summary>
        public string[] NodeStats { get; set; }

        /// <summary>Per node index: a filler node's rolled amount (growth, or whole flat points); 0 for the others.</summary>
        public double[] Amounts { get; set; }

        /// <summary>Growth the bought capstone adds to <see cref="Stat"/>.</summary>
        public double CapGrowth { get; set; }

        /// <summary>The nodes with the rolls applied, built once (TrainingRun.Nodes). Not saved.</summary>
        internal IReadOnlyList<TreeNodeDef> Built;
    }

    public enum OfferSource { Milestone, Book, Chase, Fusion }

    /// <summary>
    /// A pending tree offer (specs/15 section 4): pick one tree into a slot, or discard. Milestone drafts hold up to DRAFT_SIZE
    /// trees; book and chase offers hold one.
    /// </summary>
    public sealed class TreeOffer
    {
        public OfferSource Source { get; set; }
        public List<string> Trees { get; set; } = new List<string>();

        /// <summary>
        /// Milestone drafts of a run with rolled trees: each tree's rolls, in the order of <see cref="Trees"/>. Empty for every
        /// other offer (and a legacy run's drafts).
        /// </summary>
        public List<TreeRoll> Rolls { get; set; } = new List<TreeRoll>();

        /// <summary>Milestone drafts: the stat and threshold that caused it.</summary>
        public string Stat { get; set; }
        public int Threshold { get; set; }

        /// <summary>The rolls of tree <paramref name="choice"/>, or null when the offer has none.</summary>
        public TreeRoll RollOf(int choice) => choice >= 0 && choice < Rolls.Count ? Rolls[choice] : null;

        public TreeOffer Clone()
        {
            var c = (TreeOffer)MemberwiseClone();
            c.Trees = new List<string>(Trees);
            c.Rolls = new List<TreeRoll>(Rolls);
            return c;
        }
    }

    /// <summary>
    /// An injury (specs/14 section 4.1): the drill in <see cref="Cycle"/> injured the monster, the rest of that cycle and its
    /// fight were lost, and so were the next INJURY_SKIP_CYCLES whole cycles. <see cref="BackCycle"/> is the cycle the monster
    /// trains again (0 when the lost cycles ran past the last one and the run finished).
    /// </summary>
    public sealed class InjuryHit
    {
        public int Cycle { get; set; }
        public int BackCycle { get; set; }

        /// <summary>Whole cycles sat out after <see cref="Cycle"/>: INJURY_SKIP_CYCLES, or fewer when the run ran out of cycles.</summary>
        public int CyclesLost { get; set; }

        public InjuryHit Clone() => (InjuryHit)MemberwiseClone();
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

        /// <summary>
        /// The rank ladder (specs/14 section 5): the rank_enemies id fought at each rank, indexed like ContentDb.FightRanks, rolled
        /// at Start on the "ladder" stream. Empty for a legacy run (started before the pools, content 4.3): its opponent at each
        /// rank is the flat fight_ranks row.
        /// </summary>
        public List<string> Ladder { get; set; } = new List<string>();

        public RunPhase Phase { get; set; }
        public RunEnd End { get; set; }

        /// <summary>Drill and injury rolls.</summary>
        public RngStream Training { get; set; } = new RngStream();

        /// <summary>Whether the run is attuned to its in-run chase tree: always true, except a legacy run replayed with ChaseChance below 1.</summary>
        public bool ChaseAttuned { get; set; }

        /// <summary>Awakening (milestone tree offer) picks.</summary>
        public RngStream Draft { get; set; } = new RngStream();

        public int FightsFought { get; set; }
        public int FightsWon { get; set; }
        public int Drills { get; set; }
        public int Rests { get; set; }

        /// <summary>Unspent skill points.</summary>
        public int Sp { get; set; }

        /// <summary>Drills left that cost no energy (Momentum, specs/18 section 3).</summary>
        public int FreeDrills { get; set; }

        /// <summary>Injuries prevented so far (Second Wind; at most Mods.InjuryShields).</summary>
        public int ShieldsUsed { get; set; }

        /// <summary>Milestone drafts rerolled so far (Second Look; at most Mods.DraftRerolls).</summary>
        public int RerollsUsed { get; set; }

        /// <summary>hungry_core (lane G, #140): the core went hungry at this cycle's start, so it gives no bonus in the cycle's fight.</summary>
        public bool CoreDormant { get; set; }

        /// <summary>Length TREE_SLOTS; null = empty.</summary>
        public TreeSlot[] Slots { get; set; } = new TreeSlot[0];

        /// <summary>Pending offers, oldest first. Drill, Rest and Fight wait until this is empty.</summary>
        public List<TreeOffer> Offers { get; set; } = new List<TreeOffer>();

        /// <summary>Skill books the player brought into the run, not yet used.</summary>
        public List<string> Books { get; set; } = new List<string>();

        /// <summary>
        /// Skill books used in this run, in use order (UseBook moves them here from Books). The saved run is its state, not its
        /// log (specs/27 section 3.5), so this is how the sealed monster knows every book it brought. Not part of StateHash.
        /// </summary>
        public List<string> BooksUsed { get; set; } = new List<string>();

        /// <summary>Chase trees offered since they last unlocked; cleared for a tree when its conditions stop holding.</summary>
        public List<string> ChaseOffered { get; set; } = new List<string>();

        /// <summary>Fusion trees offered while their parents stay ready (specs/15 section 5.6); a discarded one is not offered again until then.</summary>
        public List<string> FusionOffered { get; set; } = new List<string>();

        public RunTally Tally { get; set; } = new RunTally();

        public List<MilestoneHit> Milestones { get; set; } = new List<MilestoneHit>();

        /// <summary>Every injury this run, oldest first (specs/14 section 4.1).</summary>
        public List<InjuryHit> Injuries { get; set; } = new List<InjuryHit>();
        public List<RunCommand> Log { get; set; } = new List<RunCommand>();

        public RunState Clone()
        {
            var c = (RunState)MemberwiseClone();
            c.Stats = Stats.Clone();
            c.Mods = Mods.Clone();
            c.Ladder = new List<string>(Ladder);
            c.Training = Training.Clone();
            c.Draft = Draft.Clone();
            c.Slots = new TreeSlot[Slots.Length];
            for (int i = 0; i < Slots.Length; i++) c.Slots[i] = Slots[i]?.Clone();
            c.Offers = Offers.ConvertAll(o => o.Clone());
            c.Books = new List<string>(Books);
            c.BooksUsed = new List<string>(BooksUsed);
            c.ChaseOffered = new List<string>(ChaseOffered);
            c.FusionOffered = new List<string>(FusionOffered);
            c.Tally = Tally.Clone();
            c.Milestones = Milestones.ConvertAll(m => new MilestoneHit { Stat = m.Stat, Threshold = m.Threshold, Cycle = m.Cycle });
            c.Injuries = Injuries.ConvertAll(i => i.Clone());
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

        /// <summary>The drill rolled an injury that Second Wind prevented: no injury, energy left at 0.</summary>
        public bool Shielded { get; set; }

        /// <summary>The drill cost no energy (Momentum).</summary>
        public bool Free { get; set; }

        /// <summary>Echo Training: the other stat the drill echoed into and the points it added there (null and 0 without it).</summary>
        public string EchoStat { get; set; }
        public double EchoGain { get; set; }

        /// <summary>second_breath: the energy a Super drill gave back (0 otherwise).</summary>
        public double Refunded { get; set; }

        /// <summary>The injury this drill caused (also the last entry of RunState.Injuries); null when not injured.</summary>
        public InjuryHit Injury { get; set; }
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

        /// <summary>The rank_enemies id fought (the ladder's entry at RankBefore); null in a legacy run (flat fight_ranks opponent).</summary>
        public string OpponentId { get; set; }
        public bool Won { get; set; }
        public int SpGained { get; set; }
        public FightResult Fight { get; set; }

        /// <summary>devourer: the stat a win fed on and the points it added (null and 0 otherwise).</summary>
        public string DevouredStat { get; set; }
        public double DevouredGain { get; set; }

        /// <summary>Milestones the fight's result crossed (devourer's gain); usually empty.</summary>
        public List<MilestoneHit> Milestones { get; set; } = new List<MilestoneHit>();
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

        /// <summary>SP refunded by the trees that left (TREE_REFUND of what was spent in each, rounded down).</summary>
        public int Refunded { get; set; }
    }
}
