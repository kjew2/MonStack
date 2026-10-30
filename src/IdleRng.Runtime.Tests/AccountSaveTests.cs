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
            a.Shards = 500;
            AccountOps.BuyTalent(db, a, db.TalentList[0].Id);
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
        public void Rejects_WrongKind_NewerSchema_AndARunThatNoLongerReplays()
        {
            var db = TestContent.Db;
            Assert.Throws<SaveInvalidException>(() => AccountSave.Decode(db, SaveSerializer.EncodeBody("{\"Kind\":\"other\",\"SchemaVersion\":1}")));
            Assert.Throws<SaveInvalidException>(() => AccountSave.Decode(db, SaveSerializer.EncodeBody("{\"Kind\":\"monster_account\",\"SchemaVersion\":99,\"Account\":{}}")));
            string json = Encoding.UTF8.GetString(AccountSave.Encode(Played()));
            string body = json.Substring(json.IndexOf('\n') + 1);
            int i = body.IndexOf("\"Hash\": \"", StringComparison.Ordinal) + 9;
            string tampered = body.Substring(0, i) + (body[i] == '0' ? '1' : '0') + body.Substring(i + 1);
            Assert.Throws<SaveInvalidException>(() => AccountSave.Decode(db, SaveSerializer.EncodeBody(tampered)));
            Assert.Throws<SaveCorruptException>(() => AccountSave.Decode(db, Encoding.UTF8.GetBytes("garbage")));
        }
    }
}
