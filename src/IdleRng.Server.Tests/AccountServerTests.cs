using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IdleRng.Runtime.Save;
using IdleRng.Sim.Account;
using NUnit.Framework;

namespace IdleRng.Server.Tests
{
    /// <summary>The account server core (specs/27 sections 4 to 6): open, commit, every rejection, storage and the rules.</summary>
    public class AccountServerTests
    {
        static Sim.Content.ContentDb Db => Content.Db;
        const string P = Rig.Player;

        // ------------------------------------------------------------------ open

        [Test]
        public void Genesis_CreatesTheAccount_WithAServerSeed_HeldByTheOpeningDevice()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            var r = a.Open();
            Assert.That(r.Accepted && r.Created, Is.True);
            Assert.That(r.Seq, Is.EqualTo(0));
            Assert.That(r.ContentVersion, Is.EqualTo(Db.Version));
            Assert.That(r.Day, Is.EqualTo(20391));
            Assert.That(r.Hash, Is.EqualTo(a.Hash()), "the client computes the server's hash from the account it was given");
            Assert.That(a.Acct.Seed, Is.Not.EqualTo(0UL), "the server drew the seed");
            Assert.That(a.Acct.OwnedSpecies, Is.EqualTo(new[] { Db.StarterSpecies.Id }));
            Assert.That(rig.Store.Head(P).Holder.DeviceId, Is.EqualTo("a"));
            Assert.That(rig.Store.Keys(P), Is.EqualTo(rig.Store.Referenced(P)), "head plus the six parts");
            Assert.That(rig.Store.Keys(P).Count, Is.EqualTo(7));

            var again = a.Open();
            Assert.That(again.Created, Is.False);
            Assert.That(again.Hash, Is.EqualTo(r.Hash));
            Assert.That(a.Acct.Seed, Is.EqualTo(AccountSave.Decode(Db, System.Text.Encoding.UTF8.GetBytes(r.Account)).Seed));

            var other = rig.Device("a");
            other.Player = "player-2";
            other.Open();
            Assert.That(other.Acct.Seed, Is.Not.EqualTo(a.Acct.Seed), "each account gets its own seed");
        }

        [Test]
        public void Open_RequiresAPlayerAndADevice()
        {
            var rig = new Rig();
            Assert.ThrowsAsync<ArgumentException>(() => rig.Server.OpenAsync("", new OpenRequest { DeviceId = "a" }));
            Assert.ThrowsAsync<ArgumentException>(() => rig.Server.OpenAsync(P, new OpenRequest()));
            Assert.ThrowsAsync<ArgumentException>(() => rig.Server.OpenAsync(P, null));
            Assert.ThrowsAsync<ArgumentException>(() => rig.Server.OpenAsync(P, new OpenRequest { DeviceId = new string('x', 129) }));
        }

        [Test]
        public void Genesis_LostRace_OpensTheWinnersAccount_AndDeletesItsOwnItems()
        {
            var rig = new Rig();
            var winner = new AccountServer(Db, rig.Store.Inner, new ServerConfig { NowMs = () => rig.Now, Random = () => 999 });
            rig.Store.BeforeHeadWrite = async () => await winner.OpenAsync(P, new OpenRequest { DeviceId = "a" });
            var a = rig.Device("a");
            var r = a.Open();
            Assert.That(r.Accepted, Is.True);
            Assert.That(r.Created, Is.False, "the other call created it");
            Assert.That(a.Acct.Seed, Is.EqualTo(999UL));
            Assert.That(rig.Store.Keys(P), Is.EqualTo(rig.Store.Referenced(P)), "the loser's parts are gone");
        }

        // ------------------------------------------------------------------ commits

        [Test]
        public void AWholeRun_CommittedAtCheckpoints_LeavesTheServerWithTheClientsAccount()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            a.SealOne(rig.Now);
            Assert.That(a.Acct.Vault.Count, Is.EqualTo(1));
            Assert.That(a.BaseSeq, Is.GreaterThan(2), "startRun, mid-run checkpoints and finishRun");

