using System;
using System.Collections.Generic;

namespace IdleRng.Sim.Content
{
    /// <summary>C &lt; R &lt; E &lt; L &lt; M. Used for gear rarity and hero tier alike (order is load-bearing).</summary>
    public enum Rarity { C = 0, R = 1, E = 2, L = 3, M = 4 }

    public enum BannerKind { Hero, Pet }
    public enum CurveId { EnemyHp, GoldPerKill, StatCost }

    /// <summary>ClearBoss counts boss fights STARTED, not cleared (specs/00 section 7.2).</summary>
    public enum QuestType { Login = 0, KillMobs = 1, ClearBoss = 2, DoPulls = 3 }

    public enum GrantTrigger { ProfileCreated, StageReached, FirstBossCleared }
    public enum GrantType { Gems, FreePulls, Gold, Hero }

    /// <summary>Scalar constants (specs/05 section 9.2). Property name = PascalCase of the content key.</summary>
    public sealed class Constants
    {
        public int TickHz { get; internal set; }                 // TICK_HZ
        public double SpdRef { get; internal set; }              // SPD_REF (specs/11 section 7)
        public int AttackIntervalTicks { get; internal set; }    // ATTACK_INTERVAL_TICKS
        public int KillsPerStage { get; internal set; }          // KILLS_PER_STAGE
        public int SpawnDelayTicks { get; internal set; }        // SPAWN_DELAY_TICKS
        public int BossEvery { get; internal set; }              // BOSS_EVERY
        public int StageCap { get; internal set; }               // STAGE_CAP
        public double StatPerLvl { get; internal set; }          // STAT_PER_LVL
        public int GemsPerDayF2p { get; internal set; }          // GEMS_PER_DAY_F2P
        public double OfflineRate { get; internal set; }         // OFFLINE_RATE
        public double OfflineCapH { get; internal set; }         // OFFLINE_CAP_H
        public int OfflineMinGapS { get; internal set; }         // OFFLINE_MIN_GAP_S
        public int DaySeconds { get; internal set; }             // DAY_SECONDS
        public double GearDropP { get; internal set; }           // GEAR_DROP_P
        public int TierWidth { get; internal set; }              // TIER_WIDTH
        public double GearSlotG { get; internal set; }           // GEAR_SLOT_G
        public double GearSellKills { get; internal set; }       // GEAR_SELL_KILLS
        public int EnhMax { get; internal set; }                 // ENH_MAX
        public double EnhPerLvl { get; internal set; }           // ENH_PER_LVL
        public double EnhSuccessFloor { get; internal set; }     // ENH_SUCCESS_FLOOR
        public double EnhSuccessStep { get; internal set; }      // ENH_SUCCESS_STEP
        public double EnhCostGoldMult { get; internal set; }     // ENH_COST_GOLD_MULT
        public int HeroSlots { get; internal set; }              // HERO_SLOTS
        public double HeroLvlBonus { get; internal set; }        // HERO_LVL_BONUS
        public double IdleChestHours { get; internal set; }      // IDLE_CHEST_HOURS
        public int IdleChestGems { get; internal set; }          // IDLE_CHEST_GEMS

        // 4v1 boss battle (specs/11 section 8)
        public double BattleGauge { get; internal set; }         // BATTLE_GAUGE
        public double DefKMult { get; internal set; }            // DEF_K_MULT
        public double ElementStrongMult { get; internal set; }   // ELEMENT_STRONG_MULT
        public double ElementWeakMult { get; internal set; }     // ELEMENT_WEAK_MULT
        public double BasicAttackPower { get; internal set; }    // BASIC_ATTACK_POWER
        public double AiHealBelow { get; internal set; }         // AI_HEAL_BELOW
        public double AiShieldBelow { get; internal set; }       // AI_SHIELD_BELOW

        // Monster battle (specs/16 section 7). Placeholders.
        public double MonHpPerPoint { get; internal set; }       // MON_HP_PER_POINT
        public double MonDefK { get; internal set; }             // MON_DEF_K
        public double MonEvadeBase { get; internal set; }        // MON_EVADE_BASE
        public double MonEvadePerDex { get; internal set; }      // MON_EVADE_PER_DEX
        public double MonEvadeCap { get; internal set; }         // MON_EVADE_CAP
        public double MonCritBase { get; internal set; }         // MON_CRIT_BASE
        public double MonCritPerLuk { get; internal set; }       // MON_CRIT_PER_LUK
        public double MonCritCap { get; internal set; }          // MON_CRIT_CAP
        public double MonCritDmg { get; internal set; }          // MON_CRIT_DMG
        public double MonSpdMin { get; internal set; }           // MON_SPD_MIN
        public int MonTimeLimitTicks { get; internal set; }      // MON_TIME_LIMIT_TICKS
        public int MonSideSize { get; internal set; }            // MON_SIDE_SIZE

