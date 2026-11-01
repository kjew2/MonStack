using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Rolled Awakening trees (specs/15 section 4.1, issue #121): any stat's trees, rolled filler nodes and capstone growth.</summary>
    public class RolledTreeTests
    {
        static ContentDb Db => TestContent.Db;
        static Constants K => Db.Constants;

        static IEnumerable<TreeDef> Generic => Db.TreeList.Where(t => t.Kind == TreeKind.Generic);

        static bool Filler(TreeNodeDef n) => n.Kind == NodeKind.Growth || n.Kind == NodeKind.Stat;

        [Test]
        public void RollTree_KeepsThePremadeTree_AndRollsItsNumbersInRange()
        {
            var r = new RngStream("test", 42);
            foreach (var t in Generic)
            {
                string own = t.OfferStat ?? t.Nodes.First(Filler).Stat;
                var amounts = new HashSet<double>();
                for (int k = 0; k < 200; k++)
                {
                    var roll = TrainingRun.RollTree(Db, r, t);
                    Assert.That(roll.Stat, Is.EqualTo(own), "a stat's tree keeps its stat; an any-stat tree takes its first filler node's");
                    Assert.That(roll.CapGrowth, Is.InRange(K.RollCapGrowthMin, K.RollCapGrowthMax));
                    var nodes = TrainingRun.Nodes(Db, t.Id, roll);
                    Assert.That(nodes.Count, Is.EqualTo(t.Nodes.Count));
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        var (n, c) = (nodes[i], t.Nodes[i]);
                        Assert.That((n.Id, n.Cost, n.Kind, n.Index, n.Stat), Is.EqualTo((c.Id, c.Cost, c.Kind, c.Index, c.Stat)), "kj: premade trees, only the numbers roll");
                        if (!Filler(c))
                        {
                            Assert.That(roll.NodeStats[i], Is.Null);
                            Assert.That(n, Is.SameAs(c), "skill, trigger and rule nodes are not rolled");
                            continue;
                        }
                        amounts.Add(n.Amount);
                        bool isOwn = n.Stat == own;
                        if (n.Kind == NodeKind.Growth)
                            Assert.That(n.Amount, isOwn ? Is.InRange(K.RollGrowthOwnMin, K.RollGrowthOwnMax) : Is.InRange(K.RollGrowthOffMin, K.RollGrowthOffMax));
                        else
                        {
                            Assert.That(n.Amount, isOwn ? Is.InRange(K.RollFlatOwnMin, K.RollFlatOwnMax) : Is.InRange(K.RollFlatOffMin, K.RollFlatOffMax));
                            Assert.That(n.Amount, Is.EqualTo(Math.Round(n.Amount)), "flat points are whole");
                        }
                    }
                }
                Assert.That(amounts.Count, Is.GreaterThan(10), t.Id + ": the amounts vary from roll to roll");
            }
        }

        /// <summary>Drill STR from just below the first milestone until the Awakening opens.</summary>
        static TreeOffer StrAwakening(RunState s)
        {
            s.Stats.Str = Db.StatMilestones[0] - 1;
            while (s.Offers.Count == 0) { s.Energy = K.RunEnergyMax; TrainingRun.Drill(Db, s, StatIds.Str); }
            return s.Offers.Single();
        }

        [Test]
        public void Awakening_OffersAnyStatsTrees_Rolled()
        {
            var stats = new HashSet<string>();
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var o = StrAwakening(TrainingRun.Start(Db, "drake", seed));
                Assert.That(o.Rolls.Count, Is.EqualTo(o.Trees.Count));
                Assert.That(o.Rolls, Has.None.Null);
                foreach (var id in o.Trees) stats.Add(Db.Trees[id].OfferStat);
            }
            Assert.That(stats.Count(x => x != null && x != StatIds.Str), Is.GreaterThan(3), "STR's Awakening offers other stats' trees (issue #121)");
        }

        [Test]
        public void Awakening_AlwaysOffersTheCrossingStatsTreeFirst()
        {
            var own = Generic.Where(t => t.OfferStat == StatIds.Str).Select(t => t.Id).ToList();
            Assume.That(own.Count, Is.GreaterThan(1), "content 5.4: each stat has more than one Awakening tree");
            var firsts = new HashSet<string>();
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var o = StrAwakening(TrainingRun.Start(Db, "drake", seed));
                Assert.That(own, Does.Contain(o.Trees[0]), "kj, issue #121: one of the crossing stat's own trees is always a card");
                Assert.That(o.Trees.Count, Is.EqualTo(K.DraftSize));
                firsts.Add(o.Trees[0]);
            }
            Assert.That(firsts, Is.EquivalentTo(own), "the first card is drawn from all of the stat's own trees");
        }

        [Test]
        public void Awakening_WithTheStatsTreesSlotted_DrawsEveryCardFromAnyStat()
        {
            var own = Generic.Where(t => t.OfferStat == StatIds.Str).Select(t => t.Id).ToList();
            var s = TrainingRun.Start(Db, "drake", 4);
            for (int i = 0; i < own.Count; i++)
            {
                s.Offers.Add(new TreeOffer { Source = OfferSource.Milestone, Stat = StatIds.Str, Threshold = 300, Trees = { own[i] } });
                TrainingRun.TakeTree(Db, s, 0, 2 + i);
            }
            var o = StrAwakening(s);
            Assert.That(o.Trees.Intersect(own), Is.Empty);
            Assert.That(o.Trees.Count, Is.EqualTo(K.DraftSize));
        }

        [Test]
        public void LegacyRun_DraftsItsStatsTrees_Unrolled()
        {
            for (ulong seed = 1; seed <= 10; seed++)
            {
                var o = StrAwakening(TrainingRun.Start(Db, "drake", seed, null, new RunModifiers { LegacyDrafts = true }));
                Assert.That(o.Rolls, Is.Empty);
                Assert.That(o.Trees.All(id => Db.Trees[id].OfferStat == null || Db.Trees[id].OfferStat == StatIds.Str));
                Assert.That(o.Trees, Does.Not.Contain("berserker"), "a legacy run drafts only the trees it was played with (before content 5.4)");
            }
        }

        [Test]
        public void RolledTree_GivesItsRolledStats_AndCapstoneGrowthOnceMaxed()
        {
            var t = Db.Trees["fortune"];
            var roll = TrainingRun.RollTree(Db, new RngStream("test", 3), t);
            var nodes = TrainingRun.Nodes(Db, t.Id, roll);
            double lukGrowth = nodes.Where(n => n.Kind == NodeKind.Growth && n.Stat == StatIds.Luk).Sum(n => n.Amount);
            double lukFlat = nodes.Where(n => n.Kind == NodeKind.Stat && n.Stat == StatIds.Luk).Sum(n => n.Amount);
            Assume.That(lukGrowth, Is.GreaterThan(0));
            var s = TrainingRun.Start(Db, "drake", 5);
            s.Stats.Luk = 100;   // far from a milestone, so no Awakening joins the queue
            s.Offers.Add(new TreeOffer { Source = OfferSource.Milestone, Stat = StatIds.Str, Threshold = 300, Trees = { t.Id }, Rolls = { roll } });
            TrainingRun.TakeTree(Db, s, 0, 2);
            double g0 = TrainingRun.Growth(Db, s, StatIds.Luk);
            s.Sp = 100;
            for (int i = 0; i < t.Nodes.Count - 1; i++) TrainingRun.SpendPoint(Db, s, 2);
            Assert.That(s.Stats.Luk, Is.EqualTo(100 + lukFlat), "its rolled flat points");
            Assert.That(TrainingRun.Growth(Db, s, StatIds.Luk), Is.EqualTo(g0 + lukGrowth).Within(1e-9), "its rolled growth, no capstone growth before the capstone");
            TrainingRun.SpendPoint(Db, s, 2);
            Assert.That(TrainingRun.Growth(Db, s, StatIds.Luk), Is.EqualTo(g0 + lukGrowth + roll.CapGrowth).Within(1e-9));
            // Overwritten, its flat points and growth go with it.
            s.Offers.Add(new TreeOffer { Source = OfferSource.Milestone, Stat = StatIds.Str, Threshold = 300, Trees = { "vital_core" } });
            TrainingRun.TakeTree(Db, s, 0, 2);
            Assert.That(s.Stats.Luk, Is.EqualTo(100));
            Assert.That(TrainingRun.Growth(Db, s, StatIds.Luk), Is.EqualTo(g0).Within(1e-9));
        }

        [Test]
        public void Replay_RebuildsTheRolls_AndTheVaultKeepsThem()
        {
            var s = TrainingRun.Start(Db, "drake", 21);
            int rolled = 0;
            while (s.Phase != RunPhase.Finished)
            {
                if (s.Offers.Count > 0)
                {
                    int empty = Array.FindIndex(s.Slots, x => x == null);
                    if (empty >= 0) { rolled += s.Offers[0].Rolls.Count > 0 ? 1 : 0; TrainingRun.TakeTree(Db, s, 0, empty); }
                    else TrainingRun.DiscardOffer(Db, s);
                    continue;
                }
                int buy = Array.FindIndex(s.Slots, x => x != null && x.Bought < TrainingRun.Nodes(Db, x).Count && TrainingRun.Nodes(Db, x)[x.Bought].Cost <= s.Sp);
                if (buy >= 0) { TrainingRun.SpendPoint(Db, s, buy); continue; }
                if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s);
                else if (s.Energy < 40) TrainingRun.Rest(Db, s);
                else TrainingRun.Drill(Db, s, StatIds.Str);
            }
            Assume.That(rolled, Is.GreaterThan(0), "the run slotted a rolled tree");
            var replay = TrainingRun.Replay(Db, s.SpeciesId, s.Seed, s.Log, null, s.Mods, s.Ladder);
            Assert.That(TrainingRun.StateHash(replay), Is.EqualTo(TrainingRun.StateHash(s)));
            var e = Vault.Seal(Db, s);
            var slotted = s.Slots.Where(x => x != null && x.Roll != null).ToList();
            foreach (var ts in slotted)
            {
                var vt = e.Trees.Single(x => x.TreeId == ts.TreeId);
                Assert.That(vt.Roll, Is.SameAs(ts.Roll));
                var again = replay.Slots.Single(x => x != null && x.TreeId == ts.TreeId).Roll;
                Assert.That((again.Stat, again.CapGrowth, again.NodeStats, again.Amounts), Is.EqualTo((ts.Roll.Stat, ts.Roll.CapGrowth, ts.Roll.NodeStats, ts.Roll.Amounts)));
            }
        }
    }
}
