using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Battle;
using IdleRng.Sim.Content;
using IdleRng.Sim.Events;
using IdleRng.Sim.State;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>The 4v1 ATB boss battle (specs/11 section 8).</summary>
    public class BattleTests
    {
        static ContentDb Db => TestContent.Db;
        static string FirstOf(Rarity tier, string role) => Db.HeroList.First(h => h.Tier == tier && h.Role == role).Id;

        /// <summary>Run to the end of a boss fight on <paramref name="stage"/> with the sim's current party; returns the events.</summary>
        static List<SimEvent> FightBoss(Simulation sim, EventOutbox box, int stage)
        {
            sim.Debug.SetStage(stage);
            sim.State.Stage.KillsInStage = Db.Constants.KillsPerStage - 1;
            sim.State.Stage.Boss = BossPhase.Pending;
            box.Drain();
            var all = new List<SimEvent>();
            for (int i = 0; i < 2000 && !all.Any(e => e is BossCleared || e is BossFailed); i++) { sim.Tick(); all.AddRange(box.Drain()); }
            return all;
        }

        [Test]
        public void ElementWheel_StrongWeakNeutral()
        {
            Assert.That(Db.ElementMult("fire", "nature"), Is.EqualTo(Db.Constants.ElementStrongMult));
            Assert.That(Db.ElementMult("nature", "fire"), Is.EqualTo(Db.Constants.ElementWeakMult));
            Assert.That(Db.ElementMult("light", "dark"), Is.EqualTo(Db.Constants.ElementStrongMult));
            Assert.That(Db.ElementMult("dark", "light"), Is.EqualTo(Db.Constants.ElementStrongMult));
            Assert.That(Db.ElementMult("fire", "light"), Is.EqualTo(1.0));
            Assert.That(Db.BossElement(10), Is.Not.EqualTo(Db.BossElement(20)), "boss element rotates every 10 stages");
        }

        [Test]
        public void SpdSetsActionRate_AndDefMitigatesWithK()
        {
            var party = new BattleUnit[4];
            party[0] = BattleEngine.CreateCompanion(Db, Db.StarterHeroId, 1, 0, 1.0);
            var boss = BattleEngine.CreateBoss(Db, 30);
            boss.Atk = 0; boss.Spd = 1e-9;   // a harmless, frozen boss
            boss.Hp = boss.MaxHp = 1e12;
            var b = BattleEngine.Start(Db, party, boss, 30);
            var actions = new List<BattleActionInfo>();
            for (int t = 0; t < 100; t++) BattleEngine.Step(Db, b, new Rng.RngStream("t", 1), actions.Add);
            // SPD 110 against a 1000 gauge: 100 ticks -> 11 actions
            Assert.That(actions.Count, Is.EqualTo((int)(100 * party[0].Spd / Db.Constants.BattleGauge)));
            double k = b.DefK;
            Assert.That(k, Is.EqualTo(Db.Constants.DefKMult * Db.EnemyHp(30)).Within(1e-9));
        }

        [Test]
        public void StrongParty_ClearsTheBoss_AndAdvances()
        {
            var box = new EventOutbox(EventKinds.All);
            var sim = TestContent.NewSim(sink: box);
            sim.Debug.SetStatLevel(3000);
            var ev = FightBoss(sim, box, 60);
            Assert.That(ev.OfType<BossCleared>().Single().Stage, Is.EqualTo(60));
            Assert.That(sim.State.Stage.Stage, Is.EqualTo(61));
            Assert.That(sim.State.Combat.Battle, Is.Null);
            Assert.That(ev.OfType<BattleAction>().Any(a => a.Actor == Db.StarterHeroId && a.Target == BattleEngine.BossTarget), Is.True);
            sim.Validate();
        }

        [Test]
        public void WeakParty_IsWiped_AndWalls()
        {
            var box = new EventOutbox(EventKinds.All);
            var sim = TestContent.NewSim(sink: box);
            var ev = FightBoss(sim, box, 60);   // the starter alone at level 1 against a stage-60 boss
            var fail = ev.OfType<BossFailed>().Single();
            Assert.That(fail.Wiped, Is.True);
            Assert.That(sim.State.Stage.Boss, Is.EqualTo(BossPhase.Walled));
            Assert.That(sim.Query.BossClearable, Is.False);
            sim.Validate();
        }

        [Test]
        public void TurnLimit_FailsWithoutWipe()
        {
            var box = new EventOutbox(EventKinds.All);
            var sim = TestContent.NewSim(sink: box);
            sim.Debug.SetStage(60);
            sim.State.Stage.KillsInStage = Db.Constants.KillsPerStage - 1;
            sim.State.Stage.Boss = BossPhase.Pending;
            while (sim.State.Combat.Battle == null) sim.Tick();
            var boss = sim.State.Combat.Battle.Boss;
            boss.Atk = 0; boss.Hp = boss.MaxHp = 1e15;   // cannot hurt, cannot die
            box.Drain();
            sim.Advance(Db.BossRuleFor(60).TimeTicks + 5);
            var fail = box.Drain().OfType<BossFailed>().Single();
            Assert.That(fail.Wiped, Is.False);
        }

        [Test]
        public void Taunt_RedirectsBossAttacks_AndHealerHeals()
        {
            string tank = FirstOf(Rarity.C, "tank"), healer = FirstOf(Rarity.C, "healer");
            var party = new BattleUnit[4];
            party[0] = BattleEngine.CreateCompanion(Db, Db.StarterHeroId, 1, 0, 1.0);
            party[1] = BattleEngine.CreateCompanion(Db, tank, 1, 0, 1.0);
            party[2] = BattleEngine.CreateCompanion(Db, healer, 1, 0, 1.0);
            var boss = BattleEngine.CreateBoss(Db, 10);
            boss.Hp = boss.MaxHp = 1e12;   // durable: the fight lasts long enough for the AI to react
            var b = BattleEngine.Start(Db, party, boss, 10);
            // each single hit takes ~25% of the tank's HP after DEF mitigation
            boss.Atk = 0.25 * party[1].MaxHp * (b.DefK + party[1].Def) / b.DefK;
            var acts = new List<BattleActionInfo>();
            var rng = new Rng.RngStream("t", 9);
            for (int t = 0; t < 120 && BattleEngine.Outcome(b) == BattleOutcome.Ongoing; t++) BattleEngine.Step(Db, b, rng, acts.Add);

            int tauntAt = acts.FindIndex(a => a.Actor == tank && a.Skill != null && a.Skill.Kind == SkillKind.Taunt);
            Assert.That(tauntAt, Is.GreaterThanOrEqualTo(0), "the tank taunts");
            var next = acts.Skip(tauntAt + 1).First(a => a.Actor == null && !a.Aoe);
            Assert.That(next.Target, Is.EqualTo(tank), "the boss's next single-target hit goes to the taunting tank");
            Assert.That(acts.Any(a => a.Actor == healer && a.Skill != null && a.Skill.Kind == SkillKind.Heal && a.Amount > 0),
                        Is.True, "the healer heals once an ally drops below AI_HEAL_BELOW");
        }

        [Test]
        public void Passives_ApplyAtBattleStart()
        {
            string leg = Db.HeroList.First(h => h.Tier == Rarity.L && h.Role == "striker").Id;
            var party = new BattleUnit[4];
            party[0] = BattleEngine.CreateCompanion(Db, leg, 1, 0, 1.0);
            double baseCrit = party[0].CritRate;
            var b = BattleEngine.Start(Db, party, BattleEngine.CreateBoss(Db, 10), 10);
            var passive = Db.Kits[leg].Single(s => s.Kind == SkillKind.Passive);
            Assert.That(BattleEngine.Eff(b.Party[0], passive.Stat), Is.EqualTo(baseCrit + passive.Power).Within(1e-12));
        }

        [Test]
        public void Battle_IsDeterministic_AndSurvivesCloneMidFight()
        {
            var a = TestContent.NewSim(seed: 3);
            a.Debug.GrantHeroCopies(FirstOf(Rarity.E, "caster"), 3);
            a.Debug.SetStatLevel(500);
            a.Debug.SetStage(40);
            a.State.Stage.KillsInStage = Db.Constants.KillsPerStage - 1;
            a.State.Stage.Boss = BossPhase.Pending;
            while (a.State.Combat.Battle == null) a.Tick();
            a.Advance(37);   // mid-fight
            var b = new Simulation(Db, a.State.DeepClone());
            Assert.That(b.StateHash(), Is.EqualTo(a.StateHash()));
            a.Advance(400); b.Advance(400);
            Assert.That(b.StateHash(), Is.EqualTo(a.StateHash()));
            a.Validate();
        }

        [Test]
        public void BossClearable_DoesNotConsumeBattleDraws()
        {
            var sim = TestContent.NewSim();
            sim.Debug.SetStatLevel(500);
            sim.Debug.SetStage(40);
            ulong before = sim.State.Rng.Streams[Rng.StreamNames.Battle].Counter;
            string hash = sim.StateHash();
            bool _ = sim.Query.BossClearable;
            Assert.That(sim.State.Rng.Streams[Rng.StreamNames.Battle].Counter, Is.EqualTo(before));
            Assert.That(sim.StateHash(), Is.EqualTo(hash));
        }
    }
}
