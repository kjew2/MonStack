using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Gauntlet;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>
    /// Idea-pool lane G (#140): the run-rule capstones iron_vow, cursed_mark, devourer, second_breath, ascetic and hungry_core
    /// (specs/14 section 4.3, Training/Rules/LaneG.cs).
    /// </summary>
    public class LaneGRuleTests
    {
        static ContentDb Db => TestContent.Db;
        static Constants K => Db.Constants;
        static double G(string key) => Db.ConstantValues[key];

        static readonly string[] Trees = { "iron_vow", "cursed_mark", "devourer", "second_breath", "ascetic", "hungry_core" };

        /// <summary>A run with <paramref name="tree"/> in the last slot and its first <paramref name="bought"/> nodes bought (unrolled, free).</summary>
        static RunState Holding(string tree, int bought = 8, string species = "drake", ulong seed = 5)
        {
            var s = TrainingRun.Start(Db, species, seed);
            if (tree == null) return s;
            int slot = s.Slots.Length - 1;
            s.Offers.Insert(0, new TreeOffer { Source = OfferSource.Book, Trees = { tree } });
            TrainingRun.TakeTree(Db, s, 0, slot);
            for (int i = 0; i < bought; i++)
            {
                s.Sp = Math.Max(s.Sp, 10);
                TrainingRun.SpendPoint(Db, s, slot);
            }
            return s;
        }

        static void SetStat(MonsterStats st, string id, double v)
        {
            switch (id)
            {
                case StatIds.Hp: st.Hp = v; break;
                case StatIds.Str: st.Str = v; break;
                case StatIds.Int: st.Int = v; break;
                case StatIds.Wis: st.Wis = v; break;
                case StatIds.Dex: st.Dex = v; break;
                case StatIds.Def: st.Def = v; break;
                case StatIds.Spd: st.Spd = v; break;
                default: st.Luk = v; break;
            }
        }

        /// <summary>Make the monster win every rank fight.</summary>
        static void Mighty(RunState s)
        {
            foreach (var id in StatIds.Trained) SetStat(s.Stats, id, K.StatMax);
        }

        /// <summary>Spend the cycle's AP on Rest (the vow aside), leaving the run at its fight.</summary>
        static void RestOut(RunState s)
        {
            while (s.Phase == RunPhase.Training && s.ApLeft > 0) TrainingRun.Rest(Db, s);
        }

        // ------------------------------------------------------------------ content

        [Test]
        public void Content_EveryTree_IsAnIdeaPoolPlaceholderTree_WithItsRuleCapstone()
        {
            var stats = new Dictionary<string, string>
            {
                ["iron_vow"] = StatIds.Wis, ["cursed_mark"] = StatIds.Def, ["devourer"] = StatIds.Str,
                ["second_breath"] = StatIds.Hp, ["ascetic"] = StatIds.Dex, ["hungry_core"] = StatIds.Int,
            };
            foreach (var id in Trees)
            {
                var t = Db.Trees[id];
                IdeaPool.AssertOfferedOrNative(Db, t, stats[id]);
                Assert.That(t.Nodes.Select(n => n.Kind), Is.EqualTo(new[]
                {
                    NodeKind.Growth, NodeKind.Stat, NodeKind.Growth, NodeKind.Stat, NodeKind.Skill, NodeKind.Growth, NodeKind.Stat, NodeKind.Rule,
                }), id);
                Assert.That(t.Nodes.Where(n => n.Kind == NodeKind.Growth || n.Kind == NodeKind.Stat).Select(n => n.Stat), Is.All.EqualTo(stats[id]), id);
                Assert.That(t.Nodes[4].Cost, Is.EqualTo(2));
                Assert.That(t.Nodes[7].Cost, Is.EqualTo(3));
                Assert.That(t.Nodes[7].RuleId, Is.EqualTo(id));
                Assert.That(RuleModifiers.IsKnown(id), id);
                Assert.That(Db.Text("tree." + id + ".name"), Is.Not.EqualTo("tree." + id + ".name"));
                string desc = BattleText.RuleDesc(Db, id);
                Assert.That(desc, Does.Not.Contain("{"), id + ": every placeholder is filled");
                Assert.That(Db.Text("rule." + id + ".name"), Is.Not.Empty);
            }
            foreach (var k in new[] { "VOW_GAIN", "CURSE_FOE", "DEVOUR_FRAC", "DEVOUR_ENERGY", "BREATH_SAFE", "ASCETIC_AP", "HUNGRY_STAT", "HUNGRY_ENERGY" })
                Assert.That(Db.ConstantValues.ContainsKey(k), k);
        }

        [Test]
        public void ARule_TakesEffectWhenItsCapstoneIsBought_AndEndsWhenTheTreeIsOverwritten()
        {
            foreach (var id in Trees)
            {
                var s = Holding(id, 7);
                Assert.That(TrainingRun.HasRunRule(Db, s, id), Is.False, id + ": 7 of 8 nodes");
                TrainingRun.SpendPoint(Db, s, s.Slots.Length - 1);
                Assert.That(TrainingRun.HasRunRule(Db, s, id), Is.True, id);
                Assert.That(Trees.Where(x => x != id).Any(x => TrainingRun.HasRunRule(Db, s, x)), Is.False);
                s.Offers.Insert(0, new TreeOffer { Source = OfferSource.Book, Trees = { "vital_core" } });
                TrainingRun.TakeTree(Db, s, 0, s.Slots.Length - 1);
                Assert.That(TrainingRun.HasRunRule(Db, s, id), Is.False, id + ": overwritten");
            }
        }

        // ------------------------------------------------------------------ iron_vow

        [Test]
        public void IronVow_RefusesRest_AndRaisesEveryDrillsGain()
        {
            var off = Holding("iron_vow", 7);
            var on = Holding("iron_vow", 7);
            TrainingRun.SpendPoint(Db, on, on.Slots.Length - 1);
            off.Sp = on.Sp;
            Assert.That(TrainingRun.CanRest(Db, off), Is.True);
            Assert.That(TrainingRun.CanRest(Db, on), Is.False);
            Assert.Throws<InvalidOperationException>(() => TrainingRun.Rest(Db, on));
            for (int i = 0; i < 3; i++)
            {
                var a = TrainingRun.Drill(Db, off, StatIds.Str);
                var b = TrainingRun.Drill(Db, on, StatIds.Str);
                Assert.That(b.Tier, Is.EqualTo(a.Tier), "the vow draws nothing");
                Assert.That(b.Gain, Is.EqualTo(Math.Floor(a.Gain * (1 + G("VOW_GAIN")))));
            }
            // Overwriting the tree breaks the vow.
            on.Offers.Insert(0, new TreeOffer { Source = OfferSource.Book, Trees = { "vital_core" } });
            TrainingRun.TakeTree(Db, on, 0, on.Slots.Length - 1);
            Assert.That(TrainingRun.CanRest(Db, on), Is.True);
        }

        [Test]
        public void IronVow_AtZeroEnergy_TheRunStillMovesOn()
        {
            // No soft-lock: Drill is never refused for energy (specs/14 section 4), so a vowed run at 0 energy keeps drilling
            // (high fail and injury odds) until it ends.
            var s = Holding("iron_vow");
            s.Energy = 0;
            int guard = 0;
            while (s.Phase != RunPhase.Finished)
            {
                Assert.That(++guard, Is.LessThan(1000));
                if (s.Offers.Count > 0) TrainingRun.DiscardOffer(Db, s);   // milestones
                else if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s);
                else TrainingRun.Drill(Db, s, StatIds.Wis);
            }
            Assert.That(s.Rests, Is.Zero);
            Assert.That(s.Cycle, Is.EqualTo(K.RunCycles));
        }

        // ------------------------------------------------------------------ cursed_mark

        [Test]
        public void CursedMark_DrillsGiveNoDef_AndRankFightEnemiesAreWeakened()
        {
            var s = Holding("cursed_mark");
            Assert.That(TrainingRun.Growth(Db, s, StatIds.Def), Is.Zero);
            Assert.That(TrainingRun.Growth(Db, s, StatIds.Str), Is.GreaterThan(0));
            double def = s.Stats.Def;
            var r = TrainingRun.Drill(Db, s, StatIds.Def);
            Assert.That(r.Gain, Is.Zero);
            Assert.That(s.Stats.Def, Is.EqualTo(def));

            RestOut(s);
            var foe = TrainingRun.OpponentFighter(Db, s, s.Rank).Stats;
            MonsterStats seen = null;
            TrainingRun.Fight(Db, s, null, st => seen = st.Units[1].Source.Stats.Clone());
            foreach (var id in StatIds.Trained) Assert.That(seen.Get(id), Is.EqualTo(foe.Get(id) * (1 - G("CURSE_FOE"))).Within(1e-9), id);
        }

        [Test]
        public void CursedMark_WeakensEveryGauntletEnemy_ButNotABattleOfOtherModes()
        {
            var s = Holding("cursed_mark");
            var plain = TrainingRun.MonsterFighter(Db, s);
            plain.Rules.Remove(RuleModifiers.CursedMark);
            var cursed = TrainingRun.MonsterFighter(Db, s);
            Assert.That(cursed.Rules, Does.Contain(RuleModifiers.CursedMark));
            var a = GauntletSim.Run(Db, plain, 9);
            var b = GauntletSim.Run(Db, cursed, 9);
            Assert.That(b.FloorReached, Is.GreaterThanOrEqualTo(a.FloorReached));
            Assert.That(b.Floors[0].FightHash, Is.Not.EqualTo(a.Floors[0].FightHash), "the first floor's enemy is weakened");

            // In BattleSim itself (Dungeon, Endless, PvP) a lane G rule does nothing: 1v1 and 4v4 play out the same.
            var rules = FightRules.FromContent(Db);
            var foe = GauntletSim.Enemy(Db, 3, GauntletSim.Levels(Db)[2]);
            var x = BattleSim.Resolve(rules, new[] { plain }, new[] { foe }, 4);
            var y = BattleSim.Resolve(rules, new[] { cursed }, new[] { foe }, 4);
            Assert.That(y.Outcome, Is.EqualTo(x.Outcome));
            Assert.That(y.Ticks, Is.EqualTo(x.Ticks));
            Assert.That(y.Final.Units.Select(u => u.Hp), Is.EqualTo(x.Final.Units.Select(u => u.Hp)));
        }

        // ------------------------------------------------------------------ devourer

        [Test]
        public void Devourer_AWinFeedsOnTheOpponentsHighestStat_AndEachCycleCostsEnergy()
        {
            var s = Holding("devourer", species: "slime");
            foreach (var id in StatIds.Trained) SetStat(s.Stats, id, 500);   // wins at rank 0, below the cap
            RestOut(s);
            var foe = TrainingRun.OpponentFighter(Db, s, s.Rank).Stats;
            string top = StatIds.Trained.Aggregate((p, q) => foe.Get(q) > foe.Get(p) ? q : p);
            double before = s.Stats.Get(top), energy = s.Energy;
            var r = TrainingRun.Fight(Db, s);
            Assert.That(r.Won, Is.True);
            Assert.That(r.DevouredStat, Is.EqualTo(top));
            Assert.That(r.DevouredGain, Is.EqualTo(Math.Floor(foe.Get(top) * G("DEVOUR_FRAC"))));
            Assert.That(s.Stats.Get(top), Is.EqualTo(before + r.DevouredGain));
            Assert.That(s.Energy, Is.EqualTo(energy - G("DEVOUR_ENERGY")), "the new cycle's upkeep");

            // A loss feeds on nothing; the energy floor is 0.
            var weak = Holding("devourer", species: "slime");
            foreach (var id in StatIds.Trained) SetStat(weak.Stats, id, 1);
            RestOut(weak);
            weak.Energy = 2;
            var lost = TrainingRun.Fight(Db, weak);
            Assert.That(lost.Won, Is.False);
            Assert.That(lost.DevouredStat, Is.Null);
            Assert.That(weak.Energy, Is.Zero);
        }

        [Test]
        public void Devourer_IsCappedAtTheStatCap()
        {
            var s = Holding("devourer", species: "slime");
            Mighty(s);
            RestOut(s);
            var r = TrainingRun.Fight(Db, s);
            Assert.That(r.Won, Is.True);
            Assert.That(r.DevouredGain, Is.Zero);
            foreach (var id in StatIds.Trained) Assert.That(s.Stats.Get(id), Is.EqualTo(K.StatMax));
        }

        // ------------------------------------------------------------------ second_breath

        [Test]
        public void SecondBreath_BelowTheSafeLine_NoInjuryAndNoSuper()
        {
            var s = Holding("second_breath");
            var off = Holding(null);
            double safe = G("BREATH_SAFE") / K.RunEnergyMax, below = safe - 0.01;
            Assert.That(TrainingRun.RunSuperChance(Db, s, below), Is.Zero);
            Assert.That(TrainingRun.RunInjuryChance(Db, s, below), Is.Zero);
            Assert.That(TrainingRun.RunInjuryChance(Db, s, 0), Is.Zero);
            Assert.That(TrainingRun.RunSuperChance(Db, s, safe), Is.EqualTo(TrainingRun.RunSuperChance(Db, off, safe)));
            Assert.That(TrainingRun.RunInjuryChance(Db, off, 0), Is.GreaterThan(0));

            int guard = 0;
            while (s.Phase != RunPhase.Finished && guard++ < 200)
            {
                if (s.Offers.Count > 0) { TrainingRun.DiscardOffer(Db, s); continue; }
                if (s.Phase == RunPhase.AwaitFight) { TrainingRun.Fight(Db, s); continue; }
                s.Energy = 0;
                var r = TrainingRun.Drill(Db, s, StatIds.Hp);
                Assert.That(r.Injured, Is.False);
                Assert.That(r.Tier, Is.Not.EqualTo(DrillTier.Super));
            }
            Assert.That(s.Injuries, Is.Empty);
        }

        [Test]
        public void SecondBreath_ASuperDrillRefundsItsCost()
        {
            var s = Holding("second_breath");
            int supers = 0, others = 0, guard = 0;
            while ((supers == 0 || others == 0) && guard++ < 500)
            {
                if (s.Phase == RunPhase.AwaitFight) { TrainingRun.Fight(Db, s); continue; }
                if (s.Phase == RunPhase.Finished) s = Holding("second_breath", seed: (ulong)(100 + guard));
                if (s.Offers.Count > 0) { TrainingRun.DiscardOffer(Db, s); continue; }
                s.Energy = K.RunEnergyMax;
                var r = TrainingRun.Drill(Db, s, StatIds.Hp);
                if (r.Tier == DrillTier.Super)
                {
                    supers++;
                    Assert.That(r.Refunded, Is.EqualTo(K.DrillEnergyCost));
                    Assert.That(r.EnergyAfter, Is.EqualTo(K.RunEnergyMax));
                }
                else
                {
                    others++;
                    Assert.That(r.Refunded, Is.Zero);
                    Assert.That(r.EnergyAfter, Is.EqualTo(K.RunEnergyMax - K.DrillEnergyCost));
                }
            }
            Assert.That(supers, Is.GreaterThan(0));
        }

        // ------------------------------------------------------------------ ascetic

        [Test]
        public void Ascetic_OneMoreApEveryCycle_AndWinsPayNoSp()
        {
            var s = Holding("ascetic");
            Mighty(s);
            RestOut(s);
            int sp = s.Sp;
            var r = TrainingRun.Fight(Db, s);
            Assert.That(r.Won, Is.True);
            Assert.That(r.SpGained, Is.Zero);
            Assert.That(s.Sp, Is.EqualTo(sp + K.SpPerCycle), "only the new cycle's SP");
            Assert.That(s.ApLeft, Is.EqualTo(K.RunAp + (int)G("ASCETIC_AP")));

            var off = Holding("ascetic", 7);
            Mighty(off);
            RestOut(off);
            Assert.That(TrainingRun.Fight(Db, off).SpGained, Is.EqualTo(K.SpPerWin));
            Assert.That(off.ApLeft, Is.EqualTo(K.RunAp));
        }

        // ------------------------------------------------------------------ hungry_core

        [Test]
        public void HungryCore_FedAddsItsBonusInTheFight_HungryGoesDormant()
        {
            var s = Holding("hungry_core");
            RestOut(s);
            var plain = TrainingRun.MonsterFighter(Db, s).Stats;
            MonsterStats seen = null;
            TrainingRun.Fight(Db, s, null, st => seen = st.Units[0].Source.Stats.Clone());
            foreach (var id in StatIds.Trained) Assert.That(seen.Get(id), Is.EqualTo(plain.Get(id) + G("HUNGRY_STAT")), "fed from the start: " + id);
            Assert.That(s.Energy, Is.EqualTo(K.RunEnergyMax - G("HUNGRY_ENERGY")), "the new cycle's meal");
            Assert.That(s.CoreDormant, Is.False);
            Assert.That(TrainingRun.CoreFed(Db, s), Is.True);

            // Short of energy at the next cycle start: dormant, nothing eaten, no bonus in that fight.
            RestOut(s);
            double e = G("HUNGRY_ENERGY") - 1;
            s.Energy = e;
            TrainingRun.Fight(Db, s);
            Assert.That(s.CoreDormant, Is.True);
            Assert.That(s.Energy, Is.EqualTo(e));
            Assert.That(TrainingRun.CoreFed(Db, s), Is.False);
            RestOut(s);
            plain = TrainingRun.MonsterFighter(Db, s).Stats;
            TrainingRun.Fight(Db, s, null, st => seen = st.Units[0].Source.Stats.Clone());
            foreach (var id in StatIds.Trained) Assert.That(seen.Get(id), Is.EqualTo(plain.Get(id)), "dormant: " + id);
            Assert.That(s.CoreDormant, Is.False, "fed again: Rest refilled the energy");
        }

        [Test]
        public void HungryCore_DormantState_IsHashedOnlyWhenSet()
        {
            var s = Holding("hungry_core");
            string h = TrainingRun.StateHash(s);
            s.CoreDormant = true;
            Assert.That(TrainingRun.StateHash(s), Is.Not.EqualTo(h));
            s.CoreDormant = false;
            Assert.That(TrainingRun.StateHash(s), Is.EqualTo(h));
        }

        [Test]
        public void DevourerThenHungryCore_TheCoreEatsWhatTheDevourerLeaves()
        {
            var s = Holding("devourer");
            s.Offers.Insert(0, new TreeOffer { Source = OfferSource.Book, Trees = { "hungry_core" } });
            TrainingRun.TakeTree(Db, s, 0, s.Slots.Length - 2);
            for (int i = 0; i < 8; i++) { s.Sp = Math.Max(s.Sp, 10); TrainingRun.SpendPoint(Db, s, s.Slots.Length - 2); }
            RestOut(s);
            s.Energy = G("DEVOUR_ENERGY") + G("HUNGRY_ENERGY") - 1;   // after the devourer's cost, one short of the meal
            TrainingRun.Fight(Db, s);
            Assert.That(s.CoreDormant, Is.True);
            Assert.That(s.Energy, Is.EqualTo(G("HUNGRY_ENERGY") - 1));
        }

        // ------------------------------------------------------------------ the run without lane G

        [Test]
        public void ARunWithoutTheRules_IsUntouched()
        {
            var s = Holding("hungry_core", 7);
            Assert.That(TrainingRun.CanRest(Db, s), Is.True);
            RestOut(s);
            double e = s.Energy;
            int ap0 = K.RunAp;
            TrainingRun.Fight(Db, s);
            Assert.That(s.Energy, Is.EqualTo(e));
            Assert.That(s.ApLeft, Is.EqualTo(ap0));
            Assert.That(s.CoreDormant, Is.False);
        }
    }
}
