using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IdleRng.Server;
using NUnit.Framework;

namespace MonStackAccount.Tests
{
    /// <summary>The Cloud Save account store: the head's write lock, the item rules, and races through the server core.</summary>
    [TestFixture]
    public class CloudSaveAccountStoreTests
    {
        const string P = "player-1";
        static readonly CloudSaveOwner Owner = CloudSaveOwner.Player(P);

        [Test]
        public void Head_IsCreatedOnce_ThenReplacedOnlyUnderItsCurrentLock()
        {
            var save = new FakeCloudSave();
            var store = new CloudSaveAccountStore(save);
            Assert.That(store.ReadHeadAsync(P).Result, Is.Null);

            Assert.That(store.WriteHeadAsync(P, "{\"v\":1}", null).Result, Is.True, "create");
            var h1 = store.ReadHeadAsync(P).Result;
            Assert.That(h1.Json, Is.EqualTo("{\"v\":1}"));
            Assert.That(store.WriteHeadAsync(P, "{\"v\":2}", null).Result, Is.False, "a second create loses");
            Assert.That(store.WriteHeadAsync(P, "{\"v\":2}", "not-the-lock").Result, Is.False, "a wrong lock loses");
            Assert.That(store.ReadHeadAsync(P).Result.Json, Is.EqualTo("{\"v\":1}"), "a lost write changes nothing");

            Assert.That(store.WriteHeadAsync(P, "{\"v\":3}", h1.WriteLock).Result, Is.True, "the current lock wins");
            var h3 = store.ReadHeadAsync(P).Result;
            Assert.That((h3.Json, h3.WriteLock != h1.WriteLock), Is.EqualTo(("{\"v\":3}", true)), "a write moves the lock on");
            Assert.That(store.WriteHeadAsync(P, "{\"v\":4}", h1.WriteLock).Result, Is.False, "the lock it replaced is spent");
            Assert.That(store.ReadHeadAsync(P).Result.Json, Is.EqualTo("{\"v\":3}"));
        }

        [Test]
        public void Head_ACreateOverwrittenBeforeItsReadBack_Loses()
        {
            var save = new FakeCloudSave();
            var store = new CloudSaveAccountStore(save);
            int gets = 0;
            // The read-back after the unlocked create finds another create's head: this create lost and says so.
            save.Before.Add(("get", "head", () => { gets++; return Task.CompletedTask; }));
            save.Before.Add(("get", "head", () => { gets++; save.Put(Owner, "head", "{\"other\":1}"); return Task.CompletedTask; }));
            Assert.That(store.WriteHeadAsync(P, "{\"mine\":1}", null).Result, Is.False);
            Assert.That(gets, Is.EqualTo(2), "checked absent, wrote, read back");
            Assert.That(store.ReadHeadAsync(P).Result.Json, Is.EqualTo("{\"other\":1}"));
        }

        [Test]
        public void Items_AreChunked_Batched_AndListedAcrossPages()
        {
            var save = new FakeCloudSave();
            var store = new CloudSaveAccountStore(save);
            var items = Enumerable.Range(0, 45).ToDictionary(i => "k" + i.ToString("00") + "_1_abc", i => "{\"i\":" + i + "}");
            store.WriteAsync(P, items).Wait();   // the fake refuses a batch of more than 20
            Assert.That(save.Calls.Count(c => c.StartsWith("setMany")), Is.EqualTo(3));
            store.WriteHeadAsync(P, "{}", null).Wait();

            var read = store.ReadAsync(P, items.Keys.Append("missing_1_x").ToList()).Result;   // the fake refuses more than 20 keys a read
            Assert.That(read, Is.EquivalentTo(items), "every item back, the missing key left out");
            Assert.That(store.ListKeysAsync(P).Result, Is.EqualTo(items.Keys.Append("head").OrderBy(k => k, StringComparer.Ordinal)),
                "listed across pages of " + FakeCloudSave.KeyPage);

            store.DeleteAsync(P, new[] { "k00_1_abc", "nothing_here" }).Wait();
            Assert.That(store.ReadAsync(P, new[] { "k00_1_abc" }).Result, Is.Empty);
            Assert.Throws<ArgumentException>(() => store.WriteAsync(P, new Dictionary<string, string> { ["head"] = "{}" }).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => store.DeleteAsync(P, new[] { "head" }).GetAwaiter().GetResult());
        }

