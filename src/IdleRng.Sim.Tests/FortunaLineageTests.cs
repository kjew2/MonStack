using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Account;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    public class FortunaLineageTests
    {
        static ContentDb Db => TestContent.Db;
        static AccountState Parents(ulong seed = 4)
        {
            var a = AccountOps.New(Db, seed);
            a.Shards = 10000; a.RunsStarted = 2;
            a.Vault.Add(new VaultEntry { Id = 0, SpeciesId = "slime", Trees = { new VaultTree { TreeId = "fortune", Bought = 2 } } });
            a.Vault.Add(new VaultEntry { Id = 1, SpeciesId = "slime" });
            return a;
        }
        static void Play(RunState s)
        {
            while (s.Phase != RunPhase.Finished)
            {
                while (s.Offers.Count > 0) TrainingRun.DiscardOffer(Db, s);
                if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s); else TrainingRun.Rest(Db, s);
            }
        }
        [Test]
        public void BreedingChecksBeforeMutation_AndKeepsParents()
        {
            var a = Parents();
            Assert.Throws<ArgumentException>(() => FortunaBreeding.Breed(Db, a, 0, 0));
            Assert.Throws<InvalidOperationException>(() => FortunaBreeding.Breed(Db, a, 1, 0));
            Assert.That((a.RunsStarted, a.Shards, a.Active), Is.EqualTo((2, 10000L, (RunState)null)));
            var s = FortunaBreeding.Breed(Db, a, 0, 1);
            Assert.That(a.Vault.Count, Is.EqualTo(2));
            Assert.That(a.Shards, Is.EqualTo(10000 - FortunaBreeding.Cost(Db)));
            Assert.That(s.Slots[2].TreeId, Is.EqualTo("fortune"));
            Assert.That(s.Slots[2].Bought, Is.Zero);
            Assert.That(s.Lineage.Rank, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => FortunaBreeding.Breed(Db, a, 0, 1));
            Assert.That(a.Shards, Is.EqualTo(10000 - FortunaBreeding.Cost(Db)));
        }
        [Test]
        public void PointsOnlyFromNodes_HighWaterSurvivesOverwriteAndClone_SealPaysOnce()
        {
            var a = Parents(); var s = FortunaBreeding.Breed(Db, a, 0, 1);
            s.Sp = 100;
            for (int i = 0; i < 8; i++) TrainingRun.SpendPoint(Db, s, 2);
            Assert.That(FortunaBreeding.PointsFor(Db, 7), Is.EqualTo(8));
            Assert.That(FortunaBreeding.PointsFor(Db, 8), Is.EqualTo(20));
            var clone = s.Clone(); clone.Lineage.Deepest = 1;
            Assert.That(s.Lineage.Deepest, Is.EqualTo(8));
            s.Offers.Insert(0, new TreeOffer { Trees = { "vital_core" } });
            TrainingRun.TakeTree(Db, s, 0, 2);
            s.Offers.Insert(0, new TreeOffer { Trees = { "fortune" } });
            TrainingRun.TakeTree(Db, s, 0, 2);
            TrainingRun.SpendPoint(Db, s, 2);
            Play(s); AccountOps.FinishRun(Db, a);
            Assert.That(a.Fortuna.Points, Is.EqualTo(20));
            Assert.That(a.Fortuna.LastAward, Is.EqualTo(20));
            Assert.Throws<InvalidOperationException>(() => AccountOps.FinishRun(Db, a));
            Assert.That(a.Fortuna.Points, Is.EqualTo(20));
            s = FortunaBreeding.Breed(Db, a, 0, 1); Play(s); AccountOps.FinishRun(Db, a);
            Assert.That(a.Fortuna.Points, Is.EqualTo(20), "completion, fights and grades add nothing");
        }
        [Test]
        public void UpgradesCarryOverflow_AndVIsGuaranteedByFifthAttempt()
        {
            var a = Parents();
            a.Fortuna.Points = 39;
            FortunaBreeding.Award(Db, a, new RunState { Lineage = new LineageRun { Deepest = 2 } });
            Assert.That((a.Fortuna.Rank, a.Fortuna.Points), Is.EqualTo((2, 2)));
            // Every seed must finish within its bounded retry budget; outcomes are deterministic.
            for (ulong seed = 0; seed < 200; seed++)
            {
                var b = Parents(seed); b.Fortuna.Rank = 4; b.Fortuna.Points = 179;
                var s = new RunState { Lineage = new LineageRun { Deepest = 8 } };
                FortunaBreeding.Award(Db, b, s);
                while (b.Fortuna.Rank < 5) FortunaBreeding.Award(Db, b, s);
                Assert.That(b.Fortuna.Attempts, Is.InRange(1, 5));
                Assert.That(b.Fortuna.Failures, Is.Zero);
            }
        }
        [Test]
        public void InheritedRankChangesFillersAndCapstoneKit_AndVaultIsFrozen()
        {
            var a = Parents(); a.Fortuna.Rank = 5;
            var s = FortunaBreeding.Breed(Db, a, 0, 1); s.Sp = 100;
            Assert.That(TrainingRun.Nodes(Db, s.Slots[2])[0].Amount, Is.EqualTo(0.11).Within(1e-9));
            for (int i = 0; i < 7; i++) TrainingRun.SpendPoint(Db, s, 2);
            Assert.That(TrainingRun.MonsterFighter(Db, s).Rules, Does.Not.Contain(RuleModifiers.FortuneBank));
            TrainingRun.SpendPoint(Db, s, 2);
            Assert.That(TrainingRun.MonsterFighter(Db, s).Rules, Does.Contain(RuleModifiers.FortuneBank));
            Play(s); var e = AccountOps.FinishRun(Db, a).Entry;
            a.Fortuna.Rank = 1;
            Assert.That(e.Fighter(Db).Rules, Does.Contain(RuleModifiers.FortuneBank));
            Assert.That(a.Vault[0].Fighter(Db).Rules, Does.Not.Contain(RuleModifiers.FortuneBank));
        }
        [TestCase(1, 1)] [TestCase(4, 1)] [TestCase(4, 4)]
        public void FortuneBankProcsAndResets_IsBoundedAndDeterministic(int attackers, int defenders)
        {
            var r = FightRules.FromContent(Db); r.CritBase = 0.5; r.CritPerLuk = 0; r.EvadeBase = 0; r.EvadePerDex = 0;
            var f = new Fighter { Id = "bank", Element = "fire", Stats = new MonsterStats { Hp = 300, Str = 150, Spd = 100 }, Rules = { RuleModifiers.FortuneBank } };
            var other = new Fighter { Id = "enemy", Element = "fire", Stats = new MonsterStats { Hp = 300, Str = 150, Spd = 100 } };
            var sideA = Enumerable.Range(0, attackers).Select(i => f.WithId("a" + i)).ToList();
            var sideB = Enumerable.Range(0, defenders).Select(i => other.WithId("b" + i)).ToList();
            var s = BattleSim.Start(r, sideA, sideB);
            var log = new List<FightAction>(); var rng = new RngStream("test", 12);
            while (s.Outcome == FightOutcome.Ongoing) BattleSim.Step(r, s, rng, log.Add);
            var events = log.Where(x => x.Event == "rule:fortune_bank").ToList();
            Assert.That(events, Is.Not.Empty);
            Assert.That(events.Any(x => x.Amount == 0), Is.True, "successful crit resets");
            Assert.That(events.Any(x => x.Amount > 0), Is.True, "failed crit banks");
            Assert.That(events.All(x => x.Amount >= 0 && x.Amount <= r.FortuneBankCap), Is.True);
            Assert.That(s.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut));
        }
    }
}
