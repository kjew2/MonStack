using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using IdleRng.Sim.Tower;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Gambit rulebooks in the battle engine (specs/28 section 4): conditions, order, held skills, target picks, validation.</summary>
    public class GambitTests
    {
        static FightRules Rules => FightRules.FromContent(TestContent.Db);

        static MonsterStats Sheet(double all) => new MonsterStats { Hp = all, Str = all, Int = all, Wis = all, Dex = all, Def = all, Spd = all, Luk = all };

        static Fighter Mon(string id, double all = 300, params CombatSkill[] skills) =>
            new Fighter { Id = id, Element = null, Stats = Sheet(all), Skills = skills.ToList() };

        static CombatSkill Strike(int cd = 0) =>
            new CombatSkill { Id = "strike", Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = StatIds.Str, Power = 2, Cooldown = cd };

        static CombatSkill Mend(double power = 5) =>
            new CombatSkill { Id = "mend", Kind = SkillKind.Heal, Target = SkillTarget.Self, Scales = StatIds.Wis, Power = power, Cooldown = 0 };

        static CombatSkill Calm() =>
            new CombatSkill { Id = "calm", Kind = SkillKind.Passive, Stat = StatIds.Def, Power = 0.1 };

        static Fighter[] Side(params Fighter[] f) => f;

        /// <summary>A sturdy enemy that deals no damage, so a test's attacker lives long enough to show its picks.</summary>
        static Fighter Dummy(string id)
        {
            var d = Mon(id, 999);
            d.Stats.Str = 0;
            d.Stats.Int = 0;
            return d;
        }

        /// <summary>A rng that always returns 0.99: no crit, no evade, and "auto" picks the last living enemy.</summary>
        sealed class High : IRandomSource
        {
            public double NextDouble() => 0.99;
        }

        /// <summary>Steps the fight to its end (or <paramref name="maxSteps"/> ticks) and returns the log.</summary>
        static List<FightAction> Play(FightState s, int maxSteps = 5000)
        {
            var log = new List<FightAction>();
            var rng = new High();
            for (int i = 0; i < maxSteps && s.Outcome == FightOutcome.Ongoing; i++) BattleSim.Step(Rules, s, rng, log.Add);
            return log;
        }

        [Test]
        public void ARulebookThatNeverFires_LeavesTheFightAsTheDefaultAiPlaysIt()
        {
            Fighter A() => Mon("a", 300, Strike(2));
            var plain = BattleSim.Resolve(Rules, Side(A()), Side(Mon("b", 280, Strike(3))), 11);
            var ruled = A();
            ruled.Gambits.Add(new GambitRule(GambitConditions.EnemiesAtLeast, 4, GambitRule.Basic));   // one enemy: never true
            var withBook = BattleSim.Resolve(Rules, Side(ruled), Side(Mon("b", 280, Strike(3))), 11);
            Assert.That(withBook.Hash, Is.EqualTo(plain.Hash));
        }

        [Test]
        public void ANamedSkill_IsUsedOnlyWhenItsRuleHolds()
        {
            var a = Mon("a", 300, Strike());
            a.Stats.Spd = 600;
            a.Gambits.Add(new GambitRule(GambitConditions.EnemyHpBelow, 0.3, "strike"));
            var s = BattleSim.Start(Rules, Side(a), Side(Mon("b", 300)));
            var b = s.Units[1];
            var log = new List<FightAction>();
            var rng = new High();
            bool sawGambit = false;
            int strikes = 0;
            while (s.Outcome == FightOutcome.Ongoing)
                BattleSim.Step(Rules, s, rng, x =>
                {
                    log.Add(x);
                    if (x.Event == FightAction.GambitEvent)
                    {
                        Assert.That(b.Hp / b.MaxHp, Is.LessThan(0.3), "the rule fired above its threshold");
                        sawGambit = true;
                    }
                    if (x.Actor == "a" && x.Skill?.Id == "strike")
                    {
                        Assert.That(sawGambit, "strike was used without its rule");
                        strikes++;
                    }
                });
            Assert.That(strikes, Is.GreaterThan(0));
            Assert.That(log.Any(x => x.Actor == "a" && x.Skill == null && x.Event == null), "a should basic-attack while b is above 30%");
        }

        [Test]
        public void RulesGoInOrder_AndARuleWhoseSkillIsCoolingDownIsSkipped()
        {
            var big = new CombatSkill { Id = "big", Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = StatIds.Str, Power = 3, Cooldown = 3 };
            var a = Mon("a", 300, Strike(), big);
            a.Stats.Spd = 900;
            a.Gambits.Add(new GambitRule(GambitConditions.Always, 0, "big"));
            a.Gambits.Add(new GambitRule(GambitConditions.Always, 0, "strike"));
            var s = BattleSim.Start(Rules, Side(a), Side(Mon("b", 999)));
            var log = Play(s, 60).Where(x => x.Actor == "a").ToList();
            var fired = log.Where(x => x.Event == FightAction.GambitEvent).Select(x => (int)x.Amount).Take(6).ToArray();
            // big (rule 0), then strike (rule 1) while big cools down for 3 actions, then big again
            Assert.That(fired, Is.EqualTo(new[] { 0, 1, 1, 0, 1, 1 }));
        }

        [Test]
        public void FirstActions_FiresOnTheUnitsOpeningActionsOnly()
        {
            var a = Mon("a", 300, Strike());
            a.Stats.Spd = 900;
            a.Gambits.Add(new GambitRule(GambitConditions.FirstActions, 1, GambitRule.Basic));
            var s = BattleSim.Start(Rules, Side(a), Side(Mon("b", 999)));
            var acts = Play(s, 60).Where(x => x.Actor == "a" && x.Event == null).Take(2).ToList();
            Assert.That(acts[0].Skill, Is.Null, "the opening action is the basic attack");
            Assert.That(acts[1].Skill?.Id, Is.EqualTo("strike"), "then the default AI takes over the unnamed strike");
        }

        [Test]
        public void LowestHp_FocusesOneEnemy_HighestHp_SpreadsHits_WithoutARandomDraw()
        {
            List<string> Targets(string pick)
            {
                var a = Mon("a", 300, Strike());
                a.Stats.Spd = 900;
                a.Gambits.Add(new GambitRule(GambitConditions.Always, 0, "strike", pick));
                var s = BattleSim.Start(Rules, Side(a), Side(Dummy("e0"), Dummy("e1")));
                return Play(s, 80).Where(x => x.Actor == "a" && x.Skill?.Id == "strike").Select(x => x.Target).Take(4).ToList();
            }
            // equal HP fractions at first: the lowest slot; then the hurt one again (lowest_hp) or the other (highest_hp)
            Assert.That(Targets(GambitTargets.LowestHp), Is.EqualTo(new[] { "e0", "e0", "e0", "e0" }));
            Assert.That(Targets(GambitTargets.HighestHp), Is.EqualTo(new[] { "e0", "e1", "e0", "e1" }));
            // auto keeps the default rule: one draw, 0.99 picks the last living enemy
            Assert.That(Targets(GambitTargets.Auto)[0], Is.EqualTo("e1"));
        }

        [Test]
        public void ATauntingEnemy_IsStillForced()
        {
            var a = Mon("a", 300, Strike());
            a.Stats.Spd = 900;
            a.Gambits.Add(new GambitRule(GambitConditions.Always, 0, "strike", GambitTargets.LowestHp));
            var s = BattleSim.Start(Rules, Side(a), Side(Mon("e0", 999), Mon("e1", 999)));
            s.Units[2].TauntTicks = 10_000;
            var first = Play(s, 80).First(x => x.Actor == "a" && x.Skill?.Id == "strike");
            Assert.That(first.Target, Is.EqualTo("e1"));
        }

        [Test]
        public void AHealRule_ReplacesTheDefaultWaitForTheFullAmount()
        {
            FightAction FirstHealerAction(bool withRule)
            {
                var h = Mon("h", 300, Mend());
                h.Stats.Spd = 900;
                if (withRule) h.Gambits.Add(new GambitRule(GambitConditions.SelfHpBelow, 0.95, "mend"));
                var s = BattleSim.Start(Rules, Side(h), Side(Mon("b", 10)));
                s.Units[0].Hp = 0.9 * s.Units[0].MaxHp;   // missing far less than the heal restores
                return Play(s, 80).First(x => x.Actor == "h" && x.Event == null);
            }
            Assert.That(FirstHealerAction(false).Skill, Is.Null, "the default AI waits until the heal lands in full");
            Assert.That(FirstHealerAction(true).Skill?.Id, Is.EqualTo("mend"));
        }

        [Test]
        public void Start_RejectsABadRulebook()
        {
            void Bad(GambitRule rule)
            {
                var a = Mon("a", 300, Strike(), Calm());
                a.Gambits.Add(rule);
                Assert.Throws<ArgumentException>(() => BattleSim.Start(Rules, Side(a), Side(Mon("b"))), $"{rule?.When} {rule?.Value} {rule?.Skill} {rule?.Target}");
            }
            Bad(null);
            Bad(new GambitRule("whenever", 0, "strike"));
            Bad(new GambitRule(GambitConditions.SelfHpBelow, 0, "strike"));
            Bad(new GambitRule(GambitConditions.SelfHpBelow, 1.5, "strike"));
            Bad(new GambitRule(GambitConditions.EnemiesAtLeast, 0, "strike"));
            Bad(new GambitRule(GambitConditions.FirstActions, 1.5, "strike"));
            Bad(new GambitRule(GambitConditions.Always, 0, "fireball"));          // not the unit's skill
            Bad(new GambitRule(GambitConditions.Always, 0, "calm"));              // a passive is not an action
            Bad(new GambitRule(GambitConditions.Always, 0, "strike", "random"));
            Bad(new GambitRule(GambitConditions.Always, 0, "strike", null));
            var ok = Mon("a", 300, Strike());
            ok.Gambits.Add(new GambitRule(GambitConditions.SelfHpBelow, 1, GambitRule.Basic, GambitTargets.HighestHp));
            Assert.DoesNotThrow(() => BattleSim.Start(Rules, Side(ok), Side(Mon("b"))));
        }

        [Test]
        public void AClonedFight_WithGambits_ContinuesIdentically()
        {
            var a = Mon("a", 300, Strike(1));
            a.Gambits.Add(new GambitRule(GambitConditions.EnemyHpBelow, 0.5, "strike", GambitTargets.LowestHp));
            var s = BattleSim.Start(Rules, Side(a, Mon("a2", 250)), Side(Mon("e0", 260, Strike(2)), Mon("e1", 240)));
            var rng = new RngStream(BattleSim.StreamName, 5);
            for (int i = 0; i < 120; i++) BattleSim.Step(Rules, s, rng);
            var c = s.Clone();
            var rngCopy = rng.Clone();
            while (s.Outcome == FightOutcome.Ongoing) BattleSim.Step(Rules, s, rng);
            while (c.Outcome == FightOutcome.Ongoing) BattleSim.Step(Rules, c, rngCopy);
            Assert.That(BattleSim.StateHash(c), Is.EqualTo(BattleSim.StateHash(s)));
        }

        [Test]
        public void TowerTeams_KeepTheirRulebooks()
        {
            var a = Mon("monster", 300, Strike());
            a.Gambits.Add(new GambitRule(GambitConditions.Always, 0, "strike"));
            var t = TowerSim.Team(new[] { a }, 4);
            Assert.That(t[0].Id, Is.EqualTo("t0_monster"));
            Assert.That(t[0].Gambits, Is.SameAs(a.Gambits));
        }
    }
}