        // Training run (specs/14 section 8). Placeholders.
        public int RunCycles { get; internal set; }              // RUN_CYCLES
        public int RunAp { get; internal set; }                  // RUN_AP
        public double RunEnergyMax { get; internal set; }        // RUN_ENERGY_MAX
        public double RunEnergyStart { get; internal set; }      // RUN_ENERGY_START
        public double DrillEnergyCost { get; internal set; }     // DRILL_ENERGY_COST
        public double RestEnergy { get; internal set; }          // REST_ENERGY
        public double DrillFailMin { get; internal set; }        // DRILL_FAIL_MIN
        public double DrillFailMax { get; internal set; }        // DRILL_FAIL_MAX
        public double DrillSuperP { get; internal set; }         // DRILL_SUPER_P
        public double InjuryBelow { get; internal set; }         // INJURY_BELOW
        public double InjuryMaxP { get; internal set; }          // INJURY_MAX_P
        public double StatMax { get; internal set; }             // STAT_MAX
        public int FightEvery { get; internal set; }             // FIGHT_EVERY

        // Skill trees (specs/15 section 9). Placeholders.
        public int SpPerCycle { get; internal set; }             // SP_PER_CYCLE
        public int SpPerWin { get; internal set; }               // SP_PER_WIN
        public int TreeSlots { get; internal set; }              // TREE_SLOTS
        public int DraftSize { get; internal set; }              // DRAFT_SIZE
        public double MonEchoPower { get; internal set; }        // MON_ECHO_POWER

        // Account and meta-progression (specs/18 section 5). Placeholders.
        public int ShardsPerCycle { get; internal set; }         // SHARDS_PER_CYCLE
        public int ShardsPerRank { get; internal set; }          // SHARDS_PER_RANK

        // End-of-run gauntlet (specs/19 section 4). Placeholders.
        public int GauntletFloors { get; internal set; }         // GAUNTLET_FLOORS
        public double GauntletBase { get; internal set; }        // GAUNTLET_BASE
        public double GauntletGrowth { get; internal set; }      // GAUNTLET_GROWTH
        public int GauntletBossEvery { get; internal set; }      // GAUNTLET_BOSS_EVERY
        public double GauntletBossMult { get; internal set; }    // GAUNTLET_BOSS_MULT

        /// <summary>Ticks in one "turn" at reference speed: BATTLE_GAUGE / SPD_REF (skill durations are in turns).</summary>
        public int TurnTicks => Math.Max(1, (int)Math.Round(BattleGauge / SpdRef));

        public int TierOf(int stage) => (stage / TierWidth) * TierWidth;
        public bool IsBossStage(int stage) => stage % BossEvery == 0;
        public double EnhSuccess(int level) => Math.Max(EnhSuccessFloor, 1.0 - EnhSuccessStep * level);
    }

    /// <summary>
    /// base * growth^x, evaluated by repeated multiplication (never Math.Pow; specs/03 section 4).
    /// Precomputed at load so a shared ContentDb is read-only (thread-safe); beyond the table it keeps
    /// multiplying from the last entry, which gives the same bits.
    /// </summary>
    public sealed class CurveDef
    {
        readonly double[] _pow;

        internal CurveDef(CurveId id, double @base, double growth, int precompute)
        {
            Id = id;
            Base = @base;
            Growth = growth;
            _pow = new double[precompute + 1];
            _pow[0] = 1.0;
            for (int i = 1; i < _pow.Length; i++) _pow[i] = _pow[i - 1] * growth;
        }

        public CurveId Id { get; }
        public double Base { get; }
        public double Growth { get; }

        /// <summary>growth^x.</summary>
        public double Pow(int x)
        {
            if (x < _pow.Length) return _pow[x];
            double v = _pow[_pow.Length - 1];
            for (int i = _pow.Length - 1; i < x; i++) v *= Growth;
            return v;
        }

        public double Eval(int x) => Base * Pow(x);
    }

