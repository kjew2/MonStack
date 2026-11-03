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
    /// Species lane: the Pyre Stag (#142, specs/16 section 6.2): pyre_tithe, ash_shroud, pyre_mercy, ember_eternal and Immolate's
    /// cost, each on and off, their edge cases and the modes (1v1, 4v4).
    /// </summary>
    public class PyreStagRuleTests
    {
        static FightRules Content => FightRules.FromContent(TestContent.Db);

        /// <summary>The content rules with no avoid or crit chance: hits land, nothing is drawn but the rules' own picks.</summary>
        static FightRules Plain
        {
            get
            {
                var r = Content;
                r.EvadeBase = 0; r.EvadePerDex = 0; r.CritBase = 0; r.CritPerLuk = 0;
                return r;
            }
        }

        sealed class Counting : IRandomSource
        {
            public int Draws;
            public double NextDouble() { Draws++; return 0.99; }
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
        static Fighter Harmless(string id, params string[] rules) => Mon(id, x => { x.Str = 0; x.Int = 0; x.Spd = 10; x.Hp = 99999; }, rules);

        static CombatSkill SelfHeal(double power = 0.5) =>
            new CombatSkill { Id = "test_heal", Kind = SkillKind.Heal, Target = SkillTarget.Self, Scales = StatIds.Wis, Power = power };

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
            Assert.That(r.ImmolateSelf, Is.EqualTo(k["MON_IMMOLATE_SELF"]));
            Assert.That(r.TitheCost, Is.EqualTo(k["MON_TITHE_COST"]));
            Assert.That(r.ShroudFrac, Is.EqualTo(k["MON_SHROUD_FRAC"]));
            Assert.That(r.MercyMult, Is.EqualTo(k["MON_MERCY_MULT"]));
            Assert.That(r.EmberDmg, Is.EqualTo(k["MON_EMBER_DMG"]));
            Assert.That(r.EmberFloor, Is.EqualTo(k["MON_EMBER_FLOOR"]));

            var bad = new Action<FightRules>[]
            {
                x => x.ImmolateSelf = 1.5, x => x.TitheCost = -0.1, x => x.ShroudFrac = 1, x => x.ShroudFrac = -0.1, x => x.MercyMult = -1,
                x => x.EmberDmg = -1, x => x.EmberDmg = double.PositiveInfinity, x => x.EmberFloor = 0, x => x.EmberFloor = 1.1,
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
            Fighter[] A() { var a = Mon("a1"); a.Skills.Add(SelfHeal()); return new[] { a, Mon("a2", x => x.Int = 400) }; }
            Fighter[] B() => new[] { Mon("b1", x => x.Def = 100), Mon("b2", x => x.Luk = 300) };
            var on = Content;
            var off = Content;
            off.ImmolateSelf = off.TitheCost = off.ShroudFrac = off.EmberDmg = off.MercyMult = 0;
            off.EmberFloor = 1;
            for (ulong seed = 1; seed <= 5; seed++)
                Assert.That(BattleSim.Resolve(on, A(), B(), seed).Hash, Is.EqualTo(BattleSim.Resolve(off, A(), B(), seed).Hash));
        }

        // ------------------------------------------------------------------ pyre_tithe

        [Test]
        public void Tithe_EachActionCostsHp_NeverBelowOne_AndIsNotDamage()
        {
            var r = Plain;
            var s = BattleSim.Start(r, new[] { Mon("a", x => x.Spd = 900, RuleModifiers.PyreTithe) }, new[] { Harmless("b") });
            var a = s.Units[0];
            double max = a.MaxHp;
            var log = Play(r, s, null, () => a.Tally.Actions >= 3);
            Assert.That(a.Hp, Is.EqualTo(max * (1 - 3 * r.TitheCost)).Within(1e-6));
            Assert.That(a.Tally.DamageTaken, Is.EqualTo(0), "an HP cost is not damage");
            Assert.That(Events(log, RuleModifiers.PyreTithe), Is.EqualTo(3));
            Assert.That(log.First(x => x.Event == BattleSim.TitheEvent).Amount, Is.EqualTo(r.TitheCost * max).Within(1e-6));

            a.Hp = 1.5;
            log = Play(r, s, null, () => a.Tally.Actions >= 5);
            Assert.That(a.Hp, Is.EqualTo(1.0).Within(1e-9), "never below 1 HP");
            Assert.That(a.Alive);

            // off: no cost
            s = BattleSim.Start(r, new[] { Mon("a", x => x.Spd = 900) }, new[] { Harmless("b") });
            log = Play(r, s, null, () => s.Units[0].Tally.Actions >= 3);
            Assert.That(s.Units[0].Hp, Is.EqualTo(s.Units[0].MaxHp));
            Assert.That(Events(log, RuleModifiers.PyreTithe), Is.EqualTo(0));
        }

        [Test]
        public void Tithe_DamageGrowsWithMissingHp()
        {
            double Dealt(bool tithe, double hpFrac)
            {
                var r = Plain;
                r.TitheCost = 0;   // no cost: only the multiplier
                var s = BattleSim.Start(r, new[] { tithe ? Fast("a", RuleModifiers.PyreTithe) : Fast("a") }, new[] { Slow("b") });
                s.Units[0].Hp = hpFrac * s.Units[0].MaxHp;
                Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
                return s.Units[1].Tally.DamageTaken;
            }
            double plain = Dealt(false, 1);
            Assert.That(Dealt(true, 1), Is.EqualTo(plain).Within(1e-6), "full HP: x1");
            Assert.That(Dealt(true, 0.5), Is.EqualTo(plain * 1.5).Within(1e-6));
            Assert.That(Dealt(true, 0.1), Is.EqualTo(plain * 1.9).Within(1e-6));
            Assert.That(Dealt(false, 0.1), Is.EqualTo(plain).Within(1e-6), "off: x1");
        }

        // ------------------------------------------------------------------ Immolate

        [Test]
        public void Immolate_CostsItsCasterHp_OtherSkillsDoNot()
        {
            foreach (var id in new[] { BattleSim.ImmolateSkill, "other" })
            {
                var r = Plain;
                var a = Fast("a");
                a.Skills.Add(new CombatSkill { Id = id, Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = StatIds.Str, Power = 1, Cooldown = 99 });
                var s = BattleSim.Start(r, new[] { a }, new[] { Harmless("b") });
                var u = s.Units[0];
                Play(r, s, null, () => u.Tally.Actions >= 2);
                double want = id == BattleSim.ImmolateSkill ? u.MaxHp * (1 - r.ImmolateSelf) : u.MaxHp;
                Assert.That(u.Hp, Is.EqualTo(want).Within(1e-6), id);
                Assert.That(u.Tally.DamageTaken, Is.EqualTo(0));
            }
        }

        // ------------------------------------------------------------------ ash_shroud

        [Test]
        public void AshShroud_TurnsHpIntoALastingShieldAtFightStart()
        {
            var r = Plain;
            var s = BattleSim.Start(r, new[] { Mon("a", null, RuleModifiers.AshShroud) }, new[] { Harmless("b") });
            var a = s.Units[0];
            double max = a.MaxHp;
            var log = Play(r, s, null, () => s.Ticks >= 1);
            Assert.That(a.Hp, Is.EqualTo(max * (1 - r.ShroudFrac)).Within(1e-6));
            Assert.That(a.Shield, Is.EqualTo(max * r.ShroudFrac).Within(1e-6));
            Assert.That(a.ShieldTicks, Is.EqualTo(0), "no expiry");
            Assert.That(a.Tally.DamageTaken, Is.EqualTo(0));
            Assert.That(log.Single(x => x.Event == BattleSim.ShroudEvent).Amount, Is.EqualTo(max * r.ShroudFrac).Within(1e-6));

            Play(r, s, null, () => s.Ticks >= 200);
            Assert.That(a.Shield, Is.EqualTo(max * r.ShroudFrac).Within(1e-6), "still there");

            // it starts the fight missing MON_SHROUD_FRAC of its HP (what pyre_tithe and "below X% HP" rules read)
            Assert.That(1 - a.Hp / a.MaxHp, Is.EqualTo(r.ShroudFrac).Within(1e-9));

            // off
            s = BattleSim.Start(r, new[] { Mon("a") }, new[] { Harmless("b") });
            Play(r, s, null, () => s.Ticks >= 1);
            Assert.That(s.Units[0].Hp, Is.EqualTo(s.Units[0].MaxHp));
            Assert.That(s.Units[0].Shield, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ pyre_mercy

        [Test]
        public void Mercy_OwnHealsBurnTheEnemyInstead()
        {
            var r = Plain;
            var a = Mon("a", x => { x.Spd = 900; x.Str = 0; }, RuleModifiers.PyreMercy);
            a.Skills.Add(SelfHeal(1));
            var s = BattleSim.Start(r, new[] { a }, new[] { Slow("b") });
            var u = s.Units[0];
            var b = s.Units[1];
            u.Hp = 0.2 * u.MaxHp;
            double hp = u.Hp;
            var log = Play(r, s, null, () => u.Tally.Actions >= 1);
            Assert.That(u.Hp, Is.EqualTo(hp), "no heal lands");
            Assert.That(u.Tally.HealingDone, Is.EqualTo(0));
            var burn = log.Single(x => x.Event == BattleSim.MercyEvent);
            Assert.That(burn.Target, Is.EqualTo("b"));
            double raw = r.MercyMult * 300 * 1.0;   // MON_MERCY_MULT x WIS x power
            Assert.That(burn.Amount, Is.EqualTo(BattleSim.Mitigate(r, raw * r.ElementMult(RuleModifiers.FireElement, null), 300)).Within(1e-6));
            Assert.That(b.Tally.DamageTaken, Is.EqualTo(burn.Amount).Within(1e-9));
            Assert.That(u.Tally.DamageByElement[RuleModifiers.FireElement], Is.EqualTo(burn.Amount).Within(1e-9), "counts as fire damage");
            Assert.That(u.MercyPending, Is.EqualTo(0));

            // off: the heal lands
            a = Mon("a", x => { x.Spd = 900; x.Str = 0; });
            a.Skills.Add(SelfHeal(1));
            s = BattleSim.Start(r, new[] { a }, new[] { Slow("b") });
            s.Units[0].Hp = 0.2 * s.Units[0].MaxHp;
            log = Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
            Assert.That(s.Units[0].Tally.HealingDone, Is.GreaterThan(0));
            Assert.That(Events(log, RuleModifiers.PyreMercy), Is.EqualTo(0));
        }

        [Test]
        public void Mercy_ShieldsStillWork_AndTheTitheScalesTheBurn()
        {
            var r = Plain;
            r.TitheCost = 0;
            var a = Mon("a", x => { x.Spd = 900; x.Str = 0; }, RuleModifiers.PyreMercy, RuleModifiers.PyreTithe, RuleModifiers.AshShroud);
            a.Skills.Add(SelfHeal(1));
            var s = BattleSim.Start(r, new[] { a }, new[] { Slow("b") });
            var u = s.Units[0];
            var log = Play(r, s, null, () => s.Ticks >= 1);
            Assert.That(u.Shield, Is.GreaterThan(0), "a shield is not a heal");
            u.Hp = 0.5 * u.MaxHp;
            log = Play(r, s, null, () => u.Tally.Actions >= 1);
            double raw = r.MercyMult * 300 * r.ElementMult(RuleModifiers.FireElement, null) * 1.5;
            Assert.That(log.Single(x => x.Event == BattleSim.MercyEvent).Amount, Is.EqualTo(BattleSim.Mitigate(r, raw, 300)).Within(1e-6));
        }

        [Test]
        public void Mercy_TeamHeals_BurnARandomEnemy_WithOneDraw()
        {
            var r = Plain;
            var healer = Mon("h", x => { x.Spd = 900; x.Str = 0; });
            healer.Skills.Add(new CombatSkill { Id = "test_mend", Kind = SkillKind.Heal, Target = SkillTarget.AllAllies, Scales = StatIds.Wis, Power = 1 });
            var stag = Mon("p", x => { x.Spd = 10; x.Hp = 99999; }, RuleModifiers.PyreMercy);
            var s = BattleSim.Start(r, new[] { healer, stag }, new[] { Slow("b1"), Slow("b2") });
            var p = s.Units[1];
            p.Hp = 0.1 * p.MaxHp;
            double hp = p.Hp;
            var rng = new Counting();
            s.Units[0].Hp = 0.1 * s.Units[0].MaxHp;
            var log = Play(r, s, rng, () => s.Units[0].Tally.Actions >= 1);
            Assert.That(p.Hp, Is.EqualTo(hp), "the ally's heal does not land on it");
            Assert.That(s.Units[0].Hp, Is.GreaterThan(0.1 * s.Units[0].MaxHp), "the healer still heals itself");
            var burn = log.Single(x => x.Event == BattleSim.MercyEvent);
            Assert.That(burn.Actor, Is.EqualTo("p"));
            Assert.That(burn.Target, Is.EqualTo("b2"), "draw 0.99 picks the last enemy");
            Assert.That(rng.Draws, Is.EqualTo(1), "one draw: the burn's target (the heal draws nothing)");
        }

        // ------------------------------------------------------------------ ember_eternal

        [Test]
        public void Ember_ABlowThatWouldKillBurnsMaxHpInstead()
        {
            var r = Plain;
            var a = Mon("a", x => { x.Spd = 10; }, RuleModifiers.EmberEternal);
            var s = BattleSim.Start(r, new[] { a }, new[] { Mon("b", x => x.Spd = 900) });
            var u = s.Units[0];
            double max = u.MaxHp;
            u.Hp = 10;
            var log = Play(r, s, null, () => s.Units[1].Tally.Actions >= 1);
            var ev = log.Single(x => x.Event == BattleSim.EmberEvent);
            Assert.That(u.Alive);
            Assert.That(u.Hp, Is.EqualTo(1));
            Assert.That(u.Tally.DamageTaken, Is.EqualTo(10).Within(1e-9), "the blow took the HP it had");
            // the blow's whole damage, with the same rules on a unit with HP to spare
            var s2 = BattleSim.Start(r, new[] { Mon("a", x => x.Spd = 10) }, new[] { Mon("b", x => x.Spd = 900) });
            Play(r, s2, null, () => s2.Units[1].Tally.Actions >= 1);
            double blow = s2.Units[0].Tally.DamageTaken;
            Assert.That(ev.Amount, Is.EqualTo(blow - 10 + 1).Within(1e-6), "the overflow past 1 HP");
            Assert.That(u.MaxHp, Is.EqualTo(max - ev.Amount).Within(1e-6));
            Assert.That(u.EmberBurned, Is.EqualTo(ev.Amount).Within(1e-9));
            Assert.That(u.BaseMaxHp, Is.EqualTo(u.MaxHp).Within(1e-6), "no HP effects: base follows");

            // off: it dies
            s = BattleSim.Start(r, new[] { Mon("a", x => x.Spd = 10) }, new[] { Mon("b", x => x.Spd = 900) });
            s.Units[0].Hp = 10;
            Play(r, s, null, () => s.Units[1].Tally.Actions >= 1);
            Assert.That(s.Outcome, Is.EqualTo(FightOutcome.SideBWon));
        }

        [Test]
        public void Ember_BurnedMaxHpAddsDamage()
        {
            double Dealt(double burned)
            {
                var r = Plain;
                var s = BattleSim.Start(r, new[] { Fast("a", RuleModifiers.EmberEternal) }, new[] { Slow("b") });
                var u = s.Units[0];
                double start = u.MaxHp;
                u.EmberBurned = burned * start;
                u.MaxHp = start - u.EmberBurned;
                u.Hp = u.MaxHp;
                Play(r, s, null, () => u.Tally.Actions >= 1);
                return s.Units[1].Tally.DamageTaken;
            }
            double plain = Dealt(0);
            Assert.That(Dealt(0.2), Is.EqualTo(plain * (1 + Content.EmberDmg * 20)).Within(1e-6));
        }

        [Test]
        public void Ember_DiesOnceTheBurnCrossesTheFloor()
        {
            var r = Plain;
            var s = BattleSim.Start(r, new[] { Mon("a", x => x.Spd = 10, RuleModifiers.EmberEternal) }, new[] { Mon("b", x => x.Spd = 900) });
            var u = s.Units[0];
            double start = u.MaxHp;
            // already burned to just above the floor: the next lethal blow crosses it
            u.EmberBurned = (1 - r.EmberFloor) * start - 1;
            u.MaxHp = start - u.EmberBurned;
            u.BaseMaxHp = u.MaxHp;
            u.Hp = 1;
            var log = Play(r, s, null, () => s.Units[1].Tally.Actions >= 1);
            Assert.That(s.Outcome, Is.EqualTo(FightOutcome.SideBWon));
            Assert.That(Events(log, RuleModifiers.EmberEternal), Is.EqualTo(0));
        }

        [Test]
        public void Ember_CannotStall_AFightEndsBeforeTheTimeLimit()
        {
            var r = Content;
            var stag = Mon("a", x => { x.Str = 50; x.Def = 50; x.Wis = 50; }, RuleModifiers.EmberEternal, RuleModifiers.PyreTithe, RuleModifiers.AshShroud);
            for (ulong seed = 1; seed <= 10; seed++)
            {
                var res = BattleSim.Resolve(r, new[] { stag }, new[] { Mon("b") }, seed);
                Assert.That(res.Outcome, Is.EqualTo(FightOutcome.SideBWon), "seed " + seed);
            }
        }

        [Test]
        public void Ember_ComesAfterDeathsDoor()
        {
            var r = Plain;
            var s = BattleSim.Start(r, new[] { Mon("a", x => x.Spd = 10, RuleModifiers.DeathsDoor, RuleModifiers.EmberEternal) }, new[] { Mon("b", x => x.Spd = 900) });
            var u = s.Units[0];
            double max = u.MaxHp;
            u.Hp = 5;
            var log = Play(r, s, null, () => s.Units[1].Tally.Actions >= 1);
            Assert.That(u.OnDoor, "the free Death's Door save comes first");
            Assert.That(u.MaxHp, Is.EqualTo(max));
            Assert.That(u.EmberOverflow, Is.EqualTo(0), "the noted overflow is cleared");
            Assert.That(Events(log, RuleModifiers.EmberEternal), Is.EqualTo(0));
        }

        [Test]
        public void TheSpeciesCarriesItsRules_InItsTrees()
        {
            var db = TestContent.Db;
            var sp = db.Species["pyre_stag"];
            string Cap(string tree) => db.TreeNodes.Values.Single(n => n.TreeId == tree && n.Index == 7).RuleId;
            Assert.That(Cap(sp.NativeTree1), Is.EqualTo(RuleModifiers.PyreTithe));
            Assert.That(Cap(sp.NativeTree2), Is.EqualTo(RuleModifiers.AshShroud));
            Assert.That(Cap(sp.ChaseTreeRun), Is.EqualTo(RuleModifiers.PyreMercy));
            Assert.That(Cap(sp.ChaseTreeBook), Is.EqualTo(RuleModifiers.EmberEternal));
        }
    }
}
