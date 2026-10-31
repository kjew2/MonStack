using System;
using System.IO;
using System.Linq;
using IdleRng.Sim.Account;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Legendary talents (issue #103, specs/18 sections 3 and 4): the tier's odds, the slot, and each rule change.</summary>
    public class LegendaryTalentTests
    {
        static ContentDb Db => TestContent.Db;
        static Constants K => Db.Constants;
        static VendorRarityDef Legendary => Db.VendorRarities.Single(r => r.LegendarySlot);

        static RunState Start(RunModifiers mods, string species = "drake", ulong seed = 3) => TrainingRun.Start(Db, species, seed, null, mods);

        static void AssertReplays(RunState s) =>
            Assert.That(TrainingRun.StateHash(TrainingRun.Replay(Db, s.SpeciesId, s.Seed, s.Log, null, s.Mods)), Is.EqualTo(TrainingRun.StateHash(s)));

        /// <summary>Drill <paramref name="stat"/> (resting below half energy, fighting when due) for <paramref name="n"/> drills.</summary>
        static void Train(RunState s, string stat, int n)
        {
            for (int i = 0; i < n && s.Phase != RunPhase.Finished;)
            {
                while (s.Offers.Count > 0) TrainingRun.DiscardOffer(Db, s);
                if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s);
                else if (s.Energy < K.RunEnergyMax / 2) TrainingRun.Rest(Db, s);
                else { TrainingRun.Drill(Db, s, stat); i++; }
            }
        }

        [Test]
        public void LegendaryTier_IsTheTopTier_WeighsZeroBelowA_AndTakesTheSlot()
        {
            Assert.That(Legendary.SortOrder, Is.EqualTo(Db.VendorRarities.Count - 1), "above Epic");
            int a = Db.FightRanks.ToList().FindIndex(r => r.Id == "a");
            for (int g = 0; g < Db.FightRanks.Count; g++)
                Assert.That(Legendary.Weight(g) > 0, Is.EqualTo(g >= a), "grade index " + g);
            Assert.That(Legendary.Weight(a + 1), Is.GreaterThan(Legendary.Weight(a)), "S rolls more legendaries than A");
            var legends = Db.TalentList.Where(t => t.Rarity == Legendary.Id).ToList();
            Assert.That(legends.Count, Is.InRange(3, 5));
            Assert.That(legends.All(t => t.MaxRank == 1 && ContentDb.IsLegendaryEffect(t.Effect) && AccountOps.TakesLegendarySlot(Db, t)));
            Assert.That(K.LegendarySlots, Is.EqualTo(1));
        }

        [Test]
        public void Vendor_SellsLegendaries_OnlyAfterAnAorSRun()
        {
            var acct = AccountOps.New(Db, 13);
            int a = Db.FightRanks.ToList().FindIndex(r => r.Id == "a");
            for (int g = 0; g < Db.FightRanks.Count; g++)
            {
                int seen = 0;
                for (int run = 0; run < 300; run++)
                    seen += AccountOps.RollVendor(Db, acct, g, run).Offers.Count(o => o.Rarity == Legendary.Id);
                if (g < a) Assert.That(seen, Is.EqualTo(0), "no legendary at grade index " + g);
                else Assert.That(seen, Is.GreaterThan(0), "legendaries at grade index " + g);
            }

            // Every other talent maxed: below A the vendor has nothing to sell (a zero-weight tier is never the fallback), at S only legendaries.
            foreach (var t in Db.TalentList.Where(t => t.Rarity != Legendary.Id)) acct.Talents[t.Id] = t.MaxRank;
            Assert.That(AccountOps.RollVendor(Db, acct, 0, 0).Offers, Is.Empty);
            Assert.That(AccountOps.RollVendor(Db, acct, Db.FightRanks.Count - 1, 0).Offers.All(o => o.Rarity == Legendary.Id && o.Talent != null));
            acct.Vendor = new VendorState { Grade = Db.FightRanks[0].Id, Offers = { new VendorOffer { Talent = Db.TalentList[0].Id, Rarity = "common" } } };
            acct.Shards = 1000;
            Assert.That(AccountOps.CanRerollVendor(Db, acct), Is.False, "a reroll at grade E would have nothing to roll");
            Assert.Throws<InvalidOperationException>(() => AccountOps.RerollVendor(Db, acct));
            Assert.That(acct.Shards, Is.EqualTo(1000));
        }

        [Test]
        public void Modifiers_OnlyTheSlottedLegendaryApplies()
        {
            var acct = AccountOps.New(Db, 1);
            foreach (var t in Db.TalentList.Where(t => t.Rarity == Legendary.Id)) acct.Talents[t.Id] = 1;
            var off = AccountOps.DefaultLoadout(Db, acct);
            Assert.That(off, Has.Count.EqualTo(Db.TalentList.Count(t => t.Rarity == Legendary.Id) - 1), "one legendary slot");
            var m = AccountOps.Modifiers(Db, acct, new[] { "gamblers_blood", "glass_cannon", "echo_training" });
            Assert.That(m.InjuryImmune, Is.True);
            Assert.That(m.RestEnergyMult, Is.EqualTo(Db.Talents["iron_constitution"].PerRank));
            Assert.That(m.NeverFail, Is.False);
            Assert.That(m.GlassCannonBonus, Is.EqualTo(0));
            Assert.That(m.EchoFrac, Is.EqualTo(0));

            acct.Talents["frugal_drills"] = 2;
            var g = AccountOps.Modifiers(Db, acct, new[] { "iron_constitution", "glass_cannon", "echo_training" });
            Assert.That(g.NeverFail, Is.True);
            Assert.That(g.DrillCostMult, Is.EqualTo((1 + 2 * Db.Talents["frugal_drills"].PerRank) * Db.Talents["gamblers_blood"].PerRank).Within(1e-12),
                "Gambler's Blood multiplies the Frugal Drills multiplier");
            Assert.That(AccountOps.Modifiers(Db, acct, new[] { "iron_constitution", "gamblers_blood", "echo_training" }).GlassCannonBonus, Is.EqualTo(Db.Talents["glass_cannon"].PerRank));
            Assert.That(AccountOps.Modifiers(Db, acct, new[] { "iron_constitution", "gamblers_blood", "glass_cannon" }).EchoFrac, Is.EqualTo(Db.Talents["echo_training"].PerRank));
        }

        [Test]
        public void IronConstitution_NeverInjures_RestsRestoreHalf()
        {
            var s = Start(new RunModifiers { InjuryImmune = true, RestEnergyMult = 0.5 }, "slime");
            s.Energy = 0;
            var r = TrainingRun.Rest(Db, s);
            Assert.That(r.Restored, Is.EqualTo(K.RestEnergy * 0.5));
            Assert.That(TrainingRun.RestEnergy(Db, s), Is.EqualTo(K.RestEnergy * 0.5));
            // Drill at 0 energy over and over (the highest injury chance): never injured, the injury roll still drawn.
            var plain = Start(null, "slime");
            for (int i = 0; i < 60 && s.Phase != RunPhase.Finished; i++)
            {
                while (s.Offers.Count > 0) TrainingRun.DiscardOffer(Db, s);
                if (s.Phase == RunPhase.AwaitFight) { TrainingRun.Fight(Db, s); continue; }
                s.Energy = 0;
                var d = TrainingRun.Drill(Db, s, StatIds.Str);
                Assert.That(d.Injured, Is.False);
                Assert.That(d.Shielded, Is.False);
            }
            Assert.That(s.Injuries, Is.Empty);
            bool plainInjured = false;
            for (int i = 0; i < 60 && plain.Phase != RunPhase.Finished && !plainInjured; i++)
            {
                while (plain.Offers.Count > 0) TrainingRun.DiscardOffer(Db, plain);
                if (plain.Phase == RunPhase.AwaitFight) { TrainingRun.Fight(Db, plain); continue; }
                plain.Energy = 0;
                plainInjured = TrainingRun.Drill(Db, plain, StatIds.Str).Injured;
            }
            Assert.That(plainInjured, Is.True, "without it the same drills injure");
        }

        [Test]
        public void GamblersBlood_NeverFails_CostsMore()
        {
            var s = Start(new RunModifiers { NeverFail = true, DrillCostMult = 2 });
            Assert.That(TrainingRun.RunFailChance(Db, s, 0), Is.EqualTo(0));
            Assert.That(TrainingRun.RunSuperChance(Db, s, 0), Is.EqualTo(K.DrillSuperP));
            Assert.That(TrainingRun.DrillCost(Db, s), Is.EqualTo(K.DrillEnergyCost * 2));
            for (int i = 0; i < 40 && s.Phase != RunPhase.Finished; i++)
            {
                while (s.Offers.Count > 0) TrainingRun.DiscardOffer(Db, s);
                if (s.Phase == RunPhase.AwaitFight) { TrainingRun.Fight(Db, s); continue; }
                if (s.Energy < K.RunEnergyMax / 2) { TrainingRun.Rest(Db, s); continue; }
                Assert.That(TrainingRun.Drill(Db, s, StatIds.Str).Tier, Is.Not.EqualTo(DrillTier.Fail));
            }
            AssertReplays(s);
        }

        [Test]
        public void GlassCannon_BoostsEveryStatButHp_HpGainsNothing()
        {
            var plain = Start(null);
            var s = Start(new RunModifiers { GlassCannonBonus = 0.3 });
            Assert.That(TrainingRun.Growth(Db, s, StatIds.Hp), Is.EqualTo(0));
            foreach (var id in StatIds.Trained.Where(x => x != StatIds.Hp))
                Assert.That(TrainingRun.Growth(Db, s, id), Is.EqualTo(TrainingRun.Growth(Db, plain, id) * 1.3).Within(1e-12), id);
            double hp = s.Stats.Hp;
            s.Energy = K.RunEnergyMax;
            Assert.That(TrainingRun.Drill(Db, s, StatIds.Hp).Gain, Is.EqualTo(0));
            Assert.That(s.Stats.Hp, Is.EqualTo(hp));
            Train(s, StatIds.Str, 10);
            AssertReplays(s);
        }

        [Test]
        public void EchoTraining_AddsAShareOfEachGainToAnotherStat()
        {
            var s = Start(new RunModifiers { EchoFrac = 0.25 });
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 30 && s.Phase != RunPhase.Finished; i++)
            {
                while (s.Offers.Count > 0) TrainingRun.DiscardOffer(Db, s);
                if (s.Phase == RunPhase.AwaitFight) { TrainingRun.Fight(Db, s); continue; }
                if (s.Energy < K.RunEnergyMax / 2) { TrainingRun.Rest(Db, s); continue; }
                var before = s.Stats.Clone();
                var d = TrainingRun.Drill(Db, s, StatIds.Str);
                Assert.That(d.EchoStat, Is.Not.Null.And.Not.EqualTo(StatIds.Str));
                Assert.That(d.EchoGain, Is.EqualTo(Math.Min(K.StatMax - before.Get(d.EchoStat), Math.Floor(d.Gain * 0.25))));
                Assert.That(s.Stats.Get(d.EchoStat), Is.EqualTo(before.Get(d.EchoStat) + d.EchoGain));
                foreach (var id in StatIds.Trained.Where(x => x != StatIds.Str && x != d.EchoStat))
                    Assert.That(s.Stats.Get(id), Is.EqualTo(before.Get(id)), "only one other stat");
                seen.Add(d.EchoStat);
            }
            Assert.That(seen.Count, Is.GreaterThan(1), "the echo stat is random");
            Assert.That(TrainingRun.Drill(Db, Start(null), StatIds.Str).EchoStat, Is.Null, "no talent, no echo");
            AssertReplays(s);
        }

        [Test]
        public void LegendaryRules_AreHashed_OnlyWhenUsed_AndRangeChecked()
        {
            string plain = TrainingRun.StateHash(Start(null));
            foreach (var m in new[]
            {
                new RunModifiers { InjuryImmune = true }, new RunModifiers { RestEnergyMult = 0.5 }, new RunModifiers { NeverFail = true },
                new RunModifiers { GlassCannonBonus = 0.3 }, new RunModifiers { EchoFrac = 0.25 },
            })
                Assert.That(TrainingRun.StateHash(Start(m)), Is.Not.EqualTo(plain));
            Assert.That(TrainingRun.StateHash(Start(new RunModifiers { EchoFrac = 0.25 })), Is.Not.EqualTo(TrainingRun.StateHash(Start(new RunModifiers { EchoFrac = 0.5 }))));
            Assert.That(TrainingRun.HashVersion, Is.EqualTo(8));
            foreach (var m in new[] { new RunModifiers { RestEnergyMult = -0.1 }, new RunModifiers { GlassCannonBonus = -1 }, new RunModifiers { EchoFrac = 1.5 } })
                Assert.Throws<ArgumentException>(() => Start(m));
        }

        [Test]
        public void LegendaryEffects_Validate_OneRank_AndPerRank()
        {
            string dir = FileContentSource.FindContentDirectory(TestContext.CurrentContext.TestDirectory);
            var files = Directory.GetFiles(dir, "*.json").ToDictionary(Path.GetFileName, File.ReadAllText);
            string tal = files["talents.json"];
            string Swap(string s, string id, string from, string to)
            {
                int i = s.IndexOf("\"id\": \"" + id + "\"", StringComparison.Ordinal);
                int j = s.IndexOf(from, i, StringComparison.Ordinal);
                return s.Substring(0, j) + to + s.Substring(j + from.Length);
            }
            tal = Swap(tal, "iron_constitution", "\"per_rank\": 0.5", "\"per_rank\": 1.5");
            tal = Swap(tal, "glass_cannon", "\"max_rank\": 1", "\"max_rank\": 2");
            files["talents.json"] = tal;
            var ex = Assert.Throws<ContentLoadException>(() => ContentDb.Load(new DictionaryContentSource(files), verifyHashes: false));
            var bad = ex.Errors.Where(e => e.Code == "E043").Select(e => e.Location).ToList();
            Assert.That(bad, Is.EquivalentTo(new[] { "talents[iron_constitution]", "talents[glass_cannon]" }));
        }
    }
}
