using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Tower;
using NUnit.Framework;

namespace IdleRng.Server.Tests
{
    /// <summary>The Endless Tower season board (specs/28 section 6): ranking, reads, and the server writing it at commits.</summary>
    public class EndlessBoardTests
    {
        const string P = Rig.Player;

        static BoardView Read(EndlessBoard b, string player, int season, int top = EndlessBoard.DefaultTop) => b.ReadAsync(player, season, top).Result;

        [Test]
        public void Store_KeepsEachPlayersBest_AndRanksTiesByWhoGotThereFirst()
        {
            var s = new MemoryLeaderboardStore();
            s.SubmitAsync("b", "p1", 10, 500).Wait();
            s.SubmitAsync("b", "p2", 12, 900).Wait();
            s.SubmitAsync("b", "p3", 10, 300).Wait();
            s.SubmitAsync("b", "p1", 10, 100).Wait();   // the same floor again: keeps the first time
            s.SubmitAsync("b", "p2", 8, 50).Wait();     // lower: changes nothing
            var (top, total) = s.TopAsync("b", 10).Result;
            Assert.That(total, Is.EqualTo(3));
            Assert.That(top.Select(e => (e.PlayerId, e.Score, e.AtMs, e.Rank)),
                Is.EqualTo(new[] { ("p2", 12, 900L, 1), ("p3", 10, 300L, 2), ("p1", 10, 500L, 3) }));
            s.SubmitAsync("b", "p1", 13, 1000).Wait();
            Assert.That(s.EntryAsync("b", "p1").Result.Rank, Is.EqualTo(1), "a better floor moves up");
            Assert.That(s.EntryAsync("b", "p9").Result, Is.Null);
            Assert.That(s.TopAsync("other", 10).Result.Total, Is.EqualTo(0), "boards are apart");
            s.RemoveAsync("b", "p1").Wait();
            s.RemoveAsync("b", "p1").Wait();
            Assert.That(s.TopAsync("b", 10).Result.Total, Is.EqualTo(2));
        }

        [Test]
        public void Read_GivesTheTop_AndTheReadersOwnRow_WithNamesOrGeneratedOnes()
        {
            var s = new MemoryLeaderboardStore();
            var b = new EndlessBoard(s);
            for (int i = 0; i < 30; i++) s.SubmitAsync(EndlessBoard.BoardId(7), "p" + i, 100 - i, i).Wait();
            s.SetNameAsync("p0", "Red").Wait();

            var v = Read(b, "p25", 7, 5);
            Assert.That((v.Season, v.Total, v.Top.Count), Is.EqualTo((7, 30, 5)));
            Assert.That(v.Top[0].Name, Is.EqualTo("Red"));
            Assert.That(v.Top[1].Name, Is.EqualTo(EndlessBoard.FallbackName("p1")));
            Assert.That(v.Top.Any(r => r.You), Is.False);
            Assert.That((v.You.Rank, v.You.Floor, v.You.You), Is.EqualTo((26, 75, true)));

            var mine = Read(b, "p0", 7, 5);
            Assert.That(mine.Top[0].You && mine.You.Rank == 1, Is.True);
            Assert.That(Read(b, "nobody", 7).You, Is.Null);
            Assert.That(Read(b, "p0", 8).Total, Is.EqualTo(0), "another season is another board");
            Assert.That(Read(b, "p0", 7, 1000).Top.Count, Is.EqualTo(30), "at most MaxTop rows");
            Assert.That(Read(b, "p0", 7, 0).Top.Count, Is.EqualTo(1));
            Assert.ThrowsAsync<ArgumentException>(() => b.ReadAsync("", 7));
            Assert.ThrowsAsync<ArgumentException>(() => b.ReadAsync("p0", -1));

            s.SetNameAsync("p0", null).Wait();
            Assert.That(Read(b, "p0", 7).Top[0].Name, Is.EqualTo(EndlessBoard.FallbackName("p0")), "a cleared name shows the generated one");
        }

