using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Endless Tower mutators in the battle engine (specs/28 section 3.2): each kind, stacking, validation, off by default.</summary>
    public class MutatorTests
    {
        static FightRules Rules(FightMutators m = null, bool tower = false)
        {
            var r = FightRules.FromContent(TestContent.Db, tower);
            r.Mutators = m;
            return r;
        }

        static MonsterStats Sheet(double all) => new MonsterStats { Hp = all, Str = all, Int = all, Wis = all, Dex = all, Def = all, Spd = all, Luk = all };

        static Fighter Mon(string id, string element = null, params CombatSkill[] skills) =>
            new Fighter { Id = id, Element = element, Stats = Sheet(300), Skills = skills.ToList() };

        static CombatSkill Strike(SkillTarget target = SkillTarget.Enemy, string element = null) =>
            new CombatSkill { Id = "strike", Kind = SkillKind.Damage, Target = target, Scales = StatIds.Str, Power = 2, Cooldown = 0, Element = element };

        /// <summary>A rng that always returns <paramref name="v"/>: 0.99 = no crit or evade; 0.5 crits only when the chance is above half.</summary>
        sealed class Fixed : IRandomSource
        {
            readonly double _v;
            public Fixed(double v) { _v = v; }
            public double NextDouble() => _v;
        }

        /// <summary>The first action of side A's unit "a" (the fastest) under the given rules and draws.</summary>
        static FightAction FirstAction(FightRules r, Fighter a, Fighter b, double draw = 0.99)
        {
            a.Stats.Spd = 900;
            var s = BattleSim.Start(r, new[] { a }, new[] { b });
            var log = new List<FightAction>();
            while (!log.Any(x => x.Actor == "a" && x.Event == null)) BattleSim.Step(r, s, new Fixed(draw), log.Add);
            return log.First(x => x.Actor == "a" && x.Event == null);
        }

        [Test]
        public void EmptyMutators_ChangeNothing()
        {
            Fighter[] A() => new[] { Mon("a", "fire", Strike()), Mon("a2") };
            Fighter[] B() => new[] { Mon("b", "water", Strike(SkillTarget.AllEnemies)) };
            var plain = BattleSim.Resolve(Rules(null, tower: true), A(), B(), 3);
            var empty = BattleSim.Resolve(Rules(new FightMutators(), tower: true), A(), B(), 3);
            Assert.That(empty.Hash, Is.EqualTo(plain.Hash));
        }

        [Test]
        public void AoeAndSingleMult_ScaleOnlyTheirOwnHits()
        {
            double Dmg(FightMutators m, SkillTarget t) => FirstAction(Rules(m), Mon("a", null, Strike(t)), Mon("b")).Amount;
            double single = Dmg(null, SkillTarget.Enemy), aoe = Dmg(null, SkillTarget.AllEnemies);
            var aoeUp = new FightMutators().Add(MutatorKinds.AoeMult, 2);
            Assert.That(Dmg(aoeUp, SkillTarget.AllEnemies), Is.EqualTo(2 * aoe).Within(1e-9));
            Assert.That(Dmg(aoeUp, SkillTarget.Enemy), Is.EqualTo(single).Within(1e-9));
            var singleDown = new FightMutators().Add(MutatorKinds.SingleMult, 0.5);
            Assert.That(Dmg(singleDown, SkillTarget.Enemy), Is.EqualTo(0.5 * single).Within(1e-9));
            Assert.That(Dmg(singleDown, SkillTarget.AllEnemies), Is.EqualTo(aoe).Within(1e-9));
        }

        [Test]
        public void ElementMult_ScalesThatElementOnly()
        {
            double Dmg(FightMutators m, string el) => FirstAction(Rules(m), Mon("a", null, Strike(SkillTarget.Enemy, el)), Mon("b")).Amount;
            var m = new FightMutators().Add(MutatorKinds.ElementMult, 1.5, "fire");
            Assert.That(Dmg(m, "fire"), Is.EqualTo(1.5 * Dmg(null, "fire")).Within(1e-9));
            Assert.That(Dmg(m, "water"), Is.EqualTo(Dmg(null, "water")).Within(1e-9));
        }

        [Test]
        public void HealMult_ScalesHeals()
        {
            var mend = new CombatSkill { Id = "mend", Kind = SkillKind.Heal, Target = SkillTarget.Self, Scales = StatIds.Wis, Power = 0.5 };
            double Healed(FightMutators m)
            {
                var r = Rules(m);
                var a = Mon("a", null, mend);
                a.Stats.Spd = 900;
                var s = BattleSim.Start(r, new[] { a }, new[] { Mon("b") });
                s.Units[0].Hp = 1;
                var log = new List<FightAction>();
                while (!log.Any(x => x.Actor == "a")) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
                return log.First(x => x.Actor == "a").Amount;
            }
            Assert.That(Healed(new FightMutators().Add(MutatorKinds.HealMult, 0.5)), Is.EqualTo(0.5 * Healed(null)).Within(1e-9));
        }

        [Test]
        public void SpeedMult_ScalesTheGaugeFill()
        {
            var r = Rules(new FightMutators().Add(MutatorKinds.SpeedMult, 1.5));
            var s = BattleSim.Start(r, new[] { Mon("a") }, new[] { Mon("b") });
            BattleSim.Step(r, s, new Fixed(0.99));
            Assert.That(s.Units[0].Gauge, Is.EqualTo(1.5 * BattleSim.GaugeFill(r, 300)).Within(1e-9));
        }

        [Test]
        public void CritBonus_AddsCritChance()
        {
            var a = Mon("a", null, Strike());
            a.Stats.Luk = 0;   // base crit chance only, and no fortune draws
            Assert.That(FirstAction(Rules(), a, Mon("b"), 0.5).Crit, Is.False);
            a = Mon("a", null, Strike());
            a.Stats.Luk = 0;
            Assert.That(FirstAction(Rules(new FightMutators().Add(MutatorKinds.CritBonus, 0.6)), a, Mon("b"), 0.5).Crit, Is.True);
        }

        [Test]
        public void CorruptionEarly_StartsTheClockSooner_NeverBeforeTickZero()
        {
            var plain = Rules(null, tower: true);
            int start = plain.Tower.CorruptStartTick, every = plain.Tower.CorruptEveryTicks;
            var early = Rules(new FightMutators().Add(MutatorKinds.CorruptionEarly, 100).Add(MutatorKinds.CorruptionEarly, 50), tower: true);
            Assert.That(BattleSim.CorruptionLevel(early, start - 150 + every), Is.EqualTo(1));
            Assert.That(BattleSim.CorruptionLevel(plain, start - 150 + every), Is.EqualTo(Math.Max(0, (every - 150) / every)));
            var huge = Rules(new FightMutators().Add(MutatorKinds.CorruptionEarly, 1_000_000), tower: true);
            Assert.That(BattleSim.CorruptionLevel(huge, 0), Is.EqualTo(0));
            Assert.That(BattleSim.CorruptionLevel(huge, every), Is.EqualTo(1));
            Assert.That(BattleSim.CorruptionLevel(Rules(new FightMutators().Add(MutatorKinds.CorruptionEarly, 100)), 10_000), Is.EqualTo(0),
                "no tower rules, no clock");
        }

        [Test]
        public void StartShield_GivesEveryUnitAShield()
        {
            var r = Rules(new FightMutators().Add(MutatorKinds.StartShield, 0.1).Add(MutatorKinds.StartShield, 0.15));
            var s = BattleSim.Start(r, new[] { Mon("a") }, new[] { Mon("b") });
            foreach (var u in s.Units) Assert.That(u.Shield, Is.EqualTo(0.25 * u.MaxHp).Within(1e-9));
        }

        [Test]
        public void Mutators_Stack()
        {
            var m = new FightMutators()
                .Add(MutatorKinds.HealMult, 0.5).Add(MutatorKinds.HealMult, 0.5)
                .Add(MutatorKinds.ElementMult, 2, "fire").Add(MutatorKinds.ElementMult, 1.5, "fire")
                .Add(MutatorKinds.CritBonus, 0.1).Add(MutatorKinds.CritBonus, -0.3);
            Assert.That(m.Heal, Is.EqualTo(0.25));
            Assert.That(m.ElementMult("fire"), Is.EqualTo(3));
            Assert.That(m.ElementMult("water"), Is.EqualTo(1));
            Assert.That(m.CritBonus, Is.EqualTo(-0.2).Within(1e-12));
            Assert.That(m.DamageMult(true, "fire"), Is.EqualTo(3));
        }

        [Test]
        public void Add_RejectsBadMutators()
        {
            Assert.Throws<ArgumentException>(() => new FightMutators().Add("gravity", 1));
            Assert.Throws<ArgumentException>(() => new FightMutators().Add(MutatorKinds.HealMult, -0.1));
            Assert.Throws<ArgumentException>(() => new FightMutators().Add(MutatorKinds.AoeMult, double.NaN));
            Assert.Throws<ArgumentException>(() => new FightMutators().Add(MutatorKinds.SpeedMult, 0));
            Assert.Throws<ArgumentException>(() => new FightMutators().Add(MutatorKinds.CritBonus, 1.5));
            Assert.Throws<ArgumentException>(() => new FightMutators().Add(MutatorKinds.CorruptionEarly, 10.5));
            Assert.Throws<ArgumentException>(() => new FightMutators().Add(MutatorKinds.StartShield, 2));
            Assert.Throws<ArgumentException>(() => new FightMutators().Add(MutatorKinds.ElementMult, 2));
            var direct = new FightMutators();
            direct.Element["fire"] = -1;   // written past Add: Start still refuses it
            Assert.Throws<ArgumentException>(() => BattleSim.Start(Rules(direct), new[] { Mon("a") }, new[] { Mon("b") }));
        }
    }
}
