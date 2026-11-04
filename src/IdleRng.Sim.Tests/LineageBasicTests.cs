using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    public class LineageBasicTests
    {
        sealed class Fixed : IRandomSource { public double NextDouble() => .99; }
        static FightRules Rules()
        {
            var r = FightRules.FromContent(TestContent.Db);
            r.EvadeBase = 0; r.EvadePerDex = 0; r.CritBase = 0; r.CritPerLuk = 0;
            return r;
        }
        static Fighter Mon(string id, params string[] rules)
        {
            var u = new Fighter { Id = id, Stats = new MonsterStats { Hp = 10000, Str = 100, Int = 0, Wis = 100, Def = 100, Dex = 0, Luk = 0, Spd = 100 } };
            u.Rules.AddRange(rules); return u;
        }
        static List<FightAction> Act(FightRules r, FightState s, int actions = 1)
        {
            int end = s.Units[0].Tally.Actions + actions;
            var log = new List<FightAction>();
            for (int i = 0; i < 10000 && s.Units[0].Tally.Actions < end && s.Outcome == FightOutcome.Ongoing; i++)
            { s.Units[0].Gauge = r.GaugeSize; BattleSim.Step(r, s, new Fixed(), log.Add); }
            Assert.That(s.Units[0].Tally.Actions, Is.GreaterThanOrEqualTo(end)); return log;
        }
        static CombatSkill Damage(string id, string scales) => new CombatSkill { Id = id, Kind = SkillKind.Damage, Scales = scales, Target = SkillTarget.Enemy, Power = 1, Cooldown = 0 };

        [TestCase(1)] [TestCase(4)]
        public void HeavyRhythm_CyclesAtCap_AndDemolitionOnlyAppearsAtX(int enemies)
        {
            var r = Rules();
            var targets = Enumerable.Range(0, enemies).Select(i => Mon("b" + i)).ToArray();
            var s = BattleSim.Start(r, new[] { Mon("a", "lineage_brute_force_v", "lineage_brute_force_x") }, targets);
            foreach (var t in s.Units.Skip(1)) t.Shield = 10000;
            var log = Act(r, s, 5);
            Assert.That(log.Any(x => x.Event == "rule:lineage_brute_force_v" && x.Amount > 0), Is.True);
            Assert.That(log.Any(x => x.Event == "rule:lineage_brute_force_x" && x.Amount > 0), Is.True);
            Assert.That(s.Units[0].LBHeavy, Is.InRange(0, r.LineageCap));
        }
        [Test]
        public void SpellWeaving_RewardsAlternatingDamageSpells()
        {
            var r = Rules(); var a = Mon("a", "lineage_spellcraft_v", "lineage_spellcraft_x");
            a.Stats.Int = 200; a.Skills.Add(Damage("new-spell", StatIds.Int));
            var s = BattleSim.Start(r, new[] { a }, new[] { Mon("b") });
            s.Units[0].LBArcane = r.LineageGain; s.Units[0].LBLastSpell = "old-spell";
            var log = Act(r, s);
            Assert.That(log.Any(x => x.Event == "rule:lineage_spellcraft_x" && x.Amount > r.LineageGain), Is.True);
        }
        [TestCase(false)] [TestCase(true)]
        public void HeartReserve_PaysIntoSoloOrInjuredAlly_WithoutRefillingItself(bool team)
        {
            var r = Rules(); var allies = new List<Fighter> { Mon("a", "lineage_vital_core_v", "lineage_vital_core_x") };
            if (team) allies.Add(Mon("friend"));
            var s = BattleSim.Start(r, allies, new[] { Mon("b") }); var carrier = s.Units[0];
            var target = team ? s.Units[1] : carrier;
            target.Hp *= .5; double before = target.Hp;
            carrier.LBHeart = carrier.MaxHp * r.LineageGain;
            Act(r, s);
            Assert.That(target.Hp, Is.GreaterThan(before)); Assert.That(carrier.LBHeart, Is.Zero);
        }
        [Test]
        public void SharedFortune_GrantsGiftToAllyWithoutLineage_AndHashesPendingGift()
        {
            var r = Rules(); var s = BattleSim.Start(r,
                new[] { Mon("a", "fortune_bank", "lineage_fortune_x"), Mon("friend") }, new[] { Mon("b") });
            s.Units[0].FortuneBank = r.FortuneBankCap;
            Act(r, s); Assert.That(s.Units[0].LBFortuneTarget, Is.EqualTo("friend"));
            string hash = BattleSim.StateHash(s); var clone = s.Clone(); clone.Units[0].LBFortuneTarget = null;
            Assert.That(BattleSim.StateHash(clone), Is.Not.EqualTo(hash));
            s.Units[1].Gauge = r.GaugeSize * 10;
            var log = new List<FightAction>(); BattleSim.Step(r, s, new Fixed(), log.Add);
            Assert.That(s.Units[0].LBFortuneTarget, Is.Null);
            Assert.That(log.Any(x => x.Event == "rule:lineage_fortune_x"), Is.True);
        }
        [TestCase(false)] [TestCase(true)]
        public void SharedFortune_KeepsPersonalBankWhenNoAllyCanBenefit(bool cappedAlly)
        {
            var r = Rules(); r.CritBase = r.CritCap; r.CritPerLuk = 0;
            var owner = Mon("a", "fortune_bank", "lineage_fortune_x");
            var s = BattleSim.Start(r, cappedAlly ? new[] { owner, Mon("friend") } : new[] { owner }, new[] { Mon("b") });
            s.Units[0].FortuneBank = r.FortuneBankCap;
            Act(r, s);
            Assert.That(s.Units[0].FortuneBank, Is.EqualTo(r.FortuneBankCap));
            Assert.That(s.Units[0].LBFortuneTarget, Is.Null);
        }
        [Test]
        public void SanctuaryAndBastion_ChargeFromShieldedAllyWithoutLineage()
        {
            var r = Rules(); var s = BattleSim.Start(r,
                new[] { Mon("a", "lineage_sage_mind_v", "lineage_sage_mind_x", "lineage_bulwark_v", "lineage_bulwark_x"), Mon("friend") }, new[] { Mon("b") });
            // Fixed random targeting selects the last ally, which carries no lineage rules.
            s.Units[1].Hp *= .5; s.Units[1].Shield = 10000;
            for (int i = 0; i < 100 && s.Units[0].LBSage == 0; i++)
            { s.Units[2].Gauge = r.GaugeSize * 10; BattleSim.Step(r, s, new Fixed()); }
            Assert.That(s.Units[0].LBSage, Is.GreaterThan(0).And.LessThanOrEqualTo(r.LineageCap));
            Assert.That(s.Units[0].LBBulwark, Is.GreaterThan(0).And.LessThanOrEqualTo(r.LineageCap));
        }
        [Test]
        public void NonCarrierStateAndLineageTuning_DoNotChangeFightOrHash()
        {
            var r = Rules(); var s = BattleSim.Start(r, new[] { Mon("a") }, new[] { Mon("b") }); var clone = s.Clone();
            clone.Units[0].LBHeart = 100; clone.Units[0].LBHeavy = .3; clone.Units[0].LBFury = .3;
            clone.Units[0].LBOpeningTarget = "a"; clone.Units[0].LBOpeningWindow = 2;
            Assert.That(BattleSim.StateHash(clone), Is.EqualTo(BattleSim.StateHash(s)));
            var r2 = Rules(); r2.LineageGain = .05; r2.LineageCap = .2; r2.LineageWindow = 3;
            Act(r, s, 6); Act(r2, clone, 6);
            Assert.That(BattleSim.StateHash(clone), Is.EqualTo(BattleSim.StateHash(s)));
        }
        [Test]
        public void SurvivorAndBerserker_PoolsRemainBounded_AndCloneReplays()
        {
            var r = Rules(); var s = BattleSim.Start(r,
                new[] { Mon("a", "lineage_survivor_v", "lineage_survivor_x", "lineage_berserker_v", "lineage_berserker_x", "lineage_stonewall_v", "lineage_stonewall_x") }, new[] { Mon("b") });
            Act(r, s, 10); var clone = s.Clone();
            Act(r, s, 5); Act(r, clone, 5);
            Assert.That(BattleSim.StateHash(clone), Is.EqualTo(BattleSim.StateHash(s)));
            Assert.That(s.Units[0].LBFury, Is.InRange(0, r.LineageCap));
            Assert.That(s.Units[0].LBRecovery, Is.InRange(0, s.Units[0].MaxHp * r.LineageCap));
            Assert.That(s.Units[0].LBStone, Is.InRange(0, r.LineageCap));
        }
    }
}
