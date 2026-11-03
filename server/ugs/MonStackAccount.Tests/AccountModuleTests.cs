using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IdleRng.Runtime.Save;
using IdleRng.Server;
using IdleRng.Sim.Content;
using IdleRng.Sim.Tower;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Services.CloudCode.Core;

namespace MonStackAccount.Tests
{
    /// <summary>The module's endpoints, called as Cloud Code calls them, over the fake Cloud Save (issue #28).</summary>
    [TestFixture]
    public class AccountModuleTests
    {
        const string P = "player-1";
        static readonly CloudSaveOwner Owner = CloudSaveOwner.Player(P);

        [Test]
        public void TheEmbeddedContent_IsTheRepositorysContent()
        {
            var dir = FileContentSource.FindContentDirectory(TestContext.CurrentContext.TestDirectory)
                      ?? FileContentSource.FindContentDirectory(AppContext.BaseDirectory);
            Assume.That(dir, Is.Not.Null, "Content/ not found above the test directory");
            var files = ContentDb.LoadFromDirectory(dir);
            Assert.That((ModuleContent.Db.Version, ModuleContent.Db.ContentHash), Is.EqualTo((files.Version, files.ContentHash)));
        }

        [Test]
        public void Open_CreatesTheAccount_AndSendsItAsGzipBase64AccountText()
        {
            var rig = new Rig();
            var d = rig.Device(P, "phone");
            var r = d.Open();
            Assert.That((r.Code, r.Accepted, r.Created, r.Seq), Is.EqualTo(("None", true, true, 0)));
            Assert.That(r.Account, Does.Match("^[A-Za-z0-9+/]+=*$"), "base64");
            Assert.That(Convert.FromBase64String(r.Account).Take(2), Is.EqualTo(new byte[] { 0x1f, 0x8b }), "gzip");
            Assert.That(d.Hash(), Is.EqualTo(r.Hash), "the account text loads to the account the server holds");
            Assert.That((r.ContentVersion, r.ServerSec, r.Day), Is.EqualTo((Rig.Db.Version, Rig.Start / 1000, 20391)));
            Assert.That(rig.Save.Keys(Owner), Does.Contain("head"), "private items of the calling player");

            var again = d.Open();
            Assert.That((again.Created, again.Hash), Is.EqualTo((false, r.Hash)));
        }

        [Test]
        public void ThePlayer_IsTheContextsAndNeverTheRequests()
        {
            var rig = new Rig();
            var a = rig.Device("alice", "phone");
            var b = rig.Device("bob", "phone");
            Assert.That(a.Open().Created, Is.True);
            Assert.That(b.Open().Created, Is.True, "same device id, another player: another account");
            Assert.That(rig.Save.Keys(CloudSaveOwner.Player("alice")), Does.Contain("head"));
            Assert.That(rig.Save.Keys(CloudSaveOwner.Player("bob")), Does.Contain("head"));

            var anonymous = new FakeContext(null);
            Assert.Throws<InvalidOperationException>(() => rig.Module.Open(anonymous, new OpenRequest { DeviceId = "x" }).GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => rig.Module.Commit(anonymous, new CommitRequest { DeviceId = "x" }).GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => rig.Module.EndlessBoard(anonymous, 0, 5).GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => rig.Module.GauntletBoard(anonymous, "", 5).GetAwaiter().GetResult());

            // No endpoint parameter or request field can name a player.
            foreach (var m in typeof(AccountModule).GetMethods().Where(m => m.GetCustomAttribute<CloudCodeFunctionAttribute>() != null))
                foreach (var p in m.GetParameters())
                {
                    Assert.That(p.Name, Does.Not.Contain("player").IgnoreCase, m.Name);
                    if (p.ParameterType.IsClass && p.ParameterType != typeof(string))
                        Assert.That(p.ParameterType.GetProperties().Select(x => x.Name), Has.None.Contains("Player"), m.Name + "." + p.Name);
                }
            var functions = typeof(AccountModule).GetMethods().Select(m => m.GetCustomAttribute<CloudCodeFunctionAttribute>()?.Name).Where(n => n != null);
            Assert.That(functions, Is.EquivalentTo(new[] { "Open", "Commit", "EndlessBoard", "GauntletBoard", "Profile", "Warm" }));
        }

