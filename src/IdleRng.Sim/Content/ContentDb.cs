using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IdleRng.Sim.Content
{
    /// <summary>
    /// Immutable content loaded from Content/*.json (specs/05 section 7). Load once, share freely
    /// (read-only, safe across threads). Code never hard-codes a balance number; it reads it here.
    /// </summary>
    public sealed class ContentDb
    {
        public const int SupportedSchemaVersion = 1;

        static readonly string[] TableNames =
        {
            "constants", "elements", "species", "drill_tiers", "fight_ranks", "stat_milestones", "monster_skills", "trees",
            "tree_nodes", "chase_conditions", "skill_books", "talents", "gauntlet_enemies", "legacy_titles",
            "achievements", "vendor_rarities", "tower_bosses", "boss_phases", "book_drops", "rank_enemies",
        };



        ContentDb() { }

        public string Version { get; private set; }
        public string ContentHash { get; private set; }
        public string SourceRevision { get; private set; }

        public Constants Constants { get; private set; }

        /// <summary>Every numeric constant by its content key (MON_DEF_K -> 300): what text templates name (specs/05 section 5).</summary>
        public IReadOnlyDictionary<string, double> ConstantValues { get; private set; }

        public IReadOnlyDictionary<string, ElementDef> Elements { get; private set; }

        /// <summary>Elements in sort_order.</summary>
        public IReadOnlyList<ElementDef> ElementList { get; private set; }

        public IReadOnlyDictionary<string, string> Strings { get; private set; }

        // Training run (specs/14)
        public IReadOnlyDictionary<string, SpeciesDef> Species { get; private set; }

        /// <summary>Species in CSV order.</summary>
        public IReadOnlyList<SpeciesDef> SpeciesList { get; private set; }

        /// <summary>Indexed by (int)DrillTier.</summary>
        public IReadOnlyList<DrillTierDef> DrillTiers { get; private set; }

        /// <summary>Index == sort_order == rank index in a run (0 = lowest).</summary>
        public IReadOnlyList<FightRankDef> FightRanks { get; private set; }

        /// <summary>Every rank pool monster by id (specs/14 section 5); FightRankDef.Pool lists each rank's in CSV order.</summary>
        public IReadOnlyDictionary<string, RankEnemyDef> RankEnemies { get; private set; }

        /// <summary>Stat thresholds in increasing order.</summary>
        public IReadOnlyList<int> StatMilestones { get; private set; }

        // Skill trees (specs/15)
        public IReadOnlyDictionary<string, IdleRng.Sim.Combat.CombatSkill> MonsterSkills { get; private set; }
        public IReadOnlyDictionary<string, TreeDef> Trees { get; private set; }

        /// <summary>Trees in CSV order (drafts and chase checks iterate in this order).</summary>
        public IReadOnlyList<TreeDef> TreeList { get; private set; }
        public IReadOnlyDictionary<string, TreeNodeDef> TreeNodes { get; private set; }
        public IReadOnlyDictionary<string, SkillBookDef> SkillBooks { get; private set; }

        // Account and meta-progression (specs/18)
        public IReadOnlyDictionary<string, TalentDef> Talents { get; private set; }

        /// <summary>Talents in CSV order.</summary>
        public IReadOnlyList<TalentDef> TalentList { get; private set; }

        /// <summary>Account achievements in CSV order (specs/18 section 8).</summary>
        public IReadOnlyList<AchievementDef> AchievementList { get; private set; }

        public IReadOnlyDictionary<string, AchievementDef> Achievements { get; private set; }

        /// <summary>Post-run vendor rarities by sort_order (specs/18 section 4).</summary>
        public IReadOnlyList<VendorRarityDef> VendorRarities { get; private set; }

        /// <summary>The species every account owns from the start: the first in species.csv (specs/18 section 1).</summary>
        public SpeciesDef StarterSpecies => SpeciesList[0];

        // End-of-run gauntlet (specs/19)
        /// <summary>Normal gauntlet enemies in CSV order (floors rotate through them).</summary>
        public IReadOnlyList<GauntletEnemyDef> GauntletEnemies { get; private set; }

        /// <summary>Boss gauntlet enemies in CSV order (boss floors rotate through them).</summary>
        public IReadOnlyList<GauntletEnemyDef> GauntletBosses { get; private set; }

        /// <summary>Legacy titles by increasing MinFloor; the first has MinFloor 1.</summary>
        public IReadOnlyList<LegacyTitleDef> LegacyTitles { get; private set; }

        // Dungeon (specs/21)
        /// <summary>Every tower boss row by id (regular, marquee and add).</summary>
        public IReadOnlyDictionary<string, TowerBossDef> TowerBosses { get; private set; }

        /// <summary>Regular bosses in CSV order (the lineup draws from them).</summary>
        public IReadOnlyList<TowerBossDef> TowerRegulars { get; private set; }

        /// <summary>Marquee bosses in CSV order (milestone floors draw from them).</summary>
        public IReadOnlyList<TowerBossDef> TowerMarquees { get; private set; }

        /// <summary>Skill Book drops in CSV order.</summary>
        public IReadOnlyList<BookDropDef> BookDrops { get; private set; }


        // ---------------------------------------------------------------- derived

        /// <summary>Damage multiplier for an attack of element <paramref name="attacker"/> on <paramref name="defender"/> (null = neutral).</summary>
        public double ElementMult(string attacker, string defender)
        {
            if (attacker == null || defender == null) return 1.0;
            if (Elements.TryGetValue(attacker, out var a) && a.StrongVs == defender) return Constants.ElementStrongMult;
            if (Elements.TryGetValue(defender, out var d) && d.StrongVs == attacker) return Constants.ElementWeakMult;
            return 1.0;
        }

        public string Text(string key) => Strings.TryGetValue(key, out var s) ? s : key;

        /// <summary>True when MAJOR matches. False on malformed input.</summary>
        public bool IsCompatibleWith(string saveContentVersion)
        {
            int? Major(string v)
            {
                if (string.IsNullOrEmpty(v)) return null;
                var parts = v.Split('.');
                if (parts.Length != 2) return null;
                if (!int.TryParse(parts[0], out int a) || !int.TryParse(parts[1], out _)) return null;
                return a;
            }
            var mine = Major(Version);
            var theirs = Major(saveContentVersion);
            return mine.HasValue && theirs.HasValue && mine.Value == theirs.Value;
        }

        // ---------------------------------------------------------------- loading

        public static ContentDb LoadFromDirectory(string directory, bool verifyHashes = true) =>
            Load(new FileContentSource(directory), verifyHashes);

        /// <summary>Throws ContentLoadException listing every problem found.</summary>
        public static ContentDb Load(IContentSource source, bool verifyHashes = true)
        {
            var errors = new List<ContentError>();
            if (!source.Exists("manifest.json"))
                throw new ContentLoadException(new[] { new ContentError("E001", "manifest", "manifest.json not found") });

            Dictionary<string, object> manifest;
            try { manifest = (Dictionary<string, object>)MiniJson.Parse(source.ReadText("manifest.json")); }
            catch (Exception e)
            {
                throw new ContentLoadException(new[] { new ContentError("E010", "manifest", e.Message) });
            }

            var db = new ContentDb();
            var m = new Row(manifest, "manifest");
            if (m.Int("schema_version") != SupportedSchemaVersion)
                errors.Add(new ContentError("E901", "manifest", $"unsupported schema_version {m.Int("schema_version")}"));
            db.Version = m.Str("content_version");
            db.SourceRevision = m.Str("source_revision");
            db.ContentHash = m.Str("content_hash");
            var listed = m.Obj("tables");

            var tables = new Dictionary<string, List<Row>>();
            var allNames = TableNames.Concat(new[] { "strings" });
            foreach (var name in allNames)
            {
                bool optional = name == "strings";
                if (!listed.TryGetValue(name, out var entryObj))
                {
                    if (!optional) errors.Add(new ContentError("E001", name, "table not listed in manifest.json"));
                    continue;
                }
                var entry = new Row((Dictionary<string, object>)entryObj, "manifest." + name);
                string file = entry.Str("file");
                if (!source.Exists(file))
                {
                    errors.Add(new ContentError("E001", name, $"{file} not found"));
                    continue;
                }
                string text = source.ReadText(file);
                if (verifyHashes)
                {
                    string actual = Sha256Hex(Encoding.UTF8.GetBytes(text));
                    if (!string.Equals(actual, entry.Str("sha256"), StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(new ContentError("E900", file, "sha256 does not match manifest (rebuild with tools/content_build.py)"));
                        continue;
                    }
                }
                try
                {
                    var doc = (Dictionary<string, object>)MiniJson.Parse(text);
                    var rows = (List<object>)doc["rows"];
                    tables[name] = rows.Select((r, i) => new Row((Dictionary<string, object>)r, $"{name}[{i}]")).ToList();
                }
                catch (Exception e)
                {
                    errors.Add(new ContentError("E010", file, e.Message));
                }
            }
            if (errors.Count > 0) throw new ContentLoadException(errors);

            try { db.Build(tables, errors); }
            catch (Exception e) when (e is FormatException || e is KeyNotFoundException || e is InvalidCastException)
            {
                errors.Add(new ContentError("E010", "content", e.Message));
            }
            if (errors.Count > 0) throw new ContentLoadException(errors);
            return db;
        }

        void Build(Dictionary<string, List<Row>> t, List<ContentError> errors)
        {
            // constants
            var kv = t["constants"].ToDictionary(r => r.Str("key"), r => r);
            double D(string k) { if (kv.TryGetValue(k, out var r)) return r.Dbl("value"); errors.Add(new ContentError("E040", $"constants[{k}]", "required constant missing")); return 0; }
            int I(string k) { if (kv.TryGetValue(k, out var r)) return r.Int("value"); errors.Add(new ContentError("E040", $"constants[{k}]", "required constant missing")); return 0; }
            Constants = new Constants
            {
                SpdRef = D("SPD_REF"),
                BattleGauge = D("BATTLE_GAUGE"), ElementStrongMult = D("ELEMENT_STRONG_MULT"),
                ElementWeakMult = D("ELEMENT_WEAK_MULT"), BasicAttackPower = D("BASIC_ATTACK_POWER"),
                AiHealBelow = D("AI_HEAL_BELOW"), AiShieldBelow = D("AI_SHIELD_BELOW"),
                MonHpPerPoint = D("MON_HP_PER_POINT"), MonDefK = D("MON_DEF_K"), MonEvadeBase = D("MON_EVADE_BASE"),
                MonEvadePerDex = D("MON_EVADE_PER_DEX"), MonEvadeCap = D("MON_EVADE_CAP"), MonCritBase = D("MON_CRIT_BASE"),
                MonCritPerLuk = D("MON_CRIT_PER_LUK"), MonCritCap = D("MON_CRIT_CAP"), MonCritDmg = D("MON_CRIT_DMG"),
                MonSpdMin = D("MON_SPD_MIN"), MonTimeLimitTicks = I("MON_TIME_LIMIT_TICKS"), MonSideSize = I("MON_SIDE_SIZE"),
                StatFloorFrac = D("STAT_FLOOR_FRAC"), EffectStackCap = I("EFFECT_STACK_CAP"),
                RunCycles = I("RUN_CYCLES"), RunAp = I("RUN_AP"), RunEnergyMax = D("RUN_ENERGY_MAX"),
                RunEnergyStart = D("RUN_ENERGY_START"), DrillEnergyCost = D("DRILL_ENERGY_COST"), RestEnergy = D("REST_ENERGY"),
                DrillFailMin = D("DRILL_FAIL_MIN"), DrillFailMax = D("DRILL_FAIL_MAX"), DrillSuperP = D("DRILL_SUPER_P"),
                InjuryBelow = D("INJURY_BELOW"), InjuryMaxP = D("INJURY_MAX_P"),
                InjurySkipCycles = I("INJURY_SKIP_CYCLES"), InjuryRecoverEnergy = D("INJURY_RECOVER_ENERGY"), StatMax = D("STAT_MAX"), FightEvery = I("FIGHT_EVERY"),
                SpPerCycle = I("SP_PER_CYCLE"), SpPerWin = I("SP_PER_WIN"), TreeSlots = I("TREE_SLOTS"), DraftSize = I("DRAFT_SIZE"), TreeRefund = D("TREE_REFUND"), ChaseBaseChance = D("CHASE_BASE_CHANCE"),
                MonEchoPower = D("MON_ECHO_POWER"), MonTideRegen = D("MON_TIDE_REGEN"), MonTideShieldCap = D("MON_TIDE_SHIELD_CAP"),
                MonDrainFrac = D("MON_DRAIN_FRAC"), MonFirePierce = D("MON_FIRE_PIERCE"), MonDoubleTurns = I("MON_DOUBLE_TURNS"),
                MonRebirthHeal = D("MON_REBIRTH_HEAL"), MonWailStagger = D("MON_WAIL_STAGGER"), MonWailWither = D("MON_WAIL_WITHER"), MonVeilEvadeCap = D("MON_VEIL_EVADE_CAP"),
                MonFeastStr = D("MON_FEAST_STR"), MonCounterMult = D("MON_COUNTER_MULT"), MonFireBonus = D("MON_FIRE_BONUS"),
                MonVeilEvadeBonus = D("MON_VEIL_EVADE_BONUS"), MonFeastDmg = D("MON_FEAST_DMG"), MonFeastStart = D("MON_FEAST_START"), MonEchoDecoys = I("MON_ECHO_DECOYS"),
                MonDrainShieldCap = D("MON_DRAIN_SHIELD_CAP"),
                ShardsPerCycle = I("SHARDS_PER_CYCLE"), ShardsPerRank = I("SHARDS_PER_RANK"),
                VaultCap = I("VAULT_CAP"), VendorOffers = I("VENDOR_OFFERS"),
                GauntletFloors = I("GAUNTLET_FLOORS"), GauntletBase = D("GAUNTLET_BASE"), GauntletGrowth = D("GAUNTLET_GROWTH"),
                GauntletBossEvery = I("GAUNTLET_BOSS_EVERY"), GauntletBossMult = D("GAUNTLET_BOSS_MULT"),
                TowerSeed = I("TOWER_SEED"), TowerFloors = I("TOWER_FLOORS"), TowerAttemptsPerDay = I("TOWER_ATTEMPTS_PER_DAY"),
                TowerTicketCap = I("TOWER_TICKET_CAP"), TowerTicketPieces = I("TOWER_TICKET_PIECES"),
                TowerBase = D("TOWER_BASE"), TowerGrowth = D("TOWER_GROWTH"), TowerBossHpMult = D("TOWER_BOSS_HP_MULT"),
                TowerMilestoneEvery = I("TOWER_MILESTONE_EVERY"), TowerShardsPerFloor = I("TOWER_SHARDS_PER_FLOOR"),
                TowerBookChance = D("TOWER_BOOK_CHANCE"), TowerBookChancePerFloor = D("TOWER_BOOK_CHANCE_PER_FLOOR"),
                EntropyCap = D("ENTROPY_CAP"), EntropyPerTrigger = D("ENTROPY_PER_TRIGGER"), EntropyPerEcho = D("ENTROPY_PER_ECHO"),
                EntropyPerCounter = D("ENTROPY_PER_COUNTER"), EntropyDecay = D("ENTROPY_DECAY"), EntropyRecoilFrac = D("ENTROPY_RECOIL_FRAC"),
                EntropyStunGauge = D("ENTROPY_STUN_GAUGE"), EntropyEnrageFrac = D("ENTROPY_ENRAGE_FRAC"), EntropyEnrageTurns = I("ENTROPY_ENRAGE_TURNS"),
                EntropyConvertShield = D("ENTROPY_CONVERT_SHIELD"), EntropyConvertTurns = I("ENTROPY_CONVERT_TURNS"),
                CorruptStartTick = I("CORRUPT_START_TICK"), CorruptEveryTicks = I("CORRUPT_EVERY_TICKS"),
                CorruptHealPerLevel = D("CORRUPT_HEAL_PER_LEVEL"), CorruptHealFloor = D("CORRUPT_HEAL_FLOOR"),
                CorruptDmgPerLevel = D("CORRUPT_DMG_PER_LEVEL"),
            };
            if (errors.Count > 0) return;
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var r in t["constants"]) if (r.Str("type") != "bool") values[r.Str("key")] = r.Dbl("value");
            ConstantValues = new ReadOnlyDictionary<string, double>(values);

            // elements (specs/16)
            ElementList = t["elements"].Select(r => new ElementDef
            {
                Id = r.Str("id"), SortOrder = r.Int("sort_order"), NameKey = r.Str("name_key"),
                DisplayColor = r.Str("display_color"), StrongVs = r.Str("strong_vs"),
            }).OrderBy(x => x.SortOrder).ToList().AsReadOnly();
            Elements = new ReadOnlyDictionary<string, ElementDef>(ElementList.ToDictionary(x => x.Id, StringComparer.Ordinal));
            for (int i = 0; i < ElementList.Count; i++)
                if (ElementList[i].SortOrder != i) { errors.Add(new ContentError("E047", "elements", "sort_order must be exactly 0..N-1")); break; }
            foreach (var e in ElementList)
                if (!Elements.ContainsKey(e.StrongVs))
                    errors.Add(new ContentError("E023", $"elements[{e.Id}]", $"strong_vs '{e.StrongVs}' is not an element id"));
            if (!(Constants.BattleGauge > 0 && Constants.SpdRef > 0 && Constants.ElementStrongMult > 0 && Constants.ElementWeakMult > 0
                  && Constants.BasicAttackPower > 0))
                errors.Add(new ContentError("E043", "constants", "battle constants must be > 0"));
            var mk = Constants;
            bool In01(double v) => v >= 0 && v <= 1;
            if (!(mk.MonHpPerPoint > 0 && mk.MonDefK > 0 && In01(mk.MonEvadeBase) && mk.MonEvadePerDex >= 0 && In01(mk.MonEvadeCap)
                  && In01(mk.MonCritBase) && mk.MonCritPerLuk >= 0 && In01(mk.MonCritCap) && mk.MonCritDmg >= 1 && mk.MonSpdMin > 0
                  && mk.MonTimeLimitTicks > 0 && mk.MonSideSize > 0 && mk.StatFloorFrac > 0 && mk.StatFloorFrac <= 1 && mk.EffectStackCap >= 1))
                errors.Add(new ContentError("E043", "constants", "monster battle constants out of range (specs/16 section 7)"));

            BuildTraining(t, errors);

            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            if (t.TryGetValue("strings", out var srows))
                foreach (var r in srows) strings[r.Str("key")] = r.Str("text");
            Strings = new ReadOnlyDictionary<string, string>(strings);
        }

        static IdleRng.Sim.Combat.MonsterStats StatsFrom(Row r, string prefix) => new IdleRng.Sim.Combat.MonsterStats
        {
            Hp = r.Dbl(prefix + "hp"), Str = r.Dbl(prefix + "str"), Int = r.Dbl(prefix + "int"), Wis = r.Dbl(prefix + "wis"),
            Dex = r.Dbl(prefix + "dex"), Def = r.Dbl(prefix + "def"), Spd = r.Dbl(prefix + "spd"), Luk = r.Dbl(prefix + "luk"),
        };

        /// <summary>Training run tables and constants (specs/14 section 8). Mirrors validate_training in tools/content_build.py.</summary>
        void BuildTraining(Dictionary<string, List<Row>> t, List<ContentError> errors)
        {
            var k = Constants;
            bool In01(double v) => v >= 0 && v <= 1;
            if (!(k.RunCycles > 0 && k.RunAp > 0 && k.RunEnergyMax > 0 && k.RunEnergyStart >= 0 && k.RunEnergyStart <= k.RunEnergyMax
                  && k.DrillEnergyCost >= 0 && k.RestEnergy >= 0 && In01(k.DrillFailMin) && In01(k.DrillFailMax)
                  && k.DrillFailMin <= k.DrillFailMax && In01(k.DrillSuperP) && k.DrillFailMax + k.DrillSuperP <= 1
                  && In01(k.InjuryBelow) && In01(k.InjuryMaxP) && k.InjurySkipCycles >= 0
                  && k.InjuryRecoverEnergy >= 0 && k.InjuryRecoverEnergy <= k.RunEnergyMax && k.StatMax > 0 && k.FightEvery > 0
                  && k.SpPerCycle >= 0 && k.SpPerWin >= 0 && k.TreeSlots >= 2 && k.DraftSize > 0 && In01(k.TreeRefund) && In01(k.ChaseBaseChance) && In01(k.MonEchoPower)
                  && In01(k.MonTideRegen) && In01(k.MonTideShieldCap) && k.MonDrainFrac >= 0 && In01(k.MonFirePierce) && k.MonDoubleTurns >= 1
                  && k.MonRebirthHeal > 0 && k.MonRebirthHeal <= 1 && In01(k.MonWailStagger) && In01(k.MonWailWither) && In01(k.MonVeilEvadeCap) && k.MonFeastStr >= 0
                  && k.MonCounterMult >= 0 && k.MonFireBonus >= 0 && In01(k.MonVeilEvadeBonus)
                  && k.MonFeastDmg >= 0 && k.MonFeastStart >= 0 && k.MonEchoDecoys >= 0 && In01(k.MonDrainShieldCap)
                  && k.ShardsPerCycle >= 0 && k.ShardsPerRank >= 0 && k.VaultCap > 0 && k.VendorOffers > 0 && k.GauntletFloors > 0 && k.GauntletBase > 0
                  && k.GauntletGrowth >= 1 && k.GauntletBossEvery > 0 && k.GauntletBossMult > 0))
                errors.Add(new ContentError("E043", "constants", "training run constants out of range (specs/14 section 8)"));

            var trained = IdleRng.Sim.Combat.StatIds.Trained;
            bool StatsOk(IdleRng.Sim.Combat.MonsterStats s, double lo, double hi, bool loExclusive)
            {
                foreach (var id in trained)
                {
                    double v = s.Get(id);
                    if (loExclusive ? !(v > lo) : !(v >= lo)) return false;
                    if (!(v <= hi)) return false;
                }
                return true;
            }

            SpeciesList = t["species"].Select(r => new SpeciesDef
            {
                Id = r.Str("id"), NameKey = r.Str("name_key"), Element = r.Str("element"),
                NativeTree1 = r.Str("native_tree_1"), NativeTree2 = r.Str("native_tree_2"),
                ChaseTreeRun = r.Str("chase_tree_run"), ChaseTreeBook = r.Str("chase_tree_book"),
                Base = StatsFrom(r, "base_"), Growth = StatsFrom(r, "grow_"),
            }).ToList().AsReadOnly();
            Species = new ReadOnlyDictionary<string, SpeciesDef>(SpeciesList.ToDictionary(x => x.Id, StringComparer.Ordinal));
            if (SpeciesList.Count == 0) errors.Add(new ContentError("E044", "species", "at least one species is required"));
            foreach (var sp in SpeciesList)
            {
                if (!Elements.ContainsKey(sp.Element)) errors.Add(new ContentError("E023", $"species[{sp.Id}]", $"element '{sp.Element}' is not an element id"));
                if (!StatsOk(sp.Base, 0, k.StatMax, false)) errors.Add(new ContentError("E043", $"species[{sp.Id}]", "base stats must be in [0, STAT_MAX]"));
                if (!StatsOk(sp.Growth, 0, double.MaxValue, true)) errors.Add(new ContentError("E043", $"species[{sp.Id}]", "growth must be > 0"));
            }

            var tiers = new DrillTierDef[3];
            foreach (var r in t["drill_tiers"])
            {
                DrillTier tier;
                switch (r.Str("id"))
                {
                    case "fail": tier = DrillTier.Fail; break;
                    case "success": tier = DrillTier.Success; break;
                    case "super": tier = DrillTier.Super; break;
                    default: errors.Add(new ContentError("E044", $"drill_tiers[{r.Str("id")}]", "id must be fail, success or super")); continue;
                }
                if (tiers[(int)tier] != null) { errors.Add(new ContentError("E020", $"drill_tiers[{r.Str("id")}]", "duplicate key")); continue; }
                var d = new DrillTierDef { Tier = tier, GainMin = r.Int("gain_min"), GainMax = r.Int("gain_max"), NameKey = r.Str("name_key") };
                if (!(d.GainMin >= 0 && d.GainMin <= d.GainMax)) errors.Add(new ContentError("E043", $"drill_tiers[{r.Str("id")}]", "need 0 <= gain_min <= gain_max"));
                tiers[(int)tier] = d;
            }
            if (tiers.Any(x => x == null)) errors.Add(new ContentError("E044", "drill_tiers", "must list fail, success, super exactly once"));
            DrillTiers = Array.AsReadOnly(tiers);

            FightRanks = t["fight_ranks"].Select(r => new FightRankDef
            {
                Id = r.Str("id"), SortOrder = r.Int("sort_order"), NameKey = r.Str("name_key"), Element = r.Str("element"),
                Stats = StatsFrom(r, ""), DismantleShards = r.Int("dismantle_shards"),
                TicketPieces = r.Int("ticket_pieces"),
            }).OrderBy(x => x.SortOrder).ToList().AsReadOnly();
            if (FightRanks.Count == 0) errors.Add(new ContentError("E044", "fight_ranks", "at least one rank is required"));
            if (!FightRanks.Select(x => x.SortOrder).SequenceEqual(Enumerable.Range(0, FightRanks.Count)))
                errors.Add(new ContentError("E047", "fight_ranks", "sort_order must be exactly 0..N-1"));
            foreach (var fr in FightRanks)
            {
                if (!Elements.ContainsKey(fr.Element)) errors.Add(new ContentError("E023", $"fight_ranks[{fr.Id}]", $"element '{fr.Element}' is not an element id"));
                if (!StatsOk(fr.Stats, 0, k.StatMax, false)) errors.Add(new ContentError("E043", $"fight_ranks[{fr.Id}]", "stats must be in [0, STAT_MAX]"));
                if (fr.DismantleShards < 0) errors.Add(new ContentError("E043", $"fight_ranks[{fr.Id}]", "dismantle_shards must be >= 0"));
                if (fr.TicketPieces < 0) errors.Add(new ContentError("E043", $"fight_ranks[{fr.Id}]", "ticket_pieces must be >= 0"));
                fr.Budget = StatSum(fr.Stats);
            }

            BuildTrees(t, errors);
            BuildRankEnemies(t, errors);
            BuildAccount(t, errors);
            BuildUnlocks(t, errors);
            BuildGauntlet(t, errors);
            BuildTower(t, errors);

            StatMilestones = t["stat_milestones"].Select(r => r.Int("threshold")).ToList().AsReadOnly();
            for (int i = 0; i < StatMilestones.Count; i++)
                if (StatMilestones[i] <= 0 || (i > 0 && StatMilestones[i] <= StatMilestones[i - 1]))
                    errors.Add(new ContentError("E032", $"stat_milestones[{StatMilestones[i]}]", "thresholds must be > 0 and strictly increasing"));
        }

        static readonly string[] Tallies = { "damage_dealt", "evades", "crits", "kills", "wins" };

        /// <summary>Skill trees (specs/15 section 9). Mirrors validate_trees in tools/content_build.py.</summary>
        void BuildTrees(Dictionary<string, List<Row>> t, List<ContentError> errors)
        {
            bool Trained(string s) => IdleRng.Sim.Combat.StatIds.IsTrained(s);

            var skills = new Dictionary<string, IdleRng.Sim.Combat.CombatSkill>(StringComparer.Ordinal);
            foreach (var r in t["monster_skills"])
            {
                var sk = new IdleRng.Sim.Combat.CombatSkill
                {
                    Id = r.Str("id"), Kind = ParseSkillKind(r.Str("kind")), Target = ParseSkillTarget(r.Str("target")),
                    Stat = r.NStr("stat"), Scales = r.NStr("scales"), Power = r.Dbl("power"), Cooldown = r.Int("cooldown"),
                    Duration = r.Int("duration"), Element = r.NStr("element"),
                };
                string loc = $"monster_skills[{sk.Id}]";
                bool needsStat = sk.Kind == SkillKind.Buff || sk.Kind == SkillKind.Debuff || sk.Kind == SkillKind.Passive;
                if (needsStat && !IdleRng.Sim.Combat.StatIds.IsEffectStat(sk.Stat)) errors.Add(new ContentError("E023", loc, $"stat '{sk.Stat}' is not an effect stat"));
                bool needsScales = sk.Kind == SkillKind.Damage || sk.Kind == SkillKind.Heal || sk.Kind == SkillKind.Shield;
                if (needsScales && !Trained(sk.Scales)) errors.Add(new ContentError("E023", loc, $"scales '{sk.Scales}' is not a trained stat"));
                if (!(sk.Power >= 0) || sk.Cooldown < 0 || sk.Duration < 0) errors.Add(new ContentError("E043", loc, "power, cooldown and duration must be >= 0"));
                if (sk.Element != null && !Elements.ContainsKey(sk.Element)) errors.Add(new ContentError("E023", loc, $"element '{sk.Element}' is not an element id"));
                skills[sk.Id] = sk;
            }
            MonsterSkills = new ReadOnlyDictionary<string, IdleRng.Sim.Combat.CombatSkill>(skills);

            var nodesByTree = new Dictionary<string, List<TreeNodeDef>>(StringComparer.Ordinal);
            var allNodes = new Dictionary<string, TreeNodeDef>(StringComparer.Ordinal);
            var treeIds = new HashSet<string>(t["trees"].Select(r => r.Str("id")), StringComparer.Ordinal);
            var hasThreshold = new HashSet<string>(t["trees"].Where(r => r.NInt("offer_threshold") != null).Select(r => r.Str("id")), StringComparer.Ordinal);
            foreach (var r in t["tree_nodes"])
            {
                var n = new TreeNodeDef
                {
                    Id = r.Str("id"), TreeId = r.Str("tree_id"), Index = r.Int("index"), Cost = r.Int("cost"),
                    Kind = ParseNodeKind(r.Str("kind")), Stat = r.NStr("stat"), Amount = r.NDbl("amount") ?? 0,
                    RuleId = r.NStr("rule_id"),
                };
                string loc = $"tree_nodes[{n.Id}]";
                string skillId = r.NStr("skill_id"), trig = r.NStr("trigger");
                if (skillId != null && skills.TryGetValue(skillId, out var nsk)) n.Skill = nsk;
                if (trig != null) n.Trigger = ParseFightTrigger(trig);
                allNodes[n.Id] = n;
                if (!treeIds.Contains(n.TreeId)) { errors.Add(new ContentError("E023", loc, $"tree_id '{n.TreeId}' is not a tree id")); continue; }
                if (!nodesByTree.TryGetValue(n.TreeId, out var list)) nodesByTree[n.TreeId] = list = new List<TreeNodeDef>();
                list.Add(n);
                if (n.Cost < 1) errors.Add(new ContentError("E043", loc, "cost must be >= 1"));
                if (n.Kind == NodeKind.Growth || n.Kind == NodeKind.Stat)
                {
                    if (!Trained(n.Stat)) errors.Add(new ContentError("E023", loc, $"stat '{n.Stat}' is not a trained stat"));
                    if (!(n.Amount > 0)) errors.Add(new ContentError("E043", loc, "amount must be > 0"));
                }
                if ((n.Kind == NodeKind.Skill || n.Kind == NodeKind.Trigger) && n.Skill == null)
                    errors.Add(new ContentError("E023", loc, $"skill_id '{skillId}' is not a monster skill id"));
                if (n.Kind == NodeKind.Trigger)
                {
                    if (n.Trigger == null) errors.Add(new ContentError("E043", loc, "a trigger node needs a trigger"));
                    else if (n.Skill != null && n.Skill.Kind == SkillKind.Passive) errors.Add(new ContentError("E043", loc, "a trigger node cannot fire a passive"));
                }
                if (n.Kind == NodeKind.Rule && !IdleRng.Sim.Combat.RuleModifiers.IsKnown(n.RuleId))
                    errors.Add(new ContentError("E023", loc, $"rule_id '{n.RuleId}' is not a known rule modifier"));
                if (n.Kind == NodeKind.Rule && n.RuleId == IdleRng.Sim.Combat.RuleModifiers.FirePierce
                    && !Elements.ContainsKey(IdleRng.Sim.Combat.RuleModifiers.FireElement))
                    errors.Add(new ContentError("E023", loc, "fire_pierce works on the 'fire' element, which is not an element id"));
            }
            TreeNodes = new ReadOnlyDictionary<string, TreeNodeDef>(allNodes);

            var condsByTree = new Dictionary<string, List<ChaseConditionDef>>(StringComparer.Ordinal);
            foreach (var r in t["chase_conditions"])
            {
                var cd = new ChaseConditionDef
                {
                    Id = r.Str("id"), TreeId = r.Str("tree_id"), Type = ParseConditionType(r.Str("type")), Ref = r.Str("ref"),
                    Amount = r.NDbl("amount") ?? 0,
                };
                if (!condsByTree.TryGetValue(cd.TreeId, out var list)) condsByTree[cd.TreeId] = list = new List<ChaseConditionDef>();
                list.Add(cd);
            }

            TreeList = t["trees"].Select(r =>
            {
                string id = r.Str("id");
                nodesByTree.TryGetValue(id, out var ns);
                condsByTree.TryGetValue(id, out var cs);
                return new TreeDef
                {
                    Id = id, NameKey = r.Str("name_key"), Kind = ParseTreeKind(r.Str("kind")), OfferStat = r.NStr("offer_stat"),
                    OfferThreshold = r.NInt("offer_threshold") ?? 0, ApexNameKey = r.NStr("apex_name_key"),
                    Nodes = (ns ?? new List<TreeNodeDef>()).OrderBy(n => n.Index).ToList().AsReadOnly(),
                    Conditions = (cs ?? new List<ChaseConditionDef>()).AsReadOnly(),
                };
            }).ToList().AsReadOnly();
            Trees = new ReadOnlyDictionary<string, TreeDef>(TreeList.ToDictionary(x => x.Id, StringComparer.Ordinal));

            foreach (var tr in TreeList)
            {
                string loc = $"trees[{tr.Id}]";
                if (tr.Kind == TreeKind.Generic)
                {
                    if (tr.OfferThreshold <= 0) errors.Add(new ContentError("E043", loc, "a generic tree needs offer_threshold > 0"));
                    if (tr.OfferStat != null && !Trained(tr.OfferStat)) errors.Add(new ContentError("E023", loc, $"offer_stat '{tr.OfferStat}' is not a trained stat"));
                }
                else if (tr.OfferStat != null || hasThreshold.Contains(tr.Id))
                    errors.Add(new ContentError("E043", loc, "only generic trees have offer_stat and offer_threshold"));
                if ((tr.Kind == TreeKind.Chase) != (tr.ApexNameKey != null))
                    errors.Add(new ContentError("E043", loc, "chase trees, and only chase trees, have an apex_name_key"));
                if (tr.Nodes.Count < 2) { errors.Add(new ContentError("E044", loc, "a tree needs at least 2 nodes")); continue; }
                for (int i = 0; i < tr.Nodes.Count; i++)
                    if (tr.Nodes[i].Index != i) { errors.Add(new ContentError("E060", loc, "node index must be 0..N-1 contiguous")); break; }
                var last = tr.Nodes[tr.Nodes.Count - 1].Kind;
                if (last != NodeKind.Skill && last != NodeKind.Trigger && last != NodeKind.Rule)
                    errors.Add(new ContentError("E064", loc, "the last node is the chase trait: it must be a skill, trigger or rule node"));
                if (tr.Kind == TreeKind.Chase && tr.Conditions.Count == 0)
                    errors.Add(new ContentError("E065", loc, "a chase tree needs at least one condition"));
            }

            foreach (var kv in condsByTree)
                foreach (var cd in kv.Value)
                {
                    string loc = $"chase_conditions[{cd.Id}]";
                    if (!Trees.TryGetValue(cd.TreeId, out var own) || own.Kind != TreeKind.Chase)
                    {
                        errors.Add(new ContentError("E023", loc, $"tree_id '{cd.TreeId}' is not a chase tree id"));
                        continue;
                    }
                    switch (cd.Type)
                    {
                        case ConditionType.TreeMaxed:
                            if (!Trees.ContainsKey(cd.Ref) || cd.Ref == cd.TreeId) errors.Add(new ContentError("E023", loc, $"ref '{cd.Ref}' is not another tree id"));
                            break;
                        case ConditionType.NodeOwned:
                            if (!allNodes.TryGetValue(cd.Ref, out var rn) || rn.TreeId == cd.TreeId) errors.Add(new ContentError("E023", loc, $"ref '{cd.Ref}' is not a node of another tree"));
                            break;
                        case ConditionType.StatAtLeast:
                            if (!Trained(cd.Ref)) errors.Add(new ContentError("E023", loc, $"ref '{cd.Ref}' is not a trained stat"));
                            if (!(cd.Amount >= 0)) errors.Add(new ContentError("E043", loc, "amount must be >= 0"));
                            break;
                        case ConditionType.TallyAtLeast:
                            bool ok = Array.IndexOf(Tallies, cd.Ref) >= 0 || (cd.Ref.StartsWith("damage_", StringComparison.Ordinal) && Elements.ContainsKey(cd.Ref.Substring(7)));
                            if (!ok) errors.Add(new ContentError("E023", loc, $"ref '{cd.Ref}' is not a tally"));
                            if (!(cd.Amount >= 0)) errors.Add(new ContentError("E043", loc, "amount must be >= 0"));
                            break;
                    }
                }

            foreach (var sp in SpeciesList)
            {
                foreach (var nt in new[] { sp.NativeTree1, sp.NativeTree2 })
                    if (!Trees.TryGetValue(nt, out var tr) || tr.Kind != TreeKind.Native)
                        errors.Add(new ContentError("E023", $"species[{sp.Id}]", $"'{nt}' is not a native tree id"));
                if (sp.NativeTree1 == sp.NativeTree2) errors.Add(new ContentError("E043", $"species[{sp.Id}]", "native trees must differ"));

                // Each species owns two chase trees: one in-run, one book-gated (specs/15 section 5.1).
                foreach (var (col, id, book) in new[] { ("chase_tree_run", sp.ChaseTreeRun, false), ("chase_tree_book", sp.ChaseTreeBook, true) })
                {
                    if (!Trees.TryGetValue(id, out var ct) || ct.Kind != TreeKind.Chase)
                    {
                        errors.Add(new ContentError("E023", $"species[{sp.Id}]", $"{col} '{id}' is not a chase tree id"));
                        continue;
                    }
                    if (ct.Species != null) errors.Add(new ContentError("E043", $"species[{sp.Id}]", $"chase tree '{id}' already belongs to species '{ct.Species}'"));
                    else { ct.Species = sp.Id; ct.BookGated = book; }
                    int needs = BookTreesNeeded(ct).Count();
                    if (!book && needs > 0) errors.Add(new ContentError("E043", $"species[{sp.Id}]", $"in-run chase tree '{id}' must not need a book tree"));
                    if (book && needs == 0) errors.Add(new ContentError("E043", $"species[{sp.Id}]", $"book-gated chase tree '{id}' needs a condition on a book tree"));
                }
                if (sp.ChaseTreeRun == sp.ChaseTreeBook) errors.Add(new ContentError("E043", $"species[{sp.Id}]", "chase trees must differ"));
            }
            foreach (var tr in TreeList)
                if (tr.Kind == TreeKind.Chase && tr.Species == null)
                    errors.Add(new ContentError("E043", $"trees[{tr.Id}]", "a chase tree must belong to a species (chase_tree_run or chase_tree_book)"));

            SkillBooks = new ReadOnlyDictionary<string, SkillBookDef>(t["skill_books"].Select(r => new SkillBookDef
            {
                Id = r.Str("id"), NameKey = r.Str("name_key"), TreeId = r.Str("tree_id"),
            }).ToDictionary(b => b.Id, StringComparer.Ordinal));
            foreach (var b in SkillBooks.Values)
                if (!Trees.TryGetValue(b.TreeId, out var bt) || bt.Kind != TreeKind.Book)
                    errors.Add(new ContentError("E023", $"skill_books[{b.Id}]", $"tree_id '{b.TreeId}' is not a book tree id"));
        }

        /// <summary>Account talent tree (specs/18 section 4). Mirrors validate_talents in tools/content_build.py.</summary>
        void BuildAccount(Dictionary<string, List<Row>> t, List<ContentError> errors)
        {
            TalentList = t["talents"].Select(r => new TalentDef
            {
                Id = r.Str("id"), NameKey = r.Str("name_key"), DescKey = r.Str("desc_key"), Rarity = r.Str("rarity"), Effect = ParseTalentEffect(r.Str("effect")),
                PerRank = r.Dbl("per_rank"), MaxRank = r.Int("max_rank"), CostBase = r.Int("cost_base"), CostStep = r.Int("cost_step"),
            }).ToList().AsReadOnly();
            Talents = new ReadOnlyDictionary<string, TalentDef>(TalentList.ToDictionary(x => x.Id, StringComparer.Ordinal));
            foreach (var d in TalentList)
            {
                string loc = $"talents[{d.Id}]";
                if (d.MaxRank < 1 || d.CostBase < 0 || d.CostStep < 0)
                    errors.Add(new ContentError("E043", loc, "need max_rank >= 1, cost_base >= 0, cost_step >= 0"));
                double p = d.PerRank;
                bool ok;
                switch (d.Effect)
                {
                    case TalentEffect.DrillCost: ok = p <= 0 && 1 + p * d.MaxRank >= 0; break;
                    case TalentEffect.StatFloor: ok = p >= 0; break;
                    case TalentEffect.ChaseChance: ok = p >= 0 && Constants.ChaseBaseChance + p * d.MaxRank <= 1; break;
                    default: ok = p >= 0 && p == Math.Floor(p); break;
                }
                if (!ok) errors.Add(new ContentError("E043", loc, $"per_rank {p} out of range for {d.Effect}"));
            }
        }

        /// <summary>Achievements and the post-run vendor (specs/18 sections 4 and 8). Mirrors validate_unlocks in tools/content_build.py.</summary>
        void BuildUnlocks(Dictionary<string, List<Row>> t, List<ContentError> errors)
        {
            var trained = new HashSet<string>(IdleRng.Sim.Combat.StatIds.Trained, StringComparer.Ordinal);
            var unlocked = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<AchievementDef>();
            foreach (var r in t["achievements"])
            {
                double? amount = r.NDbl("amount");
                var d = new AchievementDef
                {
                    Id = r.Str("id"), NameKey = r.Str("name_key"), Kind = ParseAchievementKind(r.Str("kind")), Ref = r.NStr("ref"),
                    Amount = amount ?? 0, UnlocksSpecies = r.NStr("unlocks_species"),
                };
                list.Add(d);
                string loc = $"achievements[{d.Id}]";
                if (d.Kind == AchievementKind.RankReached)
                {
                    if (!FightRanks.Any(x => x.Id == d.Ref)) errors.Add(new ContentError("E023", loc, $"ref '{d.Ref}' is not a fight rank id"));
                    if (amount.HasValue) errors.Add(new ContentError("E043", loc, "rank_reached takes no amount"));
                }
                else
                {
                    if (d.Kind == AchievementKind.StatReached)
                    {
                        if (d.Ref != null && !trained.Contains(d.Ref)) errors.Add(new ContentError("E023", loc, $"ref '{d.Ref}' is not a trained stat (or empty for any stat)"));
                    }
                    else if (d.Ref != null) errors.Add(new ContentError("E043", loc, $"{d.Kind} takes no ref"));
                    if (!amount.HasValue || !(d.Amount > 0) || (d.Kind != AchievementKind.StatReached && d.Amount != Math.Floor(d.Amount)))
                        errors.Add(new ContentError("E043", loc, $"{d.Kind} needs an amount > 0"));
                }
                if (d.UnlocksSpecies != null)
                {
                    if (!Species.ContainsKey(d.UnlocksSpecies)) errors.Add(new ContentError("E023", loc, $"unlocks_species '{d.UnlocksSpecies}' is not a species id"));
                    else if (SpeciesList.Count > 0 && d.UnlocksSpecies == SpeciesList[0].Id)
                        errors.Add(new ContentError("E043", loc, $"'{d.UnlocksSpecies}' is the starter species: every account owns it"));
                    unlocked.Add(d.UnlocksSpecies);
                }
            }
            AchievementList = list.AsReadOnly();
            Achievements = new ReadOnlyDictionary<string, AchievementDef>(AchievementList.ToDictionary(x => x.Id, StringComparer.Ordinal));
            foreach (var sp in SpeciesList.Skip(1))
                if (!unlocked.Contains(sp.Id))
                    errors.Add(new ContentError("E044", $"species[{sp.Id}]", "only the first species is owned from the start: every other species needs an achievement that unlocks it"));

            VendorRarities = t["vendor_rarities"].Select(r => new VendorRarityDef
            {
                Id = r.Str("id"), SortOrder = r.Int("sort_order"), NameKey = r.Str("name_key"),
                WeightBase = r.Dbl("weight_base"), WeightPerRank = r.Dbl("weight_per_rank"),
            }).OrderBy(x => x.SortOrder).ToList().AsReadOnly();
            if (VendorRarities.Count == 0) errors.Add(new ContentError("E044", "vendor_rarities", "at least one rarity is required"));
            if (!VendorRarities.Select(x => x.SortOrder).SequenceEqual(Enumerable.Range(0, VendorRarities.Count)))
                errors.Add(new ContentError("E047", "vendor_rarities", "sort_order must be exactly 0..N-1"));
            foreach (var d in TalentList)
                if (!VendorRarities.Any(r => r.Id == d.Rarity))
                    errors.Add(new ContentError("E023", $"talents[{d.Id}]", $"rarity '{d.Rarity}' is not a vendor_rarities id"));
            for (int g = 0; g < FightRanks.Count && VendorRarities.Count > 0; g++)
                if (!(VendorRarities.Sum(r => r.Weight(g)) > 0))
                    errors.Add(new ContentError("E043", "vendor_rarities", $"the weights must not all be 0 at grade index {g}"));
        }

        static AchievementKind ParseAchievementKind(string s)
        {
            switch (s)
            {
                case "runs_finished": return AchievementKind.RunsFinished;
                case "rank_reached": return AchievementKind.RankReached;
                case "stat_reached": return AchievementKind.StatReached;
                case "gauntlet_floors": return AchievementKind.GauntletFloors;
                default: throw new FormatException($"unknown achievement kind '{s}'");
            }
        }

        /// <summary>End-of-run gauntlet (specs/19 section 4). Mirrors validate_gauntlet in tools/content_build.py.</summary>
        void BuildGauntlet(Dictionary<string, List<Row>> t, List<ContentError> errors)
        {
            var all = t["gauntlet_enemies"].Select(r => new GauntletEnemyDef
            {
                Id = r.Str("id"), NameKey = r.Str("name_key"), Element = r.Str("element"), Boss = r.Bool("boss"), Weights = StatsFrom(r, ""),
            }).ToList();
            GauntletEnemies = all.Where(e => !e.Boss).ToList().AsReadOnly();
            GauntletBosses = all.Where(e => e.Boss).ToList().AsReadOnly();
            if (GauntletEnemies.Count == 0 || GauntletBosses.Count == 0)
                errors.Add(new ContentError("E044", "gauntlet_enemies", "need at least one normal enemy and one boss"));
            foreach (var e in all)
            {
                if (!Elements.ContainsKey(e.Element)) errors.Add(new ContentError("E023", $"gauntlet_enemies[{e.Id}]", $"element '{e.Element}' is not an element id"));
                foreach (var id in IdleRng.Sim.Combat.StatIds.Trained)
                    if (!(e.Weights.Get(id) >= 0)) { errors.Add(new ContentError("E043", $"gauntlet_enemies[{e.Id}]", "stat weights must be >= 0")); break; }
            }
            LegacyTitles = t["legacy_titles"].Select(r => new LegacyTitleDef { MinFloor = r.Int("min_floor"), TitleKey = r.Str("title_key") })
                .ToList().AsReadOnly();
            if (LegacyTitles.Count == 0 || LegacyTitles[0].MinFloor != 1)
                errors.Add(new ContentError("E045", "legacy_titles", "first row must have min_floor = 1"));
            for (int i = 1; i < LegacyTitles.Count; i++)
                if (LegacyTitles[i].MinFloor <= LegacyTitles[i - 1].MinFloor)
                    errors.Add(new ContentError("E032", $"legacy_titles[{LegacyTitles[i].MinFloor}]", "min_floor must strictly increase"));
        }

        static double StatSum(IdleRng.Sim.Combat.MonsterStats m)
        {
            double sum = 0;
            foreach (var id in IdleRng.Sim.Combat.StatIds.Trained) sum += m.Get(id);
            return sum;
        }

        /// <summary>
        /// Rank fight pools (specs/14 section 5): every pool monster names a rank, spends exactly its budget, uses known elements
        /// and skills and has a weight > 0; every rank has at least one. Mirrors validate_rank_enemies in tools/content_build.py.
        /// Needs FightRanks and MonsterSkills.
        /// </summary>
        void BuildRankEnemies(Dictionary<string, List<Row>> t, List<ContentError> errors)
        {
            var k = Constants;
            var rankIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < FightRanks.Count; i++) rankIndex[FightRanks[i].Id] = i;
            var pools = FightRanks.Select(_ => new List<RankEnemyDef>()).ToList();
            var byId = new Dictionary<string, RankEnemyDef>(StringComparer.Ordinal);
            foreach (var r in t["rank_enemies"])
            {
                var e = new RankEnemyDef
                {
                    Id = r.Str("id"), RankId = r.Str("rank"), NameKey = r.Str("name_key"), Element = r.Str("element"),
                    Stats = StatsFrom(r, ""), Skills = SplitIds(r.NStr("skills")).AsReadOnly(), Weight = r.Dbl("weight"), RankIndex = -1,
                };
                string loc = $"rank_enemies[{e.Id}]";
                if (byId.ContainsKey(e.Id)) { errors.Add(new ContentError("E020", loc, "duplicate key")); continue; }
                byId[e.Id] = e;
                if (!rankIndex.TryGetValue(e.RankId, out int ri)) errors.Add(new ContentError("E023", loc, $"rank '{e.RankId}' is not a fight_ranks id"));
                else
                {
                    e.RankIndex = ri;
                    pools[ri].Add(e);
                    double sum = StatSum(e.Stats), budget = FightRanks[ri].Budget;
                    if (Math.Abs(sum - budget) > 1e-6)
                        errors.Add(new ContentError("E043", loc, $"stats sum to {sum.ToString(System.Globalization.CultureInfo.InvariantCulture)}, but the budget of rank '{e.RankId}' is {budget.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
                }
                if (!Elements.ContainsKey(e.Element)) errors.Add(new ContentError("E023", loc, $"element '{e.Element}' is not an element id"));
                if (IdleRng.Sim.Combat.StatIds.Trained.Any(id => !(e.Stats.Get(id) >= 0 && e.Stats.Get(id) <= k.StatMax)))
                    errors.Add(new ContentError("E043", loc, "stats must be in [0, STAT_MAX]"));
                foreach (var sk in e.Skills)
                    if (!MonsterSkills.ContainsKey(sk)) errors.Add(new ContentError("E023", loc, $"skill '{sk}' is not a monster_skills id"));
                if (!(e.Weight > 0)) errors.Add(new ContentError("E043", loc, "weight must be > 0"));
            }
            for (int i = 0; i < FightRanks.Count; i++)
            {
                FightRanks[i].Pool = pools[i].AsReadOnly();
                if (pools[i].Count == 0) errors.Add(new ContentError("E044", $"fight_ranks[{FightRanks[i].Id}]", "the rank has no rank_enemies row (empty pool)"));
            }
            RankEnemies = new ReadOnlyDictionary<string, RankEnemyDef>(byId);
        }

        static List<string> SplitIds(string s) =>
            string.IsNullOrEmpty(s) ? new List<string>() : s.Split('|').Where(x => x.Length > 0).ToList();

        /// <summary>Dungeon (specs/21 section 5). Mirrors validate_tower in tools/content_build.py.</summary>
        void BuildTower(Dictionary<string, List<Row>> t, List<ContentError> errors)
        {
            var k = Constants;
            bool In01(double v) => v >= 0 && v <= 1;
            if (!(k.TowerFloors > 0 && k.TowerAttemptsPerDay > 0 && k.TowerTicketCap > 0 && k.TowerTicketPieces > 0 && k.TowerBase > 0 && k.TowerGrowth >= 1 && k.TowerBossHpMult > 0
                  && k.TowerMilestoneEvery > 0 && k.TowerShardsPerFloor >= 0 && In01(k.TowerBookChance) && k.TowerBookChancePerFloor >= 0
                  && k.EntropyCap > 0 && k.EntropyPerTrigger >= 0 && k.EntropyPerEcho >= 0 && k.EntropyPerCounter >= 0 && k.EntropyDecay >= 0
                  && In01(k.EntropyRecoilFrac) && k.EntropyStunGauge >= 0 && k.EntropyEnrageFrac >= 0 && k.EntropyEnrageTurns > 0
                  && k.EntropyConvertShield >= 0 && k.EntropyConvertTurns > 0 && k.CorruptStartTick >= 0 && k.CorruptEveryTicks > 0
                  && k.CorruptHealPerLevel >= 0 && In01(k.CorruptHealFloor) && k.CorruptDmgPerLevel >= 0))
                errors.Add(new ContentError("E043", "constants", "Dungeon constants out of range (specs/21 section 5)"));

            var phases = t["boss_phases"].Select(r => new BossPhaseDef
            {
                Id = r.Str("id"), BossId = r.Str("boss_id"), Order = r.Int("order"), EnterHp = r.Dbl("enter_hp"), Kind = r.Str("kind"),
                Power = r.Dbl("power"), Duration = r.Int("duration"), Count = r.Int("count"), Guard = r.Dbl("guard"),
                HealFrac = r.Dbl("heal_frac"), Strike = r.Dbl("strike"), Buff = r.Dbl("buff"),
                Adds = r.NStr("adds"),
                AddSkills = SplitIds(r.NStr("add_skills")).AsReadOnly(),
            }).ToList();
            var bosses = t["tower_bosses"].Select(r => new TowerBossDef
            {
                Id = r.Str("id"), NameKey = r.Str("name_key"), Element = r.Str("element"), Weights = StatsFrom(r, ""),
                Role = ParseTowerRole(r.Str("role")),
                Skills = SplitIds(r.NStr("skills")).AsReadOnly(),
            }).ToList();
            var byId = bosses.ToDictionary(b => b.Id, StringComparer.Ordinal);
            foreach (var b in bosses)
            {
                string loc = $"tower_bosses[{b.Id}]";
                b.Phases = phases.Where(p => p.BossId == b.Id).OrderBy(p => p.Order).ToList().AsReadOnly();
                if (!Elements.ContainsKey(b.Element)) errors.Add(new ContentError("E023", loc, $"element '{b.Element}' is not an element id"));
                foreach (var id in IdleRng.Sim.Combat.StatIds.Trained)
                    if (!(b.Weights.Get(id) >= 0)) { errors.Add(new ContentError("E043", loc, "stat weights must be >= 0")); break; }
                foreach (var sk in b.Skills)
                    if (!MonsterSkills.ContainsKey(sk)) errors.Add(new ContentError("E023", loc, $"skill '{sk}' is not a monster_skills id"));
                double prev = 1;
                foreach (var p in b.Phases)
                {
                    if (!(p.EnterHp > 0 && p.EnterHp < prev))
                        errors.Add(new ContentError("E032", $"boss_phases[{p.Id}]", "enter_hp must be in (0, 1) and strictly decrease with order"));
                    prev = p.EnterHp;
                }
                if (b.Role == TowerRole.Add && b.Phases.Count > 0) errors.Add(new ContentError("E044", loc, "an add cannot have phases"));
            }
            foreach (var p in phases)
            {
                string loc = $"boss_phases[{p.Id}]";
                if (!byId.ContainsKey(p.BossId)) errors.Add(new ContentError("E023", loc, $"boss_id '{p.BossId}' is not a tower_bosses id"));
                foreach (var sk in p.AddSkills)
                    if (!MonsterSkills.ContainsKey(sk)) errors.Add(new ContentError("E023", loc, $"add_skills '{sk}' is not a monster_skills id"));
                bool ok;
                switch (p.Kind)
                {
                    case IdleRng.Sim.Combat.PhaseKinds.ShieldCheck: ok = p.Power > 0 && p.Duration > 0 && p.HealFrac >= 0 && p.Buff >= 0; break;
                    case IdleRng.Sim.Combat.PhaseKinds.Split: ok = p.Count >= 1 && p.Guard >= 0 && p.Guard <= 1 && p.Adds != null; break;
                    case IdleRng.Sim.Combat.PhaseKinds.Channel: ok = p.Power > 0 && p.Duration > 0 && p.Strike >= 0; break;
                    case IdleRng.Sim.Combat.PhaseKinds.Enrage: ok = p.Buff >= 0; break;
                    default: errors.Add(new ContentError("E021", loc, $"unknown phase kind '{p.Kind}'")); continue;
                }
                if (!ok) errors.Add(new ContentError("E043", loc, $"values out of range for kind '{p.Kind}' (specs/21 section 3.2)"));
                if (p.Adds != null && (!byId.TryGetValue(p.Adds, out var add) || add.Role != TowerRole.Add))
                    errors.Add(new ContentError("E023", loc, $"adds '{p.Adds}' is not a tower_bosses id with role add"));
            }
            TowerBosses = new ReadOnlyDictionary<string, TowerBossDef>(byId);
            TowerRegulars = bosses.Where(b => b.Role == TowerRole.Regular).ToList().AsReadOnly();
            TowerMarquees = bosses.Where(b => b.Role == TowerRole.Marquee).ToList().AsReadOnly();
            if (TowerRegulars.Count == 0 || TowerMarquees.Count == 0)
                errors.Add(new ContentError("E044", "tower_bosses", "need at least one regular and one marquee boss"));

            BookDrops = t["book_drops"].Select(r => new BookDropDef { BookId = r.Str("book_id"), MinFloor = r.Int("min_floor"), Weight = r.Dbl("weight") })
                .ToList().AsReadOnly();
            if (BookDrops.Count == 0) errors.Add(new ContentError("E044", "book_drops", "at least one row is required"));
            else if (!BookDrops.Any(b => b.MinFloor == 1)) errors.Add(new ContentError("E044", "book_drops", "at least one row needs min_floor 1"));
            foreach (var b in BookDrops)
            {
                string loc = $"book_drops[{b.BookId}]";
                if (!SkillBooks.ContainsKey(b.BookId)) errors.Add(new ContentError("E023", loc, $"book_id '{b.BookId}' is not a skill_books id"));
                if (!(b.MinFloor >= 1 && b.MinFloor <= k.TowerFloors)) errors.Add(new ContentError("E043", loc, "min_floor must be in [1, TOWER_FLOORS]"));
                if (!(b.Weight > 0)) errors.Add(new ContentError("E043", loc, "weight must be > 0"));
            }
        }

        static TowerRole ParseTowerRole(string s)
        {
            switch (s)
            {
                case "regular": return TowerRole.Regular;
                case "marquee": return TowerRole.Marquee;
                case "add": return TowerRole.Add;
                default: throw new FormatException($"unknown tower role '{s}'");
            }
        }

        static TalentEffect ParseTalentEffect(string s)
        {
            switch (s)
            {
                case "drill_cost": return TalentEffect.DrillCost;
                case "stat_floor": return TalentEffect.StatFloor;
                case "chase_chance": return TalentEffect.ChaseChance;
                case "start_sp": return TalentEffect.StartSp;
                default: throw new FormatException($"unknown talent effect '{s}'");
            }
        }

        /// <summary>The book trees a chase tree's conditions name (tree_maxed, or node_owned on one of its nodes), in condition order.</summary>
        public IEnumerable<string> BookTreesNeeded(TreeDef chase)
        {
            foreach (var cd in chase.Conditions)
            {
                string tree = cd.Type == ConditionType.TreeMaxed ? cd.Ref
                    : cd.Type == ConditionType.NodeOwned && TreeNodes.TryGetValue(cd.Ref, out var n) ? n.TreeId : null;
                if (tree != null && Trees.TryGetValue(tree, out var t) && t.Kind == TreeKind.Book) yield return tree;
            }
        }

        static TreeKind ParseTreeKind(string s)
        {
            switch (s)
            {
                case "native": return TreeKind.Native;
                case "generic": return TreeKind.Generic;
                case "chase": return TreeKind.Chase;
                case "book": return TreeKind.Book;
                default: throw new FormatException($"unknown tree kind '{s}'");
            }
        }

        static NodeKind ParseNodeKind(string s)
        {
            switch (s)
            {
                case "growth": return NodeKind.Growth;
                case "stat": return NodeKind.Stat;
                case "skill": return NodeKind.Skill;
                case "trigger": return NodeKind.Trigger;
                case "rule": return NodeKind.Rule;
                default: throw new FormatException($"unknown node kind '{s}'");
            }
        }

        static IdleRng.Sim.Combat.FightTrigger ParseFightTrigger(string s)
        {
            switch (s)
            {
                case "battle_start": return IdleRng.Sim.Combat.FightTrigger.BattleStart;
                case "on_action": return IdleRng.Sim.Combat.FightTrigger.OnAction;
                case "on_basic_attack": return IdleRng.Sim.Combat.FightTrigger.OnBasicAttack;
                case "on_crit": return IdleRng.Sim.Combat.FightTrigger.OnCrit;
                case "on_hit_taken": return IdleRng.Sim.Combat.FightTrigger.OnHitTaken;
                case "on_evade": return IdleRng.Sim.Combat.FightTrigger.OnEvade;
                default: throw new FormatException($"unknown fight trigger '{s}'");
            }
        }

        static ConditionType ParseConditionType(string s)
        {
            switch (s)
            {
                case "tree_maxed": return ConditionType.TreeMaxed;
                case "node_owned": return ConditionType.NodeOwned;
                case "stat_at_least": return ConditionType.StatAtLeast;
                case "tally_at_least": return ConditionType.TallyAtLeast;
                default: throw new FormatException($"unknown condition type '{s}'");
            }
        }

        static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(bytes);
                var sb = new StringBuilder(h.Length * 2);
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        static SkillKind ParseSkillKind(string s)
        {
            switch (s)
            {
                case "damage": return SkillKind.Damage;
                case "heal": return SkillKind.Heal;
                case "shield": return SkillKind.Shield;
                case "taunt": return SkillKind.Taunt;
                case "buff": return SkillKind.Buff;
                case "debuff": return SkillKind.Debuff;
                case "passive": return SkillKind.Passive;
                default: throw new FormatException($"unknown skill kind '{s}'");
            }
        }

        static SkillTarget ParseSkillTarget(string s)
        {
            switch (s)
            {
                case "enemy": return SkillTarget.Enemy;
                case "all_enemies": return SkillTarget.AllEnemies;
                case "self": return SkillTarget.Self;
                case "lowest_ally": return SkillTarget.LowestAlly;
                case "all_allies": return SkillTarget.AllAllies;
                default: throw new FormatException($"unknown skill target '{s}'");
            }
        }

        /// <summary>Typed accessor over one parsed JSON object; throws FormatException naming the location.</summary>
        sealed class Row
        {
            readonly Dictionary<string, object> _d;

            public Row(Dictionary<string, object> d, string location)
            {
                _d = d;
                Location = location;
            }

            public string Location { get; }

            object Get(string k)
            {
                if (!_d.TryGetValue(k, out var v)) throw new FormatException($"{Location}.{k}: missing");
                return v;
            }

            public string Str(string k) => Get(k) as string ?? throw new FormatException($"{Location}.{k}: expected string");
            public string NStr(string k) => _d.TryGetValue(k, out var v) ? v as string : null;

            public int Int(string k)
            {
                if (Get(k) is long l && l >= int.MinValue && l <= int.MaxValue) return (int)l;
                throw new FormatException($"{Location}.{k}: expected int");
            }

            public int? NInt(string k) => _d.TryGetValue(k, out var v) && v is long l ? (int)l : (int?)null;
            public double? NDbl(string k) => _d.TryGetValue(k, out var v) ? (v is double d ? d : v is long l ? l : (double?)null) : null;

            public double Dbl(string k)
            {
                var v = Get(k);
                if (v is double d) return d;
                if (v is long l) return l;
                throw new FormatException($"{Location}.{k}: expected number");
            }

            public bool Bool(string k) => Get(k) is bool b ? b : throw new FormatException($"{Location}.{k}: expected bool");

            public Dictionary<string, object> Obj(string k) =>
                Get(k) as Dictionary<string, object> ?? throw new FormatException($"{Location}.{k}: expected object");
        }
    }
}
