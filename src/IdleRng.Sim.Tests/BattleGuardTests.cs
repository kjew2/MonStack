using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>The trigger and rule guards (specs/16 section 6.4): same-kind, MON_TRIGGER_DEPTH and MON_EXTRA_ACTIONS_CAP.</summary>
    public class BattleGuardTests
    {
        static FightRules Rules => FightRules.FromContent(TestContent.Db);

        /// <summary>Always the same draw: 0 = every evade and crit roll succeeds.</summary>
        sealed class Fixed : IRandomSource
        {
            readonly double _v;
            public Fixed(double v) { _v = v; }
            public double NextDouble() => _v;
        }

        static Fighter Mon(string id, Action<MonsterStats> tweak = null)
        {
            var st = new MonsterStats { Hp = 300, Str = 300, Int = 300, Wis = 300, Dex = 300, Def = 300, Spd = 300, Luk = 0 };
            tweak?.Invoke(st);
            return new Fighter { Id = id, Stats = st };
        }

        static CombatSkill Zap(string id) =>
            new CombatSkill { Id = id, Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = StatIds.Str, Power = 0.01 };   // physical: a magical hit grazes instead of being evaded

        /// <summary>Steps until side A's unit has taken <paramref name="actions"/> actions (side B is too slow to act).</summary>
        static List<FightAction> Play(FightRules r, Fighter a, Fighter b, int actions)
        {
            var s = BattleSim.Start(r, new[] { a }, new[] { b });
            var log = new List<FightAction>();
            while (s.Units[0].Tally.Actions < actions && s.Outcome == FightOutcome.Ongoing) BattleSim.Step(r, s, new Fixed(0), log.Add);
            Assert.That(s.Units[1].Tally.Actions, Is.EqualTo(0), "side B never acted");
            return log;
        }

        static int Basics(List<FightAction> log) => log.Count(x => x.Actor == "a" && x.Skill == null && x.Event == null && !x.Counter);

        [Test]
        public void ConstantsLoadFromContent()
        {
            var k = TestContent.Db.Constants;
            Assert.That((Rules.TriggerDepth, Rules.ExtraActionsCap), Is.EqualTo((k.MonTriggerDepth, k.MonExtraActionsCap)));
            Assert.That(Rules.TriggerDepth, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void OnCritTrigger_DoesNotFireItsOwnTriggerAgain()
        {
            // Every roll crits, so without the guard each crit of the triggered spark would fire spark again, forever.
            var a = Mon("a", x => x.Spd = 900);
            a.Triggers.Add(new TriggeredSkill(FightTrigger.OnCrit, Zap("spark")));
            var log = Play(Rules, a, Mon("b", x => { x.Dex = 0; x.Hp = 99999; x.Spd = 10; }), 3);
            int basics = Basics(log);
            Assert.That(basics, Is.GreaterThanOrEqualTo(3));
            Assert.That(log.Where(x => x.Skill == null && x.Actor == "a").All(x => x.Crit), Is.True);
            Assert.That(log.Where(x => x.Skill?.Id == "spark").All(x => x.Crit), Is.True, "the spark crits too");
            Assert.That(log.Count(x => x.Skill?.Id == "spark"), Is.EqualTo(basics), "one spark per crit of an action, none per crit of a spark");
        }

        [Test]
        public void TriggerDepth_CapsChainsOfDifferentTriggers()
        {
            // a's basic attack crits b. b's OnHitTaken (level 1) thorn is evaded by a, whose OnEvade (level 2) riposte crits b;
            // a's OnCrit would be level 3. a's own OnCrit (level 1) zap crits b; b's OnHitTaken (level 2) thorn is evaded by a,
            // and a's OnEvade would be level 3. A level-2 cap stops both third levels; a level-3 cap lets both through.
            Fighter A()
            {
                var a = Mon("a", x => x.Spd = 900);
                a.Triggers.Add(new TriggeredSkill(FightTrigger.OnCrit, Zap("zap")));
                a.Triggers.Add(new TriggeredSkill(FightTrigger.OnEvade, Zap("riposte")));
                return a;
            }
            Fighter B()
            {
                var b = Mon("b", x => { x.Dex = 0; x.Hp = 99999; x.Spd = 10; });
                b.Triggers.Add(new TriggeredSkill(FightTrigger.OnHitTaken, Zap("thorn")));
                return b;
            }

            var r = Rules;
            r.TriggerDepth = 2;
            var log = Play(r, A(), B(), 1);
            int basics = Basics(log);
            Assert.That(log.Count(x => x.Skill?.Id == "thorn"), Is.EqualTo(2 * basics), "once for the basic attack, once for zap; never inside itself");
            Assert.That(log.Count(x => x.Skill?.Id == "zap"), Is.EqualTo(basics), "level 2 cap: only the action's crit");
            Assert.That(log.Count(x => x.Skill?.Id == "riposte"), Is.EqualTo(basics), "level 2 cap: only after the action's thorn");

            r.TriggerDepth = 3;
            log = Play(r, A(), B(), 1);
            basics = Basics(log);
            Assert.That(log.Count(x => x.Skill?.Id == "zap"), Is.EqualTo(2 * basics), "level 3: riposte's crit fires zap too");
            Assert.That(log.Count(x => x.Skill?.Id == "riposte"), Is.EqualTo(2 * basics), "level 3: zap's thorn is evaded too");

            r.TriggerDepth = 1;
            log = Play(r, A(), B(), 1);
            basics = Basics(log);
            Assert.That(log.Count(x => x.Skill?.Id == "zap"), Is.EqualTo(basics));
            Assert.That(log.Count(x => x.Skill?.Id == "thorn"), Is.EqualTo(basics), "level 1: no trigger inside a trigger");
            Assert.That(log.Count(x => x.Skill?.Id == "riposte"), Is.EqualTo(0));
        }

        [Test]
        public void ExtraActionsCap_LimitsDoubleTurnStart()
        {
            Fighter Harmless(string id) => Mon(id, x => { x.Str = 0; x.Int = 0; });
            Fighter Doubler() { var f = Harmless("a"); f.Rules.Add(RuleModifiers.DoubleTurnStart); return f; }
            var r = Rules;
            r.ExtraActionsCap = 0;
            var s = BattleSim.Start(r, new[] { Doubler() }, new[] { Harmless("b") });
            var log = new List<FightAction>();
            for (int t = 0; t < 200; t++) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(s.Units[0].Tally.Actions, Is.EqualTo(s.Units[1].Tally.Actions), "cap 0: no extra action");
            Assert.That(log.Any(x => x.Event == FightAction.DoubleTurnEvent), Is.False);

            r.ExtraActionsCap = 1;
            s = BattleSim.Start(r, new[] { Doubler() }, new[] { Harmless("b") });
            for (int t = 0; t < 200; t++) BattleSim.Step(r, s, new Fixed(0.99));
            Assert.That(s.Units[0].Tally.Actions, Is.EqualTo(s.Units[1].Tally.Actions + r.DoubleTurns), "one extra per doubled turn fits under cap 1");
        }

        [Test]
        public void Validation_RejectsGuardConstantsOutOfRange()
        {
            var a = new[] { Mon("a") };
            var b = new[] { Mon("b") };
            var r = Rules;
            r.TriggerDepth = 0;
            Assert.Throws<ArgumentException>(() => BattleSim.Start(r, a, b));
            r = Rules;
            r.ExtraActionsCap = -1;
            Assert.Throws<ArgumentException>(() => BattleSim.Start(r, a, b));
        }
    }
}
