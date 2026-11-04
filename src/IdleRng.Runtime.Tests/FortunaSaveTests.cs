using System;
using System.Linq;
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
            var a = AccountOps.New(Db, seed);
            a.Shards = 10000;
            a.RunsStarted = 2;
            a.Vault.Add(new VaultEntry { Id = 0, SpeciesId = "slime", Trees = { new VaultTree { TreeId = "fortune", Bought = 2 } } });
            a.Vault.Add(new VaultEntry { Id = 1, SpeciesId = "slime" });
            return a;
        }

        [Test]
        public void BredRunRoundTrip_PreservesRankHighWaterParentIdsAndFrozenRoll()
        {
            var a = Parents();
            a.Fortuna.Rank = 5;
            var s = FortunaBreeding.Breed(Db, a, 0, 1);
            s.Sp = 100;
            for (int i = 0; i < 8; i++) TrainingRun.SpendPoint(Db, s, 2);
            var before = TrainingRun.StateHash(s);
            var b = AccountSave.Decode(Db, AccountSave.Encode(a), out var notice);
            Assert.That(notice, Is.Null);
            Assert.That(TrainingRun.StateHash(b.Active), Is.EqualTo(before));
            Assert.That((b.Active.Lineage.Rank, b.Active.Lineage.Deepest, b.Active.Lineage.Primary, b.Active.Lineage.Supporting), Is.EqualTo((5, 8, 0, 1)));
            Assert.That(b.Active.Slots[2].Roll.LineageRank, Is.EqualTo(5));
            Assert.That(TrainingRun.MonsterFighter(Db, b.Active).Rules, Does.Contain(RuleModifiers.FortuneBank));
            Assert.That(AccountSave.Encode(b), Is.EqualTo(AccountSave.Encode(a)), "save/load retains inherited roll exactly");
        }

        [Test]
        public void OverwrittenFocusTreeRoundTrip_RetainsEarnedPointsWithoutRestoringItsPower()
        {
            var a = Parents();
            a.Fortuna.Rank = 5;
            var s = FortunaBreeding.Breed(Db, a, 0, 1);
            s.Sp = 100;
            for (int i = 0; i < 8; i++) TrainingRun.SpendPoint(Db, s, 2);
            s.Offers.Insert(0, new TreeOffer { Trees = { "vital_core" } });
            TrainingRun.TakeTree(Db, s, 0, 2);
            var b = AccountSave.Decode(Db, AccountSave.Encode(a), out var notice);
            Assert.That(notice, Is.Null);
            Assert.That(b.Active.Lineage.Deepest, Is.EqualTo(8));
            Assert.That(b.Active.Slots[2].TreeId, Is.EqualTo("vital_core"));
            Assert.That(TrainingRun.MonsterFighter(Db, b.Active).Rules, Does.Not.Contain(RuleModifiers.FortuneBank));
            Assert.That(TrainingRun.StateHash(b.Active), Is.EqualTo(TrainingRun.StateHash(s)));
        }

        [Test]
        public void FailedBreakthrough_SaveLoadAndPartsRetainRetryStateAndNextOutcome()
        {
            AccountState a = null;
            // Find a deterministic seed with a failed first V attempt.
            for (ulong seed = 0; seed < 100; seed++)
            {
                a = Parents(seed);
                a.Fortuna.Rank = 4; a.Fortuna.Points = 179;
                FortunaBreeding.Award(Db, a, new RunState { Lineage = new LineageRun { Deepest = 1 } });
                if (a.Fortuna.Failures == 1) break;
            }
            Assert.That(a.Fortuna.Failures, Is.EqualTo(1));
            var items = AccountParts.Split(a);
            Assert.That(items[AccountParts.Progress], Does.Contain("Fortuna"));
            Assert.That(items[AccountParts.Training], Does.Not.Contain("Fortuna"));
            var b = AccountParts.Join(Db, items, out var notice);
            Assert.That(notice, Is.Null);
            var c = AccountSave.Decode(Db, AccountSave.Encode(a));
            Assert.That((b.Fortuna.Rank, b.Fortuna.Points, b.Fortuna.Failures, b.Fortuna.Attempts),
                Is.EqualTo((a.Fortuna.Rank, a.Fortuna.Points, a.Fortuna.Failures, a.Fortuna.Attempts)));
            Assert.That(FortunaBreeding.Required(Db, b.Fortuna), Is.EqualTo(FortunaBreeding.Required(Db, a.Fortuna)));
            Assert.That(FortunaBreeding.Chance(Db, b.Fortuna), Is.EqualTo(65));
            // A persisted failure cannot be rerolled by loading: every copy uses the same next attempt.
            foreach (var account in new[] { a, b, c })
            {
                account.Fortuna.Points = FortunaBreeding.Required(Db, account.Fortuna);
                FortunaBreeding.Award(Db, account, new RunState { Lineage = new LineageRun() });
            }
            Assert.That(AccountParts.Split(b), Is.EqualTo(AccountParts.Split(a)));
            Assert.That(AccountParts.Split(c), Is.EqualTo(AccountParts.Split(a)));
        }

        [Test]
        public void Schema12SaveAndParts_DefaultToRankOne_WithoutChangingExistingRun()
        {
            var a = Parents();
            AccountOps.StartRun(Db, a, "slime");
            var hash = TrainingRun.StateHash(a.Active);
            var body = SaveSerializer.Decode(AccountSave.Encode(a)).body;
            body["SchemaVersion"] = 12;
            body["Account"]["SchemaVersion"] = 12;
            ((JObject)body["Account"]).Remove("Fortuna");
            var b = AccountSave.Decode(Db, SaveSerializer.EncodeBody(body.ToString()), out var notice);
            Assert.That(notice, Is.Null);
            Assert.That(b.SchemaVersion, Is.EqualTo(AccountState.CurrentSchemaVersion));
            Assert.That((b.Fortuna.Rank, b.Fortuna.Points, b.Fortuna.Attempts), Is.EqualTo((1, 0, 0)));
            Assert.That(TrainingRun.StateHash(b.Active), Is.EqualTo(hash));
            var parts = AccountParts.Split(a);
            var progress = JObject.Parse(parts[AccountParts.Progress]); progress.Remove("Fortuna");
            parts[AccountParts.Progress] = progress.ToString(Newtonsoft.Json.Formatting.None);
            var c = AccountParts.Join(Db, parts, 12, out notice);
            Assert.That(notice, Is.Null);
            Assert.That(c.Fortuna.Rank, Is.EqualTo(1));
            Assert.That(TrainingRun.StateHash(c.Active), Is.EqualTo(hash));
        }
    }
}