        [Test]
        public void Items_BelongToTheirPlayer()
        {
            var save = new FakeCloudSave();
            var store = new CloudSaveAccountStore(save);
            store.WriteAsync("a", new Dictionary<string, string> { ["x_1_t"] = "1" }).Wait();
            Assert.That(store.ReadAsync("b", new[] { "x_1_t" }).Result, Is.Empty);
            Assert.That(save.Keys(CloudSaveOwner.Player("a")), Is.EqualTo(new[] { "x_1_t" }));
        }

        // ------------------------------------------------------------------ through the server core

        static List<string> Referenced(FakeCloudSave save, string player)
        {
            var h = AccountHead.FromJson(save.Value(CloudSaveOwner.Player(player), "head"));
            return h.Parts.Values.Concat(h.Entries.Values).Append("head").OrderBy(k => k, StringComparer.Ordinal).ToList();
        }

        [Test]
        public void ALostSwap_IsStale_AndLeavesOnlyTheWinnersItems()
        {
            var rig = new Rig();
            var tab1 = rig.Device(P, "phone");
            var tab2 = rig.Device(P, "phone");   // a second tab of the same device, from the same base
            tab1.Open();
            tab2.Open();
            tab1.Do("startRun", Rig.Db.StarterSpecies.Id);
            tab2.Do("startRun", Rig.Db.StarterSpecies.Id);
            tab2.Do("drill", IdleRng.Sim.Combat.StatIds.Str);

            CommitReply won = null;
            rig.Save.Before.Add(("set", "head", async () => { won = tab1.Commit(); await Task.CompletedTask; }));
            var lost = tab2.Commit();
            Assert.That(won.Accepted, Is.True, won.Reason);
            Assert.That(lost.Code, Is.EqualTo("Stale"), lost.Reason);
            var head = AccountHead.FromJson(rig.Save.Value(Owner, "head"));
            Assert.That((head.Seq, head.Hash), Is.EqualTo((1, won.Hash)));
            Assert.That(rig.Save.Keys(Owner), Is.EqualTo(Referenced(rig.Save, P)), "the loser deleted what it wrote");

            Assert.That(tab2.Open().Seq, Is.EqualTo(1), "the loser re-opens on the winner's account");
            Assert.That(tab2.Hash(), Is.EqualTo(won.Hash));
        }

        [Test]
        public void ALostGenesis_DeletesItsItems_AndOpensTheWinner()
        {
            var rig = new Rig();
            var a = rig.Device(P, "laptop");
            var b = rig.Device(P, "phone");
            OpenReply bReply = null;
            // a's parts are written; before its head lands, b's whole first open runs and creates the account.
            rig.Save.Before.Add(("setMany", null, () => { bReply = b.Open(); return Task.CompletedTask; }));
            var aReply = a.Open();
            Assert.That((bReply.Accepted, bReply.Created), Is.EqualTo((true, true)));
            Assert.That(aReply.Code, Is.EqualTo("NotHolder"), "a found b's account; b holds it and a did not claim");
            Assert.That(rig.Save.Keys(Owner), Is.EqualTo(Referenced(rig.Save, P)), "a deleted its parts");
            Assert.That(a.Open(claim: true).Hash, Is.EqualTo(bReply.Hash), "one account");
        }

        [Test]
        public void ACrashBeforeTheSwap_LeavesTheAccountWhole_AndALaterOpenSweepsTheLeftovers()
        {
            var rig = new Rig();
            var d = rig.Device(P, "phone");
            d.Open();
            d.Do("startRun", Rig.Db.StarterSpecies.Id);
            var before = AccountHead.FromJson(rig.Save.Value(Owner, "head"));
            rig.Save.FailWrite = k => k == "head";
            Assert.Throws<InvalidOperationException>(() => d.Commit());
            rig.Save.FailWrite = null;
            Assert.That(AccountHead.FromJson(rig.Save.Value(Owner, "head")).Hash, Is.EqualTo(before.Hash), "the head did not move");
            Assert.That(rig.Save.Keys(Owner).Count, Is.GreaterThan(Referenced(rig.Save, P).Count), "the crashed commit's items are left over");

            Assert.That(d.Commit().Accepted, Is.True, "the retry lands");
            d.Open();
            Assert.That(rig.Save.Keys(Owner), Is.EqualTo(Referenced(rig.Save, P)), "the next open swept the leftovers");
        }
    }

