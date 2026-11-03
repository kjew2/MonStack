using System;
using System.Linq;
using NUnit.Framework;

namespace IdleRng.Server.Tests
{
    /// <summary>The gauntlet boards (specs/19 section 9): a row per sealed monster, overall and per species, written at commits.</summary>
    public class GauntletBoardTests
    {
        const string P = Rig.Player;

        static LeaderboardRow R(string id, int score, string tag = null) => new LeaderboardRow { RowId = id, Score = score, Tag = tag };

        static GauntletBoardView Read(GauntletBoard b, string player, string species = null, int top = GauntletBoard.DefaultTop) =>
            b.ReadAsync(player, species, top).Result;

        [Test]
        public void Store_KeepsEachRowsBest_ManyRowsPerPlayer()
        {
            var s = new MemoryLeaderboardStore();
            s.SubmitRowsAsync("g", "p1", new[] { R("p1:0", 30, "a"), R("p1:1", 50, "b") }, 100).Wait();
            s.SubmitRowsAsync("g", "p2", new[] { R("p2:0", 50, "c") }, 50).Wait();
            s.SubmitRowsAsync("g", "p1", new[] { R("p1:0", 30, "changed"), R("p1:1", 20, "lower") }, 900).Wait();   // nothing better: no change
            var (top, total) = s.TopAsync("g", 10).Result;
            Assert.That(total, Is.EqualTo(3));
            Assert.That(top.Select(e => (e.RowId, e.PlayerId, e.Score, e.AtMs, e.Tag, e.Rank)),
                Is.EqualTo(new[] { ("p2:0", "p2", 50, 50L, "c", 1), ("p1:1", "p1", 50, 100L, "b", 2), ("p1:0", "p1", 30, 100L, "a", 3) }));
            Assert.That(s.EntryAsync("g", "p1").Result.RowId, Is.EqualTo("p1:1"), "a player's entry is their best row");

            Assert.ThrowsAsync<ArgumentException>(() => s.SubmitRowsAsync("g", "p2", new[] { R("p1:0", 99) }, 1), "a row stays its owner's");
            Assert.ThrowsAsync<ArgumentException>(() => s.SubmitRowsAsync("g", "p2", new[] { R(null, 99) }, 1));

            s.RemoveAsync("g", "p1").Wait();
            Assert.That(s.TopAsync("g", 10).Result.Top.Select(e => e.RowId), Is.EqualTo(new[] { "p2:0" }), "remove takes every row the player owns");
        }

        [Test]
        public void Read_ShowsSpeciesAndTheVaultLabel_AndRefusesUnknownSpecies()
        {
            var db = Content.Db;
            var s = new MemoryLeaderboardStore();
            var b = new GauntletBoard(db, s);
            var sp = db.SpeciesList[0];
            string title = db.LegacyTitles[0].TitleKey, killer = db.GauntletEnemies[0].Id;
            s.SubmitRowsAsync("gauntlet", "p1", new[] { R("p1:3", 7, sp.Id + "|" + title + "|" + killer), R("p1:4", db.Constants.GauntletFloors + 1, sp.Id + "|" + title + "|") }, 1).Wait();
            s.SetNameAsync("p1", "Ash").Wait();

            var v = Read(b, "p2");
            Assert.That((v.Species, v.Total, v.You), Is.EqualTo(((string)null, 2, (GauntletRow)null)));
            var cleared = v.Top[0];
            Assert.That((cleared.Name, cleared.Species, cleared.SpeciesName, cleared.Cleared, cleared.You), Is.EqualTo(("Ash", sp.Id, db.Text(sp.NameKey), true, false)));
            Assert.That(cleared.Label, Does.StartWith("All " + db.Constants.GauntletFloors + " floors: "));
            Assert.That(v.Top[1].Label, Is.EqualTo(GauntletBoard.Label(db, 7, title, killer)));
            Assert.That(v.Top[1].Label, Does.StartWith("Floor 7: "));

            Assert.That(Read(b, "p1").You.Rank, Is.EqualTo(1));
            Assert.That(Read(b, "p1", sp.Id).Total, Is.EqualTo(0), "the species board is its own");
            Assert.ThrowsAsync<ArgumentException>(() => b.ReadAsync("p1", "no-such-species"));
            Assert.ThrowsAsync<ArgumentException>(() => b.ReadAsync("", null));
            Assert.That(GauntletBoard.BoardId(null), Is.EqualTo("gauntlet"));
            Assert.That(GauntletBoard.BoardId("drake"), Is.EqualTo("gauntlet-drake"));
        }

        [Test]
        public void Seals_WriteARowPerMonster_DismantledOnesStay_AndADebugAccountLeaves()
        {
            var rig = new Rig(P);
            var a = rig.Device("a");
            a.Open();
            var g = rig.Server.Gauntlet;
            string sp = Content.Db.StarterSpecies.Id;

            a.Do("setName", "Misty");
            long first = rig.Now;
            a.SealOne(rig.Now);
            var v = Read(g, P);
            Assert.That((v.Total, v.You.Name, v.You.Species, v.You.Floor), Is.EqualTo((1, "Misty", sp, a.Acct.Vault[0].GauntletFloor)));
            Assert.That(rig.Boards.EntryAsync(GauntletBoard.BoardId(null), P).Result.AtMs, Is.EqualTo(first), "the server's time at the commit");
            Assert.That(Read(g, P, sp).Total, Is.EqualTo(1), "and on its species' board");

            // A run sealed and dismantled in the same commit still gets its row; a dismantled monster keeps its row.
            rig.Now += 60_000;
            a.Do("startRun", sp);
            Assert.That(a.Commit().Accepted, Is.True);
            a.PlayOut();
            a.Do("finishRun", rig.Now);
            a.Do("closeVendor");
            a.Do("dismantle", a.Acct.Vault.Count - 1);
            Assert.That(a.Commit().Accepted, Is.True);
            Assert.That(a.Acct.Vault.Count, Is.EqualTo(1));
            Assert.That(Read(g, P).Total, Is.EqualTo(2));
            a.Do("dismantle", 0);
            Assert.That(a.Commit().Accepted, Is.True);
            Assert.That(Read(g, P).Total, Is.EqualTo(2), "rows stay when a monster is dismantled");
            Assert.That(rig.Boards.EntryAsync(GauntletBoard.BoardId(null), P).Result.Name, Is.EqualTo("Misty"));

            a.Do("debugGrantBook", Content.Db.SkillBooks.Keys.First(), 1);
            Assert.That(a.Commit().Accepted, Is.True);
            Assert.That((Read(g, P).Total, Read(g, P, sp).Total), Is.EqualTo((0, 0)), "a debug account leaves every gauntlet board");
            a.SealOne(rig.Now);
            Assert.That(Read(g, P).Total, Is.EqualTo(0), "and never comes back");
        }

        [Test]
        public void AMonsterSealedBeforeTheBoards_JoinsAtTheNextSeal()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            a.SealOne(rig.Now);
            rig.Boards.RemoveAsync(GauntletBoard.BoardId(null), P).Wait();   // as if sealed before the boards existed
            Assert.That(Read(rig.Server.Gauntlet, P).Total, Is.EqualTo(0));
            a.SealOne(rig.Now);
            Assert.That(Read(rig.Server.Gauntlet, P).Total, Is.EqualTo(2));
        }
    }
}