        [Test]
        public void AWholeRun_CommittedAtCheckpoints_EndsEqualToTheClient()
        {
            var rig = new Rig();
            var d = rig.Device(P, "phone");
            d.Open();
            d.SealOne(rig.Now);
            string mine = d.Hash();
            var r = d.Open();
            Assert.That((r.Hash, d.Hash()), Is.EqualTo((mine, mine)), "the server's account is the client's");
            Assert.That(d.Acct.Vault.Count, Is.EqualTo(1));
            var head = AccountHead.FromJson(rig.Save.Value(Owner, "head"));
            Assert.That(head.Entries.Count, Is.EqualTo(1), "the sealed monster is its own item");
            Assert.That(rig.Save.Keys(Owner).Count, Is.EqualTo(head.Parts.Count + head.Entries.Count + 1), "nothing left over");
        }

        [Test]
        public void Rejections_ComeBackAsCodes_AndChangeNothing()
        {
            var rig = new Rig();
            var phone = rig.Device(P, "phone");
            var laptop = rig.Device(P, "laptop");
            phone.Open();
            Assert.That(laptop.Open().Code, Is.EqualTo("NotHolder"), "an open without a claim does not take the account");
            Assert.That(laptop.Open().Account, Is.Null);

            phone.Do("startRun", Rig.Db.StarterSpecies.Id);
            var snap = rig.Save.Snapshot();
            var req = phone.Request();

            var forged = phone.Request();
            forged.ToHash = new string('0', 64);
            var c = phone.Commit(forged);
            Assert.That((c.Code, c.Accepted), Is.EqualTo(("Mismatch", false)));

            var stale = phone.Request();
            stale.FromSeq = 7;
            Assert.That(phone.Commit(stale).Code, Is.EqualTo("Stale"));

            var other = phone.Request();
            other.ContentVersion = "0.0.0";
            Assert.That(phone.Commit(other).Code, Is.EqualTo("ContentStale"));

            var bad = phone.Request();
            bad.Commands.Add(new WireCommand("dismantle", 99));
            var refused = phone.Commit(bad);
            Assert.That((refused.Code, refused.Index), Is.EqualTo(("Refused", 1)));
            Assert.That(refused.Reason, Does.StartWith("dismantle"));
            Assert.That(rig.Save.Snapshot(), Is.EqualTo(snap), "no rejection wrote anything");

            Assert.That(phone.Commit(req).Accepted, Is.True);
            Assert.That(phone.Commit(req).Accepted, Is.True, "a retry of the last commit is accepted again");

            Assert.That(laptop.Open(claim: true).Accepted, Is.True, "a sign-in claims the account");
            phone.Do("drill", IdleRng.Sim.Combat.StatIds.Str);
            Assert.That(phone.Commit().Code, Is.EqualTo("NotHolder"), "the old device is signed out at its next call");
        }

        [Test]
        public void DebugCommands_OnlyForTheListedPlayers()
        {
            string book = Rig.Db.SkillBooks.Keys.First();
            var rig = new Rig("kj");
            var stranger = rig.Device(P, "phone");
            stranger.Open();
            stranger.Send("debugGrantBook", book, 1);
            Assert.That(stranger.Commit().Code, Is.EqualTo("Refused"));

            var kj = rig.Device("kj", "phone");
            kj.Open();
            kj.Do("debugGrantBook", book, 1);
            Assert.That(kj.Commit().Accepted, Is.True);
            Assert.That(AccountHead.FromJson(rig.Save.Value(CloudSaveOwner.Player("kj"), "head")).DebugUsed, Is.True);
        }

        [Test]
        public void TheDebugList_IsReadFromTheBuild()
        {
            Assert.That(ModuleSettings.ParseList(" a,b;c\n a "), Is.EquivalentTo(new[] { "a", "b", "c" }));
            Assert.That(ModuleSettings.ParseList(null), Is.Empty);
            var built = ModuleSettings.FromAssembly();
            Assert.That(built.DebugPlayers, Is.Empty, "a build without MonStackDebugPlayers lists nobody");
            var meta = typeof(ModuleSettings).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Select(a => a.Key);
            Assert.That(meta, Does.Contain(ModuleSettings.DebugPlayersKey), "the project stamps the list into the assembly");
        }

