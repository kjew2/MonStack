using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>
    /// Idea-pool lane B (#135, specs/16 section 6.2): resentment, vantage, deaths_door, barricade and mana_ward, each on and off,
    /// their edge cases, and the modes (1v1, 4v1, 4v4).
    /// </summary>
    public class LaneBRuleTests
    {
        static FightRules Content => FightRules.FromContent(TestContent.Db);

        /// <summary>The content rules with no avoid or crit chance: hits land, nothing is drawn but the rules' own rolls.</summary>
        static FightRules Plain
        {
            get
            {
                var r = Content;
                r.EvadeBase = 0; r.EvadePerDex = 0; r.CritBase = 0; r.CritPerLuk = 0;
                return r;
            }
        }

        sealed class Fixed : IRandomSource
        {
            readonly double _v;
            public int Draws;
            public Fixed(double v) { _v = v; }
            public double NextDouble() { Draws++; return _v; }
        }

        static Fighter Mon(string id, Action<MonsterStats> tweak = null, params string[] rules)
        {
            var st = new MonsterStats { Hp = 300, Str = 300, Int = 0, Wis = 300, Dex = 300, Def = 300, Spd = 100, Luk = 0 };
            tweak?.Invoke(st);
            var f = new Fighter { Id = id, Stats = st };
            f.Rules.AddRange(rules);
            return f;
        }

        static Fighter Fast(string id, params string[] rules) => Mon(id, x => { x.Spd = 900; x.Hp = 99999; }, rules);
        static Fighter Slow(string id, params string[] rules) => Mon(id, x => { x.Spd = 10; x.Hp = 99999; }, rules);
        static Fighter Harmless(string id, params string[] rules) => Mon(id, x => { x.Str = 0; x.Int = 0; x.Spd = 10; }, rules);

        static CombatSkill Strike(string id, SkillTarget target = SkillTarget.Enemy) =>
            new CombatSkill { Id = id, Kind = SkillKind.Damage, Target = target, Scales = StatIds.Str, Power = 1 };

        /// <summary>Steps until <paramref name="until"/> holds (or the fight ends), logging every action.</summary>
        static List<FightAction> Play(FightRules r, FightState s, IRandomSource rng, Func<bool> until)
        {
            var log = new List<FightAction>();
            for (int i = 0; i < 5000 && !until() && s.Outcome == FightOutcome.Ongoing; i++) BattleSim.Step(r, s, rng, log.Add);
            return log;
        }

        static int Events(List<FightAction> log, string rule) => log.Count(x => x.Event == FightAction.RulePrefix + rule);

        // ------------------------------------------------------------------ constants

        [Test]
        public void Constants_LoadFromContent_AndAreCheckedAtStart()
        {
            var k = TestContent.Db.ConstantValues;
            var r = Content;
            Assert.That(r.ResentFrac, Is.EqualTo(k["MON_RESENT_FRAC"]));
            Assert.That(r.ResentDecay, Is.EqualTo(k["MON_RESENT_DECAY"]));
            Assert.That(r.VantageHp, Is.EqualTo(k["MON_VANTAGE_HP"]));
            Assert.That(r.DoorStart, Is.EqualTo(k["MON_DOOR_START"]));
            Assert.That(r.DoorStep, Is.EqualTo(k["MON_DOOR_STEP"]));
            Assert.That(r.BarricadeCap, Is.EqualTo(k["MON_BARRICADE_CAP"]));
            Assert.That(r.WardWis, Is.EqualTo(k["MON_WARD_WIS"]));
            Assert.That(r.WardShare, Is.EqualTo(k["MON_WARD_SHARE"]));
            Assert.That(r.WardCdr, Is.EqualTo(k["MON_WARD_CDR"]));

            var bad = new Action<FightRules>[]
            {
                x => x.ResentFrac = -0.1, x => x.ResentDecay = 1.5, x => x.VantageHp = -1, x => x.DoorStart = 2, x => x.DoorStep = -0.1,
                x => x.BarricadeCap = 1.1, x => x.WardWis = -1, x => x.WardShare = 1.2, x => x.WardCdr = double.PositiveInfinity,
            };
            foreach (var set in bad)
            {
                var rr = Content;
                set(rr);
                Assert.Throws<ArgumentException>(() => BattleSim.Start(rr, new[] { Mon("a") }, new[] { Mon("b") }));
            }
        }

        [Test]
        public void FightsWithoutTheRules_DoNotDependOnTheLaneConstants()
        {
            Fighter[] A() => new[] { Mon("a1", null), Mon("a2", x => x.Int = 400) };
            Fighter[] B() => new[] { Mon("b1", x => x.Def = 100), Mon("b2", x => x.Luk = 300) };
            var on = Content;
            var off = Content;
            off.ResentFrac = off.ResentDecay = off.VantageHp = off.DoorStart = off.DoorStep = off.BarricadeCap = 0;
            off.WardWis = off.WardShare = off.WardCdr = 0;
            for (ulong seed = 1; seed <= 5; seed++)
                Assert.That(BattleSim.Resolve(on, A(), B(), seed).Hash, Is.EqualTo(BattleSim.Resolve(off, A(), B(), seed).Hash));
        }

        // ------------------------------------------------------------------ resentment

        [Test]
        public void Resentment_BanksEnemyDamageAsFlatStr_AndEachOwnActionDrainsIt()
        {
            var r = Plain;
            var s = BattleSim.Start(r, new[] { Fast("a") }, new[] { Slow("b", RuleModifiers.Resentment) });
            var b = s.Units[1];
            var log = Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
            Assert.That(b.Tally.DamageTaken, Is.GreaterThan(0));
            Assert.That(b.ResentPool, Is.EqualTo(r.ResentFrac * b.Tally.DamageTaken).Within(1e-9));
            Assert.That(BattleSim.Eff(r, b, StatIds.Str), Is.EqualTo(b.Source.Stats.Str + b.ResentPool).Within(1e-9), "a flat STR bonus");
            Assert.That(log.Last(x => x.Event != null).Amount, Is.EqualTo(b.ResentPool).Within(1e-9), "logged with the pool");

            // own actions drain it (a harmless enemy adds nothing)
            s = BattleSim.Start(r, new[] { Harmless("a") }, new[] { Fast("b", RuleModifiers.Resentment) });
            b = s.Units[1];
            b.ResentPool = 100;
            Play(r, s, null, () => b.Tally.Actions >= 2);
            Assert.That(s.Units[0].Tally.DamageDealt, Is.EqualTo(0));
            Assert.That(b.ResentPool, Is.EqualTo(100 * (1 - r.ResentDecay) * (1 - r.ResentDecay)).Within(1e-9));

            // off: no pool
            s = BattleSim.Start(r, new[] { Fast("a") }, new[] { Slow("b") });
            Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
            Assert.That(s.Units[1].ResentPool, Is.EqualTo(0));
        }

        [Test]
        public void Resentment_IgnoresItsOwnEntropyRecoil()
        {
            var r = FightRules.FromContent(TestContent.Db, true);
            var b = Fast("b", RuleModifiers.Resentment);
            b.Triggers.Add(new TriggeredSkill(FightTrigger.OnAction,
                new CombatSkill { Id = "pump", Kind = SkillKind.Buff, Target = SkillTarget.Self, Stat = StatIds.Def, Power = 0.01, Duration = 1 }));
            var s = BattleSim.Start(r, new[] { Harmless("a") }, new[] { b });
            var u = s.Units[1];
            u.Entropy = r.Tower.EntropyCap;   // its next trigger vents: recoil first
            var log = Play(r, s, new Fixed(0.99), () => u.Tally.Actions >= 1);
            Assert.That(log.Any(x => x.Event == "vent:recoil" && x.Amount > 0), Is.True);
            Assert.That(u.Tally.DamageTaken, Is.GreaterThan(0));
            Assert.That(u.ResentPool, Is.EqualTo(0), "only damage from enemies fills the pool");
        }

        // ------------------------------------------------------------------ vantage

        /// <summary>Side A's attacker(s) against a vantage carrier at <paramref name="hpFrac"/> of its max HP.</summary>
        static FightState VantageFight(FightRules r, Fighter[] attackers, double hpFrac, Fighter carrier = null)
        {
            var s = BattleSim.Start(r, attackers, new[] { carrier ?? Slow("v", RuleModifiers.Vantage) });
            var v = s.Units.Last();
            v.Hp = hpFrac * v.MaxHp;
            return s;
        }

        [Test]
        public void Vantage_BelowItsThreshold_StrikesFirstOncePerEnemyAction()
        {
            var r = Plain;
            var s = VantageFight(r, new[] { Fast("a") }, 0.4);
            var log = Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
            int ev = log.FindIndex(x => x.Event == FightAction.RulePrefix + RuleModifiers.Vantage);
            int counter = log.FindIndex(x => x.Actor == "v" && x.Event == null);
            int hit = log.FindIndex(x => x.Actor == "a" && x.Event == null);
            Assert.That(ev, Is.GreaterThanOrEqualTo(0));
            Assert.That(ev < counter && counter < hit, Is.True, "the counter comes before the enemy's hit");
            Assert.That(log[counter].Target, Is.EqualTo("a"));
            Assert.That(log[counter].Amount, Is.GreaterThan(0));
            Assert.That(s.Units[1].Tally.Counters, Is.EqualTo(1));

            // above the threshold: no counter
            s = VantageFight(r, new[] { Fast("a") }, 0.6);
            log = Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
            Assert.That(Events(log, RuleModifiers.Vantage), Is.EqualTo(0));

            // off: no counter
            s = VantageFight(r, new[] { Fast("a") }, 0.4, Slow("v"));
            log = Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
            Assert.That(Events(log, RuleModifiers.Vantage), Is.EqualTo(0));
        }

        [Test]
        public void Vantage_KillingTheAttacker_CancelsItsAction()
        {
            var r = Plain;
            var s = VantageFight(r, new[] { Mon("a", x => x.Spd = 900) }, 0.4);
            s.Units[0].Hp = 1;
            var log = Play(r, s, null, () => false);
            Assert.That(s.Outcome, Is.EqualTo(FightOutcome.SideBWon));
            Assert.That(log.Any(x => x.Actor == "a" && x.Event == null), Is.False, "the attack never comes");
            Assert.That(s.Units[1].Tally.DamageTaken, Is.EqualTo(0));
        }

        [Test]
        public void Vantage_IgnoresAoeAndTriggeredHits()
        {
            var r = Plain;
            var a = Fast("a");
            a.Skills.Add(Strike("sweep", SkillTarget.AllEnemies));
            var s = VantageFight(r, new[] { a }, 0.4);
            var log = Play(r, s, null, () => s.Units[0].Tally.Actions >= 2);
            Assert.That(log.Count(x => x.Skill?.Id == "sweep"), Is.EqualTo(2));
            Assert.That(Events(log, RuleModifiers.Vantage), Is.EqualTo(0), "an AoE is not aimed at it");

            // a triggered hit (the attacker's OnBasicAttack jab) does not provoke it: only the action's own hit does
            var c = Fast("c");
            c.Triggers.Add(new TriggeredSkill(FightTrigger.OnBasicAttack, Strike("jab")));
            s = VantageFight(r, new[] { c }, 0.4);
            log = Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
            Assert.That(log.Count(x => x.Skill?.Id == "jab"), Is.EqualTo(1));
            Assert.That(Events(log, RuleModifiers.Vantage), Is.EqualTo(1), "the basic attack only");
        }

        [Test]
        public void Vantage_SeveralEnemiesInARow_EachProvokeOneCounter_AndAnEchoIsTheSameAction()
        {
            var r = Plain;
            var s = VantageFight(r, new[] { Fast("a1"), Fast("a2") }, 0.4);
            var log = Play(r, s, null, () => s.Units[0].Tally.Actions >= 1 && s.Units[1].Tally.Actions >= 1);
            int actions = s.Units[0].Tally.Actions + s.Units[1].Tally.Actions;
            Assert.That(Events(log, RuleModifiers.Vantage), Is.EqualTo(actions));
            Assert.That(log.Where(x => x.Event == FightAction.RulePrefix + RuleModifiers.Vantage).Select(x => x.Target).Distinct().Count(), Is.EqualTo(2));

            var e = Fast("e", RuleModifiers.EchoCast);
            e.Skills.Add(Strike("bolt"));
            s = VantageFight(r, new[] { e }, 0.4);
            log = Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
            Assert.That(log.Count(x => x.Skill?.Id == "bolt" && x.Actor == "e"), Is.EqualTo(2), "cast and echo");
            Assert.That(Events(log, RuleModifiers.Vantage), Is.EqualTo(1));
        }

        [Test]
        public void Vantage_CounterIsNeverCountered()
        {
            // The attacker carries vantage and counter_mitigated and is below the threshold too: the carrier's counter fires neither.
            var r = Plain;
            var a = Fast("a", RuleModifiers.Vantage, RuleModifiers.CounterMitigated);
            var s = VantageFight(r, new[] { a }, 0.4);
            s.Units[0].Hp = 0.4 * s.Units[0].MaxHp;
            var log = Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
            Assert.That(Events(log, RuleModifiers.Vantage), Is.EqualTo(1));
            Assert.That(log.Count(x => x.Counter), Is.EqualTo(0), "no counter_mitigated counter of the vantage strike");
            Assert.That(s.Units[0].Tally.Counters, Is.EqualTo(0));
            Assert.That(s.Units[1].Tally.Counters, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ deaths_door

        static Fighter Doomed(string id, params string[] rules) => Mon(id, x => { x.Spd = 10; x.Hp = 10; x.Def = 0; }, rules);

        [Test]
        public void DeathsDoor_FirstKillingBlowLeavesOneHp_LaterOnesRollFallingOdds()
        {
            var r = Plain;
            var s = BattleSim.Start(r, new[] { Fast("a") }, new[] { Doomed("d", RuleModifiers.DeathsDoor) });
            var d = s.Units[1];
            var rng = new Fixed(0.65);   // holds at 70%, falls at 60%
            var log = Play(r, s, rng, () => false);
            var odds = log.Where(x => x.Event == FightAction.RulePrefix + RuleModifiers.DeathsDoor).Select(x => x.Amount).ToList();
            Assert.That(odds.Count, Is.EqualTo(3));
            Assert.That(odds[0], Is.EqualTo(1));
            Assert.That(odds[1], Is.EqualTo(r.DoorStart).Within(1e-12));
            Assert.That(odds[2], Is.EqualTo(r.DoorStart - r.DoorStep).Within(1e-12));
            Assert.That(rng.Draws, Is.EqualTo(2), "one draw per roll, none for the free save");
            Assert.That(d.Alive, Is.False);
            Assert.That(d.DoorHeld, Is.EqualTo(1));
            Assert.That(s.Outcome, Is.EqualTo(FightOutcome.SideAWon));
            Assert.That(s.Units[0].Tally.Kills, Is.EqualTo(1));

            // a held roll leaves it at exactly 1 HP
            s = BattleSim.Start(r, new[] { Fast("a") }, new[] { Doomed("d", RuleModifiers.DeathsDoor) });
            d = s.Units[1];
            Play(r, s, new Fixed(0), () => d.DoorHeld >= 2);
            Assert.That(d.Hp, Is.EqualTo(1));
            Assert.That(d.OnDoor, Is.True);

            // off: the first killing blow kills, with no draw
            s = BattleSim.Start(r, new[] { Fast("a") }, new[] { Doomed("d") });
            rng = new Fixed(0);
            Play(r, s, rng, () => false);
            Assert.That(s.Units[1].Alive, Is.False);
            Assert.That(rng.Draws, Is.EqualTo(0));
        }

        [Test]
        public void DeathsDoor_AtZeroOdds_FallsWithoutADraw()
        {
            var r = Plain;
            r.DoorStart = 0.1;
            r.DoorStep = 0.1;
            var s = BattleSim.Start(r, new[] { Fast("a") }, new[] { Doomed("d", RuleModifiers.DeathsDoor) });
            var rng = new Fixed(0);
            Play(r, s, rng, () => false);
            Assert.That(s.Units[1].DoorHeld, Is.EqualTo(1));
            Assert.That(rng.Draws, Is.EqualTo(1), "the 10% roll; the 0% one draws nothing");
        }

        [Test]
        public void DeathsDoor_ComesAfterRootRebirth()
        {
            var r = Plain;
            var s = BattleSim.Start(r, new[] { Fast("a") }, new[] { Doomed("d", RuleModifiers.DeathsDoor, RuleModifiers.RootRebirth) });
            var d = s.Units[1];
            var log = Play(r, s, new Fixed(0.99), () => false);
            int rebirth = log.FindIndex(x => x.Event == FightAction.RebirthEvent);
            int door = log.FindIndex(x => x.Event == FightAction.RulePrefix + RuleModifiers.DeathsDoor);
            Assert.That(rebirth, Is.GreaterThanOrEqualTo(0));
            Assert.That(door, Is.GreaterThan(rebirth), "rebirth first, then Death's Door");
            Assert.That(d.Alive, Is.False, "the 0.99 draw fails the first roll");
        }

        [Test]
        public void DeathsDoor_TrueDamageCountsAsAHit()
        {
            // wail_stagger's wither is the attacker's only damage (STR and INT 0): it brings the carrier to the door, then rolls
            var r = Plain;
            var s = BattleSim.Start(r, new[] { Mon("a", x => { x.Str = 0; x.Int = 0; x.Spd = 900; x.Hp = 99999; }, RuleModifiers.WailStagger) },
                new[] { Doomed("d", RuleModifiers.DeathsDoor) });
            var d = s.Units[1];
            var rng = new Fixed(0.99);
            var log = Play(r, s, rng, () => false);
            Assert.That(log.Where(x => x.Event == FightAction.RulePrefix + RuleModifiers.DeathsDoor).Select(x => x.Amount),
                Is.EqualTo(new[] { 1, r.DoorStart }));
            Assert.That(rng.Draws, Is.EqualTo(1));
            Assert.That(d.Alive, Is.False);
        }

        [Test]
        public void DeathsDoor_SavesFromItsOwnEntropyRecoil()
        {
            var r = FightRules.FromContent(TestContent.Db, true);
            var b = Fast("b", RuleModifiers.DeathsDoor);
            b.Triggers.Add(new TriggeredSkill(FightTrigger.OnAction,
                new CombatSkill { Id = "pump", Kind = SkillKind.Buff, Target = SkillTarget.Self, Stat = StatIds.Def, Power = 0.01, Duration = 1 }));
            var s = BattleSim.Start(r, new[] { Harmless("a") }, new[] { b });
            var u = s.Units[1];
            u.Entropy = r.Tower.EntropyCap;
            u.Hp = 1e-3;
            var log = Play(r, s, new Fixed(0.99), () => u.Tally.Actions >= 1);
            Assert.That(log.Any(x => x.Event == "vent:recoil"), Is.True);
            Assert.That(u.Alive, Is.True);
            Assert.That(u.OnDoor, Is.True);
            Assert.That(u.Hp, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ barricade

        static CombatSkill Guard(double power, int duration) =>
            new CombatSkill { Id = "guard", Kind = SkillKind.Shield, Target = SkillTarget.Self, Scales = StatIds.Hp, Power = power, Duration = duration };

        [Test]
        public void Barricade_ShieldsNeverExpire()
        {
            var r = Plain;
            Fighter Carrier(params string[] rules)
            {
                var f = Harmless("c", rules);
                f.Triggers.Add(new TriggeredSkill(FightTrigger.BattleStart, Guard(0.2, 1)));
                return f;
            }
            var s = BattleSim.Start(r, new[] { Harmless("a") }, new[] { Carrier(RuleModifiers.Barricade) });
            for (int t = 0; t < 3 * r.TurnTicks; t++) BattleSim.Step(r, s, null);
            Assert.That(s.Units[1].Shield, Is.EqualTo(0.2 * s.Units[1].MaxHp).Within(1e-9));
            Assert.That(s.Units[1].ShieldTicks, Is.EqualTo(0));

            s = BattleSim.Start(r, new[] { Harmless("a") }, new[] { Carrier() });
            for (int t = 0; t < 3 * r.TurnTicks; t++) BattleSim.Step(r, s, null);
            Assert.That(s.Units[1].Shield, Is.EqualTo(0), "off: the timed shield expires");
        }

        [Test]
        public void Barricade_ShieldsStackUpToItsCap()
        {
            var r = Plain;
            Fighter Carrier(params string[] rules)
            {
                var f = Mon("c", x => { x.Str = 0; x.Spd = 900; }, rules);
                f.Triggers.Add(new TriggeredSkill(FightTrigger.OnAction, Guard(0.25, 1)));
                return f;
            }
            var s = BattleSim.Start(r, new[] { Harmless("a") }, new[] { Carrier(RuleModifiers.Barricade) });
            var c = s.Units[1];
            var log = Play(r, s, null, () => c.Tally.Actions >= 2);
            Assert.That(c.Shield, Is.EqualTo(0.5 * c.MaxHp).Within(1e-9));
            Play(r, s, null, () => c.Tally.Actions >= 4);
            Assert.That(c.Shield, Is.EqualTo(r.BarricadeCap * c.MaxHp).Within(1e-9));
            Assert.That(Events(log, RuleModifiers.Barricade), Is.EqualTo(2), "each shield the carrier gains is logged");

            s = BattleSim.Start(r, new[] { Harmless("a") }, new[] { Carrier() });
            c = s.Units[1];
            Play(r, s, null, () => c.Tally.Actions >= 4);
            Assert.That(c.Shield, Is.EqualTo(0.25 * c.MaxHp).Within(1e-9), "off: the bigger shield, no stacking");

            // a single shield bigger than the cap still counts whole
            r.BarricadeCap = 0.1;
            s = BattleSim.Start(r, new[] { Harmless("a") }, new[] { Carrier(RuleModifiers.Barricade) });
            c = s.Units[1];
            Play(r, s, null, () => c.Tally.Actions >= 2);
            Assert.That(c.Shield, Is.EqualTo(0.25 * c.MaxHp).Within(1e-9));
        }

        [Test]
        public void Barricade_WithTidalOverflowOrAbyssalDrain_TheLargerCapApplies()
        {
            var r = Plain;
            Assert.That(r.BarricadeCap, Is.GreaterThan(r.TideShieldCap), "the content values this test assumes");
            Assert.That(r.DrainShieldCap, Is.GreaterThan(r.BarricadeCap));
            FightUnit Run(params string[] rules)
            {
                var s = BattleSim.Start(r, new[] { Harmless("a") }, new[] { Mon("c", x => { x.Str = 0; x.Spd = 900; }, rules) });
                var c = s.Units[1];
                Play(r, s, null, () => c.Tally.Actions >= 200);
                return c;
            }
            var tide = Run(RuleModifiers.TidalOverflow);
            Assert.That(tide.Shield, Is.EqualTo(r.TideShieldCap * tide.MaxHp).Within(1e-6));
            var both = Run(RuleModifiers.TidalOverflow, RuleModifiers.Barricade);
            Assert.That(both.Shield, Is.EqualTo(r.BarricadeCap * both.MaxHp).Within(1e-6));

            // abyssal_drain's cap is larger: it keeps it (drain overheal from hits on a sturdy dummy)
            var s2 = BattleSim.Start(r, new[] { Mon("a", x => { x.Str = 0; x.Hp = 99999; x.Def = 0; x.Spd = 10; }) },
                new[] { Mon("c", x => { x.Spd = 900; }, RuleModifiers.AbyssalDrain, RuleModifiers.Barricade) });
            var c2 = s2.Units[1];
            Play(r, s2, null, () => c2.Tally.Actions >= 60);
            Assert.That(c2.Shield, Is.GreaterThan(r.BarricadeCap * c2.MaxHp));
        }

        // ------------------------------------------------------------------ mana_ward

        [Test]
        public void ManaWard_StartsAtWisMultiple_AndTakesItsShareOfEachBlow()
        {
            var r = Plain;
            FightState Run(params string[] rules)
            {
                var s = BattleSim.Start(r, new[] { Fast("a") }, new[] { Slow("w", rules) });
                Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
                return s;
            }
            var on = Run(RuleModifiers.ManaWard);
            var off = Run();
            var w = on.Units[1];
            double ward0 = r.WardWis * w.Source.Stats.Wis;
            double blow = off.Units[1].Tally.DamageTaken;
            Assert.That(blow, Is.GreaterThan(0).And.LessThan(ward0 / r.WardShare), "the ward outlasts one blow");
            Assert.That(w.Tally.DamageTaken, Is.EqualTo((1 - r.WardShare) * blow).Within(1e-9));
            Assert.That(w.Ward, Is.EqualTo(ward0 - r.WardShare * blow).Within(1e-9));
            Assert.That(off.Units[1].Ward, Is.EqualTo(0));
        }

        [Test]
        public void ManaWard_ComesAfterTheShield_AndRunsDry()
        {
            var r = Plain;
            var w = Slow("w", RuleModifiers.ManaWard);
            w.Triggers.Add(new TriggeredSkill(FightTrigger.BattleStart, Guard(5, 0)));   // a shield far bigger than any blow
            var s = BattleSim.Start(r, new[] { Fast("a") }, new[] { w });
            var u = s.Units[1];
            Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
            Assert.That(u.Shield, Is.LessThan(5 * u.MaxHp));
            Assert.That(u.Ward, Is.EqualTo(r.WardWis * u.Source.Stats.Wis).Within(1e-9), "the shield took the whole blow");

            w = Slow("w", RuleModifiers.ManaWard);
            w.Stats.Wis = 1;   // a ward of MON_WARD_WIS
            s = BattleSim.Start(r, new[] { Fast("a") }, new[] { w });
            u = s.Units[1];
            var log = Play(r, s, null, () => s.Units[0].Tally.Actions >= 3);
            Assert.That(u.Ward, Is.EqualTo(0));
            var wardLog = log.Where(x => x.Event == FightAction.RulePrefix + RuleModifiers.ManaWard).Select(x => x.Amount).ToList();
            Assert.That(wardLog, Is.EqualTo(new[] { r.WardWis, 0.0 }), "logged at fight start with the ward, then with what is left");
            Assert.That(u.Tally.DamageTaken, Is.EqualTo(s.Units[0].Tally.DamageDealt));
        }

        [Test]
        public void ManaWard_EveryWardIsSetBeforeTheFirstWardLine()
        {
            // the web reads every unit's ward from the first logged line, so both wards are in place by then
            var r = Plain;
            var s = BattleSim.Start(r, new[] { Slow("w1", RuleModifiers.ManaWard), Slow("w2", RuleModifiers.ManaWard) }, new[] { Slow("a") });
            var seen = new List<double[]>();
            BattleSim.Step(r, s, null, x =>
            {
                if (x.Event == FightAction.RulePrefix + RuleModifiers.ManaWard) seen.Add(new[] { s.Units[0].Ward, s.Units[1].Ward });
            });
            double ward = r.WardWis * 300;
            Assert.That(seen, Has.Count.EqualTo(2));
            Assert.That(seen[0], Is.EqualTo(new[] { ward, ward }));
        }

        [Test]
        public void ManaWard_SpeedsCooldownsWhileItHolds()
        {
            var r = Plain;
            int Casts(params string[] rules)
            {
                var f = Mon("w", x => x.Spd = 900, rules);
                f.Skills.Add(new CombatSkill { Id = "bolt", Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = StatIds.Str, Power = 0.01, Cooldown = 3 });
                var s = BattleSim.Start(r, new[] { Mon("a", x => { x.Str = 0; x.Hp = 99999; x.Spd = 10; }) }, new[] { f });
                var log = Play(r, s, null, () => s.Units[1].Tally.Actions >= 12);
                return log.Count(x => x.Skill?.Id == "bolt");
            }
            int off = Casts(), on = Casts(RuleModifiers.ManaWard);
            Assert.That(off, Is.EqualTo(4), "every third action");
            Assert.That(on, Is.GreaterThan(off));

            r.WardWis = 0;   // no ward, no speed-up
            Assert.That(Casts(RuleModifiers.ManaWard), Is.EqualTo(off));
        }

        // ------------------------------------------------------------------ modes

        static Fighter Carrier(string id, string rule, string el)
        {
            var f = new Fighter
            {
                Id = id, Element = el,
                Stats = new MonsterStats { Hp = 420, Str = 300, Int = 120, Wis = 300, Dex = 200, Def = 280, Spd = 220, Luk = 200 },
                Skills = { Strike("hit"), Guard(0.15, 2) },
            };
            f.Skills[0].Cooldown = 2;
            f.Skills[1].Cooldown = 4;
            f.Rules.Add(rule);
            return f;
        }

        static readonly string[] Lane = { RuleModifiers.Resentment, RuleModifiers.Vantage, RuleModifiers.DeathsDoor, RuleModifiers.Barricade, RuleModifiers.ManaWard };

        [Test]
        public void EveryRule_In1v1_4v1_And4v4_IsDeterministic_AndCloneContinuesIdentically()
        {
            var r = Content;
            foreach (var rule in Lane)
            {
                var shapes = new (Fighter[] A, Fighter[] B)[]
                {
                    (new[] { Carrier("c", rule, "fire") }, new[] { Mon("foe", x => { x.Hp = 500; x.Spd = 200; }) }),
                    (new[] { Carrier("c1", rule, "fire"), Carrier("c2", rule, "water"), Mon("m3"), Mon("m4") },
                     new[] { Mon("boss", x => { x.Hp = 2500; x.Str = 500; x.Spd = 250; }) }),
                    (new[] { Carrier("c1", rule, "fire"), Carrier("c2", rule, "water"), Mon("m3"), Mon("m4") },
                     new[] { Carrier("d1", rule, "nature"), Mon("e2"), Mon("e3"), Mon("e4") }),
                };
                foreach (var (A, B) in shapes)
                {
                    var once = BattleSim.Resolve(r, A, B, 11);
                    Assert.That(BattleSim.Resolve(r, A, B, 11).Hash, Is.EqualTo(once.Hash), rule);
                    Assert.That(once.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut), rule + " " + A.Length + "v" + B.Length);

                    var s = BattleSim.Start(r, A, B);
                    var rng = new RngStream(BattleSim.StreamName, 11);
                    for (int i = 0; i < 40 && s.Outcome == FightOutcome.Ongoing; i++) BattleSim.Step(r, s, rng);
                    var copy = s.Clone();
                    var rngCopy = rng.Clone();
                    while (BattleSim.Step(r, copy, rngCopy) == FightOutcome.Ongoing) { }
                    Assert.That(BattleSim.StateHash(copy), Is.EqualTo(once.Hash), rule);
                }
            }
        }
    }
}
