using System;
using System.Collections.Generic;

namespace IdleRng.Sim.Content
{
    /// <summary>Scalar constants (specs/00 section 2; the constants table, specs/05 section 2). Property name = PascalCase of the content key.</summary>
    public sealed class Constants
    {
        public double SpdRef { get; internal set; }              // SPD_REF: with BATTLE_GAUGE, sets a turn

        // Battle (specs/16)
        public double BattleGauge { get; internal set; }         // BATTLE_GAUGE
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
        public double StatFloorFrac { get; internal set; }       // STAT_FLOOR_FRAC: a trained stat never drops below this x base
        public int EffectStackCap { get; internal set; }         // EFFECT_STACK_CAP: most copies of one timed effect on a unit

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
        public int InjurySkipCycles { get; internal set; }       // INJURY_SKIP_CYCLES: whole cycles lost after the injured one
        public double InjuryRecoverEnergy { get; internal set; } // INJURY_RECOVER_ENERGY: energy on return
        public double StatMax { get; internal set; }             // STAT_MAX
        public int FightEvery { get; internal set; }             // FIGHT_EVERY

        // Skill trees (specs/15 section 9). Placeholders.
        public int SpPerCycle { get; internal set; }             // SP_PER_CYCLE
        public int SpPerWin { get; internal set; }               // SP_PER_WIN
        public int TreeSlots { get; internal set; }              // TREE_SLOTS
        public int DraftSize { get; internal set; }              // DRAFT_SIZE
        public double TreeRefund { get; internal set; }          // TREE_REFUND
        public double ChaseBaseChance { get; internal set; }     // CHASE_BASE_CHANCE
        public double MonEchoPower { get; internal set; }        // MON_ECHO_POWER
        public double MonTideRegen { get; internal set; }        // MON_TIDE_REGEN
        public double MonTideShieldCap { get; internal set; }    // MON_TIDE_SHIELD_CAP
        public double MonDrainFrac { get; internal set; }        // MON_DRAIN_FRAC
        public double MonFirePierce { get; internal set; }       // MON_FIRE_PIERCE
        public double MonRebirthHeal { get; internal set; }      // MON_REBIRTH_HEAL
        public double MonWailStagger { get; internal set; }      // MON_WAIL_STAGGER
        public double MonWailWither { get; internal set; }       // MON_WAIL_WITHER
        public double MonVeilEvadeCap { get; internal set; }     // MON_VEIL_EVADE_CAP
        public double MonFeastStr { get; internal set; }         // MON_FEAST_STR
        public double MonCounterMult { get; internal set; }      // MON_COUNTER_MULT
        public double MonFireBonus { get; internal set; }        // MON_FIRE_BONUS
        public double MonVeilEvadeBonus { get; internal set; }   // MON_VEIL_EVADE_BONUS
        public double MonFeastDmg { get; internal set; }         // MON_FEAST_DMG
        public double MonFeastStart { get; internal set; }       // MON_FEAST_START
        public int MonEchoDecoys { get; internal set; }          // MON_ECHO_DECOYS
        public double MonDrainShieldCap { get; internal set; }   // MON_DRAIN_SHIELD_CAP
        public int MonDoubleTurns { get; internal set; }         // MON_DOUBLE_TURNS

        // Account and meta-progression (specs/18 section 5). Placeholders.
        public int ShardsPerCycle { get; internal set; }         // SHARDS_PER_CYCLE
        public int ShardsPerRank { get; internal set; }          // SHARDS_PER_RANK
        public int VaultCap { get; internal set; }               // VAULT_CAP
        public int VendorOffers { get; internal set; }           // VENDOR_OFFERS: offers the post-run vendor rolls (specs/18 section 4)

        // End-of-run gauntlet (specs/19 section 4). Placeholders.
        public int GauntletFloors { get; internal set; }         // GAUNTLET_FLOORS
        public double GauntletBase { get; internal set; }        // GAUNTLET_BASE
        public double GauntletGrowth { get; internal set; }      // GAUNTLET_GROWTH
        public int GauntletBossEvery { get; internal set; }      // GAUNTLET_BOSS_EVERY
        public double GauntletBossMult { get; internal set; }    // GAUNTLET_BOSS_MULT

