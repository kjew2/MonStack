using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Species Candlemane (#141): its kit in content and its two chase rules, martyr_flame and hollow_sun.</summary>
    public class CandlemaneRuleTests
    {
        static FightRules Rules => FightRules.FromContent(TestContent.Db);

        static readonly string[] SpeciesRules = { RuleModifiers.MartyrFlame, RuleModifiers.HollowSun };

        /// <summary>Always the same draw: 0.99 = no evade or crit roll succeeds (and a random pick takes the last unit).</summary>
        sealed class Fixed : IRandomSource
        {
            readonly double _v;
            public Fixed(double v) { _v = v; }
            public double NextDouble() => _v;
        }

        static Fighter Mon(string id, Action<MonsterStats> tweak = null, params string[] rules)
        {
            var st = new MonsterStats { Hp = 300, Str = 300, Int = 0, Wis = 300, Dex = 300, Def = 300, Spd = 300, Luk = 0 };
            tweak?.Invoke(st);
            var f = new Fighter { Id = id, Stats = st };
            f.Rules.AddRange(rules);
            return f;
        }

        /// <summary>A fast unit: it acts many times before the slow, very tough, harmless target acts once.</summary>
        static Fighter Fast(string id, params string[] rules) => Mon(id, x => x.Spd = 900, rules);

        static Fighter Wall(string id) => Mon(id, x => { x.Spd = 10; x.Hp = 99999; x.Str = 0; });

        static CombatSkill SelfHeal(string id, double power) =>
            new CombatSkill { Id = id, Kind = SkillKind.Heal, Target = SkillTarget.Self, Scales = StatIds.Wis, Power = power };

        /// <summary>Steps until side A's first unit has taken <paramref name="actions"/> actions; side B never acts.</summary>
        static (FightState S, List<FightAction> Log) Play(FightRules r, Fighter[] a, Fighter[] b, int actions, Action<FightState> setup = null)
        {
            var s = BattleSim.Start(r, a, b);
            setup?.Invoke(s);
            var log = new List<FightAction>();
            while (s.Units[0].Tally.Actions < actions && s.Outcome == FightOutcome.Ongoing) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            foreach (var u in s.Units.Where(u => u.Side == 1)) Assert.That(u.Tally.Actions, Is.EqualTo(0), "side B never acted");
            return (s, log);
        }

        // ------------------------------------------------------------------ content

        [Test]
        public void Content_SpeciesKit()
        {
            var db = TestContent.Db;
            var sp = db.Species["candlemane"];
            Assert.That((sp.Element, sp.NativeTree1, sp.NativeTree2, sp.ChaseTreeRun, sp.ChaseTreeBook),
                        Is.EqualTo(("light", "blood_magic", "sanctum_grace", "martyrs_pyre", "hollow_sun")));
            Assert.That(db.SpeciesList[0].Id, Is.Not.EqualTo("candlemane"), "the starter stays first");

            // blood_magic is Candlemane's, native-only now (kj, 2026-11-03): never in an Awakening draft.
            var blood = db.Trees["blood_magic"];
            Assert.That((blood.Kind, blood.OfferStat, blood.OfferThreshold), Is.EqualTo((TreeKind.Native, (string)null, 0)));
            Assert.That(db.SpeciesList.Count(s => s.NativeTree1 == "blood_magic" || s.NativeTree2 == "blood_magic"), Is.EqualTo(1));
            Assert.That(blood.Nodes[4].Skill.Id, Is.EqualTo("votive_strike"));
            Assert.That((blood.Nodes[4].Skill.Kind, blood.Nodes[4].Skill.Element), Is.EqualTo((SkillKind.Damage, "light")));

            // Sanctum Grace: Halo Mend at index 4; its capstone casts Halo Mend at half power when hit.
            var grace = db.Trees["sanctum_grace"];
            Assert.That(grace.Kind, Is.EqualTo(TreeKind.Native));
            var mend = grace.Nodes[4].Skill;
            var cap = grace.Nodes[7];
            Assert.That((mend.Id, mend.Kind, mend.Target, mend.Scales), Is.EqualTo(("halo_mend", SkillKind.Heal, SkillTarget.Self, StatIds.Wis)));
            Assert.That((cap.Kind, cap.Trigger, cap.Skill.Kind, cap.Skill.Target, cap.Skill.Scales),
                        Is.EqualTo((NodeKind.Trigger, (FightTrigger?)FightTrigger.OnHitTaken, SkillKind.Heal, SkillTarget.Self, StatIds.Wis)));
            Assert.That(cap.Skill.Power, Is.EqualTo(0.5 * mend.Power).Within(1e-12));

            // The chase trees: the in-run one asks for no book, the second names the Psalter.
            var pyre = db.Trees["martyrs_pyre"];
            var sun = db.Trees["hollow_sun"];
            Assert.That((pyre.Kind, pyre.Species, pyre.BookGated, pyre.Nodes.Last().RuleId), Is.EqualTo((TreeKind.Chase, "candlemane", false, RuleModifiers.MartyrFlame)));
            Assert.That((sun.Kind, sun.Species, sun.BookGated, sun.SecondChase, sun.Nodes.Last().RuleId),
                        Is.EqualTo((TreeKind.Chase, "candlemane", true, true, RuleModifiers.HollowSun)));
            Assert.That(sun.Conditions.Select(c => c.Ref), Is.EquivalentTo(new[] { "unburnt_psalter_n0", "sanctum_grace_n4", "damage_light" }));
            Assert.That(db.Trees["unburnt_psalter"].Kind, Is.EqualTo(TreeKind.Book));
            Assert.That(db.SkillBooks.Values.Count(b => b.TreeId == "unburnt_psalter"), Is.EqualTo(1));
            Assert.That(db.BookDrops.Count(d => db.SkillBooks[d.BookId].TreeId == "unburnt_psalter"), Is.EqualTo(1));
            foreach (var t in new[] { pyre, sun }) Assert.That(db.Text(t.ApexNameKey), Is.Not.EqualTo(t.ApexNameKey), t.Id);

            // It is unlocked by an achievement.
            Assert.That(db.Achievements.Values.Count(a => a.UnlocksSpecies == "candlemane"), Is.EqualTo(1));

            var r = Rules;
            Assert.That((r.PyreConv, r.SunRelease, r.SunHeal), Is.EqualTo((db.ConstantValues["MON_PYRE_CONV"],
                        db.ConstantValues["MON_SUN_RELEASE"], db.ConstantValues["MON_SUN_HEAL"])));
            foreach (var id in SpeciesRules)
            {
                Assert.That(RuleModifiers.IsKnown(id), id);
                var desc = BattleText.RuleDesc(db, id);
                Assert.That(TextTemplate.HasPlaceholder(desc), Is.False, desc);
                Assert.That(db.Text("rule." + id + ".name"), Is.Not.EqualTo("rule." + id + ".name"), id);
            }
        }

        [Test]
        public void Validation_RejectsConstantsOutOfRange()
        {
            var a = new[] { Mon("a") };
            var b = new[] { Mon("b") };
            foreach (var bad in new Action<FightRules>[]
                     {
                         r => r.PyreConv = -0.1, r => r.PyreConv = double.PositiveInfinity, r => r.SunRelease = 1.1,
                         r => r.SunRelease = double.NaN, r => r.SunHeal = -1,
                     })
            {
                var r = Rules;
                bad(r);
                Assert.Throws<ArgumentException>(() => BattleSim.Start(r, a, b));
            }
        }

        // ------------------------------------------------------------------ martyr_flame

        [Test]
        public void MartyrFlame_HealDealsAShareAsLightTrueDamage()
        {
            var r = Rules;
            Fighter A(params string[] rules)
            {
                var a = Fast("a", rules);
                a.Skills.Add(SelfHeal("mend", 0.5));
                return a;
            }
            var (s, log) = Play(r, new[] { A(RuleModifiers.MartyrFlame) }, new[] { Wall("b") }, 1, st => st.Units[0].Hp = 1);
            Assert.That(BattleSim.HealMult(r, s), Is.EqualTo(1));
            double healed = log.Single(x => x.Skill?.Id == "mend").Amount;
            Assert.That(healed, Is.EqualTo(150).Within(1e-9));
            var pyre = log.Single(x => x.Event == BattleSim.PyreEvent);
            Assert.That((pyre.Actor, pyre.Target), Is.EqualTo(("a", "b")));
            Assert.That(pyre.Amount, Is.EqualTo(r.PyreConv * healed).Within(1e-9));
            var b = s.Units[1];
            Assert.That(b.MaxHp - b.Hp, Is.EqualTo(pyre.Amount).Within(1e-9), "true damage: no mitigation (b has 300 DEF and WIS)");
            Assert.That(s.Units[0].Tally.DamageByElement["light"], Is.EqualTo(pyre.Amount).Within(1e-9));
            Assert.That(b.Tally.Grazes + b.Tally.Evades, Is.EqualTo(0));

            // Without the rule: the same heal, no damage.
            (s, log) = Play(r, new[] { A() }, new[] { Wall("b") }, 1, st => st.Units[0].Hp = 1);
            Assert.That(log.Single(x => x.Skill?.Id == "mend").Amount, Is.EqualTo(healed));
            Assert.That(log.Any(x => x.Event == BattleSim.PyreEvent), Is.False);
            Assert.That(s.Units[1].Hp, Is.EqualTo(s.Units[1].MaxHp));
        }

        [Test]
        public void MartyrFlame_OverhealConvertsInFull()
        {
            var r = Rules;
            var a = Fast("a", RuleModifiers.MartyrFlame);
            a.Triggers.Add(new TriggeredSkill(FightTrigger.OnAction, SelfHeal("glow", 0.5)));   // 150, triggered: no AI condition
            var (s, log) = Play(r, new[] { a }, new[] { Wall("b") }, 1, st => st.Units[0].Hp = st.Units[0].MaxHp - 10);
            Assert.That(log.Single(x => x.Skill?.Id == "glow").Amount, Is.EqualTo(10).Within(1e-9));
            var pyre = log.Single(x => x.Event == BattleSim.PyreEvent);
            Assert.That(pyre.Amount, Is.EqualTo(r.PyreConv * 10 + (150 - 10)).Within(1e-9));

            // At full HP the whole heal is overheal: it all converts.
            (s, log) = Play(r, new[] { a }, new[] { Wall("b") }, 1);
            Assert.That(log.Single(x => x.Event == BattleSim.PyreEvent).Amount, Is.EqualTo(150).Within(1e-9));
        }

        [Test]
        public void MartyrFlame_OverhealIntoAShieldIsNotLost()
        {
            var r = Rules;
            var a = Mon("a", x => { x.Spd = 900; x.Wis = 4000; }, RuleModifiers.MartyrFlame, RuleModifiers.TidalOverflow);
            a.Triggers.Add(new TriggeredSkill(FightTrigger.OnAction, SelfHeal("glow", 0.5)));   // 2000
            var (s, log) = Play(r, new[] { a }, new[] { Wall("b") }, 1);
            double shieldCap = r.TideShieldCap * s.Units[0].MaxHp, overheal = r.TideRegen * s.Units[0].MaxHp + 2000;
            Assert.That(shieldCap, Is.GreaterThan(0).And.LessThan(overheal), "the shield takes only part of the overheal");
            Assert.That(s.Units[0].Shield, Is.EqualTo(shieldCap).Within(1e-9));
            Assert.That(log.Where(x => x.Event == BattleSim.PyreEvent).Sum(x => x.Amount), Is.EqualTo(overheal - shieldCap).Within(1e-6),
                        "the tide regen and the heal both overheal at full HP; what the shield did not take converts");
        }

        [Test]
        public void MartyrFlame_CurrentTarget_IsTheLastEnemyHit_ElseTheFirstLiving()
        {
            var r = Rules;
            var a = Fast("a", RuleModifiers.MartyrFlame);
            a.Triggers.Add(new TriggeredSkill(FightTrigger.OnAction, SelfHeal("glow", 0.1)));
            var (s, log) = Play(r, new[] { a }, new[] { Wall("b"), Wall("c") }, 2);
            var pyres = log.Where(x => x.Event == BattleSim.PyreEvent).Select(x => x.Target).ToList();
            var hits = log.Where(x => x.Actor == "a" && x.Event == null && x.Skill == null).Select(x => x.Target).ToList();
            Assert.That(hits[0], Is.EqualTo("c"), "draw 0.99 picks the last enemy");
            Assert.That(pyres, Is.EqualTo(new[] { "b", "c" }), "before any hit the first living enemy, then the one it hit");
            Assert.That(s.Units[0].PyreTarget, Is.EqualTo(2));

            // A taunting enemy takes it before the unit has hit anything.
            (s, log) = Play(r, new[] { a }, new[] { Wall("b"), Wall("c") }, 1, st => st.Units[2].TauntTicks = 5);
            Assert.That(log.First(x => x.Event == BattleSim.PyreEvent).Target, Is.EqualTo("c"));
        }

        [Test]
        public void MartyrFlame_KindledGraceOnHit_ConvertsWithoutLooping()
        {
            // An attacker hits the carrier; Kindled Grace (OnHitTaken heal) heals it, and the pyre burns the attacker. The burn is
            // not a hit: it fires no trigger and no heal, so each enemy hit gives exactly one conversion.
            var db = TestContent.Db;
            var r = Rules;
            var grace = db.Trees["sanctum_grace"].Nodes[7];
            var c = Mon("c", x => { x.Spd = 10; x.Hp = 5000; }, RuleModifiers.MartyrFlame);
            c.Triggers.Add(new TriggeredSkill(grace.Trigger.Value, grace.Skill));
            var foe = Mon("foe", x => { x.Spd = 900; x.Str = 600; x.Hp = 99999; });
            var s = BattleSim.Start(r, new[] { c }, new[] { foe });
            var log = new List<FightAction>();
            while (s.Units[1].Tally.Actions < 3) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(s.Units[0].Tally.Actions, Is.EqualTo(0));
            var heals = log.Where(x => x.Skill?.Id == grace.Skill.Id).ToList();
            var pyres = log.Where(x => x.Event == BattleSim.PyreEvent).ToList();
            Assert.That(heals.Count, Is.EqualTo(3));
            Assert.That(pyres.Count, Is.EqualTo(3));
            Assert.That(pyres.All(p => p.Target == "foe"), Is.True);
            Assert.That(s.Units[1].Tally.DamageTaken, Is.EqualTo(pyres.Sum(p => p.Amount)).Within(1e-6));
        }

        // ------------------------------------------------------------------ hollow_sun

        [Test]
        public void HollowSun_HitReleasesRadiance_AndHealsHalfOfIt()
        {
            var r = Rules;
            var (s, log) = Play(r, new[] { Fast("a", RuleModifiers.HollowSun) }, new[] { Wall("b") }, 1,
                                st => { st.Units[0].Radiance = 100; st.Units[0].Hp = 50; });
            var sun = log.Single(x => x.Event == BattleSim.SunEvent);
            Assert.That((sun.Actor, sun.Target), Is.EqualTo(("a", "b")));
            Assert.That(sun.Amount, Is.EqualTo(r.SunRelease * 100).Within(1e-9));
            var a = s.Units[0];
            Assert.That(a.Radiance, Is.EqualTo(100 - sun.Amount).Within(1e-9));
            Assert.That(a.Hp, Is.EqualTo(50 + r.SunHeal * sun.Amount).Within(1e-9));
            Assert.That(a.Tally.HealingDone, Is.EqualTo(r.SunHeal * sun.Amount).Within(1e-9));
            double basic = log.Single(x => x.Actor == "a" && x.Event == null).Amount;
            Assert.That(s.Units[1].MaxHp - s.Units[1].Hp, Is.EqualTo(basic + sun.Amount).Within(1e-9), "the release is unmitigated");
            Assert.That(a.Tally.DamageByElement["light"], Is.EqualTo(sun.Amount).Within(1e-9));

            // Without the rule the stored Radiance does nothing.
            (s, log) = Play(r, new[] { Fast("a") }, new[] { Wall("b") }, 1, st => { st.Units[0].Radiance = 100; st.Units[0].Hp = 50; });
            Assert.That(log.Any(x => x.Event == BattleSim.SunEvent), Is.False);
            Assert.That(s.Units[0].Hp, Is.EqualTo(50));
        }

        [Test]
        public void HollowSun_BloodMagicCostsCharge_ThenTheNextHitReleases()
        {
            var r = Rules;
            var a = Fast("a", RuleModifiers.HollowSun, RuleModifiers.BloodMagic);
            a.Skills.Add(new CombatSkill { Id = "slash", Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = StatIds.Str, Power = 0.01, Cooldown = 3 });
            var (s, log) = Play(r, new[] { a }, new[] { Wall("b") }, 1);
            double cost = log.Single(x => x.Event == BattleSim.BloodEvent).Amount;
            Assert.That(log.Any(x => x.Event == BattleSim.SunEvent), Is.False, "the hit came before the cost: nothing stored yet");
            Assert.That(s.Units[0].Radiance, Is.EqualTo(cost).Within(1e-9));

            (s, log) = Play(r, new[] { a }, new[] { Wall("b") }, 2);
            Assert.That(log.Single(x => x.Event == BattleSim.SunEvent).Amount, Is.EqualTo(r.SunRelease * cost).Within(1e-9));
            Assert.That(s.Units[0].Radiance, Is.EqualTo((1 - r.SunRelease) * cost + cost).Within(1e-9));
        }

        [Test]
        public void HollowSun_HitsTakenCharge_UpToMaxHp()
        {
            var r = Rules;
            var c = Mon("c", x => { x.Spd = 10; x.Hp = 5000; }, RuleModifiers.HollowSun);
            var foe = Mon("foe", x => { x.Spd = 900; x.Str = 600; x.Hp = 99999; });
            var s = BattleSim.Start(r, new[] { c }, new[] { foe });
            while (s.Units[1].Tally.Actions < 2) BattleSim.Step(r, s, new Fixed(0.99));
            var u = s.Units[0];
            Assert.That(u.Tally.DamageTaken, Is.GreaterThan(0));
            Assert.That(u.Radiance, Is.EqualTo(u.Tally.DamageTaken).Within(1e-9));

            s = BattleSim.Start(r, new[] { c }, new[] { foe });
            s.Units[0].Radiance = s.Units[0].MaxHp - 1;
            while (s.Units[1].Tally.Actions < 1) BattleSim.Step(r, s, new Fixed(0.99));
            Assert.That(s.Units[0].Radiance, Is.EqualTo(s.Units[0].MaxHp));
        }

        [Test]
        public void HollowSun_AndMartyrFlame_ReleaseHealFeedsThePyreOnce()
        {
            var r = Rules;
            var (s, log) = Play(r, new[] { Fast("a", RuleModifiers.HollowSun, RuleModifiers.MartyrFlame) }, new[] { Wall("b") }, 1,
                                st => { st.Units[0].Radiance = 400; st.Units[0].Hp = 1; });
            var sun = log.Single(x => x.Event == BattleSim.SunEvent);
            var pyre = log.Single(x => x.Event == BattleSim.PyreEvent);
            Assert.That(pyre.Amount, Is.EqualTo(r.PyreConv * r.SunHeal * sun.Amount).Within(1e-9));
            Assert.That(log.IndexOf(pyre), Is.GreaterThan(log.IndexOf(sun)));
        }

        // ------------------------------------------------------------------ modes, determinism, hash

        [Test]
        public void NoCarrier_SameFightAsBefore_StateHashOnlyForCarriers()
        {
            var r = Rules;
            Fighter[] A() => new[] { Mon("a", x => x.Spd = 400) };
            Fighter[] B() => new[] { Mon("b") };
            var s = BattleSim.Start(r, A(), B());
            string before = BattleSim.StateHash(s);
            s.Units[0].Radiance = 7;
            s.Units[0].PyreTarget = 1;
            Assert.That(BattleSim.StateHash(s), Is.EqualTo(before), "a unit without the rules hashes none of their state");

            var c = Mon("a", x => x.Spd = 400, RuleModifiers.HollowSun, RuleModifiers.MartyrFlame);
            s = BattleSim.Start(r, new[] { c }, B());
            before = BattleSim.StateHash(s);
            s.Units[0].Radiance = 7;
            Assert.That(BattleSim.StateHash(s), Is.Not.EqualTo(before));
            before = BattleSim.StateHash(s);
            s.Units[0].PyreTarget = 1;
            Assert.That(BattleSim.StateHash(s), Is.Not.EqualTo(before));
        }

        static Fighter Carrier(string id, string rule)
        {
            var f = Mon(id, x => { x.Hp = 600; x.Wis = 400; x.Def = 60; }, rule, RuleModifiers.BloodMagic);
            f.Skills.Add(new CombatSkill { Id = "votive", Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = StatIds.Hp, Power = 0.12, Cooldown = 2, Element = "light" });
            f.Skills.Add(new CombatSkill { Id = "mend", Kind = SkillKind.Heal, Target = SkillTarget.Self, Scales = StatIds.Wis, Power = 2.0, Cooldown = 3 });
            f.Triggers.Add(new TriggeredSkill(FightTrigger.OnHitTaken, SelfHeal("kindle", 0.5)));
            return f;
        }

        [Test]
        public void EveryRule_In1v1_4v1_And4v4_IsDeterministic_AndCloneContinuesIdentically()
        {
            var r = Rules;
            foreach (var rule in SpeciesRules)
            {
                var shapes = new (Fighter[] A, Fighter[] B)[]
                {
                    (new[] { Carrier("c", rule) }, new[] { Mon("foe", x => { x.Hp = 500; x.Spd = 200; }) }),
                    (new[] { Carrier("c1", rule), Carrier("c2", rule), Mon("m3"), Mon("m4") },
                     new[] { Mon("boss", x => { x.Hp = 2500; x.Str = 500; x.Spd = 250; }) }),
                    (new[] { Carrier("c1", rule), Carrier("c2", rule), Mon("m3"), Mon("m4") },
                     new[] { Carrier("d1", rule), Mon("e2"), Mon("e3"), Mon("e4") }),
                };
                foreach (var (A, B) in shapes)
                {
                    var once = BattleSim.Resolve(r, A, B, 11);
                    Assert.That(BattleSim.Resolve(r, A, B, 11).Hash, Is.EqualTo(once.Hash), rule);
                    Assert.That(once.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut), rule + " " + A.Length + "v" + B.Length);

                    var s = BattleSim.Start(r, A, B);
                    var rng = new RngStream(BattleSim.StreamName, 11);
                    var log = new List<FightAction>();
                    for (int i = 0; i < 40 && s.Outcome == FightOutcome.Ongoing; i++) BattleSim.Step(r, s, rng, log.Add);
                    Assert.That(log.Any(x => x.Event == FightAction.RulePrefix + rule), Is.True, rule + " fired in " + A.Length + "v" + B.Length);
                    var copy = s.Clone();
                    var rngCopy = rng.Clone();
                    while (BattleSim.Step(r, copy, rngCopy) == FightOutcome.Ongoing) { }
                    Assert.That(BattleSim.StateHash(copy), Is.EqualTo(once.Hash), rule);
                }
            }
        }
    }
}
