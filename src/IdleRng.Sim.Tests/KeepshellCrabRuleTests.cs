using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Species Keepshell Crab (#152): its content and the rules tide_moat, unbreached and shellcrack.</summary>
    public class KeepshellCrabRuleTests
    {
        static FightRules Rules => FightRules.FromContent(TestContent.Db);

        static readonly string[] SpeciesRules = { RuleModifiers.TideMoat, RuleModifiers.Unbreached, RuleModifiers.Shellcrack };

        /// <summary>Always the same draw: 0 = every evade and crit roll succeeds, 0.99 = none does.</summary>
        sealed class Fixed : IRandomSource
        {
            readonly double _v;
            public Fixed(double v) { _v = v; }
            public double NextDouble() => _v;
        }

        static Fighter Mon(string id, Action<MonsterStats> tweak = null, string element = null, params string[] rules)
        {
            var st = new MonsterStats { Hp = 300, Str = 300, Int = 300, Wis = 300, Dex = 0, Def = 300, Spd = 300, Luk = 0 };
            tweak?.Invoke(st);
            var f = new Fighter { Id = id, Stats = st, Element = element };
            f.Rules.AddRange(rules);
            return f;
        }

        /// <summary>A fast attacker: it acts many times before the slow, very tough target acts once.</summary>
        static Fighter Fast(string id, Action<MonsterStats> tweak = null, params string[] rules) =>
            Mon(id, x => { x.Spd = 900; tweak?.Invoke(x); }, null, rules);

        static Fighter Wall(string id, Action<MonsterStats> tweak = null, params string[] rules) =>
            Mon(id, x => { x.Spd = 10; x.Hp = 99999; tweak?.Invoke(x); }, null, rules);

        static CombatSkill Dmg(string id, double power, string scales) =>
            new CombatSkill { Id = id, Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = scales, Power = power };

        static Fighter With(Fighter f, params CombatSkill[] skills) { f.Skills.AddRange(skills); return f; }

        /// <summary>Steps until side A's first unit has taken <paramref name="actions"/> actions (side B must not act).</summary>
        static List<FightAction> Play(FightRules r, Fighter a, Fighter b, int actions, double draw, Action<FightState> end = null)
        {
            var s = BattleSim.Start(r, new[] { a }, new[] { b });
            var log = new List<FightAction>();
            while (s.Units[0].Tally.Actions < actions && s.Outcome == FightOutcome.Ongoing) BattleSim.Step(r, s, new Fixed(draw), log.Add);
            Assert.That(s.Units[1].Tally.Actions, Is.EqualTo(0), "side B never acted");
            end?.Invoke(s);
            return log;
        }

        /// <summary>Side A's hits on b (basic attacks and skills, not rule events or counters).</summary>
        static List<FightAction> Hits(List<FightAction> log) => log.Where(x => x.Actor == "a" && x.Event == null && x.Target == "b" && !x.Counter).ToList();

        // ------------------------------------------------------------------ content and constants

        [Test]
        public void Content_SpeciesTreesAndConstants()
        {
            var db = TestContent.Db;
            var r = Rules;
            Assert.That((r.MoatDef, r.WallDef, r.WallErode, r.CrackDef), Is.EqualTo((db.ConstantValues["MON_MOAT_DEF"],
                db.ConstantValues["MON_WALL_DEF"], db.ConstantValues["MON_WALL_ERODE"], db.ConstantValues["MON_CRACK_DEF"])));
            var sp = db.SpeciesList.Single(x => x.Id == "keepshell_crab");
            Assert.That((sp.Element, sp.NativeTree1, sp.NativeTree2, sp.ChaseTreeRun, sp.ChaseTreeBook),
                Is.EqualTo(("water", "fortress_fist", "tide_moat", "unbreached_keep", "siege_pincers")));
            Assert.That(db.SpeciesList[0].Id, Is.Not.EqualTo("keepshell_crab"), "appended, never the starter");

            // The idea-pool tree is native-only now: never offered by an Awakening, owned by this species alone.
            var fist = db.Trees["fortress_fist"];
            Assert.That((fist.Kind, fist.OfferStat, fist.OfferThreshold), Is.EqualTo((TreeKind.Native, (string)null, 0)));
            Assert.That(db.SpeciesList.Count(x => x.NativeTree1 == "fortress_fist" || x.NativeTree2 == "fortress_fist"), Is.EqualTo(1));

            var caps = new Dictionary<string, string>
            {
                ["tide_moat"] = RuleModifiers.TideMoat, ["unbreached_keep"] = RuleModifiers.Unbreached, ["siege_pincers"] = RuleModifiers.Shellcrack,
            };
            foreach (var kv in caps)
            {
                var t = db.Trees[kv.Key];
                Assert.That(t.Nodes.Count, Is.EqualTo(8), kv.Key);
                Assert.That((t.Nodes[7].Kind, t.Nodes[7].RuleId), Is.EqualTo((NodeKind.Rule, kv.Value)), kv.Key);
                Assert.That(t.Nodes[4].Kind, Is.EqualTo(NodeKind.Skill), kv.Key);
                Assert.That(RuleModifiers.IsKnown(kv.Value), kv.Value);
                var desc = BattleText.RuleDesc(db, kv.Value);
                Assert.That(TextTemplate.HasPlaceholder(desc), Is.False, desc);
                Assert.That(db.Text("rule." + kv.Value + ".name"), Is.Not.EqualTo("rule." + kv.Value + ".name"), kv.Value);
            }
            Assert.That(db.Trees["tide_moat"].Kind, Is.EqualTo(TreeKind.Native));
            Assert.That(db.Trees["siege_pincers"].BookGated, Is.True);
            Assert.That(db.Trees["siege_manual"].Kind, Is.EqualTo(TreeKind.Book));
            Assert.That(db.SkillBooks["book_siege"].TreeId, Is.EqualTo("siege_manual"));
            Assert.That(db.BookDrops.Single(b => b.BookId == "book_siege").MinFloor, Is.EqualTo(10));
            Assert.That(db.Achievements["castle_walls"].UnlocksSpecies, Is.EqualTo("keepshell_crab"));
            Assert.That(db.Text("tree.unbreached_keep.apex"), Is.EqualTo("Citadel Kingcrab"));
            Assert.That(db.Text("tree.siege_pincers.apex"), Is.EqualTo("Siegebreaker Crab"));

            // Every skill of the kit reads DEF, so none goes dead under Fortress Fist.
            foreach (var id in new[] { "moat_surge", "battering_claw", "rubble_rain", "keep_gate", "trophy_wall" })
                Assert.That(db.MonsterSkills[id].Scales, Is.EqualTo(StatIds.Def), id);
        }

        [Test]
        public void Validation_RejectsConstantsOutOfRange()
        {
            var a = new[] { Mon("a") };
            var b = new[] { Mon("b") };
            foreach (var bad in new Action<FightRules>[]
                     {
                         r => r.MoatDef = -0.1, r => r.MoatDef = double.PositiveInfinity, r => r.WallDef = -0.1, r => r.WallDef = double.NaN,
                         r => r.WallErode = 0, r => r.WallErode = 1.1, r => r.WallErode = double.NaN, r => r.CrackDef = -0.1,
                     })
            {
                var r = Rules;
                bad(r);
                Assert.Throws<ArgumentException>(() => BattleSim.Start(r, a, b));
            }
        }

        [Test]
        public void NoCarrier_NoSpeciesEvents()
        {
            var log = new List<FightAction>();
            BattleSim.Resolve(Rules, new[] { Mon("a", null, "water") }, new[] { Mon("b", null, "nature") }, 1, log.Add);
            Assert.That(log.Any(x => x.Event != null && SpeciesRules.Any(id => x.Event == FightAction.RulePrefix + id)), Is.False);
        }

        [Test]
        public void EveryRule_In1v1_4v1_And4v4_IsDeterministic_AndCloneContinuesIdentically()
        {
            var r = Rules;
            Fighter Crab(string id, string rule) => With(Mon(id, x => { x.Hp = 600; x.Str = 0; x.Int = 0; x.Def = 700; x.Luk = 200; }, "water",
                RuleModifiers.FortressFist, rule), Dmg("moat_surge", 1.3, StatIds.Def));
            Fighter Caster(string id) => Mon(id, x => { x.Str = 0; x.Int = 400; x.Wis = 200; x.Def = 200; }, "fire");
            foreach (var rule in SpeciesRules)
            {
                var shapes = new (Fighter[] A, Fighter[] B)[]
                {
                    (new[] { Crab("c", rule) }, new[] { Caster("foe") }),
                    (new[] { Crab("c1", rule), Crab("c2", rule), Mon("m3"), Caster("m4") },
                     new[] { Mon("boss", x => { x.Hp = 2500; x.Str = 0; x.Int = 150; x.Def = 500; x.Spd = 250; }) }),
                    (new[] { Crab("c1", rule), Crab("c2", rule), Mon("m3"), Mon("m4") },
                     new[] { Crab("d1", rule), Caster("e2"), Mon("e3", x => x.Str = 120), Caster("e4") }),
                };
                foreach (var (A, B) in shapes)
                {
                    var once = BattleSim.Resolve(r, A, B, 11);
                    Assert.That(BattleSim.Resolve(r, A, B, 11).Hash, Is.EqualTo(once.Hash), rule);
                    Assert.That(once.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut), rule + " " + A.Length + "v" + B.Length);

                    var s = BattleSim.Start(r, A, B);
                    var rng = new RngStream(BattleSim.StreamName, 11);
                    var log = new List<FightAction>();
                    for (int i = 0; i < 60 && s.Outcome == FightOutcome.Ongoing; i++) BattleSim.Step(r, s, rng, log.Add);
                    Assert.That(log.Any(x => x.Event == FightAction.RulePrefix + rule), Is.True, rule + " fired in " + A.Length + "v" + B.Length);
                    var copy = s.Clone();
                    var rngCopy = rng.Clone();
                    while (BattleSim.Step(r, copy, rngCopy) == FightOutcome.Ongoing) { }
                    Assert.That(BattleSim.StateHash(copy), Is.EqualTo(once.Hash), rule);
                }
            }
        }

        [Test]
        public void EvadedHits_LogNoConversion()
        {
            // Every roll succeeds: each physical hit is evaded, so shellcrack never logs.
            var r = Rules;
            var log = Play(r, Fast("a", x => x.Int = 0, RuleModifiers.Shellcrack), Wall("b", x => { x.Def = 800; x.Dex = 900; }), 3, 0);
            Assert.That(Hits(log).All(x => x.Evaded), Is.True);
            Assert.That(log.Any(x => x.Event == BattleSim.CrackEvent), Is.False);
        }

        // ------------------------------------------------------------------ tide_moat

        [Test]
        public void TideMoat_MagicalHitsMeetTheHigherOfWisAndDef()
        {
            var r = Rules;
            Action<MonsterStats> caster = x => { x.Int = 600; x.Str = 0; };
            Action<MonsterStats> keep = x => { x.Wis = 100; x.Def = 800; };
            double raw = BattleSim.BasicAttackRaw(r, 0, 600);
            Assert.That(Hits(Play(r, Fast("a", caster), Wall("b", keep), 3, 0.99)).All(x => Math.Abs(x.Amount - BattleSim.Mitigate(r, raw, 100)) < 1e-9));

            var log = Play(r, Fast("a", caster), Wall("b", keep, RuleModifiers.TideMoat), 3, 0.99);
            double moat = r.MoatDef * 800;
            Assert.That(moat, Is.GreaterThan(100));
            Assert.That(Hits(log).Select(x => x.Amount), Is.All.EqualTo(BattleSim.Mitigate(r, raw, moat)).Within(1e-9));
            Assert.That(log.Where(x => x.Event == BattleSim.MoatEvent).Select(x => (x.Actor, x.Target, x.Amount)),
                Is.All.EqualTo(("b", "a", moat)));
            Assert.That(log.Count(x => x.Event == BattleSim.MoatEvent), Is.EqualTo(3));

            // Physical hits keep DEF; a WIS above the moat keeps WIS (no log).
            var phys = Play(r, Fast("a", x => x.Int = 0), Wall("b", keep, RuleModifiers.TideMoat), 3, 0.99);
            Assert.That(Hits(phys).Select(x => x.Amount), Is.All.EqualTo(BattleSim.Mitigate(r, BattleSim.BasicAttackRaw(r, 300, 0), 800)).Within(1e-9));
            Assert.That(phys.Any(x => x.Event == BattleSim.MoatEvent), Is.False);
            var wise = Play(r, Fast("a", caster), Wall("b", x => { keep(x); x.Wis = 900; }, RuleModifiers.TideMoat), 3, 0.99);
            Assert.That(Hits(wise).Select(x => x.Amount), Is.All.EqualTo(BattleSim.Mitigate(r, raw, 900)).Within(1e-9));
            Assert.That(wise.Any(x => x.Event == BattleSim.MoatEvent), Is.False);
        }

        // ------------------------------------------------------------------ unbreached

        [Test]
        public void Unbreached_BlocksSmallHits_UntilTheWallErodes()
        {
            var r = Rules;
            Action<MonsterStats> keep = x => x.Def = 900;
            double hit = BattleSim.Mitigate(r, BattleSim.BasicAttackRaw(r, 30, 0), 900);
            int fall = (int)Math.Ceiling(1 / r.WallErode - 1e-9);
            Assert.That(hit, Is.LessThanOrEqualTo(r.WallDef * 900 / fall), "the test hit is under the wall's last step");
            double hp = 0;
            var log = Play(r, Fast("a", x => { x.Int = 0; x.Str = 30; }), Wall("b", keep, RuleModifiers.Unbreached), fall + 5, 0.99, s => hp = s.Units[1].MaxHp - s.Units[1].Hp);
            var hits = Hits(log);
            Assert.That(hits.Take(fall).All(x => x.Amount == 0), Is.True, "blocked while the wall stands");
            Assert.That(hits.Skip(fall).All(x => Math.Abs(x.Amount - hit) < 1e-9), Is.True, "the walls are down");
            Assert.That(hp, Is.EqualTo(5 * hit).Within(1e-6));
            var walls = log.Where(x => x.Event == BattleSim.WallEvent).ToList();
            Assert.That(walls.Count, Is.EqualTo(fall));
            Assert.That(walls[0].Amount, Is.EqualTo(r.WallDef * 900 * (1 - r.WallErode)).Within(1e-9));
            Assert.That(walls[fall - 1].Amount, Is.EqualTo(0));
            Assert.That(walls.All(x => x.Actor == "b" && x.Target == "a"), Is.True);
        }

        [Test]
        public void Unbreached_BigHitsGetThrough_AndDoNotErode()
        {
            var r = Rules;
            Action<MonsterStats> keep = x => x.Def = 100;   // wall 25, far under any hit here
            var s = BattleSim.Start(r, new[] { Fast("a", x => x.Int = 0) }, new[] { Wall("b", keep, RuleModifiers.Unbreached) });
            var log = new List<FightAction>();
            while (s.Units[0].Tally.Actions < 4) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(Hits(log).All(x => x.Amount > 0), Is.True);
            Assert.That(s.Units[1].WallBlocks, Is.EqualTo(0));
            Assert.That(log.Any(x => x.Event == BattleSim.WallEvent), Is.False);
        }

        [Test]
        public void Unbreached_BlockedHit_FiresOnHitTaken_ButNotResentment()
        {
            var r = Rules;
            var thorns = new CombatSkill { Id = "thorns", Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = StatIds.Def, Power = 0.1 };
            var keep = Wall("b", x => x.Def = 900, RuleModifiers.Unbreached, RuleModifiers.Resentment);
            keep.Triggers.Add(new TriggeredSkill(FightTrigger.OnHitTaken, thorns));
            var s = BattleSim.Start(r, new[] { Fast("a", x => x.Int = 0) }, new[] { keep });
            var log = new List<FightAction>();
            while (s.Units[0].Tally.Actions < 3) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(Hits(log).All(x => x.Amount == 0), Is.True);
            Assert.That(log.Count(x => x.Actor == "b" && x.Skill?.Id == "thorns"), Is.EqualTo(3), "thorns fire on every blocked blow");
            Assert.That(s.Units[1].ResentPool, Is.EqualTo(0), "no HP lost, so resentment banks nothing");
            Assert.That(s.Units[1].Hp, Is.EqualTo(s.Units[1].MaxHp));
        }

        [Test]
        public void Unbreached_TrueDamagePasses()
        {
            // counter_mitigated's counter is true damage: it reaches the crab whatever its wall.
            var r = Rules;
            var s = BattleSim.Start(r, new[] { Fast("a", x => { x.Int = 0; x.Def = 900; x.Hp = 99999; }, RuleModifiers.Unbreached) },
                new[] { Wall("b", x => x.Def = 100, RuleModifiers.CounterMitigated) });
            var log = new List<FightAction>();
            while (s.Units[0].Tally.Actions < 3) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            var counters = log.Where(x => x.Counter && x.Actor == "b").ToList();
            Assert.That(counters.Count, Is.EqualTo(3));
            Assert.That(counters.All(x => x.Amount > 0 && x.Amount <= r.WallDef * 900), Is.True, "small, but never blocked");
            Assert.That(s.Units[0].WallBlocks, Is.EqualTo(0));

            // wail_stagger's wither (max-HP damage) passes too, while the wall blocks the wailer's small hits.
            var keep = Wall("b", x => { x.Def = 900; x.Hp = 100; }, RuleModifiers.Unbreached);
            var wail = BattleSim.Start(r, new[] { Fast("a", x => { x.Int = 0; x.Str = 30; }, RuleModifiers.WailStagger) }, new[] { keep });
            var wlog = new List<FightAction>();
            while (wail.Units[0].Tally.Actions < 2) BattleSim.Step(r, wail, new Fixed(0.99), wlog.Add);
            double wither = r.WailWither * wail.Units[1].MaxHp;
            Assert.That(wither, Is.LessThan(r.WallDef * 900), "under the wall");
            Assert.That(Hits(wlog).All(x => x.Amount == 0), Is.True, "the hits are blocked");
            Assert.That(wail.Units[1].MaxHp - wail.Units[1].Hp, Is.EqualTo(2 * wither).Within(1e-6), "the wither is not");
        }

        [Test]
        public void Unbreached_TwoCarriers_EndBeforeTheTimeLimit()
        {
            var r = Rules;
            Action<MonsterStats> crab = x => { x.Hp = 400; x.Str = 0; x.Int = 0; x.Def = 900; x.Spd = 300; };
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var res = BattleSim.Resolve(r, new[] { Mon("a", x => { crab(x); x.Str = 300; }, null, RuleModifiers.Unbreached) },
                    new[] { Mon("b", x => { crab(x); x.Str = 300; }, null, RuleModifiers.Unbreached) }, seed);
                Assert.That(res.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut), "seed " + seed);
                Assert.That(res.Ticks, Is.LessThan(r.TimeLimitTicks), "seed " + seed);
            }
        }

        [Test]
        public void Unbreached_WallIsHashedForCarriersOnly()
        {
            var r = Rules;
            FightState Fight(params string[] rules)
            {
                var s = BattleSim.Start(r, new[] { Fast("a", x => x.Int = 0) }, new[] { Wall("b", x => x.Def = 900, rules) });
                while (s.Units[0].Tally.Actions < 2) BattleSim.Step(r, s, new Fixed(0.99));
                return s;
            }
            var a = Fight(RuleModifiers.Unbreached);
            var b = Fight(RuleModifiers.Unbreached);
            Assert.That(BattleSim.StateHash(a), Is.EqualTo(BattleSim.StateHash(b)));
            b.Units[1].WallBlocks++;
            Assert.That(BattleSim.StateHash(a), Is.Not.EqualTo(BattleSim.StateHash(b)));
            var plain = Fight();
            string h = BattleSim.StateHash(plain);
            plain.Units[1].WallBlocks = 7;
            Assert.That(BattleSim.StateHash(plain), Is.EqualTo(h), "not hashed without the rule");
        }

        // ------------------------------------------------------------------ shellcrack

        [Test]
        public void Shellcrack_TurnsTheTargetsDefIntoDamage()
        {
            var r = Rules;
            double raw = BattleSim.BasicAttackRaw(r, 300, 0);
            var log = Play(r, Fast("a", x => x.Int = 0, RuleModifiers.Shellcrack), Wall("b", x => x.Def = 800), 3, 0.99);
            double add = r.CrackDef * 800;
            Assert.That(Hits(log).Select(x => x.Amount), Is.All.EqualTo(raw + add).Within(1e-9), "no mitigation, DEF added");
            Assert.That(log.Where(x => x.Event == BattleSim.CrackEvent).Select(x => (x.Actor, x.Target, x.Amount)), Is.All.EqualTo(("a", "b", add)));

            // fire_pierce on the same unit: shellcrack wins for physical hits (nothing is left to pierce).
            var fire = Mon("a", x => { x.Spd = 900; x.Int = 0; }, RuleModifiers.FireElement, RuleModifiers.Shellcrack, RuleModifiers.FirePierce);
            var both = Hits(Play(r, fire, Wall("b", x => x.Def = 800), 3, 0.99));
            double fireMult = Math.Max(1.0, r.ElementMult(RuleModifiers.FireElement, null)) * (1 + r.FireBonus);
            Assert.That(both.Select(x => x.Amount), Is.All.EqualTo((raw + add) * fireMult).Within(1e-9));

            // A 0-DEF target adds nothing (and logs nothing).
            var bare = Play(r, Fast("a", x => x.Int = 0, RuleModifiers.Shellcrack), Wall("b", x => x.Def = 0), 3, 0.99);
            Assert.That(Hits(bare).Select(x => x.Amount), Is.All.EqualTo(raw).Within(1e-9));
            Assert.That(bare.Any(x => x.Event == BattleSim.CrackEvent), Is.False);

            // Magical hits still meet WIS.
            var magic = Hits(Play(r, Fast("a", x => { x.Int = 600; x.Str = 0; }, RuleModifiers.Shellcrack), Wall("b", x => x.Def = 800), 3, 0.99));
            Assert.That(magic.Select(x => x.Amount), Is.All.EqualTo(BattleSim.Mitigate(r, BattleSim.BasicAttackRaw(r, 0, 600), 300)).Within(1e-9));
        }

        [Test]
        public void Shellcrack_WithFortressFist_IsDefAgainstDef()
        {
            var r = Rules;
            var crab = Fast("a", x => { x.Str = 0; x.Int = 0; x.Def = 900; }, RuleModifiers.FortressFist, RuleModifiers.Shellcrack);
            var hits = Hits(Play(r, crab, Wall("b", x => x.Def = 600), 3, 0.99));
            Assert.That(hits.Select(x => x.Amount), Is.All.EqualTo(r.FortressDef * 900 + r.CrackDef * 600).Within(1e-9));
            var skill = With(Fast("a", x => { x.Str = 0; x.Int = 0; x.Def = 900; }, RuleModifiers.FortressFist, RuleModifiers.Shellcrack),
                Dmg("claw", 1.5, StatIds.Def));
            var sk = Play(r, skill, Wall("b", x => x.Def = 600), 1, 0.99).First(x => x.Skill?.Id == "claw");
            Assert.That(sk.Amount, Is.GreaterThan(r.FortressDef * 900 + r.CrackDef * 600));
        }
    }
}