    /// <summary>The Cloud Save board store behaves as the in-memory one, shard by shard, and survives write races.</summary>
    [TestFixture]
    public class CloudSaveLeaderboardStoreTests
    {
        static string Show(LeaderboardEntry e) => e == null ? "-" : $"{e.Rank}:{e.PlayerId}:{e.Name}:{e.Score}:{e.AtMs}";
        static string ShowRow(LeaderboardEntry e) => e == null ? "-" : $"{e.Rank}:{e.RowId}:{e.PlayerId}:{e.Tag}:{e.Name}:{e.Score}:{e.AtMs}";

        [Test]
        public void MatchesTheMemoryStore_OverRandomOperations()
        {
            var save = new FakeCloudSave();
            var ugs = new CloudSaveLeaderboardStore(save);
            var mem = new MemoryLeaderboardStore();
            var rnd = new Random(7);
            var players = Enumerable.Range(0, 40).Select(i => "p" + i).ToList();
            string[] boards = { "endless-1", "endless-2" };
            for (int step = 0; step < 400; step++)
            {
                string p = players[rnd.Next(players.Count)], b = boards[rnd.Next(2)];
                switch (rnd.Next(10))
                {
                    case 0: ugs.RemoveAsync(b, p).Wait(); mem.RemoveAsync(b, p).Wait(); break;
                    case 1:
                        string name = rnd.Next(3) == 0 ? null : "N" + rnd.Next(5);
                        ugs.SetNameAsync(p, name).Wait(); mem.SetNameAsync(p, name).Wait();
                        break;
                    default:
                        int score = rnd.Next(1, 30);
                        long at = 1000 + step;
                        ugs.SubmitAsync(b, p, score, at).Wait(); mem.SubmitAsync(b, p, score, at).Wait();
                        break;
                }
            }
            Assert.That(players.Select(CloudSaveLeaderboardStore.ShardOf).Distinct().Count(), Is.GreaterThan(8), "the players fill many shards");
            foreach (var b in boards)
            {
                var (ut, un) = ugs.TopAsync(b, 15).Result;
                var (mt, mn) = mem.TopAsync(b, 15).Result;
                Assert.That(un, Is.EqualTo(mn), b + " total");
                Assert.That(ut.Select(Show), Is.EqualTo(mt.Select(Show)), b + " top");
                foreach (var p in players)
                    Assert.That(Show(ugs.EntryAsync(b, p).Result), Is.EqualTo(Show(mem.EntryAsync(b, p).Result)), b + " " + p);
            }
            Assert.That(save.Keys(CloudSaveOwner.Entity("endless-1")).All(k => k.StartsWith("rows-")), Is.True);
            Assert.That(save.Keys(CloudSaveOwner.Entity(CloudSaveLeaderboardStore.NamesEntity)).All(k => k.StartsWith("names-")), Is.True);
        }

        [Test]
        public void ManyRowsPerPlayer_MatchTheMemoryStore_OverRandomOperations()
        {
            var save = new FakeCloudSave();
            var ugs = new CloudSaveLeaderboardStore(save);
            var mem = new MemoryLeaderboardStore();
            var rnd = new Random(11);
            var players = Enumerable.Range(0, 30).Select(i => "p" + i).ToList();
            string[] boards = { "gauntlet", "gauntlet-drake" };
            for (int step = 0; step < 400; step++)
            {
                string p = players[rnd.Next(players.Count)], b = boards[rnd.Next(2)];
                switch (rnd.Next(10))
                {
                    case 0: ugs.RemoveAsync(b, p).Wait(); mem.RemoveAsync(b, p).Wait(); break;
                    case 1:
                        string name = rnd.Next(3) == 0 ? null : "N" + rnd.Next(5);
                        ugs.SetNameAsync(p, name).Wait(); mem.SetNameAsync(p, name).Wait();
                        break;
                    default:
                        var rows = Enumerable.Range(0, rnd.Next(1, 4)).Select(_ => rnd.Next(6)).Distinct()
                            .Select(i => new LeaderboardRow { RowId = p + ":" + i, Score = rnd.Next(1, 30), Tag = "t" + rnd.Next(3) }).ToList();
                        long at = 1000 + step / 3;   // ties on time too: the row id breaks them
                        ugs.SubmitRowsAsync(b, p, rows, at).Wait(); mem.SubmitRowsAsync(b, p, rows, at).Wait();
                        break;
                }
            }
            foreach (var b in boards)
            {
                var (ut, un) = ugs.TopAsync(b, 40).Result;
                var (mt, mn) = mem.TopAsync(b, 40).Result;
                Assert.That(un, Is.EqualTo(mn), b + " total");
                Assert.That(un, Is.GreaterThan(players.Count), b + ": players hold several rows");
                Assert.That(ut.Select(ShowRow), Is.EqualTo(mt.Select(ShowRow)), b + " top");
                foreach (var p in players)
                    Assert.That(ShowRow(ugs.EntryAsync(b, p).Result), Is.EqualTo(ShowRow(mem.EntryAsync(b, p).Result)), b + " " + p);
            }
        }