    public sealed class BossRuleDef
    {
        public string Id { get; internal set; }
        public int FromStage { get; internal set; }
        public double HpMult { get; internal set; }
        public int TimeTicks { get; internal set; }
        public double GoldMult { get; internal set; }
        public double GearDropP { get; internal set; }
        public Rarity GearMinRarity { get; internal set; }
        public int FirstClearGems { get; internal set; }

        // boss battle stats (specs/11 section 8): ATK/DEF are multiples of the stage's normal enemy HP
        public double AtkMult { get; internal set; }
        public double DefMult { get; internal set; }
        public double Spd { get; internal set; }

        /// <summary>Every Nth boss action hits the whole party for AoePower x ATK (0 = never).</summary>
        public int AoeEvery { get; internal set; }
        public double AoePower { get; internal set; }
    }

    public sealed class GearSlotDef
    {
        public string Id { get; internal set; }
        public int SortOrder { get; internal set; }
        public string NameKey { get; internal set; }
        public string IconId { get; internal set; }
    }

    public sealed class GearRarityDef
    {
        public Rarity Rarity { get; internal set; }
        public double Weight { get; internal set; }
        public double Mult { get; internal set; }
        public string DisplayColor { get; internal set; }
        public string NameKey { get; internal set; }
    }

    public sealed class HeroTierDef
    {
        public Rarity Tier { get; internal set; }
        public string DisplayColor { get; internal set; }
        public string NameKey { get; internal set; }
        public int ShardValue { get; internal set; }

        /// <summary>Promotion cap for this rarity (specs/11 section 6): C 0, R 1, E 2, L 3, M 3.</summary>
        public int MaxPromotions { get; internal set; }
    }

    /// <summary>Companion role (specs/11 section 4.1). Display only in phase 1.</summary>
    public sealed class RoleDef
    {
        public string Id { get; internal set; }
        public int SortOrder { get; internal set; }
        public string NameKey { get; internal set; }
        public string DescKey { get; internal set; }
    }

    /// <summary>Companion element (specs/11 section 4.2). StrongVs is the element this one beats.</summary>
    public sealed class ElementDef
    {
        public string Id { get; internal set; }
        public int SortOrder { get; internal set; }
        public string NameKey { get; internal set; }
        public string DisplayColor { get; internal set; }
        public string StrongVs { get; internal set; }
    }

    public enum SkillKind { Damage, Heal, Shield, Taunt, Buff, Debuff, Passive }
    public enum SkillTarget { Enemy, AllEnemies, Self, LowestAlly, AllAllies }

    /// <summary>
    /// One skill in a companion's kit (specs/11 section 4.4). Skeleton data: the battle (phase 2) gives it effect.
    /// UnlockPromotion 0 = base kit; k = unlocked by promotion k. Stat/Scales are stat-sheet ids ("atk", "def", ...) or null.
    /// </summary>
    public sealed class SkillDef
    {
        public string Id { get; internal set; }
        public string HeroId { get; internal set; }
        public int Slot { get; internal set; }
        public int UnlockPromotion { get; internal set; }
        public string NameKey { get; internal set; }
        public string DescKey { get; internal set; }
        public SkillKind Kind { get; internal set; }
        public SkillTarget Target { get; internal set; }
        public string Stat { get; internal set; }
        public string Scales { get; internal set; }
        public double Power { get; internal set; }
        public int Cooldown { get; internal set; }
        public int Duration { get; internal set; }
    }

    /// <summary>One promotion step (specs/11 section 6). Index is 1-based.</summary>
    public sealed class PromotionDef
    {
        public int Index { get; internal set; }
        public int UnlockStage { get; internal set; }
        public double Mult { get; internal set; }
        public double CostKills { get; internal set; }
        public string NameKey { get; internal set; }
    }

    public sealed class HeroDef
    {
        public string Id { get; internal set; }
        public Rarity Tier { get; internal set; }
        public string PoolId { get; internal set; }
        public string NameKey { get; internal set; }
        public string DescKey { get; internal set; }
        public string IconId { get; internal set; }
        public string Role { get; internal set; }
        public string Element { get; internal set; }

        // Base stat sheet at level 1, promotion 0 (specs/11 section 3).
        public double Hp { get; internal set; }
        public double Atk { get; internal set; }
        public double Def { get; internal set; }
        public double Spd { get; internal set; }
        public double CritRate { get; internal set; }
        public double CritDmg { get; internal set; }

        /// <summary>The one companion every new profile starts with (specs/11 section 4.5).</summary>
        public bool Starter { get; internal set; }
    }

