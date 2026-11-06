using System;
using System.Collections.Generic;
using IdleRng.Sim.Account;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Rng;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    public class FortunaReviewTests
    {
        static FightState Battle(bool carrier)
        {
            var stats = new MonsterStats { Hp = 400, Str = 80, Def = 50, Wis = 50, Spd = 100, Luk = 10, Dex = 100 };
            var a = new Fighter { Id = "a", Element = "fire", Stats = stats };
            if (carrier) a.Rules.Add(RuleModifiers.FortuneBank);
            return BattleSim.Start(FightRules.FromContent(TestContent.Db), new[] { a }, new[] { a.WithId("b") });
        }

        [Test]
        public void BankState_IsHashedForCarriers_AndCloneIsIndependent()
        {
            var s = Battle(true);
            var hash = BattleSim.StateHash(s);
            s.Units[0].FortuneBank = 0.2;
            Assert.That(BattleSim.StateHash(s), Is.Not.EqualTo(hash));
            var clone = s.Clone();
            Assert.That(BattleSim.StateHash(clone), Is.EqualTo(BattleSim.StateHash(s)));
            clone.Units[0].FortuneBank = 0.4;
            Assert.That(s.Units[0].FortuneBank, Is.EqualTo(0.2));
        }

        [Test]
        public void NoRule_IgnoresBankState_InHashAndRngConsumption()
        {
            var s = Battle(false);
            var clone = s.Clone();
            clone.Units[0].FortuneBank = 0.4;
            Assert.That(BattleSim.StateHash(clone), Is.EqualTo(BattleSim.StateHash(s)));
            var a = new RngStream("test", 123);
            var b = a.Clone();
            var rules = FightRules.FromContent(TestContent.Db);
            while (s.Outcome == FightOutcome.Ongoing)
            {
                BattleSim.Step(rules, s, a);
                BattleSim.Step(rules, clone, b);
                Assert.That(b.Counter, Is.EqualTo(a.Counter));
                Assert.That(BattleSim.StateHash(clone), Is.EqualTo(BattleSim.StateHash(s)));
            }
        }

        [Test]
        public void CarrierClone_ContinuesWithIdenticalHashesAndRandomDraws()
        {
            var s = Battle(true);
            var rules = FightRules.FromContent(TestContent.Db);
            var rng = new RngStream("test", 88);
            for (int i = 0; i < 25; i++) BattleSim.Step(rules, s, rng);
            var clone = s.Clone();
            var clonedRng = rng.Clone();
            while (s.Outcome == FightOutcome.Ongoing)
            {
                BattleSim.Step(rules, s, rng);
                BattleSim.Step(rules, clone, clonedRng);
                Assert.That(clonedRng.Counter, Is.EqualTo(rng.Counter));
                Assert.That(BattleSim.StateHash(clone), Is.EqualTo(BattleSim.StateHash(s)));
            }
        }

        [Test]
        public void LineageProgressAndBirthRank_AffectRunHash_AndCloneSeparatesHighWater()
        {
            var s = TrainingRun.Start(TestContent.Db, "slime", 8);
            var baseHash = TrainingRun.StateHash(s);
            s.Lineage = new LineageRun { Rank = 1, Primary = 0, Supporting = 1 };
            var birthHash = TrainingRun.StateHash(s);
            Assert.That(birthHash, Is.Not.EqualTo(baseHash));
            s.Lineage.Deepest = 3;
            Assert.That(TrainingRun.StateHash(s), Is.Not.EqualTo(birthHash));
            var clone = s.Clone();
            clone.Lineage.Rank = 2;
            clone.Lineage.Deepest = 8;
            Assert.That(s.Lineage.Rank, Is.EqualTo(1));
            Assert.That(s.Lineage.Deepest, Is.EqualTo(3));
            Assert.That(TrainingRun.StateHash(clone), Is.Not.EqualTo(TrainingRun.StateHash(s)));
        }
        [Test]
        public void PersonalProgressAndSupportingIdentityAreHashed_AndDeepCloned()
        {
            var s = TrainingRun.Start(TestContent.Db, "slime", 8);
            s.Lineage = new LineageRun { TreeId = "fortune", Rank = 4, Primary = 0, Supporting = 1, SupportingTreeId = "brute_force",
                Progress = new SortedDictionary<string, FortunaLineage> {
                    ["fortune"] = new FortunaLineage { Rank = 4, Points = 44, Failures = 1, Attempts = 2, Seed = 123 },
                    ["brute_force"] = new FortunaLineage { Rank = 2, Points = 3, Seed = 456 } } };
            string original = TrainingRun.StateHash(s);
            Assert.That(TrainingRun.StateHash(s.Clone()), Is.EqualTo(original));
            var changes = new (string Name, Action<RunState> Change)[] {
                ("focus progress", x => x.Lineage.Progress["fortune"].Points++),
                ("focus seed", x => x.Lineage.Progress["fortune"].Seed++),
                ("focus pity", x => x.Lineage.Progress["fortune"].Failures++),
                ("focus attempts", x => x.Lineage.Progress["fortune"].Attempts++),
                ("secondary rank", x => x.Lineage.Progress["brute_force"].Rank++),
                ("secondary progress", x => x.Lineage.Progress["brute_force"].Points++),
                ("secondary identity", x => x.Lineage.SupportingTreeId = "bulwark"),
            };
            foreach (var item in changes)
            {
                var clone = s.Clone(); item.Change(clone);
                Assert.That(TrainingRun.StateHash(clone), Is.Not.EqualTo(original), item.Name);
                Assert.That(TrainingRun.StateHash(s), Is.EqualTo(original), item.Name + " must not mutate its source clone");
            }
        }
    }
}
