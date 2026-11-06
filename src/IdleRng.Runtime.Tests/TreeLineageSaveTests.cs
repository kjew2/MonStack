using System.Collections.Generic;
using IdleRng.Runtime.Save;
using IdleRng.Sim.Account;
using IdleRng.Sim.Rng;
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
            a.Vault.Add(new VaultEntry { Id = 1, SpeciesId = "slime", Trees = { new VaultTree { TreeId = "fortune", Bought = 2 } } });
            return a;
        }
        [Test]
        public void PersonalLedgersAndFocus_RoundTripThroughSaveAndPartitionedStorage()
        {
            var a = Parents();
            a.Vault[0].Lineages = new SortedDictionary<string, FortunaLineage> { ["bulwark"] = new FortunaLineage { Rank = 9, Points = 103, Failures = 3, Attempts = 7 } };
            a.Vault[1].Lineages = new SortedDictionary<string, FortunaLineage> { ["fortune"] = new FortunaLineage { Rank = 5, Points = 111, Attempts = 4 } };
            var s = TreeLineages.Breed(Db, a, 0, 1, "bulwark"); s.Sp = 100;
            for (int i = 0; i < 8; i++) TrainingRun.SpendPoint(Db, s, 2);
            s.LineageTraining = new LineageTrainingState { Stat = "str", Choice = "test", Charge = 3, Used = true };
            string hash = TrainingRun.StateHash(s);
            var b = AccountSave.Decode(Db, AccountSave.Encode(a), out var notice);
            var d = AccountParts.Join(Db, AccountParts.Split(a), out var partsNotice);
            Assert.That(notice, Is.Null); Assert.That(partsNotice, Is.Null);
            foreach (var account in new[] { b, d })
            {
                Assert.That(TrainingRun.StateHash(account.Active), Is.EqualTo(hash));
                Assert.That(account.Active.Lineage.TreeId, Is.EqualTo("bulwark"));
                Assert.That(account.Active.LineageTraining.Choice, Is.EqualTo("test"));
                Assert.That(account.Lineages, Is.Empty);
                var fortune = account.Active.Lineage.Progress["fortune"];
                Assert.That((fortune.Rank, fortune.Points, fortune.Attempts), Is.EqualTo((5, 111, 4)));
                var saved = account.Active.Lineage.Progress["bulwark"];
                Assert.That((saved.Rank, saved.Points, saved.Failures, saved.Attempts), Is.EqualTo((9, 103, 3, 7)));
                Assert.That(AccountParts.Split(account), Is.EqualTo(AccountParts.Split(a)));
            }
        }
        [TestCase("negative-points")]
        [TestCase("missing-secondary")]
        [TestCase("duplicate-tree")]
        public void MalformedActiveInheritance_IsRefusedBeforeItCanSupplyPower(string fault)
        {
            var a = Parents(); var s = TreeLineages.Breed(Db, a, 0, 1, "bulwark");
            if (fault == "negative-points") s.Lineage.Progress["bulwark"].Points = -1;
            if (fault == "missing-secondary") s.Lineage.Progress.Remove("fortune");
            if (fault == "duplicate-tree") s.Lineage.SupportingTreeId = "bulwark";
            var loaded = AccountSave.Decode(Db, AccountSave.Encode(a), out var notice);
            Assert.That(loaded.Active, Is.Null);
            Assert.That(notice, Does.Contain("lineage").Or.Contain("inheritance"));
            Assert.That(loaded.Shards, Is.EqualTo(a.Shards));
            Assert.That(loaded.Vault, Is.Empty);
        }

        [Test]
        public void Schema13Migration_AssignsFortuneWithExactStreamHistory()
        {
            var a = Parents(); AccountOps.StartRun(Db, a, "slime"); string hash = TrainingRun.StateHash(a.Active);
            var body = SaveSerializer.Decode(AccountSave.Encode(a)).body; body["SchemaVersion"] = 13; body["Account"]["SchemaVersion"] = 13;
            ((JObject)body["Account"]).Remove("Lineages");
            body["Account"]["Fortuna"] = JObject.FromObject(new FortunaLineage { Rank = 4, Points = 21, Failures = 2, Attempts = 2 }, SaveSerializer.Serializer);
            var b = AccountSave.Decode(Db, SaveSerializer.EncodeBody(body.ToString()), out var notice);
            Assert.That(notice, Is.Null); Assert.That(b.Lineages, Is.Empty);
            var p = b.Vault[1].Lineages["fortune"];
            Assert.That((p.Rank, p.Points, p.Failures, p.Attempts), Is.EqualTo((4, 21, 2, 2)));
            Assert.That(p.Seed, Is.EqualTo(StreamNames.StreamSeed(a.Seed, "lineage.fortune")));
            Assert.That(TrainingRun.StateHash(b.Active), Is.EqualTo(hash));
            Assert.That(AccountSave.Encode(AccountSave.Decode(Db, AccountSave.Encode(b))), Is.EqualTo(AccountSave.Encode(b)), "assignment is not repeated on current-schema reload");
        }
        [Test]
        public void Schema14Migration_StrongestCarrierGetsOneCopy_UnmatchedReserveSurvivesWithoutAliasResurrection()
        {
            var a = Parents();
            a.Vault[0].Trees.Add(new VaultTree { TreeId = "fortune", Bought = 2 });
            a.Vault[0].RankIndex = 2; a.Vault[0].Locked = true; a.Vault[0].Stats.Str = 200;
            a.Vault[1].RankIndex = 2; a.Vault[1].Stats.Str = 100;
            var before = JArray.FromObject(a.Vault[0].Trees, SaveSerializer.Serializer).ToString();
            var body = SaveSerializer.Decode(AccountSave.Encode(a)).body; body["SchemaVersion"] = 14; body["Account"]["SchemaVersion"] = 14;
            body["Account"]["Lineages"] = JObject.FromObject(new SortedDictionary<string, FortunaLineage> {
                ["fortune"] = new FortunaLineage { Rank = 5, Points = 19, Attempts = 3 },
                ["spellcraft"] = new FortunaLineage { Rank = 3, Points = 7 },
            }, SaveSerializer.Serializer);
            var b = AccountSave.Decode(Db, SaveSerializer.EncodeBody(body.ToString()));
            Assert.That(b.Vault[0].Lineages["fortune"].Rank, Is.EqualTo(5), "locks protect consumption, not migration preservation");
            Assert.That(b.Vault[1].Lineages, Is.Null, "old global progress is assigned only once");
            Assert.That(b.Lineages.Keys, Is.EquivalentTo(new[] { "spellcraft" }));
            Assert.That(JArray.FromObject(b.Vault[0].Trees, SaveSerializer.Serializer).ToString(), Is.EqualTo(before));
            var c = AccountParts.Join(Db, AccountParts.Split(b), out _);
            Assert.That(c.Lineages.ContainsKey("fortune"), Is.False, "reading the legacy alias cannot resurrect claimed progress");
            Assert.That(c.Lineages["spellcraft"].Points, Is.EqualTo(7));
            Assert.That(AccountParts.Split(c), Is.EqualTo(AccountParts.Split(b)));
        }
        [Test]
        public void Schema14Migration_PreservesHigherBirthRankAndOldActiveRunWithoutConsumingItsParents()
        {
            var a = Parents();
            a.Vault[1].Lineage = new LineageRun { Rank = 9, TreeId = "fortune", Primary = 10, Supporting = 11 };
            a.Active = TrainingRun.Start(Db, "slime", 42); a.Active.Lineage = new LineageRun { Rank = 4, Deepest = 3, Primary = 0, Supporting = 1 };
            var body = SaveSerializer.Decode(AccountSave.Encode(a)).body; body["SchemaVersion"] = 14; body["Account"]["SchemaVersion"] = 14;
            body["Account"]["Lineages"] = JObject.FromObject(new SortedDictionary<string, FortunaLineage> { ["fortune"] = new FortunaLineage { Rank = 5, Points = 10 } }, SaveSerializer.Serializer);
            var b = AccountSave.Decode(Db, SaveSerializer.EncodeBody(body.ToString()));
            Assert.That(b.Vault.Count, Is.EqualTo(2));
            Assert.That(b.Vault[1].Lineages["fortune"].Rank, Is.EqualTo(9));
            Assert.That(b.Active.Lineage.Progress["fortune"].Rank, Is.EqualTo(4));
            Assert.That(b.Active.Lineage.Progress["fortune"].Points, Is.Zero);
            Assert.That(b.Active.Lineage.Deepest, Is.EqualTo(3));
        }
    }
}