    public sealed class BannerDef
    {
        public string Id { get; internal set; }
        public BannerKind Kind { get; internal set; }
        public string NameKey { get; internal set; }
        public string PoolId { get; internal set; }

        /// <summary>Indexed by (int)Rarity.</summary>
        public IReadOnlyList<double> Rates { get; internal set; }

        public double BaseLplus { get; internal set; }
        public int PitySoftStart { get; internal set; }
        public double PitySoftStep { get; internal set; }
        public int PityHard { get; internal set; }
        public double MythicShareOfLplus { get; internal set; }
        public int GuaranteeBatchSize { get; internal set; }
        public Rarity GuaranteeMinTier { get; internal set; }
        public int PullCostGems { get; internal set; }
        public int FreePullsPerDay { get; internal set; }
        public string RngStream { get; internal set; }
        public bool Enabled { get; internal set; }

        /// <summary>Hero ids per tier, ordinal-sorted. Indexed by (int)Rarity.</summary>
        public IReadOnlyList<IReadOnlyList<string>> Pool { get; internal set; }

        /// <summary>L+ chance on the n-th pull since the last L+ (specs/01 section 3.1).</summary>
        public double PityRate(int n) =>
            n >= PityHard ? 1.0 : Math.Min(1.0, BaseLplus + PitySoftStep * Math.Max(0, n - PitySoftStart));
    }

    public sealed class DailyQuestDef
    {
        public string Id { get; internal set; }
        public QuestType Type { get; internal set; }
        public int Target { get; internal set; }
        public int RewardGems { get; internal set; }
        public int SortOrder { get; internal set; }
        public string NameKey { get; internal set; }
    }

    public sealed class TutorialGrantDef
    {
        public string Id { get; internal set; }
        public GrantTrigger Trigger { get; internal set; }
        public int? TriggerValue { get; internal set; }
        public GrantType GrantType { get; internal set; }
        public int Amount { get; internal set; }
        public string GrantRef { get; internal set; }
        public int SortOrder { get; internal set; }
    }

    public sealed class ProgressTargetDef
    {
        public int Day { get; internal set; }
        public int Stage { get; internal set; }
    }

    public readonly struct ContentError
    {
        public ContentError(string code, string location, string message)
        {
            Code = code;
            Location = location;
            Message = message;
        }

        public string Code { get; }
        public string Location { get; }
        public string Message { get; }
        public override string ToString() => $"CONTENT {Code} {Location}: {Message}";
    }

    public sealed class ContentLoadException : Exception
    {
        public ContentLoadException(IReadOnlyList<ContentError> errors)
            : base("Content failed to load:\n" + string.Join("\n", errors))
        {
            Errors = errors;
        }

        public IReadOnlyList<ContentError> Errors { get; }
    }

    public interface IContentSource
    {
        bool Exists(string fileName);
        string ReadText(string fileName);
    }

    /// <summary>Harness, tests, editor: reads Content/*.json from a directory.</summary>
    public sealed class FileContentSource : IContentSource
    {
        readonly string _dir;

        public FileContentSource(string directory) { _dir = directory; }

        public bool Exists(string fileName) => System.IO.File.Exists(System.IO.Path.Combine(_dir, fileName));

        public string ReadText(string fileName) =>
            System.IO.File.ReadAllText(System.IO.Path.Combine(_dir, fileName), new System.Text.UTF8Encoding(false));

        /// <summary>Walks up from <paramref name="startDirectory"/> looking for Content/manifest.json. Null if none.</summary>
        public static string FindContentDirectory(string startDirectory)
        {
            var dir = new System.IO.DirectoryInfo(startDirectory);
            while (dir != null)
            {
                string candidate = System.IO.Path.Combine(dir.FullName, "Content");
                if (System.IO.File.Exists(System.IO.Path.Combine(candidate, "manifest.json"))) return candidate;
                dir = dir.Parent;
            }
            return null;
        }
    }

    /// <summary>In-memory source (tests, Unity TextAssets already read into strings).</summary>
    public sealed class DictionaryContentSource : IContentSource
    {
        readonly IReadOnlyDictionary<string, string> _files;

        public DictionaryContentSource(IReadOnlyDictionary<string, string> files) { _files = files; }

        public bool Exists(string fileName) => _files.ContainsKey(fileName);
        public string ReadText(string fileName) => _files[fileName];
    }

