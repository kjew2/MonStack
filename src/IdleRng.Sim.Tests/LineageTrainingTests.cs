using System;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    public class LineageTrainingTests
    {
        static ContentDb Db => TestContent.Db;
        static RunState Holding(string tree, int rank = 10)
        {
            var s = TrainingRun.Start(Db, "drake", 55);
            s.Slots[s.Slots.Length - 1] = new TreeSlot { TreeId = tree, Bought = Db.Trees[tree].Nodes.Count,
                Roll = new TreeRoll { LineageRank = rank, Stat = Db.Trees[tree].Nodes.First(n => n.Stat != null).Stat,
                    NodeStats = new string[Db.Trees[tree].Nodes.Count], Amounts = new double[Db.Trees[tree].Nodes.Count] } };
            s.Lineage = new LineageRun { TreeId = tree, Rank = rank };
            return s;
        }

        [Test]
        public void ChoicesRequireInheritedMilestoneAndBoughtCapstone()
        {
            var s = Holding("cursed_mark", 4);
            Assert.That(TrainingRun.AvailableLineageChoices(Db, s), Is.Empty);
            Assert.Throws<InvalidOperationException>(() => TrainingRun.LineageChoice(Db, s, "specialize", "str"));
            s.Lineage.Rank = 10;
            s.Slots[s.Slots.Length - 1].Bought--;
            Assert.That(TrainingRun.AvailableLineageChoices(Db, s), Is.Empty);
        }

        [Test]
        public void OrdinaryReacquisitionCannotRestoreInheritedMilestones()
        {
            var s = Holding("heirloom");
            TrainingRun.LineageChoice(Db, s, "nominate", "hp");
            s.LineageTraining.Charge = (int)Db.ConstantValues["LINEAGE_TRAIN_CHARGE"];
            int slot = s.Slots.Length - 1;
            var inherited = s.Slots[slot];
            // Overwriting and reacquiring a plain tree keeps the birth record, but loses the inherited roll.
            s.Slots[slot] = new TreeSlot { TreeId = "heirloom", Bought = Db.Trees["heirloom"].Nodes.Count };
            var ordinary = s.Clone(); ordinary.Lineage = null;
            Assert.That(TrainingRun.Growth(Db, s, "hp"), Is.EqualTo(TrainingRun.Growth(Db, ordinary, "hp")));
            Assert.That(TrainingRun.AvailableLineageChoices(Db, s), Is.Empty);
            Assert.Throws<InvalidOperationException>(() => TrainingRun.LineageChoice(Db, s, "redirect", "str"));
            // A fusion retaining the actual inherited tree preserves its milestone powers.
            var holder = new TreeSlot { TreeId = "mastery", Absorbed = { inherited } };
            s.Slots[slot] = holder;
            var absorbedOrdinary = s.Clone(); absorbedOrdinary.Lineage = null;
            Assert.That(TrainingRun.Growth(Db, s, "hp") - TrainingRun.Growth(Db, absorbedOrdinary, "hp"),
                Is.EqualTo(Db.ConstantValues["LINEAGE_TRAIN_CAP"]).Within(1e-9));
            Assert.That(TrainingRun.AvailableLineageChoices(Db, s).Any(x => x.Choice == "redirect"), Is.True);
        }

        [Test]
        public void ChoicePreviewsDoNotMutateRunOrRandomStreams()
        {
            var s = Holding("devourer");
            string before = TrainingRun.StateHash(s);
            Assert.That(TrainingRun.AvailableLineageChoices(Db, s).Count(), Is.EqualTo(StatIds.Trained.Length * 2));
            Assert.That(TrainingRun.StateHash(s), Is.EqualTo(before));
            TrainingRun.LineageChoice(Db, s, "adapt", "str");
            Assert.That(s.LineageTraining.Stat, Is.EqualTo("str"));
            Assert.That(s.Log.Last().LineageAction, Is.EqualTo("adapt"));
            var clone = s.Clone();
            clone.LineageTraining.Stat = "int";
            Assert.That(s.LineageTraining.Stat, Is.EqualTo("str"));
        }

        [Test]
        public void CursedSpecializationCannotRestoreDefenseTrainingOrChangeCommitment()
        {
            var s = Holding("cursed_mark");
            Assert.Throws<InvalidOperationException>(() => TrainingRun.LineageChoice(Db, s, "specialize", "def"));
            TrainingRun.LineageChoice(Db, s, "specialize", "str");
            Assert.That(TrainingRun.Growth(Db, s, "def"), Is.Zero);
            Assert.Throws<InvalidOperationException>(() => TrainingRun.LineageChoice(Db, s, "specialize", "int"));
        }

        [Test]
        public void VowRecoverySpendsFullRhythmWithoutUnlockingRest()
        {
            var s = Holding("iron_vow");
            int need = (int)Db.ConstantValues["LINEAGE_TRAIN_CHARGE"];
            s.LineageTraining = new LineageTrainingState { Rhythm = need - 1 };
            Assert.Throws<InvalidOperationException>(() => TrainingRun.LineageChoice(Db, s, "recover"));
            s.LineageTraining.Rhythm = need;
            s.Energy = 0;
            int ap = s.ApLeft;
            TrainingRun.LineageChoice(Db, s, "recover");
            Assert.That(s.Energy, Is.EqualTo(Db.Constants.RunEnergyMax * Db.ConstantValues["LINEAGE_TRAIN_CAP"]));
            Assert.That(s.LineageTraining.Rhythm, Is.Zero);
            Assert.That(s.ApLeft, Is.EqualTo(ap));
            Assert.That(TrainingRun.CanRest(Db, s), Is.False);
        }

        [Test]
        public void StabilizationUsesRefundReserveAndDoesNotAlterSuperRestriction()
        {
            var s = Holding("second_breath");
            s.LineageTraining = new LineageTrainingState { Charge = (int)Db.ConstantValues["LINEAGE_TRAIN_CHARGE"] };
            TrainingRun.LineageChoice(Db, s, "stabilize");
            Assert.That(s.LineageTraining.Charge, Is.Zero);
            Assert.That(TrainingRun.RunInjuryChance(Db, s, 0), Is.Zero);
            Assert.That(TrainingRun.RunSuperChance(Db, s, 0), Is.Zero);
            s.Energy = 0;
            TrainingRun.Drill(Db, s, "str");
            Assert.That(s.LineageTraining.Armed, Is.False);
        }

        [Test]
        public void AsceticBanksWithinCycleAndOnlyExchangesOnce()
        {
            var s = Holding("ascetic");
            int ap = s.ApLeft, sp = s.Sp;
            TrainingRun.LineageChoice(Db, s, "bank");
            Assert.That(s.ApLeft, Is.EqualTo(ap - 1));
            Assert.Throws<InvalidOperationException>(() => TrainingRun.LineageChoice(Db, s, "bank"));
            s.Phase = RunPhase.AwaitFight;
            TrainingRun.LineageChoice(Db, s, "release");
            Assert.That(s.Phase, Is.EqualTo(RunPhase.Training));
            Assert.That(s.ApLeft, Is.EqualTo(ap));
            TrainingRun.LineageChoice(Db, s, "exchange");
            Assert.That((s.ApLeft, s.Sp), Is.EqualTo((ap - 1, sp + Db.Constants.SpPerWin)));
            Assert.Throws<InvalidOperationException>(() => TrainingRun.LineageChoice(Db, s, "exchange"));
        }

        [Test]
        public void HungryMealChoiceIsPendingAndCannotBeRetunedInSameCycle()
        {
            var s = Holding("hungry_core");
            double energy = s.Energy;
            TrainingRun.LineageChoice(Db, s, "heavy");
            Assert.That(s.Energy, Is.EqualTo(energy), "upkeep is charged at the next cycle boundary");
            Assert.That(s.LineageTraining.SourceStat, Is.Null, "current paid meal is unchanged");
            Assert.Throws<InvalidOperationException>(() => TrainingRun.LineageChoice(Db, s, "modest"));
        }

        [Test]
        public void HeirloomRedirectConservesBuiltGrowthAndIsOncePerRun()
        {
            var s = Holding("heirloom");
            TrainingRun.LineageChoice(Db, s, "nominate", "hp");
            s.LineageTraining.Charge = (int)Db.ConstantValues["LINEAGE_TRAIN_CHARGE"];
            double hp = TrainingRun.Growth(Db, s, "hp"), str = TrainingRun.Growth(Db, s, "str");
            TrainingRun.LineageChoice(Db, s, "redirect", "str");
            Assert.That(TrainingRun.Growth(Db, s, "hp") + TrainingRun.Growth(Db, s, "str"), Is.EqualTo(hp + str).Within(1e-9));
            Assert.Throws<InvalidOperationException>(() => TrainingRun.LineageChoice(Db, s, "redirect", "int"));
        }

        [Test]
        public void ExcessTrainingIsBoundedAndManifestationIsCommitted()
        {
            var s = Holding("limit_break");
            s.Stats.Str = Db.Constants.StatMax;
            for (int i = 0; i < 4; i++) { s.ApLeft = 10; s.Energy = Db.Constants.RunEnergyMax; TrainingRun.Drill(Db, s, "str"); }
            Assert.That(s.LineageTraining.Excess, Is.InRange(0, Db.Constants.StatMax * Db.ConstantValues["LINEAGE_TRAIN_CAP"]));
            TrainingRun.LineageChoice(Db, s, "offense");
            Assert.Throws<InvalidOperationException>(() => TrainingRun.LineageChoice(Db, s, "protection"));
        }

        [Test]
        public void MasteryNeverAcceptsChaseOrItsOwnCapstone()
        {
            var s = Holding("mastery");
            string own = Db.Trees["mastery"].Nodes.Last().Id;
            Assert.Throws<InvalidOperationException>(() => TrainingRun.LineageChoice(Db, s, "pursue", own));
            Assert.That(TrainingRun.AvailableLineageChoices(Db, s).Any(x => x.Stat == own), Is.False);
        }
    }
}