        [Test]
        public void TheBoard_IsWrittenByAcceptedClimbs_AndReadWithoutPlayerIds()
        {
            var rig = new Rig();
            var a = rig.Device(P, "phone");
            a.Open();
            for (int i = 0; i < 4; i++) a.SealOne(rig.Now);
            int season = EndlessSim.SeasonOf(AccountServer.DayOf(rig.Now));
            a.Do("setName", "Brock");
            a.Do("runEndless", season, new List<int> { 0, 1, 2, 3 });
            Assert.That(a.Commit().Accepted, Is.True);
            int cleared = a.Acct.Endless.ClearedSeason;
            Assume.That(cleared, Is.GreaterThan(0), "the starter team clears a floor");

            var mine = rig.Module.EndlessBoard(new FakeContext(P), -1, 0).GetAwaiter().GetResult();
            Assert.That((mine.Season, mine.Total, mine.Top.Count), Is.EqualTo((season, 1, 1)), "season -1 is the server's season");
            Assert.That((mine.You.Rank, mine.You.Name, mine.You.Floor, mine.You.You), Is.EqualTo((1, "Brock", cleared, true)));

            var theirs = rig.Module.EndlessBoard(new FakeContext("someone-else"), season, 5).GetAwaiter().GetResult();
            Assert.That((theirs.Top[0].Name, theirs.Top[0].You, theirs.You), Is.EqualTo(("Brock", false, (BoardRowReply)null)));
            Assert.That(JsonConvert.SerializeObject(theirs), Does.Not.Contain(P), "rows carry no player ids");
            Assert.That(rig.Save.Keys(CloudSaveOwner.Entity(EndlessBoard.BoardId(season))), Has.Count.EqualTo(1), "one shard written");
            Assert.That(rig.Module.EndlessBoard(new FakeContext(P), season + 1, 5).GetAwaiter().GetResult().Total, Is.EqualTo(0));
        }

        [Test]
        public void TheGauntletBoards_AreWrittenBySeals_OneRowPerMonster()
        {
            var rig = new Rig();
            var a = rig.Device(P, "phone");
            a.Open();
            a.Do("setName", "Misty");
            for (int i = 0; i < 2; i++) a.SealOne(rig.Now);   // commits the name with the first seal
            string sp = a.Acct.Vault[0].SpeciesId;

            var all = rig.Module.GauntletBoard(new FakeContext("someone-else"), "", 0).GetAwaiter().GetResult();
            Assert.That((all.Species, all.Total, all.Top.Count, all.You), Is.EqualTo(((string)null, 2, 2, (GauntletRowReply)null)), "a row per monster");
            Assert.That(all.Top.Select(r => (r.Name, r.Species, r.Floor, r.You)),
                Is.EquivalentTo(a.Acct.Vault.Select(e => ("Misty", e.SpeciesId, e.GauntletFloor, false))));
            Assert.That(all.Top[0].Label, Does.StartWith(all.Top[0].Cleared ? "All " : "Floor " + all.Top[0].Floor + ": "));
            Assert.That(JsonConvert.SerializeObject(all), Does.Not.Contain(P), "rows carry no player ids");

            var mine = rig.Module.GauntletBoard(new FakeContext(P), sp, 5).GetAwaiter().GetResult();
            Assert.That((mine.Species, mine.Total, mine.You.You, mine.You.Rank), Is.EqualTo((sp, a.Acct.Vault.Count(e => e.SpeciesId == sp), true, 1)));
            Assert.Throws<ArgumentException>(() => rig.Module.GauntletBoard(new FakeContext(P), "no-such-species", 5).GetAwaiter().GetResult());
        }

        [Test]
        public void Replies_AreTheJsonTheBrowserReads()
        {
            var rig = new Rig();
            var d = rig.Device(P, "phone");
            // Cloud Code serializes a reply with Newtonsoft.Json's defaults.
            var open = JObject.Parse(JsonConvert.SerializeObject(d.Open()));
            Assert.That(open.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "code", "accepted", "account", "seq", "hash", "created", "contentVersion", "serverSec", "day" }));
            Assert.That((string)open["code"], Is.EqualTo("None"));