    /// <summary>A monster species (specs/14 section 3): element, starting stats and per-stat drill growth multipliers.</summary>
    public sealed class SpeciesDef
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        public string Element { get; internal set; }
        public string NativeTree1 { get; internal set; }
        public string NativeTree2 { get; internal set; }
        public IdleRng.Sim.Combat.MonsterStats Base { get; internal set; }
        public IdleRng.Sim.Combat.MonsterStats Growth { get; internal set; }
    }

    public enum DrillTier { Fail, Success, Super }

    /// <summary>Gain range of one drill result before the species growth multiplier (specs/14 section 4).</summary>
    public sealed class DrillTierDef
    {
        public DrillTier Tier { get; internal set; }
        public int GainMin { get; internal set; }
        public int GainMax { get; internal set; }
        public string NameKey { get; internal set; }
    }

    /// <summary>A fight rank and the opponent fought at it (specs/14 section 5). Index == sort_order.</summary>
    public sealed class FightRankDef
    {
        public string Id { get; internal set; }
        public int SortOrder { get; internal set; }
        public string NameKey { get; internal set; }
        public string Element { get; internal set; }
        public IdleRng.Sim.Combat.MonsterStats Stats { get; internal set; }
    }

    public enum TreeKind { Native, Generic, Chase, Book }
    public enum NodeKind { Growth, Stat, Skill, Trigger, Rule }
    public enum ConditionType { TreeMaxed, NodeOwned, StatAtLeast, TallyAtLeast }

    /// <summary>A skill tree (specs/15 section 2). Nodes are bought in index order; the last node is the chase trait.</summary>
    public sealed class TreeDef
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        public TreeKind Kind { get; internal set; }

        /// <summary>Generic trees: the stat whose milestones offer it; null = any stat.</summary>
        public string OfferStat { get; internal set; }

        /// <summary>Generic trees: the lowest milestone threshold that offers it. 0 for other kinds.</summary>
        public int OfferThreshold { get; internal set; }

        public IReadOnlyList<TreeNodeDef> Nodes { get; internal set; }

        /// <summary>Chase trees: all must hold for the tree to unlock.</summary>
        public IReadOnlyList<ChaseConditionDef> Conditions { get; internal set; }
    }

    public sealed class TreeNodeDef
    {
        public string Id { get; internal set; }
        public string TreeId { get; internal set; }
        public int Index { get; internal set; }
        public int Cost { get; internal set; }
        public NodeKind Kind { get; internal set; }

        /// <summary>Growth and Stat nodes: the trained stat.</summary>
        public string Stat { get; internal set; }

        /// <summary>Growth: added to the growth multiplier. Stat: flat points added.</summary>
        public double Amount { get; internal set; }

        /// <summary>Skill and Trigger nodes.</summary>
        public IdleRng.Sim.Combat.CombatSkill Skill { get; internal set; }
        public IdleRng.Sim.Combat.FightTrigger? Trigger { get; internal set; }

        /// <summary>Rule nodes: a RuleModifiers id.</summary>
        public string RuleId { get; internal set; }
    }

    public sealed class ChaseConditionDef
    {
        public string Id { get; internal set; }
        public string TreeId { get; internal set; }
        public ConditionType Type { get; internal set; }

        /// <summary>Tree id, node id, stat id or tally name, by Type.</summary>
        public string Ref { get; internal set; }
        public double Amount { get; internal set; }
    }

    public sealed class SkillBookDef
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        public string TreeId { get; internal set; }
    }

    public enum TalentEffect { DrillCost, StatFloor, DraftSize, StartSp }

    /// <summary>An account talent (specs/18 section 4). Rank r+1 costs CostBase + CostStep * r shards; the effect is PerRank x rank.</summary>
    public sealed class TalentDef
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        public string DescKey { get; internal set; }
        public TalentEffect Effect { get; internal set; }
        public double PerRank { get; internal set; }
        public int MaxRank { get; internal set; }
        public int CostBase { get; internal set; }
        public int CostStep { get; internal set; }
    }

    /// <summary>A gauntlet enemy (specs/19 section 2): its stats are Weights x the floor's level.</summary>
    public sealed class GauntletEnemyDef
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        public string Element { get; internal set; }
        public bool Boss { get; internal set; }
        public IdleRng.Sim.Combat.MonsterStats Weights { get; internal set; }
    }

    /// <summary>A Legacy Title band (specs/19 section 3). TitleKey's text may contain {killer}.</summary>
    public sealed class LegacyTitleDef
    {
        public int MinFloor { get; internal set; }
        public string TitleKey { get; internal set; }
    }
}
