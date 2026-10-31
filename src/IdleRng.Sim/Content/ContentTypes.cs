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
        public int VaultCap { get; internal set; }               // VAULT_CAP
        public int VendorOffers { get; internal set; }           // VENDOR_OFFERS: offers the post-run vendor rolls (specs/18 section 4)

        // End-of-run gauntlet (specs/19 section 4). Placeholders.
        public int GauntletFloors { get; internal set; }         // GAUNTLET_FLOORS
        public double GauntletBase { get; internal set; }        // GAUNTLET_BASE
        public double GauntletGrowth { get; internal set; }      // GAUNTLET_GROWTH
        public int GauntletBossEvery { get; internal set; }      // GAUNTLET_BOSS_EVERY
        public double GauntletBossMult { get; internal set; }    // GAUNTLET_BOSS_MULT

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

    /// <summary>A fight rank and the opponent fought at it (specs/14 section 5). Index == sort_order.</summary>
    public sealed class FightRankDef
    {
        public string Id { get; internal set; }
        public int SortOrder { get; internal set; }
        public string NameKey { get; internal set; }
        public string Element { get; internal set; }
        public IdleRng.Sim.Combat.MonsterStats Stats { get; internal set; }

        /// <summary>Training Shards for dismantling a Vault entry of this grade (specs/18 section 7). Placeholder.</summary>
        public int DismantleShards { get; internal set; }
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
    /// A rarity of the post-run vendor's offers (specs/18 section 4). An offer grants up to Ranks talent ranks for PriceMult x
    /// their normal cost; its weight at grade index g is max(0, WeightBase + WeightPerRank x g).
    /// </summary>
    public sealed class VendorRarityDef
    {
        public string Id { get; internal set; }
        public int SortOrder { get; internal set; }
        public string NameKey { get; internal set; }
        public int Ranks { get; internal set; }
        public double PriceMult { get; internal set; }
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

    /// <summary>A Legacy Title band (specs/19 section 3). TitleKey's text may contain {killer}.</summary>
    public sealed class LegacyTitleDef
    {
        public int MinFloor { get; internal set; }
        public string TitleKey { get; internal set; }
    }
}
