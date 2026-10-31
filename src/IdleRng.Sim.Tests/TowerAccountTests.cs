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
    /// <summary>Dungeon attempts and rewards on the account (specs/21 section 7).</summary>
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

        /// <summary>Set the account's ticket stock (the runs WithVault seals earn pieces and maybe tickets).</summary>
        static void SetTickets(AccountState a, int tickets, int pieces = 0)
        {
            a.Tower.Tickets = tickets;
            a.Tower.TicketPieces = pieces;
        }

        [Test]
        public void RunTower_PaysEveryClearedFloor_OnEveryTicket_AndMatchesTheSim()
        {
            var a = WithVault(2);
            SetTickets(a, 3);
            var team = new[] { 0, 1 };
            int day = DayWithAClear(a, team);
            int ticketsBefore = AccountOps.TowerTickets(Db, a, day);
            long before = a.Shards;
            var r = AccountOps.RunTower(Db, a, team, day);
            var expected = TowerSim.Run(Db, team.Select(i => a.Vault[i].Fighter(Db)).ToList(), day);
            Assert.That(r.Result.Hash, Is.EqualTo(expected.Hash));
            Assert.That(r.Shards, Is.EqualTo((long)K.TowerShardsPerFloor * expected.FloorsCleared));
            Assert.That(a.Shards, Is.EqualTo(before + r.Shards));
            Assert.That(r.FirstClears, Is.EqualTo(Enumerable.Range(1, expected.FloorsCleared)));
            Assert.That(r.Books.Select(b => b.Book), Is.EqualTo(Enumerable.Range(1, expected.FloorsCleared)
                .Select(f => TowerSim.BookDrop(Db, day, f, 0)).Where(b => b != null)));
            foreach (var g in r.Books.GroupBy(b => b.Book)) Assert.That(a.BookCount(g.Key), Is.EqualTo(g.Count()));
            Assert.That(a.Tower.Day, Is.EqualTo(day));
            Assert.That(a.Tower.AttemptsUsed, Is.EqualTo(1));
            Assert.That(a.Tower.ClearedToday, Is.EqualTo(expected.FloorsCleared));
            Assert.That(a.Tower.ClearedEver, Is.EqualTo(expected.FloorsCleared));
            Assert.That(r.TicketsLeft, Is.EqualTo(ticketsBefore - 1));

            // the same team again the same day clears the same floors and is paid again, with the second attempt's book rolls
            long shards = a.Shards;
            var again = AccountOps.RunTower(Db, a, team, day);
            Assert.That(again.Shards, Is.EqualTo(r.Shards));
            Assert.That(a.Shards, Is.EqualTo(shards + again.Shards));
            Assert.That(again.Books.Select(b => b.Book), Is.EqualTo(Enumerable.Range(1, expected.FloorsCleared)
                .Select(f => TowerSim.BookDrop(Db, day, f, 1)).Where(b => b != null)));
            Assert.That(again.FirstClears, Is.Empty, "a floor is a first clear only once");
            Assert.That(a.Tower.AttemptsUsed, Is.EqualTo(2));
            Assert.That(again.TicketsLeft, Is.EqualTo(ticketsBefore - 2));
        }

        [Test]
        public void FreeTickets_OnePerDay_BankUpToTheCap()
        {
            var a = WithVault(1);
            SetTickets(a, 0);
            Assert.That(AccountOps.TowerTickets(Db, a, 10), Is.EqualTo(K.TowerAttemptsPerDay), "the first visit has one day's free tickets");
            for (int i = 0; i < K.TowerAttemptsPerDay; i++) AccountOps.RunTower(Db, a, new[] { 0 }, 10);
            Assert.That(AccountOps.TowerTickets(Db, a, 10), Is.EqualTo(0));
            var snapshot = (a.Shards, a.Tower.AttemptsUsed, a.Tower.ClearedToday, a.Tower.Tickets, a.Tower.GrantDay);
            Assert.Throws<InvalidOperationException>(() => AccountOps.RunTower(Db, a, new[] { 0 }, 10));
            Assert.That((a.Shards, a.Tower.AttemptsUsed, a.Tower.ClearedToday, a.Tower.Tickets, a.Tower.GrantDay), Is.EqualTo(snapshot),
                "a refused attempt changes nothing");

            Assert.That(AccountOps.TowerTickets(Db, a, 11), Is.EqualTo(K.TowerAttemptsPerDay), "a new day grants the day's free tickets");
            AccountOps.RunTower(Db, a, new[] { 0 }, 11);
            Assert.That(a.Tower.Day, Is.EqualTo(11));
            Assert.That(a.Tower.AttemptsUsed, Is.EqualTo(1));
            Assert.That(a.Tower.GrantDay, Is.EqualTo(11));

            // missed days bank, up to the cap; reading does not grant
            int held = a.Tower.Tickets;
            Assert.That(AccountOps.TowerTickets(Db, a, 13), Is.EqualTo(Math.Min(K.TowerTicketCap, held + 2 * K.TowerAttemptsPerDay)));
            Assert.That(AccountOps.TowerTickets(Db, a, 11 + 1000), Is.EqualTo(Math.Max(held, K.TowerTicketCap)));
            Assert.That(a.Tower.Tickets, Is.EqualTo(held));
            AccountOps.RunTower(Db, a, new[] { 0 }, 11 + 1000);
            Assert.That(a.Tower.Tickets, Is.EqualTo(K.TowerTicketCap - 1), "granted up to the cap, then one spent");
        }

        [Test]
        public void FreeTickets_DoNotCutTicketsFromRuns()
        {
            var a = WithVault(1);
            SetTickets(a, K.TowerTicketCap + 3);
            a.Tower.GrantDay = 5;
            Assert.That(AccountOps.TowerTickets(Db, a, 6), Is.EqualTo(K.TowerTicketCap + 3), "over the cap: no free ticket, none lost");
            AccountOps.RunTower(Db, a, new[] { 0 }, 6);
            Assert.That(a.Tower.Tickets, Is.EqualTo(K.TowerTicketCap + 2));
        }

        [Test]
        public void FinishRun_AddsTheGradesTicketPieces_AndMakesTickets()
        {
            var a = WithVault(0);
            bool sawPieces = false;
            for (int i = 0; i < 6; i++)
            {
                int ticketsBefore = a.Tower.Tickets;
                int piecesBefore = i == 0 ? K.TowerTicketPieces - 1 : a.Tower.TicketPieces;   // one short of a ticket on the first run
                a.Tower.TicketPieces = piecesBefore;
                var s = AccountOps.StartRun(Db, a, Db.StarterSpecies.Id);
                while (s.Phase != RunPhase.Finished)
                {
                    while (s.Offers.Count > 0) TrainingRun.DiscardOffer(Db, s);
                    if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s);
                    else if (s.Energy < 40) TrainingRun.Rest(Db, s);
                    else TrainingRun.Drill(Db, s, i % 2 == 0 ? StatIds.Str : StatIds.Hp);
                }
                var r = AccountOps.FinishRun(Db, a);
                int pieces = Db.FightRanks.Single(x => x.Id == r.Entry.Grade).TicketPieces;
                Assert.That(r.TicketPieces, Is.EqualTo(pieces));
                int total = piecesBefore + pieces;
                Assert.That(r.TicketsEarned, Is.EqualTo(total / K.TowerTicketPieces));
                Assert.That(a.Tower.Tickets, Is.EqualTo(ticketsBefore + total / K.TowerTicketPieces));
                Assert.That(a.Tower.TicketPieces, Is.EqualTo(total % K.TowerTicketPieces));
                sawPieces |= pieces > 0;
            }
            if (!sawPieces) Assert.Inconclusive("no placeholder run earned ticket pieces");
            Assert.That(Db.FightRanks.Select(x => x.TicketPieces), Is.Ordered, "placeholder pieces rise with the grade");
        }

        [Test]
        public void RunTower_RefusesAnEarlierDay_ABadTeam_AndANegativeDay()
        {
            var a = WithVault(2);
            SetTickets(a, 5);
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
        public void NewAccount_HasNoTowerProgress_AndADaysFreeTicketsDue()
        {
            var a = AccountOps.New(Db, 1);
            Assert.That(a.Tower.Day, Is.EqualTo(-1));
            Assert.That(a.Tower.ClearedEver, Is.EqualTo(0));
            Assert.That(a.Tower.Tickets, Is.EqualTo(0));
            Assert.That(a.Tower.GrantDay, Is.EqualTo(-1));
            Assert.That(AccountOps.TowerTickets(Db, a, 0), Is.EqualTo(K.TowerAttemptsPerDay));
        }
    }
}
