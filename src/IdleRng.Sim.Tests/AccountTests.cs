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

        [Test]
        public void New_OwnsEverySpecies_NothingElse()
        {
            var a = AccountOps.New(Db, 5);
            Assert.That(a.OwnedSpecies, Is.EqualTo(Db.SpeciesList.Select(s => s.Id)));
            Assert.That(a.Shards, Is.EqualTo(0));
            Assert.That(a.Books, Is.Empty);
            Assert.That(a.Talents, Is.Empty);
            Assert.That(a.Vault, Is.Empty);
            Assert.That(a.Active, Is.Null);
            Assert.That(a.ContentVersion, Is.EqualTo(Db.Version));
        }

        [Test]
        public void StartRun_DerivesSeeds_OneAtATime_ChecksSpeciesAndBooks()
        {
            var a = AccountOps.New(Db, 5);
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

            var b = AccountOps.New(Db, 5);
            b.OwnedSpecies.Remove("sprite");
            Assert.Throws<InvalidOperationException>(() => AccountOps.StartRun(Db, b, "sprite"), "not owned");
            Assert.Throws<InvalidOperationException>(() => AccountOps.StartRun(Db, b, "drake", new[] { "book_echoes" }), "no books");
            Assert.That(b.Active, Is.Null);
            Assert.That(b.RunsStarted, Is.EqualTo(0), "a rejected start changes nothing");
        }

        [Test]
        public void Books_LeaveTheInventoryAtStart_UnusedOnesComeBack()
        {
            var a = AccountOps.New(Db, 5);
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
            var a = AccountOps.New(Db, 5);
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
            var a = AccountOps.New(Db, 9);
            var run = AccountOps.StartRun(Db, a, "slime");
            while (run.Phase != RunPhase.Finished) { run.Energy = 0; TrainingRun.Drill(Db, run, StatIds.Def); }
            Assert.That(run.End, Is.EqualTo(RunEnd.Injured));
            var f = AccountOps.FinishRun(Db, a);
            Assert.That(f.Shards, Is.GreaterThan(0));
            Assert.That(a.Vault.Single().End, Is.EqualTo(RunEnd.Injured));
        }

        [Test]
        public void BuyTalent_CostsEscalate_UntilMaxed()
        {
            var t = Db.TalentList[0];
            var a = AccountOps.New(Db, 5);
            a.Shards = 100000;
            for (int r = 0; r < t.MaxRank; r++)
            {
                long before = a.Shards;
                Assert.That(AccountOps.BuyTalent(Db, a, t.Id), Is.EqualTo(t.CostBase + (long)t.CostStep * r));
                Assert.That(a.Shards, Is.EqualTo(before - AccountOps.TalentCost(t, r)));
                Assert.That(a.TalentRank(t.Id), Is.EqualTo(r + 1));
            }
            Assert.Throws<InvalidOperationException>(() => AccountOps.BuyTalent(Db, a, t.Id), "maxed");
            var poor = AccountOps.New(Db, 5);
            Assert.Throws<InvalidOperationException>(() => AccountOps.BuyTalent(Db, poor, t.Id), "no shards");
            Assert.That(poor.TalentRank(t.Id), Is.EqualTo(0));
            Assert.Throws<ArgumentException>(() => AccountOps.BuyTalent(Db, poor, "nope"));
        }

        [Test]
        public void Talents_ShapeTheNextRun()
        {
            var a = AccountOps.New(Db, 5);
            a.Shards = 100000;
            foreach (var t in Db.TalentList) AccountOps.BuyTalent(Db, a, t.Id);
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

            // a talent bought mid-run waits for the next run
            AccountOps.BuyTalent(Db, a, Talent(TalentEffect.StartSp).Id);
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
        public void AbandonRun_PaysNothing_SealsNothing_ReturnsUnusedBooks_KeepsTheSeedMoving()
        {
            var a = AccountOps.New(Db, 5);
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
