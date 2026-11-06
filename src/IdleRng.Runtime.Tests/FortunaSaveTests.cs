using System.Collections.Generic;
using IdleRng.Runtime.Save;
using IdleRng.Sim.Account;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Training;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace IdleRng.Runtime.Tests
{
    public class FortunaSaveTests
    {
        static Sim.Content.ContentDb Db => TestContent.Db;
        static AccountState Parents(ulong seed = 11)
        {
            var a = AccountOps.New(Db, seed); a.Shards = 10000; a.RunsStarted = 2;
            a.Vault.Add(new VaultEntry { Id = 0, SpeciesId = "slime", Trees = { new VaultTree { TreeId = "fortune", Bought = 2 } },
                Lineages = new SortedDictionary<string, FortunaLineage> { ["fortune"] = new FortunaLineage { Rank = 5 } } });
            a.Vault.Add(new VaultEntry { Id = 1, SpeciesId = "slime", Trees = { new VaultTree { TreeId = "bulwark", Bought = 8 } } });
            return a;
        }

        [Test]
        public void BredRunRoundTrip_PreservesBothInheritancesAndFrozenRollAfterConsumingParents()
        {
            var a = Parents(); var s = FortunaBreeding.Breed(Db, a, 0, 1); s.Sp = 100;
            for (int i = 0; i < 8; i++) TrainingRun.SpendPoint(Db, s, 2);
            var before = TrainingRun.StateHash(s);
            var b = AccountSave.Decode(Db, AccountSave.Encode(a), out var notice);
            Assert.That(notice, Is.Null); Assert.That(b.Vault, Is.Empty);
            Assert.That(TrainingRun.StateHash(b.Active), Is.EqualTo(before));
            Assert.That((b.Active.Lineage.Rank, b.Active.Lineage.Deepest, b.Active.Lineage.Primary, b.Active.Lineage.Supporting), Is.EqualTo((5, 8, 0, 1)));
            Assert.That(b.Active.Lineage.SupportingTreeId, Is.EqualTo("bulwark"));
            Assert.That(b.Active.Lineage.Progress.Keys, Is.EquivalentTo(new[] { "fortune", "bulwark" }));
            Assert.That(b.Active.Slots[2].Roll.LineageRank, Is.EqualTo(5));
            Assert.That(b.Active.Slots[3].TreeId, Is.EqualTo("bulwark"));
            Assert.That(TrainingRun.MonsterFighter(Db, b.Active).Rules, Does.Contain(RuleModifiers.FortuneBank));
            Assert.That(AccountSave.Encode(b), Is.EqualTo(AccountSave.Encode(a)));
        }

        [Test]
        public void OverwrittenFocusTreeRoundTrip_RetainsGeneticProgressWithoutRestoringItsPower()
        {
            var a = Parents(); var s = FortunaBreeding.Breed(Db, a, 0, 1); s.Sp = 100;
            for (int i = 0; i < 8; i++) TrainingRun.SpendPoint(Db, s, 2);
            s.Offers.Insert(0, new TreeOffer { Trees = { "vital_core" } }); TrainingRun.TakeTree(Db, s, 0, 2);
            var b = AccountSave.Decode(Db, AccountSave.Encode(a), out var notice);
            Assert.That(notice, Is.Null); Assert.That(b.Active.Lineage.Deepest, Is.EqualTo(8));
            Assert.That(b.Active.Slots[2].TreeId, Is.EqualTo("vital_core"));
            Assert.That(b.Active.Lineage.Progress["fortune"].Rank, Is.EqualTo(5));
            Assert.That(TrainingRun.MonsterFighter(Db, b.Active).Rules, Does.Not.Contain(RuleModifiers.FortuneBank));
            Assert.That(TrainingRun.StateHash(b.Active), Is.EqualTo(TrainingRun.StateHash(s)));
        }

        [Test]
        public void FailedPersonalBreakthrough_SaveAndPartsKeepRetrySeedAndNextOutcome()
        {
            AccountState a = null;
            for (ulong seed = 0; seed < 100; seed++)
            {
                a = Parents(seed); var source = a.Vault[0].Lineages["fortune"]; source.Rank = 4; source.Points = 179;
                var s = FortunaBreeding.Breed(Db, a, 0, 1); s.Lineage.Deepest = 1; TreeLineages.Award(Db, a, s);
                if (s.Lineage.Progress["fortune"].Failures == 1) break;
            }
            var expected = a.Active.Lineage.Progress["fortune"];
            Assert.That(expected.Failures, Is.EqualTo(1));
            var items = AccountParts.Split(a);
            Assert.That(items[AccountParts.Progress], Does.Not.Contain("Fortuna"));
            Assert.That(items[AccountParts.Training], Does.Contain("Progress"));
            var b = AccountParts.Join(Db, items, out var notice); Assert.That(notice, Is.Null);
            var d = AccountSave.Decode(Db, AccountSave.Encode(a));
            foreach (var account in new[] { a, b, d })
            {
                var p = account.Active.Lineage.Progress["fortune"];
                Assert.That((p.Rank, p.Points, p.Failures, p.Attempts, p.Seed), Is.EqualTo((expected.Rank, expected.Points, expected.Failures, expected.Attempts, expected.Seed)));
                Assert.That(TreeLineages.Chance(Db, p), Is.EqualTo(65));
            }
            foreach (var account in new[] { a, b, d })
            {
                var p = account.Active.Lineage.Progress["fortune"]; p.Points = TreeLineages.Required(Db, p);
                account.Active.Lineage.Deepest = 0; TreeLineages.Award(Db, account, account.Active);
            }
            Assert.That(AccountParts.Split(b), Is.EqualTo(AccountParts.Split(a)));
            Assert.That(AccountParts.Split(d), Is.EqualTo(AccountParts.Split(a)));
        }

        [Test]
        public void Schema12SaveAndParts_DefaultToNoReserve_WithoutChangingUnrelatedRun()
        {
            var a = Parents(); a.Vault[0].Lineages = null; AccountOps.StartRun(Db, a, "slime");
            var hash = TrainingRun.StateHash(a.Active);
            var body = SaveSerializer.Decode(AccountSave.Encode(a)).body; body["SchemaVersion"] = 12; body["Account"]["SchemaVersion"] = 12;
            ((JObject)body["Account"]).Remove("Fortuna"); ((JObject)body["Account"]).Remove("Lineages");
            var b = AccountSave.Decode(Db, SaveSerializer.EncodeBody(body.ToString()), out var notice);
            Assert.That(notice, Is.Null); Assert.That(b.SchemaVersion, Is.EqualTo(AccountState.CurrentSchemaVersion));
            Assert.That(b.Lineages, Is.Empty); Assert.That(TrainingRun.StateHash(b.Active), Is.EqualTo(hash));
            var parts = AccountParts.Split(a); parts[AccountParts.Progress] = "{}";
            var c = AccountParts.Join(Db, parts, 12, out notice);
            Assert.That(notice, Is.Null); Assert.That(c.Lineages, Is.Empty); Assert.That(TrainingRun.StateHash(c.Active), Is.EqualTo(hash));
        }
    }
}
