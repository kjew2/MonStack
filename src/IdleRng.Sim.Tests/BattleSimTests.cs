using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>The monster battle (specs/16): sides of 1-4, stat sheet, ATB, AI, triggers, rule modifiers, determinism.</summary>
    public class BattleSimTests
    {
        static FightRules Rules => FightRules.FromContent(TestContent.Db);

        static MonsterStats Sheet(double all) => new MonsterStats { Hp = all, Str = all, Int = all, Wis = all, Dex = all, Def = all, Spd = all, Luk = all };

        static Fighter Mon(string id, double all = 300, string element = null, Action<MonsterStats> tweak = null, params CombatSkill[] skills)
        {
            var st = Sheet(all);
            tweak?.Invoke(st);
            return new Fighter { Id = id, Element = element, Stats = st, Skills = skills.ToList() };
        }

        static CombatSkill Strike(double power = 2, int cd = 2) =>
            new CombatSkill { Id = "strike", Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = StatIds.Str, Power = power, Cooldown = cd };

        static Fighter[] Side(params Fighter[] f) => f;

        /// <summary>A rng that always returns the same value: 0.99 = no crit or evade; 0 = always crit and evade.</summary>
        sealed class Fixed : IRandomSource
        {
            readonly double _v;
            public Fixed(double v) { _v = v; }
            public double NextDouble() => _v;
        }

        static FightResult Run(Fighter[] a, Fighter[] b, ulong seed = 1, List<FightAction> log = null) =>
            BattleSim.Resolve(Rules, a, b, seed, log == null ? null : (Action<FightAction>)log.Add);

        [Test]
        public void StackedDebuffs_FloorAStatAtStatFloorFracOfBase()
        {
            var s = BattleSim.Start(Rules, Side(Mon("a")), Side(Mon("b")));
            var b = s.Units[1];
            b.Effects.Add(new FightEffect { Stat = StatIds.Def, Frac = -0.6, TicksLeft = 50, Source = "crush" });
            b.Effects.Add(new FightEffect { Stat = StatIds.Def, Frac = -0.6, TicksLeft = 50, Source = "crush" });
            Assert.That(Rules.StatFloor, Is.EqualTo(TestContent.Db.Constants.StatFloorFrac));
            Assert.That(BattleSim.Eff(Rules, b, StatIds.Def), Is.EqualTo(300 * Rules.StatFloor).Within(1e-9));

            var noFloor = Rules;
            noFloor.StatFloor = 0;   // a hand-built FightRules (a server) must set it
            Assert.Throws<ArgumentException>(() => BattleSim.Start(noFloor, Side(Mon("a")), Side(Mon("b"))));
        }

        [Test]
        public void Rules_ComeFromContent()
        {
            var k = TestContent.Db.Constants;
            var r = Rules;
            Assert.That(r.GaugeSize, Is.EqualTo(k.BattleGauge));
            Assert.That(r.TimeLimitTicks, Is.EqualTo(k.MonTimeLimitTicks));
            Assert.That(r.SideSize, Is.EqualTo(4));
            Assert.That(r.ElementMult("fire", "nature"), Is.EqualTo(k.ElementStrongMult));
            Assert.That(r.ElementMult("nature", "fire"), Is.EqualTo(k.ElementWeakMult));
            Assert.That(r.ElementMult("fire", "light"), Is.EqualTo(1.0));
            Assert.That(r.ElementMult(null, "fire"), Is.EqualTo(1.0));
        }

        [Test]
        public void SameSeed_SameFight_DifferentSeeds_Vary()
        {
            Fighter[] A() => Side(Mon("a1", 300, "fire", null, Strike()), Mon("a2", 250, "water"));
            Fighter[] B() => Side(Mon("b1", 280, "nature", null, Strike()), Mon("b2", 260, "light"));
            var first = Run(A(), B(), 42);
            var again = Run(A(), B(), 42);
            Assert.That(again.Hash, Is.EqualTo(first.Hash));
            Assert.That(again.Ticks, Is.EqualTo(first.Ticks));
            var hashes = new HashSet<string>();
            for (ulong seed = 1; seed <= 8; seed++) hashes.Add(Run(A(), B(), seed).Hash);
            Assert.That(hashes.Count, Is.GreaterThan(1), "crits, evades and targeting draw from the seed");
        }

        [Test]
        public void OnStart_ExposesTheLiveState_WithoutChangingTheFight()
        {
            Fighter[] A() => Side(Mon("a1", 300, "fire", null, Strike(2, 3)));
            Fighter[] B() => Side(Mon("b1", 280, "nature"));
            var plain = Run(A(), B(), 7);
            FightState live = null;
            var seen = new List<(double hp, int cd)>();
            var watched = BattleSim.Resolve(Rules, A(), B(), 7,
                x => { if (x.Actor == "a1") seen.Add((live.Units[1].Hp, live.Units[0].Cooldowns[0])); },
                s => { Assert.That(s.Ticks, Is.EqualTo(0), "before the first tick"); live = s; });
            Assert.That(watched.Hash, Is.EqualTo(plain.Hash), "observing does not change the fight");
            Assert.That(live, Is.SameAs(watched.Final), "onStart sees the state the fight runs on");
            Assert.That(seen[0].cd, Is.EqualTo(3), "the actor's cooldown is already set when its skill's action is reported");
            Assert.That(seen.Last().hp, Is.EqualTo(watched.Final.Units[1].Hp), "HP is read live");
        }

        [TestCase(1, 1)]
        [TestCase(4, 1)]
        [TestCase(4, 4)]
        [TestCase(2, 3)]
        public void AnySideSizes_FinishDeterministically(int na, int nb)
        {
            Fighter[] Make(string p, int n) => Enumerable.Range(0, n).Select(i => Mon($"{p}{i}", 200 + 40 * i, null, null, Strike())).ToArray();
            var r1 = Run(Make("a", na), Make("b", nb), 9);
            var r2 = Run(Make("a", na), Make("b", nb), 9);
            Assert.That(r1.Outcome, Is.Not.EqualTo(FightOutcome.Ongoing));
            Assert.That(r1.Hash, Is.EqualTo(r2.Hash));
            Assert.That(r1.Final.Units.Count, Is.EqualTo(na + nb));
        }

        [Test]
        public void StrongerMonster_Wins1v1()
        {
            var r = Run(Side(Mon("big", 600)), Side(Mon("small", 200)), 3);
            Assert.That(r.Outcome, Is.EqualTo(FightOutcome.SideAWon));
            r = Run(Side(Mon("small", 200)), Side(Mon("big", 600)), 3);
            Assert.That(r.Outcome, Is.EqualTo(FightOutcome.SideBWon));
        }

        [Test]
        public void NoDamage_TimesOutAtTheLimit()
        {
            Fighter Pacifist(string id) => Mon(id, 300, null, s => { s.Str = 0; s.Int = 0; });
            var r = Run(Side(Pacifist("a")), Side(Pacifist("b")));
            Assert.That(r.Outcome, Is.EqualTo(FightOutcome.TimedOut));
            Assert.That(r.Ticks, Is.EqualTo(Rules.TimeLimitTicks));
        }

        [Test]
        public void StepByStep_EqualsResolve_AndCloneContinuesIdentically()
        {
            Fighter[] A() => Side(Mon("a1", 300, null, null, Strike()), Mon("a2", 250));
            Fighter[] B() => Side(Mon("b1", 300), Mon("b2", 280, null, null, Strike()));
            var expected = Run(A(), B(), 77);

            var s = BattleSim.Start(Rules, A(), B());
            var rng = new RngStream(BattleSim.StreamName, 77);
            for (int i = 0; i < 25; i++) BattleSim.Step(Rules, s, rng);
            var copy = s.Clone();
            var rngCopy = rng.Clone();
            while (BattleSim.Step(Rules, s, rng) == FightOutcome.Ongoing) { }
            while (BattleSim.Step(Rules, copy, rngCopy) == FightOutcome.Ongoing) { }
            Assert.That(BattleSim.StateHash(s), Is.EqualTo(expected.Hash));
            Assert.That(BattleSim.StateHash(copy), Is.EqualTo(expected.Hash));
            Assert.That(BattleSim.Step(Rules, s, rng), Is.EqualTo(expected.Outcome), "a finished fight stays finished");
            Assert.That(s.Ticks, Is.EqualTo(expected.Ticks));
        }

        [Test]
        public void DamageTallies_Balance()
        {
            var r = Run(Side(Mon("a1", 300, "fire", null, Strike()), Mon("a2", 300)), Side(Mon("b1", 300), Mon("b2", 300, "water")), 5);
            double dealtA = r.Final.Units.Where(u => u.Side == 0).Sum(u => u.Tally.DamageDealt);
            double takenB = r.Final.Units.Where(u => u.Side == 1).Sum(u => u.Tally.DamageTaken);
            Assert.That(dealtA, Is.EqualTo(takenB).Within(1e-9));
            var a1 = r.Final.Units.First(u => u.Id == "a1");
            Assert.That(a1.Tally.DamageByElement["fire"], Is.EqualTo(a1.Tally.DamageDealt).Within(1e-9));
            Assert.That(r.Final.Units.First(u => u.Id == "a2").Tally.DamageByElement, Is.Empty, "elementless damage is not listed");
            Assert.That(r.Final.Units.Sum(u => u.Tally.Kills), Is.EqualTo(r.Final.Units.Count(u => !u.Alive)));
        }

        /// <summary>One basic attack from a to b with a fixed draw; returns the damage dealt.</summary>
        static double OneHit(Fighter a, Fighter b, double draw)
        {
            var s = BattleSim.Start(Rules, Side(a), Side(b));
            var log = new List<FightAction>();
            while (log.Count == 0 && BattleSim.Step(Rules, s, new Fixed(draw), log.Add) == FightOutcome.Ongoing) { }
            Assert.That(log[0].Actor, Is.EqualTo("a"), "a is faster and acts first");
            return log[0].Amount;
        }

        static Fighter Attacker(string element = null, Action<MonsterStats> t = null) =>
            Mon("a", 300, element, s => { s.Spd = 500; t?.Invoke(s); });

        [Test]
        public void Damage_IsPhysicalMitigatedByDef_OrMagicalMitigatedByWis()
        {
            var r = Rules;
            double phys = OneHit(Attacker(), Mon("b", 300), 0.99);
            Assert.That(phys, Is.EqualTo(r.BasicAttackPower * 300 * r.DefK / (r.DefK + 300)).Within(1e-9));
            double physHighDef = OneHit(Attacker(), Mon("b", 300, null, s => s.Def = 900), 0.99);
            Assert.That(physHighDef, Is.LessThan(phys));
            // INT above STR makes the basic attack magical: WIS mitigates, DEF does not
            double mag = OneHit(Attacker(null, s => s.Int = 400), Mon("b", 300, null, s => s.Def = 900), 0.99);
            Assert.That(mag, Is.EqualTo(r.BasicAttackPower * 400 * r.DefK / (r.DefK + 300)).Within(1e-9));
        }

        [Test]
        public void Element_And_Crit_Multiply()
        {
            var r = Rules;
            double neutral = OneHit(Attacker("fire"), Mon("b", 300, "light"), 0.99);
            double strong = OneHit(Attacker("fire"), Mon("b", 300, "nature"), 0.99);
            Assert.That(strong / neutral, Is.EqualTo(r.ElementStrongMult).Within(1e-9));
            // draw 0 means: the target evades every physical hit, so use a magical attacker to see the crit
            double magNoCrit = OneHit(Attacker(null, s => s.Int = 400), Mon("b", 300), 0.99);
            double magCrit = OneHit(Attacker(null, s => s.Int = 400), Mon("b", 300), 0.0);
            Assert.That(magCrit / magNoCrit, Is.EqualTo(r.CritDmg).Within(1e-9));
        }

        [Test]
        public void Evasion_OnlyAgainstPhysical_AndCounted()
        {
            var s = BattleSim.Start(Rules, Side(Attacker()), Side(Mon("b", 300, null, x => x.Dex = 999)));
            var log = new List<FightAction>();
            while (log.Count == 0) BattleSim.Step(Rules, s, new Fixed(0.0), log.Add);
            Assert.That(log[0].Evaded, Is.True);
            Assert.That(s.Units[1].Tally.Evades, Is.EqualTo(1));
            Assert.That(s.Units[1].Hp, Is.EqualTo(s.Units[1].MaxHp));

            double mag = OneHit(Attacker(null, x => x.Int = 400), Mon("b", 300, null, x => x.Dex = 999), 0.0);
            Assert.That(mag, Is.GreaterThan(0), "magic cannot be evaded");
        }

        [Test]
        public void Taunt_RedirectsSingleTargetAttacks()
        {
            var taunt = new CombatSkill { Id = "taunt", Kind = SkillKind.Taunt, Target = SkillTarget.Self, Cooldown = 3, Duration = 3 };
            var tank = Mon("tank", 400, null, x => x.Spd = 900, taunt);
            var squishy = Mon("squishy", 150);
            var s = BattleSim.Start(Rules, Side(tank, squishy), Side(Mon("enemy", 300, null, x => x.Spd = 200)));
            var rng = new RngStream(BattleSim.StreamName, 11);
            int whileTaunting = 0;
            void Check(FightAction a)
            {
                if (a.Actor != "enemy" || s.Units[0].TauntTicks == 0 || !s.Units[0].Alive) return;
                whileTaunting++;
                Assert.That(a.Target, Is.EqualTo("tank"));
            }
            while (BattleSim.Step(Rules, s, rng, Check) == FightOutcome.Ongoing) { }
            Assert.That(whileTaunting, Is.GreaterThan(0));
        }

        [Test]
        public void Healer_HealsOnlyWhenAnAllyIsLow()
        {
            var mend = new CombatSkill { Id = "mend", Kind = SkillKind.Heal, Target = SkillTarget.LowestAlly, Scales = StatIds.Wis, Power = 1, Cooldown = 1 };
            var log = new List<FightAction>();
            var r = Run(Side(Mon("healer", 300, null, null, mend), Mon("buddy", 300)), Side(Mon("enemy", 500, null, null, Strike())), 4, log);
            var heals = log.Where(a => a.Skill?.Id == "mend").ToList();
            Assert.That(heals, Is.Not.Empty);
            Assert.That(r.Final.Units[0].Tally.HealingDone, Is.EqualTo(heals.Sum(a => a.Amount)).Within(1e-9));
            Assert.That(log.First(a => a.Actor == "healer").Skill?.Id, Is.Not.EqualTo("mend"), "nobody is hurt at the first action");
        }

        [Test]
        public void Passive_AppliesAtStart_AndTriggersFire()
        {
            var tough = new CombatSkill { Id = "tough", Kind = SkillKind.Passive, Target = SkillTarget.Self, Stat = StatIds.Hp, Power = 0.5 };
            var f = Mon("a", 300, null, x => x.Spd = 500, tough);
            var onCrit = new CombatSkill { Id = "frenzy", Kind = SkillKind.Buff, Target = SkillTarget.Self, Stat = StatIds.Str, Power = 0.25, Duration = 2 };
            f.Triggers.Add(new TriggeredSkill(FightTrigger.OnCrit, onCrit));
            f.Stats.Int = 400;   // magical basic attack: no evade roll, so a draw of 0 always crits

            var s = BattleSim.Start(Rules, Side(f), Side(Mon("b", 300)));
            var log = new List<FightAction>();
            BattleSim.Step(Rules, s, new Fixed(0.0), log.Add);
            Assert.That(s.Units[0].MaxHp, Is.EqualTo(300 * Rules.HpPerPoint * 1.5).Within(1e-9));
            while (!log.Any(a => a.Skill?.Id == "frenzy")) BattleSim.Step(Rules, s, new Fixed(0.0), log.Add);
            Assert.That(s.Units[0].Effects.Any(e => e.Source == "frenzy"), Is.True);
            Assert.That(BattleSim.Eff(Rules, s.Units[0], StatIds.Str), Is.EqualTo(300 * 1.25).Within(1e-9));
        }

        [Test]
        public void BattleStartDamageTrigger_CanEndTheFightBeforeAnyAction()
        {
            var opener = new CombatSkill { Id = "opener", Kind = SkillKind.Damage, Target = SkillTarget.AllEnemies, Scales = StatIds.Int, Power = 100 };
            var a = Mon("a", 300);
            a.Triggers.Add(new TriggeredSkill(FightTrigger.BattleStart, opener));
            var log = new List<FightAction>();
            var r = Run(Side(a), Side(Mon("b1", 50), Mon("b2", 50)), 1, log);
            Assert.That(r.Outcome, Is.EqualTo(FightOutcome.SideAWon));
            Assert.That(r.Ticks, Is.EqualTo(0), "decided before the first gauge fill");
            Assert.That(log.All(x => x.Skill?.Id == "opener"), Is.True);
            Assert.That(r.Final.Units[0].Tally.Kills, Is.EqualTo(2));
        }

        [Test]
        public void CounterMitigated_HitsBackForMitigatedDamage()
        {
            var r = Rules;
            var knight = Mon("knight", 300, null, x => { x.Def = 900; x.Spd = 10; });
            knight.Rules.Add(RuleModifiers.CounterMitigated);
            var s = BattleSim.Start(r, Side(Attacker()), Side(knight));
            var log = new List<FightAction>();
            while (!log.Any(a => a.Counter)) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            var hit = log.First(a => a.Actor == "a");
            var counter = log.First(a => a.Counter);
            double raw = r.BasicAttackPower * 300;
            Assert.That(hit.Amount, Is.EqualTo(raw * r.DefK / (r.DefK + 900)).Within(1e-9));
            Assert.That(counter.Actor, Is.EqualTo("knight"));
            Assert.That(counter.Target, Is.EqualTo("a"));
            Assert.That(counter.Amount, Is.EqualTo(raw - hit.Amount).Within(1e-9));
            Assert.That(s.Units[1].Tally.Counters, Is.EqualTo(1));
        }

        [Test]
        public void EchoCast_RepeatsActiveSkillsAtEchoPower()
        {
            var bolt = new CombatSkill { Id = "bolt", Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = StatIds.Int, Power = 2, Cooldown = 3 };
            var caster = Mon("a", 300, null, x => x.Spd = 900, bolt);
            caster.Rules.Add(RuleModifiers.EchoCast);
            var s = BattleSim.Start(Rules, Side(caster), Side(Mon("b", 999)));
            var log = new List<FightAction>();
            while (log.Count(a => a.Skill?.Id == "bolt") < 2) BattleSim.Step(Rules, s, new Fixed(0.99), log.Add);
            var casts = log.Where(a => a.Skill?.Id == "bolt").ToList();
            Assert.That(casts[1].Amount / casts[0].Amount, Is.EqualTo(Rules.EchoPower).Within(1e-9));
            Assert.That(s.Units[0].Tally.Actions, Is.EqualTo(1), "the echo is part of the same action");
        }

        [Test]
        public void AoeDamage_HitsEveryLivingEnemy()
        {
            var quake = new CombatSkill { Id = "quake", Kind = SkillKind.Damage, Target = SkillTarget.AllEnemies, Scales = StatIds.Int, Power = 1, Cooldown = 5 };
            var s = BattleSim.Start(Rules, Side(Mon("a", 300, null, x => x.Spd = 900, quake)), Side(Mon("b1"), Mon("b2"), Mon("b3")));
            var log = new List<FightAction>();
            while (log.Count == 0) BattleSim.Step(Rules, s, new Fixed(0.99), log.Add);
            Assert.That(log.Count(a => a.Skill?.Id == "quake" && a.Aoe), Is.EqualTo(3));
            Assert.That(s.Units.Skip(1).All(u => u.Hp < u.MaxHp), Is.True);
        }

        [Test]
        public void Validation_RejectsBadSides()
        {
            var r = Rules;
            Assert.Throws<ArgumentException>(() => BattleSim.Start(r, Side(), Side(Mon("b"))));
            Assert.Throws<ArgumentException>(() => BattleSim.Start(r, Side(Mon("a1"), Mon("a2"), Mon("a3"), Mon("a4"), Mon("a5")), Side(Mon("b"))));
            Assert.Throws<ArgumentException>(() => BattleSim.Start(r, Side(Mon("x")), Side(Mon("x"))), "duplicate id");
            Assert.Throws<ArgumentException>(() => BattleSim.Start(r, Side(Mon("a", 300, null, s => s.Str = -1)), Side(Mon("b"))));
            var bad = Mon("a");
            bad.Rules.Add("no_such_rule");
            Assert.Throws<ArgumentException>(() => BattleSim.Start(r, Side(bad), Side(Mon("b"))));
            var badSkill = Mon("a", 300, null, null, new CombatSkill { Id = "x", Kind = SkillKind.Damage, Scales = "atk", Power = 1 });
            Assert.Throws<ArgumentException>(() => BattleSim.Start(r, Side(badSkill), Side(Mon("b"))), "atk is not a monster stat");
            var badBuff = Mon("a", 300, null, null, new CombatSkill { Id = "x", Kind = SkillKind.Buff, Stat = null, Power = 1 });
            Assert.Throws<ArgumentException>(() => BattleSim.Start(r, Side(badBuff), Side(Mon("b"))));
            Assert.Throws<ArgumentNullException>(() => BattleSim.Start(r, null, Side(Mon("b"))));
        }

        [Test]
        public void ZeroStats_StillValid()
        {
            var r = Run(Side(Mon("zero", 0)), Side(Mon("b", 100)), 2);
            Assert.That(r.Outcome, Is.EqualTo(FightOutcome.SideBWon), "HP 0 still gets 1 max HP, SPD 0 still fills at SPD_MIN");
        }
    }
}
