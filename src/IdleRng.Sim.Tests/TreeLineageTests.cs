using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Account;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    public class TreeLineageTests
    {
        static ContentDb Db => TestContent.Db;
        static AccountState Parents(string tree)
        {
            var a = AccountOps.New(Db, 42); a.Shards = 10000; a.RunsStarted = 2;
            a.Vault.Add(new VaultEntry { Id = 0, SpeciesId = "slime", Trees = { new VaultTree { TreeId = tree, Bought = Db.Trees[tree].Nodes.Count } } });
            a.Vault.Add(new VaultEntry { Id = 1, SpeciesId = "slime", Trees = { new VaultTree { TreeId = tree == "fortune" ? "brute_force" : "fortune", Bought = 8 } } });
            return a;
        }
        [Test]
        public void AllGenericTreesHaveTwentyPointBudget_AndInheritedBirthSnapshot()
        {
            var trees = TreeLineages.Trees(Db).ToArray();
            Assert.That(trees.Length, Is.EqualTo(40));
            foreach (var tree in trees)
            {
                var a = Parents(tree.Id);
                Assert.That(TreeLineages.EligiblePrimary(Db, a.Vault[0], tree.Id), Is.True, tree.Id);
                var inherited = new FortunaLineage { Rank = 10 };
                a.Vault[0].Lineages = new SortedDictionary<string, FortunaLineage> { [tree.Id] = inherited };
                var s = TreeLineages.Breed(Db, a, 0, 1, tree.Id);
                Assert.That((s.Lineage.TreeId, s.Lineage.Rank, s.Slots[2].Roll.LineageRank), Is.EqualTo((tree.Id, 10, 10)), tree.Id);
                Assert.That(TreeLineages.PointsFor(Db, tree.Id, 1), Is.GreaterThan(0), tree.Id);
                Assert.That(TreeLineages.PointsFor(Db, tree.Id, tree.Nodes.Count - 1), Is.EqualTo(8), tree.Id);
                Assert.That(TreeLineages.PointsFor(Db, tree.Id, tree.Nodes.Count), Is.EqualTo(20), tree.Id);
                int prior = 0;
                for (int i = 0; i <= tree.Nodes.Count; i++)
                {
                    int next = TreeLineages.PointsFor(Db, tree.Id, i);
                    Assert.That(next, Is.InRange(prior, 20), tree.Id); prior = next;
                }
                foreach (var n in tree.Nodes.Where(n => TrainingRun.IsFiller(n.Kind)))
                    Assert.That(TrainingRun.Nodes(Db, s.Slots[2])[n.Index].Amount, Is.EqualTo(n.Amount * 1.15).Within(1e-9), tree.Id);
                inherited.Rank = 1;
                Assert.That(s.Lineage.Rank, Is.EqualTo(10), tree.Id);
            }
        }
        [Test]
        public void LegacyReserveLedgersAreIndependent_AndFortuneAliasPreservesReference()
        {
            var a = Parents("fortune");
            Assert.That(TreeLineages.GetLedger(a, "fortune"), Is.SameAs(a.Fortuna));
            var p = new FortunaLineage { Rank = 4, Points = 3, Attempts = 2 };
            a.Fortuna = p;
            Assert.That(a.Lineages["fortune"], Is.SameAs(p));
            TreeLineages.Ledger(a, "bulwark").Rank = 8;
            Assert.That(a.Fortuna.Rank, Is.EqualTo(4));
            int count = a.Lineages.Count;
            Assert.That(TreeLineages.GetLedger(a, "spellcraft").Rank, Is.EqualTo(1));
            Assert.That(a.Lineages.Count, Is.EqualTo(count), "reading a missing ledger must not mutate saves");
        }
        [Test]
        public void OnlyFocusedNodesAdvanceHighWater_AndFailedBreedDoesNotCreateLedger()
        {
            var a = Parents("brute_force");
            Assert.Throws<InvalidOperationException>(() => TreeLineages.Breed(Db, a, 1, 0, "brute_force"));
            Assert.That(a.Lineages.ContainsKey("brute_force"), Is.False);
            var s = TreeLineages.Breed(Db, a, 0, 1, "brute_force"); s.Sp = 100;
            TrainingRun.SpendPoint(Db, s, 0);
            Assert.That(s.Lineage.Deepest, Is.Zero);
            for (int i = 0; i < 8; i++) TrainingRun.SpendPoint(Db, s, 2);
            Assert.That(s.Lineage.Deepest, Is.EqualTo(8));
            var clone = s.Clone(); clone.Lineage.Deepest = 1;
            Assert.That(s.Lineage.Deepest, Is.EqualTo(8));
            TreeLineages.Award(Db, a, s);
            Assert.That(s.Lineage.Progress["brute_force"].Points, Is.EqualTo(20));
            Assert.That(a.Lineages.ContainsKey("brute_force"), Is.False, "new awards never create account unlocks");
            Assert.That(a.Fortuna.Points, Is.Zero);
        }
        [Test]
        public void LaterThresholdsAndGuarantees_AndRankTenStopsProgress()
        {
            var a = Parents("bulwark"); var p = new FortunaLineage { Seed = 123 };
            int[] thresholds = { 40, 80, 140, 180, 320, 480, 700, 1000, 1400 };
            for (int rank = 1; rank < 10; rank++)
            {
                p.Rank = rank; p.Failures = 0;
                Assert.That(TreeLineages.Required(Db, p), Is.EqualTo(thresholds[rank - 1]));
                if (rank != 4 && rank != 9) Assert.That(TreeLineages.Chance(Db, p), Is.EqualTo(100));
            }
            p.Rank = 9; p.Failures = 0;
            Assert.That(TreeLineages.Chance(Db, p), Is.EqualTo(35));
            p.Failures = 4; Assert.That(TreeLineages.Chance(Db, p), Is.EqualTo(95));
            p.Failures = 5; Assert.That(TreeLineages.Chance(Db, p), Is.EqualTo(100));
            Assert.That(TreeLineages.Required(Db, p), Is.EqualTo(350));
            p.Points = 350;
            var s = new RunState { Lineage = new LineageRun { TreeId = "bulwark", Rank = p.Rank, Deepest = 8,
                Progress = new SortedDictionary<string, FortunaLineage> { ["bulwark"] = p } } };
            TreeLineages.Award(Db, a, s);
            Assert.That((p.Rank, p.Points, p.Failures), Is.EqualTo((10, 20, 0)));
            int attempts = p.Attempts;
            TreeLineages.Award(Db, a, s);
            Assert.That((p.Rank, p.Points, p.LastAward, p.Attempts), Is.EqualTo((10, 20, 0, attempts)));
        }
        [Test]
        public void OtherTreeAwardsDoNotPerturbSeededBreakthroughs()
        {
            var a = Parents("fortune"); var b = Parents("fortune");
            var fortuneA = new FortunaLineage { Rank = 4, Points = 180, Seed = 77 };
            var fortuneB = fortuneA.Clone();
            var other = new FortunaLineage { Rank = 9, Points = 1400, Seed = 92 };
            TreeLineages.Award(Db, a, new RunState { Lineage = new LineageRun { TreeId = "bulwark", Rank = 9,
                Progress = new SortedDictionary<string, FortunaLineage> { ["bulwark"] = other } } });
            var first = new RunState { Lineage = new LineageRun { Rank = 4, Progress = new SortedDictionary<string, FortunaLineage> { ["fortune"] = fortuneA } } };
            var second = new RunState { Lineage = new LineageRun { Rank = 4, Progress = new SortedDictionary<string, FortunaLineage> { ["fortune"] = fortuneB } } };
            TreeLineages.Award(Db, a, first); TreeLineages.Award(Db, b, second);
            Assert.That((fortuneA.Rank, fortuneA.Points, fortuneA.Failures, fortuneA.Attempts), Is.EqualTo((fortuneB.Rank, fortuneB.Points, fortuneB.Failures, fortuneB.Attempts)));
            Assert.That((a.Fortuna.Points, b.Fortuna.Points), Is.EqualTo((0, 0)));
        }
        [Test]
        public void BothMilestonesRequireFrozenInheritedCapstone()
        {
            var a = Parents("vital_core"); var inherited = new FortunaLineage { Rank = 10 };
            a.Vault[0].Lineages = new SortedDictionary<string, FortunaLineage> { ["vital_core"] = inherited };
            var s = TreeLineages.Breed(Db, a, 0, 1, "vital_core"); s.Sp = 100;
            for (int i = 0; i < 7; i++) TrainingRun.SpendPoint(Db, s, 2);
            Assert.That(TrainingRun.MonsterFighter(Db, s).Rules, Does.Not.Contain("lineage_vital_core_x"));
            TrainingRun.SpendPoint(Db, s, 2);
            Assert.That(TrainingRun.MonsterFighter(Db, s).Rules, Does.Contain("lineage_vital_core_v").And.Contain("lineage_vital_core_x"));
            inherited.Rank = 1;
            Assert.That(TrainingRun.MonsterFighter(Db, s).Rules, Does.Contain("lineage_vital_core_x"));
        }
    }
}
