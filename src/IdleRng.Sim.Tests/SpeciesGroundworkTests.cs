using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
using IdleRng.TestFixtures;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>
    /// Species groundwork (#160, kj 2026-11-03): an idea-pool tree a species lane turns native-only is never drafted again but
    /// keeps working where a run already holds it, and a species' second chase tree may need no book (a harder in-run chase).
    /// </summary>
    public class SpeciesGroundworkTests
    {
        static readonly Lazy<ContentDb> _native = new Lazy<ContentDb>(() => ContentFixtures.WithNativeTrees(ContentFixtures.SpeciesTrees));
        static ContentDb Native => _native.Value;

        static void Max(ContentDb c, RunState s, int slot)
        {
            int n = c.Trees[s.Slots[slot].TreeId].Nodes.Count;
            while (s.Slots[slot] != null && s.Slots[slot].Bought < n) { s.Sp = Math.Max(s.Sp, 10); TrainingRun.SpendPoint(c, s, slot); }
        }

        static int SlotOf(RunState s, string tree) => Array.FindIndex(s.Slots, x => x != null && x.TreeId == tree);

        [Test]
        public void NativeOnlyTrees_Load_WithTheirNodesAndRuleCapstone()
        {
            var shipped = TestContent.Db;
            foreach (var id in ContentFixtures.SpeciesTrees)
            {
                var t = Native.Trees[id];
                Assert.That((t.Kind, t.OfferStat, t.OfferThreshold), Is.EqualTo((TreeKind.Native, (string)null, 0)), id);
                Assert.That(t.Nodes.Select(n => (n.Id, n.Kind, n.Cost, n.RuleId)), Is.EqualTo(shipped.Trees[id].Nodes.Select(n => (n.Id, n.Kind, n.Cost, n.RuleId))), id);
                Assert.That(t.Nodes[t.Nodes.Count - 1].RuleId, Is.EqualTo(id), id + ": the signature rule stays its capstone");
            }
            int stillGeneric = ContentFixtures.SpeciesTrees.Count(id => shipped.Trees[id].Kind == TreeKind.Generic);
            Assert.That(Native.TreeList.Count(t => t.Kind == TreeKind.Generic), Is.EqualTo(shipped.TreeList.Count(t => t.Kind == TreeKind.Generic) - stillGeneric),
                "every other generic tree stays in the Awakening pool");
        }

        [Test]
        public void Awakenings_NeverOfferANativeOnlyTree()
        {
            var flipped = new HashSet<string>(ContentFixtures.SpeciesTrees);
            int offers = 0;
            foreach (var stat in StatIds.Trained)
                for (ulong seed = 1; seed <= 25; seed++)
                {
                    var s = TrainingRun.Start(Native, "drake", seed);
                    typeof(MonsterStats).GetProperties().Single(p => p.Name.ToLowerInvariant() == stat).SetValue(s.Stats, (double)(Native.StatMilestones[0] - 1));
                    while (s.Offers.Count == 0) { s.Energy = Native.Constants.RunEnergyMax; TrainingRun.Drill(Native, s, stat); }
                    var o = s.Offers.Single();
                    Assert.That(o.Source, Is.EqualTo(OfferSource.Milestone));
                    Assert.That(o.Trees.Where(flipped.Contains), Is.Empty, $"{stat} seed {seed}: {string.Join(",", o.Trees)}");
                    Assert.That(o.Trees.All(id => Native.Trees[id].Kind == TreeKind.Generic), Is.True);
                    offers++;
                }
            Assert.That(offers, Is.EqualTo(StatIds.Trained.Length * 25));
        }

        [Test]
        public void ARunHoldingTheTree_FromAnEarlierAwakening_KeepsItSlottedAndWorking()
        {
            // Taken from an Awakening while blood_magic was generic (shipped content), then the content turns it native (rule 1 of
            // #132: a tree a monster holds stays slotted).
            var shipped = TestContent.Db;
            var s = TrainingRun.Start(shipped, "drake", 3);
            s.Offers.Add(new TreeOffer { Source = OfferSource.Milestone, Stat = StatIds.Hp, Threshold = shipped.StatMilestones[0], Trees = { "blood_magic" },
                                         Rolls = { TrainingRun.RollTree(shipped, s.Draft, shipped.Trees["blood_magic"]) } });
            TrainingRun.TakeTree(shipped, s, 0, 2);
            Assert.That(s.Slots[2].TreeId, Is.EqualTo("blood_magic"));

            Max(Native, s, 2);
            Assert.That(TrainingRun.MonsterFighter(Native, s).Rules, Does.Contain(RuleModifiers.BloodMagic), "its capstone still works");
            for (int i = 0; i < 6 && s.Phase != RunPhase.Finished; i++)
            {
                while (s.Offers.Count > 0) TrainingRun.DiscardOffer(Native, s);
                if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Native, s);
                else TrainingRun.Rest(Native, s);
            }
            Assert.That(s.Slots[2].TreeId, Is.EqualTo("blood_magic"), "still slotted after more commands");
        }

        [Test]
        public void ASecondChaseTreeWithoutABook_Loads_AndUnlocksInRun()
        {
            var c = ContentFixtures.WithDrakeSecondChaseWithoutBook();
            var second = c.Trees["inferno_sovereign"];
            Assert.That((second.Species, second.BookGated, second.SecondChase), Is.EqualTo(("drake", false, true)));
            Assert.That(c.BookTreesNeeded(second), Is.Empty);
            var run = c.Trees["unbreakable_will"];
            Assert.That((run.BookGated, run.SecondChase), Is.EqualTo((false, false)));

            var s = TrainingRun.Start(c, "drake", 5);   // no books
            Assert.That(TrainingRun.ChaseOpen(c, s, second), Is.True);
            Max(c, s, SlotOf(s, "pyromancy"));
            Max(c, s, SlotOf(s, "heavy_armor"));
            Assert.That(s.Offers.SelectMany(o => o.Trees), Does.Not.Contain("inferno_sovereign"), "the fire tally is not met yet");
            s.Tally.DamageByElement["fire"] = 5000;
            TrainingRun.Rest(c, s);
            Assert.That(s.Offers.Any(o => o.Source == OfferSource.Chase && o.Trees.Contains("inferno_sovereign")), Is.True, "offered with no book");
        }

        [Test]
        public void ShippedContent_SecondChaseTreeIsBookGatedOrAHarderInRunOne()
        {
            var c = TestContent.Db;
            foreach (var sp in c.SpeciesList)
            {
                var run = c.Trees[sp.ChaseTreeRun];
                var second = c.Trees[sp.ChaseTreeBook];
                Assert.That((run.SecondChase, second.SecondChase), Is.EqualTo((false, true)), sp.Id);
                Assert.That(second.BookGated, Is.EqualTo(c.BookTreesNeeded(second).Any()), sp.Id);
                if (second.BookGated) continue;
                // A harder in-run chase (kj, 2026-11-03, #160): both native trees maxed, and every condition of the in-run chase tree
                // asked for again, as hard or harder, so a run that unlocks it has unlocked the in-run one too.
                bool Maxed(string tree) => second.Conditions.Any(cd => cd.Type == ConditionType.TreeMaxed && cd.Ref == tree);
                Assert.That(Maxed(sp.NativeTree1) && Maxed(sp.NativeTree2), Is.True, sp.Id + ": both native trees maxed");
                foreach (var cd in run.Conditions)
                {
                    bool covered = cd.Type == ConditionType.TreeMaxed ? Maxed(cd.Ref)
                        : cd.Type == ConditionType.NodeOwned ? Maxed(c.TreeNodes[cd.Ref].TreeId) || second.Conditions.Any(x => x.Type == cd.Type && x.Ref == cd.Ref)
                        : second.Conditions.Any(x => x.Type == cd.Type && x.Ref == cd.Ref && x.Amount >= cd.Amount);
                    Assert.That(covered, Is.True, $"{sp.Id}: the second chase tree asks for {cd.Id} again, as hard or harder");
                }
            }
        }
    }
}
