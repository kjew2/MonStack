using System;
using System.Linq;
using IdleRng.Sim.Account;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Account and meta-progression (specs/18): runs, shard payout, talents, run modifiers, books.</summary>
    public class AccountTests
    {
        static ContentDb Db => TestContent.Db;
        static Constants K => Db.Constants;

        /// <summary>Finish a run with rests and fights only.</summary>
        static void PlayOut(RunState s)
        {
            while (s.Phase != RunPhase.Finished)
            {
                while (s.Offers.Count > 0) TrainingRun.DiscardOffer(Db, s);
                if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s);
                else TrainingRun.Rest(Db, s);
            }
        }

        static TalentDef Talent(TalentEffect e) => Db.TalentList.First(t => t.Effect == e);

        /// <summary>A new account with every species unlocked, for tests about other rules.</summary>
        static AccountState NewAll(ulong seed)
        {
            var a = AccountOps.New(Db, seed);
            foreach (var sp in Db.SpeciesList) AccountOps.UnlockSpecies(Db, a, sp.Id);
            return a;
        }

        /// <summary>A Vault entry with the given grade index, final stats and gauntlet floors cleared, for record tests.</summary>
        static VaultEntry Sealed(int rank, double stat = 100, int floorsCleared = 0) => new VaultEntry
        {
            Grade = Db.FightRanks[rank].Id, RankIndex = rank, LegacyTitle = "t", GauntletFloor = floorsCleared + 1,
            Stats = new MonsterStats { Hp = stat, Str = stat, Int = stat, Wis = stat, Dex = stat, Def = stat, Spd = stat, Luk = stat },
        };

        [Test]
        public void New_OwnsOnlyTheStarter_NothingElse()
        {
            var a = AccountOps.New(Db, 5);
            Assert.That(a.OwnedSpecies, Is.EqualTo(new[] { Db.SpeciesList[0].Id }), "the first species in content is the starter");
            Assert.That(Db.StarterSpecies.Id, Is.EqualTo("slime"));
            Assert.That(a.Shards, Is.EqualTo(0));
            Assert.That(a.Books, Is.Empty);
            Assert.That(a.Talents, Is.Empty);
            Assert.That(a.Achievements, Is.Empty);
            Assert.That(a.Records.RunsFinished, Is.EqualTo(0));
            Assert.That(a.Vendor, Is.Null);
            Assert.That(a.Vault, Is.Empty);
            Assert.That(a.Active, Is.Null);
            Assert.That(a.ContentVersion, Is.EqualTo(Db.Version));
            Assert.Throws<InvalidOperationException>(() => AccountOps.StartRun(Db, a, "drake"), "drake is locked");
            Assert.That(a.RunsStarted, Is.EqualTo(0));
        }

        [Test]
        public void EveryOtherSpecies_HasAnAchievementThatUnlocksIt()
        {
            foreach (var sp in Db.SpeciesList.Skip(1))
                Assert.That(Db.AchievementList.Any(d => d.UnlocksSpecies == sp.Id), Is.True, sp.Id);
            Assert.That(Db.AchievementList.Any(d => d.UnlocksSpecies == Db.StarterSpecies.Id), Is.False);
        }

        [Test]
        public void Achievements_EarnedFromRecords_UnlockSpecies_InContentOrder()
        {
            var a = AccountOps.New(Db, 5);
            var rankC = Db.AchievementList.First(d => d.Kind == AchievementKind.RankReached);
            int c = Db.FightRanks.ToList().FindIndex(r => r.Id == rankC.Ref);
            var stat = Db.AchievementList.First(d => d.Kind == AchievementKind.StatReached);
            var runs = Db.AchievementList.First(d => d.Kind == AchievementKind.RunsFinished);
            var floors = Db.AchievementList.First(d => d.Kind == AchievementKind.GauntletFloors);

            AccountOps.Record(a, Sealed(c - 1));
            Assert.That(AccountOps.CheckAchievements(Db, a), Is.Empty);
            Assert.That(AccountOps.AchievementProgress(Db, a, rankC), Is.EqualTo(((double)c, (double)c + 1)), "ranks count from 1");

            AccountOps.Record(a, Sealed(c, stat.Amount, (int)floors.Amount));
            var got = AccountOps.CheckAchievements(Db, a).Select(d => d.Id).ToList();
            Assert.That(got, Is.EqualTo(new[] { rankC.Id, stat.Id, floors.Id }), "content order; runs_finished not yet");
            Assert.That(a.OwnedSpecies, Is.EqualTo(Db.SpeciesList.Select(s => s.Id).Where(id => id == "slime" || id == rankC.UnlocksSpecies || id == stat.UnlocksSpecies)));
            Assert.That(AccountOps.CheckAchievements(Db, a), Is.Empty, "each achievement is earned once");

            for (int i = a.Records.RunsFinished; i < runs.Amount; i++) AccountOps.Record(a, Sealed(0));
            Assert.That(AccountOps.CheckAchievements(Db, a).Single().Id, Is.EqualTo(runs.Id));
            Assert.That(a.Records.BestRank, Is.EqualTo(c), "a worse run never lowers a best");
        }

        [Test]
        public void StatAchievement_CountsTheNamedStatOnly()
        {
            var a = AccountOps.New(Db, 5);
            var any = Db.AchievementList.First(d => d.Kind == AchievementKind.StatReached);
            Assert.That(any.Ref, Is.Null, "the placeholder counts any stat");
            var e = Sealed(0, 10);
            e.Stats.Luk = any.Amount;
            AccountOps.Record(a, e);
            Assert.That(AccountOps.AchievementHolds(Db, a, any), Is.True);
            Assert.That(a.Records.BestStat(StatIds.Luk), Is.EqualTo(any.Amount));
            Assert.That(a.Records.BestStat(StatIds.Str), Is.EqualTo(10));
        }

        [Test]
        public void FinishRun_RecordsTheRun_ReportsAchievementsAndUnlocks()
        {
            var a = AccountOps.New(Db, 5);
            // Five finished runs earn Veteran; the starter's runs here never reach rank C, so drake stays locked unless they do.
            FinishResult last = null;
            var runs = Db.AchievementList.First(d => d.Kind == AchievementKind.RunsFinished);
            for (int i = 0; i < runs.Amount; i++)
            {
                var run = AccountOps.StartRun(Db, a, "slime");
                PlayOut(run);
                last = AccountOps.FinishRun(Db, a);
            }
            Assert.That(a.Records.RunsFinished, Is.EqualTo((int)runs.Amount));
            Assert.That(last.Achievements, Does.Contain(runs.Id));
            Assert.That(a.Achievements, Does.Contain(runs.Id));
            foreach (var sp in last.SpeciesUnlocked) Assert.That(a.OwnedSpecies, Does.Contain(sp));
            int best = a.Vault.Max(v => v.RankIndex);
            Assert.That(a.Records.BestRank, Is.EqualTo(best));

            // An abandoned run does not count.
            AccountOps.StartRun(Db, a, "slime");
            AccountOps.AbandonRun(Db, a);
            Assert.That(a.Records.RunsFinished, Is.EqualTo((int)runs.Amount));
        }

        [Test]
        public void FinishRun_ReportsASpeciesUnlockOnce()
        {
            var a = AccountOps.New(Db, 5);
            var rankC = Db.AchievementList.First(d => d.Kind == AchievementKind.RankReached);
            a.Records.BestRank = Db.FightRanks.Count - 1;   // already met: the next seal earns it
            var run = AccountOps.StartRun(Db, a, "slime");
            PlayOut(run);
            var f = AccountOps.FinishRun(Db, a);
            Assert.That(f.Achievements, Does.Contain(rankC.Id));
            Assert.That(f.SpeciesUnlocked, Does.Contain(rankC.UnlocksSpecies));
            Assert.That(f.SpeciesUnlocked, Is.Unique);
            Assert.That(AccountOps.UnlockSpecies(Db, a, rankC.UnlocksSpecies), Is.False, "already owned");
            Assert.Throws<ArgumentException>(() => AccountOps.UnlockSpecies(Db, a, "nope"));
        }

        // ------------------------------------------------------------------ the post-run vendor

        [Test]
        public void FinishRun_RollsTheVendor_Deterministic_StartRunClearsIt()
        {
            var a = AccountOps.New(Db, 5);
            var run = AccountOps.StartRun(Db, a, "slime");
            PlayOut(run);
            var f = AccountOps.FinishRun(Db, a);
            Assert.That(a.Vendor, Is.Not.Null);
            Assert.That(a.Vendor.Grade, Is.EqualTo(f.Entry.Grade));
            Assert.That(a.Vendor.Offers, Has.Count.EqualTo(K.VendorOffers));
            foreach (var o in a.Vendor.Offers)
            {
                Assert.That(Db.Talents.ContainsKey(o.Talent), Is.True);
                Assert.That(Db.VendorRarities.Any(r => r.Id == o.Rarity), Is.True);
                Assert.That(o.Bought, Is.False);
            }
            var again = AccountOps.RollVendor(Db, AccountOps.New(Db, 5), f.Entry.RankIndex, 0);
            Assert.That(again.Offers.Select(o => o.Talent + "/" + o.Rarity), Is.EqualTo(a.Vendor.Offers.Select(o => o.Talent + "/" + o.Rarity)),
                "the offers derive from the account seed and the run index");
            var other = AccountOps.RollVendor(Db, AccountOps.New(Db, 5), f.Entry.RankIndex, 1);
            Assert.That(other.Offers.Select(o => o.Talent + "/" + o.Rarity), Is.Not.EqualTo(again.Offers.Select(o => o.Talent + "/" + o.Rarity)));

            AccountOps.StartRun(Db, a, "slime");
            Assert.That(a.Vendor, Is.Null, "unbought offers vanish when the next run starts");
            Assert.Throws<InvalidOperationException>(() => AccountOps.BuyOffer(Db, a, 0));
        }

        [Test]
        public void Vendor_HigherGrades_RollRarerOffers()
        {
            var a = AccountOps.New(Db, 11);
            double Avg(int grade)
            {
                double sum = 0; int n = 0;
                for (int run = 0; run < 200; run++)
                    foreach (var o in AccountOps.RollVendor(Db, a, grade, run).Offers) { sum += Db.VendorRarities.First(r => r.Id == o.Rarity).SortOrder; n++; }
                return sum / n;
            }
            Assert.That(Avg(Db.FightRanks.Count - 1), Is.GreaterThan(Avg(0)));
        }

        [Test]
        public void BuyOffer_SpendsShards_AddsOneRank_SellsOnce()
        {
            var a = AccountOps.New(Db, 5);
            var t = Db.TalentList.First(x => x.MaxRank >= 2);
            a.Vendor = new VendorState
            {
                Grade = "e",
                Offers = { new VendorOffer { Talent = t.Id, Rarity = t.Rarity }, new VendorOffer { Talent = t.Id, Rarity = t.Rarity } },
            };
            long price = AccountOps.TalentCost(t, 0);
            Assert.That(AccountOps.OfferOpen(Db, a, a.Vendor.Offers[0]), Is.True);
            Assert.That(AccountOps.OfferPrice(Db, a, a.Vendor.Offers[0]), Is.EqualTo(price));

            Assert.Throws<InvalidOperationException>(() => AccountOps.BuyOffer(Db, a, 0), "no shards");
            Assert.That(a.TalentRank(t.Id), Is.EqualTo(0));
            a.Shards = price + 1000;
            Assert.That(AccountOps.BuyOffer(Db, a, 0), Is.EqualTo(price));
            Assert.That(a.Shards, Is.EqualTo(1000));
            Assert.That(a.TalentRank(t.Id), Is.EqualTo(1), "one rank per purchase, whatever the rarity (#102)");
            Assert.That(a.Vendor.Offers[0].Bought, Is.True);
            Assert.Throws<InvalidOperationException>(() => AccountOps.BuyOffer(Db, a, 0), "sold");
            Assert.Throws<ArgumentException>(() => AccountOps.BuyOffer(Db, a, 2));

            // The second offer of the same talent is repriced at its next rank; at max rank it cannot be bought.
            Assert.That(AccountOps.OfferPrice(Db, a, a.Vendor.Offers[1]), Is.EqualTo(AccountOps.TalentCost(t, 1)));
            a.Talents[t.Id] = t.MaxRank;
            Assert.That(AccountOps.OfferOpen(Db, a, a.Vendor.Offers[1]), Is.False);
            Assert.That(AccountOps.OfferPrice(Db, a, a.Vendor.Offers[1]), Is.EqualTo(0));
            Assert.Throws<InvalidOperationException>(() => AccountOps.BuyOffer(Db, a, 1), "maxed");
        }

        [Test]
        public void Vendor_OfferRarityIsTheTalentsOwnTier()
        {
            var a = AccountOps.New(Db, 9);
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int run = 0; run < 200; run++)
                foreach (var o in AccountOps.RollVendor(Db, a, Db.FightRanks.Count - 1, run).Offers)
                {
                    Assert.That(o.Rarity, Is.EqualTo(Db.Talents[o.Talent].Rarity));
                    seen.Add(o.Rarity);
                }
            Assert.That(seen, Is.EquivalentTo(Db.TalentList.Select(t => t.Rarity).Distinct()), "every tier with talents shows up at the top grade");
        }

        [Test]
        public void Vendor_ExhaustedTier_IsSkipped()
        {
            // Max every talent of the commonest tier: those offers must come from the other tiers, even at grade E.
            var a = AccountOps.New(Db, 5);
            string common = Db.VendorRarities[0].Id;
            foreach (var t in Db.TalentList.Where(x => x.Rarity == common)) a.Talents[t.Id] = t.MaxRank;
            for (int run = 0; run < 50; run++)
            {
                var v = AccountOps.RollVendor(Db, a, 0, run);
                Assert.That(v.Offers, Has.Count.EqualTo(K.VendorOffers));
                Assert.That(v.Offers.All(o => o.Rarity != common && a.TalentRank(o.Talent) < Db.Talents[o.Talent].MaxRank), Is.True);
            }
        }

        [Test]
        public void Vendor_OffersOnlyTalentsNotMaxed_NoneWhenAllAre()
        {
            var a = AccountOps.New(Db, 5);
            foreach (var t in Db.TalentList.Skip(1)) a.Talents[t.Id] = t.MaxRank;
            var v = AccountOps.RollVendor(Db, a, 2, 0);
            Assert.That(v.Offers.Select(o => o.Talent).Distinct(), Is.EqualTo(new[] { Db.TalentList[0].Id }));
            a.Talents[Db.TalentList[0].Id] = Db.TalentList[0].MaxRank;
            Assert.That(AccountOps.RollVendor(Db, a, 2, 0).Offers, Is.Empty);
        }

        [Test]
        public void StartRun_DerivesSeeds_OneAtATime_ChecksSpeciesAndBooks()
        {
            var a = NewAll(5);
            var r1 = AccountOps.StartRun(Db, a, "drake");
            Assert.That(r1.Seed, Is.EqualTo(AccountOps.RunSeed(5, 0)));
            Assert.That(a.Active, Is.SameAs(r1));
            Assert.That(a.RunsStarted, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => AccountOps.StartRun(Db, a, "drake"), "one run at a time");
            PlayOut(r1);
            AccountOps.FinishRun(Db, a);
            var r2 = AccountOps.StartRun(Db, a, "slime");
            Assert.That(r2.Seed, Is.EqualTo(AccountOps.RunSeed(5, 1)));
            Assert.That(r2.Seed, Is.Not.EqualTo(r1.Seed));

            var b = NewAll(5);
            b.OwnedSpecies.Remove("sprite");
            Assert.Throws<InvalidOperationException>(() => AccountOps.StartRun(Db, b, "sprite"), "not owned");
            Assert.Throws<InvalidOperationException>(() => AccountOps.StartRun(Db, b, "drake", new[] { "book_echoes" }), "no books");
            Assert.That(b.Active, Is.Null);
            Assert.That(b.RunsStarted, Is.EqualTo(0), "a rejected start changes nothing");
        }

        [Test]
        public void Books_LeaveTheInventoryAtStart_UnusedOnesComeBack()
        {
            var a = NewAll(5);
            AccountOps.GrantBooks(Db, a, "book_echoes", 2);
            var run = AccountOps.StartRun(Db, a, "sprite", new[] { "book_echoes", "book_echoes" });
            Assert.That(a.BookCount("book_echoes"), Is.EqualTo(0));
            Assert.That(a.Books, Is.Empty, "a zero count is removed");
            TrainingRun.UseBook(Db, run, "book_echoes");
            PlayOut(run);
            var f = AccountOps.FinishRun(Db, a);
            Assert.That(f.BooksReturned, Is.EqualTo(new[] { "book_echoes" }));
            Assert.That(a.BookCount("book_echoes"), Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => AccountOps.GrantBooks(Db, a, "nope", 1));
            Assert.Throws<InvalidOperationException>(() => AccountOps.GrantBooks(Db, a, "book_echoes", -2));
        }

        [Test]
        public void FinishRun_PaysShards_VaultsTheMonster_ClearsTheRun()
        {
            var a = NewAll(5);
            var run = AccountOps.StartRun(Db, a, "drake");
            Assert.Throws<InvalidOperationException>(() => AccountOps.FinishRun(Db, a), "not over yet");
            PlayOut(run);
            var f = AccountOps.FinishRun(Db, a, 1234);
            Assert.That(f.Shards, Is.EqualTo((long)K.ShardsPerCycle * run.Cycle + (long)K.ShardsPerRank * run.Rank));
            Assert.That(a.Shards, Is.EqualTo(f.Shards));
            Assert.That(a.Vault.Single(), Is.SameAs(f.Entry));
            Assert.That(f.Entry.SpeciesId, Is.EqualTo("drake"));
            Assert.That(f.Entry.SealedUtcMs, Is.EqualTo(1234));
            Assert.That(f.Entry.Mods.DrillCostMult, Is.EqualTo(run.Mods.DrillCostMult));
            Assert.That(a.Active, Is.Null);
            Assert.Throws<InvalidOperationException>(() => AccountOps.FinishRun(Db, a), "nothing to finish");
        }

        [Test]
        public void InjuredRun_StillPays()
        {
            var a = NewAll(9);
            var run = AccountOps.StartRun(Db, a, "slime");
            while (run.Phase != RunPhase.Finished) { run.Energy = 0; TrainingRun.Drill(Db, run, StatIds.Def); }
            Assert.That(run.End, Is.EqualTo(RunEnd.Injured));
            var f = AccountOps.FinishRun(Db, a);
            Assert.That(f.Shards, Is.GreaterThan(0));
            Assert.That(a.Vault.Single().End, Is.EqualTo(RunEnd.Injured));
        }

        [Test]
        public void TalentCost_Escalates()
        {
            var t = Db.TalentList[0];
            for (int r = 0; r < t.MaxRank; r++) Assert.That(AccountOps.TalentCost(t, r), Is.EqualTo(t.CostBase + (long)t.CostStep * r));
        }

        [Test]
        public void Talents_ShapeTheNextRun()
        {
            var a = NewAll(5);
            foreach (var t in Db.TalentList) a.Talents[t.Id] = 1;
            var m = AccountOps.Modifiers(Db, a);
            Assert.That(m.DrillCostMult, Is.EqualTo(1 + Talent(TalentEffect.DrillCost).PerRank).Within(1e-12));
            Assert.That(m.StatFloorMult, Is.EqualTo(1 + Talent(TalentEffect.StatFloor).PerRank).Within(1e-12));
            Assert.That(m.DraftSizeBonus, Is.EqualTo((int)Talent(TalentEffect.DraftSize).PerRank));
            Assert.That(m.StartSp, Is.EqualTo((int)Talent(TalentEffect.StartSp).PerRank));

            var run = AccountOps.StartRun(Db, a, "drake");
            var sp = Db.Species["drake"];
            Assert.That(run.Stats.Str, Is.EqualTo(Math.Floor(sp.Base.Str * m.StatFloorMult)));
            Assert.That(run.Sp, Is.EqualTo(K.SpPerCycle + m.StartSp));
            TrainingRun.Drill(Db, run, StatIds.Str);
            Assert.That(run.Energy, Is.EqualTo(K.RunEnergyStart - K.DrillEnergyCost * m.DrillCostMult).Within(1e-9));

            // a talent gained mid-run waits for the next run
            a.Talents[Talent(TalentEffect.StartSp).Id] = 2;
            Assert.That(run.Mods.StartSp, Is.EqualTo(m.StartSp));

            // a vaulted talented monster still replays to its sealed run
            PlayOut(run);
            var entry = AccountOps.FinishRun(Db, a).Entry;
            var replayed = TrainingRun.Replay(Db, entry.SpeciesId, entry.Seed, entry.Log, entry.Books, entry.Mods);
            Assert.That(TrainingRun.StateHash(replayed), Is.EqualTo(entry.RunHash));
            Assert.That(entry.Fighter(Db).Stats.Str, Is.EqualTo(replayed.Stats.Str));
        }

        [Test]
        public void DraftSizeBonus_WidensMilestoneDrafts()
        {
            int t = Db.StatMilestones[0];
            int Draft(RunModifiers mods)
            {
                var s = TrainingRun.Start(Db, "drake", 3, null, mods);
                s.Stats.Str = t - 1;
                while (s.Offers.Count == 0) { s.Energy = K.RunEnergyMax; TrainingRun.Drill(Db, s, StatIds.Str); }
                return s.Offers[0].Trees.Count;
            }
            int pool = Db.TreeList.Count(x => x.Kind == TreeKind.Generic && (x.OfferStat == null || x.OfferStat == StatIds.Str) && x.OfferThreshold <= t);
            Assert.That(Draft(null), Is.EqualTo(Math.Min(K.DraftSize, pool)));
            Assert.That(Draft(new RunModifiers { DraftSizeBonus = 5 }), Is.EqualTo(pool));
        }

        [Test]
        public void ModifiedRun_ReplaysWithItsModifiers()
        {
            var mods = new RunModifiers { DrillCostMult = 0.8, StatFloorMult = 1.2, DraftSizeBonus = 1, StartSp = 2 };
            var s = TrainingRun.Start(Db, "sprite", 21, null, mods);
            for (int i = 0; i < 4; i++) TrainingRun.Drill(Db, s, StatIds.Int);
            Assert.That(TrainingRun.StateHash(TrainingRun.Replay(Db, "sprite", 21, s.Log, null, mods)), Is.EqualTo(TrainingRun.StateHash(s)));
            Assert.That(TrainingRun.StateHash(TrainingRun.Replay(Db, "sprite", 21, s.Log)), Is.Not.EqualTo(TrainingRun.StateHash(s)),
                "the modifiers are part of the run");
            Assert.Throws<ArgumentException>(() => TrainingRun.Start(Db, "sprite", 1, null, new RunModifiers { DrillCostMult = -1 }));
        }
    
        [Test]
        public void VaultCap_FinishSealsOverTheCap_StartWaitsForDismantling()
        {
            var a = NewAll(9);
            for (int i = 0; i < K.VaultCap; i++) a.Vault.Add(new VaultEntry { Grade = "e" });
            Assert.That(AccountOps.VaultOver(Db, a), Is.EqualTo(0), "at the cap is not over it");
            var run = AccountOps.StartRun(Db, a, "drake");   // a run may start at the cap
            PlayOut(run);
            var f = AccountOps.FinishRun(Db, a);
            Assert.That(a.Vault.Count, Is.EqualTo(K.VaultCap + 1), "a finished run always seals, even over the cap");
            Assert.That(a.Vault[K.VaultCap], Is.SameAs(f.Entry));
            Assert.That(AccountOps.VaultOver(Db, a), Is.EqualTo(1));
            long shards = a.Shards;
            int runs = a.RunsStarted;
            var ex = Assert.Throws<InvalidOperationException>(() => AccountOps.StartRun(Db, a, "drake"));
            Assert.That(ex.Message, Does.Contain("dismantle 1"));
            Assert.That(a.Active, Is.Null);
            Assert.That(a.RunsStarted, Is.EqualTo(runs), "a refused start changes nothing");
            Assert.That(a.Shards, Is.EqualTo(shards));

            AccountOps.Dismantle(Db, a, 0);
            Assert.That(AccountOps.VaultOver(Db, a), Is.EqualTo(0));
            Assert.That(AccountOps.StartRun(Db, a, "drake"), Is.Not.Null, "down to the cap, runs start again");
        }

        [Test]
        public void Dismantle_RefundsByGrade_RemovesTheEntry_RefusesLockedOnes()
        {
            var a = NewAll(9);
            var low = new VaultEntry { Grade = Db.FightRanks[0].Id };
            var top = new VaultEntry { Grade = Db.FightRanks[Db.FightRanks.Count - 1].Id };
            var mid = new VaultEntry { Grade = Db.FightRanks[1].Id };
            a.Vault.AddRange(new[] { low, top, mid });
            Assert.That(Db.FightRanks.Last().DismantleShards, Is.GreaterThan(Db.FightRanks[0].DismantleShards), "placeholder refunds rise with the grade");

            AccountOps.SetLocked(Db, a, 1, true);
            Assert.That(top.Locked, Is.True);
            Assert.Throws<InvalidOperationException>(() => AccountOps.Dismantle(Db, a, 1), "locked");
            Assert.That(a.Vault.Count, Is.EqualTo(3));
            Assert.That(a.Shards, Is.EqualTo(0));

            Assert.That(AccountOps.Dismantle(Db, a, 0), Is.EqualTo(Db.FightRanks[0].DismantleShards));
            Assert.That(a.Shards, Is.EqualTo(Db.FightRanks[0].DismantleShards));
            Assert.That(a.Vault, Is.EqualTo(new[] { top, mid }), "later entries move down one index");

            AccountOps.SetLocked(Db, a, 0, false);
            long refund = AccountOps.Dismantle(Db, a, 0);
            Assert.That(refund, Is.EqualTo(Db.FightRanks.Last().DismantleShards));
            Assert.That(a.Shards, Is.EqualTo(Db.FightRanks[0].DismantleShards + refund));
            Assert.That(a.Vault, Is.EqualTo(new[] { mid }));

            Assert.Throws<ArgumentException>(() => AccountOps.Dismantle(Db, a, 1), "no such entry");
            Assert.Throws<ArgumentException>(() => AccountOps.Dismantle(Db, a, -1));
            Assert.Throws<ArgumentException>(() => AccountOps.SetLocked(Db, a, 5, true));
            Assert.That(AccountOps.DismantleRefund(Db, new VaultEntry { Grade = "no_such_grade" }), Is.EqualTo(0));
        }

        [Test]
        public void Dismantle_WorksDuringARun_AndLockDoesNotTouchTheRunHash()
        {
            var a = NewAll(4);
            PlayOut(AccountOps.StartRun(Db, a, "slime"));
            var e = AccountOps.FinishRun(Db, a).Entry;
            string hash = e.RunHash;
            AccountOps.StartRun(Db, a, "drake");
            AccountOps.SetLocked(Db, a, 0, true);
            Assert.That(TrainingRun.StateHash(TrainingRun.Replay(Db, e.SpeciesId, e.Seed, e.Log, e.Books, e.Mods)), Is.EqualTo(hash));
            AccountOps.SetLocked(Db, a, 0, false);
            AccountOps.Dismantle(Db, a, 0);
            Assert.That(a.Vault, Is.Empty);
            Assert.That(a.Active, Is.Not.Null, "the run in progress is untouched");
        }

        [Test]
        public void AbandonRun_PaysNothing_SealsNothing_ReturnsUnusedBooks_KeepsTheSeedMoving()
        {
            var a = NewAll(5);
            AccountOps.GrantBooks(Db, a, "book_echoes", 2);
            var run = AccountOps.StartRun(Db, a, "sprite", new[] { "book_echoes", "book_echoes" });
            TrainingRun.UseBook(Db, run, "book_echoes");
            Assert.That(run.Offers, Is.Not.Empty, "abandoning works with an offer pending");
            Assert.That(AccountOps.AbandonRun(Db, a), Is.EqualTo(new[] { "book_echoes" }));
            Assert.That(a.Active, Is.Null);
            Assert.That(a.Shards, Is.EqualTo(0));
            Assert.That(a.Vault, Is.Empty);
            Assert.That(a.BookCount("book_echoes"), Is.EqualTo(1), "the used book stays used");
            Assert.That(AccountOps.StartRun(Db, a, "sprite").Seed, Is.EqualTo(AccountOps.RunSeed(5, 1)), "no reroll of the same seed");
            AccountOps.AbandonRun(Db, a);
            Assert.Throws<InvalidOperationException>(() => AccountOps.AbandonRun(Db, a));
        }
}
}