        [Test]
        public void OnePlayerRows_AndNamedRows_ReadTheirOwnShapes()
        {
            var save = new FakeCloudSave();
            var store = new CloudSaveLeaderboardStore(save);
            store.SubmitAsync("endless-4", "ann", 5, 10).Wait();
            store.SubmitRowsAsync("gauntlet", "ann", new[] { new LeaderboardRow { RowId = "ann:1", Score = 40, Tag = "slime|t|" } }, 20).Wait();
            string key = CloudSaveLeaderboardStore.RowsKey(CloudSaveLeaderboardStore.ShardOf("ann"));
            Assert.That(save.GetOneAsync(CloudSaveOwner.Entity("endless-4"), key).Result.Value, Is.EqualTo("{\"ann\":[5,10]}"), "the Endless shape is unchanged");
            Assert.That(save.GetOneAsync(CloudSaveOwner.Entity("gauntlet"), key).Result.Value, Is.EqualTo("{\"ann:1\":[40,20,\"ann\",\"slime|t|\"]}"));
            Assert.That(ShowRow(store.EntryAsync("gauntlet", "ann").Result), Is.EqualTo("1:ann:1:ann:slime|t|::40:20"));
            store.RemoveAsync("gauntlet", "ann").Wait();
            Assert.That(store.TopAsync("gauntlet", 5).Result.Total, Is.EqualTo(0));
        }

        [Test]
        public void AConcurrentWriteToTheSameShard_IsRetried_NotLost()
        {
            var save = new FakeCloudSave();
            var store = new CloudSaveLeaderboardStore(save);
            string a = "alice", b = Enumerable.Range(0, 1000).Select(i => "bob" + i).First(p => CloudSaveLeaderboardStore.ShardOf(p) == CloudSaveLeaderboardStore.ShardOf(a));
            string key = CloudSaveLeaderboardStore.RowsKey(CloudSaveLeaderboardStore.ShardOf(a));
            store.SubmitAsync("endless-3", a, 5, 100).Wait();   // the shard exists: later writes are locked

            save.Before.Add(("set", key, () => store.SubmitAsync("endless-3", b, 9, 200)));
            store.SubmitAsync("endless-3", a, 7, 300).Wait();
            var (top, total) = store.TopAsync("endless-3", 10).Result;
            Assert.That(total, Is.EqualTo(2));
            Assert.That(top.Select(Show), Is.EqualTo(new[] { $"1:{b}::9:200", $"2:{a}::7:300" }));
            Assert.That(save.Calls.Count(c => c.StartsWith("set custom:endless-3 " + key + " locked")), Is.EqualTo(3), "alice's first locked write lost and was retried");
        }

        [Test]
        public void KeepsTheBest_TiesGoToTheEarlier_AndNamesFollowThePlayer()
        {
            var store = new CloudSaveLeaderboardStore(new FakeCloudSave());
            store.SubmitAsync("endless-9", "x", 10, 500).Wait();
            store.SubmitAsync("endless-9", "y", 10, 400).Wait();
            store.SubmitAsync("endless-9", "x", 10, 100).Wait();   // the same score keeps the first time
            store.SubmitAsync("endless-9", "x", 3, 50).Wait();     // a lower one changes nothing
            store.SetNameAsync("x", "Ash").Wait();
            var (top, _) = store.TopAsync("endless-9", 5).Result;
            Assert.That(top.Select(Show), Is.EqualTo(new[] { "1:y::10:400", "2:x:Ash:10:500" }));
            Assert.That(store.EntryAsync("endless-8", "x").Result, Is.Null, "another board");
            Assert.Throws<ArgumentException>(() => store.TopAsync("bad board!", 5).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => store.TopAsync(CloudSaveLeaderboardStore.NamesEntity, 5).GetAwaiter().GetResult());
        }
    }
}
