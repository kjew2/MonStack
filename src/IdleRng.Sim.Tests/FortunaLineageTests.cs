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
            a.Vault.Add(new VaultEntry { Id = 1, SpeciesId = "slime", Trees = { new VaultTree { TreeId = "brute_force", Bought = 5 } } });
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
        public void BreedingChecksBeforeMutation_AndConsumesBothParents()
        {
            var a = Parents();
            Assert.Throws<ArgumentException>(() => FortunaBreeding.Breed(Db, a, 0, 0));
            Assert.Throws<InvalidOperationException>(() => FortunaBreeding.Breed(Db, a, 1, 0));
            Assert.That((a.RunsStarted, a.Shards, a.Active), Is.EqualTo((2, 10000L, (RunState)null)));
            var s = FortunaBreeding.Breed(Db, a, 0, 1);
            Assert.That(a.Vault, Is.Empty);
            Assert.That(a.Shards, Is.EqualTo(10000 - FortunaBreeding.Cost(Db)));
            Assert.That(s.Slots[2].TreeId, Is.EqualTo("fortune"));
            Assert.That(s.Slots[2].Bought, Is.Zero);
            Assert.That(s.Lineage.Rank, Is.EqualTo(1));
            Assert.Catch<Exception>(() => FortunaBreeding.Breed(Db, a, 0, 1));
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
            Play(s); var entry = AccountOps.FinishRun(Db, a).Entry;
            Assert.That(entry.Lineages["fortune"].Points, Is.EqualTo(20));
            Assert.That(entry.Lineages["fortune"].LastAward, Is.EqualTo(20));
            Assert.That(a.Fortuna.Points, Is.Zero, "sealed progress belongs to the monster, not the account reserve");
            Assert.Throws<InvalidOperationException>(() => AccountOps.FinishRun(Db, a));
            Assert.That(entry.Lineages["fortune"].Points, Is.EqualTo(20));
            a.Vault.Add(new VaultEntry { Id = 3, SpeciesId = "slime", Trees = { new VaultTree { TreeId = "brute_force", Bought = 5 } } });
            a.RunsStarted = 4;
            s = FortunaBreeding.Breed(Db, a, 0, 1); Play(s); entry = AccountOps.FinishRun(Db, a).Entry;
            Assert.That(entry.Lineages["fortune"].Points, Is.EqualTo(20), "completion, fights and grades add nothing");
        }
        static RunState Focus(FortunaLineage progress, int deepest) => new RunState
        {
            Lineage = new LineageRun { Rank = progress.Rank, Deepest = deepest,
                Progress = new SortedDictionary<string, FortunaLineage> { ["fortune"] = progress } }
        };
        [Test]
        public void UpgradesCarryOverflow_AndVIsGuaranteedByFifthAttempt()
        {
            var a = Parents();
            var progress = new FortunaLineage { Points = 39, Seed = 4 };
            FortunaBreeding.Award(Db, a, Focus(progress, 2));
            Assert.That((progress.Rank, progress.Points), Is.EqualTo((2, 2)));
            Assert.That(a.Fortuna.Points, Is.Zero);
            // Each family carries its own seeded attempt stream. A full focused tree guarantees V within the refill budget.
            for (ulong seed = 0; seed < 200; seed++)
            {
                var b = Parents(seed);
                var family = new FortunaLineage { Rank = 4, Points = 179, Seed = seed + 1 };
                var s = Focus(family, 8);
                for (int i = 0; i < 25 && family.Rank < 5; i++) FortunaBreeding.Award(Db, b, s);
                Assert.That(family.Rank, Is.EqualTo(5), "seed " + seed);
                Assert.That(family.Attempts, Is.InRange(1, 5));
                Assert.That(family.Failures, Is.Zero);
            }
        }
        [Test]
        public void InheritedRankChangesFillersAndCapstoneKit_AndVaultIsFrozen()
        {
            var a = Parents(); a.Vault[0].Lineages = new SortedDictionary<string, FortunaLineage> { ["fortune"] = new FortunaLineage { Rank = 5 } };
            var supporting = a.Vault[1];
            var s = FortunaBreeding.Breed(Db, a, 0, 1); s.Sp = 100;
            Assert.That(TrainingRun.Nodes(Db, s.Slots[2])[0].Amount, Is.EqualTo(0.11).Within(1e-9));
            for (int i = 0; i < 7; i++) TrainingRun.SpendPoint(Db, s, 2);
            Assert.That(TrainingRun.MonsterFighter(Db, s).Rules, Does.Not.Contain(RuleModifiers.FortuneBank));
            TrainingRun.SpendPoint(Db, s, 2);
            Assert.That(TrainingRun.MonsterFighter(Db, s).Rules, Does.Contain(RuleModifiers.FortuneBank));
            Play(s); var e = AccountOps.FinishRun(Db, a).Entry;
            e.Lineages["fortune"].Rank = 1;
            Assert.That(e.Fighter(Db).Rules, Does.Contain(RuleModifiers.FortuneBank));
            Assert.That(supporting.Fighter(Db).Rules, Does.Not.Contain(RuleModifiers.FortuneBank));
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
