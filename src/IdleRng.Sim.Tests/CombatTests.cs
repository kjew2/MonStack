using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Events;
using IdleRng.Sim.State;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    public class CombatTests
    {
        [Test]
        public void FirstSpawnAndHit_Timing()
        {
            var box = new EventOutbox(EventKinds.All);
            var sim = TestContent.NewSim(sink: box);
            sim.Advance(20);
            var ev = box.Drain();
            Assert.That(ev.OfType<EnemySpawned>().First().Tick, Is.EqualTo(5));
            Assert.That(ev.OfType<HitLanded>().First().Tick, Is.EqualTo(15));
        }

        [Test]
        public void TenKills_AdvanceStage_AndPayGold()
        {
            var box = new EventOutbox(EventKinds.All);
            var sim = TestContent.NewSim(sink: box);
            var db = TestContent.Db;
            // Stage 1: HP 21.4, hit 10 -> 3 hits -> 35 ticks per kill.
            Assert.That(sim.Query.HitsToKill(db.EnemyHp(1)), Is.EqualTo(3));
            sim.Advance(350);
            var ev = box.Drain();
            Assert.That(ev.OfType<StageAdvanced>().Single(), Is.EqualTo(new StageAdvanced(350, 1, 2, false)));
            Assert.That(sim.State.Currencies.Gold, Is.EqualTo(10 * db.GoldPerKill(1)).Within(1e-9));
            sim.Validate();
        }

        [Test]
        public void Boss_FailsThenWalled_ThenClearsAfterUpgrade()
        {
            var box = new EventOutbox(EventKinds.All);
            var sim = TestContent.NewSim(sink: box);
            var db = TestContent.Db;
            sim.Debug.SetStage(10);
            // nine normal kills, then the 10th enemy is the boss
            while (!box.Drain().OfType<EnemySpawned>().Any(e => e.IsBoss)) sim.Tick();
            Assert.That(sim.State.Combat.EnemyMaxHp, Is.EqualTo(10 * db.EnemyHp(10)));
            Assert.That(sim.State.Combat.BossTimer, Is.EqualTo(200));
            Assert.That(sim.State.Daily.Progress[2], Is.EqualTo(1));   // boss attempt counts toward the quest

            sim.Advance(200);
            Assert.That(box.Drain().OfType<BossFailed>().Single().Stage, Is.EqualTo(10));
            Assert.That(sim.State.Stage.Boss, Is.EqualTo(BossPhase.Walled));
            Assert.That(sim.State.Lifetime.FirstWallStage, Is.EqualTo(10));

            double gold = sim.State.Currencies.Gold;
            sim.Advance(500);
            Assert.That(sim.State.Stage.KillsInStage, Is.EqualTo(9));
            Assert.That(sim.State.Currencies.Gold, Is.GreaterThan(gold));

            sim.Debug.SetStatLevel(200);
            sim.Debug.SetStage(10);
            sim.State.Stage.KillsInStage = 9;
            sim.State.Stage.Boss = BossPhase.Walled;
            sim.State.Stage.BossFailsOnStage = 1;
            Assert.That(sim.AttemptBoss().Ok, Is.True);
            Assert.That(sim.AttemptBoss().Reason, Is.EqualTo(Reject.BossAlreadyPending));
            box.Drain();
            sim.Advance(300);
            var ev = box.Drain();
            Assert.That(ev.OfType<BossCleared>().Single().Stage, Is.EqualTo(10));
            Assert.That(ev.OfType<StageAdvanced>().First(), Is.EqualTo(new StageAdvanced(ev.OfType<StageAdvanced>().First().Tick, 10, 11, true)));
            Assert.That(ev.OfType<FirstClear>().Single().Gems, Is.EqualTo(10));
            Assert.That(sim.AttemptBoss().Reason, Is.EqualTo(Reject.NoBossToAttempt));
        }

        [Test]
        public void ClassAdvance_OnReachingStage50()
        {
            var sim = TestContent.NewSim();
            sim.Debug.SetStatLevel(100000);
            sim.Debug.SetStage(49);
            double dps = sim.Query.Dps;
            sim.Advance(200);
            Assert.That(sim.State.Stage.Stage, Is.GreaterThanOrEqualTo(50));
            Assert.That(sim.State.Progression.ClassAdvances, Is.EqualTo(1));
            Assert.That(sim.Query.Dps, Is.EqualTo(dps * 1.5).Within(1e-9).Percent);
        }

        [Test]
        public void StatLevels_And_Enhance()
        {
            var sim = TestContent.NewSim();
            var db = TestContent.Db;
            Assert.That(sim.BuyStatLevel().Reason, Is.EqualTo(Reject.NotEnoughGold));
            sim.Debug.GrantGold(db.StatCost(0) + db.StatCost(1) + 1);
            Assert.That(sim.BuyStatLevelsMax(), Is.EqualTo(2));
            Assert.That(sim.Query.StatMult, Is.EqualTo(1.1).Within(1e-12));

            Assert.That(sim.Enhance(0).Reason, Is.EqualTo(Reject.EmptySlot));
            sim.State.Gear.Slots[0] = new GearPiece(0, IdleRng.Sim.Content.Rarity.R, 30, 0);
            sim.Debug.GrantGold(1e6);
            var r = sim.Enhance(0);
            Assert.That(r.Ok && r.Success && r.Level == 1, Is.True);   // ENH_SUCCESS(0) = 1
            Assert.That(sim.Query.GearMult, Is.EqualTo(1 + db.GearSlotPct(30) * 1.2 * 1.05).Within(1e-12));
            Assert.That(sim.State.Lifetime.EnhanceAttempts, Is.EqualTo(1));
        }
    }
}
