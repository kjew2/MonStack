using System;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Gauntlet;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>The end-of-run gauntlet (specs/19): escalating floors, bosses, where the wheel stops, Legacy Titles.</summary>
    public class GauntletTests
    {
        static ContentDb Db => TestContent.Db;
        static Constants K => Db.Constants;

        static Fighter Mon(double all) => new Fighter
        {
            Id = "monster", Element = "water",
            Stats = new MonsterStats { Hp = all, Str = all, Int = all, Wis = all, Dex = all, Def = all, Spd = all, Luk = all },
        };

        [Test]
        public void Levels_EscalateByRepeatedMultiplication_BossFloorsHigher()
        {
            var l = GauntletSim.Levels(Db);
            Assert.That(l.Length, Is.EqualTo(K.GauntletFloors));
            Assert.That(l[0], Is.EqualTo(K.GauntletBase));
            Assert.That(l[1], Is.EqualTo(K.GauntletBase * K.GauntletGrowth).Within(1e-9));
            int boss = K.GauntletBossEvery;
            Assert.That(GauntletSim.IsBoss(Db, boss), Is.True);
            Assert.That(GauntletSim.IsBoss(Db, boss - 1), Is.False);
            Assert.That(l[boss - 1], Is.GreaterThan(l[boss]), "a boss floor out-levels the floor after it");
            Assert.That(GauntletSim.EnemyDef(Db, boss).Boss, Is.True);
            Assert.That(GauntletSim.EnemyDef(Db, 1).Boss, Is.False);
            Assert.That(GauntletSim.EnemyDef(Db, 2 * boss).Id, Is.Not.EqualTo(GauntletSim.EnemyDef(Db, boss).Id), "bosses rotate");
        }

        [Test]
        public void Run_StopsAtTheFirstLoss_AndIsDeterministic()
        {
            var r = GauntletSim.Run(Db, Mon(150), 9);
            Assert.That(r.Cleared, Is.False);
            Assert.That(r.Floors.Count, Is.EqualTo(r.FloorReached));
            Assert.That(r.Floors.Take(r.FloorReached - 1).All(f => f.Won), Is.True);
            Assert.That(r.Floors.Last().Won, Is.False);
            Assert.That(r.KillerId, Is.EqualTo(r.Floors.Last().EnemyId));
            Assert.That(r.TitleKey, Is.EqualTo(GauntletSim.Title(Db, r.FloorReached).TitleKey));
            Assert.That(GauntletSim.Run(Db, Mon(150), 9).Hash, Is.EqualTo(r.Hash));
        }

        [Test]
        public void StrongerMonsters_ClimbHigher_AndAHugeOneClearsTheTower()
        {
            int weak = GauntletSim.Run(Db, Mon(100), 1).FloorReached;
            int strong = GauntletSim.Run(Db, Mon(600), 1).FloorReached;
            Assert.That(strong, Is.GreaterThan(weak));
            var god = GauntletSim.Run(Db, Mon(1e9), 1);
            Assert.That(god.Cleared, Is.True);
            Assert.That(god.KillerId, Is.Null);
            Assert.That(god.FloorReached, Is.EqualTo(K.GauntletFloors + 1));
            Assert.That(god.Floors.Count, Is.EqualTo(K.GauntletFloors));
            Assert.That(god.TitleKey, Is.EqualTo(Db.LegacyTitles.Last().TitleKey));
        }

        [Test]
        public void Title_PicksTheHighestBandAtOrBelowTheFloor()
        {
            var t = Db.LegacyTitles;
            Assert.That(GauntletSim.Title(Db, 1), Is.SameAs(t[0]));
            Assert.That(GauntletSim.Title(Db, t[1].MinFloor - 1), Is.SameAs(t[0]));
            Assert.That(GauntletSim.Title(Db, t[1].MinFloor), Is.SameAs(t[1]));
        }

        [Test]
        public void TitleText_FillsTheKillersName()
        {
            var food = Db.LegacyTitles.First(t => Db.Text(t.TitleKey).Contains("{killer}")).TitleKey;
            var boss = Db.GauntletBosses[0];
            Assert.That(GauntletSim.TitleText(Db, food, boss.Id), Is.EqualTo(Db.Text(food).Replace("{killer}", Db.Text(boss.NameKey))));
            Assert.That(GauntletSim.TitleText(Db, food, Db.GauntletEnemies[0].Id), Does.StartWith(Db.Text(Db.GauntletEnemies[0].NameKey)));
            Assert.That(GauntletSim.TitleText(Db, food, null), Does.Not.Contain("{killer}"));
            var top = Db.LegacyTitles.Last().TitleKey;
            Assert.That(GauntletSim.TitleText(Db, top, null), Is.EqualTo(Db.Text(top)));
            Assert.That(GauntletSim.EnemyName(Db, boss.Id), Is.EqualTo(Db.Text(boss.NameKey)));
            Assert.That(GauntletSim.EnemyName(Db, "no_such_enemy"), Is.EqualTo("no_such_enemy"));
        }

        [Test]
        public void ARunMonster_CanBeSentUpTheTower()
        {
            var s = TrainingRun.Start(Db, "drake", 4);
            while (s.Phase != RunPhase.Finished)
            {
                while (s.Offers.Count > 0) TrainingRun.DiscardOffer(Db, s);
                if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s);
                else if (s.Energy < 40) TrainingRun.Rest(Db, s);
                else TrainingRun.Drill(Db, s, StatIds.Str);
            }
            var r = GauntletSim.Run(Db, TrainingRun.MonsterFighter(Db, s), s.Seed);
            Assert.That(r.FloorReached, Is.InRange(1, K.GauntletFloors + 1));
            Assert.Throws<ArgumentNullException>(() => GauntletSim.Run(Db, null, 1));
        }
    }
}
