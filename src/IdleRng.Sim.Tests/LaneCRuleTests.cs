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
    /// Idea-pool lane C (#136): the turn-gauge capstones brave_heart, one_more, guard_break and latent_power (specs/16 section
    /// 6.2, Combat/Rules/LaneC.cs).
    /// </summary>
    public class LaneCRuleTests
    {
        static FightRules Rules => FightRules.FromContent(TestContent.Db);

        /// <summary>Always the same draw: 0.99 = no crit or evade; 0 = every crit and evade roll succeeds.</summary>
        sealed class Fixed : IRandomSource
        {
            readonly double _v;
            public Fixed(double v) { _v = v; }
            public double NextDouble() => _v;
        }

        static Fighter Mon(string id, Action<MonsterStats> tweak = null, params string[] rules)
        {
            var st = new MonsterStats { Hp = 300, Str = 300, Int = 0, Wis = 300, Dex = 300, Def = 300, Spd = 300, Luk = 0 };
            tweak?.Invoke(st);
            var f = new Fighter { Id = id, Stats = st };
            f.Rules.AddRange(rules);
            return f;
        }

        /// <summary>Deals no damage (STR and INT 0), so its foe stays at full HP.</summary>
        static Fighter Harmless(string id, Action<MonsterStats> tweak = null) => Mon(id, x => { x.Str = 0; x.Int = 0; tweak?.Invoke(x); });

        /// <summary>Never evades (DEX 0) and does not die (huge HP).</summary>
        static Fighter Dummy(string id, Action<MonsterStats> tweak = null) => Harmless(id, x => { x.Dex = 0; x.Hp = 99999; tweak?.Invoke(x); });

        static List<FightAction> Run(FightRules r, FightState s, double draw, int ticks, Action<FightAction> also = null)
        {
            var log = new List<FightAction>();
            for (int t = 0; t < ticks && s.Outcome == FightOutcome.Ongoing; t++)
                BattleSim.Step(r, s, new Fixed(draw), x => { log.Add(x); also?.Invoke(x); });
            return log;
        }

        /// <summary>The hits the unit dealt as actions (basic attacks and skills, not counters or rule events).</summary>
        static List<FightAction> Hits(List<FightAction> log, string id) =>
            log.Where(x => x.Actor == id && x.Event == null && !x.Counter && x.Target != id).ToList();

        [Test]
        public void ConstantsLoadFromContent()
        {
            var k = TestContent.Db.ConstantValues;
            var r = Rules;
            Assert.That(r.BraveMax, Is.EqualTo((int)k["MON_BRAVE_MAX"]));
            Assert.That(r.BraveGuard, Is.EqualTo(k["MON_BRAVE_GUARD"]));
            Assert.That(r.OneMore, Is.EqualTo(k["MON_ONE_MORE"]));
            Assert.That(r.BreakPips, Is.EqualTo((int)k["MON_BREAK_PIPS"]));
            Assert.That(r.BreakBonus, Is.EqualTo(k["MON_BREAK_BONUS"]));
            Assert.That(r.LatentActions, Is.EqualTo((int)k["MON_LATENT_ACTIONS"]));
            Assert.That(r.LatentBonus, Is.EqualTo(k["MON_LATENT_BONUS"]));
            Assert.That(r.BraveMax, Is.GreaterThanOrEqualTo(1));
            Assert.That(r.BreakPips, Is.GreaterThanOrEqualTo(1));
            foreach (var id in new[] { RuleModifiers.BraveHeart, RuleModifiers.OneMore, RuleModifiers.GuardBreak, RuleModifiers.LatentPower })
            {
                Assert.That(RuleModifiers.IsKnown(id), id);
                Assert.That(TestContent.Db.Trees.ContainsKey(id), "the generic tree " + id);
            }
        }

        [Test]
        public void Validation_RejectsLaneConstantsOutOfRange()
        {
            var a = new[] { Mon("a") };
            var b = new[] { Mon("b") };
            foreach (Action<FightRules> bad in new Action<FightRules>[]
                     {
                         r => r.BraveMax = -1, r => r.BraveGuard = 1.5, r => r.OneMore = -0.1, r => r.BreakPips = 0, r => r.BreakBonus = -1,
                         r => r.LatentActions = 0, r => r.LatentBonus = -0.1,
                     })
            {
                var r = Rules;
                bad(r);
                Assert.Throws<ArgumentException>(() => BattleSim.Start(r, a, b));
            }
        }

        // ------------------------------------------------------------------ brave_heart

        [Test]
        public void BraveHeart_DefaultsAtFullHp_ThenActsAndSpendsPipsUnderTheCap()
        {
            var r = Rules;
            var s = BattleSim.Start(r, new[] { Harmless("a", null) }, new[] { Dummy("b") });
            s.Units[0].Source.Rules.Add(RuleModifiers.BraveHeart);
            var a = s.Units[0];
            var log = new List<FightAction>();
            while (a.Tally.Actions == 0 && s.Ticks < 200) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            var brave = log.Where(x => x.Actor == "a" && x.Event == BattleSim.BraveEvent).ToList();
            var defaults = brave.Where(x => x.Amount > 0).Select(x => (int)x.Amount).ToList();
            Assert.That(defaults, Is.EqualTo(Enumerable.Range(1, r.BraveMax).ToList()), "it Defaults, one pip a turn, up to MON_BRAVE_MAX");
            int spend = Math.Min(r.BraveMax, r.ExtraActionsCap);
            Assert.That(a.Tally.Actions, Is.EqualTo(1 + spend), "at max pips it acts, then spends pips as extra actions under the cap");
            Assert.That(brave.Count(x => x.Amount == 0), Is.EqualTo(spend), "each pip spent is logged before its extra action");
            Assert.That(a.BravePips, Is.EqualTo(r.BraveMax - spend), "pips over the cap wait for a later turn");
            Assert.That(a.Hp, Is.EqualTo(a.MaxHp));

            // It never Defaults forever against a foe that cannot hurt it: it keeps acting.
            Run(r, s, 0.99, 300);
            Assert.That(a.Tally.Actions, Is.GreaterThan(s.Units[1].Tally.Actions / 2), "it acts at least every other turn");
            Assert.That(a.BravePips, Is.LessThanOrEqualTo(r.BraveMax));
        }

        [Test]
        public void BraveHeart_ActsWhenHurt_AndADefaultGuardsUntilItsNextTurn()
        {
            // b hits a once per a's two turns (a is twice as fast); a's first two turns are Defaults, so b's first hit is guarded.
            double FirstHitTaken(bool brave)
            {
                var a = Harmless("a", x => x.Spd = 600);
                if (brave) a.Rules.Add(RuleModifiers.BraveHeart);
                var s = BattleSim.Start(Rules, new[] { a }, new[] { Mon("b", x => x.Hp = 99999) });
                var log = Run(Rules, s, 0.99, 50);
                return Hits(log, "b")[0].Amount;
            }
            Assert.That(FirstHitTaken(true), Is.EqualTo(FirstHitTaken(false) * (1 - Rules.BraveGuard)).Within(1e-9));

            // Hurt and unshielded, it does not Default: it acts at once.
            var r = Rules;
            var s2 = BattleSim.Start(r, new[] { Harmless("a", null) }, new[] { Dummy("b") });
            var u = s2.Units[0];
            u.Source.Rules.Add(RuleModifiers.BraveHeart);
            u.Hp = u.MaxHp - 1;
            var log2 = Run(r, s2, 0.99, 5);
            Assert.That(log2.Any(x => x.Event == BattleSim.BraveEvent && x.Amount > 0), Is.False);
            Assert.That(u.Tally.Actions, Is.GreaterThan(0));
        }

        [Test]
        public void BraveHeart_WithDoubleTurnStartAndOneMore_KeepsTheExtraActionCap()
        {
            foreach (int cap in new[] { 0, 1, 2 })
            {
                var r = Rules;
                r.ExtraActionsCap = cap;
                var a = Mon("a", x => { x.Str = 1; x.Luk = 999; }, RuleModifiers.BraveHeart, RuleModifiers.DoubleTurnStart, RuleModifiers.OneMore);
                var s = BattleSim.Start(r, new[] { a }, new[] { Dummy("b"), Dummy("c") });
                int maxTurnAction = 0;
                // Draw 0: every hit crits, so one_more refunds each action and the fight never ends on its own.
                var log = Run(r, s, 0, 400, x => maxTurnAction = Math.Max(maxTurnAction, s.Units.Max(u => u.TurnAction)));
                Assert.That(maxTurnAction, Is.EqualTo(cap), "cap " + cap + ": reached, never passed");
                Assert.That(s.Outcome == FightOutcome.TimedOut || s.Ticks == 400, "the fight ran on without looping");
                Assert.That(s.Units[0].Tally.Actions, Is.GreaterThan(0));
                if (cap == 0) Assert.That(log.Any(x => x.Event == BattleSim.BraveEvent && x.Amount == 0), Is.False, "cap 0: no pip is spent");
            }
        }

        [Test]
        public void BraveHeart_ADefaultIsNeverFollowedByAnExtraAction()
        {
            // Cap 2: act (1 action), double_turn_start (2), a pip (3): the next turn starts on an odd count, where double_turn_start
            // would grant an extra action after any action. A Default is no action, so nothing follows it (specs/16 section 6.4).
            var r = Rules;
            Assert.That(r.ExtraActionsCap, Is.EqualTo(2));
            var s = BattleSim.Start(r, new[] { Harmless("a", null) }, new[] { Dummy("b") });
            s.Units[0].Source.Rules.AddRange(new[] { RuleModifiers.BraveHeart, RuleModifiers.DoubleTurnStart });
            var mine = Run(r, s, 0.99, 200).Where(x => x.Actor == "a").ToList();
            int defaults = 0;
            for (int i = 0; i + 1 < mine.Count; i++)
            {
                if (mine[i].Event != BattleSim.BraveEvent || mine[i].Amount == 0) continue;
                defaults++;
                Assert.That(mine[i + 1].Event, Is.Not.EqualTo(FightAction.DoubleTurnEvent), "no extra action after a Default");
                Assert.That(mine[i + 1].Event == BattleSim.BraveEvent && mine[i + 1].Amount == 0, Is.False, "no pip spent after a Default");
            }
            Assert.That(defaults, Is.GreaterThan(r.BraveMax), "it Defaults again after spending pips");
            Assert.That(mine.Count(x => x.Event == FightAction.DoubleTurnEvent), Is.EqualTo(1),
                "the second doubled turn would have come after a Default; it is not granted");
        }

        // ------------------------------------------------------------------ one_more

        [Test]
        public void OneMore_RefundsGaugeOnCritOrStrongElement_OncePerAction()
        {
            var r = Rules;
            var sweep = new CombatSkill { Id = "sweep", Kind = SkillKind.Damage, Target = SkillTarget.AllEnemies, Scales = StatIds.Str, Power = 0.01 };
            Fighter A(bool rule) { var f = Mon("a", null, rule ? new[] { RuleModifiers.OneMore } : new string[0]); f.Skills.Add(sweep); return f; }

            // Draw 0: every hit crits. Two enemies per sweep, yet one refund per action.
            var s = BattleSim.Start(r, new[] { A(true) }, new[] { Dummy("b"), Dummy("c") });
            var log = Run(r, s, 0, 60, x => Assert.That(s.Units[0].Gauge, Is.LessThanOrEqualTo(r.GaugeSize + 1e-9), "never past a full gauge"));
            var refunds = log.Where(x => x.Event == BattleSim.OneMoreEvent).ToList();
            Assert.That(refunds.Count, Is.EqualTo(s.Units[0].Tally.Actions));
            Assert.That(refunds.All(x => x.Actor == "a" && x.Amount <= r.OneMore + 1e-9), Is.True);
            int withRule = s.Units[0].Tally.Actions;

            var off = BattleSim.Start(r, new[] { A(false) }, new[] { Dummy("b"), Dummy("c") });
            var offLog = Run(r, off, 0, 60);
            Assert.That(offLog.Any(x => x.Event == BattleSim.OneMoreEvent), Is.False);
            Assert.That(withRule, Is.GreaterThan(off.Units[0].Tally.Actions), "refunds mean more actions");

            // No crit (draw 0.99): only a hit whose element is strong against the target refunds.
            int Refunds(string mine, string theirs)
            {
                var a = A(true); a.Element = mine;
                var st = BattleSim.Start(r, new[] { a }, new[] { Dummy("b") });
                st.Units[1].Source.Element = theirs;
                return Run(r, st, 0.99, 20).Count(x => x.Event == BattleSim.OneMoreEvent);
            }
            Assert.That(Refunds("fire", "nature"), Is.GreaterThan(0), "fire is strong against nature");
            Assert.That(Refunds("fire", "water"), Is.EqualTo(0), "a weak hit does not refund");
            Assert.That(Refunds(null, null), Is.EqualTo(0));
        }

        [Test]
        public void OneMore_RefundStopsAtAFullGauge()
        {
            var r = Rules;
            var s = BattleSim.Start(r, new[] { Mon("a", null, RuleModifiers.OneMore) }, new[] { Dummy("b", x => x.Spd = 10) });
            double fill = BattleSim.GaugeFill(r, 300);
            // After the first tick's fill and the action's cost the gauge is 0.8 of full, so the crit refunds only 0.2 (not 0.4).
            s.Units[0].Gauge = 1.8 * r.GaugeSize - fill;
            var log = new List<FightAction>();
            BattleSim.Step(r, s, new Fixed(0), log.Add);
            var refund = log.First(x => x.Event == BattleSim.OneMoreEvent);
            Assert.That(refund.Amount, Is.EqualTo(0.2).Within(1e-9), "the share of a full gauge");
            Assert.That(r.OneMore, Is.GreaterThan(refund.Amount));
        }

        // ------------------------------------------------------------------ guard_break

        [Test]
        public void GuardBreak_StripsPips_ThenTheEnemyLosesAnActionAndTakesMore()
        {
            var r = Rules;
            var s = BattleSim.Start(r, new[] { Mon("a", x => x.Str = 50, RuleModifiers.GuardBreak) }, new[] { Dummy("b") });
            // Draw 0: every hit of a crits (b cannot evade). Same SPD: each tick a acts, then b (Units order).
            var log = Run(r, s, 0, 12 * (r.BreakPips + 1));   // about three actions each per 10 ticks
            var strips = log.Where(x => x.Event == BattleSim.BreakEvent && x.Actor == "a").ToList();
            Assert.That(strips.All(x => x.Target == "b"), Is.True);
            var left = strips.Select(x => (int)x.Amount).ToList();
            var cycle = Enumerable.Range(0, r.BreakPips).Reverse().ToList();
            Assert.That(left.Take(2 * r.BreakPips).ToList(), Is.EqualTo(cycle.Concat(cycle).ToList()), "pips left after each crit, down to 0, then refilled");

            var lost = log.Where(x => x.Event == BattleSim.BreakEvent && x.Actor == "b").ToList();
            Assert.That(lost.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(lost.All(x => x.Target == "b" && x.Amount == 0), Is.True);
            Assert.That(s.Units[1].Tally.Actions, Is.EqualTo(s.Units[0].Tally.Actions - lost.Count), "each break costs b one action");

            // The hit that breaks b is plain; a's next hit (before b acts again) takes the bonus; the hit after b acted is plain.
            var hits = Hits(log, "a");
            double plain = hits[0].Amount;
            foreach (var l in lost)
            {
                int i = log.IndexOf(l);
                var next = log.Skip(i).First(x => x.Actor == "a" && x.Event == null && x.Target == "b");
                Assert.That(next.Amount, Is.EqualTo(plain * (1 + r.BreakBonus)).Within(1e-6), "broken until it acts again");
            }
            Assert.That(hits.Count(x => Math.Abs(x.Amount - plain) > 1e-6), Is.EqualTo(lost.Count(l => log.Skip(log.IndexOf(l)).Any(x => x.Actor == "a" && x.Event == null))));

            // Off: no pips stripped, no lost action.
            var off = BattleSim.Start(r, new[] { Mon("a", x => x.Str = 50) }, new[] { Dummy("b") });
            var offLog = Run(r, off, 0, 30);
            Assert.That(offLog.Any(x => x.Event == BattleSim.BreakEvent), Is.False);
            Assert.That(off.Units[1].Tally.Actions, Is.EqualTo(off.Units[0].Tally.Actions));
        }

        [Test]
        public void GuardBreak_TwoCarriersShareTheEnemysPips()
        {
            var r = Rules;
            var s = BattleSim.Start(r, new[] { Mon("a1", x => x.Str = 50, RuleModifiers.GuardBreak), Mon("a2", x => x.Str = 50, RuleModifiers.GuardBreak) },
                new[] { Dummy("b") });
            var log = new List<FightAction>();
            while (s.Units[2].BreakState == 0 && s.Ticks < 100) BattleSim.Step(r, s, new Fixed(0), log.Add);
            var strips = log.Where(x => x.Event == BattleSim.BreakEvent && x.Actor != "b").ToList();
            Assert.That(strips.Count, Is.EqualTo(r.BreakPips), "one pool of pips for both carriers");
            Assert.That(strips.Select(x => x.Actor).Distinct().Count(), Is.EqualTo(2));
        }

        [Test]
        public void GuardBreak_PipsAreHashedOnlyWithACarrierAgainstTheUnit()
        {
            var r = Rules;
            string Hash(bool carrier, int stripped)
            {
                var a = Mon("a", null, carrier ? new[] { RuleModifiers.GuardBreak } : new string[0]);
                var s = BattleSim.Start(r, new[] { a }, new[] { Dummy("b") });
                s.Units[1].BreakStripped = stripped;
                return BattleSim.StateHash(s);
            }
            Assert.That(Hash(false, 2), Is.EqualTo(Hash(false, 0)), "no carrier: not hashed, so old fights hash as before");
            Assert.That(Hash(true, 2), Is.Not.EqualTo(Hash(true, 0)));
        }

        // ------------------------------------------------------------------ latent_power

        [Test]
        public void LatentPower_RaisesStatsAfterItsNthAction_Once()
        {
            var r = Rules;
            var s = BattleSim.Start(r, new[] { Mon("a", null, RuleModifiers.LatentPower) }, new[] { Dummy("b") });
            var log = Run(r, s, 0.99, 40);
            var a = s.Units[0];
            Assert.That(a.Tally.Actions, Is.GreaterThan(r.LatentActions + 1));
            var wake = log.Where(x => x.Event == BattleSim.LatentEvent).ToList();
            Assert.That(wake.Count, Is.EqualTo(1));
            Assert.That(wake[0].Amount, Is.EqualTo(r.LatentBonus));
            int at = log.IndexOf(wake[0]);
            Assert.That(Hits(log.Take(at).ToList(), "a").Count, Is.EqualTo(r.LatentActions), "it wakes right after its Nth action");
            var hits = Hits(log, "a");
            Assert.That(hits[r.LatentActions].Amount, Is.EqualTo(hits[0].Amount * (1 + r.LatentBonus)).Within(1e-6), "STR rose by the bonus");
            Assert.That(BattleSim.Eff(r, a, StatIds.Spd), Is.EqualTo(300 * (1 + r.LatentBonus)).Within(1e-9));

            var off = BattleSim.Start(r, new[] { Mon("a") }, new[] { Dummy("b") });
            var offLog = Run(r, off, 0.99, 40);
            Assert.That(BattleSim.Eff(r, a, StatIds.Hp), Is.EqualTo(BattleSim.Eff(r, off.Units[0], StatIds.Hp)), "HP does not rise");
            Assert.That(offLog.Any(x => x.Event == BattleSim.LatentEvent), Is.False);
            Assert.That(Hits(offLog, "a").Select(x => x.Amount).Distinct().Count(), Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ modes

        [Test]
        public void Modes_1v1_4v1_4v4_ResolveDeterministically()
        {
            var r = Rules;
            Fighter Carrier(string id, string rule, string el) =>
                Mon(id, x => { x.Hp = 400; x.Luk = 400; x.Spd = 280; }, rule).Also(f => f.Element = el);
            Fighter[] Team(string p) => new[]
            {
                Carrier(p + "brave", RuleModifiers.BraveHeart, "fire"), Carrier(p + "more", RuleModifiers.OneMore, "water"),
                Carrier(p + "break", RuleModifiers.GuardBreak, "nature"), Carrier(p + "latent", RuleModifiers.LatentPower, "light"),
            };
            var boss = Mon("boss", x => { x.Hp = 3000; x.Str = 500; x.Spd = 250; });
            foreach (var (a, b) in new[]
                     {
                         (new[] { Carrier("a", RuleModifiers.BraveHeart, "fire") }, new[] { Carrier("b", RuleModifiers.GuardBreak, "water") }),
                         (Team("a_"), new[] { boss }),
                         (Team("a_"), Team("b_")),
                     })
            {
                var one = BattleSim.Resolve(r, a, b, 7);
                var two = BattleSim.Resolve(r, a, b, 7);
                Assert.That(one.Outcome, Is.Not.EqualTo(FightOutcome.Ongoing));
                Assert.That(one.Hash, Is.EqualTo(two.Hash));
            }
        }
    }

    static class FighterExt
    {
        public static Fighter Also(this Fighter f, Action<Fighter> a) { a(f); return f; }
    }
}
