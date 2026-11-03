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
            Assert.That(b.Vault[0].Id, Is.EqualTo(a.Vault[0].Id));
            Assert.That(b.Vault[0].Fighter(TestContent.Db).Stats.Str, Is.EqualTo(a.Vault[0].Fighter(TestContent.Db).Stats.Str));
            Assert.That(b.Active, Is.Not.Null);
            Assert.That(TrainingRun.StateHash(b.Active), Is.EqualTo(TrainingRun.StateHash(a.Active)));
            Assert.That(Encoding.UTF8.GetString(AccountSave.Encode(b)), Is.EqualTo(Encoding.UTF8.GetString(AccountSave.Encode(a))),
                "encode(decode(x)) is stable");
        }

        [Test]
        public void AnActiveRunWithAnInjury_ReplaysOnLoad()
        {
            var db = TestContent.Db;
            var a = AccountOps.New(db, 5);
            var run = AccountOps.StartRun(db, a, a.OwnedSpecies[0]);
            while (run.Injuries.Count == 0 && run.Phase != RunPhase.Finished)
            {
                while (run.Offers.Count > 0) TrainingRun.DiscardOffer(db, run);
                if (run.Phase == RunPhase.AwaitFight) TrainingRun.Fight(db, run);
                else TrainingRun.Drill(db, run, StatIds.Str);   // never rests, so an injury comes (specs/14 section 4.1)
            }
            Assert.That(run.Injuries, Is.Not.Empty);
            Assume.That(run.Phase, Is.Not.EqualTo(RunPhase.Finished), "an early injury leaves the run going");
            var b = AccountSave.Decode(db, AccountSave.Encode(a), out var notice);
            Assert.That(notice, Is.Null);
            Assert.That(TrainingRun.StateHash(b.Active), Is.EqualTo(TrainingRun.StateHash(run)));
            Assert.That(b.Active.Injuries.Single().BackCycle, Is.EqualTo(run.Injuries.Single().BackCycle));
        }

        [Test]
        public void TheLadder_RoundTrips_InTheRecipe()
        {
            var a = Played();
            var b = AccountSave.Decode(TestContent.Db, AccountSave.Encode(a), out var notice);
            Assert.That(notice, Is.Null);
            Assert.That(b.Active.Ladder, Is.EqualTo(a.Active.Ladder));
            Assert.That(b.Active.Ladder, Is.Not.Empty);
        }

        [Test]
        public void APrePoolSave_KeepsItsRunGoing_AgainstTheFlatRanks()
        {
            // A save written before the rank pools (content 4.3): no Ladder in the recipe or the Vault entries, and the run's hash
            // is the version 5 one. Its run in progress replays as a legacy run, so it is not dropped.
            var db = TestContent.Db;
            var a = Played();
            var legacy = TrainingRun.Start(db, "sprite", 123, null, null, new System.Collections.Generic.List<string>());
            while (legacy.Cycle < 3)
            {
                while (legacy.Offers.Count > 0) TrainingRun.DiscardOffer(db, legacy);
                if (legacy.Phase == RunPhase.AwaitFight) TrainingRun.Fight(db, legacy); else TrainingRun.Drill(db, legacy, StatIds.Int);
            }
            a.Active = legacy;
            var body = OldSaveBody(a);
            ((Newtonsoft.Json.Linq.JObject)body["ActiveRun"]).Remove("Ladder");
            foreach (var e in body["Account"]["Vault"]) ((Newtonsoft.Json.Linq.JObject)e).Remove("Ladder");
            var b = AccountSave.Decode(db, SaveSerializer.EncodeBody(body.ToString()), out var notice);
            Assert.That(notice, Is.Null, "nothing was dropped");
            Assert.That(b.Active.Ladder, Is.Empty);
            Assert.That(TrainingRun.StateHash(b.Active), Is.EqualTo(TrainingRun.StateHash(legacy)));
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
            var body = OldSaveBody(a);
            body["SchemaVersion"] = 1;
            body["Account"]["SchemaVersion"] = 1;
            foreach (var e in body["Account"]["Vault"]) ((Newtonsoft.Json.Linq.JObject)e).Remove("Locked");
            var b = AccountSave.Decode(TestContent.Db, SaveSerializer.EncodeBody(body.ToString()));
            Assert.That(b.SchemaVersion, Is.EqualTo(AccountState.CurrentSchemaVersion));
            Assert.That(b.Vault.Count, Is.EqualTo(1));
            Assert.That(b.Vault[0].Locked, Is.False);
            Assert.That(b.Vault[0].GauntletFloor, Is.EqualTo(a.Vault[0].GauntletFloor));
            Assert.That(TrainingRun.StateHash(b.Active), Is.EqualTo(TrainingRun.StateHash(a.Active)), "the active run still replays");
            Assert.That(SaveBody(AccountSave.Encode(b)), Does.Contain("\"SchemaVersion\": " + AccountState.CurrentSchemaVersion), "it saves back as the current version");
        }

        /// <summary>A save body as the v2 code wrote it: no achievements, records or vendor; every species owned.</summary>
        static Newtonsoft.Json.Linq.JObject V2Body(AccountState a)
        {
            var body = OldSaveBody(a);
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
        public void V3Save_MigratesToV4_WithNoTowerProgress()
        {
            var db = TestContent.Db;
            var a = Played();
            var body = OldSaveBody(a);
            body["SchemaVersion"] = 3;
            var acct = (Newtonsoft.Json.Linq.JObject)body["Account"];
            acct["SchemaVersion"] = 3;
            acct.Remove("Tower");
            var b = AccountSave.Decode(db, SaveSerializer.EncodeBody(body.ToString()));
            Assert.That(b.SchemaVersion, Is.EqualTo(AccountState.CurrentSchemaVersion));
            Assert.That(b.Tower, Is.Not.Null);
            Assert.That(b.Tower.Day, Is.EqualTo(-1));
            Assert.That(b.Tower.ClearedEver, Is.EqualTo(0));
            Assert.That(b.Vault.Count, Is.EqualTo(a.Vault.Count));
            Assert.That(TrainingRun.StateHash(b.Active), Is.EqualTo(TrainingRun.StateHash(a.Active)), "the active run still replays");
        }

        [Test]
        public void V4Save_MigratesToV5_WithNoTicketsYet()
        {
            var db = TestContent.Db;
            var a = Played();
            AccountOps.RunTower(db, a, new[] { 0 }, 40);
            var body = OldSaveBody(a);
            body["SchemaVersion"] = 4;
            var acct = (Newtonsoft.Json.Linq.JObject)body["Account"];
            acct["SchemaVersion"] = 4;
            var tower = (Newtonsoft.Json.Linq.JObject)acct["Tower"];
            tower.Remove("Tickets"); tower.Remove("TicketPieces"); tower.Remove("GrantDay");
            var b = AccountSave.Decode(db, SaveSerializer.EncodeBody(body.ToString()));
            Assert.That(b.SchemaVersion, Is.EqualTo(AccountState.CurrentSchemaVersion));
            Assert.That(b.Tower.Day, Is.EqualTo(40), "v4 progress is kept");
            Assert.That(b.Tower.ClearedEver, Is.EqualTo(a.Tower.ClearedEver));
            Assert.That(b.Tower.Tickets, Is.EqualTo(0));
            Assert.That(b.Tower.TicketPieces, Is.EqualTo(0));
            Assert.That(b.Tower.GrantDay, Is.EqualTo(-1));
            Assert.That(AccountOps.TowerTickets(db, b, 41), Is.EqualTo(db.Constants.TowerAttemptsPerDay), "the next day's free tickets are due");
        }

        /// <summary>Plays a run to its end with rests and fights only (offers discarded).</summary>
        static void PlayOut(Sim.Content.ContentDb db, RunState run)
        {
            while (run.Phase != RunPhase.Finished)
            {
                while (run.Offers.Count > 0) TrainingRun.DiscardOffer(db, run);
                if (run.Phase == RunPhase.AwaitFight) TrainingRun.Fight(db, run);
                else TrainingRun.Rest(db, run);
            }
        }

        /// <summary>An account right after its first sealed run: no run in progress, the vendor's offers rolled.</summary>
        static AccountState SealedOnce()
        {
            var db = TestContent.Db;
            var a = AccountOps.New(db, 78);
            var run = AccountOps.StartRun(db, a, "slime");
            while (run.Phase != RunPhase.Finished)
            {
                while (run.Offers.Count > 0) TrainingRun.DiscardOffer(db, run);
                if (run.Phase == RunPhase.AwaitFight) TrainingRun.Fight(db, run);
                else TrainingRun.Rest(db, run);
            }
            AccountOps.FinishRun(db, a, 1000);
            return a;
        }

        [Test]
        public void V5Save_MigratesToV6_VendorWithNoRerolls()
        {
            var db = TestContent.Db;
            var a = SealedOnce();
            Assert.That(a.Vendor, Is.Not.Null);
            var body = OldSaveBody(a);
            body["SchemaVersion"] = 5;
            var acct = (Newtonsoft.Json.Linq.JObject)body["Account"];
            acct["SchemaVersion"] = 5;
            ((Newtonsoft.Json.Linq.JObject)acct["Vendor"]).Remove("Rerolls");
            var b = AccountSave.Decode(db, SaveSerializer.EncodeBody(body.ToString()));
            Assert.That(b.SchemaVersion, Is.EqualTo(AccountState.CurrentSchemaVersion));
            Assert.That(b.Vendor.Rerolls, Is.EqualTo(0));
            Assert.That(b.Vendor.Offers.Select(o => o.Talent), Is.EqualTo(a.Vendor.Offers.Select(o => o.Talent)), "the offers are kept");
        }

        [Test]
        public void VendorRerolls_RoundTrip()
        {
            var db = TestContent.Db;
            var a = SealedOnce();
            a.Shards = 1000;
            AccountOps.RerollVendor(db, a);
            var b = AccountSave.Decode(db, AccountSave.Encode(a));
            Assert.That(b.Vendor.Rerolls, Is.EqualTo(1));
            Assert.That(b.Vendor.Offers.Select(o => o.Talent), Is.EqualTo(a.Vendor.Offers.Select(o => o.Talent)));
        }

        [Test]
        public void V6Save_MigratesToV7_EveryTalentOn_AndTheLoadoutRoundTrips()
        {
            var db = TestContent.Db;
            var a = SealedOnce();
            a.TalentsOff.Add(db.TalentList[1].Id);
            var b = AccountSave.Decode(db, AccountSave.Encode(a));
            Assert.That(b.TalentsOff, Is.EqualTo(new[] { db.TalentList[1].Id }), "the loadout round-trips");

            var body = OldSaveBody(a);
            body["SchemaVersion"] = 6;
            var acct = (Newtonsoft.Json.Linq.JObject)body["Account"];
            acct["SchemaVersion"] = 6;
            acct.Remove("TalentsOff");
            var c = AccountSave.Decode(db, SaveSerializer.EncodeBody(body.ToString()));
            Assert.That(c.SchemaVersion, Is.EqualTo(AccountState.CurrentSchemaVersion));
            Assert.That(c.TalentsOff, Is.Empty, "a v6 account had every talent on");
        }

        [Test]
        public void V7Save_MigratesToV8_SavedRunsDraftAsLegacy()
        {
            var db = TestContent.Db;
            var a = SealedOnce();
            AccountOps.StartRun(db, a, "slime");
            Assume.That(a.Vault, Is.Not.Empty);
            Assert.That(a.Active.Mods.LegacyDrafts, Is.False, "a new run rolls its Awakening trees");
            var body = OldSaveBody(a);
            body["SchemaVersion"] = 7;
            var acct = (Newtonsoft.Json.Linq.JObject)body["Account"];
            acct["SchemaVersion"] = 7;
            ((Newtonsoft.Json.Linq.JObject)acct["Vault"][0]).Remove("Mods");   // an entry saved before talents
            var b = AccountSave.Decode(db, SaveSerializer.EncodeBody(body.ToString()));
            Assert.That(b.SchemaVersion, Is.EqualTo(AccountState.CurrentSchemaVersion));
            Assert.That(b.Active.Mods.LegacyDrafts, Is.True, "the run in progress replays its fixed drafts");
            Assert.That(AccountSave.Decode(db, AccountSave.Encode(a)).Active.Mods.LegacyDrafts, Is.False, "a v8 save keeps a new run new");
        }

        [Test]
        public void V8Save_MigratesToV9_EntriesGetTheirRunIndex_AndLoseTheirReplay()
        {
            var db = TestContent.Db;
            var a = AccountOps.New(db, 77);
            for (int i = 0; i < 3; i++) { PlayOut(db, AccountOps.StartRun(db, a, "slime")); AccountOps.FinishRun(db, a); }
            AccountOps.StartRun(db, a, "slime");
            AccountOps.AbandonRun(db, a);   // a run that left no entry: ids keep counting runs, not entries
            PlayOut(db, AccountOps.StartRun(db, a, "slime"));
            AccountOps.FinishRun(db, a);
            Assert.That(a.Vault.Select(e => e.Id), Is.EqualTo(new[] { 0, 1, 2, 4 }));
            var want = a.Vault.Select(e => e.Fighter(db)).ToList();

            // Rewrite it as a v8 save: entries without Id, with a replay recipe.
            var body = OldSaveBody(a);
            body["SchemaVersion"] = 8;
            var acct = (Newtonsoft.Json.Linq.JObject)body["Account"];
            acct["SchemaVersion"] = 8;
            foreach (Newtonsoft.Json.Linq.JObject e in acct["Vault"])
            {
                e["Seed"] = AccountOps.RunSeed(a.Seed, (int)e["Id"]).ToString("x16");   // v8 entries carried their run seed
                e.Remove("Id");
                e["Log"] = new Newtonsoft.Json.Linq.JArray(new Newtonsoft.Json.Linq.JObject { ["Kind"] = "Rest" });
                e["Mods"] = new Newtonsoft.Json.Linq.JObject { ["LegacyDrafts"] = true };
                e["Ladder"] = new Newtonsoft.Json.Linq.JArray();
            }
            var stray = (Newtonsoft.Json.Linq.JObject)acct["Vault"][3];
            stray["Seed"] = "00000000000000ff";   // no started run has this seed
            var b = AccountSave.Decode(db, SaveSerializer.EncodeBody(body.ToString()));
            Assert.That(b.SchemaVersion, Is.EqualTo(AccountState.CurrentSchemaVersion));
            Assert.That(b.Vault.Select(e => e.Id), Is.EqualTo(new[] { 0, 1, 2, -1 }), "found from the seed; -1 when no run matches");
            for (int i = 0; i < 3; i++) Assert.That(b.Vault[i].Fighter(db).Stats.Str, Is.EqualTo(want[i].Stats.Str));
            string saved = SaveBody(AccountSave.Encode(b));
            Assert.That(saved, Does.Not.Contain("\"Log\"").And.Not.Contain("\"Ladder\""), "the replay recipe is gone from the entries");
        }

        [Test]
        public void TowerProgress_RoundTrips()
        {
            var db = TestContent.Db;
            var a = Played();
            AccountOps.RunTower(db, a, new[] { 0 }, 123);
            var b = AccountSave.Decode(db, AccountSave.Encode(a));
            Assert.That(b.Tower.Day, Is.EqualTo(123));
            Assert.That(b.Tower.AttemptsUsed, Is.EqualTo(1));
            Assert.That(b.Tower.ClearedToday, Is.EqualTo(a.Tower.ClearedToday));
            Assert.That(b.Tower.ClearedEver, Is.EqualTo(a.Tower.ClearedEver));
            Assert.That(b.Tower.Tickets, Is.EqualTo(a.Tower.Tickets));
            Assert.That(b.Tower.TicketPieces, Is.EqualTo(a.Tower.TicketPieces));
            Assert.That(b.Tower.GrantDay, Is.EqualTo(123));
            Assert.That(b.Shards, Is.EqualTo(a.Shards));
        }

        [Test]
        public void EndlessProgressAndRulebooks_RoundTrip()
        {
            var db = TestContent.Db;
            var a = Played();
            a.Endless = new EndlessProgress { Season = 2913, Climbs = 3, ClearedSeason = 12, ClearedEver = 17 };
            AccountOps.SetRulebook(db, a, 0, "self_hp_below:0.3>basic@lowest_hp|always>basic");
            var b = AccountSave.Decode(db, AccountSave.Encode(a));
            Assert.That((b.Endless.Season, b.Endless.Climbs, b.Endless.ClearedSeason, b.Endless.ClearedEver), Is.EqualTo((2913, 3, 12, 17)));
            Assert.That(b.Rulebooks, Is.EqualTo(a.Rulebooks));
            Assert.That(b.Rulebook(b.Vault[0].Id), Is.EqualTo("self_hp_below:0.3>basic@lowest_hp|always>basic"));

            var fresh = AccountSave.Decode(db, AccountSave.Encode(AccountOps.New(db, 4)));
            Assert.That((fresh.Endless.Season, fresh.Endless.ClearedEver), Is.EqualTo((-1, 0)), "nothing saved reads as no climbs yet");
            Assert.That(fresh.Rulebooks, Is.Empty);
        }

        [Test]
        public void DisplayName_RoundTrips_AndAV11SaveHasNone()
        {
            var db = TestContent.Db;
            var a = Played();
            AccountOps.SetName(a, "  Ash   Ketch ");
            var b = AccountSave.Decode(db, AccountSave.Encode(a));
            Assert.That(b.Name, Is.EqualTo("Ash Ketch"));

            var body = Newtonsoft.Json.Linq.JObject.Parse(SaveBody(AccountSave.Encode(Played())));
            var acct = (Newtonsoft.Json.Linq.JObject)body["Account"];
            Assert.That(acct["Name"], Is.Null, "no name is written as nothing (lean JSON)");
            body["SchemaVersion"] = 11;
            acct["SchemaVersion"] = 11;
            var c = AccountSave.Decode(db, SaveSerializer.EncodeBody(body.ToString()), out var notice);
            Assert.That(notice, Is.Null);
            Assert.That((c.SchemaVersion, c.Name), Is.EqualTo((12, (string)null)));
        }

        [Test]
        public void V10Save_MigratesToV11_WithNoClimbsAndNoRulebooks()
        {
            var db = TestContent.Db;
            var a = Played();
            var body = Newtonsoft.Json.Linq.JObject.Parse(SaveBody(AccountSave.Encode(a)));
            var acct = (Newtonsoft.Json.Linq.JObject)body["Account"];
            acct.Remove("Endless");
            acct.Remove("Rulebooks");
            body["SchemaVersion"] = 10;
            acct["SchemaVersion"] = 10;
            var b = AccountSave.Decode(db, SaveSerializer.EncodeBody(body.ToString()), out var notice);
            Assert.That(notice, Is.Null);
            Assert.That(b.SchemaVersion, Is.EqualTo(AccountState.CurrentSchemaVersion));
            Assert.That((b.Endless.Season, b.Endless.Climbs, b.Endless.ClearedSeason, b.Endless.ClearedEver), Is.EqualTo((-1, 0, 0, 0)));
            Assert.That(b.Rulebooks, Is.Empty);
            Assert.That(b.Vault.Count, Is.EqualTo(a.Vault.Count));
            Assert.That(TrainingRun.StateHash(b.Active), Is.EqualTo(TrainingRun.StateHash(a.Active)));
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

        /// <summary>A pre-v10 save of the account (schema 9): the run in progress as its replay recipe under "ActiveRun".</summary>
        static Newtonsoft.Json.Linq.JObject OldSaveBody(AccountState a)
        {
            var body = Newtonsoft.Json.Linq.JObject.Parse(SaveBody(AccountSave.Encode(a)));
            body.Remove("Run");
            body["ActiveRun"] = a.Active == null ? (Newtonsoft.Json.Linq.JToken)Newtonsoft.Json.Linq.JValue.CreateNull() : AccountSave.LegacyRecipe(a.Active);
            body["SchemaVersion"] = 9;
            body["Account"]["SchemaVersion"] = 9;
            return body;
        }

        /// <summary>An old (recipe) save of the account, edited as text: the replay path that pre-v10 saves still take once.</summary>
        static byte[] WithTamperedRecipe(AccountState a, Func<string, string> edit) =>
            SaveSerializer.EncodeBody(edit(OldSaveBody(a).ToString(Newtonsoft.Json.Formatting.Indented)));

        [Test]
        public void ASavedRunState_ThatRefersToRemovedContent_IsDropped_WithEveryBookReturned()
        {
            var db = TestContent.Db;
            var a = Played();   // the run brought two book_echoes and used one
            Assume.That(a.Active.BooksUsed, Is.EqualTo(new[] { "book_echoes" }));
            var body = Newtonsoft.Json.Linq.JObject.Parse(SaveBody(AccountSave.Encode(a)));
            Assert.That(body["Run"]?["Log"], Is.Null, "the run is saved as its state, not its log");
            body["Run"]["SpeciesId"] = "species_removed_from_content";
            var b = AccountSave.Decode(db, SaveSerializer.EncodeBody(body.ToString()), out var notice);
            Assert.That(b.Active, Is.Null);
            Assert.That(notice, Does.Contain("no longer in the game"));
            Assert.That(b.BookCount("book_echoes"), Is.EqualTo(a.BookCount("book_echoes") + 2), "the used and the unused book both come back");
            Assert.That(b.Vault.Count, Is.EqualTo(a.Vault.Count));
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
