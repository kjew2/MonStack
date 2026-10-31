using System.Collections.Generic;

namespace IdleRng.Sim.Combat
{
    public enum FightOutcome { Ongoing, SideAWon, SideBWon, TimedOut }

    /// <summary>
    /// A fight in progress (specs/16 section 4). Plain mutable graph, so it can be saved and cloned; Clone plus
    /// the same random draws continues identically.
    /// </summary>
    public sealed class FightState
    {
        /// <summary>Ticks stepped so far.</summary>
        public int Ticks { get; set; }

        /// <summary>False until the first Step has fired the BattleStart triggers.</summary>
        public bool Started { get; set; }

        public FightOutcome Outcome { get; set; }

        /// <summary>True when the fight runs with tower rules: the tower fields below are then hashed (specs/21 section 3).</summary>
        public bool Tower { get; set; }

        /// <summary>Highest corruption level reached so far (tower fights).</summary>
        public int PeakCorruption { get; set; }

        /// <summary>Side A in slot order, then side B in slot order. This order breaks every tie.</summary>
        public List<FightUnit> Units { get; set; } = new List<FightUnit>();

        /// <summary>Scratch list for trigger collection (not saved, not hashed).</summary>
        internal List<CombatSkill> Scratch => _scratch ??= new List<CombatSkill>();
        List<CombatSkill> _scratch;

        public FightState Clone()
        {
            var c = (FightState)MemberwiseClone();
            c._scratch = null;
            c.Units = new List<FightUnit>(Units.Count);
            foreach (var u in Units) c.Units.Add(u.Clone());
            return c;
        }
    }

    /// <summary>One combatant during a fight. Source is the fighter snapshot it was built from (shared, read-only).</summary>
    public sealed class FightUnit
    {
        public Fighter Source { get; set; }
        public string Id => Source.Id;

        /// <summary>0 = side A, 1 = side B.</summary>
        public int Side { get; set; }
        public int Slot { get; set; }

        /// <summary>Max HP with its HP effects: BaseMaxHp x max(STAT_FLOOR_FRAC, 1 + timed HP effects).</summary>
        public double MaxHp { get; set; }

        /// <summary>
        /// Max HP before timed HP effects (permanent HP passives included). Not hashed: it is MaxHp over the effect multiplier.
        /// </summary>
        public double BaseMaxHp { get; set; }
        public double Hp { get; set; }
        public double Gauge { get; set; }
        public double Shield { get; set; }
        public int ShieldTicks { get; set; }
        public int TauntTicks { get; set; }

        /// <summary>Own actions left before each skill (indexed like Source.Skills) is ready.</summary>
        public int[] Cooldowns { get; set; } = new int[0];

        public List<FightEffect> Effects { get; set; } = new List<FightEffect>();
        public UnitTally Tally { get; set; } = new UnitTally();

        public bool Alive => Hp > 0;

        // ---- tower fields (specs/21 section 3), all zero outside tower fights

        /// <summary>Entropy gauge, 0 to the cap.</summary>
        public double Entropy { get; set; }

        /// <summary>Times the gauge has vented; the vent kind rotates with it.</summary>
        public int Vents { get; set; }

        /// <summary>Index of the next boss phase to enter.</summary>
        public int PhaseNext { get; set; }

        /// <summary>An add summoned by a split phase: it never counts as keeping its side alive.</summary>
        public bool IsAdd { get; set; }

        /// <summary>Adds spawned so far (for their ids).</summary>
        public int Spawned { get; set; }

        /// <summary>Damage the unit takes less while one of its side's adds lives (split phase).</summary>
        public double Guard { get; set; }

        /// <summary>A shield_check phase is waiting for its shield to break or expire.</summary>
        public bool CheckPending { get; set; }
        public double CheckHealFrac { get; set; }
        public double CheckBuff { get; set; }

        /// <summary>A channel in progress: ticks left, damage still to deal to interrupt it, and the strike if it lands.</summary>
        public int ChannelTicks { get; set; }
        public double ChannelNeed { get; set; }
        public double ChannelStrike { get; set; }
        public double ChannelDealt { get; set; }

        /// <summary>enrage phase: damage buff added to the unit each turn.</summary>
        public double EnrageStep { get; set; }

        /// <summary>root_rebirth: the fraction of max HP it comes back with; 0 = no rebirth left (used, or no rule).</summary>
        public double RebirthHeal { get; set; }

        /// <summary>root_rebirth fired in the last Absorb and its event is not out yet.</summary>
        public bool RebornPending { get; set; }

        /// <summary>night_feast: the STR bonus (a fraction of base) the unit has gained from its hits this fight.</summary>
        public double FeastBonus { get; set; }

        public FightUnit Clone()
        {
            var c = (FightUnit)MemberwiseClone();
            c.Cooldowns = (int[])Cooldowns.Clone();
            c.Effects = new List<FightEffect>(Effects.Count);
            foreach (var e in Effects) c.Effects.Add(e.Clone());
            c.Tally = Tally.Clone();
            return c;
        }
    }

