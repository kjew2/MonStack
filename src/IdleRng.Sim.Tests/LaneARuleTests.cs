using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Idea-pool lane A (#134): the conversion capstones iron_reflexes, elemental_avatar, fortress_fist and blood_magic.</summary>
    public class LaneARuleTests
    {
        static FightRules Rules => FightRules.FromContent(TestContent.Db);

        static readonly string[] LaneRules = { RuleModifiers.IronReflexes, RuleModifiers.ElementalAvatar, RuleModifiers.FortressFist, RuleModifiers.BloodMagic };

        /// <summary>Always the same draw: 0 = every evade and crit roll succeeds, 0.99 = none does.</summary>
        sealed class Fixed : IRandomSource
        {
            readonly double _v;
            public Fixed(double v) { _v = v; }
            public double NextDouble() => _v;
        }

        static Fighter Mon(string id, Action<MonsterStats> tweak = null, string element = null, params string[] rules)
        {
            var st = new MonsterStats { Hp = 300, Str = 300, Int = 300, Wis = 300, Dex = 300, Def = 300, Spd = 300, Luk = 0 };
            tweak?.Invoke(st);
            var f = new Fighter { Id = id, Stats = st, Element = element };
            f.Rules.AddRange(rules);
            return f;
        }

        /// <summary>A fast attacker: it acts many times before the slow, very tough target acts once.</summary>
        static Fighter Fast(string id, Action<MonsterStats> tweak = null, string element = null, params string[] rules) =>
            Mon(id, x => { x.Spd = 900; tweak?.Invoke(x); }, element, rules);

        static Fighter Wall(string id, Action<MonsterStats> tweak = null, string element = null, params string[] rules) =>
            Mon(id, x => { x.Spd = 10; x.Hp = 99999; tweak?.Invoke(x); }, element, rules);

        static CombatSkill Dmg(string id, string scales, double power, int cd = 0, string element = null) =>
            new CombatSkill { Id = id, Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = scales, Power = power, Cooldown = cd, Element = element };

        /// <summary>Steps until side A's first unit has taken <paramref name="actions"/> actions (side B must not act).</summary>
        static List<FightAction> Play(FightRules r, Fighter a, Fighter b, int actions, double draw, Action<FightState> setup = null)
        {
            var s = BattleSim.Start(r, new[] { a }, new[] { b });
            setup?.Invoke(s);
            var log = new List<FightAction>();
            while (s.Units[0].Tally.Actions < actions && s.Outcome == FightOutcome.Ongoing) BattleSim.Step(r, s, new Fixed(draw), log.Add);
            Assert.That(s.Units[1].Tally.Actions, Is.EqualTo(0), "side B never acted");
            return log;
        }

        /// <summary>Side A's hits on b (basic attacks and skills, not rule events).</summary>
        static List<FightAction> Hits(List<FightAction> log) => log.Where(x => x.Actor == "a" && x.Event == null && x.Target == "b" && !x.Counter).ToList();

        // ------------------------------------------------------------------ content and constants

        [Test]
        public void Content_TreesAndConstants()
        {
            var db = TestContent.Db;
            var r = Rules;
            Assert.That((r.IronDex, r.AvatarBonus, r.FortressDef, r.BloodCost, r.BloodFloor), Is.EqualTo((
                db.ConstantValues["MON_IRON_DEX"], db.ConstantValues["MON_AVATAR_BONUS"], db.ConstantValues["MON_FORTRESS_DEF"],
                db.ConstantValues["MON_BLOOD_COST"], db.ConstantValues["MON_BLOOD_FLOOR"])));
            var stats = new Dictionary<string, string>
            {
                [RuleModifiers.IronReflexes] = StatIds.Dex, [RuleModifiers.ElementalAvatar] = StatIds.Int,
                [RuleModifiers.FortressFist] = StatIds.Def, [RuleModifiers.BloodMagic] = StatIds.Hp,
            };
            foreach (var id in LaneRules)
            {
                Assert.That(RuleModifiers.IsKnown(id), id);
                var t = db.Trees[id];
                IdeaPool.AssertOfferedOrNative(db, t, stats[id]);
                Assert.That(t.Nodes.Count, Is.EqualTo(8), id);
                var cap = t.Nodes[7];
                Assert.That((cap.Kind, cap.RuleId, cap.Cost), Is.EqualTo((NodeKind.Rule, id, 3)), id);
                Assert.That(t.Nodes[4].Kind, Is.EqualTo(NodeKind.Skill), id);
                Assert.That(t.Nodes.Where(n => n.Kind == NodeKind.Growth || n.Kind == NodeKind.Stat).All(n => n.Stat == stats[id]), id + ": filler on the tree's own stat");
                Assert.That(db.Text("tree." + id + ".name"), Is.Not.EqualTo("tree." + id + ".name"), id);
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
                         r => r.IronDex = -0.1, r => r.AvatarBonus = -0.1, r => r.FortressDef = double.NaN,
                         r => r.BloodCost = 1.1, r => r.BloodFloor = -0.1,
                     })
            {
                var r = Rules;
                bad(r);
                Assert.Throws<ArgumentException>(() => BattleSim.Start(r, a, b));
            }
        }

        [Test]
        public void NoCarrier_NoLaneEvents()
        {
            var log = new List<FightAction>();
            BattleSim.Resolve(Rules, new[] { Mon("a", null, "fire") }, new[] { Mon("b", null, "water") }, 1, log.Add);
            Assert.That(log.Any(x => x.Event != null && LaneRules.Any(id => x.Event == FightAction.RulePrefix + id)), Is.False);
        }

        // ------------------------------------------------------------------ iron_reflexes

        [Test]
        public void IronReflexes_NeverEvades_AndMitigatesWithDex()
        {
            var r = Rules;
            Action<MonsterStats> nimble = x => { x.Dex = 900; x.Def = 200; };
            // Every roll succeeds: without the rule each physical hit is evaded, with it none is.
            var off = Hits(Play(r, Fast("a", x => x.Dex = 0), Wall("b", nimble), 5, 0));
            Assert.That(off.All(x => x.Evaded), Is.True);
            var log = Play(r, Fast("a", x => x.Dex = 0), Wall("b", nimble, null, RuleModifiers.IronReflexes), 5, 0);
            Assert.That(Hits(log).Any(x => x.Evaded || x.Graze), Is.False);
            var start = log.Single(x => x.Event == BattleSim.IronEvent);
            Assert.That(start.Amount, Is.EqualTo(r.IronDex * 900).Within(1e-9));

            // No roll succeeds: the damage is mitigated by DEF + MON_IRON_DEX x DEX.
            var hits = Hits(Play(r, Fast("a", x => x.Dex = 0), Wall("b", nimble, null, RuleModifiers.IronReflexes), 3, 0.99));
            double expect = BattleSim.Mitigate(r, BattleSim.BasicAttackRaw(r, 300, 300), 200 + r.IronDex * 900);
            Assert.That(hits.Select(x => x.Amount), Is.All.EqualTo(expect).Within(1e-9));
        }

        [Test]
        public void IronReflexes_MagicalHits_NoGraze_MitigatedByWis()
        {
            var r = Rules;
            Action<MonsterStats> mage = x => { x.Dex = 0; x.Int = 400; };
            Action<MonsterStats> nimble = x => { x.Dex = 900; x.Wis = 250; };
            Assert.That(Hits(Play(r, Fast("a", mage), Wall("b", nimble), 5, 0)).All(x => x.Graze), Is.True);
            Assert.That(Hits(Play(r, Fast("a", mage), Wall("b", nimble, null, RuleModifiers.IronReflexes), 5, 0)).Any(x => x.Graze || x.Evaded), Is.False);
            var hits = Hits(Play(r, Fast("a", mage), Wall("b", nimble, null, RuleModifiers.IronReflexes), 3, 0.99));
            double expect = BattleSim.Mitigate(r, BattleSim.BasicAttackRaw(r, 300, 400), 250);
            Assert.That(hits.Select(x => x.Amount), Is.All.EqualTo(expect).Within(1e-9), "WIS alone: DEX only helps against physical hits");
        }

        [Test]
        public void IronReflexes_WinsOverUmbralVeil()
        {
            // umbral_veil would turn every graze into an evade at a higher cap; iron_reflexes sets the chance to 0 first.
            var r = Rules;
            Action<MonsterStats> nimble = x => x.Dex = 900;
            var veil = Hits(Play(r, Fast("a", x => { x.Dex = 0; x.Int = 400; }), Wall("b", nimble, null, RuleModifiers.UmbralVeil), 5, 0));
            Assert.That(veil.All(x => x.Evaded), Is.True);
            var both = Hits(Play(r, Fast("a", x => { x.Dex = 0; x.Int = 400; }), Wall("b", nimble, null, RuleModifiers.UmbralVeil, RuleModifiers.IronReflexes), 5, 0));
            Assert.That(both.Any(x => x.Evaded || x.Graze), Is.False);
        }

        // ------------------------------------------------------------------ elemental_avatar

        [Test]
        public void ElementalAvatar_HitsTakeOwnElement_AndBonus()
        {
            var r = Rules;
            Fighter A(params string[] rules)
            {
                var f = Fast("a", null, "fire", rules);
                f.Skills.Add(Dmg("vine", StatIds.Str, 1.0, 0, "nature"));   // nature beats water; fire is weak against it
                return f;
            }
            double raw = 300 * 1.0;
            var off = Hits(Play(r, A(), Wall("b", null, "water"), 3, 0.99));
            Assert.That(off.Select(x => x.Amount), Is.All.EqualTo(BattleSim.Mitigate(r, raw * r.ElementMult("nature", "water"), 300)).Within(1e-9));
            var log = Play(r, A(RuleModifiers.ElementalAvatar), Wall("b", null, "water"), 3, 0.99);
            Assert.That(r.ElementMult("fire", "water"), Is.Not.EqualTo(r.ElementMult("nature", "water")));
            double expect = BattleSim.Mitigate(r, raw * r.ElementMult("fire", "water") * (1 + r.AvatarBonus), 300);
            Assert.That(Hits(log).Select(x => x.Amount), Is.All.EqualTo(expect).Within(1e-9));
            Assert.That(log.Single(x => x.Event == BattleSim.AvatarEvent).Amount, Is.EqualTo(r.AvatarBonus));
        }

        [Test]
        public void ElementalAvatar_TriggeredHits_AndBasicAttacks()
        {
            var r = Rules;
            var a = Fast("a", null, "fire", RuleModifiers.ElementalAvatar);
            a.Triggers.Add(new TriggeredSkill(FightTrigger.OnAction, Dmg("splash", StatIds.Str, 0.5, 0, "water")));
            var log = Play(r, a, Wall("b", null, "nature"), 3, 0.99);
            double mult = r.ElementMult("fire", "nature") * (1 + r.AvatarBonus);
            var hits = Hits(log);
            Assert.That(hits.Where(x => x.Skill?.Id == "splash").Select(x => x.Amount), Is.All.EqualTo(BattleSim.Mitigate(r, 150 * mult, 300)).Within(1e-9));
            Assert.That(hits.Where(x => x.Skill == null).Select(x => x.Amount),
                Is.All.EqualTo(BattleSim.Mitigate(r, BattleSim.BasicAttackRaw(r, 300, 300) * mult, 300)).Within(1e-9));
            Assert.That(hits.Count(x => x.Skill?.Id == "splash"), Is.EqualTo(3));
        }

        [Test]
        public void ElementalAvatar_NoElement_DoesNothing()
        {
            var r = Rules;
            var off = Hits(Play(r, Fast("a"), Wall("b", null, "water"), 3, 0.99));
            var log = Play(r, Fast("a", null, null, RuleModifiers.ElementalAvatar), Wall("b", null, "water"), 3, 0.99);
            Assert.That(Hits(log).Select(x => x.Amount), Is.EqualTo(off.Select(x => x.Amount)));
            Assert.That(log.Any(x => x.Event == BattleSim.AvatarEvent), Is.False);
        }

        // ------------------------------------------------------------------ fortress_fist

        [Test]
        public void FortressFist_StrAndIntCountZero_DefAdds()
        {
            var r = Rules;
            Action<MonsterStats> tank = x => { x.Def = 400; x.Str = 500; x.Int = 100; };
            Action<MonsterStats> target = x => { x.Def = 200; x.Wis = 600; };
            double add = r.FortressDef * 400;

            // Basic attack: only the DEF part.
            var log = Play(r, Fast("a", tank, null, RuleModifiers.FortressFist), Wall("b", target), 3, 0.99);
            Assert.That(Hits(log).Select(x => x.Amount), Is.All.EqualTo(BattleSim.Mitigate(r, add, 200)).Within(1e-9));
            Assert.That(log.Single(x => x.Event == BattleSim.FortressEvent).Amount, Is.EqualTo(add).Within(1e-9));

            // A skill scaling STR brings nothing of its own; one scaling DEF keeps its damage and adds the DEF part.
            var a = Fast("a", tank, null, RuleModifiers.FortressFist);
            a.Skills.Add(Dmg("punch", StatIds.Str, 2.0));
            Assert.That(Hits(Play(r, a, Wall("b", target), 3, 0.99)).Where(x => x.Skill?.Id == "punch").Select(x => x.Amount),
                Is.All.EqualTo(BattleSim.Mitigate(r, add, 200)).Within(1e-9));
            a = Fast("a", tank, null, RuleModifiers.FortressFist);
            a.Skills.Add(Dmg("press", StatIds.Def, 1.0));
            Assert.That(Hits(Play(r, a, Wall("b", target), 3, 0.99)).Where(x => x.Skill?.Id == "press").Select(x => x.Amount),
                Is.All.EqualTo(BattleSim.Mitigate(r, 400 + add, 200)).Within(1e-9));
        }

        [Test]
        public void FortressFist_MagicalBasicAttack_BecomesPhysical()
        {
            var r = Rules;
            Action<MonsterStats> caster = x => { x.Def = 400; x.Str = 100; x.Int = 500; };
            Action<MonsterStats> target = x => { x.Def = 200; x.Wis = 600; };
            var hits = Hits(Play(r, Fast("a", caster, null, RuleModifiers.FortressFist), Wall("b", target), 3, 0.99));
            Assert.That(hits.Select(x => x.Amount), Is.All.EqualTo(BattleSim.Mitigate(r, r.FortressDef * 400, 200)).Within(1e-9), "mitigated by DEF, not WIS");
            // Every roll succeeds: a physical hit is evaded, never grazed.
            Assert.That(Hits(Play(r, Fast("a", x => { caster(x); x.Dex = 0; }, null, RuleModifiers.FortressFist), Wall("b", x => { target(x); x.Dex = 900; }), 3, 0))
                .All(x => x.Evaded && !x.Graze), Is.True);
        }

        [Test]
        public void FortressFist_HealsKeepStr()
        {
            // Recommendation in #134: only damage reads 0; a heal or shield scaling STR or INT is unchanged.
            var r = Rules;
            double Healed(params string[] rules)
            {
                var a = Fast("a", x => x.Str = 500, null, rules);
                a.Skills.Add(new CombatSkill { Id = "feast", Kind = SkillKind.Heal, Target = SkillTarget.Self, Scales = StatIds.Str, Power = 0.5 });
                var log = Play(r, a, Wall("b"), 1, 0.99, s => s.Units[0].Hp = 1);
                return log.Single(x => x.Skill?.Id == "feast").Amount;
            }
            Assert.That(Healed(RuleModifiers.FortressFist), Is.EqualTo(Healed()).And.GreaterThan(0));
        }

        // ------------------------------------------------------------------ blood_magic

        static Fighter Bleeder(params string[] rules)
        {
            var a = Fast("a", x => x.Hp = 300, null, rules);
            a.Skills.Add(Dmg("slash", StatIds.Str, 0.01, 3));
            return a;
        }

        [Test]
        public void BloodMagic_NoCooldown_CostsHp_StopsAtFloor()
        {
            var r = Rules;
            var s = BattleSim.Start(r, new[] { Bleeder(RuleModifiers.BloodMagic) }, new[] { Wall("b", x => { x.Str = 0; x.Int = 0; }) });
            var log = new List<FightAction>();
            while (s.Units[0].Tally.Actions < 20) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            var u = s.Units[0];
            double max = u.MaxHp, cost = r.BloodCost * max;
            int expected = 0;
            for (double hp = max; hp - cost >= r.BloodFloor * max; hp -= cost) expected++;
            Assert.That(expected, Is.GreaterThan(1).And.LessThan(20));
            var casts = log.Where(x => x.Skill?.Id == "slash").ToList();
            Assert.That(casts.Count, Is.EqualTo(expected), "cast on every action until the floor");
            Assert.That(log.Where(x => x.Actor == "a" && x.Event == null).Take(expected).All(x => x.Skill?.Id == "slash"), Is.True);
            var costs = log.Where(x => x.Event == BattleSim.BloodEvent).ToList();
            Assert.That(costs.Count, Is.EqualTo(expected));
            Assert.That(costs.Select(x => x.Amount), Is.All.EqualTo(cost).Within(1e-9));
            Assert.That(u.Hp, Is.EqualTo(max - expected * cost).Within(1e-9));
            Assert.That(u.Hp - cost, Is.LessThan(r.BloodFloor * max));
            Assert.That(u.Tally.DamageTaken, Is.EqualTo(0), "the cost is not damage");
            Assert.That(u.Cooldowns, Is.All.EqualTo(0));

            // Without the rule the cooldown applies (it ticks at the start of each action): actions 1, 4, 7 ... 19; no HP lost.
            s = BattleSim.Start(r, new[] { Bleeder() }, new[] { Wall("b", x => { x.Str = 0; x.Int = 0; }) });
            log.Clear();
            while (s.Units[0].Tally.Actions < 20) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(log.Count(x => x.Skill?.Id == "slash"), Is.EqualTo(7));
            Assert.That(s.Units[0].Hp, Is.EqualTo(s.Units[0].MaxHp));
        }

        [Test]
        public void BloodMagic_SelfHealNeedsTwiceItsCost()
        {
            // #141: with no cooldown, a self-heal that nets less than one cast's cost would be cast again and again. Max HP 3000:
            // a cast costs 180, so a self-heal is ready from 360 on; a heal on allies is not held back.
            var r = Rules;
            foreach (var (power, target, casts) in new[] { (1.19, SkillTarget.Self, false), (1.2, SkillTarget.Self, true), (0.5, SkillTarget.LowestAlly, true) })
            {
                var a = Fast("a", x => { x.Hp = 300; x.Wis = 300; }, null, RuleModifiers.BloodMagic);
                a.Skills.Add(new CombatSkill { Id = "mend", Kind = SkillKind.Heal, Target = target, Scales = StatIds.Wis, Power = power });
                var log = Play(r, a, Wall("b", x => { x.Str = 0; x.Int = 0; }), 1, 0.99, s => s.Units[0].Hp = 1000);
                Assert.That(log.Any(x => x.Skill?.Id == "mend"), Is.EqualTo(casts), $"{power} {target}");
                Assert.That(log.Any(x => x.Actor == "a" && x.Event == null && x.Skill == null), Is.EqualTo(!casts), "else it attacks");
            }
        }

        [Test]
        public void BloodMagic_NeverBelowOneHp()
        {
            var r = Rules;
            r.BloodCost = 1;
            r.BloodFloor = 0;
            var s = BattleSim.Start(r, new[] { Bleeder(RuleModifiers.BloodMagic) }, new[] { Wall("b", x => { x.Str = 0; x.Int = 0; }) });
            var log = new List<FightAction>();
            while (s.Units[0].Tally.Actions < 5) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(s.Units[0].Hp, Is.EqualTo(1));
            Assert.That(s.Units[0].Alive, Is.True);
            Assert.That(log.Count(x => x.Skill?.Id == "slash"), Is.EqualTo(5), "floor 0: it keeps casting at 1 HP for free");
            Assert.That(log.Where(x => x.Event == BattleSim.BloodEvent).Skip(1).Select(x => x.Amount), Is.All.EqualTo(0));
        }

        [Test]
        public void BloodMagic_PassivesTriggersBasicsFree_NoHitTaken()
        {
            var r = Rules;
            var a = Fast("a", x => x.Hp = 300, null, RuleModifiers.BloodMagic, RuleModifiers.NightFeast);
            a.Skills.Add(new CombatSkill { Id = "toughen", Kind = SkillKind.Passive, Target = SkillTarget.Self, Stat = StatIds.Def, Power = 0.1 });
            a.Triggers.Add(new TriggeredSkill(FightTrigger.OnAction, Dmg("spark", StatIds.Str, 0.01, 2)));
            a.Triggers.Add(new TriggeredSkill(FightTrigger.OnHitTaken, new CombatSkill { Id = "flinch", Kind = SkillKind.Buff, Target = SkillTarget.Self, Stat = StatIds.Spd, Power = 0.1, Duration = 1 }));
            var s = BattleSim.Start(r, new[] { a }, new[] { Wall("b", x => { x.Str = 0; x.Int = 0; }) });
            var log = new List<FightAction>();
            while (s.Units[0].Tally.Actions < 6) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(log.Any(x => x.Event == BattleSim.BloodEvent), Is.False, "no active skill: nothing costs HP");
            Assert.That(log.Count(x => x.Skill?.Id == "spark"), Is.EqualTo(6));
            Assert.That(s.Units[0].Hp, Is.EqualTo(s.Units[0].MaxHp));

            // With an active skill, the HP cost fires no OnHitTaken trigger and feeds no night_feast.
            a.Skills.Add(Dmg("slash", StatIds.Str, 0.01, 3));
            s = BattleSim.Start(r, new[] { a }, new[] { Wall("b", x => { x.Str = 0; x.Int = 0; }) });
            log.Clear();
            while (s.Units[0].Tally.Actions < 3) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(log.Count(x => x.Event == BattleSim.BloodEvent), Is.EqualTo(3));
            Assert.That(log.Any(x => x.Skill?.Id == "flinch"), Is.False);
            int feasts = log.Count(x => x.Event == FightAction.FeastEvent), landed = log.Count(x => x.Actor == "a" && x.Event == null && x.Amount > 0 && x.Skill?.Kind != SkillKind.Passive);
            Assert.That(feasts, Is.EqualTo(landed), "one feast per landed hit, none for the HP cost");
        }

        // ------------------------------------------------------------------ modes

        [Test]
        public void Modes_1v1_4v1_4v4_EveryRuleProcs()
        {
            // 1v1 (run fights, gauntlet), 4v1 (Dungeon) and 4v4 (Endless, PvP): every carrier logs its rule and the fight ends.
            var r = Rules;
            Fighter Carrier(string id, string rule)
            {
                var f = Mon(id, null, "fire", rule);
                f.Skills.Add(new CombatSkill { Id = "blast", Kind = SkillKind.Damage, Target = SkillTarget.AllEnemies, Scales = StatIds.Int, Power = 0.8, Cooldown = 3, Element = "water" });
                return f;
            }
            Fighter Foe(string id) => Mon(id, x => x.Hp = 600, "nature");
            var team = LaneRules.Select((id, i) => Carrier("a" + i, id)).ToArray();
            var cases = new (Fighter[] a, Fighter[] b)[]
            {
                (new[] { Carrier("a0", RuleModifiers.BloodMagic) }, new[] { Foe("b0") }),
                (team, new[] { Mon("boss", x => x.Hp = 3000, "dark") }),
                (team, Enumerable.Range(0, 4).Select(i => Foe("b" + i)).ToArray()),
            };
            foreach (var (a, b) in cases)
            {
                var log = new List<FightAction>();
                var res = BattleSim.Resolve(r, a, b, 7, log.Add);
                Assert.That(res.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut));
                foreach (var f in a)
                    Assert.That(log.Any(x => x.Actor == f.Id && x.Event == FightAction.RulePrefix + f.Rules[0]), Is.True, f.Rules[0]);
            }
        }
    }
}