        [Test]
        public void FallbackName_IsStablePerPlayer()
        {
            Assert.That(EndlessBoard.FallbackName("abc"), Does.Match("^Trainer [0-9A-F]{4}$"));
            Assert.That(EndlessBoard.FallbackName("abc"), Is.EqualTo(EndlessBoard.FallbackName("abc")));
            Assert.That(EndlessBoard.FallbackName("abc"), Is.Not.EqualTo(EndlessBoard.FallbackName("abd")));
            Assert.That(EndlessBoard.BoardId(2913), Is.EqualTo("endless-2913"));
        }

        [Test]
        public void AcceptedCommits_WriteTheBoard_AndADebugAccountLeavesIt()
        {
            var rig = new Rig(P);
            var a = rig.Device("a");
            a.Open();
            for (int i = 0; i < 4; i++) a.SealOne(rig.Now);
            int season = EndlessSim.SeasonOf(AccountServer.DayOf(rig.Now));
            var board = rig.Server.Board;
            Assert.That(Read(board, P, season).Total, Is.EqualTo(0), "no climb yet: not on the board");

            a.Do("setName", "Brock");
            Assert.That(a.Commit().Accepted, Is.True);
            Assert.That(Read(board, P, season).Total, Is.EqualTo(0), "a name alone puts nobody on the board");

            // A refused commit writes nothing.
            a.Do("runEndless", season, new List<int> { 0, 1, 2, 3 });
            a.Send("dismantle", 99);
            Assert.That(a.Commit().Code, Is.EqualTo(Rejection.Refused));
            Assert.That(Read(board, P, season).Total, Is.EqualTo(0));
            a.Open();

            long first = rig.Now;
            a.Do("runEndless", season, new List<int> { 0, 1, 2, 3 });
            Assert.That(a.Commit().Accepted, Is.True);
            int cleared = a.Acct.Endless.ClearedSeason;
            Assume.That(cleared, Is.GreaterThan(0), "the starter team clears a floor");
            var v = Read(board, P, season);
            Assert.That((v.Total, v.You.Rank, v.You.Floor, v.You.Name), Is.EqualTo((1, 1, cleared, "Brock")));
            var entry = rig.Boards.EntryAsync(EndlessBoard.BoardId(season), P).Result;
            Assert.That(entry.AtMs, Is.EqualTo(first));

            rig.Now += 60_000;
            a.Do("runEndless", season, new List<int> { 0, 1, 2, 3 });   // the same team clears the same floors: same row, same time
            a.Do("setName", "Brock Two");
            Assert.That(a.Commit().Accepted, Is.True);
            entry = rig.Boards.EntryAsync(EndlessBoard.BoardId(season), P).Result;
            Assert.That((entry.Score, entry.AtMs, entry.Name), Is.EqualTo((cleared, first, "Brock Two")));

            a.Do("debugGrantBook", Content.Db.SkillBooks.Keys.First(), 1);
            Assert.That(a.Commit().Accepted, Is.True);
            Assert.That(Read(board, P, season).Total, Is.EqualTo(0), "a debug account leaves the board");
            a.Do("runEndless", season, new List<int> { 0, 1, 2, 3 });
            Assert.That(a.Commit().Accepted, Is.True);
            Assert.That(Read(board, P, season).Total, Is.EqualTo(0), "and never comes back");
        }

        [Test]
        public void AServerWithoutBoards_CommitsAsBefore()
        {
            var rig = new Rig();
            var server = new AccountServer(Content.Db, new MemoryAccountStore(), rig.Config);
            Assert.That(server.Board, Is.Null);
            var r = server.OpenAsync(P, new OpenRequest { DeviceId = "a" }).Result;
            var acct = IdleRng.Runtime.Save.AccountSave.Decode(Content.Db, System.Text.Encoding.UTF8.GetBytes(r.Account));
            Sim.Account.AccountOps.SetName(acct, "Misty");
            var c = server.CommitAsync(P, new CommitRequest
            {
                DeviceId = "a", FromSeq = r.Seq, FromHash = r.Hash, ContentVersion = Content.Db.Version,
                Commands = new List<WireCommand> { new WireCommand("setName", "Misty") },
                ToHash = IdleRng.Runtime.Save.AccountParts.AccountHash(IdleRng.Runtime.Save.AccountParts.Split(acct)),
            }).Result;
            Assert.That(c.Accepted, Is.True, c.Reason);
        }
    }
}