        // Dungeon (specs/21 section 5; "tower" in code). Placeholders.
        public int TowerSeed { get; internal set; }              // TOWER_SEED
        public int TowerFloors { get; internal set; }            // TOWER_FLOORS
        public int TowerAttemptsPerDay { get; internal set; }    // TOWER_ATTEMPTS_PER_DAY: free tickets per day
        public int TowerTicketCap { get; internal set; }         // TOWER_TICKET_CAP
        public int TowerTicketPieces { get; internal set; }      // TOWER_TICKET_PIECES
        public double TowerBase { get; internal set; }           // TOWER_BASE
        public double TowerGrowth { get; internal set; }         // TOWER_GROWTH
        public double TowerBossHpMult { get; internal set; }     // TOWER_BOSS_HP_MULT
        public int TowerMilestoneEvery { get; internal set; }    // TOWER_MILESTONE_EVERY
        public int TowerShardsPerFloor { get; internal set; }    // TOWER_SHARDS_PER_FLOOR
        public double TowerBookChance { get; internal set; }     // TOWER_BOOK_CHANCE
        public double TowerBookChancePerFloor { get; internal set; } // TOWER_BOOK_CHANCE_PER_FLOOR
        public double EntropyCap { get; internal set; }          // ENTROPY_CAP
        public double EntropyPerTrigger { get; internal set; }   // ENTROPY_PER_TRIGGER
        public double EntropyPerEcho { get; internal set; }      // ENTROPY_PER_ECHO
        public double EntropyPerCounter { get; internal set; }   // ENTROPY_PER_COUNTER
        public double EntropyDecay { get; internal set; }        // ENTROPY_DECAY
        public double EntropyRecoilFrac { get; internal set; }   // ENTROPY_RECOIL_FRAC
        public double EntropyStunGauge { get; internal set; }    // ENTROPY_STUN_GAUGE
        public double EntropyEnrageFrac { get; internal set; }   // ENTROPY_ENRAGE_FRAC
        public int EntropyEnrageTurns { get; internal set; }     // ENTROPY_ENRAGE_TURNS
        public double EntropyConvertShield { get; internal set; } // ENTROPY_CONVERT_SHIELD
        public int EntropyConvertTurns { get; internal set; }    // ENTROPY_CONVERT_TURNS
        public int CorruptStartTick { get; internal set; }       // CORRUPT_START_TICK
        public int CorruptEveryTicks { get; internal set; }      // CORRUPT_EVERY_TICKS
        public double CorruptHealPerLevel { get; internal set; } // CORRUPT_HEAL_PER_LEVEL
        public double CorruptHealFloor { get; internal set; }    // CORRUPT_HEAL_FLOOR
        public double CorruptDmgPerLevel { get; internal set; }  // CORRUPT_DMG_PER_LEVEL

        /// <summary>Ticks in one "turn" at reference speed: BATTLE_GAUGE / SPD_REF (skill durations are in turns).</summary>
        public int TurnTicks => Math.Max(1, (int)Math.Round(BattleGauge / SpdRef));

    }

    /// <summary>Element (specs/16 section 5.3). StrongVs is the element this one beats.</summary>
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

    /// <summary>In-memory source (tests, the browser's content pack).</summary>
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

        /// <summary>The species' in-run chase tree: unlocks from its own trees and stats (specs/15 section 5.1).</summary>
        public string ChaseTreeRun { get; internal set; }

        /// <summary>The species' book-gated chase tree: its conditions also need a skill book tree (specs/15 section 5.1).</summary>
        public string ChaseTreeBook { get; internal set; }
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

    /// <summary>
    /// A rank of the ladder (specs/14 section 5). Index == sort_order. <see cref="Stats"/> sets the rank's stat budget (their
    /// sum, <see cref="Budget"/>) that every pool monster of the rank spends; they are also the flat opponent of a run started
    /// before the pools (a legacy run, empty RunState.Ladder).
    /// </summary>
    public sealed class FightRankDef
    {
        public string Id { get; internal set; }
        public int SortOrder { get; internal set; }
        public string NameKey { get; internal set; }
        public string Element { get; internal set; }
        public IdleRng.Sim.Combat.MonsterStats Stats { get; internal set; }

        /// <summary>The sum of the eight <see cref="Stats"/>: what each pool monster's stats add up to.</summary>
        public double Budget { get; internal set; }

        /// <summary>The rank's pool of opponents (rank_enemies rows of this rank) in CSV order; never empty in loaded content.</summary>
        public IReadOnlyList<RankEnemyDef> Pool { get; internal set; }

        /// <summary>Training Shards for dismantling a Vault entry of this grade (specs/18 section 7). Placeholder.</summary>
        public int DismantleShards { get; internal set; }

        /// <summary>Dungeon ticket pieces a sealed run of this grade earns (specs/21 section 7). Placeholder.</summary>
        public int TicketPieces { get; internal set; }
    }

    /// <summary>
    /// One monster of a rank's pool (specs/14 section 5): a run rolls one per rank at its start. Its stats spend the rank's
    /// budget in its own shape; its skills are monster_skills ids in AI priority order.
    /// </summary>
    public sealed class RankEnemyDef
    {
        public string Id { get; internal set; }

        /// <summary>The fight_ranks id of the pool it belongs to.</summary>
        public string RankId { get; internal set; }

        /// <summary>Index of that rank in ContentDb.FightRanks.</summary>
        public int RankIndex { get; internal set; }
        public string NameKey { get; internal set; }
        public string Element { get; internal set; }
        public IdleRng.Sim.Combat.MonsterStats Stats { get; internal set; }
        public IReadOnlyList<string> Skills { get; internal set; }

        /// <summary>Relative chance of being rolled within its rank (> 0).</summary>
        public double Weight { get; internal set; }
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

