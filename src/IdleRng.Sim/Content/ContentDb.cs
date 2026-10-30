using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IdleRng.Sim.Content
{
    /// <summary>
    /// Immutable content loaded from Content/*.json (specs/05 section 9). Load once, share freely
    /// (read-only, safe across threads). Code never hard-codes a balance number; it reads it here.
    /// </summary>
    public sealed class ContentDb
    {
        public const int SupportedSchemaVersion = 1;

        static readonly string[] TableNames =
        {
            "banners", "boss_rules", "constants", "curves", "daily_quests", "elements", "gear_rarity",
            "gear_slots", "hero_level_thresholds", "hero_tiers", "heroes", "progress_targets", "promotions", "roles", "skills",
            "tutorial_grants", "species", "drill_tiers", "fight_ranks", "stat_milestones",
            "monster_skills", "trees", "tree_nodes", "chase_conditions", "skill_books", "talents",
        };

        // Curves are precomputed to this many steps (covers STAGE_CAP); beyond it Eval keeps multiplying.
        const int CurvePrecompute = 10000;

        double[] _promoMult;     // [k] = product of the first k promotion multipliers
        CurveDef _gearSlot;      // a = 1, g = GEAR_SLOT_G

        ContentDb() { }

        public string Version { get; private set; }
        public string ContentHash { get; private set; }
        public string SourceRevision { get; private set; }

        public Constants Constants { get; private set; }
        public IReadOnlyDictionary<CurveId, CurveDef> Curves { get; private set; }
        public IReadOnlyList<BossRuleDef> BossRules { get; private set; }

        /// <summary>Index == sort_order == the slot index the sim and the save use.</summary>
        public IReadOnlyList<GearSlotDef> GearSlots { get; private set; }

        /// <summary>Indexed by (int)Rarity.</summary>
        public IReadOnlyList<GearRarityDef> GearRarity { get; private set; }

        /// <summary>Indexed by (int)Rarity.</summary>
        public IReadOnlyList<HeroTierDef> HeroTiers { get; private set; }

        public IReadOnlyDictionary<string, HeroDef> Heroes { get; private set; }

        /// <summary>Heroes in CSV order (C..M).</summary>
        public IReadOnlyList<HeroDef> HeroList { get; private set; }

        /// <summary>[0] is level 1 (cumulative copies).</summary>
        public IReadOnlyList<int> HeroLevelThresholds { get; private set; }

        public IReadOnlyDictionary<string, BannerDef> Banners { get; private set; }

        /// <summary>The P0 hero banner (first enabled banner of kind Hero).</summary>
        public BannerDef HeroBanner { get; private set; }

        /// <summary>Promotion steps; [0] is promotion 1 (specs/11 section 6).</summary>
        public IReadOnlyList<PromotionDef> Promotions { get; private set; }

        public IReadOnlyDictionary<string, RoleDef> Roles { get; private set; }
        public IReadOnlyDictionary<string, ElementDef> Elements { get; private set; }

        /// <summary>Roles and elements in sort_order.</summary>
        public IReadOnlyList<RoleDef> RoleList { get; private set; }
        public IReadOnlyList<ElementDef> ElementList { get; private set; }

        /// <summary>Each companion's kit in slot order (specs/11 section 4.4). Every hero has an entry.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<SkillDef>> Kits { get; private set; }

        /// <summary>Id of the starter companion (specs/11 section 4.5).</summary>
        public string StarterHeroId { get; private set; }

        /// <summary>The starter's FarmDps at level 1, promotion 0: the base of the reporting layers (specs/11 section 7.1).</summary>
        public double BaseFarmDps { get; private set; } = 1.0;
        public IReadOnlyList<DailyQuestDef> DailyQuests { get; private set; }
        public IReadOnlyList<TutorialGrantDef> TutorialGrants { get; private set; }
        public IReadOnlyList<ProgressTargetDef> ProgressTargets { get; private set; }
        public IReadOnlyDictionary<string, string> Strings { get; private set; }

        // Training run (specs/14)
        public IReadOnlyDictionary<string, SpeciesDef> Species { get; private set; }

        /// <summary>Species in CSV order.</summary>
        public IReadOnlyList<SpeciesDef> SpeciesList { get; private set; }

        /// <summary>Indexed by (int)DrillTier.</summary>
        public IReadOnlyList<DrillTierDef> DrillTiers { get; private set; }

        /// <summary>Index == sort_order == rank index in a run (0 = lowest).</summary>
        public IReadOnlyList<FightRankDef> FightRanks { get; private set; }

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

        public int GearSlotCount => GearSlots.Count;

        // ---------------------------------------------------------------- derived (specs/03 section 4)

        public double EnemyHp(int stage) => Curves[CurveId.EnemyHp].Eval(stage);
        public double GoldPerKill(int stage) => Curves[CurveId.GoldPerKill].Eval(stage);
        public double StatCost(int level) => Curves[CurveId.StatCost].Eval(level);

        /// <summary>GEAR_SLOT_G^tier - 1, before rarity. A fraction (0.5 = +50%).</summary>
        public double GearSlotPct(int tier) => _gearSlot.Eval(tier) - 1.0;

        /// <summary>Product of the first <paramref name="promotions"/> promotion multipliers (1 at 0).</summary>
        public double PromoMult(int promotions) =>
            _promoMult[Math.Max(0, Math.Min(promotions, _promoMult.Length - 1))];

        /// <summary>Promotion cap for a companion's rarity.</summary>
        public int MaxPromotions(HeroDef hero) => HeroTiers[(int)hero.Tier].MaxPromotions;

        /// <summary>Gold cost of promotion step <paramref name="index"/> (1-based): cost_kills * GOLD(unlock_stage).</summary>
        public double PromotionCost(int index)
        {
            var p = Promotions[index - 1];
            return p.CostKills * GoldPerKill(p.UnlockStage);
        }

        public BossRuleDef BossRuleFor(int stage)
        {
            var rules = BossRules;
            for (int i = rules.Count - 1; i > 0; i--)
                if (rules[i].FromStage <= stage) return rules[i];
            return rules[0];
        }

        /// <summary>Count of thresholds &lt;= copies (0 when copies == 0).</summary>
        public int HeroLevel(int copies)
        {
            int lvl = 0;
            var t = HeroLevelThresholds;
            for (int i = 0; i < t.Count; i++) if (copies >= t[i]) lvl = i + 1;
            return lvl;
        }

        /// <summary>HP/ATK/DEF scale from dupes: 1 + HERO_LVL_BONUS * (level - 1); 0 when not owned (specs/11 section 3.1).</summary>
        public double LevelMult(int level) => level <= 0 ? 0.0 : 1.0 + Constants.HeroLvlBonus * (level - 1);

        /// <summary>Expected crit multiplier: 1 + clamp(rate, 0, 1) * (critDmg - 1) (specs/11 section 7).</summary>
        public static double CritFactor(HeroDef hero) =>
            1.0 + Math.Max(0.0, Math.Min(1.0, hero.CritRate)) * (hero.CritDmg - 1.0);

        /// <summary>A companion's farming DPS at a level and promotion count (specs/11 section 7). 0 when level is 0.</summary>
        public double FarmDps(HeroDef hero, int level, int promotions) =>
            hero.Atk * LevelMult(level) * PromoMult(promotions) * CritFactor(hero) * hero.Spd / Constants.SpdRef;

        /// <summary>Element damage multiplier for attacker vs defender (specs/11 section 8): strong, weak or 1.</summary>
        public double ElementMult(string attacker, string defender)
        {
            if (attacker == null || defender == null) return 1.0;
            if (Elements.TryGetValue(attacker, out var a) && a.StrongVs == defender) return Constants.ElementStrongMult;
            if (Elements.TryGetValue(defender, out var d) && d.StrongVs == attacker) return Constants.ElementWeakMult;
            return 1.0;
        }

        /// <summary>The boss element on <paramref name="stage"/>: rotates through elements every BOSS_EVERY stages.</summary>
        public string BossElement(int stage) =>
            ElementList.Count == 0 ? null : ElementList[(stage / Math.Max(1, Constants.BossEvery)) % ElementList.Count].Id;

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
                TickHz = I("TICK_HZ"), SpdRef = D("SPD_REF"), AttackIntervalTicks = I("ATTACK_INTERVAL_TICKS"),
                KillsPerStage = I("KILLS_PER_STAGE"), SpawnDelayTicks = I("SPAWN_DELAY_TICKS"), BossEvery = I("BOSS_EVERY"),
                StageCap = I("STAGE_CAP"), StatPerLvl = D("STAT_PER_LVL"), GemsPerDayF2p = I("GEMS_PER_DAY_F2P"),
                OfflineRate = D("OFFLINE_RATE"), OfflineCapH = D("OFFLINE_CAP_H"), OfflineMinGapS = I("OFFLINE_MIN_GAP_S"),
                DaySeconds = I("DAY_SECONDS"), GearDropP = D("GEAR_DROP_P"), TierWidth = I("TIER_WIDTH"),
                GearSlotG = D("GEAR_SLOT_G"), GearSellKills = D("GEAR_SELL_KILLS"), EnhMax = I("ENH_MAX"),
                EnhPerLvl = D("ENH_PER_LVL"), EnhSuccessFloor = D("ENH_SUCCESS_FLOOR"), EnhSuccessStep = D("ENH_SUCCESS_STEP"),
                EnhCostGoldMult = D("ENH_COST_GOLD_MULT"), HeroSlots = I("HERO_SLOTS"), HeroLvlBonus = D("HERO_LVL_BONUS"),
                IdleChestHours = D("IDLE_CHEST_HOURS"), IdleChestGems = I("IDLE_CHEST_GEMS"),
                BattleGauge = D("BATTLE_GAUGE"), DefKMult = D("DEF_K_MULT"), ElementStrongMult = D("ELEMENT_STRONG_MULT"),
                ElementWeakMult = D("ELEMENT_WEAK_MULT"), BasicAttackPower = D("BASIC_ATTACK_POWER"),
                AiHealBelow = D("AI_HEAL_BELOW"), AiShieldBelow = D("AI_SHIELD_BELOW"),
                MonHpPerPoint = D("MON_HP_PER_POINT"), MonDefK = D("MON_DEF_K"), MonEvadeBase = D("MON_EVADE_BASE"),
                MonEvadePerDex = D("MON_EVADE_PER_DEX"), MonEvadeCap = D("MON_EVADE_CAP"), MonCritBase = D("MON_CRIT_BASE"),
                MonCritPerLuk = D("MON_CRIT_PER_LUK"), MonCritCap = D("MON_CRIT_CAP"), MonCritDmg = D("MON_CRIT_DMG"),
                MonSpdMin = D("MON_SPD_MIN"), MonTimeLimitTicks = I("MON_TIME_LIMIT_TICKS"), MonSideSize = I("MON_SIDE_SIZE"),
                RunCycles = I("RUN_CYCLES"), RunAp = I("RUN_AP"), RunEnergyMax = D("RUN_ENERGY_MAX"),
                RunEnergyStart = D("RUN_ENERGY_START"), DrillEnergyCost = D("DRILL_ENERGY_COST"), RestEnergy = D("REST_ENERGY"),
                DrillFailMin = D("DRILL_FAIL_MIN"), DrillFailMax = D("DRILL_FAIL_MAX"), DrillSuperP = D("DRILL_SUPER_P"),
                InjuryBelow = D("INJURY_BELOW"), InjuryMaxP = D("INJURY_MAX_P"), StatMax = D("STAT_MAX"), FightEvery = I("FIGHT_EVERY"),
                SpPerCycle = I("SP_PER_CYCLE"), SpPerWin = I("SP_PER_WIN"), TreeSlots = I("TREE_SLOTS"), DraftSize = I("DRAFT_SIZE"),
                MonEchoPower = D("MON_ECHO_POWER"), ShardsPerCycle = I("SHARDS_PER_CYCLE"), ShardsPerRank = I("SHARDS_PER_RANK"),
            };
            if (errors.Count > 0) return;

            // curves
            var curves = new Dictionary<CurveId, CurveDef>();
            foreach (var r in t["curves"])
            {
                var id = ParseCurveId(r.Str("id"));
                curves[id] = new CurveDef(id, r.Dbl("base"), r.Dbl("growth"), CurvePrecompute);
            }
            foreach (CurveId id in new[] { CurveId.EnemyHp, CurveId.GoldPerKill, CurveId.StatCost })
                if (!curves.ContainsKey(id)) errors.Add(new ContentError("E044", $"curves[{id}]", "curve missing"));
            Curves = new ReadOnlyDictionary<CurveId, CurveDef>(curves);
            _gearSlot = new CurveDef(CurveId.EnemyHp, 1.0, Constants.GearSlotG, CurvePrecompute);

            // boss rules
            BossRules = t["boss_rules"].Select(r => new BossRuleDef
            {
                Id = r.Str("id"), FromStage = r.Int("from_stage"), HpMult = r.Dbl("hp_mult"), TimeTicks = r.Int("time_ticks"),
                GoldMult = r.Dbl("gold_mult"), GearDropP = r.Dbl("gear_drop_p"), GearMinRarity = ParseRarity(r.Str("gear_min_rarity")),
                FirstClearGems = r.Int("first_clear_gems"), AtkMult = r.Dbl("atk_mult"), DefMult = r.Dbl("def_mult"),
                Spd = r.Dbl("spd"), AoeEvery = r.Int("aoe_every"), AoePower = r.Dbl("aoe_power"),
            }).OrderBy(b => b.FromStage).ToList().AsReadOnly();
            foreach (var b in BossRules)
                if (!(b.Spd > 0) || b.AtkMult < 0 || b.DefMult < 0 || b.AoeEvery < 0 || b.AoePower < 0)
                    errors.Add(new ContentError("E043", $"boss_rules[{b.Id}]", "atk_mult, def_mult, aoe_power >= 0; spd > 0; aoe_every >= 0"));
            if (!(Constants.BattleGauge > 0 && Constants.DefKMult > 0 && Constants.ElementStrongMult > 0 && Constants.ElementWeakMult > 0
                  && Constants.BasicAttackPower > 0))
                errors.Add(new ContentError("E043", "constants", "battle constants must be > 0"));
            var mk = Constants;
            bool In01(double v) => v >= 0 && v <= 1;
            if (!(mk.MonHpPerPoint > 0 && mk.MonDefK > 0 && In01(mk.MonEvadeBase) && mk.MonEvadePerDex >= 0 && In01(mk.MonEvadeCap)
                  && In01(mk.MonCritBase) && mk.MonCritPerLuk >= 0 && In01(mk.MonCritCap) && mk.MonCritDmg >= 1 && mk.MonSpdMin > 0
                  && mk.MonTimeLimitTicks > 0 && mk.MonSideSize > 0))
                errors.Add(new ContentError("E043", "constants", "monster battle constants out of range (specs/16 section 7)"));
            if (BossRules.Count == 0 || BossRules[0].FromStage != 1)
                errors.Add(new ContentError("E045", "boss_rules", "first row must have from_stage = 1"));

            // gear
            GearSlots = t["gear_slots"].Select(r => new GearSlotDef
            {
                Id = r.Str("id"), SortOrder = r.Int("sort_order"), NameKey = r.Str("name_key"), IconId = r.Str("icon_id"),
            }).OrderBy(s => s.SortOrder).ToList().AsReadOnly();
            for (int i = 0; i < GearSlots.Count; i++)
                if (GearSlots[i].SortOrder != i) errors.Add(new ContentError("E047", "gear_slots", "sort_order must be exactly 0..N-1"));

            GearRarity = t["gear_rarity"].Select(r => new GearRarityDef
            {
                Rarity = ParseRarity(r.Str("rarity")), Weight = r.Dbl("weight"), Mult = r.Dbl("mult"),
                DisplayColor = r.Str("display_color"), NameKey = r.Str("name_key"),
            }).OrderBy(g => g.Rarity).ToList().AsReadOnly();
            CheckFiveTiers(GearRarity.Select(g => g.Rarity), "gear_rarity", errors);
            double wsum = GearRarity.Sum(g => g.Weight);
            if (Math.Abs(wsum - 1.0) > 1e-9) errors.Add(new ContentError("E030", "gear_rarity", $"weights sum to {wsum:R}"));

            // heroes
            HeroTiers = t["hero_tiers"].Select(r => new HeroTierDef
            {
                Tier = ParseRarity(r.Str("tier")), DisplayColor = r.Str("display_color"), NameKey = r.Str("name_key"),
                ShardValue = r.Int("shard_value"), MaxPromotions = r.Int("max_promotions"),
            }).OrderBy(h => h.Tier).ToList().AsReadOnly();
            CheckFiveTiers(HeroTiers.Select(h => h.Tier), "hero_tiers", errors);

            var heroList = t["heroes"].Select(r => new HeroDef
            {
                Id = r.Str("id"), Tier = ParseRarity(r.Str("tier")), PoolId = r.Str("pool_id"), NameKey = r.Str("name_key"),
                DescKey = r.Str("desc_key"), IconId = r.Str("icon_id"), Role = r.Str("role"), Element = r.Str("element"),
                Hp = r.Dbl("hp"), Atk = r.Dbl("atk"), Def = r.Dbl("def"), Spd = r.Dbl("spd"),
                CritRate = r.Dbl("crit_rate"), CritDmg = r.Dbl("crit_dmg"), Starter = r.Bool("starter"),
            }).ToList();
            HeroList = heroList.AsReadOnly();
            var heroes = new Dictionary<string, HeroDef>(StringComparer.Ordinal);
            foreach (var h in heroList)
            {
                if (heroes.ContainsKey(h.Id)) errors.Add(new ContentError("E020", $"heroes[{h.Id}]", "duplicate id"));
                heroes[h.Id] = h;
            }
            Heroes = new ReadOnlyDictionary<string, HeroDef>(heroes);
            if (!(Constants.SpdRef > 0)) errors.Add(new ContentError("E043", "constants[SPD_REF]", "value out of range (> 0)"));
            foreach (var h in heroList)
            {
                if (!(h.Hp > 0 && h.Atk > 0 && h.Def > 0 && h.Spd > 0))
                    errors.Add(new ContentError("E043", $"heroes[{h.Id}]", "hp, atk, def and spd must be > 0"));
                if (!(h.CritRate >= 0 && h.CritRate <= 1)) errors.Add(new ContentError("E043", $"heroes[{h.Id}]", "crit_rate must be in [0, 1]"));
                if (!(h.CritDmg >= 1)) errors.Add(new ContentError("E043", $"heroes[{h.Id}]", "crit_dmg must be >= 1"));
            }
            var starters = heroList.Where(h => h.Starter).ToList();
            if (starters.Count != 1)
                errors.Add(new ContentError("E062", "heroes", $"exactly one hero must have starter = true (found {starters.Count})"));
            else StarterHeroId = starters[0].Id;

            // roles and elements (specs/11 section 10)
            RoleList = t["roles"].Select(r => new RoleDef
            {
                Id = r.Str("id"), SortOrder = r.Int("sort_order"), NameKey = r.Str("name_key"), DescKey = r.Str("desc_key"),
            }).OrderBy(x => x.SortOrder).ToList().AsReadOnly();
            ElementList = t["elements"].Select(r => new ElementDef
            {
                Id = r.Str("id"), SortOrder = r.Int("sort_order"), NameKey = r.Str("name_key"),
                DisplayColor = r.Str("display_color"), StrongVs = r.Str("strong_vs"),
            }).OrderBy(x => x.SortOrder).ToList().AsReadOnly();
            Roles = new ReadOnlyDictionary<string, RoleDef>(RoleList.ToDictionary(x => x.Id, StringComparer.Ordinal));
            Elements = new ReadOnlyDictionary<string, ElementDef>(ElementList.ToDictionary(x => x.Id, StringComparer.Ordinal));
            foreach (var e in ElementList)
                if (!Elements.ContainsKey(e.StrongVs))
                    errors.Add(new ContentError("E023", $"elements[{e.Id}]", $"strong_vs '{e.StrongVs}' is not an element id"));
            foreach (var h in heroList)
            {
                if (!Roles.ContainsKey(h.Role)) errors.Add(new ContentError("E023", $"heroes[{h.Id}]", $"role '{h.Role}' is not a role id"));
                if (!Elements.ContainsKey(h.Element)) errors.Add(new ContentError("E023", $"heroes[{h.Id}]", $"element '{h.Element}' is not an element id"));
            }

            var thresholds = t["hero_level_thresholds"].OrderBy(r => r.Int("level")).Select(r => r.Int("cum_copies")).ToList();
            for (int i = 1; i < thresholds.Count; i++)
                if (thresholds[i] <= thresholds[i - 1])
                    errors.Add(new ContentError("E031", $"hero_level_thresholds[{i + 1}]", "cum_copies must strictly increase"));
            if (thresholds.Count == 0 || thresholds[0] != 1)
                errors.Add(new ContentError("E061", "hero_level_thresholds[1]", "level 1 must need exactly 1 copy"));
            HeroLevelThresholds = thresholds.AsReadOnly();

            // banners
            var banners = new Dictionary<string, BannerDef>(StringComparer.Ordinal);
            foreach (var r in t["banners"])
            {
                var ratesObj = r.Obj("rates");
                var rates = new double[5];
                for (int i = 0; i < 5; i++) rates[i] = new Row(ratesObj, r.Location + ".rates").Dbl(((Rarity)i).ToString());
                var b = new BannerDef
                {
                    Id = r.Str("id"), Kind = r.Str("kind") == "pet" ? BannerKind.Pet : BannerKind.Hero, NameKey = r.Str("name_key"),
                    PoolId = r.Str("pool_id"), Rates = Array.AsReadOnly(rates), BaseLplus = rates[(int)Rarity.L] + rates[(int)Rarity.M],
                    PitySoftStart = r.Int("pity_soft_start"), PitySoftStep = r.Dbl("pity_soft_step"), PityHard = r.Int("pity_hard"),
                    MythicShareOfLplus = r.Dbl("mythic_share_of_lplus"), GuaranteeBatchSize = r.Int("guarantee_batch_size"),
                    GuaranteeMinTier = ParseRarity(r.Str("guarantee_min_tier")), PullCostGems = r.Int("pull_cost_gems"),
                    FreePullsPerDay = r.Int("free_pulls_per_day"), RngStream = r.Str("rng_stream"), Enabled = r.Bool("enabled"),
                };
                if (Math.Abs(rates.Sum() - 1.0) > 1e-9)
                    errors.Add(new ContentError("E030", $"banners[{b.Id}]", $"rates sum to {rates.Sum():R}"));
                var pool = new IReadOnlyList<string>[5];
                for (int i = 0; i < 5; i++)
                {
                    var tier = (Rarity)i;
                    var ids = heroList.Where(h => h.PoolId == b.PoolId && h.Tier == tier).Select(h => h.Id)
                        .OrderBy(id => id, StringComparer.Ordinal).ToList();
                    if (rates[i] > 0 && ids.Count == 0)
                        errors.Add(new ContentError("E033", $"banners[{b.Id}]", $"tier {tier} has a rate but no heroes in pool"));
                    pool[i] = ids.AsReadOnly();
                }
                b.Pool = Array.AsReadOnly(pool);
                banners[b.Id] = b;
            }
            Banners = new ReadOnlyDictionary<string, BannerDef>(banners);
            HeroBanner = banners.Values.FirstOrDefault(b => b.Enabled && b.Kind == BannerKind.Hero);
            if (HeroBanner == null) errors.Add(new ContentError("E022", "banners", "no enabled hero banner"));

            // promotions (specs/11 section 6)
            Promotions = t["promotions"].Select(r => new PromotionDef
            {
                Index = r.Int("index"), UnlockStage = r.Int("unlock_stage"), Mult = r.Dbl("mult"),
                CostKills = r.Dbl("cost_kills"), NameKey = r.Str("name_key"),
            }).OrderBy(p => p.Index).ToList().AsReadOnly();
            _promoMult = new double[Promotions.Count + 1];
            _promoMult[0] = 1.0;
            for (int i = 0; i < Promotions.Count; i++)
            {
                _promoMult[i + 1] = _promoMult[i] * Promotions[i].Mult;
                if (Promotions[i].Index != i + 1)
                    errors.Add(new ContentError("E060", $"promotions[{Promotions[i].Index}]", "index must be 1..N contiguous"));
                if (i > 0 && Promotions[i].UnlockStage <= Promotions[i - 1].UnlockStage)
                    errors.Add(new ContentError("E032", $"promotions[{Promotions[i].Index}]", "unlock_stage must strictly increase"));
                if (!(Promotions[i].Mult > 0) || !(Promotions[i].CostKills >= 0))
                    errors.Add(new ContentError("E043", $"promotions[{Promotions[i].Index}]", "mult must be > 0 and cost_kills >= 0"));
            }
            if (StarterHeroId != null && Constants.SpdRef > 0) BaseFarmDps = FarmDps(Heroes[StarterHeroId], 1, 0);
            foreach (var tierDef in HeroTiers)
                if (tierDef.MaxPromotions < 0 || tierDef.MaxPromotions > Promotions.Count)
                    errors.Add(new ContentError("E043", $"hero_tiers[{tierDef.Tier}]", $"max_promotions must be in 0..{Promotions.Count}"));

            // skeleton kits (specs/11 section 4.4)
            var skills = t["skills"].Select(r => new SkillDef
            {
                Id = r.Str("id"), HeroId = r.Str("hero_id"), Slot = r.Int("slot"), UnlockPromotion = r.Int("unlock_promotion"),
                NameKey = r.Str("name_key"), DescKey = r.Str("desc_key"), Kind = ParseSkillKind(r.Str("kind")),
                Target = ParseSkillTarget(r.Str("target")), Stat = r.NStr("stat"), Scales = r.NStr("scales"),
                Power = r.Dbl("power"), Cooldown = r.Int("cooldown"), Duration = r.Int("duration"),
            }).ToList();
            var kits = new Dictionary<string, IReadOnlyList<SkillDef>>(StringComparer.Ordinal);
            foreach (var h in heroList)
            {
                var kit = skills.Where(x => x.HeroId == h.Id).OrderBy(x => x.Slot).ToList();
                for (int i = 0; i < kit.Count; i++)
                    if (kit[i].Slot != i) errors.Add(new ContentError("E060", $"skills[{h.Id}]", "slots must be 0..N-1 contiguous per hero"));
                int max = HeroTiers.Count == 5 ? HeroTiers[(int)h.Tier].MaxPromotions : 0;
                var promos = kit.Where(x => x.UnlockPromotion > 0).Select(x => x.UnlockPromotion).OrderBy(x => x).ToList();
                if (!kit.Any(x => x.UnlockPromotion == 0) || !promos.SequenceEqual(Enumerable.Range(1, max)))
                    errors.Add(new ContentError("E063", $"skills[{h.Id}]", $"needs a base skill and exactly one skill per promotion 1..{max}"));
                kits[h.Id] = kit.AsReadOnly();
            }
            foreach (var x in skills)
                if (x.Stat == "hp" && x.Kind != SkillKind.Passive)
                    errors.Add(new ContentError("E054", $"skills[{x.Id}]", "stat hp is only allowed on passives"));
            foreach (var x in skills)
                if (!heroes.ContainsKey(x.HeroId)) errors.Add(new ContentError("E023", $"skills[{x.Id}]", $"hero_id '{x.HeroId}' is not a hero id"));
            Kits = new ReadOnlyDictionary<string, IReadOnlyList<SkillDef>>(kits);

            DailyQuests = t["daily_quests"].Select(r => new DailyQuestDef
            {
                Id = r.Str("id"), Type = ParseQuestType(r.Str("type")), Target = r.Int("target"), RewardGems = r.Int("reward_gems"),
                SortOrder = r.Int("sort_order"), NameKey = r.Str("name_key"),
            }).OrderBy(q => q.SortOrder).ToList().AsReadOnly();

            TutorialGrants = t["tutorial_grants"].Select(r => new TutorialGrantDef
            {
                Id = r.Str("id"), Trigger = ParseGrantTrigger(r.Str("trigger")), TriggerValue = r.NInt("trigger_value"),
                GrantType = ParseGrantType(r.Str("grant_type")), Amount = r.Int("amount"), GrantRef = r.NStr("grant_ref"),
                SortOrder = r.Int("sort_order"),
            }).OrderBy(g => g.SortOrder).ToList().AsReadOnly();

            ProgressTargets = t["progress_targets"].Select(r => new ProgressTargetDef { Day = r.Int("day"), Stage = r.Int("stage") })
                .OrderBy(p => p.Day).ToList().AsReadOnly();

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
                  && In01(k.InjuryBelow) && In01(k.InjuryMaxP) && k.StatMax > 0 && k.FightEvery > 0
                  && k.SpPerCycle >= 0 && k.SpPerWin >= 0 && k.TreeSlots >= 2 && k.DraftSize > 0 && In01(k.MonEchoPower)
                  && k.ShardsPerCycle >= 0 && k.ShardsPerRank >= 0))
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
                Stats = StatsFrom(r, ""),
            }).OrderBy(x => x.SortOrder).ToList().AsReadOnly();
            if (FightRanks.Count == 0) errors.Add(new ContentError("E044", "fight_ranks", "at least one rank is required"));
            if (!FightRanks.Select(x => x.SortOrder).SequenceEqual(Enumerable.Range(0, FightRanks.Count)))
                errors.Add(new ContentError("E047", "fight_ranks", "sort_order must be exactly 0..N-1"));
            foreach (var fr in FightRanks)
            {
                if (!Elements.ContainsKey(fr.Element)) errors.Add(new ContentError("E023", $"fight_ranks[{fr.Id}]", $"element '{fr.Element}' is not an element id"));
                if (!StatsOk(fr.Stats, 0, k.StatMax, false)) errors.Add(new ContentError("E043", $"fight_ranks[{fr.Id}]", "stats must be in [0, STAT_MAX]"));
            }

            BuildTrees(t, errors);
            BuildAccount(t, errors);

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
                    OfferThreshold = r.NInt("offer_threshold") ?? 0,
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
            }

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
                Id = r.Str("id"), NameKey = r.Str("name_key"), DescKey = r.Str("desc_key"), Effect = ParseTalentEffect(r.Str("effect")),
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
                    default: ok = p >= 0 && p == Math.Floor(p); break;
                }
                if (!ok) errors.Add(new ContentError("E043", loc, $"per_rank {p} out of range for {d.Effect}"));
            }
        }

        static TalentEffect ParseTalentEffect(string s)
        {
            switch (s)
            {
                case "drill_cost": return TalentEffect.DrillCost;
                case "stat_floor": return TalentEffect.StatFloor;
                case "draft_size": return TalentEffect.DraftSize;
                case "start_sp": return TalentEffect.StartSp;
                default: throw new FormatException($"unknown talent effect '{s}'");
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

        static void CheckFiveTiers(IEnumerable<Rarity> got, string table, List<ContentError> errors)
        {
            if (!got.SequenceEqual(new[] { Rarity.C, Rarity.R, Rarity.E, Rarity.L, Rarity.M }))
                errors.Add(new ContentError("E044", table, "must list C, R, E, L, M exactly once"));
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

        public static Rarity ParseRarity(string s)
        {
            switch (s)
            {
                case "C": return Rarity.C;
                case "R": return Rarity.R;
                case "E": return Rarity.E;
                case "L": return Rarity.L;
                case "M": return Rarity.M;
                default: throw new FormatException($"'{s}' is not one of C, R, E, L, M");
            }
        }

        static CurveId ParseCurveId(string s)
        {
            switch (s)
            {
                case "enemy_hp": return CurveId.EnemyHp;
                case "gold_per_kill": return CurveId.GoldPerKill;
                case "stat_cost": return CurveId.StatCost;
                default: throw new FormatException($"unknown curve id '{s}'");
            }
        }

        static QuestType ParseQuestType(string s)
        {
            switch (s)
            {
                case "login": return QuestType.Login;
                case "kill_mobs": return QuestType.KillMobs;
                case "clear_boss": return QuestType.ClearBoss;
                case "do_pulls": return QuestType.DoPulls;
                default: throw new FormatException($"unknown quest type '{s}'");
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

        static GrantTrigger ParseGrantTrigger(string s)
        {
            switch (s)
            {
                case "profile_created": return GrantTrigger.ProfileCreated;
                case "stage_reached": return GrantTrigger.StageReached;
                case "first_boss_cleared": return GrantTrigger.FirstBossCleared;
                default: throw new FormatException($"unknown grant trigger '{s}'");
            }
        }

        static GrantType ParseGrantType(string s)
        {
            switch (s)
            {
                case "gems": return GrantType.Gems;
                case "free_pulls": return GrantType.FreePulls;
                case "gold": return GrantType.Gold;
                case "hero": return GrantType.Hero;
                default: throw new FormatException($"unknown grant type '{s}'");
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
