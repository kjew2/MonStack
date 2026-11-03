using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Tower;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>The Dungeon sim and its content (specs/21 sections 2, 4, 5 and 7).</summary>
    public class TowerTests
    {
        static ContentDb Db => TestContent.Db;
        static Constants K => Db.Constants;

        static Fighter Monster(string species, ulong seed)
        {
            var s = TrainingRun.Start(Db, species, seed);
            while (s.Phase != RunPhase.Finished)
            {
                while (s.Offers.Count > 0) TrainingRun.DiscardOffer(Db, s);
                if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s);
                else if (s.Energy < 40) TrainingRun.Rest(Db, s);
                else TrainingRun.Drill(Db, s, StatIds.Str);
            }
            return TrainingRun.MonsterFighter(Db, s);
        }

        static Fighter Strong(string id = "monster") => new Fighter
        {
            Id = id,
            Stats = new MonsterStats { Hp = 5000, Str = 5000, Int = 0, Wis = 3000, Dex = 500, Def = 3000, Spd = 900, Luk = 100 },
        };

        [Test]
        public void Content_LoadsBossesPhasesAndDrops()
        {
            Assert.That(Db.TowerRegulars, Is.Not.Empty);
            Assert.That(Db.TowerMarquees, Is.Not.Empty);
            Assert.That(Db.TowerMarquees.All(b => b.Phases.Count > 0), "placeholder marquee bosses all have phases");
            foreach (var b in Db.TowerBosses.Values)
            {
                double prev = 1;
                foreach (var p in b.Phases) { Assert.That(p.EnterHp, Is.LessThan(prev)); prev = p.EnterHp; }
            }
            Assert.That(Db.BookDrops.Any(d => d.MinFloor == 1));
            Assert.That(FightRules.FromContent(Db, tower: true).Tower.Valid);
            Assert.That(FightRules.FromContent(Db).Tower, Is.Null, "plain fights stay plain");
        }

        [Test]
        public void Lineup_IsTheSameForADay_DiffersAcrossDays_AndPutsMarqueesOnMilestones()
        {
            var a = TowerSim.Lineup(Db, 100);
            var b = TowerSim.Lineup(Db, 100);
            Assert.That(a.Select(p => p.BossId), Is.EqualTo(b.Select(p => p.BossId)));
            Assert.That(a.Count, Is.EqualTo(K.TowerFloors));
            var differs = Enumerable.Range(101, 10).Any(d => !TowerSim.Lineup(Db, d).Select(p => p.BossId).SequenceEqual(a.Select(p => p.BossId)));
            Assert.That(differs, "other days bring other lineups");
            foreach (var p in a)
            {
                bool m = p.Floor % K.TowerMilestoneEvery == 0;
                Assert.That(p.Marquee, Is.EqualTo(m));
                Assert.That(Db.TowerBosses[p.BossId].Role, Is.EqualTo(m ? TowerRole.Marquee : TowerRole.Regular));
            }
            Assert.That(a[1].Level, Is.EqualTo(K.TowerBase * K.TowerGrowth).Within(1e-9));
            Assert.Throws<ArgumentOutOfRangeException>(() => TowerSim.Lineup(Db, -1));
        }

        [Test]
        public void Lineup_UsesTheWholeRegularPoolBeforeRepeating()
        {
            var regulars = TowerSim.Lineup(Db, 7).Where(p => !p.Marquee).Select(p => p.BossId).ToList();
            int n = Db.TowerRegulars.Count;
            Assert.That(regulars.Take(n).Distinct().Count(), Is.EqualTo(Math.Min(n, regulars.Count)));
        }

        [Test]
        public void Boss_ScalesWeightsByLevel_AndOnlyBossesGetTheHpMultiplier()
        {
            var def = Db.TowerMarquees[0];
            var f = TowerSim.Boss(Db, def.Id, 100);
            Assert.That(f.Stats.Str, Is.EqualTo(def.Weights.Str * 100).Within(1e-9));
            Assert.That(f.Stats.Hp, Is.EqualTo(def.Weights.Hp * 100 * K.TowerBossHpMult).Within(1e-9));
            Assert.That(f.Phases.Count, Is.EqualTo(def.Phases.Count));
            var split = f.Phases.FirstOrDefault(p => p.Kind == PhaseKinds.Split);
            if (split != null)
            {
                var addDef = Db.TowerBosses[def.Phases.First(p => p.Kind == PhaseKinds.Split).Adds];
                Assert.That(split.Add.Stats.Hp, Is.EqualTo(addDef.Weights.Hp * 100).Within(1e-9), "adds skip the boss HP multiplier");
            }
        }

        [Test]
        public void Run_IsDeterministic_AndReplays()
        {
            var team = new[] { Monster("drake", 4), Monster("slime", 5) };
            var a = TowerSim.Run(Db, team, 20391);
            var b = TowerSim.Run(Db, team, 20391);
            Assert.That(a.Hash, Is.EqualTo(b.Hash));
            Assert.That(a.FloorReached, Is.InRange(1, K.TowerFloors + 1));
            Assert.That(a.Floors.Count, Is.EqualTo(a.Cleared ? K.TowerFloors : a.FloorReached));
            Assert.That(a.Floors.Take(a.Floors.Count - (a.Cleared ? 0 : 1)).All(f => f.Won));
            Assert.That(TowerSim.Run(Db, team, 20392).Hash, Is.Not.EqualTo(a.Hash), "another day, another climb");
        }

        [Test]
        public void Run_SlotOrderMatters_AndDuplicateMonsterIdsAreFine()
        {
            var x = Monster("drake", 4);
            var y = Monster("slime", 5);
            Assert.That(x.Id, Is.EqualTo(y.Id), "every vaulted monster shares one fighter id");
            var ab = TowerSim.Run(Db, new[] { x, y }, 9);
            var ba = TowerSim.Run(Db, new[] { y, x }, 9);
            Assert.That(ab.Hash, Is.Not.EqualTo(ba.Hash));
        }

        [Test]
        public void Run_RejectsBadTeams()
        {
            Assert.Throws<ArgumentException>(() => TowerSim.Run(Db, new Fighter[0], 1));
            Assert.Throws<ArgumentException>(() => TowerSim.Run(Db, Enumerable.Repeat(Strong(), K.MonSideSize + 1).ToArray(), 1));
            Assert.Throws<ArgumentNullException>(() => TowerSim.Run(Db, null, 1));
        }

        [Test]
        public void Run_AStrongTeamClearsTheTower_SeeingPhasesOnMarqueeFloors()
        {
            var r = TowerSim.Run(Db, new[] { Strong("a"), Strong("b"), Strong("c"), Strong("d") }, 3);
            Assert.That(r.Cleared, Is.True);
            Assert.That(r.FloorReached, Is.EqualTo(K.TowerFloors + 1));
            Assert.That(r.FloorsCleared, Is.EqualTo(K.TowerFloors));
            Assert.That(r.Floors.Where(f => f.Marquee).All(f => f.Phases.Count > 0), "every marquee boss shifts phase before it dies");
        }

        [Test]
        public void Run_ObserversSeeEachFloorAndEveryAction()
        {
            var starts = new List<int>();
            int actions = 0;
            var r = TowerSim.Run(Db, new[] { Monster("drake", 4) }, 11, a => actions++, (plan, state) =>
            {
                starts.Add(plan.Floor);
                Assert.That(state.Tower, Is.True);
            });
            Assert.That(starts, Is.EqualTo(r.Floors.Select(f => f.Floor)));
            Assert.That(actions, Is.GreaterThan(0));
            Assert.That(TowerSim.Run(Db, new[] { Monster("drake", 4) }, 11).Hash, Is.EqualTo(r.Hash), "watching changes nothing");
        }

        [Test]
        public void Run_KeepsEachFloorsTeamTallies_ForTheDamageChart()
        {
            var r = TowerSim.Run(Db, new[] { Monster("drake", 4), Monster("drake", 5) }, 11);
            foreach (var f in r.Floors)
            {
                Assert.That(f.Team.Count, Is.EqualTo(2), "one entry per team slot, adds excluded");
                Assert.That(f.Team.All(t => t.Dealt >= 0 && t.Taken >= 0 && t.Healed >= 0), Is.True);
                if (f.Won) Assert.That(f.Team.Sum(t => t.Dealt), Is.GreaterThan(0), "a won floor took damage to win");
            }
        }

        [Test]
        public void BookDrop_IsFixedByDayAndFloor_AndOnlyDropsEligibleBooks()
        {
            // enough days for about 400 expected drops, whatever the (uber rare) rate is
            double perDay = Enumerable.Range(1, K.TowerFloors).Sum(f => K.TowerBookChance + K.TowerBookChancePerFloor * (f - 1));
            int days = (int)Math.Ceiling(400 / perDay);
            int hits = 0;
            for (int day = 0; day < days; day++)
                for (int floor = 1; floor <= K.TowerFloors; floor++)
                {
                    var book = TowerSim.BookDrop(Db, day, floor);
                    Assert.That(TowerSim.BookDrop(Db, day, floor), Is.EqualTo(book));
                    if (book == null) continue;
                    hits++;
                    var row = Db.BookDrops.Single(b => b.BookId == book);
                    Assert.That(row.MinFloor, Is.LessThanOrEqualTo(floor), "deeper books only drop deep enough");
                }
            double expected = perDay * days;
            Assert.That(hits, Is.InRange(expected * 0.8, expected * 1.2), "the drop rate follows the constants");
        }

        [Test]
        public void BookDrop_RollsAfreshForEachAttempt()
        {
            Assert.That(TowerSim.BookDrop(Db, 9, 3), Is.EqualTo(TowerSim.BookDrop(Db, 9, 3, 0)), "attempt 0 is the default");
            // on the deepest floor, over enough days that attempts 0 and 1 drop about 100 books each, their drops differ
            int floor = K.TowerFloors;
            double chance = K.TowerBookChance + K.TowerBookChancePerFloor * (floor - 1);
            int days = (int)Math.Ceiling(100 / chance);
            int first = 0, second = 0, both = 0;
            for (int day = 0; day < days; day++)
            {
                bool a = TowerSim.BookDrop(Db, day, floor, 0) != null, b = TowerSim.BookDrop(Db, day, floor, 1) != null;
                Assert.That(TowerSim.BookDrop(Db, day, floor, 1) != null, Is.EqualTo(b), "fixed by (day, attempt, floor)");
                if (a) first++;
                if (b) second++;
                if (a && b) both++;
            }
            Assert.That(second, Is.InRange(70, 130), "a later attempt drops at the same rate");
            Assert.That(both, Is.LessThan(Math.Min(first, second) / 2), "a later attempt is not the first attempt's roll again");
            Assert.Throws<ArgumentOutOfRangeException>(() => TowerSim.BookDrop(Db, 0, 1, -1));
        }

        [Test]
        public void BossName_ReadsTheString()
        {
            var b = Db.TowerMarquees[0];
            Assert.That(TowerSim.BossName(Db, b.Id), Is.EqualTo(Db.Text(b.NameKey)));
            Assert.That(TowerSim.BossName(Db, "nope"), Is.EqualTo("nope"));
        }
    }
}
