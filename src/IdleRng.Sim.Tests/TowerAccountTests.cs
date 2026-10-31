using System;
using System.Linq;
using IdleRng.Sim.Account;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Tower;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Daily Tower attempts and rewards on the account (specs/21 section 7).</summary>
    public class TowerAccountTests
    {
        static ContentDb Db => TestContent.Db;
        static Constants K => Db.Constants;

        /// <summary>An account with <paramref name="monsters"/> sealed monsters in the Vault.</summary>
        static AccountState WithVault(int monsters, ulong seed = 21)
        {
            var a = AccountOps.New(Db, seed);
            for (int i = 0; i < monsters; i++)
            {
                var s = AccountOps.StartRun(Db, a, Db.StarterSpecies.Id);
                while (s.Phase != RunPhase.Finished)
                {
                    while (s.Offers.Count > 0) TrainingRun.DiscardOffer(Db, s);
                    if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s);
                    else if (s.Energy < 40) TrainingRun.Rest(Db, s);
                    else TrainingRun.Drill(Db, s, StatIds.Str);
                }
                AccountOps.FinishRun(Db, a);
            }
            return a;
        }

        /// <summary>A day on which the team clears at least one floor (searched, so the test does not depend on placeholder numbers).</summary>
        static int DayWithAClear(AccountState a, int[] team)
        {
            for (int day = 0; day < 400; day++)
                if (TowerSim.Run(Db, team.Select(i => a.Vault[i].Fighter(Db)).ToList(), day).FloorsCleared > 0) return day;
            Assert.Inconclusive("no day in 0..399 where the placeholder team clears a floor");
            return -1;
        }

        [Test]
        public void RunTower_PaysEachClearedFloorOnce_AndMatchesTheSim()
        {
            var a = WithVault(2);
            var team = new[] { 0, 1 };
            int day = DayWithAClear(a, team);
            long before = a.Shards;
            var r = AccountOps.RunTower(Db, a, team, day);
            var expected = TowerSim.Run(Db, team.Select(i => a.Vault[i].Fighter(Db)).ToList(), day);
            Assert.That(r.Result.Hash, Is.EqualTo(expected.Hash));
            Assert.That(r.Shards, Is.EqualTo((long)K.TowerShardsPerFloor * expected.FloorsCleared));
            Assert.That(a.Shards, Is.EqualTo(before + r.Shards));
            Assert.That(r.FirstClears, Is.EqualTo(Enumerable.Range(1, expected.FloorsCleared)));
            Assert.That(r.Books.Select(b => b.Book), Is.EqualTo(Enumerable.Range(1, expected.FloorsCleared)
                .Select(f => TowerSim.BookDrop(Db, day, f)).Where(b => b != null)));
            foreach (var g in r.Books.GroupBy(b => b.Book)) Assert.That(a.BookCount(g.Key), Is.EqualTo(g.Count()));
            Assert.That(a.Tower.Day, Is.EqualTo(day));
            Assert.That(a.Tower.AttemptsUsed, Is.EqualTo(1));
            Assert.That(a.Tower.ClearedToday, Is.EqualTo(expected.FloorsCleared));
            Assert.That(a.Tower.ClearedEver, Is.EqualTo(expected.FloorsCleared));
            Assert.That(r.AttemptsLeft, Is.EqualTo(K.TowerAttemptsPerDay - 1));

            // the same team again the same day clears the same floors: nothing more to pay
            long shards = a.Shards;
            var again = AccountOps.RunTower(Db, a, team, day);
            Assert.That(again.Shards, Is.EqualTo(0));
            Assert.That(again.Books, Is.Empty);
            Assert.That(again.FirstClears, Is.Empty);
            Assert.That(a.Shards, Is.EqualTo(shards));
        }

        [Test]
        public void RunTower_CapsAttemptsPerDay_AndANewDayResets()
        {
            var a = WithVault(1);
            for (int i = 0; i < K.TowerAttemptsPerDay; i++) AccountOps.RunTower(Db, a, new[] { 0 }, 10);
            Assert.That(AccountOps.TowerAttemptsLeft(Db, a, 10), Is.EqualTo(0));
            var snapshot = (a.Shards, a.Tower.AttemptsUsed, a.Tower.ClearedToday);
            Assert.Throws<InvalidOperationException>(() => AccountOps.RunTower(Db, a, new[] { 0 }, 10));
            Assert.That((a.Shards, a.Tower.AttemptsUsed, a.Tower.ClearedToday), Is.EqualTo(snapshot), "a refused attempt changes nothing");

            Assert.That(AccountOps.TowerAttemptsLeft(Db, a, 11), Is.EqualTo(K.TowerAttemptsPerDay));
            AccountOps.RunTower(Db, a, new[] { 0 }, 11);
            Assert.That(a.Tower.Day, Is.EqualTo(11));
            Assert.That(a.Tower.AttemptsUsed, Is.EqualTo(1));
        }

        [Test]
        public void RunTower_RefusesAnEarlierDay_ABadTeam_AndANegativeDay()
        {
            var a = WithVault(2);
            AccountOps.RunTower(Db, a, new[] { 0 }, 50);
            Assert.Throws<InvalidOperationException>(() => AccountOps.RunTower(Db, a, new[] { 0 }, 49), "the clock went back");
            Assert.Throws<ArgumentException>(() => AccountOps.RunTower(Db, a, new int[0], 50));
            Assert.Throws<ArgumentException>(() => AccountOps.RunTower(Db, a, new[] { 0, 0 }, 50));
            Assert.Throws<ArgumentOutOfRangeException>(() => AccountOps.RunTower(Db, a, new[] { 5 }, 50));
            Assert.Throws<ArgumentException>(() => AccountOps.RunTower(Db, a, Enumerable.Repeat(0, K.MonSideSize + 1).ToArray(), 50));
            Assert.Throws<ArgumentOutOfRangeException>(() => AccountOps.RunTower(Db, a, new[] { 0 }, -1));
            Assert.That(a.Tower.AttemptsUsed, Is.EqualTo(1), "refused attempts are not counted");
        }

        [Test]
        public void RunTower_WorksWithARunInProgress_AndLeavesItAlone()
        {
            var a = WithVault(1);
            var run = AccountOps.StartRun(Db, a, Db.StarterSpecies.Id);
            string hash = TrainingRun.StateHash(run);
            AccountOps.RunTower(Db, a, new[] { 0 }, 3);
            Assert.That(a.Active, Is.SameAs(run));
            Assert.That(TrainingRun.StateHash(a.Active), Is.EqualTo(hash));
        }

        [Test]
        public void NewAccount_HasNoTowerProgress()
        {
            var a = AccountOps.New(Db, 1);
            Assert.That(a.Tower.Day, Is.EqualTo(-1));
            Assert.That(a.Tower.ClearedEver, Is.EqualTo(0));
            Assert.That(AccountOps.TowerAttemptsLeft(Db, a, 0), Is.EqualTo(K.TowerAttemptsPerDay));
        }
    }
}
