using System;
using System.Linq;
using System.Text;
using IdleRng.Runtime.Save;
using IdleRng.Sim.Account;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Runtime.Tests
{
    /// <summary>The account save lineage (specs/18 section 6): round trip, active run by replay, fallback, rejection.</summary>
    public class AccountSaveTests
    {
        static AccountState Played()
        {
            var db = TestContent.Db;
            var a = AccountOps.New(db, 77);
            foreach (var sp in db.SpeciesList) AccountOps.UnlockSpecies(db, a, sp.Id);
            a.Shards = 500;
            a.Talents[db.TalentList[0].Id] = 1;
            AccountOps.GrantBooks(db, a, "book_echoes", 2);

            var first = AccountOps.StartRun(db, a, "drake");
            while (first.Phase != RunPhase.Finished)
            {
                while (first.Offers.Count > 0) TrainingRun.DiscardOffer(db, first);
                if (first.Phase == RunPhase.AwaitFight) TrainingRun.Fight(db, first);
                else TrainingRun.Rest(db, first);
            }
            AccountOps.FinishRun(db, a, 1000);

            var run = AccountOps.StartRun(db, a, "sprite", new[] { "book_echoes", "book_echoes" });
            TrainingRun.UseBook(db, run, "book_echoes");
            TrainingRun.TakeTree(db, run, 0, 2);
            TrainingRun.Drill(db, run, StatIds.Int);
            TrainingRun.Rest(db, run);
            return a;
        }

        [Test]
        public void RoundTrip_KeepsEverything_AndRebuildsTheActiveRun()
        {
            var a = Played();
            var b = AccountSave.Decode(TestContent.Db, AccountSave.Encode(a));
            Assert.That(b.Seed, Is.EqualTo(a.Seed));
            Assert.That(b.RunsStarted, Is.EqualTo(a.RunsStarted));
            Assert.That(b.Shards, Is.EqualTo(a.Shards));
            Assert.That(b.OwnedSpecies, Is.EqualTo(a.OwnedSpecies));
            Assert.That(b.Books, Is.EqualTo(a.Books));
            Assert.That(b.Talents, Is.EqualTo(a.Talents));
            Assert.That(b.Vault.Count, Is.EqualTo(1));
            Assert.That(b.Vault[0].RunHash, Is.EqualTo(a.Vault[0].RunHash));
            Assert.That(b.Vault[0].Log.Count, Is.EqualTo(a.Vault[0].Log.Count));
            Assert.That(b.Active, Is.Not.Null);
            Assert.That(TrainingRun.StateHash(b.Active), Is.EqualTo(TrainingRun.StateHash(a.Active)));
            Assert.That(Encoding.UTF8.GetString(AccountSave.Encode(b)), Is.EqualTo(Encoding.UTF8.GetString(AccountSave.Encode(a))),
                "encode(decode(x)) is stable");
        }

        [Test]
        public void VaultLock_RoundTrips()
        {
            var a = Played();
            AccountOps.SetLocked(TestContent.Db, a, 0, true);
            var b = AccountSave.Decode(TestContent.Db, AccountSave.Encode(a));
            Assert.That(b.Vault[0].Locked, Is.True);
        }

        [Test]
        public void V1Save_MigratesToV2_EveryEntryUnlocked()
        {
            // A v1 body as the v1 code wrote it: no Locked on Vault entries.
            var a = Played();
            var body = Newtonsoft.Json.Linq.JObject.Parse(SaveBody(AccountSave.Encode(a)));
            body["SchemaVersion"] = 1;
            body["Account"]["SchemaVersion"] = 1;
            foreach (var e in body["Account"]["Vault"]) ((Newtonsoft.Json.Linq.JObject)e).Remove("Locked");
            var b = AccountSave.Decode(TestContent.Db, SaveSerializer.EncodeBody(body.ToString()));
            Assert.That(b.SchemaVersion, Is.EqualTo(AccountState.CurrentSchemaVersion));
            Assert.That(b.Vault.Count, Is.EqualTo(1));
            Assert.That(b.Vault[0].Locked, Is.False);
            Assert.That(b.Vault[0].RunHash, Is.EqualTo(a.Vault[0].RunHash));
            Assert.That(TrainingRun.StateHash(b.Active), Is.EqualTo(TrainingRun.StateHash(a.Active)), "the active run still replays");
            Assert.That(SaveBody(AccountSave.Encode(b)), Does.Contain("\"SchemaVersion\": 3"), "it saves back as the current version");
        }

        /// <summary>A save body as the v2 code wrote it: no achievements, records or vendor; every species owned.</summary>
        static Newtonsoft.Json.Linq.JObject V2Body(AccountState a)
        {
            var body = Newtonsoft.Json.Linq.JObject.Parse(SaveBody(AccountSave.Encode(a)));
            body["SchemaVersion"] = 2;
            var acct = (Newtonsoft.Json.Linq.JObject)body["Account"];
            acct["SchemaVersion"] = 2;
            acct.Remove("Achievements");
            acct.Remove("Records");
            acct.Remove("Vendor");
            return body;
        }

        [Test]
        public void V2Save_MigratesToV3_KeepsSpeciesAndTalents_RebuildsRecordsFromTheVault()
        {
            var db = TestContent.Db;
            var a = Played();
            var b = AccountSave.Decode(db, SaveSerializer.EncodeBody(V2Body(a).ToString()));
            Assert.That(b.SchemaVersion, Is.EqualTo(AccountState.CurrentSchemaVersion));
            Assert.That(b.OwnedSpecies, Is.EqualTo(db.SpeciesList.Select(s => s.Id)), "a pre-T9 account keeps every species it owned");
            Assert.That(b.Talents, Is.EqualTo(a.Talents));
            Assert.That(b.Vendor, Is.Null);
            Assert.That(b.Records.RunsFinished, Is.EqualTo(b.Vault.Count));
            Assert.That(b.Records.BestRank, Is.EqualTo(b.Vault[0].RankIndex));
            foreach (var d in db.AchievementList)
                Assert.That(b.Achievements.Contains(d.Id), Is.EqualTo(AccountOps.AchievementHolds(db, b, d)), d.Id + ": earned from the rebuilt records");
            Assert.That(TrainingRun.StateHash(b.Active), Is.EqualTo(TrainingRun.StateHash(a.Active)), "the active run still replays");
        }

        [Test]
        public void AchievementsRecordsAndVendor_RoundTrip()
        {
            var db = TestContent.Db;
            var a = AccountOps.New(db, 12);
            var run = AccountOps.StartRun(db, a, "slime");
            while (run.Phase != RunPhase.Finished)
            {
                while (run.Offers.Count > 0) TrainingRun.DiscardOffer(db, run);
                if (run.Phase == RunPhase.AwaitFight) TrainingRun.Fight(db, run);
                else TrainingRun.Rest(db, run);
            }
            AccountOps.FinishRun(db, a, 5);
            a.Shards = 100000;
            AccountOps.BuyOffer(db, a, 1);
            a.Achievements.Add("veteran");   // earned out of order, kept as saved
            var b = AccountSave.Decode(db, AccountSave.Encode(a));
            Assert.That(b.Achievements, Is.EqualTo(a.Achievements));
            Assert.That(b.Records.RunsFinished, Is.EqualTo(1));
            Assert.That(b.Records.BestStats, Is.EqualTo(a.Records.BestStats));
            Assert.That(b.Vendor.Grade, Is.EqualTo(a.Vendor.Grade));
            Assert.That(b.Vendor.Offers.Select(o => o.Talent + "/" + o.Rarity + "/" + o.Bought), Is.EqualTo(a.Vendor.Offers.Select(o => o.Talent + "/" + o.Rarity + "/" + o.Bought)));
            Assert.That(b.Vendor.Offers[1].Bought, Is.True);
            Assert.That(b.OwnedSpecies, Is.EqualTo(a.OwnedSpecies));
        }

        static string SaveBody(byte[] file)
        {
            string text = Encoding.UTF8.GetString(file);
            return text.Substring(text.IndexOf('\n') + 1);
        }

        [Test]
        public void NoActiveRun_RoundTrips()
        {
            var a = AccountOps.New(TestContent.Db, 3);
            var b = AccountSave.Decode(TestContent.Db, AccountSave.Encode(a));
            Assert.That(b.Active, Is.Null);
            Assert.That(b.OwnedSpecies, Is.EqualTo(a.OwnedSpecies));
        }

        [Test]
        public void Load_FallsBackToBackup_AndQuarantinesTheBadPrimary()
        {
            var store = new MemorySaveStore();
            Assert.That(AccountSave.Load(TestContent.Db, store, 0), Is.Null, "no account yet");
            var a = Played();
            AccountSave.Write(store, a);
            a.Shards += 1;
            AccountSave.Write(store, a);
            store.Primary[store.Primary.Length - 2] ^= 0x20;   // damage the newest file's body
            var b = AccountSave.Load(TestContent.Db, store, 0);
            Assert.That(b.Shards, Is.EqualTo(a.Shards - 1), "the previous save");
            Assert.That(store.Quarantined.Count, Is.EqualTo(1));
        }

        [Test]
        public void Rejects_WrongKind_NewerSchema_AndDamage()
        {
            var db = TestContent.Db;
            Assert.Throws<SaveInvalidException>(() => AccountSave.Decode(db, SaveSerializer.EncodeBody("{\"Kind\":\"other\",\"SchemaVersion\":1}")));
            Assert.Throws<SaveInvalidException>(() => AccountSave.Decode(db, SaveSerializer.EncodeBody("{\"Kind\":\"monster_account\",\"SchemaVersion\":99,\"Account\":{}}")));
            Assert.Throws<SaveCorruptException>(() => AccountSave.Decode(db, Encoding.UTF8.GetBytes("garbage")));
        }

        static byte[] WithTamperedRecipe(AccountState a, Func<string, string> edit)
        {
            string json = Encoding.UTF8.GetString(AccountSave.Encode(a));
            return SaveSerializer.EncodeBody(edit(json.Substring(json.IndexOf('\n') + 1)));
        }

        [Test]
        public void ARunThatNoLongerReplays_IsDropped_AndTheRestOfTheAccountLoads()
        {
            var db = TestContent.Db;
            var a = Played();
            int unusedBefore = a.Active.Books.Count;
            var file = WithTamperedRecipe(a, body =>
            {
                int i = body.IndexOf("\"Hash\": \"", StringComparison.Ordinal) + 9;
                return body.Substring(0, i) + (body[i] == '0' ? '1' : '0') + body.Substring(i + 1);
            });
            var b = AccountSave.Decode(db, file, out var notice);
            Assert.That(b.Active, Is.Null, "the stale run is dropped");
            Assert.That(notice, Does.Contain("older rules"));
            Assert.That(b.Shards, Is.EqualTo(a.Shards));
            Assert.That(b.Vault.Count, Is.EqualTo(a.Vault.Count), "the Vault is untouched");
            Assert.That(b.Talents, Is.EqualTo(a.Talents));
            Assert.That(b.BookCount("book_echoes"), Is.EqualTo(a.BookCount("book_echoes") + 2), "both books brought into the run come back");
            Assert.That(b.RunsStarted, Is.EqualTo(a.RunsStarted), "the run stays counted, so the next run gets a new seed");
        }

        [Test]
        public void ARunOfARemovedSpecies_IsDropped_NotFatal()
        {
            var a = Played();
            var file = WithTamperedRecipe(a, body =>
            {
                int i = body.IndexOf("\"SpeciesId\": \"sprite\"", StringComparison.Ordinal);
                Assert.That(i, Is.GreaterThan(0), "the active run is the sprite");
                return body.Replace("\"SpeciesId\": \"sprite\"", "\"SpeciesId\": \"gone\"");
            });
            var b = AccountSave.Decode(TestContent.Db, file, out var notice);
            Assert.That(b.Active, Is.Null);
            Assert.That(notice, Is.Not.Null);
            Assert.That(b.Vault.Count, Is.EqualTo(1));
        }

        [Test]
        public void ACleanLoad_HasNoNotice_AndTheStaleAccountIsPersistedOnceDropped()
        {
            var a = Played();
            AccountSave.Decode(TestContent.Db, AccountSave.Encode(a), out var clean);
            Assert.That(clean, Is.Null);
            var dropped = AccountSave.Decode(TestContent.Db, WithTamperedRecipe(a, body => body.Replace("\"SpeciesId\": \"sprite\"", "\"SpeciesId\": \"gone\"")));
            var again = AccountSave.Decode(TestContent.Db, AccountSave.Encode(dropped), out var second);
            Assert.That(again.Active, Is.Null);
            Assert.That(second, Is.Null, "once re-saved, the notice does not repeat");
        }

        [Test]
        public void ANewerContentMajor_LoadsWithANotice_AndIsStamped()
        {
            var a = AccountOps.New(TestContent.Db, 5);
            a.ContentVersion = "1.0";   // a MAJOR older than the shipped content
            var b = AccountSave.Decode(TestContent.Db, AccountSave.Encode(a), out var notice);
            Assert.That(notice, Does.Contain("1.0"));
            Assert.That(b.ContentVersion, Is.EqualTo(TestContent.Db.Version));
        }
    }
}
