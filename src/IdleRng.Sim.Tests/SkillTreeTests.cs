using System;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Skill trees in the training run (specs/15): slots, skill points, nodes, offers, overwrites, chase trees, books.</summary>
    public class SkillTreeTests
    {
        static ContentDb Db => TestContent.Db;
        static Constants K => Db.Constants;

        static RunState Drake(ulong seed = 7, params string[] books) => TrainingRun.Start(Db, "drake", seed, books);

        /// <summary>Put an offer at the end of the queue (a test shortcut for a milestone draft).</summary>
        static void Offer(RunState s, params string[] trees) =>
            s.Offers.Add(new TreeOffer { Source = OfferSource.Milestone, Stat = StatIds.Str, Threshold = 300, Trees = trees.ToList() });

        static int SlotOf(RunState s, string tree) => Array.FindIndex(s.Slots, x => x != null && x.TreeId == tree);

        /// <summary>SP spent in the tree in <paramref name="slot"/>: the cost of its bought nodes.</summary>
        static int Spent(RunState s, int slot) => Db.Trees[s.Slots[slot].TreeId].Nodes.Take(s.Slots[slot].Bought).Sum(n => n.Cost);

        static void Max(RunState s, int slot)
        {
            int n = Db.Trees[s.Slots[slot].TreeId].Nodes.Count;
            while (s.Slots[slot] != null && s.Slots[slot].Bought < n) { s.Sp = Math.Max(s.Sp, 10); TrainingRun.SpendPoint(Db, s, slot); }
        }

        [Test]
        public void Content_TreesHaveMostlyFillerAndAChaseTraitLast()
        {
            foreach (var t in Db.TreeList)
            {
                Assert.That(t.Nodes.Last().Kind, Is.AnyOf(NodeKind.Skill, NodeKind.Trigger, NodeKind.Rule), t.Id);
                double filler = t.Nodes.Count(n => TrainingRun.IsFiller(n.Kind)) / (double)t.Nodes.Count;
                Assert.That(filler, Is.InRange(0.6, 0.9), $"{t.Id}: about 75% filler (kj)");
            }
            foreach (var sp in Db.SpeciesList)
            {
                Assert.That(Db.Trees[sp.NativeTree1].Kind, Is.EqualTo(TreeKind.Native));
                Assert.That(Db.Trees[sp.NativeTree2].Kind, Is.EqualTo(TreeKind.Native));
            }
            Assert.That(Db.TreeList.Count(t => t.Kind == TreeKind.Chase), Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void Content_EachSpeciesOwnsAnInRunAndASecondChaseTree()
        {
            var owned = Db.SpeciesList.SelectMany(sp => new[] { sp.ChaseTreeRun, sp.ChaseTreeBook }).ToList();
            Assert.That(owned, Is.Unique, "no chase tree is shared between species (kj, issue #78)");
            Assert.That(owned, Is.EquivalentTo(Db.TreeList.Where(t => t.Kind == TreeKind.Chase).Select(t => t.Id)), "every chase tree has an owner");
            foreach (var sp in Db.SpeciesList)
            {
                var run = Db.Trees[sp.ChaseTreeRun];
                var book = Db.Trees[sp.ChaseTreeBook];
                Assert.That((run.Species, run.BookGated, book.Species), Is.EqualTo((sp.Id, false, sp.Id)), sp.Id);
                Assert.That(Db.BookTreesNeeded(run), Is.Empty, sp.Id + ": the in-run tree needs no book");
                // The second chase tree is book-gated, or a harder in-run one with no book (kj, 2026-11-03; SpeciesGroundworkTests).
                var needs = Db.BookTreesNeeded(book).ToList();
                Assert.That(book.BookGated, Is.EqualTo(needs.Count > 0), sp.Id);
                Assert.That(needs.All(tree => Db.SkillBooks.Values.Any(b => b.TreeId == tree)), Is.True, sp.Id + ": a skill book opens each book tree it needs");
                var natives = new[] { sp.NativeTree1, sp.NativeTree2 };
                Assert.That(run.Conditions.Any(cd => cd.Type == ConditionType.TreeMaxed && natives.Contains(cd.Ref)), Is.True,
                    sp.Id + ": the in-run tree builds on a native tree (kj)");
            }
        }

        [Test]
        public void ChaseTree_OfAnotherSpecies_IsNeverOffered()
        {
            var s = TrainingRun.Start(Db, "slime", 7);       // Unbreakable Will is drake's
            s.Stats.Def = 600;
            s.FightsWon = 2;
            Offer(s, "heavy_armor");
            TrainingRun.TakeTree(Db, s, 0, 2);
            Max(s, 2);
            Assert.That(TrainingRun.ChaseConditionsHold(Db, s, Db.Trees["unbreakable_will"]), Is.True);
            Assert.That(s.Offers, Is.Empty);
        }

        [Test]
        public void BookGatedChaseTree_NeedsItsBookTree()
        {
            // Arcane Overflow (content 4.10, issue #111): Echo Song node 0, Mind Warp node 4, 2 crits.
            var s = TrainingRun.Start(Db, "sprite", 7, new[] { "book_echoes" });
            s.Tally.Crits = 2;
            Assert.That(s.Slots[1].TreeId, Is.EqualTo("mind_warp"));
            Max(s, 1);
            Assert.That(s.Offers, Is.Empty, "Arcane Overflow also needs an Echo Song node");
            TrainingRun.UseBook(Db, s, "book_echoes");
            TrainingRun.TakeTree(Db, s, 0, 2);
            Assert.That(s.Offers, Is.Empty, "the book tree is slotted but none of its nodes is bought");
            s.Sp = Math.Max(s.Sp, 1);
            TrainingRun.SpendPoint(Db, s, 2);
            var offer = s.Offers.Single();
            Assert.That((offer.Source, offer.Trees.Single()), Is.EqualTo((OfferSource.Chase, "arcane_overflow")));
        }

        [Test]
        public void Start_NativeTreesInSlots0And1_TwoEmpty_CycleSp()
        {
            var s = Drake();
            Assert.That(s.Slots.Length, Is.EqualTo(K.TreeSlots));
            Assert.That(s.Slots[0].TreeId, Is.EqualTo(Db.Species["drake"].NativeTree1));
            Assert.That(s.Slots[1].TreeId, Is.EqualTo(Db.Species["drake"].NativeTree2));
            Assert.That(s.Slots.Skip(2).All(x => x == null), Is.True);
            Assert.That(s.Sp, Is.EqualTo(K.SpPerCycle));
            Assert.That(s.Offers, Is.Empty);
        }

        [Test]
        public void SpendPoint_BuysInOrder_GrowthAndFlatStat()
        {
            var s = Drake();
            var nodes = Db.Trees[s.Slots[0].TreeId].Nodes;
            Assert.That(nodes[0].Kind, Is.EqualTo(NodeKind.Growth));
            Assert.That(nodes[1].Kind, Is.EqualTo(NodeKind.Stat));
            s.Sp = 10;
            double g = TrainingRun.Growth(Db, s, nodes[0].Stat);
            var r0 = TrainingRun.SpendPoint(Db, s, 0);
            Assert.That(r0.Node, Is.SameAs(nodes[0]));
            Assert.That(TrainingRun.Growth(Db, s, nodes[0].Stat), Is.EqualTo(g + nodes[0].Amount).Within(1e-12));
            double before = s.Stats.Get(nodes[1].Stat);
            var r1 = TrainingRun.SpendPoint(Db, s, 0);
            Assert.That(r1.StatGain, Is.EqualTo(nodes[1].Amount));
            Assert.That(s.Stats.Get(nodes[1].Stat), Is.EqualTo(before + nodes[1].Amount));
            Assert.That(s.Sp, Is.EqualTo(10 - nodes[0].Cost - nodes[1].Cost));
            Assert.That(s.Slots[0].Bought, Is.EqualTo(2));

            s.Sp = 0;
            Assert.Throws<InvalidOperationException>(() => TrainingRun.SpendPoint(Db, s, 0), "not enough SP");
            s.Sp = 10;
            Assert.Throws<InvalidOperationException>(() => TrainingRun.SpendPoint(Db, s, 2), "empty slot");
            Max(s, 0);
            Assert.Throws<InvalidOperationException>(() => TrainingRun.SpendPoint(Db, s, 0), "maxed");
        }

        [Test]
        public void BoughtSkillNodes_JoinTheMonstersFightKit()
        {
            var s = Drake();
            Assert.That(TrainingRun.MonsterFighter(Db, s).Skills, Is.Empty);
            Max(s, 0);
            var tree = Db.Trees[s.Slots[0].TreeId];
            var f = TrainingRun.MonsterFighter(Db, s);
            var skillNodes = tree.Nodes.Where(n => n.Kind == NodeKind.Skill).Select(n => n.Skill).ToList();
            var triggerNodes = tree.Nodes.Where(n => n.Kind == NodeKind.Trigger).ToList();
            Assert.That(f.Skills, Is.EqualTo(skillNodes));
            Assert.That(f.Triggers.Select(x => x.Skill), Is.EqualTo(triggerNodes.Select(n => n.Skill)));
            Assert.That(f.Triggers.Select(x => x.Trigger), Is.EqualTo(triggerNodes.Select(n => n.Trigger.Value)));
            // and the fight still runs with them
            while (s.Phase == RunPhase.Training) TrainingRun.Rest(Db, s);
            Assert.DoesNotThrow(() => TrainingRun.Fight(Db, s));
        }

        [Test]
        public void Milestone_OpensADraftOfGenericTrees_AndBlocksTraining()
        {
            int t = Db.StatMilestones[0];
            var s = Drake(3);
            s.Stats.Str = t - 1;
            while (s.Offers.Count == 0) { s.Energy = K.RunEnergyMax; TrainingRun.Drill(Db, s, StatIds.Str); }
            var offer = s.Offers.Single();
            Assert.That(offer.Source, Is.EqualTo(OfferSource.Milestone));
            Assert.That(offer.Trees.Count, Is.InRange(1, K.DraftSize));
            Assert.That(offer.Trees.Distinct().Count(), Is.EqualTo(offer.Trees.Count));
            Assert.That(offer.Rolls.Count, Is.EqualTo(offer.Trees.Count), "every drafted tree is rolled (specs/15 section 4.1)");
            foreach (var id in offer.Trees)
            {
                var tr = Db.Trees[id];
                Assert.That(tr.Kind, Is.EqualTo(TreeKind.Generic));
                Assert.That(tr.OfferThreshold, Is.LessThanOrEqualTo(t));   // any stat's tree (issue #121)
            }
            Assert.Throws<InvalidOperationException>(() => TrainingRun.Drill(Db, s, StatIds.Str));
            Assert.Throws<InvalidOperationException>(() => TrainingRun.Rest(Db, s));
            var r = TrainingRun.TakeTree(Db, s, 0, 2);
            Assert.That(s.Slots[2].TreeId, Is.EqualTo(offer.Trees[0]));
            Assert.That(s.Slots[2].Roll, Is.SameAs(offer.Rolls[0]), "the slotted tree keeps its rolls");
            Assert.That(r.Removed, Is.Empty);
            Assert.That(s.Offers, Is.Empty);
            Assert.DoesNotThrow(() => TrainingRun.Rest(Db, s));
        }

        [Test]
        public void Overwrite_RefundsPartOfThePoints_RemovesFlatStats_AndGrowth()
        {
            var s = Drake();
            var nodes = Db.Trees[s.Slots[0].TreeId].Nodes;
            string stat = nodes[1].Stat;
            double baseStat = s.Stats.Get(stat), baseGrowth = TrainingRun.Growth(Db, s, nodes[0].Stat);
            s.Sp = 10;
            TrainingRun.SpendPoint(Db, s, 0);
            TrainingRun.SpendPoint(Db, s, 0);
            Assert.That(s.Stats.Get(stat), Is.GreaterThan(baseStat));

            Offer(s, "vital_core");
            var r = TrainingRun.TakeTree(Db, s, 0, 0);
            Assert.That(r.Removed, Is.EqualTo(new[] { nodes[0].TreeId }));
            Assert.That(s.Slots[0].TreeId, Is.EqualTo("vital_core"));
            Assert.That(s.Slots[0].Bought, Is.EqualTo(0));
            Assert.That(s.Stats.Get(stat), Is.EqualTo(baseStat));
            Assert.That(TrainingRun.Growth(Db, s, nodes[0].Stat), Is.EqualTo(baseGrowth).Within(1e-12));
            int spent = nodes[0].Cost + nodes[1].Cost;
            int refund = (int)Math.Floor(spent * Db.Constants.TreeRefund);
            Assert.That(Db.Constants.TreeRefund, Is.EqualTo(0.5), "kj: half comes back");
            Assert.That(r.Refunded, Is.EqualTo(refund));
            Assert.That(s.Sp, Is.EqualTo(10 - spent + refund), "TREE_REFUND of the spent points comes back, rounded down");
        }

        [Test]
        public void Discard_DropsTheOffer()
        {
            var s = Drake();
            Offer(s, "vital_core", "bulwark");
            TrainingRun.DiscardOffer(Db, s);
            Assert.That(s.Offers, Is.Empty);
            Assert.That(s.Slots.Skip(2).All(x => x == null), Is.True);
            Assert.Throws<InvalidOperationException>(() => TrainingRun.DiscardOffer(Db, s));
            Assert.Throws<InvalidOperationException>(() => TrainingRun.TakeTree(Db, s, 0, 2));

            Offer(s, s.Slots[0].TreeId);
            Assert.Throws<InvalidOperationException>(() => TrainingRun.TakeTree(Db, s, 0, 2), "a tree cannot sit in two slots");
            Assert.Throws<ArgumentOutOfRangeException>(() => TrainingRun.TakeTree(Db, s, 5, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => TrainingRun.TakeTree(Db, s, 0, 9));
        }

        /// <summary>Drake meeting Unbreakable Will's conditions: Heavy Armor maxed, DEF >= 500, 2 wins.</summary>
        static RunState DrakeWithUnbreakableWillOffered()
        {
            var s = Drake();
            s.Stats.Def = 600;
            s.FightsWon = 2;
            Max(s, SlotOf(s, "heavy_armor"));
            return s;
        }

        [Test]
        public void LegacyUnattunedRun_NeverOffersItsInRunChaseTree_ButStillOffersTheBookOne()
        {
            var off = new RunModifiers { ChaseChance = 0 };   // a recipe saved while attunement existed
            var s = TrainingRun.Start(Db, "drake", 7, null, off);
            Assert.That(s.ChaseAttuned, Is.False);
            s.Stats.Def = 600;
            s.FightsWon = 2;
            Max(s, SlotOf(s, "heavy_armor"));
            Assert.That(TrainingRun.ChaseConditionsHold(Db, s, Db.Trees["unbreakable_will"]), Is.True);
            Assert.That(TrainingRun.ChaseOpen(Db, s, Db.Trees["unbreakable_will"]), Is.False);
            Assert.That(s.Offers, Is.Empty, "dormant: the conditions hold but the run is not attuned");

            var book = Db.Species["drake"].ChaseTreeBook;
            Assert.That(TrainingRun.ChaseOpen(Db, s, Db.Trees[book]), Is.True, "the book-gated chase tree does not need attunement");
            Assert.That(TrainingRun.ChaseOpen(Db, s, Db.Trees["arcane_overflow"]), Is.False, "another species' tree is never open");
        }

        [Test]
        public void ChaseTree_OffersWhenConditionsHold_AndLocksAgainWhenAPrerequisiteIsOverwritten()
        {
            var s = DrakeWithUnbreakableWillOffered();
            var offer = s.Offers.Single();
            Assert.That(offer.Source, Is.EqualTo(OfferSource.Chase));
            Assert.That(offer.Trees, Is.EqualTo(new[] { "unbreakable_will" }));
            TrainingRun.TakeTree(Db, s, 0, 2);
            Assert.That(s.Slots[2].TreeId, Is.EqualTo("unbreakable_will"));
            Max(s, 2);
            Assert.That(TrainingRun.MonsterFighter(Db, s).Rules, Does.Contain(RuleModifiers.CounterMitigated), "the chase capstone");

            Offer(s, "vital_core");
            int spentArmor = Spent(s, SlotOf(s, "heavy_armor")), spentWill = Spent(s, 2), sp0 = s.Sp;
            var r = TrainingRun.TakeTree(Db, s, 0, SlotOf(s, "heavy_armor"));
            Assert.That(r.Removed, Is.EquivalentTo(new[] { "heavy_armor", "unbreakable_will" }));
            int expected = (int)Math.Floor(spentArmor * Db.Constants.TreeRefund) + (int)Math.Floor(spentWill * Db.Constants.TreeRefund);
            Assert.That(r.Refunded, Is.EqualTo(expected), "the chase tree that locked again is refunded like the overwritten tree, each rounded down");
            Assert.That(s.Sp, Is.EqualTo(sp0 + expected));
            Assert.That(SlotOf(s, "unbreakable_will"), Is.EqualTo(-1));
            Assert.That(s.Slots[2], Is.Null);
            Assert.That(s.ChaseOffered, Is.Empty, "it can be offered again once its conditions hold again");
            Assert.That(TrainingRun.MonsterFighter(Db, s).Rules, Is.Empty);
        }

        [Test]
        public void ApexForm_IsTheFirstSlottedChaseTreeWithItsCapstoneBought()
        {
            var s = DrakeWithUnbreakableWillOffered();
            Assert.That(ApexForm.TreeOf(Db, s), Is.Null, "no chase tree slotted: the base form");
            Assert.That(ApexForm.Name(Db, "drake", null), Is.EqualTo(Db.Text(Db.Species["drake"].NameKey)));
            TrainingRun.TakeTree(Db, s, 0, 2);
            while (s.Slots[2].Bought < Db.Trees["unbreakable_will"].Nodes.Count - 1) { s.Sp = Math.Max(s.Sp, 10); TrainingRun.SpendPoint(Db, s, 2); }
            Assert.That(ApexForm.TreeOf(Db, s), Is.Null, "every node but the capstone: still the base form");
            Max(s, 2);
            Assert.That(ApexForm.TreeOf(Db, s), Is.EqualTo("unbreakable_will"));
            Assert.That(ApexForm.Name(Db, "drake", "unbreakable_will"), Is.EqualTo(Db.Text(Db.Trees["unbreakable_will"].ApexNameKey)));
            Assert.That(ApexForm.Name(Db, "drake", "unbreakable_will"), Is.Not.EqualTo(ApexForm.Name(Db, "drake", null)));

            var e = new VaultEntry { SpeciesId = "drake" };
            e.Trees.Add(new VaultTree { TreeId = "pyromancy", Bought = Db.Trees["pyromancy"].Nodes.Count });
            e.Trees.Add(new VaultTree { TreeId = "inferno_sovereign", Bought = Db.Trees["inferno_sovereign"].Nodes.Count });
            e.Trees.Add(new VaultTree { TreeId = "unbreakable_will", Bought = Db.Trees["unbreakable_will"].Nodes.Count });
            Assert.That(ApexForm.TreeOf(Db, e), Is.EqualTo("inferno_sovereign"), "two capstones: the first in slot order; native trees never count");
        }

        /// <summary>Drake with Heavy Armor and Bulwark slotted and maxed: Living Fortress is offered (specs/15 section 5.6).</summary>
        static RunState DrakeReadyToFuse()
        {
            var s = Drake();
            s.Stats.Def = 300;   // below Unbreakable Will's DEF so no chase offer joins the queue
            Offer(s, "bulwark");
            TrainingRun.TakeTree(Db, s, 0, 2);
            Max(s, SlotOf(s, "heavy_armor"));
            Max(s, SlotOf(s, "bulwark"));
            return s;
        }

        [Test]
        public void Fusion_IsOfferedWhenBothParentsAreMaxed_AndKeepsBoth()
        {
            var s = DrakeReadyToFuse();
            var offer = s.Offers.Single();
            Assert.That((offer.Source, offer.Trees.Single()), Is.EqualTo((OfferSource.Fusion, "living_fortress")));
            int armor = SlotOf(s, "heavy_armor"), bulwark = SlotOf(s, "bulwark");
            var before = TrainingRun.MonsterFighter(Db, s);
            double def = s.Stats.Def, sp = s.Sp;
            int other = Enumerable.Range(0, s.Slots.Length).First(i => i != armor && i != bulwark);
            Assert.Throws<InvalidOperationException>(() => TrainingRun.TakeTree(Db, s, 0, other), "only a parent's slot");

            var r = TrainingRun.TakeTree(Db, s, 0, bulwark);
            Assert.That(s.Slots[bulwark].TreeId, Is.EqualTo("living_fortress"));
            Assert.That(s.Slots[bulwark].Absorbed.Select(a => a.TreeId), Is.EqualTo(new[] { "heavy_armor", "bulwark" }));
            Assert.That(s.Slots[armor], Is.Null, "the other parent's slot comes free");
            Assert.That((s.Stats.Def, s.Sp, r.Refunded, r.Removed.Count), Is.EqualTo((def, sp, 0, 0)), "nothing is lost or refunded");
            Assert.That(TrainingRun.MonsterFighter(Db, s).Skills.Select(k => k.Id), Is.EqualTo(before.Skills.Select(k => k.Id)), "the parents' skills stay");
            Assert.That(ApexForm.TreeOf(Db, s), Is.EqualTo("living_fortress"), "the fused look shows at once");
            Assert.That(TrainingRun.ConditionHolds(Db, s, Db.Trees["unbreakable_will"].Conditions.First(cd => cd.Type == ConditionType.TreeMaxed)),
                "an absorbed parent still counts for chase conditions");

            Max(s, bulwark);
            Assert.That(TrainingRun.MonsterFighter(Db, s).Triggers.Select(t => t.Skill.Id), Does.Contain("siege_return"), "the duo capstone");
            s.Phase = RunPhase.Finished;   // a test shortcut: Seal reads the slots, not how the run ended
            s.End = RunEnd.Completed;
            var e = Vault.Seal(Db, s, 0);
            Assert.That(e.Trees.Where(t => t.FusedInto == "living_fortress").Select(t => t.TreeId), Is.EquivalentTo(new[] { "heavy_armor", "bulwark" }));
            Assert.That(ApexForm.TreeOf(Db, e), Is.EqualTo("living_fortress"));
        }

        [Test]
        public void Fusion_OverwrittenTakesItsParents_AndADiscardIsNotReoffered()
        {
            var s = DrakeReadyToFuse();
            TrainingRun.DiscardOffer(Db, s);
            Offer(s, "vital_core");
            TrainingRun.TakeTree(Db, s, 0, Array.IndexOf(s.Slots, null));
            Assert.That(s.Offers, Is.Empty, "a discarded fusion is not offered again while its parents stay ready");

            s = DrakeReadyToFuse();
            TrainingRun.TakeTree(Db, s, 0, SlotOf(s, "heavy_armor"));
            int slot = SlotOf(s, "living_fortress");
            Max(s, slot);
            double def0 = s.Stats.Def - s.Slots[slot].Absorbed.Concat(new[] { s.Slots[slot] }).Sum(t => t.FlatAdded[Array.IndexOf(StatIds.Trained, StatIds.Def)]);
            int expected = s.Slots[slot].Absorbed.Concat(new[] { s.Slots[slot] })
                .Sum(t => (int)Math.Floor(Db.Trees[t.TreeId].Nodes.Take(t.Bought).Sum(n => n.Cost) * Db.Constants.TreeRefund));
            Offer(s, "vital_core");
            var r = TrainingRun.TakeTree(Db, s, 0, slot);
            Assert.That(r.Refunded, Is.EqualTo(expected), "the fused tree and both parents are refunded, each rounded down");
            Assert.That(s.Stats.Def, Is.EqualTo(def0).Within(1e-9), "and their flat stats come off");
            Assert.That(s.Slots.Any(x => x != null && (x.TreeId == "heavy_armor" || x.TreeId == "bulwark")), Is.False);
        }

        [Test]
        public void PendingChaseOffer_IsWithdrawn_WhenItsConditionsBreak()
        {
            var s = Drake();
            s.Stats.Def = 600;
            s.FightsWon = 2;
            Offer(s, "vital_core");                       // queued before the chase offer
            Max(s, SlotOf(s, "heavy_armor"));
            Assert.That(s.Offers.Count, Is.EqualTo(2));
            TrainingRun.TakeTree(Db, s, 0, SlotOf(s, "heavy_armor"));
            Assert.That(s.Offers, Is.Empty, "Heavy Armor is gone, so Unbreakable Will is no longer unlocked");
        }

        [Test]
        public void ChaseTree_OverwritingItsOwnPrerequisite_LocksAtOnce()
        {
            var s = DrakeWithUnbreakableWillOffered();
            var r = TrainingRun.TakeTree(Db, s, 0, SlotOf(s, "heavy_armor"));
            Assert.That(r.Removed, Is.EquivalentTo(new[] { "heavy_armor", "unbreakable_will" }));
            Assert.That(s.Slots.Count(x => x != null), Is.EqualTo(1));
        }

        [Test]
        public void DeclinedChaseTree_IsNotReofferedWhileItStaysUnlocked()
        {
            var s = DrakeWithUnbreakableWillOffered();
            TrainingRun.DiscardOffer(Db, s);
            s.Sp = 5;
            TrainingRun.SpendPoint(Db, s, 0);
            Assert.That(s.Offers, Is.Empty);
        }

        [Test]
        public void UseBook_OffersItsTree_AndIsConsumed()
        {
            var s = Drake(7, "book_echoes");
            Assert.That(s.Books, Is.EqualTo(new[] { "book_echoes" }));
            TrainingRun.UseBook(Db, s, "book_echoes");
            Assert.That(s.Books, Is.Empty);
            var offer = s.Offers.Single();
            Assert.That(offer.Source, Is.EqualTo(OfferSource.Book));
            Assert.That(offer.Trees, Is.EqualTo(new[] { Db.SkillBooks["book_echoes"].TreeId }));
            Assert.Throws<InvalidOperationException>(() => TrainingRun.UseBook(Db, s, "book_echoes"));
            Assert.Throws<ArgumentException>(() => TrainingRun.Start(Db, "drake", 1, new[] { "no_such_book" }));
        }

        [Test]
        public void SkillPoints_PerCycleAndPerWin()
        {
            var s = Drake();
            double m = K.StatMax;
            s.Stats = new MonsterStats { Hp = m, Str = m, Int = m, Wis = m, Dex = m, Def = m, Spd = m, Luk = m };
            while (s.Phase == RunPhase.Training) TrainingRun.Rest(Db, s);
            var r = TrainingRun.Fight(Db, s);
            Assert.That(r.Won, Is.True);
            Assert.That(r.SpGained, Is.EqualTo(K.SpPerWin));
            Assert.That(s.Sp, Is.EqualTo(2 * K.SpPerCycle + K.SpPerWin));
        }

        [Test]
        public void RunTally_AddsUpFightTallies()
        {
            var s = Drake();
            int fights = 0;
            double dealt = 0;
            while (fights < 3)
            {
                while (s.Phase == RunPhase.Training) TrainingRun.Rest(Db, s);
                dealt += TrainingRun.Fight(Db, s).Fight.Final.Units[0].Tally.DamageDealt;
                fights++;
            }
            Assert.That(s.Tally.DamageDealt, Is.EqualTo(dealt).Within(1e-9));
            Assert.That(TrainingRun.TallyValue(s, "wins"), Is.EqualTo(s.FightsWon));
            Assert.That(TrainingRun.TallyValue(s, "damage_fire"), Is.EqualTo(s.Tally.DamageDealt).Within(1e-9), "drake damage is fire");
        }
    }
}
