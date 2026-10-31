using System;
using System.Collections.Generic;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Combat
{
    /// <summary>Stat ids used by monster stat sheets, skills and effects (specs/16 section 3).</summary>
    public static class StatIds
    {
        public const string Hp = "hp", Str = "str", Int = "int", Wis = "wis", Dex = "dex", Def = "def", Spd = "spd", Luk = "luk";

        /// <summary>Bonus-only stats: effects add to them directly (crit chance, crit damage multiplier).</summary>
        public const string CritRate = "crit_rate", CritDmg = "crit_dmg";

        /// <summary>The eight trained stats, in sheet order.</summary>
        public static readonly string[] Trained = { Hp, Str, Int, Wis, Dex, Def, Spd, Luk };

        public static bool IsTrained(string id) => Array.IndexOf(Trained, id) >= 0;
        public static bool IsEffectStat(string id) => IsTrained(id) || id == CritRate || id == CritDmg;
    }

    /// <summary>A monster's eight trained stats (kj, 2026-10-30). Values are on the 0-999 training scale.</summary>
    public sealed class MonsterStats
    {
        public double Hp { get; set; }
        public double Str { get; set; }
        public double Int { get; set; }
        public double Wis { get; set; }
        public double Dex { get; set; }
        public double Def { get; set; }
        public double Spd { get; set; }
        public double Luk { get; set; }

        public double Get(string id)
        {
            switch (id)
            {
                case StatIds.Hp: return Hp;
                case StatIds.Str: return Str;
                case StatIds.Int: return Int;
                case StatIds.Wis: return Wis;
                case StatIds.Dex: return Dex;
                case StatIds.Def: return Def;
                case StatIds.Spd: return Spd;
                case StatIds.Luk: return Luk;
                default: throw new ArgumentOutOfRangeException(nameof(id), id, "not a trained stat");
            }
        }

        public MonsterStats Clone() => (MonsterStats)MemberwiseClone();
    }

    /// <summary>
    /// One battle action a fighter can take or a triggered effect it carries (specs/16 section 5). Uses the kit vocabulary of
    /// the idle battle (SkillKind, SkillTarget); tree nodes and chase traits (T3) produce these.
    /// </summary>
    public sealed class CombatSkill
    {
        public string Id { get; set; }
        public SkillKind Kind { get; set; }
        public SkillTarget Target { get; set; }

        /// <summary>Stat a buff, debuff or passive changes (StatIds.IsEffectStat).</summary>
        public string Stat { get; set; }

        /// <summary>Stat that sets the amount of damage, heal or shield: amount = current value x Power. "hp" means max HP.</summary>
        public string Scales { get; set; }

        /// <summary>Multiplier of Scales, or the buff/debuff fraction.</summary>
        public double Power { get; set; }

        /// <summary>Own actions before the skill is ready again. 0 for passives.</summary>
        public int Cooldown { get; set; }

        /// <summary>Turns a shield, taunt, buff or debuff lasts.</summary>
        public int Duration { get; set; }

        /// <summary>Damage element; null means the user's element.</summary>
        public string Element { get; set; }

        /// <summary>INT- and WIS-scaled damage is magical (mitigated by WIS, cannot be evaded); the rest is physical.</summary>
        public bool Magical => Scales == StatIds.Int || Scales == StatIds.Wis;
    }

    /// <summary>Where a triggered effect fires (specs/16 section 6).</summary>
    public enum FightTrigger { BattleStart, OnAction, OnBasicAttack, OnCrit, OnHitTaken, OnEvade }

    public sealed class TriggeredSkill
    {
        public TriggeredSkill() { }
        public TriggeredSkill(FightTrigger trigger, CombatSkill skill) { Trigger = trigger; Skill = skill; }

        public FightTrigger Trigger { get; set; }
        public CombatSkill Skill { get; set; }
    }

    /// <summary>
    /// A combatant as it enters a fight: the snapshot the run engine, the gauntlet, towers or PvP hand to BattleSim. Treated as
    /// read-only once a fight starts (FightUnit keeps a reference).
    /// </summary>
    public sealed class Fighter
    {
        /// <summary>Unique within the fight. Used in actions, tallies and the result.</summary>
        public string Id { get; set; }
        public string Element { get; set; }
        public MonsterStats Stats { get; set; } = new MonsterStats();

        /// <summary>Active skills in AI priority order, plus passives (applied at battle start).</summary>
        public List<CombatSkill> Skills { get; set; } = new List<CombatSkill>();

        /// <summary>Effects that fire at trigger points (chase traits, tree nodes).</summary>
        public List<TriggeredSkill> Triggers { get; set; } = new List<TriggeredSkill>();

        /// <summary>Coded rule modifiers by id (RuleModifiers).</summary>
        public List<string> Rules { get; set; } = new List<string>();
    }

    /// <summary>
    /// The numbers a fight runs on (specs/16 section 7). Plain data so a server can resolve PvP with the same rules; build it
    /// from content with FromContent.
    /// </summary>
    public sealed class FightRules
    {
        public double GaugeSize { get; set; }
        public int TurnTicks { get; set; }
        public int TimeLimitTicks { get; set; }
        public int SideSize { get; set; }
        public double BasicAttackPower { get; set; }
        public double AiHealBelow { get; set; }
        public double AiShieldBelow { get; set; }
        public double ElementStrongMult { get; set; }
        public double ElementWeakMult { get; set; }

        /// <summary>Element id -> the element it beats.</summary>
        public Dictionary<string, string> StrongVs { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public double HpPerPoint { get; set; }
        public double DefK { get; set; }
        public double EvadeBase { get; set; }
        public double EvadePerDex { get; set; }
        public double EvadeCap { get; set; }
        public double CritBase { get; set; }
        public double CritPerLuk { get; set; }
        public double CritCap { get; set; }
        public double CritDmg { get; set; }
        public double SpdMin { get; set; }

        /// <summary>A trained stat never drops below this fraction of its base (STAT_FLOOR_FRAC).</summary>
        public double StatFloor { get; set; }

        /// <summary>echo_cast: the echo's fraction of the skill's power.</summary>
        public double EchoPower { get; set; }

        public static FightRules FromContent(ContentDb c)
        {
            var k = c.Constants;
            var r = new FightRules
            {
                GaugeSize = k.BattleGauge, TurnTicks = k.TurnTicks, TimeLimitTicks = k.MonTimeLimitTicks, SideSize = k.MonSideSize,
                BasicAttackPower = k.BasicAttackPower, AiHealBelow = k.AiHealBelow, AiShieldBelow = k.AiShieldBelow,
                ElementStrongMult = k.ElementStrongMult, ElementWeakMult = k.ElementWeakMult,
                HpPerPoint = k.MonHpPerPoint, DefK = k.MonDefK, EvadeBase = k.MonEvadeBase, EvadePerDex = k.MonEvadePerDex,
                EvadeCap = k.MonEvadeCap, CritBase = k.MonCritBase, CritPerLuk = k.MonCritPerLuk, CritCap = k.MonCritCap,
                CritDmg = k.MonCritDmg, SpdMin = k.MonSpdMin, StatFloor = k.StatFloorFrac, EchoPower = k.MonEchoPower,
            };
            foreach (var e in c.ElementList) r.StrongVs[e.Id] = e.StrongVs;
            return r;
        }

        public double ElementMult(string attacker, string defender)
        {
            if (attacker == null || defender == null) return 1.0;
            if (StrongVs.TryGetValue(attacker, out var a) && a == defender) return ElementStrongMult;
            if (StrongVs.TryGetValue(defender, out var d) && d == attacker) return ElementWeakMult;
            return 1.0;
        }
    }

    /// <summary>Coded rule modifiers (specs/16 section 6.2): behaviour a table row cannot express, referenced by id.</summary>
    public static class RuleModifiers
    {
        /// <summary>
        /// After taking a single-target physical hit and surviving, counterattack at once for the damage DEF mitigated, as true
        /// damage (no evade, crit, element or mitigation), regardless of gauge. The drafts' Heavy Armor + Unbreakable Will passive.
        /// </summary>
        public const string CounterMitigated = "counter_mitigated";

        /// <summary>
        /// Every active damage, heal or shield skill this unit uses is cast a second time at once, at MON_ECHO_POWER of its
        /// power (passives, triggered effects and basic attacks do not echo). The Arcane Overflow chase capstone.
        /// </summary>
        public const string EchoCast = "echo_cast";

        public static readonly string[] Known = { CounterMitigated, EchoCast };

        public static bool IsKnown(string id) => Array.IndexOf(Known, id) >= 0;
    }
}
