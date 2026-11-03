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
    /// <summary>
    /// Idea-pool lane F (#139): the tree and kit build capstones mimic_core, heirloom (and the growth_use node kind), mastery and
    /// limit_break (specs/15 section 7, Training/Rules/LaneF.cs).
    /// </summary>
    public class LaneFRuleTests
    {
        static ContentDb Db => TestContent.Db;
        static Constants K => Db.Constants;
        static double C(string key) => Db.ConstantValues[key];

        static readonly string[] LaneTrees = { "mimic_core", "heirloom", "mastery", "limit_break" };

        /// <summary>A drake run (slot 0 Pyromancy, slot 1 Heavy Armor) with plenty of SP and no offer pending.</summary>
        static RunState Drake(ulong seed = 7)
        {
            var s = TrainingRun.Start(Db, "drake", seed);
            s.Sp = 500;
            return s;
        }

        /// <summary>Put <paramref name="tree"/> in <paramref name="slot"/> with <paramref name="bought"/> nodes (all by default), as content (no roll).</summary>
        static TreeSlot Put(RunState s, int slot, string tree, int bought = -1, TreeRoll roll = null)
        {
            var ts = new TreeSlot { TreeId = tree, Bought = bought < 0 ? Db.Trees[tree].Nodes.Count : bought, Roll = roll };
            s.Slots[slot] = ts;
            return ts;
        }

        static string Capstone(string tree) => Db.Trees[tree].Nodes.Last().Id;

        static Fighter Kit(RunState s) => TrainingRun.MonsterFighter(Db, s);

        /// <summary>Win (strong monster) or lose (feeble monster) the due rank fight; offers are cleared first.</summary>
        static RankFightResult Fight(RunState s, bool win)
        {
            s.Offers.Clear();
            foreach (var id in StatIds.Trained) SetStat(s, id, win ? 999 : 1);
            s.Phase = RunPhase.AwaitFight;
            var r = TrainingRun.Fight(Db, s);
            Assert.That(r.Won, Is.EqualTo(win), "the scripted fight went the other way");
            return r;
        }

        /// <summary>One drill at full energy (no injury), whatever the phase and offers were.</summary>
        static DrillResult Train(RunState s, string stat)
        {
            s.Energy = K.RunEnergyMax;
            s.Offers.Clear();
            s.Phase = RunPhase.Training;
            if (s.ApLeft <= 0) s.ApLeft = 1;
            return TrainingRun.Drill(Db, s, stat);
        }

        static void SetStat(RunState s, string id, double v)
        {
            var st = s.Stats;
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

        // ------------------------------------------------------------------ content

        [Test]
        public void Content_TheFourTreesAreGenericAt400OrNative_WithTheirRuleCapstones()
        {
            foreach (var id in LaneTrees)
            {
                var t = Db.Trees[id];
                IdeaPool.AssertOfferedOrNative(Db, t, id == "mastery" ? StatIds.Wis : null);
                Assert.That(t.Nodes.Count, Is.EqualTo(8), id);
                Assert.That((t.Nodes[7].Kind, t.Nodes[7].RuleId, t.Nodes[7].Cost), Is.EqualTo((NodeKind.Rule, id, 3)), id);
                Assert.That(t.Nodes[4].Kind, Is.EqualTo(NodeKind.Skill), id);
                Assert.That(RuleModifiers.IsKnown(id), Is.True, id);
                Assert.That(BattleText.RuleDesc(Db, id), Does.Not.Contain("{"), id + ": the description fills its constants");
            }
            Assert.That(Db.Trees["mastery"].OfferStat, Is.EqualTo(StatIds.Wis));
            Assert.That(new[] { "mimic_core", "heirloom", "limit_break" }.Select(id => Db.Trees[id].OfferStat), Is.All.Null, "any-stat trees");
            var heir = Db.Trees["heirloom"].Nodes;
            Assert.That(new[] { 0, 2, 5 }.Select(i => heir[i].Kind), Is.All.EqualTo(NodeKind.GrowthUse), "Heirloom's filler growth is keepsake growth");
        }

        [Test]
        public void Constants_LoadAndAreInRange()
        {
            var k = new TrainingRun.LaneFConstants(Db);
            Assert.That((k.KeepsakeStep, k.KeepsakeDrills, k.KeepsakeMax, k.MasteryWins, k.LimitBreakMax),
                Is.EqualTo((C("KEEPSAKE_STEP"), (int)C("KEEPSAKE_DRILLS"), C("KEEPSAKE_MAX"), (int)C("MASTERY_WINS"), C("LIMIT_BREAK_MAX"))));
            Assert.That(k.LimitBreakMax, Is.GreaterThanOrEqualTo(K.StatMax));
        }

        [Test]
        public void RunRules_DoNothingInAFight_In1v1_4v1_And4v4()
        {
            // The four are run rules: in the fight kit (1v1 run fights and gauntlet, 4v1 Dungeon, 4v4 Endless and PvP) a carrier
            // fights exactly as the same unit without them.
            var rules = FightRules.FromContent(Db);
            Fighter Unit(string id, bool carrier)
            {
                var f = new Fighter { Id = id, Element = "fire", Stats = new MonsterStats { Hp = 400, Str = 300, Int = 100, Wis = 200, Dex = 200, Def = 250, Spd = 220, Luk = 150 } };
                if (carrier) f.Rules.AddRange(LaneTrees);
                return f;
            }
            foreach (var (a, b) in new[] { (1, 1), (4, 1), (4, 4) })
                for (ulong seed = 1; seed <= 3; seed++)
                {
                    FightResult Play(bool carrier) => BattleSim.Resolve(rules,
                        Enumerable.Range(0, a).Select(i => Unit("a" + i, carrier)).ToList(), Enumerable.Range(0, b).Select(i => Unit("b" + i, false)).ToList(), seed);
                    var with = Play(true);
                    var without = Play(false);
                    Assert.That((with.Outcome, with.Ticks), Is.EqualTo((without.Outcome, without.Ticks)), $"{a}v{b} seed {seed}");
                    Assert.That(with.Final.Units.Select(u => u.Hp), Is.EqualTo(without.Final.Units.Select(u => u.Hp)), $"{a}v{b} seed {seed}");
                }
        }

        // ------------------------------------------------------------------ mimic_core

        [Test]
        public void Mimic_CopiesThePreviousSlotsCapstone_BoughtOrNot()
        {
            var s = Drake();
            s.Slots[1].Bought = 0;   // Heavy Armor: nothing bought, its capstone is Plate Mastery
            Put(s, 2, "mimic_core");
            Assert.That(TrainingRun.KitExtra(Db, s), Is.EqualTo(new[] { Capstone("heavy_armor") }));
            Assert.That(Kit(s).Skills.Select(k => k.Id), Does.Contain("plate_mastery"));
            Assert.That(Kit(s).Rules, Does.Contain(RuleModifiers.MimicCore), "its own rule is in the kit (it does nothing in a fight)");
        }

        [Test]
        public void Mimic_NeedsItsCapstone_AndAPreviousTree()
        {
            var s = Drake();
            Put(s, 2, "mimic_core", 7);
            Assert.That(TrainingRun.KitExtra(Db, s), Is.Empty, "capstone not bought");
            Put(s, 3, "mimic_core");
            s.Slots[2] = null;
            Assert.That(TrainingRun.KitExtra(Db, s), Is.Empty, "the slot before it is empty");
            s.Slots[3] = null;
            Put(s, 0, "mimic_core");
            Assert.That(TrainingRun.KitExtra(Db, s), Is.Empty, "slot 0 has no slot before it");
        }

        [Test]
        public void Mimic_NeverCopiesChaseOrFusionCapstones()
        {
            var s = Drake();
            Put(s, 1, "unbreakable_will", 0);
            Put(s, 2, "mimic_core");
            Assert.That(TrainingRun.KitExtra(Db, s), Is.Empty, "chase");
            Put(s, 1, "living_fortress", 0);
            Assert.That(TrainingRun.KitExtra(Db, s), Is.Empty, "fusion");
        }

        [Test]
        public void Mimic_FollowsTheSlotBeforeIt_WhenThatTreeChanges()
        {
            var s = Drake();
            Put(s, 2, "mimic_core");
            s.Offers.Add(new TreeOffer { Source = OfferSource.Book, Trees = { "fortune" } });
            TrainingRun.TakeTree(Db, s, 0, 1);   // Fortune overwrites Heavy Armor
            Assert.That(TrainingRun.KitExtra(Db, s), Is.EqualTo(new[] { Capstone("fortune") }));
            Assert.That(Kit(s).Skills.Select(k => k.Id), Does.Contain("jackpot").And.Not.Contain("plate_mastery"));
        }

        [Test]
        public void Mimic_SealedInTheVault_KeepsTheCopy()
        {
            var s = Drake();
            Put(s, 2, "mimic_core");
            s.Phase = RunPhase.Finished;
            s.End = RunEnd.Completed;
            var e = Vault.Seal(Db, s);
            Assert.That(e.KitExtra, Is.EqualTo(new[] { Capstone("heavy_armor") }));
            Assert.That(e.Fighter(Db).Skills.Select(k => k.Id), Is.EqualTo(Kit(s).Skills.Select(k => k.Id)));
            Assert.That(e.Fighter(Db).Rules, Is.EqualTo(Kit(s).Rules));
            Assert.That(Vault.Seal(Db, Drake().Also(x => { x.Phase = RunPhase.Finished; x.End = RunEnd.Completed; })).KitExtra, Is.Null,
                "no extra nodes: nothing stored (lean JSON)");
        }

        // ------------------------------------------------------------------ heirloom

        [Test]
        public void Heirloom_GrowthUse_AddsItsAmount_ThenGrowsWithDrills()
        {
            var s = Drake();
            double g0 = TrainingRun.Growth(Db, s, StatIds.Hp);
            var ts = Put(s, 2, "heirloom", 1);   // n0: growth_use HP 0.1
            double amount = Db.Trees["heirloom"].Nodes[0].Amount, step = C("KEEPSAKE_STEP");
            int per = (int)C("KEEPSAKE_DRILLS");
            Assert.That(TrainingRun.Growth(Db, s, StatIds.Hp), Is.EqualTo(g0 + amount).Within(1e-9));
            Assert.That(ts.KeepsakeDrills, Is.Null, "nothing counted yet (lean JSON)");
            for (int i = 0; i < per; i++) Train(s, StatIds.Str);
            Assert.That(ts.KeepsakeDrills, Is.Null, "drills of another stat do not count");
            for (int i = 0; i < per - 1; i++) Train(s, StatIds.Hp);
            Assert.That(TrainingRun.Growth(Db, s, StatIds.Hp), Is.EqualTo(g0 + amount).Within(1e-9), "not yet a full step");
            Train(s, StatIds.Hp);
            Assert.That(ts.KeepsakeDrills[0], Is.EqualTo(per));
            Assert.That(TrainingRun.Growth(Db, s, StatIds.Hp), Is.EqualTo(g0 + amount + step).Within(1e-9), "one step after KEEPSAKE_DRILLS drills");
            Assert.That(TrainingRun.KeepsakeBonus(Db, s, ts, 0), Is.EqualTo(step).Within(1e-9));
        }

        [Test]
        public void Heirloom_Capstone_DoublesTheStep_UpToTheMax()
        {
            var s = Drake();
            var ts = Put(s, 2, "heirloom");
            ts.KeepsakeDrills = new int[8];
            int per = (int)C("KEEPSAKE_DRILLS");
            double step = C("KEEPSAKE_STEP"), max = C("KEEPSAKE_MAX");
            ts.KeepsakeDrills[0] = per;
            Assert.That(TrainingRun.KeepsakeBonus(Db, s, ts, 0), Is.EqualTo(Math.Min(max, 2 * step)).Within(1e-9), "doubled");
            ts.Bought = 7;
            Assert.That(TrainingRun.KeepsakeBonus(Db, s, ts, 0), Is.EqualTo(Math.Min(max, step)).Within(1e-9), "capstone not bought");
            ts.Bought = 8;
            ts.KeepsakeDrills[0] = per * 1000;
            Assert.That(TrainingRun.KeepsakeBonus(Db, s, ts, 0), Is.EqualTo(max).Within(1e-9), "capped at KEEPSAKE_MAX");
        }

        [Test]
        public void Heirloom_Overwritten_LosesItsGrowth()
        {
            var s = Drake();
            double g0 = TrainingRun.Growth(Db, s, StatIds.Hp);
            Put(s, 2, "heirloom").KeepsakeDrills = new[] { 30, 0, 30, 0, 0, 30, 0, 0 };
            Assert.That(TrainingRun.Growth(Db, s, StatIds.Hp), Is.GreaterThan(g0));
            s.Offers.Add(new TreeOffer { Source = OfferSource.Book, Trees = { "fortune" } });
            TrainingRun.TakeTree(Db, s, 0, 2);
            Assert.That(TrainingRun.Growth(Db, s, StatIds.Hp), Is.EqualTo(g0).Within(1e-9));
        }

        [Test]
        public void Heirloom_Rolled_GrowthUseRollsLikeGrowth()
        {
            var r = new RngStream("test", 3);
            var roll = TrainingRun.RollTree(Db, r, Db.Trees["heirloom"]);
            var nodes = TrainingRun.Nodes(Db, "heirloom", roll);
            foreach (int i in new[] { 0, 2, 5 })
            {
                Assert.That(nodes[i].Kind, Is.EqualTo(NodeKind.GrowthUse));
                Assert.That(nodes[i].Amount, Is.InRange(K.RollGrowthOwnMin, K.RollGrowthOwnMax), "own-stat growth range");
            }
        }

        // ------------------------------------------------------------------ mastery

        [Test]
        public void Mastery_MastersATreeAfterItsWins_AndKeepsItsCapstoneWhenOverwritten()
        {
            var s = Drake();   // Pyromancy (slot 0) and Heavy Armor (slot 1) maxed
            s.Slots[0].Bought = 8;
            s.Slots[1].Bought = 8;
            Put(s, 2, "mastery");
            int need = (int)C("MASTERY_WINS");
            for (int i = 0; i < need - 1; i++) Fight(s, true);
            Assert.That(s.Mastered, Is.Null, "not yet");
            Fight(s, true);
            Assert.That(s.Mastered, Is.EqualTo(new[] { Capstone("pyromancy"), Capstone("heavy_armor") }), "slot order");
            Assert.That(s.Slots[2].MasteryWins, Is.EqualTo(0), "Mastery never masters itself");
            Assert.That(TrainingRun.KitExtra(Db, s), Is.Empty, "both still slotted: their capstones come from their trees");

            s.Offers.Add(new TreeOffer { Source = OfferSource.Book, Trees = { "fortune" } });
            TrainingRun.TakeTree(Db, s, 0, 1);   // Fortune overwrites the mastered Heavy Armor
            Assert.That(TrainingRun.KitExtra(Db, s), Is.EqualTo(new[] { Capstone("heavy_armor") }));
            var kit = Kit(s);
            Assert.That(kit.Skills.Last().Id, Is.EqualTo("plate_mastery"), "a kept capstone goes last in kit order");
            Assert.That(kit.Skills.Count(k => k.Id == "rally_cry"), Is.EqualTo(0), "only the capstone, not the filler or skill");
        }

        [Test]
        public void Mastery_LossesAndUnfinishedTreesDoNotCount()
        {
            var s = Drake();
            s.Slots[0].Bought = 8;
            s.Slots[1].Bought = 3;
            Put(s, 2, "mastery");
            Fight(s, false);
            Assert.That(s.Slots[0].MasteryWins, Is.EqualTo(0), "a loss");
            Fight(s, true);
            Assert.That((s.Slots[0].MasteryWins, s.Slots[1].MasteryWins), Is.EqualTo((1, 0)), "Heavy Armor's capstone is not bought");
        }

        [Test]
        public void Mastery_NeedsItsCapstone_AndItsKeptCapstonesGoWithIt()
        {
            var s = Drake();
            s.Slots[1].Bought = 8;
            var m = Put(s, 2, "mastery", 7);
            Fight(s, true);
            Assert.That(s.Slots[1].MasteryWins, Is.EqualTo(0), "Mastery's capstone not bought");
            m.Bought = 8;
            s.Mastered = new List<string> { Capstone("heavy_armor") };
            s.Slots[1] = null;
            Assert.That(TrainingRun.KitExtra(Db, s), Is.EqualTo(new[] { Capstone("heavy_armor") }));
            s.Offers.Add(new TreeOffer { Source = OfferSource.Book, Trees = { "fortune" } });
            TrainingRun.TakeTree(Db, s, 0, 2);   // Mastery itself is overwritten
            Assert.That(TrainingRun.KitExtra(Db, s), Is.Empty, "overwriting Mastery drops its effect");
        }

        [Test]
        public void Mastery_NeverMastersChaseOrFusionTrees()
        {
            var s = Drake();
            Put(s, 0, "living_fortress");
            Put(s, 2, "mastery");
            for (int i = 0; i < (int)C("MASTERY_WINS"); i++) Fight(s, true);
            Assert.That(s.Slots[0].MasteryWins, Is.EqualTo(0));
            Assert.That(s.Mastered ?? new List<string>(), Does.Not.Contain(Capstone("living_fortress")));
        }

        // ------------------------------------------------------------------ limit_break

        [Test]
        public void LimitBreak_RaisesItsOwnStatsCap_OnlyWithItsCapstone()
        {
            var s = Drake();
            var ts = Put(s, 2, "limit_break", 7);
            Assert.That(TrainingRun.StatCap(Db, s, StatIds.Str), Is.EqualTo(K.StatMax), "capstone not bought");
            ts.Bought = 8;
            Assert.That(TrainingRun.StatCap(Db, s, StatIds.Str), Is.EqualTo(C("LIMIT_BREAK_MAX")), "content own stat: its first filler's (STR)");
            Assert.That(StatIds.Trained.Where(id => id != StatIds.Str).Select(id => TrainingRun.StatCap(Db, s, id)), Is.All.EqualTo(K.StatMax));

            s.Stats.Str = K.StatMax - 1;
            for (int i = 0; i < 20 && s.Stats.Str <= K.StatMax; i++) Train(s, StatIds.Str);
            Assert.That(s.Stats.Str, Is.GreaterThan(K.StatMax), "drills pass STAT_MAX");
        }

        [Test]
        public void LimitBreak_RollsItsOwnStat_AndItsFillerFollows()
        {
            var t = Db.Trees["limit_break"];
            var seen = new HashSet<string>();
            var r = new RngStream("test", 9);
            for (int k = 0; k < 200; k++)
            {
                var roll = TrainingRun.RollTree(Db, r, t);
                seen.Add(roll.Stat);
                var nodes = TrainingRun.Nodes(Db, t.Id, roll);
                Assert.That(nodes.Where(n => TrainingRun.IsFiller(n.Kind)).Select(n => n.Stat), Is.All.EqualTo(roll.Stat));
            }
            Assert.That(seen, Is.EquivalentTo(StatIds.Trained), "every trained stat can be rolled");

            var s = Drake();
            TreeRoll spd = null;
            for (ulong seed = 1; spd == null || spd.Stat != StatIds.Spd; seed++) spd = TrainingRun.RollTree(Db, new RngStream("x", seed), t);
            Put(s, 2, "limit_break", 8, spd);
            Assert.That(TrainingRun.StatCap(Db, s, StatIds.Spd), Is.EqualTo(C("LIMIT_BREAK_MAX")));
            Assert.That(TrainingRun.StatCap(Db, s, StatIds.Str), Is.EqualTo(K.StatMax));
        }

        [Test]
        public void LimitBreak_OtherTreesDrawNoExtraNumber()
        {
            var a = new RngStream("t", 5);
            var b = new RngStream("t", 5);
            TrainingRun.RollTree(Db, a, Db.Trees["mimic_core"]);
            TrainingRun.RollTree(Db, b, Db.Trees["limit_break"]);
            Assert.That(b.Counter, Is.EqualTo(a.Counter + 1), "one draw more, for Limit Break's own stat only");
        }

        [Test]
        public void LimitBreak_FlatNodesPassTheCap_AndLeavingBringsTheStatBackDown()
        {
            var s = Drake();
            Put(s, 2, "limit_break");
            s.Slots[3] = new TreeSlot { TreeId = "brute_force" };   // a STR stat tree: node 1 is a flat STR node
            s.Stats.Str = K.StatMax;
            while (Db.Trees["brute_force"].Nodes[s.Slots[3].Bought].Kind != NodeKind.Stat || Db.Trees["brute_force"].Nodes[s.Slots[3].Bought].Stat != StatIds.Str)
                TrainingRun.SpendPoint(Db, s, 3);
            var res = TrainingRun.SpendPoint(Db, s, 3);
            Assert.That(res.StatGain, Is.GreaterThan(0), "the flat node adds past STAT_MAX");
            Assert.That(s.Stats.Str, Is.GreaterThan(K.StatMax));

            s.Offers.Add(new TreeOffer { Source = OfferSource.Book, Trees = { "fortune" } });
            TrainingRun.TakeTree(Db, s, 0, 2);   // Limit Break is overwritten
            Assert.That(s.Stats.Str, Is.EqualTo(K.StatMax), "back down to the cap");
            Assert.That(s.Stats.Str, Is.LessThanOrEqualTo(TrainingRun.StatCap(Db, s, StatIds.Str)));
        }

        // ------------------------------------------------------------------ state, hash, clone

        [Test]
        public void Hash_AddsLaneFStateOnlyWhenTheRunHasIt()
        {
            var s = Drake();
            Put(s, 2, "mimic_core");
            string plain = TrainingRun.StateHash(s);
            var t = s.Clone();
            t.Slots[1].MasteryWins = 1;
            Assert.That(TrainingRun.StateHash(t), Is.Not.EqualTo(plain));
            t = s.Clone();
            t.Slots[1].KeepsakeDrills = new int[8];
            Assert.That(TrainingRun.StateHash(t), Is.Not.EqualTo(plain));
            t = s.Clone();
            t.Mastered = new List<string> { Capstone("pyromancy") };
            Assert.That(TrainingRun.StateHash(t), Is.Not.EqualTo(plain));
            Assert.That(TrainingRun.StateHash(s.Clone()), Is.EqualTo(plain), "no lane F state: the hash is as before");
        }

        [Test]
        public void Clone_CopiesLaneFState()
        {
            var s = Drake();
            Put(s, 2, "heirloom").KeepsakeDrills = new int[8];
            s.Slots[2].MasteryWins = 1;
            s.Mastered = new List<string> { Capstone("pyromancy") };
            var c = s.Clone();
            c.Slots[2].KeepsakeDrills[0] = 5;
            c.Mastered.Add("x");
            Assert.That((s.Slots[2].KeepsakeDrills[0], s.Mastered.Count, c.Slots[2].MasteryWins), Is.EqualTo((0, 1, 1)));
        }
    }

    static class LaneFTestExtensions
    {
        public static T Also<T>(this T x, Action<T> f) { f(x); return x; }
    }
}