    /// <summary>A timed or permanent stat change. Frac is a fraction of base for trained stats, added directly to crit stats.</summary>
    public sealed class FightEffect
    {
        public string Stat { get; set; }
        public double Frac { get; set; }

        /// <summary>Ticks left; -1 = permanent.</summary>
        public int TicksLeft { get; set; }

        /// <summary>Skill id that applied it, so the AI does not re-apply an active effect.</summary>
        public string Source { get; set; }

        public FightEffect Clone() => (FightEffect)MemberwiseClone();
    }

    /// <summary>
    /// What a unit did during the fight. The run engine reads these for playstyle conditions such as "deal X fire damage" or
    /// "evade N attacks" (specs/12 section 5.5).
    /// </summary>
    public sealed class UnitTally
    {
        public int Actions { get; set; }
        public double DamageDealt { get; set; }
        public double DamageTaken { get; set; }
        public double HealingDone { get; set; }
        public int Crits { get; set; }

        /// <summary>Hits this unit evaded.</summary>
        public int Evades { get; set; }
        public int Kills { get; set; }
        public int Counters { get; set; }

        /// <summary>Damage dealt by element id (sorted, so hashing and printing are stable). Elementless damage is not listed.</summary>
        public SortedDictionary<string, double> DamageByElement { get; set; } = new SortedDictionary<string, double>(System.StringComparer.Ordinal);

        public UnitTally Clone()
        {
            var c = (UnitTally)MemberwiseClone();
            c.DamageByElement = new SortedDictionary<string, double>(DamageByElement, System.StringComparer.Ordinal);
            return c;
        }
    }

    /// <summary>One thing that happened, for the UI's battle log and tests. Skill is null for a basic attack or a counter.</summary>
    public readonly struct FightAction
    {
        public FightAction(string actor, CombatSkill skill, string target, double amount, bool crit, bool evaded, bool aoe, bool counter)
        {
            Actor = actor; Skill = skill; Target = target; Amount = amount; Crit = crit; Evaded = evaded; Aoe = aoe; Counter = counter;
            Event = null;
        }

        /// <summary>A rule modifier or tower event (specs/16 section 6.2, specs/21 section 6): see <see cref="Event"/>.</summary>
        public FightAction(string actor, string evt, string target, double amount)
        {
            Actor = actor; Skill = null; Target = target; Amount = amount; Crit = false; Evaded = false; Aoe = false; Counter = false;
            Event = evt;
        }

        /// <summary>A rule modifier's event: "rule:" + the rule id (specs/16 section 6.2).</summary>
        public const string RulePrefix = "rule:";
        /// <summary>tidal_overflow's regeneration: Amount is HP healed plus shield gained.</summary>
        public const string TideEvent = RulePrefix + RuleModifiers.TidalOverflow;
        /// <summary>abyssal_drain after a hit: Amount is HP healed plus shield gained.</summary>
        public const string DrainEvent = RulePrefix + RuleModifiers.AbyssalDrain;
        /// <summary>double_turn_start: the unit's extra action follows (Amount 0).</summary>
        public const string DoubleTurnEvent = RulePrefix + RuleModifiers.DoubleTurnStart;
        /// <summary>root_rebirth: the unit survived a killing blow; Amount is the HP it came back with.</summary>
        public const string RebirthEvent = RulePrefix + RuleModifiers.RootRebirth;
        /// <summary>wail_stagger after a hit: Target lost Amount of ATB gauge.</summary>
        public const string StaggerEvent = RulePrefix + RuleModifiers.WailStagger;
        /// <summary>night_feast after a hit: Amount is the unit's STR bonus now (a fraction of base).</summary>
        public const string FeastEvent = RulePrefix + RuleModifiers.NightFeast;

        /// <summary>
        /// Null for an ordinary action. Otherwise a rule modifier's event ("rule:&lt;id&gt;": <see cref="TideEvent"/>,
        /// <see cref="DrainEvent"/>, <see cref="DoubleTurnEvent"/>, <see cref="RebirthEvent"/>, <see cref="StaggerEvent"/>, <see cref="FeastEvent"/>) or a tower event: "phase:&lt;kind&gt;" (a boss entered a phase),
        /// "vent:recoil|stun|enrage|convert" (a unit's entropy gauge vented), "check_failed" (a shield_check shield expired unbroken),
        /// "channel_broken" or "channel_strike" (a channel was interrupted or landed). Actor is the unit; Amount is damage, heal or
        /// shield where there is one.
        /// </summary>
        public string Event { get; }

        public string Actor { get; }
        public CombatSkill Skill { get; }

        /// <summary>Unit id, or null when the effect covers several units.</summary>
        public string Target { get; }
        public double Amount { get; }
        public bool Crit { get; }
        public bool Evaded { get; }
        public bool Aoe { get; }
        public bool Counter { get; }
    }

    /// <summary>The end of a resolved fight. Hash covers the whole final state (BattleSim.StateHash).</summary>
    public sealed class FightResult
    {
        public FightResult(FightState final, string hash) { Final = final; Hash = hash; }

        public FightState Final { get; }
        public FightOutcome Outcome => Final.Outcome;
        public int Ticks => Final.Ticks;
        public string Hash { get; }
    }
}