        /// <summary>Chase trees: the species that owns it; only that species' runs unlock it (specs/15 section 5.1). Null otherwise.</summary>
        public string Species { get; internal set; }

        /// <summary>Chase trees: true for the species' book-gated chase tree, false for its in-run one.</summary>
        public bool BookGated { get; internal set; }

        /// <summary>Chase trees: the name of the apex form a monster takes once it owns this tree's capstone (specs/15 section 5.3). Null otherwise.</summary>
        public string ApexNameKey { get; internal set; }
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

    /// <summary>What a talent changes (specs/18 sections 3 and 4); each maps to one RunModifiers field.</summary>
    public enum TalentEffect
    {
        DrillCost, StatFloor, ChaseChance, StartSp, RestEnergy, FailChance, SuperChance, ShardMult, Momentum, LossSp, Specialist,
        TreeSlots, InjuryShield, LastCycleAp, MilestoneMult, DraftRerolls,
    }

    /// <summary>An account talent (specs/18 section 4). Rank r+1 costs CostBase + CostStep * r shards; the effect is PerRank x rank.</summary>
    public sealed class TalentDef
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        public string DescKey { get; internal set; }

        /// <summary>The talent's vendor tier: a <see cref="VendorRarityDef"/> id (specs/18 section 4).</summary>
        public string Rarity { get; internal set; }
        public TalentEffect Effect { get; internal set; }
        public double PerRank { get; internal set; }
        public int MaxRank { get; internal set; }
        public int CostBase { get; internal set; }
        public int CostStep { get; internal set; }
    }

    public enum AchievementKind { RunsFinished, RankReached, StatReached, GauntletFloors }

    /// <summary>
    /// An account achievement (specs/18 section 8): checked when a run is sealed; earning it may unlock a species.
    /// Ref: a fight rank id (RankReached), a stat id or null for any stat (StatReached), null otherwise. Amount: the count, stat
    /// value or floors cleared (0 for RankReached).
    /// </summary>
    public sealed class AchievementDef
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        public AchievementKind Kind { get; internal set; }
        public string Ref { get; internal set; }
        public double Amount { get; internal set; }

        /// <summary>The species earning it unlocks, or null.</summary>
        public string UnlocksSpecies { get; internal set; }
    }

    /// <summary>
    /// A talent tier of the post-run vendor (specs/18 section 4). Every talent has one; the vendor draws a tier by weight, then a
    /// talent of that tier. Its weight at grade index g is max(0, WeightBase + WeightPerRank x g).
    /// </summary>
    public sealed class VendorRarityDef
    {
        public string Id { get; internal set; }
        public int SortOrder { get; internal set; }
        public string NameKey { get; internal set; }
        public double WeightBase { get; internal set; }
        public double WeightPerRank { get; internal set; }

        public double Weight(int gradeIndex) => System.Math.Max(0, WeightBase + WeightPerRank * gradeIndex);
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

    /// <summary>Which Dungeon pool a boss row belongs to (specs/21 section 4).</summary>
    public enum TowerRole { Regular, Marquee, Add }

    /// <summary>A Dungeon boss (specs/21 section 4): stats are Weights x the floor level (hp also x TOWER_BOSS_HP_MULT, not for adds).</summary>
    public sealed class TowerBossDef
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        public string Element { get; internal set; }
        public TowerRole Role { get; internal set; }
        public IdleRng.Sim.Combat.MonsterStats Weights { get; internal set; }

        /// <summary>monster_skills ids, in AI priority order.</summary>
        public IReadOnlyList<string> Skills { get; internal set; }

        /// <summary>Phases in order (highest enter_hp first). Empty for regular bosses unless content gives them some.</summary>
        public IReadOnlyList<BossPhaseDef> Phases { get; internal set; }
    }

    /// <summary>A boss_phases row (specs/21 section 3.2).</summary>
    public sealed class BossPhaseDef
    {
        public string Id { get; internal set; }
        public string BossId { get; internal set; }
        public int Order { get; internal set; }
        public double EnterHp { get; internal set; }
        public string Kind { get; internal set; }
        public double Power { get; internal set; }
        public int Duration { get; internal set; }
        public int Count { get; internal set; }
        public double Guard { get; internal set; }
        public double HealFrac { get; internal set; }
        public double Strike { get; internal set; }
        public double Buff { get; internal set; }

        /// <summary>tower_bosses id (role add) a split phase spawns, or null.</summary>
        public string Adds { get; internal set; }
        public IReadOnlyList<string> AddSkills { get; internal set; }
    }

    /// <summary>A Skill Book the Dungeon can drop from MinFloor on, by weight (specs/21 section 7).</summary>
    public sealed class BookDropDef
    {
        public string BookId { get; internal set; }
        public int MinFloor { get; internal set; }
        public double Weight { get; internal set; }
    }

    /// <summary>A Legacy Title band (specs/19 section 3). TitleKey's text may contain {killer}.</summary>
    public sealed class LegacyTitleDef
    {
        public int MinFloor { get; internal set; }
        public string TitleKey { get; internal set; }
    }
}
