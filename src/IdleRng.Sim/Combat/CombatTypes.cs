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

        /// <summary>
        /// More bonus-only stats, added directly: damage dealt (a fraction on top of 1; the entropy enrage vent and boss phases
        /// use it) and entropy gain (a fraction of the gain; negative lowers it, specs/21 section 3.1).
        /// </summary>
        public const string DmgDealt = "dmg_dealt", EntropyGain = "entropy_gain";

        /// <summary>The eight trained stats, in sheet order.</summary>
        public static readonly string[] Trained = { Hp, Str, Int, Wis, Dex, Def, Spd, Luk };

        public static bool IsTrained(string id) => Array.IndexOf(Trained, id) >= 0;
        public static bool IsEffectStat(string id) => IsTrained(id) || id == CritRate || id == CritDmg || id == DmgDealt || id == EntropyGain;
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

        /// <summary>Boss phases, highest enter_hp first (specs/21 section 3.2). Only a fight with <see cref="FightRules.Tower"/> runs them.</summary>
        public List<BossPhase> Phases { get; set; } = new List<BossPhase>();

        /// <summary>
        /// The unit's gambit rulebook, checked in order before the default AI (specs/28 section 4). Empty: the default AI alone,
        /// as before rulebooks existed.
        /// </summary>
        public List<GambitRule> Gambits { get; set; } = new List<GambitRule>();

        /// <summary>A shallow copy under another id (towers rename team members; split phases number their adds).</summary>
        public Fighter WithId(string id) => new Fighter
        {
            Id = id, Element = Element, Stats = Stats, Skills = Skills, Triggers = Triggers, Rules = Rules, Phases = Phases, Gambits = Gambits,
        };
    }

    /// <summary>
    /// One IF/THEN line of a gambit rulebook (specs/28 section 4): when <see cref="When"/> holds (with <see cref="Value"/>), use
    /// <see cref="Skill"/> (one of the unit's own active skills by id, or <see cref="Basic"/>), aimed by <see cref="Target"/>.
    /// </summary>
    public sealed class GambitRule
    {
        /// <summary>The basic attack as a rule's action.</summary>
        public const string Basic = "basic";

        public GambitRule() { }
        public GambitRule(string when, double value, string skill, string target = GambitTargets.Auto)
        {
            When = when; Value = value; Skill = skill; Target = target;
        }

        /// <summary>A <see cref="GambitConditions"/> id.</summary>
        public string When { get; set; } = GambitConditions.Always;

        /// <summary>The condition's threshold: an HP fraction in (0, 1] or a whole count of at least 1; unused by "always".</summary>
        public double Value { get; set; }

        public string Skill { get; set; }

        /// <summary>A <see cref="GambitTargets"/> id: how the action picks a single enemy.</summary>
        public string Target { get; set; } = GambitTargets.Auto;
    }

    /// <summary>Gambit conditions (specs/28 section 4.1). HP conditions compare HP fractions; counts are whole numbers.</summary>
    public static class GambitConditions
    {
        public const string Always = "always";
        public const string SelfHpBelow = "self_hp_below";
        public const string AllyHpBelow = "ally_hp_below";
        public const string EnemyHpBelow = "enemy_hp_below";
        public const string EnemiesAtLeast = "enemies_at_least";
        public const string FirstActions = "first_actions";

        public static readonly string[] Known = { Always, SelfHpBelow, AllyHpBelow, EnemyHpBelow, EnemiesAtLeast, FirstActions };
        public static bool IsKnown(string id) => Array.IndexOf(Known, id) >= 0;

        /// <summary>True when the condition's Value is an HP fraction in (0, 1].</summary>
        public static bool TakesFraction(string id) => id == SelfHpBelow || id == AllyHpBelow || id == EnemyHpBelow;

        /// <summary>True when the condition's Value is a whole count of at least 1.</summary>
        public static bool TakesCount(string id) => id == EnemiesAtLeast || id == FirstActions;
    }

    /// <summary>How a gambit's action picks a single enemy (specs/28 section 4.2). A taunting enemy is still forced.</summary>
    public static class GambitTargets
    {
        /// <summary>The default rule: a random living enemy.</summary>
        public const string Auto = "auto";
        /// <summary>The living enemy with the lowest HP fraction (lowest slot on a tie); no random draw.</summary>
        public const string LowestHp = "lowest_hp";
        /// <summary>The living enemy with the highest HP fraction (lowest slot on a tie); no random draw.</summary>
        public const string HighestHp = "highest_hp";

        public static readonly string[] Known = { Auto, LowestHp, HighestHp };
        public static bool IsKnown(string id) => Array.IndexOf(Known, id) >= 0;
    }

    /// <summary>The coded boss phase kinds (specs/21 section 3.2).</summary>
    public static class PhaseKinds
    {
        public const string ShieldCheck = "shield_check", Split = "split", Channel = "channel", Enrage = "enrage";
        public static readonly string[] Known = { ShieldCheck, Split, Channel, Enrage };
        public static bool IsKnown(string id) => Array.IndexOf(Known, id) >= 0;
    }

    /// <summary>
    /// One boss phase (specs/21 section 3.2): entered once, when the boss's HP first falls to <see cref="EnterHp"/> of max.
    /// Which of the other fields matter depends on <see cref="Kind"/>.
    /// </summary>
    public sealed class BossPhase
    {
        /// <summary>HP fraction in (0, 1) at or below which the phase starts.</summary>
        public double EnterHp { get; set; }
        public string Kind { get; set; }

        /// <summary>shield_check: shield as a fraction of max HP. channel: damage to deal during the channel, as a fraction of max HP.</summary>
        public double Power { get; set; }

        /// <summary>shield_check and channel: turns it lasts.</summary>
        public int Duration { get; set; }

        /// <summary>split: adds that join the boss's side (up to the side-size cap).</summary>
        public int Count { get; set; }

        /// <summary>split: damage the boss takes less while an add lives, as a fraction.</summary>
        public double Guard { get; set; }

        /// <summary>shield_check: fraction of max HP the boss heals if the shield expires unbroken.</summary>
        public double HealFrac { get; set; }

        /// <summary>channel: true damage to every enemy if not interrupted, as a multiple of the boss's STR or INT (the higher).</summary>
        public double Strike { get; set; }

        /// <summary>shield_check: a permanent damage buff if the shield expires unbroken. enrage: damage buff added each turn.</summary>
        public double Buff { get; set; }

        /// <summary>split: what each add looks like (a fighter template; its id gets a #n suffix).</summary>
        public Fighter Add { get; set; }

        /// <summary>Skills the boss gains on entry (passives apply at once). Added after its own, so cooldowns of existing skills are kept.</summary>
        public List<CombatSkill> AddSkills { get; set; } = new List<CombatSkill>();
    }

    /// <summary>
    /// The Dungeon's extra rules (specs/21 section 3): the entropy gauge and the environmental clock. Null on
    /// <see cref="FightRules.Tower"/> turns all of it, and boss phases, off, and the fight (and its hash) is exactly as before.
    /// </summary>
    public sealed class TowerRules
    {
        public double EntropyCap { get; set; }
        public double EntropyPerTrigger { get; set; }
        public double EntropyPerEcho { get; set; }
        public double EntropyPerCounter { get; set; }
        public double EntropyDecay { get; set; }
        public double RecoilFrac { get; set; }
        public double StunGauge { get; set; }
        public double EnrageFrac { get; set; }
        public int EnrageTurns { get; set; }
        public double ConvertShield { get; set; }
        public int ConvertTurns { get; set; }

        /// <summary>Corruption level = max(0, (tick - CorruptStartTick) / CorruptEveryTicks), integer division.</summary>
        public int CorruptStartTick { get; set; }
        public int CorruptEveryTicks { get; set; }
        public double CorruptHealPerLevel { get; set; }
        public double CorruptHealFloor { get; set; }
        public double CorruptDmgPerLevel { get; set; }

        public bool Valid =>
            EntropyCap > 0 && EntropyPerTrigger >= 0 && EntropyPerEcho >= 0 && EntropyPerCounter >= 0 && EntropyDecay >= 0
            && RecoilFrac >= 0 && RecoilFrac <= 1 && StunGauge >= 0 && EnrageFrac >= 0 && EnrageTurns > 0 && ConvertShield >= 0 && ConvertTurns > 0
            && CorruptStartTick >= 0 && CorruptEveryTicks > 0 && CorruptHealPerLevel >= 0 && CorruptHealFloor >= 0 && CorruptHealFloor <= 1
            && CorruptDmgPerLevel >= 0;

        public TowerRules Clone() => (TowerRules)MemberwiseClone();

        /// <summary>The tower rules from content constants (specs/21 section 5).</summary>
        public static TowerRules FromContent(ContentDb c)
        {
            var k = c.Constants;
            return new TowerRules
            {
                EntropyCap = k.EntropyCap, EntropyPerTrigger = k.EntropyPerTrigger, EntropyPerEcho = k.EntropyPerEcho,
                EntropyPerCounter = k.EntropyPerCounter, EntropyDecay = k.EntropyDecay, RecoilFrac = k.EntropyRecoilFrac,
                StunGauge = k.EntropyStunGauge, EnrageFrac = k.EntropyEnrageFrac, EnrageTurns = k.EntropyEnrageTurns,
                ConvertShield = k.EntropyConvertShield, ConvertTurns = k.EntropyConvertTurns,
                CorruptStartTick = k.CorruptStartTick, CorruptEveryTicks = k.CorruptEveryTicks,
                CorruptHealPerLevel = k.CorruptHealPerLevel, CorruptHealFloor = k.CorruptHealFloor, CorruptDmgPerLevel = k.CorruptDmgPerLevel,
            };
        }
    }

    /// <summary>Mutator kinds (specs/28 section 3.2): rule changes for every unit in a fight.</summary>
    public static class MutatorKinds
    {
        /// <summary>Heals and shields x value (value >= 0).</summary>
        public const string HealMult = "heal_mult";
        /// <summary>All-enemy damage x value (value >= 0).</summary>
        public const string AoeMult = "aoe_mult";
        /// <summary>Single-target damage (skills, basic attacks, triggers) x value (value >= 0).</summary>
        public const string SingleMult = "single_mult";
        /// <summary>Damage of one element x value (value >= 0; needs the element).</summary>
        public const string ElementMult = "element_mult";
        /// <summary>Every unit's ATB gauge fill x value (value > 0).</summary>
        public const string SpeedMult = "speed_mult";
        /// <summary>Crit chance + value, in [-1, 1], before the crit cap.</summary>
        public const string CritBonus = "crit_bonus";
        /// <summary>The environmental clock starts value ticks earlier (a whole number >= 0; tower fights only).</summary>
        public const string CorruptionEarly = "corruption_early";
        /// <summary>Every unit starts with value x its max HP as shield, until broken or replaced (value in [0, 1]).</summary>
        public const string StartShield = "start_shield";

        public static readonly string[] Known = { HealMult, AoeMult, SingleMult, ElementMult, SpeedMult, CritBonus, CorruptionEarly, StartShield };
        public static bool IsKnown(string id) => Array.IndexOf(Known, id) >= 0;
    }

    /// <summary>
    /// The mutators in force in a fight, folded into the numbers BattleSim reads (specs/28 section 3.2). Mutators stack: the
    /// multipliers multiply, crit bonuses, clock ticks and start shields add. The defaults change nothing.
    /// </summary>
    public sealed class FightMutators
    {
        public double Heal { get; private set; } = 1;
        public double Aoe { get; private set; } = 1;
        public double Single { get; private set; } = 1;
        public double Speed { get; private set; } = 1;
        public double CritBonus { get; private set; }
        public int CorruptEarlyTicks { get; private set; }
        public double StartShield { get; private set; }

        /// <summary>Element id -> damage multiplier.</summary>
        public SortedDictionary<string, double> Element { get; } = new SortedDictionary<string, double>(StringComparer.Ordinal);

        /// <summary>Adds one mutator. Throws ArgumentException for an unknown kind, a value out of its range or a missing element.</summary>
        public FightMutators Add(string kind, double value, string element = null)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException($"mutator '{kind}': value must be finite", nameof(value));
            switch (kind)
            {
                case MutatorKinds.HealMult: Heal *= NonNegative(kind, value); break;
                case MutatorKinds.AoeMult: Aoe *= NonNegative(kind, value); break;
                case MutatorKinds.SingleMult: Single *= NonNegative(kind, value); break;
                case MutatorKinds.ElementMult:
                    if (string.IsNullOrEmpty(element)) throw new ArgumentException("mutator 'element_mult' needs an element", nameof(element));
                    Element[element] = ElementMult(element) * NonNegative(kind, value);
                    break;
                case MutatorKinds.SpeedMult:
                    if (!(value > 0)) throw new ArgumentException("mutator 'speed_mult' needs a value > 0", nameof(value));
                    Speed *= value;
                    break;
                case MutatorKinds.CritBonus:
                    if (value < -1 || value > 1) throw new ArgumentException("mutator 'crit_bonus' needs a value in [-1, 1]", nameof(value));
                    CritBonus += value;
                    break;
                case MutatorKinds.CorruptionEarly:
                    if (value < 0 || value > int.MaxValue || value != Math.Floor(value))
                        throw new ArgumentException("mutator 'corruption_early' needs a whole number of ticks >= 0", nameof(value));
                    CorruptEarlyTicks = (int)Math.Min(int.MaxValue, (long)CorruptEarlyTicks + (long)value);
                    break;
                case MutatorKinds.StartShield:
                    if (value < 0 || value > 1) throw new ArgumentException("mutator 'start_shield' needs a value in [0, 1]", nameof(value));
                    StartShield += value;
                    break;
                default: throw new ArgumentException($"unknown mutator kind '{kind}'", nameof(kind));
            }
            return this;
        }

        static double NonNegative(string kind, double value) =>
            value >= 0 ? value : throw new ArgumentException($"mutator '{kind}' needs a value >= 0", nameof(value));

        public double ElementMult(string element) =>
            element != null && Element.TryGetValue(element, out var m) ? m : 1.0;

        /// <summary>The damage multiplier for one hit: AoE or single-target, times its element's.</summary>
        public double DamageMult(bool aoe, string element) => (aoe ? Aoe : Single) * ElementMult(element);
    }

    /// <summary>
    /// The numbers a fight runs on (specs/16 section 7). Plain data so a server can resolve PvP with the same rules; build it
    /// from content with FromContent.
    /// </summary>
    public sealed partial class FightRules
    {
        public double GaugeSize { get; set; }
        public int TurnTicks { get; set; }
        public int TimeLimitTicks { get; set; }
        public int SideSize { get; set; }
        public double BasicAttackPower { get; set; }
        public double AiHealBelow { get; set; }
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

        /// <summary>Crit damage multiplier added per point of the attacker's DEX (specs/16 section 5.3).</summary>
        public double CritDmgPerDex { get; set; }

        /// <summary>Share of its damage a magical hit deals when it rolls under the target's evade chance.</summary>
        public double GrazeMult { get; set; }

        /// <summary>Fortune = min(FortuneCap, LUK / (LUK + FortuneK)): the chance to reroll a roll that went against the unit.</summary>
        public double FortuneK { get; set; }
        public double FortuneCap { get; set; }
        public double SpdMin { get; set; }

        /// <summary>A trained stat never drops below this fraction of its base (STAT_FLOOR_FRAC).</summary>
        public double StatFloor { get; set; }

        /// <summary>Most copies of one timed effect (same source and stat) a unit can carry (EFFECT_STACK_CAP).</summary>
        public int StackCap { get; set; }

        /// <summary>echo_cast: the echo's fraction of the skill's power.</summary>
        public double EchoPower { get; set; }

        /// <summary>tidal_overflow: the fraction of max HP the unit heals at the start of each of its actions.</summary>
        public double TideRegen { get; set; }

        /// <summary>tidal_overflow: overhealing becomes shield up to this fraction of max HP.</summary>
        public double TideShieldCap { get; set; }

        /// <summary>abyssal_drain: the fraction of the HP damage a hit deals that heals the attacker.</summary>
        public double DrainFrac { get; set; }

        /// <summary>fire_pierce: the fraction of the target's DEF or WIS that fire damage ignores.</summary>
        public double FirePierce { get; set; }

        /// <summary>double_turn_start: how many of the unit's first actions are each followed by an extra action.</summary>
        public int DoubleTurns { get; set; }

        /// <summary>root_rebirth: the fraction of max HP the unit comes back with after its one killing blow.</summary>
        public double RebirthHeal { get; set; }

        /// <summary>wail_stagger: the fraction of a full ATB gauge each of the unit's actions takes from every enemy.</summary>
        public double WailStagger { get; set; }

        /// <summary>wail_stagger: true damage, a fraction of each enemy's max HP, each of the unit's actions deals to every enemy.</summary>
        public double WailWither { get; set; }

        /// <summary>umbral_veil: the unit's evade cap (its evade roll also covers magical hits).</summary>
        public double VeilEvadeCap { get; set; }

        /// <summary>night_feast: the STR (fraction of base) each landed hit adds for the rest of the fight.</summary>
        public double FeastStr { get; set; }

        /// <summary>counter_mitigated: the counter deals this multiple of the damage DEF mitigated.</summary>
        public double CounterMult { get; set; } = 1;

        /// <summary>fire_pierce: the unit's fire damage is multiplied by 1 + this.</summary>
        public double FireBonus { get; set; }

        /// <summary>umbral_veil: flat evade chance added before the cap.</summary>
        public double VeilEvadeBonus { get; set; }

        /// <summary>night_feast: the damage-dealt bonus each landed hit adds for the rest of the fight.</summary>
        public double FeastDmg { get; set; }

        /// <summary>night_feast: the damage-dealt bonus the unit starts every fight with.</summary>
        public double FeastStart { get; set; }

        /// <summary>echo_cast: hits per fight its mirror twin takes instead of it.</summary>
        public int EchoDecoys { get; set; }

        /// <summary>abyssal_drain: draining beyond max HP becomes shield up to this fraction of max HP.</summary>
        public double DrainShieldCap { get; set; }

        /// <summary>Loop guard: most trigger levels running at once, MON_TRIGGER_DEPTH (specs/16 section 6.4).</summary>
        public int TriggerDepth { get; set; } = 2;

        /// <summary>Loop guard: most extra actions a unit takes after one gauge-paid action, MON_EXTRA_ACTIONS_CAP (specs/16 section 6.4).</summary>
        public int ExtraActionsCap { get; set; } = 2;

        /// <summary>The Dungeon's entropy gauge, boss phases and environmental clock; null = off (specs/21 section 3).</summary>
        public TowerRules Tower { get; set; }

        /// <summary>The Endless Tower's mutators, for every unit on both sides; null = none (specs/28 section 3.2).</summary>
        public FightMutators Mutators { get; set; }

        /// <summary>Fight rules from content; <paramref name="tower"/> adds the Dungeon's rules (specs/21 section 3).</summary>
        public static FightRules FromContent(ContentDb c, bool tower)
        {
            var r = FromContent(c);
            if (tower) r.Tower = TowerRules.FromContent(c);
            return r;
        }

        public static FightRules FromContent(ContentDb c)
        {
            var k = c.Constants;
            var r = new FightRules
            {
                GaugeSize = k.BattleGauge, TurnTicks = k.TurnTicks, TimeLimitTicks = k.MonTimeLimitTicks, SideSize = k.MonSideSize,
                BasicAttackPower = k.BasicAttackPower, AiHealBelow = k.AiHealBelow,
                ElementStrongMult = k.ElementStrongMult, ElementWeakMult = k.ElementWeakMult,
                HpPerPoint = k.MonHpPerPoint, DefK = k.MonDefK, EvadeBase = k.MonEvadeBase, EvadePerDex = k.MonEvadePerDex,
                EvadeCap = k.MonEvadeCap, CritBase = k.MonCritBase, CritPerLuk = k.MonCritPerLuk, CritCap = k.MonCritCap,
                CritDmg = k.MonCritDmg, CritDmgPerDex = k.MonCritDmgPerDex, GrazeMult = k.MonGrazeMult, FortuneK = k.MonFortuneK, FortuneCap = k.MonFortuneCap, SpdMin = k.MonSpdMin, StatFloor = k.StatFloorFrac, StackCap = k.EffectStackCap, EchoPower = k.MonEchoPower,
                TideRegen = k.MonTideRegen, TideShieldCap = k.MonTideShieldCap, DrainFrac = k.MonDrainFrac, FirePierce = k.MonFirePierce,
                DoubleTurns = k.MonDoubleTurns, RebirthHeal = k.MonRebirthHeal, WailStagger = k.MonWailStagger, WailWither = k.MonWailWither,
                VeilEvadeCap = k.MonVeilEvadeCap, FeastStr = k.MonFeastStr, CounterMult = k.MonCounterMult, FireBonus = k.MonFireBonus,
                VeilEvadeBonus = k.MonVeilEvadeBonus, FeastDmg = k.MonFeastDmg, FeastStart = k.MonFeastStart, EchoDecoys = k.MonEchoDecoys, DrainShieldCap = k.MonDrainShieldCap,
                TriggerDepth = k.MonTriggerDepth, ExtraActionsCap = k.MonExtraActionsCap,
            };
            foreach (var e in c.ElementList) r.StrongVs[e.Id] = e.StrongVs;
            LoadLanes(r, c);
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

        /// <summary>
        /// At the start of each of its actions the unit heals MON_TIDE_REGEN of its max HP, and any healing it receives beyond max HP
        /// (this regeneration, heal skills, abyssal_drain) becomes shield, up to MON_TIDE_SHIELD_CAP of its max HP. The Living Ocean
        /// chase capstone.
        /// </summary>
        public const string TidalOverflow = "tidal_overflow";

        /// <summary>
        /// Every hit the unit lands heals it for MON_DRAIN_FRAC of the HP damage the hit dealt (counters and other true damage do
        /// not drain). The Abyssal Bloom chase capstone.
        /// </summary>
        public const string AbyssalDrain = "abyssal_drain";

        /// <summary>
        /// The unit's fire damage ignores MON_FIRE_PIERCE of the target's DEF or WIS and is never resisted (the element multiplier is
        /// at least 1). The Inferno Sovereign chase capstone.
        /// </summary>
        public const string FirePierce = "fire_pierce";

        /// <summary>
        /// Each of the unit's first MON_DOUBLE_TURNS actions of the fight is followed at once by an extra full action (cooldowns
        /// tick, OnAction triggers fire). The Starlit Ascension chase capstone.
        /// </summary>
        public const string DoubleTurnStart = "double_turn_start";

        /// <summary>
        /// Once per fight, a blow that would bring the unit to 0 HP leaves it alive at MON_REBIRTH_HEAL of its max HP instead (no
        /// kill is counted). The World Root chase capstone (Mandrake).
        /// </summary>
        public const string RootRebirth = "root_rebirth";

        /// <summary>
        /// Every hit the unit lands that deals damage takes MON_WAIL_STAGGER of a full ATB gauge from the target (never below 0),
        /// pushing its next turn back. The Mandragora Scream chase capstone (Mandrake).
        /// </summary>
        public const string WailStagger = "wail_stagger";

        /// <summary>
        /// The unit's evade roll also applies to magical hits, and its evade chance is capped at MON_VEIL_EVADE_CAP instead of
        /// MON_EVADE_CAP. The Umbral Veil chase capstone (Shade Hound).
        /// </summary>
        public const string UmbralVeil = "umbral_veil";

        /// <summary>
        /// Every hit the unit lands that deals damage adds MON_FEAST_STR of its base STR for the rest of the fight, without a cap.
        /// The Night Feast chase capstone (Shade Hound).
        /// </summary>
        public const string NightFeast = "night_feast";

        /// <summary>The element id fire_pierce works on.</summary>
        public const string FireElement = "fire";

        /// <summary>
        /// When the unit's entropy gauge would vent (tower fights only), it gains a shield of ENTROPY_CONVERT_SHIELD of its max HP
        /// instead (specs/21 section 3.1).
        /// </summary>
        public const string EntropyConvert = "entropy_convert";

        // Idea-pool lane A (#134): conversion rules, coded in Combat/Rules/LaneA.cs.

        /// <summary>
        /// The unit's evade and graze chance are 0 (this wins over umbral_veil); it mitigates physical hits with DEF +
        /// MON_IRON_DEX x DEX. The Iron Reflexes generic capstone.
        /// </summary>
        public const string IronReflexes = "iron_reflexes";

        /// <summary>
        /// Every hit the unit deals (skills, basic attacks, triggered hits) takes the unit's own element and deals x(1 +
        /// MON_AVATAR_BONUS); a unit without an element is unaffected. The Elemental Avatar generic capstone.
        /// </summary>
        public const string ElementalAvatar = "elemental_avatar";

        /// <summary>
        /// Each hit the unit deals adds MON_FORTRESS_DEF x its DEF to the raw damage; its STR and INT count as 0 for damage (heals
        /// and shields keep them). The Fortress Fist generic capstone.
        /// </summary>
        public const string FortressFist = "fortress_fist";

        /// <summary>
        /// The unit's active skills have no cooldown; each cast costs MON_BLOOD_COST of max HP (never below 1 HP, not damage), and
        /// it does not cast when that would leave it under MON_BLOOD_FLOOR of max HP. The Blood Magic generic capstone.
        /// </summary>
        public const string BloodMagic = "blood_magic";

        // Idea-pool lane D (#137): debuffs, stacking and skill locks, coded in Combat/Rules/LaneD.cs.

        /// <summary>
        /// Each hit the unit lands fills the target's bleed gauge by MON_BLEED_PER_HIT x DEX / MON_BLEED_DEX_REF; at 100 it bursts
        /// for MON_BLEED_BURST of the target's max HP as true damage and resets. The Hemorrhage generic capstone.
        /// </summary>
        public const string Hemorrhage = "hemorrhage";

        /// <summary>
        /// The unit's hits deal x(1 + MON_PRIV_BONUS) to a target with at least MON_PRIV_DEBUFFS active debuffs from any source. The
        /// Privileged Status generic capstone.
        /// </summary>
        public const string PrivilegedStatus = "privileged_status";

        /// <summary>
        /// A hit of an element marks the target with it; the unit's next hit on that target of another element deals x(1 +
        /// MON_EQ_BONUS) and clears the mark. The Equilibrium generic capstone.
        /// </summary>
        public const string Equilibrium = "equilibrium";

        /// <summary>
        /// Each active skill the unit casts as an action is cast a second time at once at full power, then is locked for the rest
        /// of the fight. The Corruption generic capstone.
        /// </summary>
        public const string Corruption = "corruption";

        /// <summary>
        /// Whenever one of the unit's skills is locked for the fight, it gains a random active skill (one it lacks) for the rest
        /// of the fight. The Dead Branch generic capstone.
        /// </summary>
        public const string DeadBranch = "dead_branch";

        public static readonly string[] Known = { CounterMitigated, EchoCast, TidalOverflow, AbyssalDrain, FirePierce, DoubleTurnStart, EntropyConvert,
            RootRebirth, WailStagger, UmbralVeil, NightFeast,
            IronReflexes, ElementalAvatar, FortressFist, BloodMagic,
            Hemorrhage, PrivilegedStatus, Equilibrium, Corruption, DeadBranch };

        public static bool IsKnown(string id) => Array.IndexOf(Known, id) >= 0;
    }
}
