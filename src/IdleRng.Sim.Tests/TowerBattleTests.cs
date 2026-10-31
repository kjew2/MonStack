using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>The Daily Tower's battle hooks (specs/21 section 3): entropy gauge, boss phases, environmental clock.</summary>
    public class TowerBattleTests
    {
        static TowerRules Tower() => new TowerRules
        {
            EntropyCap = 100, EntropyPerTrigger = 100, EntropyPerEcho = 100, EntropyPerCounter = 100, EntropyDecay = 0,
            RecoilFrac = 0.1, StunGauge = 500, EnrageFrac = 0.25, EnrageTurns = 3, ConvertShield = 0.2, ConvertTurns = 3,
            CorruptStartTick = 300, CorruptEveryTicks = 100, CorruptHealPerLevel = 0.2, CorruptHealFloor = 0.2, CorruptDmgPerLevel = 0.1,
        };

        static FightRules Rules(bool tower = true)
        {
            var r = FightRules.FromContent(TestContent.Db);
            if (tower) r.Tower = Tower();
            return r;
        }

        static Fighter Mon(string id, double hp = 300, double str = 300, double def = 0, double spd = 100, params CombatSkill[] skills) => new Fighter
        {
            Id = id,
            Stats = new MonsterStats { Hp = hp, Str = str, Int = 0, Wis = 0, Dex = 0, Def = def, Spd = spd, Luk = 0 },
            Skills = skills.ToList(),
        };

        static CombatSkill Taunt() => new CombatSkill { Id = "taunt", Kind = SkillKind.Taunt, Target = SkillTarget.Self, Duration = 90 };

        static CombatSkill Buff(string id = "buff") =>
            new CombatSkill { Id = id, Kind = SkillKind.Buff, Target = SkillTarget.Self, Stat = StatIds.Str, Power = 0.01, Duration = 1 };

        sealed class Fixed : IRandomSource
        {
            public double NextDouble() => 0.99;   // never crit, never evade
        }

        static (FightState state, List<FightAction> log) Run(FightRules r, Fighter[] a, Fighter[] b)
        {
            var s = BattleSim.Start(r, a, b);
            var log = new List<FightAction>();
            var rng = new Fixed();
            while (BattleSim.Step(r, s, rng, log.Add) == FightOutcome.Ongoing) { }
            return (s, log);
        }

        static List<string> Events(List<FightAction> log) => log.Where(a => a.Event != null).Select(a => a.Event).ToList();

        // ------------------------------------------------------------------ off by default

        [Test]
        public void PhasesNeedTowerRules()
        {
            var boss = Mon("boss");
            boss.Phases.Add(new BossPhase { EnterHp = 0.5, Kind = PhaseKinds.Enrage, Buff = 0.1 });
            Assert.Throws<ArgumentException>(() => BattleSim.Start(Rules(false), new[] { Mon("a") }, new[] { boss }));
            Assert.DoesNotThrow(() => BattleSim.Start(Rules(true), new[] { Mon("a") }, new[] { boss }));
        }

        [Test]
        public void TowerRules_ChangeNothingUntilTheyMatter_AndOnlyTowerFightsHashTheNewFields()
        {
            var a = new[] { Mon("a") };
            var b = new[] { Mon("b", str: 100) };
            var plain = BattleSim.Resolve(Rules(false), a, b, 3);
            var tower = BattleSim.Resolve(Rules(true), a, b, 3);
            Assert.That(tower.Outcome, Is.EqualTo(plain.Outcome));
            Assert.That(tower.Hash, Is.Not.EqualTo(plain.Hash), "tower fights hash their extra state");
            Assert.That(BattleSim.Resolve(Rules(false), a, b, 3).Hash, Is.EqualTo(plain.Hash));
        }

        [Test]
        public void BadTowerRules_AreRejected()
        {
            var r = Rules();
            r.Tower.EntropyCap = 0;
            Assert.Throws<ArgumentException>(() => BattleSim.Start(r, new[] { Mon("a") }, new[] { Mon("b") }));
        }

        [Test]
        public void BadPhases_AreRejected()
        {
            var up = Mon("boss");
            up.Phases.Add(new BossPhase { EnterHp = 0.3, Kind = PhaseKinds.Enrage });
            up.Phases.Add(new BossPhase { EnterHp = 0.6, Kind = PhaseKinds.Enrage });
            Assert.Throws<ArgumentException>(() => BattleSim.Start(Rules(), new[] { Mon("a") }, new[] { up }), "enter_hp must fall");

            var noAdd = Mon("boss");
            noAdd.Phases.Add(new BossPhase { EnterHp = 0.5, Kind = PhaseKinds.Split, Count = 1 });
            Assert.Throws<ArgumentException>(() => BattleSim.Start(Rules(), new[] { Mon("a") }, new[] { noAdd }), "split needs an add");

            var unknown = Mon("boss");
            unknown.Phases.Add(new BossPhase { EnterHp = 0.5, Kind = "nope" });
            Assert.Throws<ArgumentException>(() => BattleSim.Start(Rules(), new[] { Mon("a") }, new[] { unknown }));
        }

        // ------------------------------------------------------------------ environmental clock

        [Test]
        public void CorruptionLevel_StartsLateAndStepsByTheInterval()
        {
            var r = Rules();
            Assert.That(BattleSim.CorruptionLevel(r, 0), Is.EqualTo(0));
            Assert.That(BattleSim.CorruptionLevel(r, 399), Is.EqualTo(0));
            Assert.That(BattleSim.CorruptionLevel(r, 400), Is.EqualTo(1));
            Assert.That(BattleSim.CorruptionLevel(r, 650), Is.EqualTo(3));
            Assert.That(BattleSim.CorruptionLevel(Rules(false), 650), Is.EqualTo(0), "no tower rules, no corruption");
        }

        [Test]
        public void Corruption_CutsHealingToItsFloor()
        {
            var r = Rules();
            var s = BattleSim.Start(r, new[] { Mon("a") }, new[] { Mon("b") });
            Assert.That(BattleSim.HealMult(r, s), Is.EqualTo(1.0));
            s.Ticks = 500;   // level 2
            Assert.That(BattleSim.HealMult(r, s), Is.EqualTo(0.6).Within(1e-9));
            s.Ticks = 900;   // level 6 would be -0.2: floored
            Assert.That(BattleSim.HealMult(r, s), Is.EqualTo(0.2).Within(1e-9));
            Assert.That(BattleSim.HealMult(Rules(false), s), Is.EqualTo(1.0));
        }

        [Test]
        public void Corruption_AddsDamageForBothSides_AndTheFightRecordsItsPeak()
        {
            double Total(double perLevel, out int peak)
            {
                var r = Rules();
                r.Tower.CorruptStartTick = 0;
                r.Tower.CorruptEveryTicks = 50;
                r.Tower.CorruptDmgPerLevel = perLevel;
                var a = new[] { Mon("a", hp: 999, str: 20, def: 999) };
                var b = new[] { Mon("b", hp: 999, str: 20, def: 999) };
                var (s, _) = Run(r, a, b);
                peak = s.PeakCorruption;
                return s.Units.Sum(u => u.Tally.DamageDealt);
            }
            double calm = Total(0, out _);
            double rising = Total(1.0, out int peak);
            Assert.That(peak, Is.GreaterThan(0));
            Assert.That(rising, Is.GreaterThan(calm), "both sides deal more as the arena corrupts");
        }

        // ------------------------------------------------------------------ entropy

        [Test]
        public void Entropy_VentsRecoilThenStunThenEnrage_WithNoRandomDraw()
        {
            var r = Rules();
            var worker = Mon("a", hp: 999, str: 1, def: 999);
            worker.Triggers.Add(new TriggeredSkill(FightTrigger.OnAction, Buff()));
            var (_, log) = Run(r, new[] { worker }, new[] { Mon("b", hp: 999, str: 1, def: 999) });
            var vents = Events(log).Where(e => e.StartsWith("vent:")).Take(3).ToList();
            Assert.That(vents, Is.EqualTo(new[] { "vent:recoil", "vent:stun", "vent:enrage" }));
        }

        [Test]
        public void NextVent_PredictsEachVentBeforeItHappens()
        {
            // what playback shows as the next vent (BattleSim.NextVent) is the vent that fires, for the rotation and for convert
            foreach (bool convert in new[] { false, true })
            {
                var r = Rules();
                var worker = Mon("a", hp: 999, str: 1, def: 999);
                if (convert) worker.Rules.Add(RuleModifiers.EntropyConvert);
                worker.Triggers.Add(new TriggeredSkill(FightTrigger.OnAction, Buff()));
                var s = BattleSim.Start(r, new[] { worker }, new[] { Mon("b", hp: 999, str: 1, def: 999) });
                string predicted = BattleSim.NextVent(s.Units[0]);
                var seen = new List<(string predicted, string fired)>();
                var rng = new Fixed();
                while (seen.Count < 4 && BattleSim.Step(r, s, rng, a =>
                {
                    if (a.Event != null && a.Event.StartsWith("vent:")) seen.Add((predicted, a.Event.Substring(5)));
                    predicted = BattleSim.NextVent(s.Units[0]);
                }) == FightOutcome.Ongoing) { }
                Assert.That(seen.Count, Is.EqualTo(4));
                foreach (var (p, f) in seen) Assert.That(f, Is.EqualTo(p));
                Assert.That(seen.Select(x => x.fired), Is.EqualTo(convert ? new[] { "convert", "convert", "convert", "convert" } : new[] { "recoil", "stun", "enrage", "recoil" }));
            }
        }

        [Test]
        public void Entropy_DecaysAtTheStartOfTheUnitsAction()
        {
            var r = Rules();
            r.Tower.EntropyPerTrigger = 5;
            r.Tower.EntropyDecay = 5;   // decay first, then the trigger adds 5: it never builds
            var worker = Mon("a", hp: 999, str: 1, def: 999);
            worker.Triggers.Add(new TriggeredSkill(FightTrigger.OnAction, Buff()));
            var s = BattleSim.Start(r, new[] { worker }, new[] { Mon("b", hp: 999, str: 1, def: 999) });
            var rng = new Fixed();
            for (int i = 0; i < 200; i++) BattleSim.Step(r, s, rng);
            Assert.That(s.Units[0].Entropy, Is.EqualTo(5).Within(1e-9));
            Assert.That(s.Units[0].Vents, Is.EqualTo(0));
        }

        [Test]
        public void Entropy_PassivesAtBattleStartDoNotBuildIt()
        {
            var r = Rules();
            var passive = Mon("a", hp: 999, str: 1, def: 999,
                skills: new CombatSkill { Id = "tough", Kind = SkillKind.Passive, Stat = StatIds.Def, Power = 0.1 });
            var s = BattleSim.Start(r, new[] { passive }, new[] { Mon("b") });
            BattleSim.Step(r, s, new Fixed());
            Assert.That(s.Units[0].Entropy, Is.EqualTo(0));
        }

        [Test]
        public void EntropyGain_NegativeEffectLowersGain()
        {
            var r = Rules();
            r.Tower.EntropyPerTrigger = 60;
            var worker = Mon("a", hp: 999, str: 1, def: 999);
            worker.Triggers.Add(new TriggeredSkill(FightTrigger.OnAction, Buff()));
            // the enemy lowers the worker's entropy gain by 100% with a debuff before the worker's second action
            var calmer = Mon("b", hp: 999, str: 1, def: 999, skills: new CombatSkill
            {
                Id = "calm", Kind = SkillKind.Debuff, Target = SkillTarget.Enemy, Stat = StatIds.EntropyGain, Power = 1.0, Duration = 90,
            });
            var s = BattleSim.Start(r, new[] { worker }, new[] { calmer });
            var rng = new Fixed();
            for (int i = 0; i < 200; i++) BattleSim.Step(r, s, rng);
            Assert.That(s.Units[0].Vents, Is.EqualTo(0), "gain is cut to nothing");
            Assert.That(s.Units[0].Entropy, Is.LessThan(100));
        }

        [Test]
        public void EntropyConvert_ShieldsInsteadOfVenting()
        {
            var r = Rules();
            var worker = Mon("a", hp: 999, str: 1, def: 999);
            worker.Rules.Add(RuleModifiers.EntropyConvert);
            worker.Triggers.Add(new TriggeredSkill(FightTrigger.OnAction, Buff()));
            var (s, log) = Run(r, new[] { worker }, new[] { Mon("b", hp: 999, str: 1, def: 999) });
            var events = Events(log);
            Assert.That(events, Does.Contain("vent:convert"));
            Assert.That(events.Where(e => e.StartsWith("vent:") && e != "vent:convert"), Is.Empty);
            var shield = log.First(a => a.Event == "vent:convert").Amount;
            Assert.That(shield, Is.EqualTo(0.2 * 9990).Within(1e-6));
        }

        [Test]
        public void Counters_AndEchoes_BuildEntropy()
        {
            var r = Rules();
            r.Tower.EntropyPerCounter = 40;
            r.Tower.EntropyPerEcho = 30;
            var tank = Mon("a", hp: 999, str: 1, def: 300);
            tank.Rules.Add(RuleModifiers.CounterMitigated);
            var s = BattleSim.Start(r, new[] { tank }, new[] { Mon("b", hp: 999, str: 200, def: 300) });
            BattleSim.Step(r, s, new Fixed());
            for (int i = 0; i < 12; i++) BattleSim.Step(r, s, new Fixed());
            Assert.That(s.Units[0].Entropy + s.Units[0].Vents * 100, Is.GreaterThanOrEqualTo(40), "a counter adds ENTROPY_PER_COUNTER");

            var caster = Mon("c", hp: 999, str: 100, def: 999,
                skills: new CombatSkill { Id = "bolt", Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = StatIds.Str, Power = 0.1 });
            caster.Rules.Add(RuleModifiers.EchoCast);
            var s2 = BattleSim.Start(r, new[] { caster }, new[] { Mon("d", hp: 999, str: 1, def: 999) });
            for (int i = 0; i < 12; i++) BattleSim.Step(r, s2, new Fixed());
            Assert.That(s2.Units[0].Entropy + s2.Units[0].Vents * 100, Is.GreaterThanOrEqualTo(30), "an echo adds ENTROPY_PER_ECHO");
        }

        // ------------------------------------------------------------------ boss phases

        static Fighter Boss(double hp = 500, params BossPhase[] phases)
        {
            var b = Mon("boss", hp: hp, str: 50, def: 0, spd: 100, skills: Taunt());
            b.Phases.AddRange(phases);
            return b;
        }

        [Test]
        public void ShieldCheck_ExpiringUnbroken_HealsAndBuffsTheBoss_BrokenShieldDoesNot()
        {
            // an attacker too weak to break a huge shield: the check fails
            var r = Rules();
            var phase = new BossPhase { EnterHp = 0.99, Kind = PhaseKinds.ShieldCheck, Power = 5.0, Duration = 1, HealFrac = 0.5, Buff = 0.3 };
            var (s, log) = Run(r, new[] { Mon("a", hp: 999, str: 40, def: 999) }, new[] { Boss(500, phase) });
            Assert.That(Events(log), Does.Contain("phase:shield_check"));
            Assert.That(Events(log), Does.Contain("check_failed"));
            Assert.That(s.Units[1].Effects.Any(e => e.Source == "phase_check" && e.Stat == StatIds.DmgDealt));

            // the same phase with a tiny shield is broken at once
            var weak = new BossPhase { EnterHp = 0.99, Kind = PhaseKinds.ShieldCheck, Power = 0.001, Duration = 1, HealFrac = 0.5, Buff = 0.3 };
            var (_, log2) = Run(r, new[] { Mon("a", hp: 999, str: 40, def: 999, spd: 400) }, new[] { Boss(500, weak) });
            Assert.That(Events(log2), Does.Contain("phase:shield_check"));
            Assert.That(Events(log2), Does.Not.Contain("check_failed"));
        }

        [Test]
        public void Split_SummonsAddsUpToTheSideCap_GuardsTheBoss_AndKillingTheBossWins()
        {
            var r = Rules();
            var add = Mon("imp", hp: 5000, str: 1, def: 999);
            var phase = new BossPhase { EnterHp = 0.9, Kind = PhaseKinds.Split, Count = 9, Guard = 0.5, Add = add };
            var (s, log) = Run(r, new[] { Mon("a", hp: 999, str: 200, def: 999) }, new[] { Boss(500, phase) });
            Assert.That(Events(log).Count(e => e == "phase:split"), Is.EqualTo(1), "a phase is entered once");
            Assert.That(s.Units.Count(u => u.Side == 1), Is.EqualTo(r.SideSize), "boss plus adds, up to the side cap");
            Assert.That(s.Units.Where(u => u.IsAdd).Select(u => u.Id), Is.EquivalentTo(new[] { "boss#1", "boss#2", "boss#3" }));
            Assert.That(s.Outcome, Is.EqualTo(FightOutcome.SideAWon));
            Assert.That(s.Units.Any(u => u.IsAdd && u.Alive), "the boss died with adds still up");
        }

        [Test]
        public void Split_GuardCutsDamageWhileAnAddLives()
        {
            var r = Rules();
            var add = Mon("imp", hp: 5000, str: 1, def: 999);
            var guarded = new BossPhase { EnterHp = 0.99, Kind = PhaseKinds.Split, Count = 1, Guard = 0.8, Add = add };
            var open = new BossPhase { EnterHp = 0.99, Kind = PhaseKinds.Split, Count = 1, Guard = 0.0, Add = add };
            var (g, _) = Run(r, new[] { Mon("a", hp: 999, str: 200, def: 999) }, new[] { Boss(500, guarded) });
            var (o, _) = Run(r, new[] { Mon("a", hp: 999, str: 200, def: 999) }, new[] { Boss(500, open) });
            Assert.That(g.Ticks, Is.GreaterThan(o.Ticks), "a guarded boss takes longer to kill");
        }

        [Test]
        public void Channel_UninterruptedStrikesTheTeam_FocusedFireBreaksIt()
        {
            var r = Rules();
            var unbreakable = new BossPhase { EnterHp = 0.99, Kind = PhaseKinds.Channel, Power = 50.0, Duration = 2, Strike = 3.0 };
            var (s, log) = Run(r, new[] { Mon("a", hp: 999, str: 20, def: 999) }, new[] { Boss(5000, unbreakable) });
            var events = Events(log);
            Assert.That(events, Does.Contain("phase:channel"));
            Assert.That(events, Does.Contain("channel_strike"));
            Assert.That(log.First(a => a.Event == "channel_strike").Amount, Is.GreaterThan(0));

            var breakable = new BossPhase { EnterHp = 0.99, Kind = PhaseKinds.Channel, Power = 0.01, Duration = 3, Strike = 3.0 };
            var (_, log2) = Run(r, new[] { Mon("a", hp: 999, str: 300, def: 999) }, new[] { Boss(5000, breakable) });
            Assert.That(Events(log2), Does.Contain("channel_broken"));
            Assert.That(Events(log2), Does.Not.Contain("channel_strike"));
        }

        [Test]
        public void ChannelStrike_CanWipeTheTeamAndEndTheFight()
        {
            var r = Rules();
            var deadly = new BossPhase { EnterHp = 0.99, Kind = PhaseKinds.Channel, Power = 50.0, Duration = 1, Strike = 500.0 };
            var (s, _) = Run(r, new[] { Mon("a", hp: 50, str: 20, def: 0) }, new[] { Boss(5000, deadly) });
            Assert.That(s.Outcome, Is.EqualTo(FightOutcome.SideBWon));
            Assert.That(s.Units[0].Alive, Is.False);
        }

        [Test]
        public void Enrage_GrowsTheBossesDamageBuffEveryTurn()
        {
            var r = Rules();
            var phase = new BossPhase { EnterHp = 0.99, Kind = PhaseKinds.Enrage, Buff = 0.1 };
            var (s, _) = Run(r, new[] { Mon("a", hp: 999, str: 20, def: 999) }, new[] { Boss(5000, phase) });
            var e = s.Units[1].Effects.Single(x => x.Source == "phase_enrage");
            Assert.That(e.Frac, Is.GreaterThan(0.1 * 5));
            Assert.That(e.TicksLeft, Is.EqualTo(-1), "permanent");
        }

        [Test]
        public void AddSkills_AreGainedOnEntry_AndPassivesApplyAtOnce()
        {
            var r = Rules();
            var phase = new BossPhase
            {
                EnterHp = 0.99, Kind = PhaseKinds.Enrage, Buff = 0.0,
                AddSkills = { new CombatSkill { Id = "thick_skin", Kind = SkillKind.Passive, Stat = StatIds.Def, Power = 0.5 } },
            };
            var boss = Boss(500, phase);
            var s = BattleSim.Start(r, new[] { Mon("a", hp: 999, str: 100, def: 999) }, new[] { boss });
            var rng = new Fixed();
            while (BattleSim.Step(r, s, rng) == FightOutcome.Ongoing && s.Units[1].PhaseNext == 0) { }
            var b = s.Units[1];
            Assert.That(b.Source.Skills.Select(x => x.Id), Does.Contain("thick_skin"));
            Assert.That(b.Cooldowns.Length, Is.EqualTo(b.Source.Skills.Count));
            Assert.That(b.Effects.Any(x => x.Stat == StatIds.Def && x.Frac == 0.5));
            Assert.That(boss.Skills.Count, Is.EqualTo(1), "the shared fighter snapshot is not changed");
        }

        // ------------------------------------------------------------------ determinism

        [Test]
        public void TowerFights_AreDeterministic_AndAClonedStateContinuesIdentically()
        {
            var r = Rules();
            var add = Mon("imp", hp: 500, str: 30, def: 100);
            var boss = Boss(1200,
                new BossPhase { EnterHp = 0.8, Kind = PhaseKinds.ShieldCheck, Power = 0.3, Duration = 2, HealFrac = 0.2, Buff = 0.1 },
                new BossPhase { EnterHp = 0.6, Kind = PhaseKinds.Split, Count = 2, Guard = 0.3, Add = add },
                new BossPhase { EnterHp = 0.4, Kind = PhaseKinds.Channel, Power = 0.2, Duration = 2, Strike = 1.0 },
                new BossPhase { EnterHp = 0.2, Kind = PhaseKinds.Enrage, Buff = 0.05 });
            var team = new[] { Mon("a", hp: 600, str: 150, def: 100), Mon("b", hp: 600, str: 120, def: 100) };

            var h1 = BattleSim.Resolve(r, team, new[] { boss }, 11).Hash;
            var h2 = BattleSim.Resolve(r, team, new[] { boss }, 11).Hash;
            Assert.That(h1, Is.EqualTo(h2));
            Assert.That(BattleSim.Resolve(r, team, new[] { boss }, 12).Hash, Is.Not.EqualTo(h1));

            var s = BattleSim.Start(r, team, new[] { boss });
            var fixedRng = new Fixed();
            for (int i = 0; i < 60; i++) BattleSim.Step(r, s, fixedRng);
            var copy = s.Clone();
            Assert.That(BattleSim.StateHash(copy), Is.EqualTo(BattleSim.StateHash(s)));
            while (BattleSim.Step(r, s, fixedRng) == FightOutcome.Ongoing) { }
            while (BattleSim.Step(r, copy, fixedRng) == FightOutcome.Ongoing) { }
            Assert.That(BattleSim.StateHash(copy), Is.EqualTo(BattleSim.StateHash(s)), "a clone plus the same draws continues identically");
        }
    }
}