            var refused = JObject.Parse(JsonConvert.SerializeObject(rig.Device(P, "laptop").Open()));
            Assert.That(refused.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "code", "accepted", "seq", "created", "contentVersion", "serverSec", "day" }),
                "a rejected open sends no account, no hash and never the holder");

            d.Do("startRun", Rig.Db.StarterSpecies.Id);
            var commit = JObject.Parse(JsonConvert.SerializeObject(d.Commit()));
            Assert.That(commit.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "code", "accepted", "index", "seq", "hash", "serverSec", "day" }));

            var board = JObject.Parse(JsonConvert.SerializeObject(rig.Module.EndlessBoard(new FakeContext(P), 0, 3).GetAwaiter().GetResult()));
            Assert.That(board.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "season", "total", "top", "fallbackName" }));
            var gauntlet = JObject.Parse(JsonConvert.SerializeObject(rig.Module.GauntletBoard(new FakeContext(P), "", 3).GetAwaiter().GetResult()));
            Assert.That(gauntlet.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "total", "top", "fallbackName" }), "the overall board names no species");

            // And the module reads the requests the browser sends: camelCase, commands as { k, a }.
            var req = JsonConvert.DeserializeObject<CommitRequest>("{\"deviceId\":\"phone\",\"fromSeq\":3,\"fromHash\":\"h\",\"commands\":[{\"k\":\"drill\",\"a\":[2]}],\"toHash\":\"t\",\"contentVersion\":\"v\"}");
            Assert.That((req.DeviceId, req.FromSeq, req.Commands[0].ToString(), req.ToHash, req.ContentVersion), Is.EqualTo(("phone", 3, "drill[2]", "t", "v")));
            var openReq = JsonConvert.DeserializeObject<OpenRequest>("{\"deviceId\":\"phone\",\"claim\":true}");
            Assert.That((openReq.DeviceId, openReq.Claim), Is.EqualTo(("phone", true)));
        }

        [Test]
        public void TheProfile_IsAProtectedItem_ReadByItsOwnerOrAnyone()
        {
            var rig = new Rig();
            var a = rig.Device(P, "phone");
            a.Open();
            var none = rig.Module.Profile(new FakeContext(P), "").GetAwaiter().GetResult();
            Assert.That((none.Found, none.Profile), Is.EqualTo((false, (JObject)null)), "no profile before a commit that writes one");

            a.SealOne(rig.Now);
            var mine = rig.Module.Profile(new FakeContext(P), null).GetAwaiter().GetResult();
            Assert.That(mine.Found, Is.True);
            Assert.That((string)mine.Profile["Name"], Is.EqualTo(EndlessBoard.FallbackName(P)), "no name yet: the generated one");
            Assert.That(mine.Profile["Showcase"], Is.Not.Null, "the one sealed monster");
            Assert.That((string)mine.Profile["BestGrade"], Is.EqualTo(Rig.Db.FightRanks[a.Acct.Records.BestRank].Id));

            a.Do("setName", "Misty");
            Assert.That(a.Commit().Accepted, Is.True);
            var seen = rig.Module.Profile(new FakeContext("someone-else"), P).GetAwaiter().GetResult();
            Assert.That((seen.Found, (string)seen.Profile["Name"]), Is.EqualTo((true, "Misty")), "another player reads it by id");

            Assert.That(rig.Save.Keys(CloudSaveOwner.PlayerProtected(P)), Is.EqualTo(new[] { CloudSaveProfileStore.Key }), "a protected item");
            Assert.That(rig.Save.Keys(Owner), Has.No.Member(CloudSaveProfileStore.Key), "not among the private account items");
            Assert.That(rig.Save.Keys(CloudSaveOwner.PlayerProtected("someone-else")), Is.Empty, "a read writes nothing");
            Assert.That(rig.Module.Profile(new FakeContext("someone-else"), "nobody").GetAwaiter().GetResult().Found, Is.False);
        }

        [Test]
        public void Gzip_RoundTrips()
        {
            string text = "{\"é\":\"" + new string('x', 5000) + "\"}";
            string packed = Gzip.Pack(text);
            Assert.That(Gzip.Unpack(packed), Is.EqualTo(text));
            Assert.That(packed.Length, Is.LessThan(text.Length / 10));
        }

        [Test]
        public void Warm_NeedsNoPlayer()
        {
            var rig = new Rig();
            var w = rig.Module.Warm();
            Assert.That((w.ContentVersion, w.ServerSec), Is.EqualTo((Rig.Db.Version, Rig.Start / 1000)));
            Assert.That(rig.Save.Calls, Is.Empty, "reads no data");
        }
    }
}
