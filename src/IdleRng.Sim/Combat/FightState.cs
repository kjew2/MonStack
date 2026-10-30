using System.Collections.Generic;

namespace IdleRng.Sim.Combat
{
    public enum FightOutcome { Ongoing, SideAWon, SideBWon, TimedOut }

    /// <summary>
    /// A fight in progress (specs/16 section 4). Plain mutable graph like GameState, so it can be saved and cloned; Clone plus
    /// the same random draws continues identically.
    /// </summary>
    public sealed class FightState
    {
        /// <summary>Ticks stepped so far.</summary>
        public int Ticks { get; set; }

        /// <summary>False until the first Step has fired the BattleStart triggers.</summary>
        public bool Started { get; set; }

        public FightOutcome Outcome { get; set; }

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

        public double MaxHp { get; set; }
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
        }

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
