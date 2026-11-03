using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>
    /// Idea-pool lane E (#138, specs/16 section 6.2): lone_wolf, arise and last_rite, each on and off, their edge cases, and the
    /// modes (1v1, 4v1, 4v4), plus the mid-fight unit they need (BattleSim.Summon, specs/16 section 6.4).
    /// </summary>
    public class LaneERuleTests
    {
        static FightRules Content => FightRules.FromContent(TestContent.Db);

        /// <summary>The content rules with no avoid or crit chance: hits land and nothing is drawn (with a null random source).</summary>
        static FightRules Plain
        {
            get
            {
                var r = Content;
                r.EvadeBase = 0; r.EvadePerDex = 0; r.CritBase = 0; r.CritPerLuk = 0;
                return r;
            }
        }

        static Fighter Mon(string id, Action<MonsterStats> tweak = null, params string[] rules)
        {
            var st = new MonsterStats { Hp = 300, Str = 300, Int = 0, Wis = 300, Dex = 300, Def = 300, Spd = 100, Luk = 0 };
            tweak?.Invoke(st);
            var f = new Fighter { Id = id, Stats = st };
            f.Rules.AddRange(rules);
            return f;
        }

        /// <summary>A fast heavy hitter that cannot die soon.</summary>
        static Fighter Killer(string id, params string[] rules) => Mon(id, x => { x.Spd = 900; x.Str = 3000; x.Hp = 99999; }, rules);

        /// <summary>A unit that deals no damage and acts rarely.</summary>
        static Fighter Harmless(string id, Action<MonsterStats> tweak = null, params string[] rules) =>
            Mon(id, x => { x.Str = 0; x.Int = 0; x.Spd = 10; tweak?.Invoke(x); }, rules);

        static List<FightAction> Play(FightRules r, FightState s, IRandomSource rng, Func<bool> until)
        {
            var log = new List<FightAction>();
            for (int i = 0; i < 5000 && !until() && s.Outcome == FightOutcome.Ongoing; i++) BattleSim.Step(r, s, rng, log.Add);
            return log;
        }

        static int Events(List<FightAction> log, string rule) => log.Count(x => x.Event == FightAction.RulePrefix + rule);

        // ------------------------------------------------------------------ constants

        [Test]
        public void Constants_LoadFromContent_AndAreCheckedAtStart()
        {
            var k = TestContent.Db.ConstantValues;
            var r = Content;
            Assert.That(r.LoneBonus, Is.EqualTo(k["MON_LONE_BONUS"]));
            Assert.That(r.LoneMalus, Is.EqualTo(k["MON_LONE_MALUS"]));
            Assert.That(r.AriseStats, Is.EqualTo(k["MON_ARISE_STATS"]));
            Assert.That(r.RiteShare, Is.EqualTo(k["MON_RITE_SHARE"]));

            var bad = new Action<FightRules>[]
            {
                x => x.LoneBonus = -0.1, x => x.LoneBonus = double.PositiveInfinity, x => x.LoneMalus = 1.5, x => x.LoneMalus = -0.1,
                x => x.AriseStats = 1.1, x => x.AriseStats = -0.1, x => x.RiteShare = -1, x => x.RiteShare = double.NaN,
            };
            foreach (var set in bad)
            {
                var rr = Content;
                set(rr);
                Assert.Throws<ArgumentException>(() => BattleSim.Start(rr, new[] { Mon("a") }, new[] { Mon("b") }));
            }
        }

        [Test]
        public void FightsWithoutTheRules_DoNotDependOnTheLaneConstants()
        {
            Fighter[] A() => new[] { Mon("a1"), Mon("a2", x => x.Int = 400) };
            Fighter[] B() => new[] { Mon("b1", x => x.Def = 100), Mon("b2", x => x.Luk = 300) };
            var on = Content;
            var off = Content;
            off.LoneBonus = off.LoneMalus = off.AriseStats = off.RiteShare = 0;
            for (ulong seed = 1; seed <= 5; seed++)
                Assert.That(BattleSim.Resolve(on, A(), B(), seed).Hash, Is.EqualTo(BattleSim.Resolve(off, A(), B(), seed).Hash));
        }

        // ------------------------------------------------------------------ lone_wolf

        [Test]
        public void LoneWolf_AloneOnItsSide_RaisesEveryStat_ForTheFight()
        {
            var r = Plain;
            var s = BattleSim.Start(r, new[] { Mon("a", null, RuleModifiers.LoneWolf) }, new[] { Mon("b1"), Mon("b2") });
            var a = s.Units[0];
            double baseHp = a.MaxHp;
            var log = Play(r, s, null, () => s.Started);
            Assert.That(a.MaxHp, Is.EqualTo(baseHp * (1 + r.LoneBonus)).Within(1e-9));
            Assert.That(a.Hp, Is.EqualTo(a.MaxHp).Within(1e-9), "it starts at full HP");
            foreach (var stat in StatIds.Trained.Where(x => x != StatIds.Hp))
                Assert.That(BattleSim.Eff(r, a, stat), Is.EqualTo(a.Source.Stats.Get(stat) * (1 + r.LoneBonus)).Within(1e-9), stat);
            var ev = log.Single(x => x.Event == FightAction.RulePrefix + RuleModifiers.LoneWolf);
            Assert.That(ev.Actor, Is.EqualTo("a"));
            Assert.That(ev.Amount, Is.EqualTo(r.LoneBonus));
            Assert.That(s.Units[1].MaxHp, Is.EqualTo(baseHp), "only the carrier");

            // off: nothing changes
            s = BattleSim.Start(r, new[] { Mon("a") }, new[] { Mon("b") });
            log = Play(r, s, null, () => s.Started);
            Assert.That(s.Units[0].MaxHp, Is.EqualTo(baseHp));
            Assert.That(BattleSim.Eff(r, s.Units[0], StatIds.Str), Is.EqualTo(300));
            Assert.That(Events(log, RuleModifiers.LoneWolf), Is.EqualTo(0));
        }

        [Test]
        public void LoneWolf_WithAllies_LowersEveryStat_InTheDungeonShapeToo()
        {
            var r = Plain;
            // 4v1, the Dungeon's shape: the monsters' side has 4, so the carrier is weaker; the boss alone is not a carrier
            var s = BattleSim.Start(r, new[] { Mon("a1", null, RuleModifiers.LoneWolf), Mon("a2"), Mon("a3"), Mon("a4") },
                new[] { Mon("boss", x => x.Hp = 3000) });
            var a = s.Units[0];
            double baseHp = a.MaxHp;
            var log = Play(r, s, null, () => s.Started);
            Assert.That(a.MaxHp, Is.EqualTo(baseHp * (1 - r.LoneMalus)).Within(1e-9));
            Assert.That(BattleSim.Eff(r, a, StatIds.Spd), Is.EqualTo(100 * (1 - r.LoneMalus)).Within(1e-9));
            Assert.That(log.Single(x => x.Event == FightAction.RulePrefix + RuleModifiers.LoneWolf).Amount, Is.EqualTo(-r.LoneMalus));
            Assert.That(BattleSim.Eff(r, s.Units[1], StatIds.Str), Is.EqualTo(300), "allies are unaffected");

            // a malus keeps the stat floor: with a big debuff on top the stat stays at STAT_FLOOR_FRAC of base
            a.Effects.Add(new FightEffect { Stat = StatIds.Str, Frac = -0.95, TicksLeft = 5, Source = "test" });
            Assert.That(BattleSim.Eff(r, a, StatIds.Str), Is.EqualTo(300 * r.StatFloor).Within(1e-9));
        }

        [Test]
        public void LoneWolf_IsDecidedAtFightStart_ARisenAllyDoesNotChangeIt()
        {
            var r = Plain;
            var s = BattleSim.Start(r, new[] { Killer("a", RuleModifiers.LoneWolf, RuleModifiers.Arise) },
                new[] { Harmless("b1", x => x.Hp = 1), Harmless("b2") });
            var a = s.Units[0];
            Play(r, s, null, () => s.Units.Count > 3);
            Assert.That(s.Units.Count, Is.EqualTo(4), "an ally rose");
            Assert.That(BattleSim.Eff(r, a, StatIds.Str), Is.EqualTo(3000 * (1 + r.LoneBonus)).Within(1e-9), "still alone at the start");
        }

        // ------------------------------------------------------------------ arise

        [Test]
        public void Arise_TheFirstKill_RisesOnTheCarriersSide_WithItsSkillsAtAShareOfItsStats()
        {
            var r = Plain;
            var fallen = Harmless("b1", x => { x.Hp = 1; x.Def = 200; });
            fallen.Element = "fire";
            fallen.Skills.Add(new CombatSkill { Id = "jab", Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = StatIds.Str, Power = 1 });
            fallen.Rules.Add(RuleModifiers.RootRebirth);
            fallen.Gambits.Add(new GambitRule(GambitConditions.Always, 0, GambitRule.Basic));
            var s = BattleSim.Start(r, new[] { Killer("a", RuleModifiers.Arise) }, new[] { fallen, Harmless("b2") });
            // root_rebirth would save it once; let the first blow use it up
            var log = Play(r, s, null, () => s.Units.Count > 3);
            Assert.That(s.Units.Count, Is.EqualTo(4));
            var risen = s.Units[3];
            Assert.That(risen.Id, Is.EqualTo("b1" + BattleSim.RisenSuffix));
            Assert.That(risen.Side, Is.EqualTo(0));
            Assert.That(risen.Slot, Is.EqualTo(1));
            Assert.That(risen.IsAdd && risen.Risen, Is.True);
            Assert.That(risen.Source.Stats.Def, Is.EqualTo(200 * r.AriseStats).Within(1e-9));
            Assert.That(risen.MaxHp, Is.EqualTo(BattleSim.MaxHp(r, 1 * r.AriseStats)));
            Assert.That(risen.Hp, Is.EqualTo(risen.MaxHp));
            Assert.That(risen.Gauge, Is.EqualTo(0));
            Assert.That(risen.Source.Element, Is.EqualTo("fire"));
            Assert.That(risen.Source.Skills.Select(x => x.Id), Is.EqualTo(new[] { "jab" }), "its skills");
            Assert.That(risen.Source.Rules, Is.Empty, "no rules");
            Assert.That(risen.Source.Gambits, Is.Empty, "no rulebook");
            Assert.That(s.Units[0].AriseUsed, Is.True);
            var ev = log.Single(x => x.Event == FightAction.RulePrefix + RuleModifiers.Arise);
            Assert.That(ev.Actor, Is.EqualTo("a"));
            Assert.That(ev.Target, Is.EqualTo(risen.Id));
            Assert.That(ev.Amount, Is.EqualTo(r.AriseStats));
            Assert.That(s.Units[0].Tally.Kills, Is.EqualTo(1));
        }

        [Test]
        public void Arise_OncePerFight_NeverOnTheLastEnemy_AndOffDoesNothing()
        {
            var r = Plain;
            // 1v3: the first kill rises, the second does not; the risen keeps nothing alive, so the last kill ends the fight
            var s = BattleSim.Start(r, new[] { Killer("a", RuleModifiers.Arise) },
                new[] { Harmless("b1", x => x.Hp = 1), Harmless("b2", x => x.Hp = 1), Harmless("b3", x => x.Hp = 1) });
            var log = Play(r, s, null, () => false);
            Assert.That(s.Outcome, Is.EqualTo(FightOutcome.SideAWon));
            Assert.That(Events(log, RuleModifiers.Arise), Is.EqualTo(1));
            Assert.That(s.Units.Count(u => u.Risen), Is.EqualTo(1));

            // 1v1: the only kill is the last enemy: nothing rises
            s = BattleSim.Start(r, new[] { Killer("a", RuleModifiers.Arise) }, new[] { Harmless("b", x => x.Hp = 1) });
            log = Play(r, s, null, () => false);
            Assert.That(Events(log, RuleModifiers.Arise), Is.EqualTo(0));
            Assert.That(s.Units.Count, Is.EqualTo(2));
            Assert.That(s.Units[0].AriseUsed, Is.False);

            // off
            s = BattleSim.Start(r, new[] { Killer("a") }, new[] { Harmless("b1", x => x.Hp = 1), Harmless("b2") });
            log = Play(r, s, null, () => s.Units[1].Hp <= 0);
            Assert.That(Events(log, RuleModifiers.Arise), Is.EqualTo(0));
            Assert.That(s.Units.Count, Is.EqualTo(3));
        }

        [Test]
        public void Arise_InTheDungeonShape_RaisesAnAdd_WhileTheBossLives()
        {
            var r = Plain;
            // 4v1 with the boss's add already in the fight: killing the add (not the boss) raises it
            var s = BattleSim.Start(r, new[] { Killer("a1", RuleModifiers.Arise), Harmless("a2"), Harmless("a3"), Harmless("a4") },
                new[] { Harmless("add", x => x.Hp = 1), Harmless("boss", x => x.Hp = 99999) });
            s.Units[4].IsAdd = true;   // the add: never keeps its side alive
            var log = Play(r, s, null, () => s.Units.Count > 6);
            Assert.That(Events(log, RuleModifiers.Arise), Is.EqualTo(1));
            Assert.That(s.Units[6].Side, Is.EqualTo(0));
            Assert.That(s.Units[6].Slot, Is.EqualTo(4), "the side's next slot");
        }

        [Test]
        public void Arise_TheRisenNeverKeepsItsSideAlive_AndIsNotRaisedAgain()
        {
            var r = Plain;
            var s = BattleSim.Start(r, new[] { Killer("a", RuleModifiers.Arise) }, new[] { Harmless("b1", x => x.Hp = 1), Harmless("b2") });
            Play(r, s, null, () => s.Units.Count > 3);
            var risen = s.Units[3];
            s.Units[0].Hp = 0;   // the carrier falls: its risen ally alone does not keep side A in the fight
            s.Units[0].KilledBy = "b2";
            var log = Play(r, s, null, () => false);
            Assert.That(s.Outcome, Is.EqualTo(FightOutcome.SideBWon));
            Assert.That(risen.Alive, Is.True);

            // an enemy arise carrier killing a risen unit does not raise it again (nor spend its own rise)
            s = BattleSim.Start(r, new[] { Killer("a", RuleModifiers.Arise) },
                new[] { Harmless("b1", x => x.Hp = 1), Harmless("b2", x => x.Hp = 99999, RuleModifiers.Arise) });
            Play(r, s, null, () => s.Units.Count > 3);
            risen = s.Units[3];
            Assert.That(risen.Risen, Is.True);
            risen.Hp = 0;
            risen.KilledBy = "b2";
            log = Play(r, s, null, () => risen.DeathSeen);
            Assert.That(Events(log, RuleModifiers.Arise), Is.EqualTo(0));
            Assert.That(s.Units.Count, Is.EqualTo(4));
            Assert.That(s.Units[2].AriseUsed, Is.False);
        }

        [Test]
        public void Arise_TakenIds_GetANumber()
        {
            var r = Plain;
            var s = BattleSim.Start(r, new[] { Killer("a", RuleModifiers.Arise), Harmless("b1" + BattleSim.RisenSuffix, x => x.Hp = 99999) },
                new[] { Harmless("b1", x => x.Hp = 1), Harmless("b2") });
            Play(r, s, null, () => s.Units.Count > 4);
            Assert.That(s.Units[4].Id, Is.EqualTo("b1" + BattleSim.RisenSuffix + "2"));
        }

        // ------------------------------------------------------------------ last_rite

        [Test]
        public void LastRite_WhenTheCarrierDies_ItsLivingAlliesSplitAShareOfItsBaseStats()
        {
            var r = Plain;
            var carrier = Harmless("a1", x => { x.Hp = 1; x.Str = 500; x.Def = 100; }, RuleModifiers.LastRite);
            var s = BattleSim.Start(r, new[] { carrier, Harmless("a2", x => x.Hp = 99999), Harmless("a3", x => x.Hp = 99999) },
                new[] { Killer("b") });
            var a2 = s.Units[1];
            double hp0 = a2.MaxHp;
            var log = Play(r, s, null, () => !s.Units[0].Alive);
            double share = r.RiteShare / 2;
            Assert.That(BattleSim.Eff(r, a2, StatIds.Str), Is.EqualTo(0 + share * 500).Within(1e-9));
            Assert.That(BattleSim.Eff(r, a2, StatIds.Def), Is.EqualTo(300 + share * 100).Within(1e-9));
            Assert.That(a2.MaxHp, Is.EqualTo(hp0 + share * BattleSim.MaxHp(r, 1)).Within(1e-9));
            var ev = log.Single(x => x.Event == FightAction.RulePrefix + RuleModifiers.LastRite);
            Assert.That(ev.Actor, Is.EqualTo("a1"));
            Assert.That(ev.Amount, Is.EqualTo(share).Within(1e-12));
            Assert.That(s.Units[3].RiteFlat, Is.Null, "enemies gain nothing");

            // off: nothing
            s = BattleSim.Start(r, new[] { Harmless("a1", x => x.Hp = 1), Harmless("a2", x => x.Hp = 99999) }, new[] { Killer("b") });
            log = Play(r, s, null, () => !s.Units[0].Alive);
            Assert.That(Events(log, RuleModifiers.LastRite), Is.EqualTo(0));
            Assert.That(s.Units[1].RiteFlat, Is.Null);
        }

        [Test]
        public void LastRite_HpGain_RaisesMaxAndCurrentHp_AndSurvivesTimedHpEffects()
        {
            var r = Plain;
            var s = BattleSim.Start(r, new[] { Harmless("a1", x => x.Hp = 1000, RuleModifiers.LastRite), Harmless("a2", x => x.Hp = 99999) },
                new[] { Killer("b") });
            var a2 = s.Units[1];
            a2.Hp = a2.MaxHp / 2;
            double before = a2.Hp, max0 = a2.MaxHp;
            s.Units[0].Hp = 1;
            Play(r, s, null, () => !s.Units[0].Alive);
            double gain = r.RiteShare * BattleSim.MaxHp(r, 1000);
            Assert.That(a2.Hp, Is.EqualTo(before + gain).Within(1e-6));
            Assert.That(a2.MaxHp, Is.EqualTo(max0 + gain).Within(1e-6));
        }

        [Test]
        public void LastRite_DoesNothingAlone_OrOnASave()
        {
            var r = Plain;
            // 1v1: no ally to inherit
            var s = BattleSim.Start(r, new[] { Harmless("a", x => x.Hp = 1, RuleModifiers.LastRite) }, new[] { Killer("b") });
            var log = Play(r, s, null, () => false);
            Assert.That(s.Outcome, Is.EqualTo(FightOutcome.SideBWon));
            Assert.That(Events(log, RuleModifiers.LastRite), Is.EqualTo(0));

            // a root_rebirth save is not a death
            s = BattleSim.Start(r, new[] { Harmless("a1", x => x.Hp = 1, RuleModifiers.LastRite, RuleModifiers.RootRebirth), Harmless("a2", x => x.Hp = 99999) },
                new[] { Killer("b") });
            log = Play(r, s, null, () => s.Units[0].RebirthHeal == 0);
            Assert.That(s.Units[0].Alive, Is.True);
            Assert.That(Events(log, RuleModifiers.LastRite), Is.EqualTo(0));

            // nor a deaths_door save
            s = BattleSim.Start(r, new[] { Harmless("a1", x => x.Hp = 1, RuleModifiers.LastRite, RuleModifiers.DeathsDoor), Harmless("a2", x => x.Hp = 99999) },
                new[] { Killer("b") });
            log = Play(r, s, null, () => s.Units[0].OnDoor);
            Assert.That(s.Units[0].Alive, Is.True);
            Assert.That(Events(log, RuleModifiers.LastRite), Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ modes, determinism, clone

        static Fighter Carrier(string id, string rule, string element)
        {
            var f = Mon(id, x => { x.Hp = 400; x.Str = 350; x.Spd = 150; }, rule);
            f.Element = element;
            f.Skills.Add(new CombatSkill { Id = "maul", Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = StatIds.Str, Power = 1.5, Cooldown = 2 });
            return f;
        }

        static readonly string[] Lane = { RuleModifiers.LoneWolf, RuleModifiers.Arise, RuleModifiers.LastRite };

        [Test]
        public void EveryRule_In1v1_4v1_And4v4_IsDeterministic_AndCloneContinuesIdentically()
        {
            var r = Content;
            foreach (var rule in Lane)
            {
                var shapes = new (Fighter[] A, Fighter[] B)[]
                {
                    (new[] { Carrier("c", rule, "fire") }, new[] { Mon("foe", x => { x.Hp = 500; x.Spd = 200; }) }),
                    (new[] { Carrier("c1", rule, "fire"), Carrier("c2", rule, "water"), Mon("m3"), Mon("m4") },
                     new[] { Mon("boss", x => { x.Hp = 2500; x.Str = 500; x.Spd = 250; }) }),
                    (new[] { Carrier("c1", rule, "fire"), Carrier("c2", rule, "water"), Mon("m3"), Mon("m4") },
                     new[] { Carrier("d1", rule, "nature"), Mon("e2"), Mon("e3"), Mon("e4") }),
                };
                foreach (var (A, B) in shapes)
                {
                    var once = BattleSim.Resolve(r, A, B, 11);
                    Assert.That(BattleSim.Resolve(r, A, B, 11).Hash, Is.EqualTo(once.Hash), rule);
                    Assert.That(once.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut), rule + " " + A.Length + "v" + B.Length);

                    var s = BattleSim.Start(r, A, B);
                    var rng = new RngStream(BattleSim.StreamName, 11);
                    for (int i = 0; i < 40 && s.Outcome == FightOutcome.Ongoing; i++) BattleSim.Step(r, s, rng);
                    var copy = s.Clone();
                    var rngCopy = rng.Clone();
                    while (BattleSim.Step(r, copy, rngCopy) == FightOutcome.Ongoing) { }
                    Assert.That(BattleSim.StateHash(copy), Is.EqualTo(once.Hash), rule);
                }
            }
        }

        [Test]
        public void Clone_CopiesLastRitesGains()
        {
            var r = Plain;
            var s = BattleSim.Start(r, new[] { Harmless("a1", x => x.Hp = 1, RuleModifiers.LastRite), Harmless("a2", x => x.Hp = 99999) },
                new[] { Killer("b") });
            Play(r, s, null, () => !s.Units[0].Alive);
            var copy = s.Clone();
            Assert.That(copy.Units[1].RiteFlat, Is.Not.SameAs(s.Units[1].RiteFlat));
            Assert.That(copy.Units[1].RiteFlat, Is.EqualTo(s.Units[1].RiteFlat));
            Assert.That(BattleSim.StateHash(copy), Is.EqualTo(BattleSim.StateHash(s)));
            copy.Units[1].RiteFlat[1] += 1;
            Assert.That(BattleSim.StateHash(copy), Is.Not.EqualTo(BattleSim.StateHash(s)), "the gains are hashed");
        }
    }
}
