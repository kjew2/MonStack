using System;
using System.Collections.Generic;
using System.Reflection;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    public class LineageAdvancedTests
    {
        static FightRules Rules => FightRules.FromContent(TestContent.Db);
        static Fighter Mon(string id, params string[] rules)
        {
            var f = new Fighter { Id = id, Element = "fire", Stats = new MonsterStats { Hp = 400, Str = 300, Int = 300, Wis = 300, Dex = 300, Def = 300, Spd = 300, Luk = 300 } };
            f.Rules.AddRange(rules); return f;
        }
        static void Hook(string name, params object[] args) => typeof(BattleSim).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        static (FightRules r, FightState s, RuleCtx c, FightUnit u, FightUnit t) Setup(string tree, bool x = false, bool ally = false)
        {
            var r = Rules;
            var f = Mon("a", tree, "lineage_" + tree + "_v");
            if (x) f.Rules.Add("lineage_" + tree + "_x");
            var s = BattleSim.Start(r, ally ? new[] { f, Mon("ally") } : new[] { f }, new[] { Mon("enemy"), Mon("other") });
            return (r, s, new RuleCtx(r, s, null, null), s.Units[0], s.Units[ally ? 2 : 1]);
        }

        sealed class Fixed : IRandomSource { public double NextDouble() => .99; }

        [TestCase("iron_reflexes")] [TestCase("resentment")] [TestCase("deaths_door")]
        [TestCase("one_more")] [TestCase("latent_power")] [TestCase("lone_wolf")]
        [TestCase("arise")] [TestCase("last_rite")] [TestCase("hemorrhage")]
        [TestCase("privileged_status")] [TestCase("equilibrium")] [TestCase("corruption")] [TestCase("dead_branch")]
        public void IntegratedFight_ClonedMilestoneStateReplaysDeterministically(string tree)
        {
            var z = Setup(tree, true, true);
            for (int i = 0; i < 80 && z.s.Outcome == FightOutcome.Ongoing; i++) BattleSim.Step(z.r, z.s, new Fixed());
            var copy = z.s.Clone();
            Assert.That(BattleSim.StateHash(copy), Is.EqualTo(BattleSim.StateHash(z.s)));
            for (int i = 0; i < 80 && z.s.Outcome == FightOutcome.Ongoing; i++)
            {
                BattleSim.Step(z.r, z.s, new Fixed()); BattleSim.Step(z.r, copy, new Fixed());
                Assert.That(BattleSim.StateHash(copy), Is.EqualTo(BattleSim.StateHash(z.s)), tree + " replay step " + i);
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void Iron_PhysicalPressureIsBounded_AndOnlyXSpendsIt(bool x)
        {
            var z = Setup("iron_reflexes", x);
            var incoming = new HitInfo { Src = z.t, Tgt = z.u, Raw = 100 };
            for (int i = 0; i < 100; i++) Hook("LineageAdvancedIncoming", z.c, incoming);
            Assert.That(z.u.LineageIronPressure, Is.LessThanOrEqualTo(z.r.LineageCap * z.u.MaxHp));
            var outgoing = new HitInfo { Src = z.u, Tgt = z.t, Raw = 100 };
            Hook("LineageAdvancedOutgoing", z.c, outgoing);
            Assert.That(outgoing.Raw, x ? Is.GreaterThan(100) : Is.EqualTo(100));
            Assert.That(z.u.LineageIronPressure, x ? Is.Zero : Is.GreaterThan(0));
        }

        [Test]
        public void Resentment_ReserveSurvivesDecay_AndRevengeSpendsStrBenefit()
        {
            var z = Setup("resentment", true); z.u.ResentPool = 100;
            Hook("LineageAdvancedAfterAction", z.c, z.u);
            Assert.That(z.u.LineageRedReserve, Is.GreaterThan(0));
            var hit = new HitInfo { Src = z.u, Tgt = z.t, Raw = 100 };
            Hook("LineageAdvancedOutgoing", z.c, hit);
            Assert.That(hit.Raw, Is.GreaterThan(100)); Assert.That(z.u.ResentPool, Is.LessThan(100)); Assert.That(z.u.LineageRedReserve, Is.Zero);
        }

        [Test]
        public void Door_RecoveryDoesNotRefreshOnLaterSaves_AndHealthyCarrierLosesRetaliation()
        {
            var z = Setup("deaths_door", true); z.u.Hp = 1;
            Hook("LineageAdvancedWouldDie", z.c, z.t, z.u, true);
            Assert.That(z.u.LineageRecovery, Is.EqualTo(z.r.LineageWindow));
            Hook("LineageAdvancedBeforeAction", z.c, z.u, false);
            Assert.That(z.u.Hp, Is.GreaterThan(1));
            Hook("LineageAdvancedWouldDie", z.c, z.t, z.u, true);
            Assert.That(z.u.LineageRecovery, Is.EqualTo(z.r.LineageWindow - 1));
            z.u.Hp = z.u.MaxHp; Hook("LineageAdvancedBeforeAction", z.c, z.u, false);
            Assert.That(z.u.LineageRetaliation, Is.False);
        }

        [TestCase(false)] [TestCase(true)]
        public void OneMore_BankHasCap_ThenMovesThroughOrdinaryGauge(bool x)
        {
            var z = Setup("one_more", x, true); z.u.Gauge = z.r.GaugeSize;
            Hook("LineageAdvancedLanded", z.c, new HitInfo { Src = z.u, Tgt = z.t, Crit = true });
            Assert.That(z.u.LineageRefundBank, Is.GreaterThan(0).And.LessThanOrEqualTo(z.r.LineageCap * z.r.GaugeSize));
            z.u.Gauge = 0; z.s.Units[1].Gauge = 0;
            Hook("LineageAdvancedBeforeAction", z.c, z.u, false);
            Assert.That(x ? z.s.Units[1].Gauge : z.u.Gauge, Is.GreaterThan(0)); Assert.That(z.u.LineageRefundBank, Is.Zero);
        }

        [Test]
        public void Latent_CooldownAdvanceOccursOnlyOnce()
        {
            var z = Setup("latent_power", true); z.u.Tally.Actions = z.r.LatentActions; z.u.Cooldowns = new[] { 10 };
            Hook("LineageAdvancedAfterAction", z.c, z.u); int after = z.u.Cooldowns[0];
            Hook("LineageAdvancedAfterAction", z.c, z.u);
            Assert.That(after, Is.EqualTo(10 - z.r.LineageWindow)); Assert.That(z.u.Cooldowns[0], Is.EqualTo(after));
        }

        [TestCase(false)] [TestCase(true)]
        public void LoneWolf_DuelIdentityInTeamRequiresX(bool x)
        {
            var z = Setup("lone_wolf", x, true);
            for (int i = 0; i < 20; i++) Hook("LineageAdvancedBeforeAction", z.c, z.u, false);
            Assert.That(z.u.LineageDuelCharge, x ? Is.EqualTo(z.r.LineageCap) : Is.Zero);
        }

        [Test]
        public void LastRite_AliveReleaseIsOnce_AndLeavesReserveForDeath()
        {
            var z = Setup("last_rite", true, true);
            for (int i = 0; i < 20; i++) Hook("LineageAdvancedAfterAction", z.c, z.u);
            double shield = z.s.Units[1].Shield; Assert.That(shield, Is.GreaterThan(0));
            Hook("LineageAdvancedAfterAction", z.c, z.u); Assert.That(z.s.Units[1].Shield, Is.EqualTo(shield));
            z.u.Hp = 0; Hook("LineageAdvancedDeath", z.c, z.u);
            Assert.That(z.s.Units[1].Shield, Is.GreaterThan(shield)); Assert.That(z.u.LineageLegacy, Is.Zero);
        }

        [Test]
        public void Hemorrhage_XKeepsVOverflow_AndSeedsOnlyOneOtherTarget()
        {
            var z = Setup("hemorrhage", true); z.t.Bleed = BattleSim.BleedGauge;
            var h = new HitInfo { Src = z.u, Tgt = z.t };
            Hook("LineageAdvancedLanded", z.c, h); z.t.Bleed = 0; // original burst consumed gauge
            Hook("LineageAdvancedLandedAfter", z.c, h);
            Assert.That(z.t.Bleed, Is.GreaterThan(0).And.LessThan(BattleSim.BleedGauge));
            Assert.That(z.s.Units[2].Bleed, Is.GreaterThan(0).And.LessThan(BattleSim.BleedGauge));
            Assert.That(z.u.LineageBleedOverflow, Is.Zero);
        }

        [Test]
        public void Privilege_CashOutConsumesOneDebuff()
        {
            var z = Setup("privileged_status", true);
            z.t.Effects.Add(new FightEffect { Stat = StatIds.Str, Frac = -z.r.LineageGain, Source = "test", TicksLeft = 100 });
            z.u.LineagePrivilege = z.r.LineageCap;
            var h = new HitInfo { Src = z.u, Tgt = z.t, Raw = 100 };
            Hook("LineageAdvancedOutgoing", z.c, h);
            Assert.That(h.Raw, Is.GreaterThan(100)); Assert.That(z.t.Effects.Count, Is.Zero); Assert.That(z.u.LineagePrivilege, Is.Zero);
        }

        [Test]
        public void Equilibrium_RequiresDifferentElements_AndDischargesSequence()
        {
            var z = Setup("equilibrium", true);
            for (int i = 0; i <= z.r.LineageWindow; i++)
            {
                var h = new HitInfo { Src = z.u, Tgt = z.t, Element = i % 2 == 0 ? "fire" : "water", Raw = 100 };
                Hook("LineageAdvancedOutgoing", z.c, h);
                if (i == z.r.LineageWindow) Assert.That(h.Raw, Is.GreaterThan(100));
            }
            Assert.That(z.u.LineageResonance, Is.Zero);
        }

        [Test]
        public void Corruption_RestoresOnlyOneSkill_WithRecoveryDelay()
        {
            var z = Setup("corruption", true); z.u.Locked = new List<int> { 0, 1 }; z.u.LineageLastLocks = z.r.LineageWindow; z.u.Cooldowns = new[] { 0, 0 };
            Hook("LineageAdvancedBeforeAction", z.c, z.u, false);
            Assert.That(z.u.SkillLocked(0), Is.False); Assert.That(z.u.Cooldowns[0], Is.EqualTo(z.r.LineageWindow));
            Hook("LineageAdvancedBeforeAction", z.c, z.u, false); Assert.That(z.u.SkillLocked(1), Is.True);
        }

        [Test]
        public void DeadBranch_RoleControlDoesNotMutateSkills_AndSignatureResistsOneLock()
        {
            var z = Setup("dead_branch", true);
            var damage = new CombatSkill { Id = "damage", Kind = SkillKind.Damage };
            var heal = new CombatSkill { Id = "heal", Kind = SkillKind.Heal };
            z.u.Source.Skills.Add(damage);
            var options = new List<CombatSkill> { damage, heal }; Hook("LineageAdvancedBranchOptions", z.c, z.u, options);
            Assert.That(options, Is.EqualTo(new[] { heal })); Assert.That(z.u.Source.Skills.Count, Is.EqualTo(1));
            z.u.Source.Skills.Add(heal); z.u.LineageSignature = heal.Id;
            var method = typeof(BattleSim).GetMethod("LineageAdvancedKeepSignature", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method.Invoke(null, new object[] { z.c, z.u, 1 }), Is.True);
            Assert.That(method.Invoke(null, new object[] { z.c, z.u, 1 }), Is.False);
        }

        [Test]
        public void Arise_SacrificeIsOnce_AndNeverConsumesUnownedRisenUnit()
        {
            var z = Setup("arise", true, true); var risen = z.s.Units[1]; risen.Risen = true;
            Hook("LineageAdvancedBeforeAction", z.c, z.u, false); Assert.That(risen.Alive, Is.True);
            risen.LineageDuelTarget = z.u.Id; Hook("LineageAdvancedBeforeAction", z.c, z.u, false);
            Assert.That(risen.Alive, Is.False); Assert.That(z.u.LineageSacrificed, Is.True);
        }
    }
}
