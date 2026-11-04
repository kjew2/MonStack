using IdleRng.Runtime.Save;
using IdleRng.Sim.Account;
using IdleRng.Sim.Training;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace IdleRng.Runtime.Tests
{
    public class TreeLineageSaveTests
    {
        static Sim.Content.ContentDb Db => TestContent.Db;
        static AccountState Parents()
        {
            var a = AccountOps.New(Db, 11); a.Shards = 10000; a.RunsStarted = 2;
            a.Vault.Add(new VaultEntry { Id = 0, SpeciesId = "slime", Trees = { new VaultTree { TreeId = "bulwark", Bought = 8 } } });
            a.Vault.Add(new VaultEntry { Id = 1, SpeciesId = "slime" });
            return a;
        }
        [Test]
        public void GeneralizedLedgersAndFocus_RoundTripThroughSaveAndPartitionedStorage()
        {
            var a = Parents(); a.Fortuna = new FortunaLineage { Rank = 5, Points = 111, Attempts = 4 };
            var p = TreeLineages.Ledger(a, "bulwark"); p.Rank = 9; p.Points = 103; p.Failures = 3; p.Attempts = 7;
            var s = TreeLineages.Breed(Db, a, 0, 1, "bulwark"); s.Sp = 100;
            for (int i = 0; i < 8; i++) TrainingRun.SpendPoint(Db, s, 2);
            s.LineageTraining = new LineageTrainingState { Stat = "str", Choice = "test", Charge = 3, Used = true };
            string hash = TrainingRun.StateHash(s);
            var b = AccountSave.Decode(Db, AccountSave.Encode(a), out var notice);
            var c = AccountParts.Join(Db, AccountParts.Split(a), out var partsNotice);
            Assert.That(notice, Is.Null); Assert.That(partsNotice, Is.Null);
            foreach (var account in new[] { b, c })
            {
                Assert.That(TrainingRun.StateHash(account.Active), Is.EqualTo(hash));
                Assert.That(account.Active.Lineage.TreeId, Is.EqualTo("bulwark"));
                Assert.That(account.Active.LineageTraining.Choice, Is.EqualTo("test"));
                Assert.That(account.Fortuna, Is.SameAs(account.Lineages["fortune"]));
                Assert.That((account.Fortuna.Rank, account.Fortuna.Points, account.Fortuna.Attempts), Is.EqualTo((5, 111, 4)));
                var saved = account.Lineages["bulwark"];
                Assert.That((saved.Rank, saved.Points, saved.Failures, saved.Attempts), Is.EqualTo((9, 103, 3, 7)));
                Assert.That(AccountParts.Split(account), Is.EqualTo(AccountParts.Split(a)));
            }
        }
        [Test]
        public void SchemaThirteenMigrationRetainsExistingFortuneStreamHistory()
        {
            var a = Parents(); a.Fortuna = new FortunaLineage { Rank = 4, Points = 21, Failures = 2, Attempts = 2 };
            AccountOps.StartRun(Db, a, "slime"); string hash = TrainingRun.StateHash(a.Active);
            var body = SaveSerializer.Decode(AccountSave.Encode(a)).body;
            body["SchemaVersion"] = 13; body["Account"]["SchemaVersion"] = 13;
            ((JObject)body["Account"]).Remove("Lineages");
            var b = AccountSave.Decode(Db, SaveSerializer.EncodeBody(body.ToString()), out var notice);
            Assert.That(notice, Is.Null);
            Assert.That((b.Fortuna.Rank, b.Fortuna.Points, b.Fortuna.Failures, b.Fortuna.Attempts), Is.EqualTo((4, 21, 2, 2)));
            Assert.That(b.Fortuna, Is.SameAs(b.Lineages["fortune"]));
            Assert.That(TreeLineages.GetLedger(b, "bulwark").Rank, Is.EqualTo(1));
            Assert.That(TrainingRun.StateHash(b.Active), Is.EqualTo(hash));
            foreach (var account in new[] { a, b })
            {
                account.Fortuna.Points = TreeLineages.Required(Db, account.Fortuna);
                TreeLineages.Award(Db, account, new RunState { Lineage = new LineageRun() });
            }
            Assert.That((b.Fortuna.Rank, b.Fortuna.Failures, b.Fortuna.Attempts), Is.EqualTo((a.Fortuna.Rank, a.Fortuna.Failures, a.Fortuna.Attempts)));
        }
    }
}
