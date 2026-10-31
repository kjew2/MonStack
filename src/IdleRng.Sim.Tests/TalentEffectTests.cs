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
    /// <summary>Talent catalog v2 (issue #105, specs/18 section 3): each new talent effect and its run modifier.</summary>
    public class TalentEffectTests
    {
        static ContentDb Db => TestContent.Db;
        static Constants K => Db.Constants;

        static RunState Start(RunModifiers mods, string species = "drake", ulong seed = 3) => TrainingRun.Start(Db, species, seed, null, mods);

        /// <summary>Rest or fight (discarding offers) until <paramref name="until"/> holds or the run ends.</summary>
        static void Advance(RunState s, Func<RunState, bool> until)
        {
            while (s.Phase != RunPhase.Finished && !until(s))
            {
                while (s.Offers.Count > 0) TrainingRun.DiscardOffer(Db, s);
                if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s);
                else TrainingRun.Rest(Db, s);
            }
        }

        static void SetStat(MonsterStats m, string id, double v)
        {
            switch (id)
            {
                case StatIds.Hp: m.Hp = v; break;
                case StatIds.Str: m.Str = v; break;
                case StatIds.Int: m.Int = v; break;
                case StatIds.Wis: m.Wis = v; break;
                case StatIds.Dex: m.Dex = v; break;
                case StatIds.Def: m.Def = v; break;
                case StatIds.Spd: m.Spd = v; break;
                default: m.Luk = v; break;
            }
        }

        static void AssertReplays(RunState s) =>
            Assert.That(TrainingRun.StateHash(TrainingRun.Replay(Db, s.SpeciesId, s.Seed, s.Log, null, s.Mods)), Is.EqualTo(TrainingRun.StateHash(s)));

        [Test]
        public void EveryEffect_HasATalent_AndOneRankSetsItsField()
        {
            foreach (TalentEffect e in Enum.GetValues(typeof(TalentEffect)))
                Assert.That(Db.TalentList.Any(t => t.Effect == e), $"no talent uses {e}");
            var a = AccountOps.New(Db, 1);
            foreach (var t in Db.TalentList) a.Talents[t.Id] = 1;
            var m = AccountOps.Modifiers(Db, a);
            double P(TalentEffect e) => Db.TalentList.Where(t => t.Effect == e).Sum(t => t.PerRank);
            Assert.That(m.RestEnergyBonus, Is.EqualTo(P(TalentEffect.RestEnergy)).Within(1e-12));
            Assert.That(m.FailChanceDelta, Is.EqualTo(P(TalentEffect.FailChance)).Within(1e-12));
            Assert.That(m.SuperChanceBonus, Is.EqualTo(P(TalentEffect.SuperChance)).Within(1e-12));
            Assert.That(m.ShardMult, Is.EqualTo(1 + P(TalentEffect.ShardMult)).Within(1e-12));
            Assert.That(m.MomentumDrills, Is.EqualTo((int)P(TalentEffect.Momentum)));
            Assert.That(m.LossSp, Is.EqualTo((int)P(TalentEffect.LossSp)));
            Assert.That(m.SpecialistBonus, Is.EqualTo(P(TalentEffect.Specialist)).Within(1e-12));
            Assert.That(m.TreeSlotBonus, Is.EqualTo((int)P(TalentEffect.TreeSlots)));
            Assert.That(m.InjuryShields, Is.EqualTo((int)P(TalentEffect.InjuryShield)));
            Assert.That(m.LastCycleAp, Is.EqualTo((int)P(TalentEffect.LastCycleAp)));
            Assert.That(m.MilestoneMult, Is.EqualTo(1 + P(TalentEffect.MilestoneMult)).Within(1e-12));
            Assert.That(m.DraftRerolls, Is.EqualTo((int)P(TalentEffect.DraftRerolls)));

            // every talent at max rank still makes a run the engine accepts, and it plays out and replays
            foreach (var t in Db.TalentList) a.Talents[t.Id] = t.MaxRank;
            AccountOps.UnlockSpecies(Db, a, "drake");
            var run = AccountOps.StartRun(Db, a, "drake");
            for (int i = 0; i < 20 && run.Phase == RunPhase.Training; i++)
            {
                while (run.Offers.Count > 0) TrainingRun.DiscardOffer(Db, run);
                if (run.Phase == RunPhase.Training) TrainingRun.Drill(Db, run, StatIds.Str);
            }
            Advance(run, _ => false);
            AssertReplays(run);
        }

        [Test]
        public void CatalogV2Talents_AreHashed_OnlyWhenUsed()
        {
            // a run without them hashes as before (version 6); one with any of them hashes them too (version 7)
            string plain = TrainingRun.StateHash(Start(null));
            Assert.That(TrainingRun.StateHash(Start(new RunModifiers())), Is.EqualTo(plain));
            foreach (var m in new[]
            {
                new RunModifiers { RestEnergyBonus = 10 }, new RunModifiers { FailChanceDelta = -0.02 }, new RunModifiers { SuperChanceBonus = 0.03 },
                new RunModifiers { ShardMult = 1.1 }, new RunModifiers { MomentumDrills = 1 }, new RunModifiers { LossSp = 1 },
                new RunModifiers { SpecialistBonus = 0.15 }, new RunModifiers { InjuryShields = 1 }, new RunModifiers { LastCycleAp = 1 },
                new RunModifiers { MilestoneMult = 0.9 }, new RunModifiers { DraftRerolls = 1 },
            })
                Assert.That(TrainingRun.StateHash(Start(m)), Is.Not.EqualTo(plain));
            var a = TrainingRun.StateHash(Start(new RunModifiers { RestEnergyBonus = 10 }));
            Assert.That(TrainingRun.StateHash(Start(new RunModifiers { RestEnergyBonus = 20 })), Is.Not.EqualTo(a), "the values are hashed, not only a flag");
        }

        [Test]
        public void Modifiers_ClampStackedTalents()
        {
            var a = AccountOps.New(Db, 1);
            var bloom = Db.TalentList.First(t => t.Effect == TalentEffect.MilestoneMult);
            a.Talents[bloom.Id] = 100;   // more ranks than content allows, as several stacked talents could sum to
            Assert.That(AccountOps.Modifiers(Db, a).MilestoneMult, Is.EqualTo(AccountOps.MinMilestoneMult));
            var spec = Db.TalentList.First(t => t.Effect == TalentEffect.Specialist);
            a.Talents[spec.Id] = 100;
            Assert.That(AccountOps.Modifiers(Db, a).SpecialistBonus, Is.EqualTo(2));
            Assert.DoesNotThrow(() => TrainingRun.Start(Db, "slime", 1, null, AccountOps.Modifiers(Db, a)));
        }

        [Test]
        public void Start_RejectsOutOfRangeModifiers()
        {
            foreach (var m in new[]
            {
                new RunModifiers { RestEnergyBonus = -1 }, new RunModifiers { FailChanceDelta = 0.1 }, new RunModifiers { SuperChanceBonus = -0.1 },
                new RunModifiers { ShardMult = -1 }, new RunModifiers { MomentumDrills = -1 }, new RunModifiers { LossSp = -1 },
                new RunModifiers { SpecialistBonus = 2.5 }, new RunModifiers { TreeSlotBonus = -1 }, new RunModifiers { InjuryShields = -1 },
                new RunModifiers { LastCycleAp = -1 }, new RunModifiers { MilestoneMult = 0 }, new RunModifiers { DraftRerolls = -1 },
            })
                Assert.Throws<ArgumentException>(() => Start(m));
        }

        [Test]
        public void DeepRest_RestoresMore()
        {
            var s = Start(new RunModifiers { RestEnergyBonus = 10 });
            s.Energy = 0;
            var r = TrainingRun.Rest(Db, s);
            Assert.That(r.Restored, Is.EqualTo(K.RestEnergy + 10));
            Assert.That(TrainingRun.RestEnergy(Db, s), Is.EqualTo(K.RestEnergy + 10));
        }

        [Test]
        public void SteadyHandsAndSuperSet_ShiftTheTierOdds()
        {
            var plain = Start(null);
            Assert.That(TrainingRun.RunFailChance(Db, plain, 1), Is.EqualTo(K.DrillFailMin));
            Assert.That(TrainingRun.RunSuperChance(Db, plain, 1), Is.EqualTo(K.DrillSuperP));

            var s = Start(new RunModifiers { FailChanceDelta = -0.02, SuperChanceBonus = 0.03 });
            Assert.That(TrainingRun.RunFailChance(Db, s, 1), Is.EqualTo(K.DrillFailMin - 0.02).Within(1e-12));
            Assert.That(TrainingRun.RunFailChance(Db, s, 0), Is.EqualTo(K.DrillFailMax - 0.02).Within(1e-12));
            Assert.That(TrainingRun.RunSuperChance(Db, s, 1), Is.EqualTo(K.DrillSuperP + 0.03).Within(1e-12));

            // never below 0 fail, and super never takes more than what fail leaves
            var sure = Start(new RunModifiers { FailChanceDelta = -1, SuperChanceBonus = 1 });
            Assert.That(TrainingRun.RunFailChance(Db, sure, 0), Is.EqualTo(0));
            Assert.That(TrainingRun.RunSuperChance(Db, sure, 0), Is.EqualTo(1));
            var capped = Start(new RunModifiers { SuperChanceBonus = 1 });
            Assert.That(TrainingRun.RunSuperChance(Db, capped, 0), Is.EqualTo(1 - K.DrillFailMax).Within(1e-12));
            for (int i = 0; i < 3; i++) Assert.That(TrainingRun.Drill(Db, sure, StatIds.Str).Tier, Is.EqualTo(DrillTier.Super));
        }

        [Test]
        public void Scavenger_MultipliesThePayout_RoundedDown()
        {
            var plain = Start(null);
            Advance(plain, _ => false);
            var s = Start(new RunModifiers { ShardMult = 1.15 });
            Advance(s, _ => false);
            long basePay = (long)K.ShardsPerCycle * s.Cycle + (long)K.ShardsPerRank * s.Rank;
            Assert.That(AccountOps.Payout(Db, plain), Is.EqualTo((long)K.ShardsPerCycle * plain.Cycle + (long)K.ShardsPerRank * plain.Rank));
            Assert.That(AccountOps.Payout(Db, s), Is.EqualTo((long)Math.Floor(basePay * 1.15)));
        }

        [Test]
        public void Momentum_MakesTheDrillAfterASuperFree()
        {
            // every drill rolls Super here, so after the first (paid) one each drill is free and earns the next
            var s = Start(new RunModifiers { FailChanceDelta = -1, SuperChanceBonus = 1, MomentumDrills = 1 });
            var r1 = TrainingRun.Drill(Db, s, StatIds.Str);
            Assert.That(r1.Free, Is.False);
            Assert.That(s.Energy, Is.EqualTo(K.RunEnergyStart - K.DrillEnergyCost));
            Assert.That(s.FreeDrills, Is.EqualTo(1));
            Assert.That(TrainingRun.DrillCost(Db, s), Is.EqualTo(0), "the UI shows the next drill as free");
            var r2 = TrainingRun.Drill(Db, s, StatIds.Str);
            Assert.That(r2.Free, Is.True);
            Assert.That(s.Energy, Is.EqualTo(K.RunEnergyStart - K.DrillEnergyCost));

            // without a Super the free drill is used up and not renewed
            var t = Start(new RunModifiers { FailChanceDelta = -1, MomentumDrills = 1 });
            t.FreeDrills = 1;
            bool super = TrainingRun.Drill(Db, t, StatIds.Str).Tier == DrillTier.Super;
            Assert.That(t.FreeDrills, Is.EqualTo(super ? 1 : 0));
            Assert.That(t.Energy, Is.EqualTo(K.RunEnergyStart));

            // no talent, no free drills
            var plain = Start(new RunModifiers { FailChanceDelta = -1, SuperChanceBonus = 1 });
            TrainingRun.Drill(Db, plain, StatIds.Str);
            Assert.That(plain.FreeDrills, Is.EqualTo(0));
            AssertReplays(s);
        }

        [Test]
        public void GrudgeMatch_PaysSpForALoss()
        {
            foreach (int loss in new[] { 0, 2 })
            {
                var s = Start(new RunModifiers { LossSp = loss });
                foreach (var id in StatIds.Trained) SetStat(s.Stats, id, 1);   // too weak to win
                Advance(s, x => x.Phase == RunPhase.AwaitFight);
                int sp = s.Sp;
                var r = TrainingRun.Fight(Db, s);
                Assert.That(r.Won, Is.False);
                Assert.That(r.SpGained, Is.EqualTo(loss));
                Assert.That(s.Sp, Is.EqualTo(sp + loss + (s.Phase == RunPhase.Finished ? 0 : K.SpPerCycle)));
            }
        }

        [Test]
        public void Specialist_BoostsTheTopStat_AndSlowsTheRest()
        {
            var plain = Start(null);
            var s = Start(new RunModifiers { SpecialistBonus = 0.3 });
            string top = StatIds.Trained.OrderByDescending(id => s.Stats.Get(id)).First();
            string low = StatIds.Trained.First(id => s.Stats.Get(id) < s.Stats.Get(top));
            Assert.That(TrainingRun.Growth(Db, s, top), Is.EqualTo(TrainingRun.Growth(Db, plain, top) * 1.3).Within(1e-12));
            Assert.That(TrainingRun.Growth(Db, s, low), Is.EqualTo(TrainingRun.Growth(Db, plain, low) * 0.85).Within(1e-12));
            SetStat(s.Stats, low, s.Stats.Get(top));   // a tie counts as highest
            Assert.That(TrainingRun.Growth(Db, s, low), Is.EqualTo(TrainingRun.Growth(Db, plain, low) * 1.3).Within(1e-12));
        }

        [Test]
        public void FifthSlot_AddsATreeSlot()
        {
            Assert.That(Start(null).Slots.Length, Is.EqualTo(K.TreeSlots));
            var s = Start(new RunModifiers { TreeSlotBonus = 1 });
            Assert.That(s.Slots.Length, Is.EqualTo(K.TreeSlots + 1));
            Assert.That(s.Slots.Skip(2).All(x => x == null), "the extra slot starts empty");
        }

        [Test]
        public void SecondWind_PreventsTheFirstInjury()
        {
            for (ulong seed = 1; seed < 40; seed++)
            {
                var s = Start(new RunModifiers { InjuryShields = 1 }, "slime", seed);
                DrillResult shielded = null;
                for (int i = 0; i < 200 && s.Phase != RunPhase.Finished && s.Injuries.Count == 0; i++)
                {
                    while (s.Offers.Count > 0) TrainingRun.DiscardOffer(Db, s);
                    if (s.Phase == RunPhase.AwaitFight) { TrainingRun.Fight(Db, s); continue; }
                    s.Energy = 0;   // injury risk is highest at 0 energy
                    var r = TrainingRun.Drill(Db, s, StatIds.Str);
                    if (r.Shielded)
                    {
                        Assert.That(shielded, Is.Null, "only one injury is prevented");
                        Assert.That(r.Injured, Is.False);
                        Assert.That(r.Injury, Is.Null);
                        Assert.That(s.Energy, Is.EqualTo(0));
                        Assert.That(s.Injuries, Is.Empty);
                        shielded = r;
                    }
                }
                if (shielded == null || s.Injuries.Count == 0) continue;
                Assert.That(s.ShieldsUsed, Is.EqualTo(1));
                Assert.That(s.Injuries.Count, Is.EqualTo(1), "the next injury is real");
                return;   // no replay check: the test sets energy outside the command log
            }
            Assert.Fail("no seed showed a prevented injury followed by a real one");
        }

        [Test]
        public void Overtime_AddsApToTheLastCycleOnly()
        {
            var s = Start(new RunModifiers { LastCycleAp = 2 });
            Assert.That(s.ApLeft, Is.EqualTo(K.RunAp));
            Advance(s, x => x.Cycle == K.RunCycles);
            Assert.That(s.ApLeft, Is.EqualTo(K.RunAp + 2));
            int rests = 0;
            while (s.Phase == RunPhase.Training) { TrainingRun.Rest(Db, s); rests++; }
            Assert.That(rests, Is.EqualTo(K.RunAp + 2));
        }

        [Test]
        public void EarlyBloom_LowersTheMilestoneValue_TheHitKeepsItsThreshold()
        {
            int t = Db.StatMilestones[0];
            var s = Start(new RunModifiers { MilestoneMult = 0.9 });
            double at = TrainingRun.MilestoneAt(s, t);
            Assert.That(at, Is.EqualTo(Math.Round(t * 0.9)));
            Assert.That(TrainingRun.MilestoneAt(Start(null), t), Is.EqualTo(t));
            s.Stats.Str = at - 1;
            while (s.Stats.Str < at) { s.Energy = K.RunEnergyMax; TrainingRun.Drill(Db, s, StatIds.Str); }
            Assert.That(s.Milestones.Any(m => m.Stat == StatIds.Str && m.Threshold == t), "the hit names the content threshold");
            Assert.That(s.Offers.Single().Threshold, Is.EqualTo(t));
        }

        [Test]
        public void SecondLook_RerollsAMilestoneDraftOnce()
        {
            int t = Db.StatMilestones[0];
            int pool = Db.TreeList.Count(x => x.Kind == TreeKind.Generic && x.OfferThreshold <= t);   // any stat's trees
            Assume.That(pool, Is.GreaterThan(K.DraftSize), "the pool needs other trees to reroll into");
            // commands only, so the run replays: Early Bloom halves the threshold and drake's INT starts below it
            var s = Start(new RunModifiers { DraftRerolls = 1, MilestoneMult = 0.5 });
            Assume.That(s.Stats.Int, Is.LessThan(TrainingRun.MilestoneAt(s, t)));
            while (s.Offers.Count == 0 && s.Phase != RunPhase.Finished)
            {
                if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s);
                else if (s.Energy < 50) TrainingRun.Rest(Db, s);
                else TrainingRun.Drill(Db, s, StatIds.Int);
            }
            Assert.That(s.Offers.Single().Stat, Is.EqualTo(StatIds.Int));
            var before = s.Offers[0].Trees.ToList();
            Assert.That(TrainingRun.CanRerollDraft(Db, s));
            TrainingRun.RerollDraft(Db, s);
            var after = s.Offers[0].Trees;
            Assert.That(after, Is.Not.Empty);
            Assert.That(after.Count, Is.EqualTo(Math.Min(K.DraftSize, pool - before.Count)));
            Assert.That(after.Intersect(before), Is.Empty, "a reroll draws new trees");
            Assert.That(s.Offers[0].Source, Is.EqualTo(OfferSource.Milestone));
            Assert.That(s.RerollsUsed, Is.EqualTo(1));
            Assert.That(after.Any(id => Db.Trees[id].OfferStat != null && Db.Trees[id].OfferStat != StatIds.Int), "a reroll draws other stats' trees too");
            Assert.That(TrainingRun.CanRerollDraft(Db, s), Is.False);
            Assert.Throws<InvalidOperationException>(() => TrainingRun.RerollDraft(Db, s), "one reroll per rank");
            AssertReplays(s);

            var none = Start(null);
            none.Stats.Str = t - 1;
            while (none.Offers.Count == 0) { none.Energy = K.RunEnergyMax; TrainingRun.Drill(Db, none, StatIds.Str); }
            Assert.That(TrainingRun.CanRerollDraft(Db, none), Is.False);
            Assert.Throws<InvalidOperationException>(() => TrainingRun.RerollDraft(Db, none), "no talent, no reroll");
            Assert.Throws<InvalidOperationException>(() => TrainingRun.RerollDraft(Db, Start(new RunModifiers { DraftRerolls = 1 })), "no offer");
        }

        [Test]
        public void TalentEffects_ValidatePerRank()
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
            tal = Swap(tal, "scavenger", "\"per_rank\": 0.1", "\"per_rank\": -0.1");
            tal = Swap(tal, "steady_hands", "\"per_rank\": -0.02", "\"per_rank\": 0.02");
            tal = Swap(tal, "specialist", "\"per_rank\": 0.15", "\"per_rank\": 1.5");
            tal = Swap(tal, "early_bloom", "\"per_rank\": -0.1", "\"per_rank\": -0.6");
            tal = Swap(tal, "overtime", "\"per_rank\": 1.0", "\"per_rank\": 1.5");
            files["talents.json"] = tal;
            var ex = Assert.Throws<ContentLoadException>(() => ContentDb.Load(new DictionaryContentSource(files), verifyHashes: false));
            var bad = ex.Errors.Where(e => e.Code == "E043").Select(e => e.Location).ToList();
            Assert.That(bad, Is.EquivalentTo(new[] { "talents[scavenger]", "talents[steady_hands]", "talents[specialist]", "talents[early_bloom]", "talents[overtime]" }));
        }
    }
}