            var b = rig.Device("a");
            var r = b.Open();
            Assert.That(r.Seq, Is.EqualTo(a.BaseSeq));
            Assert.That(b.Hash(), Is.EqualTo(a.Hash()));
            Assert.That(r.Hash, Is.EqualTo(a.Hash()));
            Assert.That(AccountParts.Split(b.Acct), Is.EqualTo(AccountParts.Split(a.Acct)), "item by item");
            Assert.That(rig.Store.Keys(P), Is.EqualTo(rig.Store.Referenced(P)), "nothing superseded is left");
            Assert.That(rig.Store.Head(P).Entries.Count, Is.EqualTo(1));
        }

        [Test]
        public void AMidRunCheckpoint_ReadsAndWritesOnlyTheTrainingPart()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            a.Do("startRun", Db.StarterSpecies.Id);
            Assert.That(a.Commit().Accepted, Is.True);
            var before = rig.Store.Head(P);
            rig.Store.ClearLog();

            a.Do("drill", Sim.Combat.StatIds.Str);
            a.Do("rest");
            Assert.That(a.Commit().Accepted, Is.True);
            var after = rig.Store.Head(P);
            Assert.That(rig.Store.Read, Is.EqualTo(new[] { before.Parts["training"] }), "only the training part is loaded");
            Assert.That(rig.Store.Written, Is.EqualTo(new[] { after.Parts["training"] }));
            Assert.That(rig.Store.HeadWrites, Is.EqualTo(1));
            Assert.That(after.Parts.Where(kv => kv.Key != "training"), Is.EqualTo(before.Parts.Where(kv => kv.Key != "training")));
            Assert.That(rig.Store.Keys(P), Does.Not.Contain(before.Parts["training"]), "the superseded version is deleted");
            Assert.That(rig.Store.Keys(P), Is.EqualTo(rig.Store.Referenced(P)));
        }

        [Test]
        public void Dismantle_DeletesTheEntryItem_AndLockRewritesOnlyTheIndex()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            a.SealOne(rig.Now);
            a.SealOne(rig.Now);
            var head = rig.Store.Head(P);
            Assert.That(head.Entries.Keys, Is.EqualTo(new[] { 0, 1 }), "entries are named by the run that made them");

            rig.Store.ClearLog();
            a.Do("lock", 0, true);
            Assert.That(a.Commit().Accepted, Is.True);
            var locked = rig.Store.Head(P);
            Assert.That(rig.Store.Written, Is.EqualTo(new[] { locked.Parts["vault"] }), "a lock rewrites only the index");
            Assert.That(locked.Entries, Is.EqualTo(head.Entries), "the entries' items are untouched");

            a.Do("dismantle", 1);
            Assert.That(a.Commit().Accepted, Is.True);
            var after = rig.Store.Head(P);
            Assert.That(after.Entries.Keys, Is.EqualTo(new[] { 0 }));
            Assert.That(rig.Store.Keys(P), Does.Not.Contain(head.Entries[1]), "the dismantled monster's item is deleted");
            Assert.That(rig.Store.Keys(P), Is.EqualTo(rig.Store.Referenced(P)));
        }

        [Test]
        public void ARetryOfTheLastCommit_IsAcceptedAgain_WithoutApplyingIt()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            a.Do("startRun", Db.StarterSpecies.Id);
            var req = a.Request();
            var first = a.Commit(req);
            Assert.That(first.Accepted, Is.True);
            var keys = rig.Store.Keys(P);
            rig.Store.ClearLog();

            var retry = a.Commit(req);   // the answer to the first was lost
            Assert.That(retry.Accepted, Is.True);
            Assert.That(retry.Seq, Is.EqualTo(first.Seq));
            Assert.That(retry.Hash, Is.EqualTo(first.Hash));
            Assert.That(rig.Store.Written, Is.Empty);
            Assert.That(rig.Store.HeadWrites, Is.EqualTo(0));
            Assert.That(rig.Store.Keys(P), Is.EqualTo(keys));

            a.Do("rest");
            Assert.That(a.Commit().Accepted, Is.True);
            var old = a.Commit(req);
            Assert.That(old.Code, Is.EqualTo(Rejection.Stale), "only the last commit is recognised as a retry");
        }

        // ------------------------------------------------------------------ rejections

        [Test]
        public void Stale_WhenTheServerIsPastTheBase_OrTheHashDiffers_OrThereIsNoAccount()
        {
            var rig = new Rig();
            var none = new CommitRequest { DeviceId = "a", FromHash = "x", ToHash = "y", ContentVersion = Db.Version, Commands = { new WireCommand("rest") } };
            Assert.That(rig.Server.CommitAsync(P, none).Result.Code, Is.EqualTo(Rejection.Stale), "no account yet");

            var a = rig.Device("a");
            a.Open();
            var tab2 = rig.Device("a");   // a second tab of the same device
            tab2.Open();
            a.Do("startRun", Db.StarterSpecies.Id);
            Assert.That(a.Commit().Accepted, Is.True);
            tab2.Do("setLoadout", new List<string>());
            var r = tab2.Commit();
            Assert.That(r.Code, Is.EqualTo(Rejection.Stale));
            Assert.That(r.Index, Is.EqualTo(-1));

            a.Do("rest");
            var forged = a.Request();
            forged.FromHash = new string('0', 64);
            AssertUnchanged(rig, () => Assert.That(a.Commit(forged).Code, Is.EqualTo(Rejection.Stale)));
        }

        [Test]
        public void ContentStale_WhenTheClientRunsOtherContent()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            a.Do("setLoadout", new List<string>());
            var req = a.Request();
            req.ContentVersion = "0.0.1";
            AssertUnchanged(rig, () => Assert.That(a.Commit(req).Code, Is.EqualTo(Rejection.ContentStale)));
        }

        [Test]
        public void Mismatch_WhenTheClientsResultDiffers_ForgedShardsAreNotKept()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            a.Do("setLoadout", new List<string>());
            a.Acct.Shards += 1_000_000;   // an edited save
            var r = default(CommitResult);
            AssertUnchanged(rig, () => r = a.Commit());
            Assert.That(r.Code, Is.EqualTo(Rejection.Mismatch));
            Assert.That(r.Hash, Is.Not.EqualTo(a.Hash()));
            a.Acct.Shards -= 1_000_000;
            Assert.That(r.Hash, Is.EqualTo(a.Hash()), "the server reports the hash it computed");
        }

        [Test]
        public void Refused_ForAnIllegalOrMalformedCommand_WithItsIndex()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();

            void Refused(int index, params WireCommand[] cmds)
            {
                var req = a.Request();
                req.Commands = cmds.ToList();
                CommitResult r = null;
                AssertUnchanged(rig, () => r = a.Commit(req));
                Assert.That(r.Code, Is.EqualTo(Rejection.Refused), string.Join(" ", cmds.Select(c => c?.ToString())));
                Assert.That(r.Index, Is.EqualTo(index), r.Reason);
                Assert.That(r.Reason, Is.Not.Empty);
            }

            var ok = new WireCommand("setLoadout", new List<string>());
            Refused(-1);                                                            // no commands
            Refused(1, ok, new WireCommand("drill", "str"));                        // no run in progress
            Refused(1, ok, new WireCommand("gauntlet"));                            // a read, not a command
            Refused(0, new WireCommand("startRun", 3));                             // a number where a species id goes
            Refused(0, new WireCommand("startRun"));                                // argument missing
            Refused(0, new WireCommand("startRun", "no-such-species"));             // not owned
            Refused(0, new WireCommand { K = "lock", A = null });                   // arguments missing
            Refused(0, (WireCommand)null);
            Refused(2, ok, ok, new WireCommand("dismantle", 0));                    // no such Vault entry

            var req2 = a.Request();
            req2.Commands = null;
            Assert.That(a.Commit(req2).Code, Is.EqualTo(Rejection.Refused));
            var req3 = a.Request();
            req3.DeviceId = null;
            Assert.That(a.Commit(req3).Code, Is.EqualTo(Rejection.Refused));
        }

        [Test]
        public void Refused_AboveTheCommandCap_OrForASecondFinishRun()
        {
            var rig = new Rig();
            rig.Config.CommitMaxCommands = 3;
            var a = rig.Device("a");
            a.Open();
            var ok = new WireCommand("setLoadout", new List<string>());
            var req = a.Request();
            req.Commands = new List<WireCommand> { ok, ok, ok, ok };
            var r = a.Commit(req);
            Assert.That(r.Code, Is.EqualTo(Rejection.Refused));
            Assert.That(r.Index, Is.EqualTo(3));

            req.Commands = new List<WireCommand> { new WireCommand("finishRun", 0), new WireCommand("finishRun", 0) };
            r = a.Commit(req);
            Assert.That(r.Code, Is.EqualTo(Rejection.Refused));
            Assert.That(r.Index, Is.EqualTo(1), "screened before anything is applied");
        }

        // ------------------------------------------------------------------ the holder rule (section 6.1)

        [Test]
        public void OneHolder_ASecondDeviceMustClaim_AndThenTheFirstIsOut()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            a.Do("startRun", Db.StarterSpecies.Id);
            Assert.That(a.Commit().Accepted, Is.True);

            var b = rig.Device("b");
            Assert.That(b.Open().Code, Is.EqualTo(Rejection.NotHolder), "an ordinary page open does not take the account");
            Assert.That(b.Acct, Is.Null);
            var claimed = b.Open(claim: true);
            Assert.That(claimed.Accepted, Is.True);
            Assert.That(b.Hash(), Is.EqualTo(a.Hash()), "the run in progress moves to the new device");
            Assert.That(rig.Store.Head(P).Holder.DeviceId, Is.EqualTo("b"));
            Assert.That(claimed.Seq, Is.EqualTo(a.BaseSeq), "a claim is not a commit");

            a.Do("rest");
            Assert.That(a.Commit().Code, Is.EqualTo(Rejection.NotHolder), "commands played after the takeover are not applied");
            Assert.That(a.Open().Code, Is.EqualTo(Rejection.NotHolder), "the first device cannot take it back without claiming");

            b.Do("rest");
            Assert.That(b.Commit().Accepted, Is.True);
            Assert.That(a.Open(claim: true).Accepted, Is.True, "signing in again takes it back");
        }

        // ------------------------------------------------------------------ inputs the client supplies (section 6.2)

        [Test]
        public void SealTime_IsRefusedPastTheServersTimePlusTheSlack()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            a.Do("startRun", Db.StarterSpecies.Id);
            a.PlayOut();
            if (a.Pending.Count > 0) Assert.That(a.Commit().Accepted, Is.True);
            long slack = rig.Config.ClockSlackSec * 1000L;

            var late = rig.Device("a");
            late.Open();
            late.Do("finishRun", rig.Now + slack + 1);
            var r = late.Commit();
            Assert.That(r.Code, Is.EqualTo(Rejection.Refused));
            Assert.That(r.Index, Is.EqualTo(0));

            late.Open();
            late.Do("finishRun", rig.Now + slack);
            Assert.That(late.Commit().Accepted, Is.True, "up to the slack is accepted (display only)");
        }

        [Test]
        public void DungeonDay_MustBeTheServersDayOrTheDayBefore()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            a.SealOne(rig.Now);
            int today = AccountServer.DayOf(rig.Now);
            Assert.That(today, Is.EqualTo(20391));

            foreach (var day in new[] { today + 1, today - 2, 0 })
            {
                a.Open();
                a.Send("runTower", day, new List<int> { 0 });
                var r = a.Commit();
                Assert.That(r.Code, Is.EqualTo(Rejection.Refused), "day " + day);
                Assert.That(r.Reason, Does.Contain("day"));
            }

            a.Open();
            a.Do("runTower", today - 1, new List<int> { 0 });   // a commit landing just after midnight
            Assert.That(a.Commit().Accepted, Is.True);
            a.Do("runTower", today, new List<int> { 0 });
            Assert.That(a.Commit().Accepted, Is.True);
            var head = rig.Store.Head(P);
            Assert.That(rig.Store.Keys(P), Is.EqualTo(rig.Store.Referenced(P)));
            Assert.That(head.Entries.Count, Is.EqualTo(1), "a Dungeon attempt stores no entry");
        }

        // ------------------------------------------------------------------ debug commands (section 6.3)

        [Test]
        public void DebugCommands_OnlyForListedPlayers_AndTheAccountIsMarkedForGood()
        {
            string book = Db.SkillBooks.Keys.First();

            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            a.Send("debugGrantBook", book, 1);
            var r = a.Commit();
            Assert.That(r.Code, Is.EqualTo(Rejection.Refused));
            Assert.That(r.Reason, Does.Contain("debug"));
            Assert.That(rig.Store.Head(P).DebugUsed, Is.False);

            var dev = new Rig(P);
            var d = dev.Device("a");
            d.Open();
            d.Do("debugGrantBook", book, 2);
            Assert.That(d.Commit().Accepted, Is.True);
            Assert.That(d.Acct.BookCount(book), Is.EqualTo(2));
            d.Do("setLoadout", new List<string>());
            Assert.That(d.Commit().Accepted, Is.True);
            Assert.That(dev.Store.Head(P).DebugUsed, Is.True, "it stays marked after later commits");
        }

        // ------------------------------------------------------------------ copy-on-write

        [Test]
        public void ACrashBeforeTheHeadSwap_LeavesTheAccountWhole_AndTheNextOpenDeletesTheLeftovers()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            a.SealOne(rig.Now);
            var head = rig.Store.Head(P);
            a.Do("dismantle", 0);
            a.Do("startRun", Db.StarterSpecies.Id);
            rig.Store.FailNextHeadWrite = true;
            Assert.ThrowsAsync<InvalidOperationException>(() => rig.Server.CommitAsync(P, a.Request()));
            Assert.That(rig.Store.Head(P).ToJson(), Is.EqualTo(head.ToJson()), "the commit did not land");
            Assert.That(rig.Store.Keys(P), Is.SupersetOf(rig.Store.Referenced(P)));
            Assert.That(rig.Store.Keys(P).Count, Is.GreaterThan(rig.Store.Referenced(P).Count), "its new items are left over");

            Assert.That(a.Commit().Accepted, Is.True, "the retry lands");
            var b = rig.Device("a");
            Assert.That(b.Open().Accepted, Is.True);
            Assert.That(rig.Store.Keys(P), Is.EqualTo(rig.Store.Referenced(P)), "the open swept the crashed attempt's items");
            Assert.That(b.Hash(), Is.EqualTo(a.Hash()));
        }

        [Test]
        public void ASwapThatLosesTheRace_IsStale_AndDeletesWhatItWrote()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            a.Do("startRun", Db.StarterSpecies.Id);
            // Another commit lands between this one's item writes and its head swap (here: the head rewritten, so its lock moves).
            rig.Store.BeforeHeadWrite = async () =>
            {
                var h = await rig.Store.Inner.ReadHeadAsync(P);
                await rig.Store.Inner.WriteHeadAsync(P, h.Json, h.WriteLock);
            };
            var r = a.Commit();
            Assert.That(r.Code, Is.EqualTo(Rejection.Stale));
            Assert.That(rig.Store.Keys(P), Is.EqualTo(rig.Store.Referenced(P)));
        }

        [Test]
        public void ACommitLandingWhileAnotherCallLoads_MakesTheOpenRetry_AndTheCommitStale()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            a.Do("startRun", Db.StarterSpecies.Id);
            Assert.That(a.Commit().Accepted, Is.True);
            var tab = rig.Device("a");
            tab.Open();

            // The other tab's commit lands after this open read the head and before it loads the parts it names.
            tab.Do("rest");
            rig.Store.BeforeRead = () => Assert.That(tab.Commit().Accepted, Is.True);
            var r = a.Open();
            Assert.That(r.Accepted, Is.True);
            Assert.That(r.Seq, Is.EqualTo(tab.BaseSeq), "it opened the new head");
            Assert.That(a.Hash(), Is.EqualTo(tab.Hash()));

            tab.Do("rest");
            rig.Store.BeforeRead = () => Assert.That(tab.Commit().Accepted, Is.True);
            a.Do("drill", Sim.Combat.StatIds.Str);
            Assert.That(a.Commit().Code, Is.EqualTo(Rejection.Stale));
            Assert.That(rig.Store.Keys(P), Is.EqualTo(rig.Store.Referenced(P)));
        }

        [Test]
        public void Sweep_LeavesItemsOfLaterCommits_AndKeysItDidNotWrite()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            var later = AccountHead.ItemKey("training", 5, "abcd0123");   // a commit in flight from a newer base
            rig.Store.Inner.WriteAsync(P, new Dictionary<string, string> { [later] = "{}", ["profile"] = "{}", ["training_0_dead0000"] = "{}" }).Wait();
            a.Open();
            Assert.That(rig.Store.Keys(P), Does.Contain(later).And.Contain("profile"));
            Assert.That(rig.Store.Keys(P), Does.Not.Contain("training_0_dead0000"));
        }

        [Test]
        public void ADamagedItem_IsReported_NotLoaded()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            var head = rig.Store.Head(P);
            rig.Store.Inner.WriteAsync(P, new Dictionary<string, string> { [head.Parts["wallet"]] = "{\"Shards\":99999}" }).Wait();
            Assert.ThrowsAsync<AccountDamagedException>(() => rig.Server.OpenAsync(P, new OpenRequest { DeviceId = "a" }));
            rig.Store.Inner.DeleteAsync(P, new[] { head.Parts["training"] }).Wait();
            a.Send("setLoadout", new List<string>());
            Assert.ThrowsAsync<AccountDamagedException>(() => rig.Server.CommitAsync(P, a.Request()));
        }

        [Test]
        public void AfterAContentChange_TheNextCommitRewritesEveryPartUnderTheNewVersion()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            a.SealOne(rig.Now);
            var h = rig.Store.Head(P);
            h.Content = "0.0.1";   // written by an older module
            var stored = rig.Store.Inner.ReadHeadAsync(P).Result;
            rig.Store.Inner.WriteHeadAsync(P, h.ToJson(), stored.WriteLock).Wait();

            Assert.That(a.Open().Accepted, Is.True, "an open loads it under the server's content");
            rig.Store.ClearLog();
            a.Do("setLoadout", new List<string>());
            Assert.That(a.Commit().Accepted, Is.True);
            Assert.That(rig.Store.Read.Count, Is.EqualTo(7), "every part and the entry are loaded");
            Assert.That(rig.Store.Head(P).Content, Is.EqualTo(Db.Version));
        }

        [Test]
        public void EveryAccountCommand_InItsOwnCommit_LoadingOnlyTheParts_ItDeclares()
        {
            var rig = new Rig(P);
            var a = rig.Device("a");
            a.Open();
            string book = Db.SkillBooks.Keys.First();
            var seen = new HashSet<string>();
            void One(string k, params object[] args)
            {
                a.Do(k, args);
                var r = a.Commit();
                Assert.That(r.Accepted, Is.True, k + ": " + r.Code + " " + r.Reason);
                seen.Add(k);
            }

            One("debugGrantBook", book, 2);
            One("startRun", Db.StarterSpecies.Id, new List<string> { book });
            a.PlayOut(every: 10);
            if (a.Pending.Count > 0) Assert.That(a.Commit().Accepted, Is.True);
            One("finishRun", rig.Now);
            for (int i = 0; i < a.Acct.Vendor.Offers.Count; i++)
                if (AccountOps.OfferOpen(Db, a.Acct, a.Acct.Vendor.Offers[i]) && AccountOps.OfferPrice(Db, a.Acct, a.Acct.Vendor.Offers[i]) <= a.Acct.Shards) { One("buyOffer", i); break; }
            if (AccountOps.CanRerollVendor(Db, a.Acct)) One("rerollVendor");
            One("closeVendor");
            One("setLoadout", new List<string>());
            One("lock", 0, true);
            One("runTower", AccountServer.DayOf(rig.Now), new List<int> { 0 });
            One("lock", 0, false);
            One("startRun", Db.StarterSpecies.Id);
            One("abandon");
            One("dismantle", 0);

            Assert.That(seen, Is.SupersetOf(new[] { "debugGrantBook", "startRun", "finishRun", "buyOffer", "rerollVendor", "closeVendor", "setLoadout", "lock", "runTower", "abandon", "dismantle" }));
            var b = rig.Device("a");
            b.Open();
            Assert.That(AccountParts.Split(b.Acct), Is.EqualTo(AccountParts.Split(a.Acct)), "the server holds the client's account, item by item");
            Assert.That(rig.Store.Keys(P), Is.EqualTo(rig.Store.Referenced(P)));
        }

        [Test]
        public void Arguments_ReadByTheBridgesRules()
        {
            var args = new JsonArgs(Newtonsoft.Json.Linq.JArray.Parse("[null, 3, 2.5, \"x\", true, [1, 2], [\"a\"], [1.5], [4294967296]]"));
            Assert.That(args.Has(0), Is.False, "null reads as absent");
            Assert.That(args.Has(9), Is.False, "past the end reads as absent");
            Assert.That(args.Num(1), Is.EqualTo(3));
            Assert.That(args.Num(2), Is.EqualTo(2.5));
            Assert.That(args.Str(3), Is.EqualTo("x"));
            Assert.That(args.Bool(4), Is.True);
            Assert.That(args.IntList(5), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(args.StrList(6), Is.EqualTo(new[] { "a" }));
            Assert.Throws<ArgumentException>(() => args.Str(1), "a number is not a string");
            Assert.Throws<ArgumentException>(() => args.Num(3), "a string is not a number");
            Assert.Throws<ArgumentException>(() => args.Bool(1));
            Assert.Throws<ArgumentException>(() => args.IntList(7), "not a whole number");
            Assert.Throws<ArgumentException>(() => args.IntList(8), "out of range");
            Assert.Throws<ArgumentException>(() => args.StrList(5));
            Assert.Throws<ArgumentException>(() => args.StrList(0), "missing");
            Assert.That(new JsonArgs(null).Has(0), Is.False);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Runs a rejected call and checks the store did not change.</summary>
        static void AssertUnchanged(Rig rig, Action act)
        {
            var head = rig.Store.Inner.ReadHeadAsync(P).Result?.Json;
            var keys = rig.Store.Keys(P);
            act();
            Assert.That(rig.Store.Inner.ReadHeadAsync(P).Result?.Json, Is.EqualTo(head), "the head is unchanged");
            Assert.That(rig.Store.Keys(P), Is.EqualTo(keys), "no item was left");
        }
    }
}
