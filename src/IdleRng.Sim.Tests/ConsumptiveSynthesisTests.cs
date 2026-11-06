using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Account;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    public class ConsumptiveSynthesisTests
    {
        static ContentDb Db => TestContent.Db;
        static AccountState Parents()
        {
            var a = AccountOps.New(Db, 314);
            a.Shards = 10000; a.RunsStarted = 2; a.Lineages.Clear();
            a.Vault.Add(new VaultEntry { Id = 0, SpeciesId = "slime", Trees = { new VaultTree { TreeId = "fortune", Bought = 2 } } });
            a.Vault.Add(new VaultEntry { Id = 1, SpeciesId = "slime", Trees = { new VaultTree { TreeId = "brute_force", Bought = 5 } } });
            a.Rulebooks[0] = "primary"; a.Rulebooks[1] = "partner";
            return a;
        }
        static void Play(RunState s)
        {
            while (s.Phase != RunPhase.Finished)
            {
                while (s.Offers.Count > 0) TrainingRun.DiscardOffer(Db, s);
                if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s); else TrainingRun.Rest(Db, s);
            }
        }
        static void Hold(VaultEntry monster, string tree, FortunaLineage progress)
        {
            monster.Lineages ??= new SortedDictionary<string, FortunaLineage>();
            monster.Lineages[tree] = progress;
        }

        [Test]
        public void BothParentsContributeSelectedProgress_WithoutCombiningRanksOrKeepingAliases()
        {
            var a = Parents();
            var primary = a.Vault[0]; var support = a.Vault[1];
            var fortune = new FortunaLineage { Rank = 4, Points = 12, Failures = 1, Attempts = 3, Seed = 111 };
            var brute = new FortunaLineage { Rank = 3, Points = 10, Seed = 222 };
            Hold(primary, "fortune", fortune); Hold(primary, "spellcraft", new FortunaLineage { Rank = 8 });
            Hold(support, "brute_force", brute); Hold(support, "fortune", new FortunaLineage { Rank = 9 });
            a.Vault.Add(new VaultEntry { Id = 2, SpeciesId = "slime" }); a.RunsStarted = 3;
            a.Rulebooks[2] = "unrelated";
            var s = TreeLineages.Breed(Db, a, 0, 1, "fortune", supportingTree: "brute_force");

            Assert.That(a.Vault.Select(e => e.Id), Is.EqualTo(new[] { 2 }));
            Assert.That(a.Rulebooks.Keys, Is.EqualTo(new[] { 2 }));
            Assert.That(a.Shards, Is.EqualTo(10000 - TreeLineages.Cost(Db)));
            Assert.That((s.Lineage.Primary, s.Lineage.Supporting, s.Lineage.SupportingTreeId), Is.EqualTo((0, 1, "brute_force")));
            Assert.That(s.Lineage.Progress.Keys, Is.EquivalentTo(new[] { "fortune", "brute_force" }));
            Assert.That((s.Slots[2].TreeId, s.Slots[2].Roll.LineageRank, s.Slots[2].Bought), Is.EqualTo(("fortune", 4, 0)));
            Assert.That((s.Slots[3].TreeId, s.Slots[3].Roll.LineageRank, s.Slots[3].Bought), Is.EqualTo(("brute_force", 3, 0)));
            var focus = s.Lineage.Progress["fortune"];
            Assert.That((focus.Rank, focus.Points, focus.Failures, focus.Attempts, focus.Seed), Is.EqualTo((4, 12, 1, 3, 111UL)));
            Assert.That((s.Lineage.Progress["brute_force"].Points, s.Lineage.Progress["brute_force"].Seed), Is.EqualTo((10, 222UL)));
            Assert.That(focus, Is.Not.SameAs(fortune));
            fortune.Points = 999; brute.Rank = 10;
            Assert.That((focus.Points, s.Lineage.Progress["brute_force"].Rank), Is.EqualTo((12, 3)));
            var clone = s.Clone(); clone.Lineage.Progress["fortune"].Points = 888;
            Assert.That(focus.Points, Is.EqualTo(12), "simulation clones must not share personal progress");
        }

        [TestCase("same-parent", typeof(ArgumentException))]
        [TestCase("bad-index", typeof(ArgumentException))]
        [TestCase("locked-primary", typeof(InvalidOperationException))]
        [TestCase("locked-support", typeof(InvalidOperationException))]
        [TestCase("duplicate-id", typeof(InvalidOperationException))]
        [TestCase("uncertified", typeof(InvalidOperationException))]
        [TestCase("missing-focus", typeof(InvalidOperationException))]
        [TestCase("missing-support", typeof(InvalidOperationException))]
        [TestCase("same-tree", typeof(InvalidOperationException))]
        [TestCase("shards", typeof(InvalidOperationException))]
        [TestCase("loadout", typeof(ArgumentException))]
        [TestCase("active", typeof(InvalidOperationException))]
        [TestCase("species", typeof(InvalidOperationException))]
        [TestCase("over-cap", typeof(InvalidOperationException))]
        public void RefusedSynthesisLeavesParentsWalletReservesAndOtherAccountStateUntouched(string scenario, Type error)
        {
            var a = Parents();
            a.Lineages["fortune"] = new FortunaLineage { Rank = 4, Points = 11, Seed = 333 };
            int primary = 0, support = 1; string secondary = "brute_force"; IEnumerable<string> off = null;
            switch (scenario)
            {
                case "same-parent": support = 0; break;
                case "bad-index": primary = 4; break;
                case "locked-primary": a.Vault[0].Locked = true; break;
                case "locked-support": a.Vault[1].Locked = true; break;
                case "duplicate-id": a.Vault[1].Id = a.Vault[0].Id; break;
                case "uncertified": a.Vault[1].Id = -1; break;
                case "missing-focus": a.Vault[0].Trees.Clear(); break;
                case "missing-support": a.Vault[1].Trees.Clear(); break;
                case "same-tree": secondary = "fortune"; break;
                case "shards": a.Shards = TreeLineages.Cost(Db) - 1; break;
                case "loadout": off = new[] { "not_a_talent" }; break;
                case "active": AccountOps.StartRun(Db, a, "slime"); break;
                case "species": a.Vault[0].SpeciesId = "not_owned"; break;
                case "over-cap":
                    for (int i = 2; i < Db.Constants.VaultCap + 3; i++) a.Vault.Add(new VaultEntry { Id = i, SpeciesId = "slime" });
                    a.RunsStarted = a.Vault.Count; break;
            }
            var parents = a.Vault.ToArray(); var reserve = a.Lineages["fortune"];
            var book = a.Rulebooks.ToArray(); var loadout = a.TalentsOff.ToArray();
            long shards = a.Shards; int runs = a.RunsStarted; var active = a.Active; var vendor = a.Vendor;

            Assert.Throws(error, () => TreeLineages.Breed(Db, a, primary, support, "fortune", off, secondary));
            Assert.That(a.Vault.ToArray(), Is.EqualTo(parents), "failed startup must restore exact Vault positions and objects");
            Assert.That((a.Shards, a.RunsStarted), Is.EqualTo((shards, runs)));
            Assert.That(a.Active, Is.SameAs(active)); Assert.That(a.Vendor, Is.SameAs(vendor));
            Assert.That(a.Rulebooks.ToArray(), Is.EqualTo(book)); Assert.That(a.TalentsOff, Is.EqualTo(loadout));
            Assert.That(a.Lineages["fortune"], Is.SameAs(reserve));
            Assert.That((reserve.Rank, reserve.Points, reserve.Seed), Is.EqualTo((4, 11, 333UL)));
        }

        [Test]
        public void ConsumptionCanMakeAnOverfullVaultFit_WithoutReorderingSurvivors()
        {
            var a = Parents();
            for (int i = 2; i < Db.Constants.VaultCap + 2; i++) a.Vault.Add(new VaultEntry { Id = i, SpeciesId = "slime" });
            a.RunsStarted = a.Vault.Count;
            var survivors = a.Vault.Skip(2).ToArray();
            var s = TreeLineages.Breed(Db, a, 0, 1, "fortune");
            Assert.That(a.Vault, Is.EqualTo(survivors));
            Assert.That(a.Vault.Count, Is.EqualTo(Db.Constants.VaultCap));
            Assert.That(a.Active, Is.SameAs(s));
        }

        [Test]
        public void ReservePreviewDoesNotClaimOrAlias_AndSuccessfulSynthesisClaimsOnce()
        {
            var a = Parents();
            var reserve = new FortunaLineage { Rank = 4, Points = 44, Failures = 4, Attempts = 7, Seed = 333 };
            a.Lineages["fortune"] = reserve;
            a.Lineages["bulwark"] = new FortunaLineage { Rank = 8, Points = 100 };
            var preview = TreeLineages.Inheritance(Db, a, a.Vault[0], "fortune");
            Assert.That((preview.Rank, preview.Points, preview.Failures, preview.Attempts, preview.Seed), Is.EqualTo((4, 44, 4, 7, 333UL)));
            preview.Points = 999;
            Assert.That(reserve.Points, Is.EqualTo(44));
            Assert.That(a.Lineages["fortune"], Is.SameAs(reserve));
            var s = TreeLineages.Breed(Db, a, 0, 1, "fortune");
            Assert.That(a.Lineages.ContainsKey("fortune"), Is.False);
            Assert.That(a.Lineages["bulwark"].Rank, Is.EqualTo(8), "unselected reserves remain available");
            Assert.That(s.Lineage.Progress["fortune"].Points, Is.EqualTo(44));
            AccountOps.AbandonRun(Db, a);
            Assert.That(a.Vault, Is.Empty, "abandoning a descendant never recreates sacrificed parents");
            a.Vault.Add(new VaultEntry { Id = 3, SpeciesId = "slime", Trees = { new VaultTree { TreeId = "fortune", Bought = 2 } } });
            a.Vault.Add(new VaultEntry { Id = 4, SpeciesId = "slime", Trees = { new VaultTree { TreeId = "brute_force", Bought = 5 } } });
            a.RunsStarted = 5;
            var next = TreeLineages.Breed(Db, a, 0, 1, "fortune");
            Assert.That((next.Lineage.Rank, next.Lineage.Progress["fortune"].Points), Is.EqualTo((1, 0)), "a new family cannot reuse the claimed reserve");
            Assert.That(a.Shards, Is.EqualTo(10000 - 2 * TreeLineages.Cost(Db)));
        }

        [Test]
        public void OnlyFocusEarnsPoints_SealKeepsBirthCombatFrozen_AndPassesNewRankForward()
        {
            var a = Parents();
            Hold(a.Vault[0], "fortune", new FortunaLineage { Rank = 4, Points = 44, Failures = 4, Attempts = 4, Seed = 11 });
            Hold(a.Vault[1], "brute_force", new FortunaLineage { Rank = 2, Points = 73, Seed = 99 });
            var s = TreeLineages.Breed(Db, a, 0, 1, "fortune"); s.Sp = 200;
            for (int slot = 2; slot < 4; slot++) for (int i = 0; i < 8; i++) TrainingRun.SpendPoint(Db, s, slot);
            Assert.That((s.Lineage.Progress["fortune"].Points, s.Lineage.Progress["brute_force"].Points), Is.EqualTo((44, 73)), "buying either tree does not prematurely award points");
            Play(s);
            var entry = AccountOps.FinishRun(Db, a).Entry;
            var focus = entry.Lineages["fortune"]; var secondary = entry.Lineages["brute_force"];
            Assert.That((focus.Rank, focus.Points, focus.Failures, focus.Attempts), Is.EqualTo((5, 19, 0, 5)));
            Assert.That((secondary.Rank, secondary.Points, secondary.Seed), Is.EqualTo((2, 73, 99UL)), "a fully purchased supporting tree carries progress but earns no extra award");
            Assert.That((entry.Lineage.Rank, entry.Trees.Single(t => t.TreeId == "fortune").Roll.LineageRank), Is.EqualTo((4, 4)));
            Assert.That(entry.Fighter(Db).Rules, Does.Not.Contain(RuleModifiers.FortuneBank), "a sealed fighter does not acquire a new combat kit retroactively");
            Assert.That(a.Lineages, Is.Empty, "seal does not write a global unlock");
            s.Lineage.Progress["fortune"].Points = 999;
            Assert.That(focus.Points, Is.EqualTo(19));
            a.Vault.Add(new VaultEntry { Id = 3, SpeciesId = "slime", Trees = { new VaultTree { TreeId = "brute_force", Bought = 5 } } }); a.RunsStarted = 4;
            var child = TreeLineages.Breed(Db, a, 0, 1, "fortune");
            Assert.That((child.Lineage.Rank, child.Slots[2].Roll.LineageRank, child.Lineage.Progress["fortune"].Points), Is.EqualTo((5, 5, 19)));
            Assert.That(child.Lineage.Progress["brute_force"].Rank, Is.EqualTo(1), "each selected tree comes from its chosen parent, not an unselected tree on the other parent");
        }

        [Test]
        public void GeneticCarrierSurvivesOverwrite_ButAnOrdinaryUnlearnedTreeDoesNotQualify()
        {
            var carrier = new VaultEntry { Id = 8, SpeciesId = "slime" };
            Assert.That(TreeLineages.EligiblePrimary(Db, carrier, "fortune"), Is.False);
            Hold(carrier, "fortune", new FortunaLineage { Rank = 3, Points = 8 });
            Assert.That(TreeLineages.EligiblePrimary(Db, carrier, "fortune"), Is.True);
            carrier.Locked = true;
            Assert.That(TreeLineages.EligiblePrimary(Db, carrier, "fortune"), Is.False, "genetic carriers still respect consumption locks");
        }
        [Test]
        public void SupportingTrainingMilestonesHaveIndependentChoicesChargesAndCloneState()
        {
            var a = Parents();
            Hold(a.Vault[0], "iron_vow", new FortunaLineage { Rank = 10, Seed = 11 });
            Hold(a.Vault[1], "heirloom", new FortunaLineage { Rank = 10, Seed = 99 });
            var s = TreeLineages.Breed(Db, a, 0, 1, "iron_vow", supportingTree: "heirloom"); s.Sp = 200;
            for (int slot = 2; slot < 4; slot++) for (int i = 0; i < 8; i++) TrainingRun.SpendPoint(Db, s, slot);
            s.LineageTraining = new LineageTrainingState { Stat = "str", Rhythm = 2 };
            string beforePreview = TrainingRun.StateHash(s);
            Assert.That(TrainingRun.AvailableLineageChoices(Db, s).Any(x => x.Choice == "heirloom:nominate" && x.Stat == "hp"), Is.True);
            Assert.That(TrainingRun.StateHash(s), Is.EqualTo(beforePreview), "secondary choice previews are read-only");
            TrainingRun.LineageChoice(Db, s, "heirloom:nominate", "hp");
            Assert.That((s.Lineage.TreeId, s.Lineage.Rank, s.LineageTraining.Stat, s.LineageTraining.Rhythm), Is.EqualTo(("iron_vow", 10, "str", 2)));
            Assert.That(s.SecondaryLineageTraining.Stat, Is.EqualTo("hp"));
            string beforeRejected = TrainingRun.StateHash(s);
            Assert.Throws<InvalidOperationException>(() => TrainingRun.LineageChoice(Db, s, "heirloom:nominate", "hp"));
            Assert.That(TrainingRun.StateHash(s), Is.EqualTo(beforeRejected), "rejected secondary decisions must restore context without mutation");
            TrainingRun.Drill(Db, s, "hp");
            Assert.That((s.LineageTraining.Rhythm, s.LineageTraining.Stat, s.SecondaryLineageTraining.Charge), Is.EqualTo((1, "hp", 1)));
            var noFamilyState = s.Clone(); noFamilyState.SecondaryLineageTraining = null;
            Assert.That(TrainingRun.Growth(Db, s, "hp") - TrainingRun.Growth(Db, noFamilyState, "hp"), Is.EqualTo(Db.ConstantValues["LINEAGE_TRAIN_GAIN"]).Within(1e-9));
            s.LineageTraining.Rhythm = (int)Db.ConstantValues["LINEAGE_TRAIN_CHARGE"];
            s.Energy = 0;
            TrainingRun.LineageChoice(Db, s, "recover");
            Assert.That((s.LineageTraining.Rhythm, s.SecondaryLineageTraining.Charge), Is.EqualTo((0, 1)), "focus recovery cannot spend the supporting tree's charge");
            Assert.That(TrainingRun.CanRest(Db, s), Is.False);
            TrainingRun.LineageChoice(Db, s, "heirloom:redirect", "str");
            Assert.That((s.Lineage.TreeId, s.LineageTraining.Stat, s.SecondaryLineageTraining.Stat), Is.EqualTo(("iron_vow", "hp", "str")));
            Assert.That(s.Log.Last().LineageAction, Is.EqualTo("heirloom:redirect"));
            var clone = s.Clone();
            clone.SecondaryLineageTraining.Charge++;
            Assert.That(s.SecondaryLineageTraining.Charge, Is.EqualTo(1));
            Assert.That(TrainingRun.StateHash(clone), Is.Not.EqualTo(TrainingRun.StateHash(s)), "secondary training resources participate in deterministic replay hashing");
            Assert.That(s.Lineage.Progress["heirloom"].Points, Is.Zero, "training powers do not grant secondary lineage points");
        }

        [Test]
        public void SupportingTrainingPowersRequireTheActualInheritedCapstone()
        {
            var a = Parents();
            Hold(a.Vault[0], "fortune", new FortunaLineage { Rank = 2 });
            Hold(a.Vault[1], "heirloom", new FortunaLineage { Rank = 10 });
            var s = TreeLineages.Breed(Db, a, 0, 1, "fortune", supportingTree: "heirloom"); s.Sp = 100;
            for (int i = 0; i < 7; i++) TrainingRun.SpendPoint(Db, s, 3);
            Assert.That(TrainingRun.AvailableLineageChoices(Db, s), Is.Empty);
            TrainingRun.SpendPoint(Db, s, 3);
            TrainingRun.LineageChoice(Db, s, "heirloom:nominate", "hp");
            s.SecondaryLineageTraining.Charge = 3;
            s.Slots[3] = new TreeSlot { TreeId = "heirloom", Bought = Db.Trees["heirloom"].Nodes.Count };
            Assert.That(TrainingRun.AvailableLineageChoices(Db, s), Is.Empty);
            Assert.Throws<InvalidOperationException>(() => TrainingRun.LineageChoice(Db, s, "heirloom:redirect", "str"));
            var noFamilyState = s.Clone(); noFamilyState.SecondaryLineageTraining = null;
            Assert.That(TrainingRun.Growth(Db, s, "hp"), Is.EqualTo(TrainingRun.Growth(Db, noFamilyState, "hp")), "reacquiring an ordinary copy does not restore supporting inheritance powers");
        }
        [Test]
        public void SupportingInheritedTrainingPowerSurvivesAbsorptionWithoutChangingFocusContext()
        {
            var a = Parents();
            Hold(a.Vault[0], "fortune", new FortunaLineage { Rank = 2 });
            Hold(a.Vault[1], "heirloom", new FortunaLineage { Rank = 10 });
            var s = TreeLineages.Breed(Db, a, 0, 1, "fortune", supportingTree: "heirloom");
            s.Sp = 100;
            for (int i = 0; i < Db.Trees["heirloom"].Nodes.Count; i++) TrainingRun.SpendPoint(Db, s, 3);
            TrainingRun.LineageChoice(Db, s, "heirloom:nominate", "hp");
            s.SecondaryLineageTraining.Charge = (int)Db.ConstantValues["LINEAGE_TRAIN_CHARGE"];
            var inherited = s.Slots[3];
            s.Slots[3] = new TreeSlot { TreeId = "mastery", Absorbed = { inherited } };
            var ordinary = s.Clone();
            ordinary.SecondaryLineageTraining = null;
            Assert.That(TrainingRun.Growth(Db, s, "hp") - TrainingRun.Growth(Db, ordinary, "hp"),
                Is.EqualTo(Db.ConstantValues["LINEAGE_TRAIN_CAP"]).Within(1e-9));
            string before = TrainingRun.StateHash(s);
            Assert.That(TrainingRun.AvailableLineageChoices(Db, s).Any(x => x.Choice == "heirloom:redirect" && x.Stat == "str"), Is.True);
            Assert.Throws<InvalidOperationException>(() => TrainingRun.LineageChoice(Db, s, "fortune:nominate", "hp"));
            Assert.That(TrainingRun.StateHash(s), Is.EqualTo(before), "previews and an invalid tree prefix cannot mutate either family's state");
            TrainingRun.LineageChoice(Db, s, "heirloom:redirect", "str");
            Assert.That((s.Lineage.TreeId, s.Lineage.Rank, s.LineageTraining), Is.EqualTo(("fortune", 2, (LineageTrainingState)null)));
            Assert.That(s.SecondaryLineageTraining.Stat, Is.EqualTo("str"));
            Assert.That(s.Log.Last().LineageAction, Is.EqualTo("heirloom:redirect"));
        }
    }
}
