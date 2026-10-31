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
        public void RollTree_StaysInItsRanges_AndKeepsSkillsAndCosts()
        {
            var r = new RngStream("test", 42);
            int own = 0, filler = 0;
            foreach (var t in Generic)
                for (int k = 0; k < 200; k++)
                {
                    var roll = TrainingRun.RollTree(Db, r, t);
                    Assert.That(StatIds.Trained, Does.Contain(roll.Stat));
                    if (t.OfferStat != null) Assert.That(roll.Stat, Is.EqualTo(t.OfferStat), "a stat's tree keeps its stat");
                    Assert.That(roll.CapGrowth, Is.InRange(K.RollCapGrowthMin, K.RollCapGrowthMax));
                    var nodes = TrainingRun.Nodes(Db, t.Id, roll);
                    Assert.That(nodes.Count, Is.EqualTo(t.Nodes.Count));
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        var (n, c) = (nodes[i], t.Nodes[i]);
                        Assert.That((n.Id, n.Cost, n.Kind, n.Index), Is.EqualTo((c.Id, c.Cost, c.Kind, c.Index)));
                        if (!Filler(c))
                        {
                            Assert.That(roll.NodeStats[i], Is.Null);
                            Assert.That(n, Is.SameAs(c), "skill, trigger and rule nodes are not rolled");
                            continue;
                        }
                        filler++;
                        bool isOwn = n.Stat == roll.Stat;
                        if (isOwn) own++;
                        Assert.That(StatIds.Trained, Does.Contain(n.Stat));
                        if (n.Kind == NodeKind.Growth)
                            Assert.That(n.Amount, isOwn ? Is.InRange(K.RollGrowthOwnMin, K.RollGrowthOwnMax) : Is.InRange(K.RollGrowthOffMin, K.RollGrowthOffMax));
                        else
                        {
                            Assert.That(n.Amount, isOwn ? Is.InRange(K.RollFlatOwnMin, K.RollFlatOwnMax) : Is.InRange(K.RollFlatOffMin, K.RollFlatOffMax));
                            Assert.That(n.Amount, Is.EqualTo(Math.Round(n.Amount)), "flat points are whole");
                        }
                    }
                }
            Assert.That((double)own / filler, Is.EqualTo(K.RollOwnChance).Within(0.05), "about ROLL_OWN_CHANCE of filler nodes are on the own stat");
        }

        [Test]
        public void AnyStatTree_RollsItsOwnStat()
        {
            var any = Generic.FirstOrDefault(t => t.OfferStat == null);
            Assume.That(any, Is.Not.Null, "content has an any-stat generic tree");
            var r = new RngStream("test", 9);
            var stats = Enumerable.Range(0, 100).Select(_ => TrainingRun.RollTree(Db, r, any).Stat).Distinct().ToList();
            Assert.That(stats.Count, Is.GreaterThan(4), "an any-stat tree's own stat is rolled");
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
        public void LegacyRun_DraftsItsStatsTrees_Unrolled()
        {
            for (ulong seed = 1; seed <= 10; seed++)
            {
                var o = StrAwakening(TrainingRun.Start(Db, "drake", seed, null, new RunModifiers { LegacyDrafts = true }));
                Assert.That(o.Rolls, Is.Empty);
                Assert.That(o.Trees.All(id => Db.Trees[id].OfferStat == null || Db.Trees[id].OfferStat == StatIds.Str));
            }
        }

        [Test]
        public void RolledTree_GivesItsRolledStats_AndCapstoneGrowthOnceMaxed()
        {
            var t = Db.Trees["fortune"];
            var roll = new TreeRoll { Stat = StatIds.Luk, NodeStats = new string[t.Nodes.Count], Amounts = new double[t.Nodes.Count], CapGrowth = 0.5 };
            for (int i = 0; i < t.Nodes.Count; i++)
                if (Filler(t.Nodes[i])) { roll.NodeStats[i] = StatIds.Luk; roll.Amounts[i] = t.Nodes[i].Kind == NodeKind.Growth ? 0.3 : 25; }
            var s = TrainingRun.Start(Db, "drake", 5);
            s.Stats.Luk = 100;   // far from a milestone, so no Awakening joins the queue
            s.Offers.Add(new TreeOffer { Source = OfferSource.Milestone, Stat = StatIds.Str, Threshold = 300, Trees = { t.Id }, Rolls = { roll } });
            TrainingRun.TakeTree(Db, s, 0, 2);
            double g0 = TrainingRun.Growth(Db, s, StatIds.Luk);
            int growth = t.Nodes.Count(n => n.Kind == NodeKind.Growth), flat = t.Nodes.Count(n => n.Kind == NodeKind.Stat);
            s.Sp = 100;
            for (int i = 0; i < t.Nodes.Count - 1; i++) TrainingRun.SpendPoint(Db, s, 2);
            Assert.That(s.Stats.Luk, Is.EqualTo(100 + 25 * flat));
            Assert.That(TrainingRun.Growth(Db, s, StatIds.Luk), Is.EqualTo(g0 + 0.3 * growth).Within(1e-9), "no capstone growth before the capstone");
            TrainingRun.SpendPoint(Db, s, 2);
            Assert.That(TrainingRun.Growth(Db, s, StatIds.Luk), Is.EqualTo(g0 + 0.3 * growth + 0.5).Within(1e-9));
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
