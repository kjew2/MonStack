using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>The training run (specs/14): AP cycles of Drill/Rest, drill rolls, injury, milestones, rank fights, replay.</summary>
    public class TrainingRunTests
    {
        static ContentDb Db => TestContent.Db;
        static Constants K => Db.Constants;
        static string Species => Db.SpeciesList[0].Id;

        static RunState NewRun(ulong seed = 7) => TrainingRun.Start(Db, Species, seed);

        /// <summary>Spend the rest of the cycle's AP on rests.</summary>
        static void FinishCycleWithRests(RunState s)
        {
            while (s.Phase == RunPhase.Training && s.ApLeft > 0) TrainingRun.Rest(Db, s);
        }

        [Test]
        public void Start_UsesSpeciesBaseAndConstants()
        {
            var sp = Db.SpeciesList[0];
            var s = NewRun();
            foreach (var id in StatIds.Trained) Assert.That(s.Stats.Get(id), Is.EqualTo(sp.Base.Get(id)));
            Assert.That(s.Energy, Is.EqualTo(K.RunEnergyStart));
            Assert.That(s.Cycle, Is.EqualTo(1));
            Assert.That(s.ApLeft, Is.EqualTo(K.RunAp));
            Assert.That(s.Rank, Is.EqualTo(0));
            Assert.That(s.Phase, Is.EqualTo(RunPhase.Training));
            Assert.That(s.Stats, Is.Not.SameAs(sp.Base), "the run owns its stats");
            Assert.Throws<ArgumentException>(() => TrainingRun.Start(Db, "no_such_species", 1));
        }

        [Test]
        public void Rest_RestoresEnergyCapped_CostsOneAp_DrawsNothing()
        {
            var s = NewRun();
            s.Energy = 10;
            ulong counter = s.Training.Counter;
            var r = TrainingRun.Rest(Db, s);
            Assert.That(r.Restored, Is.EqualTo(Math.Min(K.RestEnergy, K.RunEnergyMax - 10)));
            Assert.That(s.Energy, Is.EqualTo(Math.Min(K.RunEnergyMax, 10 + K.RestEnergy)));
            Assert.That(s.ApLeft, Is.EqualTo(K.RunAp - 1));
            Assert.That(s.Training.Counter, Is.EqualTo(counter));
            s.Energy = K.RunEnergyMax;
            Assert.That(TrainingRun.Rest(Db, s).Restored, Is.EqualTo(0));
        }

        [Test]
        public void Drill_GainInsideTierRange_TimesGrowth_CostsEnergyAndAp()
        {
            var growth = Db.SpeciesList[0].Growth;
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var s = NewRun(seed);
                double before = s.Stats.Str;
                ulong counter = s.Training.Counter;
                var r = TrainingRun.Drill(Db, s, StatIds.Str);
                var tier = Db.DrillTiers[(int)r.Tier];
                Assert.That(r.Gain, Is.InRange(Math.Floor(tier.GainMin * growth.Str), Math.Floor(tier.GainMax * growth.Str)));
                Assert.That(s.Stats.Str, Is.EqualTo(before + r.Gain));
                Assert.That(s.Energy, Is.EqualTo(Math.Max(0, K.RunEnergyStart - K.DrillEnergyCost)));
                Assert.That(s.ApLeft, Is.EqualTo(K.RunAp - 1));
                Assert.That(s.Training.Counter - counter, Is.EqualTo(2UL), "tier and gain rolls; no injury roll at full energy");
            }
            Assert.Throws<ArgumentException>(() => TrainingRun.Drill(Db, NewRun(), "atk"));
        }

        [Test]
        public void Odds_AreMonotoneInEnergy()
        {
            Assert.That(TrainingRun.FailChance(K, 1), Is.EqualTo(K.DrillFailMin).Within(1e-12));
            Assert.That(TrainingRun.FailChance(K, 0), Is.EqualTo(K.DrillFailMax).Within(1e-12));
            Assert.That(TrainingRun.InjuryChance(K, 1), Is.EqualTo(0));
            Assert.That(TrainingRun.InjuryChance(K, K.InjuryBelow), Is.EqualTo(0));
            Assert.That(TrainingRun.InjuryChance(K, 0), Is.EqualTo(K.InjuryMaxP).Within(1e-12));
            Assert.That(TrainingRun.RollTier(K, 1, 0), Is.EqualTo(DrillTier.Fail));
            Assert.That(TrainingRun.RollTier(K, 1, K.DrillFailMin + 1e-9), Is.EqualTo(DrillTier.Success));
            Assert.That(TrainingRun.RollTier(K, 1, 1 - K.DrillSuperP + 1e-9), Is.EqualTo(DrillTier.Super));
            Assert.That(TrainingRun.RollTier(K, 0, K.DrillFailMax - 1e-9), Is.EqualTo(DrillTier.Fail), "low energy fails more");
        }

        [Test]
        public void SpentAp_AwaitsFight_ThenFightStartsNextCycle()
        {
            var s = NewRun();
            FinishCycleWithRests(s);
            Assert.That(s.Phase, Is.EqualTo(RunPhase.AwaitFight), "FIGHT_EVERY = 1 in content");
            Assert.Throws<InvalidOperationException>(() => TrainingRun.Rest(Db, s));
            Assert.Throws<InvalidOperationException>(() => TrainingRun.Drill(Db, s, StatIds.Str));
            ulong counter = s.Training.Counter;
            var r = TrainingRun.Fight(Db, s);
            Assert.That(s.Training.Counter, Is.EqualTo(counter), "fights never draw from the training stream");
            Assert.That(r.RankAfter, Is.EqualTo(r.Won ? Math.Min(1, Db.FightRanks.Count - 1) : 0));
            Assert.That(s.Cycle, Is.EqualTo(2));
            Assert.That(s.ApLeft, Is.EqualTo(K.RunAp));
            Assert.That(s.Phase, Is.EqualTo(RunPhase.Training));
            Assert.Throws<InvalidOperationException>(() => TrainingRun.Fight(Db, s));
        }

        [Test]
        public void StrongMonster_WinsAndRanksUp_TopRankHolds()
        {
            var s = NewRun();
            foreach (var id in StatIds.Trained) SetStat(s.Stats, id, K.StatMax);
            for (int i = 0; i < Db.FightRanks.Count + 1; i++)
            {
                FinishCycleWithRests(s);
                var r = TrainingRun.Fight(Db, s);
                Assert.That(r.Won, Is.True, $"max stats beat rank {Db.FightRanks[r.RankBefore].Id}");
            }
            Assert.That(s.Rank, Is.EqualTo(Db.FightRanks.Count - 1));
            Assert.That(s.FightsWon, Is.EqualTo(Db.FightRanks.Count + 1));
        }

        [Test]
        public void FullRun_CompletesAfterRunCycles()
        {
            var s = NewRun();
            while (s.Phase != RunPhase.Finished)
            {
                if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s);
                else TrainingRun.Rest(Db, s);
            }
            Assert.That(s.End, Is.EqualTo(RunEnd.Completed));
            Assert.That(s.Cycle, Is.EqualTo(K.RunCycles));
            Assert.That(s.FightsFought, Is.EqualTo(K.RunCycles / K.FightEvery));
            Assert.That(s.Rests, Is.EqualTo(K.RunCycles * K.RunAp));
            Assert.Throws<InvalidOperationException>(() => TrainingRun.Rest(Db, s));
            Assert.Throws<InvalidOperationException>(() => TrainingRun.Fight(Db, s));
        }

        /// <summary>A run on <paramref name="seed"/> played with rests and fights to the start of <paramref name="cycle"/>.</summary>
        static RunState RunAtCycle(ulong seed, int cycle)
        {
            var s = NewRun(seed);
            while (s.Cycle < cycle)
            {
                if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s);
                else TrainingRun.Rest(Db, s);
            }
            return s;
        }

        /// <summary>The first seed whose run, at the start of <paramref name="cycle"/> with energy set to 0, is injured by its first drill.</summary>
        static (RunState s, DrillResult r, int sp, int fights) InjuredAt(int cycle)
        {
            for (ulong seed = 1; seed <= 200; seed++)
            {
                var s = RunAtCycle(seed, cycle);
                int sp = s.Sp, fights = s.FightsFought;
                s.Energy = 0;
                var r = TrainingRun.Drill(Db, s, StatIds.Def);
                if (r.Injured) return (s, r, sp, fights);
            }
            Assert.Fail($"no injury in cycle {cycle} within 200 seeds");
            return default;
        }

        [Test]
        public void Injury_LosesTheCycleAndItsFight_AndTheNextCycle_ThenTheRunGoesOn()
        {
            Assume.That(K.RunCycles, Is.GreaterThanOrEqualTo(3 + K.InjurySkipCycles), "room for a cycle after the lost ones");
            var (s, r, spBefore, fightsBefore) = InjuredAt(1);
            double def = Db.SpeciesList[0].Base.Def;
            Assert.That(s.Stats.Def, Is.EqualTo(def + r.Gain), "the injuring drill's gain still counts");
            Assert.That(s.Training.Counter, Is.EqualTo(3UL), "tier, gain and injury rolls; the injury itself draws nothing more");
            int back = 2 + K.InjurySkipCycles;
            Assert.That(r.Injury, Is.Not.Null);
            Assert.That((r.Injury.Cycle, r.Injury.CyclesLost, r.Injury.BackCycle), Is.EqualTo((1, K.InjurySkipCycles, back)));
            Assert.That(s.Injuries, Has.Count.EqualTo(1));
            Assert.That(s.Injuries[0], Is.SameAs(r.Injury));
            Assert.That(s.Phase, Is.EqualTo(RunPhase.Training), "the run is not over");
            Assert.That(s.End, Is.EqualTo(RunEnd.None));
            Assert.That(s.Cycle, Is.EqualTo(back));
            Assert.That(s.ApLeft, Is.EqualTo(K.RunAp));
            Assert.That(s.Energy, Is.EqualTo(K.InjuryRecoverEnergy));
            Assert.That(s.FightsFought, Is.EqualTo(fightsBefore), "the injured cycle's fight and the lost cycles' fights are skipped");
            Assert.That(s.Sp, Is.EqualTo(spBefore + K.SpPerCycle), "lost cycles pay no SP; the cycle the monster is back in does");
            Assert.That(s.Drills, Is.EqualTo(1));

            while (s.Phase != RunPhase.Finished)
            {
                if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s);
                else TrainingRun.Rest(Db, s);
            }
            Assert.That(s.End, Is.EqualTo(RunEnd.Completed));
            Assert.That(s.Cycle, Is.EqualTo(K.RunCycles));
            int fought = 0;
            for (int c = back; c <= K.RunCycles; c++) if (c % K.FightEvery == 0) fought++;
            Assert.That(s.FightsFought, Is.EqualTo(fought));
        }

        [Test]
        public void Injury_InTheLastCycles_FinishesTheRunAsCompleted()
        {
            var (s, r, _, fights) = InjuredAt(K.RunCycles);
            Assert.That(s.Phase, Is.EqualTo(RunPhase.Finished));
            Assert.That(s.End, Is.EqualTo(RunEnd.Completed), "a new run never ends Injured");
            Assert.That((r.Injury.Cycle, r.Injury.CyclesLost, r.Injury.BackCycle), Is.EqualTo((K.RunCycles, 0, 0)));
            Assert.That(s.Cycle, Is.EqualTo(K.RunCycles));
            Assert.That(s.FightsFought, Is.EqualTo(fights), "the last fight is skipped");
            Assert.Throws<InvalidOperationException>(() => TrainingRun.Drill(Db, s, StatIds.Def));

            if (K.InjurySkipCycles < 1 || K.RunCycles < 2) return;
            (s, r, _, _) = InjuredAt(K.RunCycles - 1);
            Assert.That(s.Phase, Is.EqualTo(RunPhase.Finished), "the lost cycle was the last one");
            Assert.That(s.End, Is.EqualTo(RunEnd.Completed));
            Assert.That((r.Injury.Cycle, r.Injury.CyclesLost, r.Injury.BackCycle), Is.EqualTo((K.RunCycles - 1, 1, 0)));
            Assert.That(s.Cycle, Is.EqualTo(K.RunCycles));
        }

        [Test]
        public void ARunThatNeverRests_IsInjured_FinishesCompleted_AndReplays()
        {
            int injured = 0;
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var s = NewRun(seed);
                while (s.Phase != RunPhase.Finished)
                {
                    if (s.Offers.Count > 0) TrainingRun.DiscardOffer(Db, s);
                    else if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s);
                    else TrainingRun.Drill(Db, s, StatIds.Str);
                }
                Assert.That(s.End, Is.EqualTo(RunEnd.Completed));
                Assert.That(s.Cycle, Is.EqualTo(K.RunCycles));
                if (s.Injuries.Count > 0) injured++;
                var replayed = TrainingRun.Replay(Db, Species, seed, s.Log);
                Assert.That(TrainingRun.StateHash(replayed), Is.EqualTo(TrainingRun.StateHash(s)), $"seed {seed}");
                Assert.That(replayed.Injuries.Select(i => (i.Cycle, i.BackCycle, i.CyclesLost)),
                            Is.EqualTo(s.Injuries.Select(i => (i.Cycle, i.BackCycle, i.CyclesLost))));
                Assert.That(TrainingRun.StateHash(s.Clone()), Is.EqualTo(TrainingRun.StateHash(s)));
            }
            Assert.That(injured, Is.GreaterThan(0), "drilling at zero energy injures");
        }

        [Test]
        public void StateHash_CoversInjuries()
        {
            var (s, _, _, _) = InjuredAt(1);
            var c = s.Clone();
            c.Injuries[0].BackCycle++;
            Assert.That(TrainingRun.StateHash(c), Is.Not.EqualTo(TrainingRun.StateHash(s)));
            Assert.That(s.Injuries[0].BackCycle, Is.EqualTo(2 + K.InjurySkipCycles), "Clone copies the injuries");
        }

        [Test]
        public void CrossingAThreshold_RecordsAMilestone_AndStatsCapAtMax()
        {
            int t = Db.StatMilestones[0];
            var s = NewRun();
            s.Stats.Int = t - 1;
            DrillResult r = null;
            while (s.Stats.Int < t && s.Phase == RunPhase.Training) { s.Energy = K.RunEnergyMax; r = TrainingRun.Drill(Db, s, StatIds.Int); }
            Assert.That(s.Milestones.Single().Threshold, Is.EqualTo(t));
            Assert.That(s.Milestones[0].Stat, Is.EqualTo(StatIds.Int));
            Assert.That(r.Milestones, Has.Count.EqualTo(1));

            var c = NewRun(3);
            c.Stats.Luk = K.StatMax - 1;
            for (int i = 0; i < 4 && c.Phase == RunPhase.Training; i++) { c.Energy = K.RunEnergyMax; TrainingRun.Drill(Db, c, StatIds.Luk); }
            Assert.That(c.Stats.Luk, Is.LessThanOrEqualTo(K.StatMax));
        }

        [Test]
        public void Replay_RebuildsTheSameRun()
        {
            foreach (var sp in Db.SpeciesList)
            {
                var s = TrainingRun.Start(Db, sp.Id, 11);
                int n = 0;
                while (s.Phase != RunPhase.Finished)
                {
                    if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s);
                    else if (s.Energy < 40) TrainingRun.Rest(Db, s);
                    else TrainingRun.Drill(Db, s, StatIds.Trained[n++ % StatIds.Trained.Length]);
                }
                var replayed = TrainingRun.Replay(Db, sp.Id, 11, s.Log);
                Assert.That(TrainingRun.StateHash(replayed), Is.EqualTo(TrainingRun.StateHash(s)), sp.Id);
                var clone = s.Clone();
                Assert.That(TrainingRun.StateHash(clone), Is.EqualTo(TrainingRun.StateHash(s)));
            }
        }

        [Test]
        public void FightEvery2_FightsOnEvenCyclesOnly()
        {
            var db = WithConstant("FIGHT_EVERY", 2);
            var s = TrainingRun.Start(db, Species, 5);
            while (s.Phase == RunPhase.Training && s.Cycle == 1) TrainingRun.Rest(db, s);
            Assert.That(s.Cycle, Is.EqualTo(2), "cycle 1 ends without a fight");
            Assert.That(s.Phase, Is.EqualTo(RunPhase.Training));
            while (s.Phase == RunPhase.Training) TrainingRun.Rest(db, s);
            Assert.That(s.Phase, Is.EqualTo(RunPhase.AwaitFight));
            Assert.That(s.Cycle, Is.EqualTo(2));
        }

        static ContentDb WithConstant(string key, double value)
        {
            string dir = FileContentSource.FindContentDirectory(TestContext.CurrentContext.TestDirectory);
            var files = Directory.GetFiles(dir, "*.json").ToDictionary(Path.GetFileName, f => File.ReadAllText(f));
            var doc = JsonNode.Parse(files["constants.json"]);
            foreach (var row in doc["rows"].AsArray())
                if ((string)row["key"] == key) row["value"] = value;
            files["constants.json"] = doc.ToJsonString();
            return ContentDb.Load(new DictionaryContentSource(files), verifyHashes: false);
        }

        static void SetStat(MonsterStats s, string id, double v)
        {
            switch (id)
            {
                case StatIds.Hp: s.Hp = v; break;
                case StatIds.Str: s.Str = v; break;
                case StatIds.Int: s.Int = v; break;
                case StatIds.Wis: s.Wis = v; break;
                case StatIds.Dex: s.Dex = v; break;
                case StatIds.Def: s.Def = v; break;
                case StatIds.Spd: s.Spd = v; break;
                case StatIds.Luk: s.Luk = v; break;
            }
        }

        [Test]
        public void RunRuleHooks_AreNoOpsUntilALaneFillsThem()
        {
            // Wave 0 (#133): the lane F and G hooks change nothing yet.
            var s = NewRun();
            Assert.That(TrainingRun.CanRest(Db, s), Is.True);
            foreach (var id in StatIds.Trained) Assert.That(TrainingRun.StatCap(Db, s, id), Is.EqualTo(K.StatMax), id);
            Assert.That(TrainingRun.DrillCost(Db, s), Is.EqualTo(K.DrillEnergyCost * s.Mods.DrillCostMult));
            Assert.That(TrainingRun.RestEnergy(Db, s), Is.EqualTo((K.RestEnergy + s.Mods.RestEnergyBonus) * s.Mods.RestEnergyMult));
        }
    }
}
