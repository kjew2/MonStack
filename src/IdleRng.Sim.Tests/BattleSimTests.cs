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

        /// <summary>A rng that returns the given draws in order, then 0.99 (no crit, evade or reroll) forever.</summary>
        sealed class Seq : IRandomSource
        {
            readonly Queue<double> _q;
            public Seq(params double[] v) { _q = new Queue<double>(v); }
            public double NextDouble() => _q.Count > 0 ? _q.Dequeue() : 0.99;
        }

        /// <summary>The first action of a fight of a fast attacker against <paramref name="target"/>, under the given draws.</summary>
        static FightAction FirstHit(Fighter attacker, Fighter target, IRandomSource rng)
        {
            var s = BattleSim.Start(Rules, Side(attacker), Side(target));
            var log = new List<FightAction>();
            while (log.Count == 0) BattleSim.Step(Rules, s, rng, log.Add);
            return log[0];
        }

        [Test]
        public void Fortune_IsLukOverLukPlusK_Capped()
        {
            var r = Rules;
            Assert.That(BattleSim.Fortune(r, 0), Is.EqualTo(0));
            Assert.That(BattleSim.Fortune(r, 300), Is.EqualTo(Math.Min(r.FortuneCap, 300 / (300 + r.FortuneK))).Within(1e-12));
            Assert.That(BattleSim.Fortune(r, 1e9), Is.EqualTo(r.FortuneCap));
        }

        [Test]
        public void Fortune_RerollsAFailedEvade()
        {
            // target DEX 999 vs 300: evade chance is the cap. Draws: evade fails, fortune hits, evade again succeeds.
            var hit = FirstHit(Attacker(), Mon("b", 300, null, x => x.Dex = 999), new Seq(0.99, 0.0, 0.0));
            Assert.That((hit.Evaded, hit.Lucky), Is.EqualTo((true, true)));
            // fortune hits but the second evade roll fails too: the hit lands, nothing lucky
            hit = FirstHit(Attacker(), Mon("b", 300, null, x => x.Dex = 999), new Seq(0.99, 0.0, 0.99));
            Assert.That((hit.Evaded, hit.Lucky), Is.EqualTo((false, false)));
            // no LUK: no fortune draw, so the second draw (0) is the crit roll
            hit = FirstHit(Attacker(), Mon("b", 300, null, x => { x.Dex = 999; x.Luk = 0; }), new Seq(0.99, 0.0, 0.99));
            Assert.That((hit.Evaded, hit.Crit, hit.Lucky), Is.EqualTo((false, true, false)));
        }

        [Test]
        public void Fortune_RerollsAFailedCrit_AndMakesACritAgainstItRollAgain()
        {
            // target DEX 0: no evade draw. Draws: crit fails, attacker's fortune hits, crit again lands, target's fortune misses
            var hit = FirstHit(Attacker(), Mon("b", 300, null, x => x.Dex = 0), new Seq(0.99, 0.0, 0.0, 0.99));
            Assert.That((hit.Crit, hit.Lucky), Is.EqualTo((true, true)));
            // crit lands, the target's fortune hits, the crit's second roll fails: denied
            hit = FirstHit(Attacker(), Mon("b", 300, null, x => x.Dex = 0), new Seq(0.0, 0.0, 0.99));
            Assert.That((hit.Crit, hit.Lucky), Is.EqualTo((false, true)));
            // crit lands, the target's fortune hits, the crit's second roll lands too: it stands
            hit = FirstHit(Attacker(), Mon("b", 300, null, x => x.Dex = 0), new Seq(0.0, 0.0, 0.0));
            Assert.That((hit.Crit, hit.Lucky), Is.EqualTo((true, false)));
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
            // a target with no DEX has no evade chance (no evade draw), so a draw of 0 always crits; DEX adds crit damage
            double noCrit = OneHit(Attacker(), Mon("b", 300, null, s => s.Dex = 0), 0.99);
            double crit = OneHit(Attacker(), Mon("b", 300, null, s => s.Dex = 0), 0.0);
            Assert.That(crit / noCrit, Is.EqualTo(BattleSim.CritMult(r, 300, 0)).Within(1e-9));
            Assert.That(BattleSim.CritMult(r, 300, 0), Is.EqualTo(r.CritDmg + 300 * r.CritDmgPerDex).Within(1e-12));
            Assert.That(BattleSim.CritMult(r, 0, -5), Is.EqualTo(1.0), "never below x1");
        }

        [Test]
        public void Evasion_PhysicalMisses_MagicGrazes_AndCounted()
        {
            var s = BattleSim.Start(Rules, Side(Attacker()), Side(Mon("b", 300, null, x => x.Dex = 999)));
            var log = new List<FightAction>();
            while (log.Count == 0) BattleSim.Step(Rules, s, new Fixed(0.0), log.Add);
            Assert.That(log[0].Evaded, Is.True);
            Assert.That(s.Units[1].Tally.Evades, Is.EqualTo(1));
            Assert.That(s.Units[1].Hp, Is.EqualTo(s.Units[1].MaxHp));

            // a magical hit that rolls under the evade chance is grazed: MON_GRAZE_MULT of its damage, no crit, not an evade
            var r = Rules;
            double full = OneHit(Attacker(null, x => x.Int = 400), Mon("b", 300, null, x => x.Dex = 999), 0.99);
            var gs = BattleSim.Start(r, Side(Attacker(null, x => x.Int = 400)), Side(Mon("b", 300, null, x => x.Dex = 999)));
            var glog = new List<FightAction>();
            while (glog.Count == 0) BattleSim.Step(r, gs, new Fixed(0.0), glog.Add);
            Assert.That((glog[0].Graze, glog[0].Evaded, glog[0].Crit), Is.EqualTo((true, false, false)));
            Assert.That(glog[0].Amount, Is.EqualTo(full * r.GrazeMult).Within(1e-9));
            Assert.That((gs.Units[1].Tally.Grazes, gs.Units[1].Tally.Evades), Is.EqualTo((1, 0)));
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
        public void Healer_HealsOnlyWhenTheWholeHealLands()
        {
            var mend = new CombatSkill { Id = "mend", Kind = SkillKind.Heal, Target = SkillTarget.LowestAlly, Scales = StatIds.Wis, Power = 1, Cooldown = 1 };
            var log = new List<FightAction>();
            var r = Run(Side(Mon("healer", 300, null, null, mend), Mon("buddy", 300)), Side(Mon("enemy", 500, null, null, Strike())), 4, log);
            var heals = log.Where(a => a.Skill?.Id == "mend").ToList();
            Assert.That(heals, Is.Not.Empty);
            Assert.That(r.Final.Units[0].Tally.HealingDone, Is.EqualTo(heals.Sum(a => a.Amount)).Within(1e-9));
            Assert.That(heals.Select(a => a.Amount), Has.All.EqualTo(300).Within(1e-9), "every heal lands in full: 1 x WIS 300");
        }

        [Test]
        public void Healer_RescuesBeforeADamageSkillListedFirst()
        {
            var mend = new CombatSkill { Id = "mend", Kind = SkillKind.Heal, Target = SkillTarget.LowestAlly, Scales = StatIds.Wis, Power = 1, Cooldown = 1 };
            var s = BattleSim.Start(Rules, Side(Mon("healer", 300, null, x => x.Spd = 500, Strike(), mend), Mon("buddy", 300)), Side(Mon("enemy", 300)));
            s.Units[1].Hp = s.Units[1].MaxHp * 0.1;
            var log = new List<FightAction>();
            var rng = new RngStream(BattleSim.StreamName, 5);
            while (!log.Any(a => a.Actor == "healer")) BattleSim.Step(Rules, s, rng, log.Add);
            Assert.That(log.First(a => a.Actor == "healer").Skill?.Id, Is.EqualTo("mend"));
        }

        /// <summary>The healer (fast, WIS 300, mend = 1 x WIS = 300 HP) and its allies against a harmless enemy; returns its first action.</summary>
        static FightAction FirstHealerAction(double power, Action<FightState> hurt, params Fighter[] allies)
        {
            var mend = new CombatSkill { Id = "mend", Kind = SkillKind.Heal, Target = SkillTarget.LowestAlly, Scales = StatIds.Wis, Power = power, Cooldown = 1 };
            var healer = Mon("healer", 300, null, x => { x.Spd = 900; x.Str = 0; x.Int = 0; }, Strike(), mend);
            var s = BattleSim.Start(Rules, Side(new[] { healer }.Concat(allies).ToArray()), Side(Harmless("enemy", 10)));
            hurt(s);
            var log = new List<FightAction>();
            while (!log.Any(a => a.Actor == "healer")) BattleSim.Step(Rules, s, new Fixed(0.99), log.Add);
            return log.First(a => a.Actor == "healer");
        }

        [Test]
        public void Healer_WaitsUntilTheWholeHealFits()
        {
            double amt = 300 * 1.0;   // WIS x power, no tower heal cut
            var tooEarly = FirstHealerAction(1, s => s.Units[1].Hp = s.Units[1].MaxHp - amt + 1, Mon("buddy", 300));
            Assert.That(tooEarly.Skill?.Id, Is.EqualTo("strike"), "missing 1 HP less than the heal: it would waste some, so wait");
            var fits = FirstHealerAction(1, s => s.Units[1].Hp = s.Units[1].MaxHp - amt, Mon("buddy", 300));
            Assert.That(fits.Skill?.Id, Is.EqualTo("mend"), "missing exactly the heal: cast it");
            Assert.That(fits.Target, Is.EqualTo("buddy"));
            Assert.That(fits.Amount, Is.EqualTo(amt).Within(1e-9), "no overheal");
            var self = FirstHealerAction(1, s => s.Units[0].Hp = s.Units[0].MaxHp - amt, Mon("buddy", 300));
            Assert.That(self.Skill?.Id, Is.EqualTo("mend"));
            Assert.That(self.Target, Is.EqualTo("healer"), "the healer is one of its allies");
        }

        [Test]
        public void Healer_TargetsTheLowestAllyTheWholeHealFits()
        {
            // small: 200 max HP at 30%, missing 140 (< 300, and < the 75% cap for a heal bigger than it); big: 3000 max HP at 50%, missing 1500
            Action<FightState> hurt = s => { s.Units[1].Hp = s.Units[1].MaxHp * 0.3; s.Units[2].Hp = s.Units[2].MaxHp * 0.5; };
            var a = FirstHealerAction(1, hurt, Mon("small", 20), Mon("big", 300));
            Assert.That(a.Skill?.Id, Is.EqualTo("mend"));
            Assert.That(a.Target, Is.EqualTo("big"), "the lower ally would waste half the heal");
            // both fit: the lower HP fraction wins
            var b = FirstHealerAction(1, s => { s.Units[1].Hp = s.Units[1].MaxHp * 0.6; s.Units[2].Hp = s.Units[2].MaxHp * 0.5; },
                                      Mon("mid", 300), Mon("low", 300));
            Assert.That(b.Target, Is.EqualTo("low"));
        }

        [Test]
        public void Healer_AHealBiggerThanTheGapWaitsForAiHealBelow()
        {
            var r = Rules;
            Assert.That(r.AiHealBelow, Is.GreaterThan(0).And.LessThan(1));
            // power 10: 3000 HP, the buddy's whole max HP, can never land in full
            double below = r.AiHealBelow * 3000;
            var above = FirstHealerAction(10, s => s.Units[1].Hp = below + 1, Mon("buddy", 300));
            Assert.That(above.Skill?.Id, Is.EqualTo("strike"));
            var at = FirstHealerAction(10, s => s.Units[1].Hp = below, Mon("buddy", 300));
            Assert.That(at.Skill?.Id, Is.EqualTo("mend"));
            Assert.That(at.Target, Is.EqualTo("buddy"));
        }

        [Test]
        public void TriggeredBuff_StacksUpToTheCap_ThenRefreshesTheShortestCopy()
        {
            var r = Rules;
            Assert.That(r.StackCap, Is.EqualTo(TestContent.Db.Constants.EffectStackCap).And.GreaterThanOrEqualTo(1));
            var res = new CombatSkill { Id = "res", Kind = SkillKind.Buff, Target = SkillTarget.Self, Stat = StatIds.Int, Power = 0.1, Duration = 3 };
            var a = Mon("a", 300, null, x => { x.Spd = 500; x.Str = 1; x.Int = 1; });
            a.Triggers.Add(new TriggeredSkill(FightTrigger.OnAction, res));
            var s = BattleSim.Start(r, Side(a), Side(Mon("b", 999, null, x => x.Spd = 10)));
            int ticks = res.Duration * r.TurnTicks, fired = 0;
            List<FightEffect> Copies() => s.Units[0].Effects.Where(e => e.Source == "res").ToList();
            Action<FightAction> check = x =>
            {
                if (x.Skill?.Id != "res") return;
                fired++;
                var c = Copies();
                Assert.That(c.Count, Is.EqualTo(Math.Min(fired, r.StackCap)), "one copy per trigger, never more than the cap");
                Assert.That(c.Count(e => e.TicksLeft == ticks), Is.EqualTo(1), "the new or refreshed copy has the full duration");
            };
            var rng = new Fixed(0.99);
            while (fired < r.StackCap + 3) BattleSim.Step(r, s, rng, check);
            Assert.That(BattleSim.Eff(r, s.Units[0], StatIds.Int), Is.EqualTo(1 * (1 + 0.1 * r.StackCap)).Within(1e-9));

            var capped = Rules;
            capped.StackCap = 0;   // a hand-built FightRules must set it
            Assert.Throws<ArgumentException>(() => BattleSim.Start(capped, Side(Mon("a")), Side(Mon("b"))));
        }

        [Test]
        public void TimedHpEffects_ChangeMaxHp_KeepTheHpFraction_AndExpire()
        {
            var r = Rules;
            var bulk = new CombatSkill { Id = "bulk", Kind = SkillKind.Buff, Target = SkillTarget.Self, Stat = StatIds.Hp, Power = 0.5, Duration = 2, Cooldown = 99 };
            var sap = new CombatSkill { Id = "sap", Kind = SkillKind.Debuff, Target = SkillTarget.Enemy, Stat = StatIds.Hp, Power = 0.4, Duration = 2, Cooldown = 99 };
            var a = Mon("a", 300, null, x => { x.Spd = 500; x.Str = 1; x.Int = 1; }, bulk, sap);
            var s = BattleSim.Start(r, Side(a), Side(Mon("b", 300, null, x => x.Spd = 10)));
            var me = s.Units[0];
            var foe = s.Units[1];
            double baseHp = 300 * r.HpPerPoint;
            me.Hp = baseHp / 2;
            var log = new List<FightAction>();
            var rng = new Fixed(0.99);
            while (!log.Any(x => x.Skill?.Id == "sap")) BattleSim.Step(r, s, rng, log.Add);

            Assert.That(me.MaxHp, Is.EqualTo(baseHp * 1.5).Within(1e-9), "a timed HP buff raises max HP");
            Assert.That(me.Hp / me.MaxHp, Is.EqualTo(0.5).Within(1e-9), "current HP keeps its fraction");
            Assert.That(BattleSim.Eff(r, me, StatIds.Hp), Is.EqualTo(me.MaxHp));
            Assert.That(foe.MaxHp, Is.EqualTo(baseHp * 0.6).Within(1e-9), "a timed HP debuff lowers max HP");
            Assert.That(foe.Hp, Is.EqualTo(foe.MaxHp).Within(1e-6), "a full-HP target stays full");

            while (me.Effects.Count > 0 || foe.Effects.Count > 0) BattleSim.Step(r, s, rng, log.Add);
            Assert.That(me.MaxHp, Is.EqualTo(baseHp).Within(1e-9), "max HP returns when the buff expires");
            Assert.That(me.Hp / me.MaxHp, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(foe.MaxHp, Is.EqualTo(baseHp).Within(1e-9));
            Assert.That(foe.Hp / foe.MaxHp, Is.GreaterThan(0.99), "a's 1-STR basic attacks barely scratch b");
        }

        [Test]
        public void SingleTargetDebuff_LandsOnAnEnemyWithoutIt_EvenPastATaunter()
        {
            var r = Rules;
            var sap = new CombatSkill { Id = "sap", Kind = SkillKind.Debuff, Target = SkillTarget.Enemy, Stat = StatIds.Def, Power = 0.2, Duration = 5 };
            var a = Mon("a", 300, null, x => { x.Spd = 500; x.Str = 1; x.Int = 1; }, sap);
            Fighter Foe(string id) => Mon(id, 300, null, x => x.Spd = 10);
            var s = BattleSim.Start(r, Side(a), Side(Foe("b1"), Foe("b2"), Foe("b3")));
            s.Units[2].TauntTicks = 1000;   // b2 taunts: it takes the first debuff, then the others get theirs
            var log = new List<FightAction>();
            var rng = new Fixed(0.99);   // a random pick would always take the last candidate
            while (log.Count(x => x.Skill?.Id == "sap") < 3) BattleSim.Step(r, s, rng, log.Add);
            foreach (var u in s.Units.Skip(1))
                Assert.That(u.Effects.Count(e => e.Source == "sap"), Is.EqualTo(1), u.Id);
            Assert.That(s.Units[2].Effects.Single(e => e.Source == "sap").TicksLeft,
                        Is.LessThan(s.Units[1].Effects.Single(e => e.Source == "sap").TicksLeft), "the taunter was debuffed first");
        }

        [Test]
        public void Passive_AppliesAtStart_AndTriggersFire()
        {
            var tough = new CombatSkill { Id = "tough", Kind = SkillKind.Passive, Target = SkillTarget.Self, Stat = StatIds.Hp, Power = 0.5 };
            var f = Mon("a", 300, null, x => x.Spd = 500, tough);
            var onCrit = new CombatSkill { Id = "frenzy", Kind = SkillKind.Buff, Target = SkillTarget.Self, Stat = StatIds.Str, Power = 0.25, Duration = 2 };
            f.Triggers.Add(new TriggeredSkill(FightTrigger.OnCrit, onCrit));
            // the target has no DEX, so no evade roll: a draw of 0 always crits

            var s = BattleSim.Start(Rules, Side(f), Side(Mon("b", 300, null, x => x.Dex = 0)));
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
            Assert.That(counter.Amount, Is.EqualTo((raw - hit.Amount) * r.CounterMult).Within(1e-9), "MON_COUNTER_MULT times what DEF stopped");
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

        static Fighter With(Fighter f, string rule) { f.Rules.Add(rule); return f; }

        /// <summary>A unit that deals no damage (STR and INT 0), so a fight with it only moves by the rule under test.</summary>
        static Fighter Harmless(string id, double spd = 300) => Mon(id, 300, null, x => { x.Str = 0; x.Int = 0; x.Spd = spd; });

        [Test]
        public void TidalOverflow_RegeneratesEachAction_AndOverhealBecomesCappedShield()
        {
            var r = Rules;
            Assert.That(r.TideRegen, Is.GreaterThan(0));
            var s = BattleSim.Start(r, Side(With(Harmless("a", 900), RuleModifiers.TidalOverflow)), Side(Harmless("b", 10)));
            var a = s.Units[0];
            double missing = a.MaxHp * r.TideRegen / 2;
            a.Hp -= missing;
            var log = new List<FightAction>();
            while (s.Outcome == FightOutcome.Ongoing && !log.Any(x => x.Event == FightAction.TideEvent)) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            var first = log.First(x => x.Event == FightAction.TideEvent);
            Assert.That(first.Actor, Is.EqualTo("a"));
            Assert.That(first.Amount, Is.EqualTo(r.TideRegen * a.MaxHp).Within(1e-9), "HP healed plus shield gained");
            Assert.That(a.Hp, Is.EqualTo(a.MaxHp).Within(1e-9));
            Assert.That(a.Shield, Is.EqualTo(r.TideRegen * a.MaxHp - missing).Within(1e-9), "the overflow became shield");
            Assert.That(a.Tally.HealingDone, Is.EqualTo(missing).Within(1e-9), "only HP counts as healing");

            int need = (int)(r.TideShieldCap / r.TideRegen) + 3;
            while (a.Tally.Actions < need && s.Outcome == FightOutcome.Ongoing) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(a.Shield, Is.EqualTo(r.TideShieldCap * a.MaxHp).Within(1e-9), "the shield stops at the cap");
            Assert.That(log.Count(x => x.Event == FightAction.TideEvent), Is.LessThan(need), "nothing left to restore: no event");

            // a heal skill's overflow also becomes shield; without the rule it is lost
            var mend = new CombatSkill { Id = "mend", Kind = SkillKind.Heal, Target = SkillTarget.Self, Scales = StatIds.Wis, Power = 20, Cooldown = 1 };
            foreach (bool rule in new[] { true, false })
            {
                var healer = Mon("h", 300, null, x => { x.Str = 0; x.Int = 0; x.Spd = 900; }, mend);
                if (rule) healer.Rules.Add(RuleModifiers.TidalOverflow);
                var hs = BattleSim.Start(r, Side(healer), Side(Harmless("b", 10)));
                hs.Units[0].Hp = hs.Units[0].MaxHp * r.AiHealBelow / 2;
                var hl = new List<FightAction>();
                while (hs.Outcome == FightOutcome.Ongoing && !hl.Any(x => x.Skill?.Id == "mend")) BattleSim.Step(r, hs, new Fixed(0.99), hl.Add);
                Assert.That(hs.Units[0].Hp, Is.EqualTo(hs.Units[0].MaxHp).Within(1e-9));
                Assert.That(hs.Units[0].Shield > 0, Is.EqualTo(rule));
            }
        }

        [Test]
        public void AbyssalDrain_HealsTheAttackerForAShareOfDamageDealt()
        {
            var r = Rules;
            Assert.That(r.DrainFrac, Is.GreaterThan(0));
            var s = BattleSim.Start(r, Side(With(Attacker(), RuleModifiers.AbyssalDrain)), Side(Mon("b", 999, null, x => x.Spd = 10)));
            var a = s.Units[0];
            a.Hp = a.MaxHp / 2;
            var log = new List<FightAction>();
            while (s.Outcome == FightOutcome.Ongoing && !log.Any(x => x.Event == FightAction.DrainEvent)) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            int i = log.FindIndex(x => x.Event == FightAction.DrainEvent);
            var hit = log[i - 1];
            Assert.That(hit.Actor, Is.EqualTo("a"));
            Assert.That(hit.Target, Is.EqualTo("b"));
            Assert.That(log[i].Amount, Is.EqualTo(r.DrainFrac * hit.Amount).Within(1e-9));
            Assert.That(a.Hp, Is.EqualTo(a.MaxHp / 2 + r.DrainFrac * hit.Amount).Within(1e-9));
            Assert.That(a.Tally.HealingDone, Is.EqualTo(r.DrainFrac * hit.Amount).Within(1e-9));

            // a unit without the rule does not drain
            var plain = BattleSim.Start(r, Side(Attacker()), Side(Mon("b", 999, null, x => x.Spd = 10)));
            plain.Units[0].Hp = plain.Units[0].MaxHp / 2;
            var pl = new List<FightAction>();
            while (plain.Outcome == FightOutcome.Ongoing && !pl.Any(x => x.Actor == "a")) BattleSim.Step(r, plain, new Fixed(0.99), pl.Add);
            Assert.That(plain.Units[0].Hp, Is.EqualTo(plain.Units[0].MaxHp / 2));
            Assert.That(pl.Any(x => x.Event != null), Is.False);
        }

        [Test]
        public void RootRebirth_SurvivesOneKillingBlowPerFight()
        {
            var r = Rules;
            Assert.That(r.RebirthHeal, Is.GreaterThan(0));
            var s = BattleSim.Start(r, Side(Attacker()), Side(With(Harmless("b", 10), RuleModifiers.RootRebirth)));
            var a = s.Units[0];
            var b = s.Units[1];
            b.Hp = 1;
            var log = new List<FightAction>();
            while (s.Outcome == FightOutcome.Ongoing && !log.Any(x => x.Event == FightAction.RebirthEvent)) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            var ev = log.Single(x => x.Event == FightAction.RebirthEvent);
            Assert.That(ev.Actor, Is.EqualTo("b"));
            Assert.That(ev.Amount, Is.EqualTo(r.RebirthHeal * b.MaxHp).Within(1e-9));
            Assert.That(b.Alive && s.Outcome == FightOutcome.Ongoing, "it survived the blow");
            Assert.That(b.Hp, Is.EqualTo(r.RebirthHeal * b.MaxHp).Within(1e-9));
            Assert.That(a.Tally.Kills, Is.EqualTo(0), "no kill is counted");
            Assert.That(log.IndexOf(ev), Is.GreaterThan(log.FindIndex(x => x.Actor == "a" && x.Target == "b")), "the event follows the hit");

            b.Hp = 1;   // the second killing blow lands
            while (s.Outcome == FightOutcome.Ongoing) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(s.Outcome, Is.EqualTo(FightOutcome.SideAWon));
            Assert.That(a.Tally.Kills, Is.EqualTo(1));
            Assert.That(log.Count(x => x.Event == FightAction.RebirthEvent), Is.EqualTo(1), "once per fight");
        }

        [Test]
        public void WailStagger_EachActionDelaysAndWithersEveryEnemy()
        {
            var r = Rules;
            Assert.That(r.WailStagger, Is.GreaterThan(0));
            Assert.That(r.WailWither, Is.GreaterThan(0));
            var s = BattleSim.Start(r, Side(With(Harmless("a", 900), RuleModifiers.WailStagger)),
                Side(Mon("b", 999, null, x => x.Spd = 10), Mon("c", 999, null, x => x.Spd = 10)));
            var b = s.Units[1];
            var c = s.Units[2];
            var log = new List<FightAction>();
            double bAt = 0, cAt = 0;
            while (s.Outcome == FightOutcome.Ongoing && !log.Any(x => x.Event == FightAction.StaggerEvent))
            {
                b.Gauge = r.GaugeSize * 0.9;
                c.Gauge = r.GaugeSize * r.WailStagger / 2;
                double fill = BattleSim.GaugeFill(r, b.Source.Stats.Spd);
                bAt = b.Gauge + fill;
                cAt = c.Gauge + fill;
                BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            }
            var evs = log.Where(x => x.Event == FightAction.StaggerEvent).ToList();
            Assert.That(evs.Select(x => (x.Actor, x.Target)), Is.EqualTo(new[] { ("a", "b"), ("a", "c") }), "every enemy, at the start of its action");
            foreach (var e in evs) Assert.That(e.Amount, Is.EqualTo(r.WailWither * b.MaxHp).Within(1e-9), "true damage, a share of max HP");
            Assert.That(b.Gauge, Is.EqualTo(bAt - r.WailStagger * r.GaugeSize).Within(1e-9));
            Assert.That(c.Gauge, Is.EqualTo(0), "never below an empty gauge");
            Assert.That(cAt, Is.LessThan(r.WailStagger * r.GaugeSize));
            Assert.That(b.Hp, Is.EqualTo(b.MaxHp * (1 - r.WailWither)).Within(1e-9));
        }

        [Test]
        public void UmbralVeil_EvadesSpells_WithAHigherCap()
        {
            var r = Rules;
            Assert.That(r.VeilEvadeCap, Is.GreaterThan(r.EvadeCap));
            var bolt = new CombatSkill { Id = "bolt", Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = StatIds.Int, Power = 2, Cooldown = 1 };
            foreach (bool veil in new[] { true, false })
            {
                var target = Mon("b", 999, null, x => x.Spd = 10);
                if (veil) target.Rules.Add(RuleModifiers.UmbralVeil);
                var s = BattleSim.Start(r, Side(Mon("a", 300, null, x => x.Spd = 900, bolt)), Side(target));
                var log = new List<FightAction>();
                while (s.Outcome == FightOutcome.Ongoing && !log.Any(x => x.Skill?.Id == "bolt")) BattleSim.Step(r, s, new Fixed(0), log.Add);
                var hit = log.First(x => x.Skill?.Id == "bolt");
                Assert.That((hit.Evaded, hit.Graze), Is.EqualTo((veil, !veil)), veil ? "a spell is evaded" : "without it a spell is only grazed");
            }
            Assert.That(BattleSim.EvadeChance(r, 1e9, 0, r.VeilEvadeCap), Is.EqualTo(r.VeilEvadeCap));
            Assert.That(BattleSim.EvadeChance(r, 1e9, 0), Is.EqualTo(r.EvadeCap));
        }

        [Test]
        public void NightFeast_StartsFed_AndEachHitAddsStrAndDamage()
        {
            var r = Rules;
            Assert.That(r.FeastStr + r.FeastDmg, Is.GreaterThan(0));
            var s = BattleSim.Start(r, Side(With(Attacker(), RuleModifiers.NightFeast)), Side(Mon("b", 9999, null, x => x.Spd = 10)));
            var a = s.Units[0];
            Assert.That(a.FeastDmgBonus, Is.EqualTo(r.FeastStart), "it starts every fight already fed");
            var log = new List<FightAction>();
            string h0 = BattleSim.StateHash(s);
            while (s.Outcome == FightOutcome.Ongoing && log.Count(x => x.Event == FightAction.FeastEvent) < 3) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            var feasts = log.Where(x => x.Event == FightAction.FeastEvent).ToList();
            for (int i = 0; i < 3; i++)
                Assert.That(feasts[i].Amount, Is.EqualTo((i + 1) * r.FeastStr + r.FeastStart + (i + 1) * r.FeastDmg).Within(1e-9), "stacks without a cap");
            Assert.That(BattleSim.Eff(r, a, StatIds.Str), Is.EqualTo(300 * (1 + 3 * r.FeastStr)).Within(1e-9));
            var hits = log.Where(x => x.Actor == "a" && x.Event == null).ToList();
            Assert.That(hits[1].Amount, Is.GreaterThan(hits[0].Amount), "the next hit is stronger");
            Assert.That(BattleSim.StateHash(s), Is.Not.EqualTo(h0));

            double plain = OneHit(Attacker(), Mon("b", 300), 0.99);
            Assert.That(OneHit(With(Attacker(), RuleModifiers.NightFeast), Mon("b", 300), 0.99), Is.EqualTo(plain * (1 + r.FeastStart)).Within(1e-9),
                "the first hit already carries the opening bonus");
        }

        [Test]
        public void EchoCast_MirrorTwinTakesTheFirstHits()
        {
            var r = Rules;
            Assert.That(r.EchoDecoys, Is.GreaterThan(0));
            var s = BattleSim.Start(r, Side(Attacker()), Side(With(Harmless("b", 10), RuleModifiers.EchoCast)));
            var b = s.Units[1];
            var log = new List<FightAction>();
            while (s.Outcome == FightOutcome.Ongoing && log.Count(x => x.Actor == "a" && x.Event == null) < r.EchoDecoys + 1)
                BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            var hits = log.Where(x => x.Actor == "a" && x.Event == null).ToList();
            for (int i = 0; i < r.EchoDecoys; i++) Assert.That((hits[i].Evaded, hits[i].Amount), Is.EqualTo((true, 0.0)), "the twin takes it");
            Assert.That(hits[r.EchoDecoys].Evaded, Is.False, "then the hits land");
            Assert.That(log.Count(x => x.Event == FightAction.DecoyEvent), Is.EqualTo(r.EchoDecoys));
            Assert.That(b.DecoysLeft, Is.EqualTo(0));
        }

        [Test]
        public void FirePierce_IgnoresPartOfDefense_AndElementResistance()
        {
            var r = Rules;
            Assert.That(r.FirePierce, Is.GreaterThan(0));
            Assert.That(r.ElementMult("fire", "water"), Is.LessThan(1.0), "water resists fire");
            double raw = r.BasicAttackPower * 300;
            double plain = OneHit(Attacker("fire"), Mon("b", 300, "water", x => x.Def = 600), 0.99);
            Assert.That(plain, Is.EqualTo(raw * r.ElementWeakMult * r.DefK / (r.DefK + 600)).Within(1e-9));
            double pierced = OneHit(With(Attacker("fire"), RuleModifiers.FirePierce), Mon("b", 300, "water", x => x.Def = 600), 0.99);
            Assert.That(pierced, Is.EqualTo(raw * (1 + r.FireBonus) * r.DefK / (r.DefK + 600 * (1 - r.FirePierce))).Within(1e-9));
            // an element strong against the target is kept
            double strong = OneHit(With(Attacker("fire"), RuleModifiers.FirePierce), Mon("b", 300, "nature", x => x.Def = 600), 0.99);
            Assert.That(strong, Is.EqualTo(raw * r.ElementStrongMult * (1 + r.FireBonus) * r.DefK / (r.DefK + 600 * (1 - r.FirePierce))).Within(1e-9));
            // other elements are not pierced
            double water = OneHit(Attacker("water"), Mon("b", 300, "light", x => x.Def = 600), 0.99);
            Assert.That(OneHit(With(Attacker("water"), RuleModifiers.FirePierce), Mon("b", 300, "light", x => x.Def = 600), 0.99),
                Is.EqualTo(water).Within(1e-12));
        }

        [Test]
        public void DoubleTurnStart_FirstActionsAreEachFollowedByAnExtraAction()
        {
            var r = Rules;
            Assert.That(r.DoubleTurns, Is.GreaterThanOrEqualTo(1));
            var s = BattleSim.Start(r, Side(With(Harmless("a"), RuleModifiers.DoubleTurnStart)), Side(Harmless("b")));
            var log = new List<FightAction>();
            while (s.Outcome == FightOutcome.Ongoing && !log.Any(x => x.Actor == "b")) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(s.Units[0].Tally.Actions, Is.EqualTo(2), "a acted twice before b's first action");
            Assert.That(log.Count(x => x.Event == FightAction.DoubleTurnEvent), Is.EqualTo(1));
            for (int t = 0; t < 200; t++) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(s.Units[0].Tally.Actions, Is.EqualTo(s.Units[1].Tally.Actions + r.DoubleTurns), "same SPD: only the opening turns are doubled");
            Assert.That(log.Count(x => x.Event == FightAction.DoubleTurnEvent), Is.EqualTo(r.DoubleTurns));

            var plain = BattleSim.Start(r, Side(Harmless("a")), Side(Harmless("b")));
            for (int t = 0; t < 200; t++) BattleSim.Step(r, plain, new Fixed(0.99));
            Assert.That(plain.Units[0].Tally.Actions, Is.EqualTo(plain.Units[1].Tally.Actions));
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
