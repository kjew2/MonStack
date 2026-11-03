using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Idea-pool lane D (#137): hemorrhage, privileged_status, equilibrium, corruption and dead_branch.</summary>
    public class LaneDRuleTests
    {
        static FightRules Rules => FightRules.FromContent(TestContent.Db);

        static readonly string[] LaneRules =
        {
            RuleModifiers.Hemorrhage, RuleModifiers.PrivilegedStatus, RuleModifiers.Equilibrium, RuleModifiers.Corruption, RuleModifiers.DeadBranch,
        };

        /// <summary>Always the same draw: 0 = every evade and crit roll succeeds, 0.99 = none does.</summary>
        sealed class Fixed : IRandomSource
        {
            readonly double _v;
            public Fixed(double v) { _v = v; }
            public int Draws;
            public double NextDouble() { Draws++; return _v; }
        }

        static Fighter Mon(string id, Action<MonsterStats> tweak = null, string element = null, params string[] rules)
        {
            var st = new MonsterStats { Hp = 300, Str = 300, Int = 300, Wis = 300, Dex = 300, Def = 300, Spd = 300, Luk = 0 };
            tweak?.Invoke(st);
            var f = new Fighter { Id = id, Stats = st, Element = element };
            f.Rules.AddRange(rules);
            return f;
        }

        /// <summary>A fast attacker: it acts many times before the slow, very tough target acts once.</summary>
        static Fighter Fast(string id, Action<MonsterStats> tweak = null, string element = null, params string[] rules) =>
            Mon(id, x => { x.Spd = 900; tweak?.Invoke(x); }, element, rules);

        static Fighter Wall(string id, Action<MonsterStats> tweak = null, string element = null, params string[] rules) =>
            Mon(id, x => { x.Spd = 10; x.Hp = 99999; tweak?.Invoke(x); }, element, rules);

        static CombatSkill Dmg(string id, string scales, double power, int cd = 0, string element = null, SkillTarget target = SkillTarget.Enemy) =>
            new CombatSkill { Id = id, Kind = SkillKind.Damage, Target = target, Scales = scales, Power = power, Cooldown = cd, Element = element };

        /// <summary>Steps until side A's first unit has taken <paramref name="actions"/> actions (side B must not act).</summary>
        static List<FightAction> Play(FightRules r, Fighter a, Fighter[] b, int actions, IRandomSource rng, Action<FightState> setup = null,
                                      Action<FightState> end = null)
        {
            var s = BattleSim.Start(r, new[] { a }, b);
            setup?.Invoke(s);
            var log = new List<FightAction>();
            while (s.Units[0].Tally.Actions < actions && s.Outcome == FightOutcome.Ongoing) BattleSim.Step(r, s, rng, log.Add);
            for (int i = 1; i < s.Units.Count; i++) Assert.That(s.Units[i].Tally.Actions, Is.EqualTo(0), "side B never acted");
            end?.Invoke(s);
            return log;
        }

        static List<FightAction> Play(FightRules r, Fighter a, Fighter b, int actions, double draw = 0.99, Action<FightState> setup = null,
                                      Action<FightState> end = null) =>
            Play(r, a, new[] { b }, actions, new Fixed(draw), setup, end);

        /// <summary>Side A's hits (basic attacks and skills, not rule events or counters).</summary>
        static List<FightAction> Hits(List<FightAction> log) => log.Where(x => x.Actor == "a" && x.Event == null && x.Target != null && !x.Counter).ToList();

        static int Count(List<FightAction> log, string evt) => log.Count(x => x.Event == evt);

        static FightEffect Debuff(string source, string stat = StatIds.Spd) => new FightEffect { Stat = stat, Frac = -0.1, TicksLeft = 100000, Source = source };

        // ------------------------------------------------------------------ content and constants

        [Test]
        public void Content_TreesAndConstants()
        {
            var db = TestContent.Db;
            var r = Rules;
            var k = db.ConstantValues;
            Assert.That((r.BleedPerHit, r.BleedDexRef, r.BleedBurst, r.PrivBonus, r.PrivDebuffs, r.EqBonus), Is.EqualTo((
                k["MON_BLEED_PER_HIT"], k["MON_BLEED_DEX_REF"], k["MON_BLEED_BURST"], k["MON_PRIV_BONUS"], (int)k["MON_PRIV_DEBUFFS"], k["MON_EQ_BONUS"])));
            var stats = new Dictionary<string, string>
            {
                [RuleModifiers.Hemorrhage] = StatIds.Dex, [RuleModifiers.PrivilegedStatus] = StatIds.Wis, [RuleModifiers.Equilibrium] = StatIds.Int,
                [RuleModifiers.Corruption] = StatIds.Wis, [RuleModifiers.DeadBranch] = StatIds.Luk,
            };
            foreach (var id in LaneRules)
            {
                Assert.That(RuleModifiers.IsKnown(id), id);
                var t = db.Trees[id];
                Assert.That((t.Kind, t.OfferStat, t.OfferThreshold, t.Nodes.Count), Is.EqualTo((TreeKind.Generic, stats[id], 400, 8)), id);
                var cap = t.Nodes[7];
                Assert.That((cap.Kind, cap.RuleId, cap.Cost), Is.EqualTo((NodeKind.Rule, id, 3)), id);
                Assert.That(t.Nodes[4].Kind, Is.EqualTo(NodeKind.Skill), id);
                Assert.That(t.Nodes[4].Skill.Kind, Is.Not.EqualTo(SkillKind.Passive), id);
                Assert.That(t.Nodes.Where(n => n.Kind == NodeKind.Growth || n.Kind == NodeKind.Stat).All(n => n.Stat == stats[id]), id + ": filler on the tree's own stat");
                Assert.That(db.Text("tree." + id + ".name"), Is.Not.EqualTo("tree." + id + ".name"), id);
                var desc = BattleText.RuleDesc(db, id);
                Assert.That(TextTemplate.HasPlaceholder(desc), Is.False, desc);
                Assert.That(db.Text("rule." + id + ".name"), Is.Not.EqualTo("rule." + id + ".name"), id);
            }
            // Privileged Status brings its own debuff; the cross-tree pair names each other.
            Assert.That(db.Trees[RuleModifiers.PrivilegedStatus].Nodes[4].Skill.Kind, Is.EqualTo(SkillKind.Debuff));
            Assert.That(BattleText.RuleDesc(db, RuleModifiers.Corruption), Does.Contain("Dead Branch"));
            Assert.That(BattleText.RuleDesc(db, RuleModifiers.DeadBranch), Does.Contain("Corruption"));
        }

        [Test]
        public void BranchPool_IsKitSkillsOfNativeGenericAndBookTrees()
        {
            var db = TestContent.Db;
            var pool = Rules.BranchPool;
            Assert.That(pool.Count, Is.GreaterThan(10));
            Assert.That(pool.Select(x => x.Id), Is.Ordered.Using((IComparer<string>)StringComparer.Ordinal), "sorted by id");
            Assert.That(pool.Select(x => x.Id).Distinct().Count(), Is.EqualTo(pool.Count));
            Assert.That(pool.All(x => x.Kind != SkillKind.Passive), "active skills only");
            var chase = db.TreeList.Where(t => t.Kind == TreeKind.Chase || t.Kind == TreeKind.Fusion)
                .SelectMany(t => t.Nodes).Where(n => n.Skill != null).Select(n => n.Skill.Id).ToHashSet();
            var kit = db.TreeList.Where(t => t.Kind == TreeKind.Native || t.Kind == TreeKind.Generic || t.Kind == TreeKind.Book)
                .SelectMany(t => t.Nodes).Where(n => n.Kind == NodeKind.Skill).Select(n => n.Skill.Id).ToHashSet();
            foreach (var sk in pool) Assert.That(kit.Contains(sk.Id), sk.Id + " is a kit skill");
            foreach (var id in new[] { "sovereign_flame", "star_lance", "overflow_beam", "boss_cleave", "boss_roar", "wildfire", "riposte" })
                Assert.That(pool.Any(x => x.Id == id), Is.False, id + " is not in the pool");
            Assert.That(chase.Contains("sovereign_flame"), "the chase skills exist");
            Assert.That(pool.Any(x => x.Id == "hex") && pool.Any(x => x.Id == "smash"), Is.True);
        }

        [Test]
        public void Validation_RejectsConstantsOutOfRange()
        {
            var a = new[] { Mon("a") };
            var b = new[] { Mon("b") };
            foreach (var bad in new Action<FightRules>[]
                     {
                         r => r.BleedPerHit = -1, r => r.BleedDexRef = 0, r => r.BleedBurst = 1.1, r => r.PrivBonus = double.NaN,
                         r => r.PrivDebuffs = 0, r => r.EqBonus = -0.1, r => r.BleedPerHit = double.PositiveInfinity,
                     })
            {
                var r = Rules;
                bad(r);
                Assert.Throws<ArgumentException>(() => BattleSim.Start(r, a, b));
            }
        }

        [Test]
        public void NoCarrier_NoLaneEventsOrState()
        {
            var log = new List<FightAction>();
            var a = Mon("a", null, "fire");
            a.Skills.Add(Dmg("bolt", StatIds.Str, 1.2, 2, "water"));
            var res = BattleSim.Resolve(Rules, new[] { a }, new[] { Mon("b", null, "water") }, 1, log.Add);
            Assert.That(log.Any(x => x.Event != null && LaneRules.Any(id => x.Event == FightAction.RulePrefix + id)), Is.False);
            foreach (var u in res.Final.Units)
                Assert.That((u.Bleed, u.EqMarks, u.Locked, u.Branched, u.Source.Skills.Count), Is.EqualTo((0.0, (SortedDictionary<string, string>)null, (List<int>)null, (List<string>)null, u.Id == "a" ? 1 : 0)));
        }

        // ------------------------------------------------------------------ hemorrhage

        [Test]
        public void Hemorrhage_FillsByDex_BurstsAtFull_AndResets()
        {
            var r = Rules;
            double fill = r.BleedPerHit * 300 / r.BleedDexRef;
            int toBurst = (int)Math.Ceiling(BattleSim.BleedGauge / fill);
            FightState final = null;
            var log = Play(r, Fast("a", null, null, RuleModifiers.Hemorrhage), Wall("b"), toBurst + 1, end: s => final = s);
            var bursts = log.Where(x => x.Event == BattleSim.BleedEvent).ToList();
            Assert.That(bursts.Count, Is.EqualTo(1));
            var b = final.Units[1];
            Assert.That(bursts[0].Target, Is.EqualTo("b"));
            Assert.That(bursts[0].Amount, Is.EqualTo(r.BleedBurst * b.MaxHp).Within(1e-6), "true damage: no mitigation");
            int burstAt = log.FindIndex(x => x.Event == BattleSim.BleedEvent);
            Assert.That(log.Take(burstAt).Count(x => x.Actor == "a" && x.Event == null && x.Target == "b"), Is.EqualTo(toBurst), "after the hit that filled it");
            Assert.That(log[burstAt - 1].Event, Is.Null, "right after it");
            Assert.That(b.Bleed, Is.EqualTo(fill).Within(1e-9), "reset to 0, then one more hit");
            Assert.That(b.Tally.DamageTaken, Is.EqualTo(Hits(log).Sum(x => x.Amount) + bursts[0].Amount).Within(1e-6));

            // Twice the DEX fills twice as fast; no DEX fills nothing; no rule, no gauge.
            Play(r, Fast("a", x => x.Dex = 600, null, RuleModifiers.Hemorrhage), Wall("b"), 1, end: s => Assert.That(s.Units[1].Bleed, Is.EqualTo(2 * fill).Within(1e-9)));
            Play(r, Fast("a", x => x.Dex = 0, null, RuleModifiers.Hemorrhage), Wall("b"), 30, end: s => Assert.That(s.Units[1].Bleed, Is.EqualTo(0)));
            Play(r, Fast("a"), Wall("b"), 30, end: s => Assert.That(s.Units[1].Bleed, Is.EqualTo(0)));
        }

        [Test]
        public void Hemorrhage_EvadedHitsDoNotFill_ShieldsTakeTheBurst()
        {
            var r = Rules;
            // Every evade roll succeeds at draw 0: physical hits miss and fill nothing.
            Play(r, Fast("a", null, null, RuleModifiers.Hemorrhage), Wall("b"), 20, draw: 0, end: s => Assert.That(s.Units[1].Bleed, Is.EqualTo(0)));
            // A big shield absorbs the hits and the burst alike; the gauge still fills (the hits landed).
            double fill = r.BleedPerHit * 300 / r.BleedDexRef;
            int toBurst = (int)Math.Ceiling(BattleSim.BleedGauge / fill);
            var log = Play(r, Fast("a", null, null, RuleModifiers.Hemorrhage), Wall("b"), toBurst, setup: s => s.Units[1].Shield = 1e9,
                end: s => Assert.That(s.Units[1].Hp, Is.EqualTo(s.Units[1].MaxHp)));
            Assert.That(log.Single(x => x.Event == BattleSim.BleedEvent).Amount, Is.EqualTo(0), "the shield took it");
        }

        [Test]
        public void Hemorrhage_BurstCanKill_AndTheGaugeSurvivesBossPhases()
        {
            var r = Rules;
            double fill = r.BleedPerHit * 300 / r.BleedDexRef;
            int toBurst = (int)Math.Ceiling(BattleSim.BleedGauge / fill);
            var log = new List<FightAction>();
            var s = BattleSim.Start(r, new[] { Fast("a", null, null, RuleModifiers.Hemorrhage) }, new[] { Wall("b") });
            s.Units[1].Bleed = BattleSim.BleedGauge - 0.5;
            s.Units[1].Hp = s.Units[1].MaxHp * 0.1;   // far more than a basic attack deals, less than the burst
            while (s.Outcome == FightOutcome.Ongoing && s.Units[0].Tally.Actions < 1) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(s.Units[1].Alive, Is.False, "the burst (15% of max HP) finished a target at 10%");
            Assert.That(s.Outcome, Is.EqualTo(FightOutcome.SideAWon));
            Assert.That(s.Units[0].Tally.Kills, Is.EqualTo(1));

            // 4v1 in the Dungeon: the boss's gauge is kept when it enters a phase.
            var tr = FightRules.FromContent(TestContent.Db, tower: true);
            var boss = Mon("boss", x => { x.Hp = 3000; x.Spd = 10; }, "dark");
            boss.Phases.Add(new BossPhase { EnterHp = 0.9, Kind = PhaseKinds.Enrage, Buff = 0.05 });
            var team = Enumerable.Range(0, 4).Select(i => Fast("a" + i, null, null, RuleModifiers.Hemorrhage)).ToArray();
            var ts = BattleSim.Start(tr, team, new[] { boss });
            var tlog = new List<FightAction>();
            double bleedAtPhase = -1;
            var rng = new RngStream(BattleSim.StreamName, 3);
            while (ts.Outcome == FightOutcome.Ongoing)
            {
                int before = tlog.Count;
                BattleSim.Step(tr, ts, rng, tlog.Add);
                if (bleedAtPhase < 0 && tlog.Skip(before).Any(x => x.Event == "phase:enrage")) bleedAtPhase = ts.Units[4].Bleed;
            }
            Assert.That(bleedAtPhase, Is.GreaterThan(0), "the gauge was not reset by the phase");
            Assert.That(Count(tlog, BattleSim.BleedEvent), Is.GreaterThan(0));
            Assert.That(ts.Outcome, Is.EqualTo(FightOutcome.SideAWon));
        }

        // ------------------------------------------------------------------ privileged_status

        [Test]
        public void PrivilegedStatus_BonusOnlyAtEnoughDebuffs_StackedCopiesCount()
        {
            var r = Rules;
            double Hit(bool rule, int debuffs, bool sameSource, out int events)
            {
                var a = Fast("a", null, null, rule ? new[] { RuleModifiers.PrivilegedStatus } : new string[0]);
                var log = Play(r, a, Wall("b"), 1, setup: s =>
                {
                    for (int i = 0; i < debuffs; i++) s.Units[1].Effects.Add(Debuff(sameSource ? "hex" : "d" + i));
                });
                events = Count(log, BattleSim.PrivEvent);
                return Hits(log)[0].Amount;
            }
            double plain = Hit(false, 2, false, out _);
            Assert.That(Hit(true, 1, false, out int e1), Is.EqualTo(plain).Within(1e-9), "one debuff: no bonus");
            Assert.That(e1, Is.EqualTo(0));
            Assert.That(Hit(true, 2, false, out int e2), Is.EqualTo(plain * (1 + r.PrivBonus)).Within(1e-6), "two debuffs");
            Assert.That(e2, Is.EqualTo(1));
            Assert.That(Hit(true, 2, true, out _), Is.EqualTo(plain * (1 + r.PrivBonus)).Within(1e-6), "two stacked copies of one debuff");

            // Buffs on the target are not debuffs; BattleSim.DebuffCount counts negative effects only.
            var u = new FightUnit();
            u.Effects.Add(new FightEffect { Stat = StatIds.Def, Frac = 0.3, TicksLeft = 5, Source = "iron_hide" });
            u.Effects.Add(Debuff("confuse", StatIds.Dex));
            u.Effects.Add(Debuff("confuse", StatIds.Dex));
            Assert.That(BattleSim.DebuffCount(u), Is.EqualTo(2));
        }

        [Test]
        public void PrivilegedStatus_WithItsOwnHex_AndCorruptionReachesTwo()
        {
            // The tree's own debuff skill, corrupted, is cast twice: two copies, so the next hits get the bonus.
            var r = Rules;
            var hex = TestContent.Db.MonsterSkills["hex"];
            var a = Fast("a", null, null, RuleModifiers.PrivilegedStatus, RuleModifiers.Corruption);
            a.Skills.Add(hex);
            FightState final = null;
            var log = Play(r, a, Wall("b"), 3, end: s => final = s);
            Assert.That(BattleSim.DebuffCount(final.Units[1]), Is.EqualTo(2));
            Assert.That(Count(log, BattleSim.PrivEvent), Is.EqualTo(2), "both basic attacks after the hex");
        }

        // ------------------------------------------------------------------ equilibrium

        [Test]
        public void Equilibrium_AlternatingElementsPayEveryOtherHit()
        {
            var r = Rules;
            // A fire skill every other action, a water basic attack between: fire marks, water consumes, fire marks again...
            Fighter A(bool rule)
            {
                var f = Fast("a", null, "water", rule ? new[] { RuleModifiers.Equilibrium } : new string[0]);
                f.Skills.Add(Dmg("fire_bolt", StatIds.Str, 1.0, 2, "fire"));
                return f;
            }
            var on = Play(r, A(true), Wall("b"), 4);
            var off = Play(r, A(false), Wall("b"), 4);
            var hitsOn = Hits(on);
            var hitsOff = Hits(off);
            Assert.That(hitsOn.Select(x => x.Skill?.Id), Is.EqualTo(new[] { "fire_bolt", null, "fire_bolt", null }));
            Assert.That(Count(on, BattleSim.EqEvent), Is.EqualTo(2));
            for (int i = 0; i < 4; i++)
                Assert.That(hitsOn[i].Amount, Is.EqualTo(hitsOff[i].Amount * (i % 2 == 1 ? 1 + r.EqBonus : 1)).Within(1e-6), "hit " + i);

            // The same element twice keeps the mark and gets nothing; elementless hits neither mark nor consume.
            var same = Play(r, Fast("a", null, "water", RuleModifiers.Equilibrium), Wall("b"), 5,
                end: s => Assert.That(s.Units[0].EqMarks["b"], Is.EqualTo("water")));
            Assert.That(Count(same, BattleSim.EqEvent), Is.EqualTo(0));
            Play(r, Fast("a", null, null, RuleModifiers.Equilibrium), Wall("b"), 5, end: s => Assert.That(s.Units[0].EqMarks, Is.Null));
        }

        [Test]
        public void Equilibrium_MarksArePerTarget_InTeamFights()
        {
            var r = Rules;
            // An all-enemies fire skill marks both targets; the single-target water basic attack consumes only its target's mark.
            var a = Fast("a", null, "water", RuleModifiers.Equilibrium);
            a.Skills.Add(Dmg("fire_wave", StatIds.Str, 0.6, 2, "fire", SkillTarget.AllEnemies));
            FightState final = null;
            var log = Play(r, a, new[] { Wall("b0"), Wall("b1") }, 2, new Fixed(0.99), end: s => final = s);
            var eq = log.Single(x => x.Event == BattleSim.EqEvent);
            string other = eq.Target == "b0" ? "b1" : "b0";
            Assert.That(final.Units[0].EqMarks.ContainsKey(eq.Target), Is.False, "consumed");
            Assert.That(final.Units[0].EqMarks[other], Is.EqualTo("fire"), "the other target keeps its mark");
        }

        // ------------------------------------------------------------------ corruption

        [Test]
        public void Corruption_CastsTwiceAtFullPower_ThenLocks()
        {
            var r = Rules;
            Fighter A(params string[] rules)
            {
                var f = Fast("a", null, null, rules);
                f.Skills.Add(Dmg("big", StatIds.Str, 2.0, 0));
                return f;
            }
            FightState final = null;
            var log = Play(r, A(RuleModifiers.Corruption), Wall("b"), 4, end: s => final = s);
            var hits = Hits(log);
            Assert.That(hits.Select(x => x.Skill?.Id), Is.EqualTo(new[] { "big", "big", null, null, null }), "twice, then basic attacks only");
            Assert.That(hits[1].Amount, Is.EqualTo(hits[0].Amount).Within(1e-9), "the second cast at full power");
            var ev = log.Single(x => x.Event == BattleSim.CorruptEvent);
            Assert.That(ev.Amount, Is.EqualTo(1));
            int evAt = log.FindIndex(x => x.Event == BattleSim.CorruptEvent);
            Assert.That(log.Take(evAt).Count(x => x.Skill?.Id == "big"), Is.EqualTo(1), "logged between the two casts");
            Assert.That(log[evAt + 1].Skill?.Id, Is.EqualTo("big"));
            Assert.That(final.Units[0].SkillLocked(0), Is.True);
            Assert.That(final.Units[0].Tally.Actions, Is.EqualTo(4), "the second cast is not an action");

            // Without the rule the skill is cast every action.
            var plain = Hits(Play(r, A(), Wall("b"), 4));
            Assert.That(plain.All(x => x.Skill?.Id == "big"), Is.True);
        }

        [Test]
        public void Corruption_AndEchoCast_NeitherRepeatsTheOther()
        {
            var r = Rules;
            var a = Fast("a", null, null, RuleModifiers.Corruption, RuleModifiers.EchoCast);
            a.Skills.Add(Dmg("big", StatIds.Str, 2.0, 0));
            var log = Play(r, a, Wall("b"), 2);
            // Cast, its echo, then corruption's second cast: three, never four.
            Assert.That(Hits(log).Count(x => x.Skill?.Id == "big"), Is.EqualTo(3));
            Assert.That(Count(log, BattleSim.CorruptEvent), Is.EqualTo(1));
        }

        [Test]
        public void Corruption_LockedSkill_IsSkippedByGambits_AndTriggersStillFire()
        {
            var r = Rules;
            var a = Fast("a", null, null, RuleModifiers.Corruption);
            a.Skills.Add(Dmg("big", StatIds.Str, 2.0, 0));
            a.Gambits.Add(new GambitRule(GambitConditions.Always, 0, "big"));
            a.Triggers.Add(new TriggeredSkill(FightTrigger.OnBasicAttack, Dmg("jab", StatIds.Str, 0.2)));
            var log = Play(r, a, Wall("b"), 3);
            var hits = Hits(log);
            Assert.That(hits.Count(x => x.Skill?.Id == "big"), Is.EqualTo(2));
            Assert.That(hits.Count(x => x.Skill?.Id == "jab"), Is.EqualTo(2), "a triggered skill is not cast as an action: never corrupted or locked");
            Assert.That(Count(log, FightAction.GambitEvent), Is.EqualTo(1), "the rule no longer fires once its skill is locked");
        }

        // ------------------------------------------------------------------ dead_branch

        [Test]
        public void DeadBranch_EachLockDrawsASkillItLacks()
        {
            var r = Rules;
            var a = Fast("a", null, null, RuleModifiers.Corruption, RuleModifiers.DeadBranch);
            a.Skills.Add(Dmg("big", StatIds.Str, 2.0, 0));
            var rng = new Fixed(0);
            FightState final = null;
            var log = Play(r, a, new[] { Wall("b") }, 1, rng, end: s => final = s);
            var u = final.Units[0];
            var drawn = log.Single(x => x.Event == BattleSim.BranchEvent);
            Assert.That(drawn.Amount, Is.EqualTo(1), "joins the end of the list");
            Assert.That(u.Source.Skills.Count, Is.EqualTo(2));
            Assert.That(u.Cooldowns.Length, Is.EqualTo(2));
            Assert.That(u.Source.Skills[1].Id, Is.EqualTo(r.BranchPool[0].Id), "draw 0 picks the first pool skill");
            Assert.That(u.Branched, Is.EqualTo(new[] { r.BranchPool[0].Id }));
            Assert.That(a.Skills.Count, Is.EqualTo(1), "the fighter snapshot is not changed");

            // At draw 0.99 the last pool skill the unit lacks; a skill it already has is never drawn.
            var b = Fast("a", null, null, RuleModifiers.Corruption, RuleModifiers.DeadBranch);
            var last = r.BranchPool[r.BranchPool.Count - 1];
            b.Skills.Add(Dmg("big", StatIds.Str, 2.0, 0));
            b.Skills.Add(last);
            Play(r, b, new[] { Wall("b") }, 1, new Fixed(0.99), end: s =>
                Assert.That(s.Units[0].Source.Skills.Last().Id, Is.EqualTo(r.BranchPool[r.BranchPool.Count - 2].Id)));
        }

        [Test]
        public void DeadBranch_ChainsWithCorruption_UntilThePoolIsDry()
        {
            var r = Rules;
            r.BranchPool = new[] { Dmg("p1", StatIds.Str, 1.0), Dmg("p2", StatIds.Str, 1.0) };
            var a = Fast("a", null, null, RuleModifiers.Corruption, RuleModifiers.DeadBranch);
            a.Skills.Add(Dmg("big", StatIds.Str, 2.0, 0));
            FightState final = null;
            var log = Play(r, a, new[] { Wall("b") }, 5, new Fixed(0.5), end: s => final = s);
            Assert.That(Count(log, BattleSim.BranchEvent), Is.EqualTo(2), "one draw per lock while the pool lasts");
            Assert.That(Count(log, BattleSim.CorruptEvent), Is.EqualTo(3), "each drawn skill is corrupted and locked too");
            Assert.That(final.Units[0].Locked, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(Hits(log).Skip(6).All(x => x.Skill == null), Is.True, "then basic attacks");
        }

        [Test]
        public void DeadBranch_AloneOrWithAnEmptyPool_DoesNothingAndDrawsNothing()
        {
            var r = Rules;
            Fighter A(params string[] rules)
            {
                var f = Fast("a", null, null, rules);
                f.Skills.Add(Dmg("big", StatIds.Str, 2.0, 1));
                return f;
            }
            // No lock source: nothing to do (Dead Branch needs Corruption).
            var lone = Play(r, A(RuleModifiers.DeadBranch), Wall("b"), 6);
            Assert.That(Count(lone, BattleSim.BranchEvent), Is.EqualTo(0));

            // Empty pool: the lock happens, nothing is drawn (the same draws as corruption alone).
            r.BranchPool = Array.Empty<CombatSkill>();
            var withRng = new Fixed(0.99);
            var plainRng = new Fixed(0.99);
            var withLog = Play(r, A(RuleModifiers.Corruption, RuleModifiers.DeadBranch), new[] { Wall("b") }, 4, withRng);
            Play(r, A(RuleModifiers.Corruption), new[] { Wall("b") }, 4, plainRng);
            Assert.That(Count(withLog, BattleSim.BranchEvent), Is.EqualTo(0));
            Assert.That(withRng.Draws, Is.EqualTo(plainRng.Draws));

            // With a pool, the carrier draws exactly once more per lock.
            var pr = Rules;
            var drawRng = new Fixed(0.99);
            var noRng = new Fixed(0.99);
            var a = Fast("a", x => x.Luk = 0, null, RuleModifiers.Corruption, RuleModifiers.DeadBranch);
            a.Skills.Add(Dmg("big", StatIds.Str, 2.0, 1));
            Play(pr, a, new[] { Wall("b") }, 1, drawRng);
            Play(pr, A(RuleModifiers.Corruption), new[] { Wall("b") }, 1, noRng);
            Assert.That(drawRng.Draws, Is.EqualTo(noRng.Draws + 1));
        }

        // ------------------------------------------------------------------ state: clone and hash

        [Test]
        public void Clone_CopiesLaneState_AndContinuesIdentically()
        {
            var r = Rules;
            var a = Fast("a", null, "water", RuleModifiers.Hemorrhage, RuleModifiers.Equilibrium, RuleModifiers.Corruption, RuleModifiers.DeadBranch);
            a.Skills.Add(Dmg("fire_bolt", StatIds.Str, 1.0, 2, "fire"));
            var s = BattleSim.Start(r, new[] { a }, new[] { Mon("b", x => x.Hp = 2000, "nature") });
            var rng = new RngStream(BattleSim.StreamName, 11);
            while (s.Units[0].Tally.Actions < 3 && s.Outcome == FightOutcome.Ongoing) BattleSim.Step(r, s, rng, null);
            Assert.That(s.Units[0].Locked, Is.Not.Null);
            var copy = s.Clone();
            Assert.That(copy.Units[0].Locked, Is.Not.SameAs(s.Units[0].Locked));
            Assert.That(copy.Units[0].Branched, Is.Not.SameAs(s.Units[0].Branched));
            Assert.That(BattleSim.StateHash(copy), Is.EqualTo(BattleSim.StateHash(s)));
            var rngCopy = new RngStream(BattleSim.StreamName, 99);
            var rngOrig = new RngStream(BattleSim.StreamName, 99);
            while (BattleSim.Step(r, s, rngOrig) == FightOutcome.Ongoing) { }
            while (BattleSim.Step(r, copy, rngCopy) == FightOutcome.Ongoing) { }
            Assert.That(BattleSim.StateHash(copy), Is.EqualTo(BattleSim.StateHash(s)));
        }

        [Test]
        public void Hash_CoversLaneState_OnlyWithCarriers()
        {
            var r = Rules;
            string Hash(bool carrier, Action<FightState> poke)
            {
                var s = BattleSim.Start(r, new[] { Mon("a", null, null, carrier ? LaneRules : new string[0]) }, new[] { Mon("b") });
                poke?.Invoke(s);
                return BattleSim.StateHash(s);
            }
            Assert.That(Hash(false, s => s.Units[1].Bleed = 5), Is.EqualTo(Hash(false, null)), "no carrier: not hashed");
            Assert.That(Hash(true, s => s.Units[1].Bleed = 5), Is.Not.EqualTo(Hash(true, null)));
            Assert.That(Hash(true, s => s.Units[0].Locked = new List<int> { 0 }), Is.Not.EqualTo(Hash(true, null)));
            Assert.That(Hash(true, s => s.Units[0].EqMarks = new SortedDictionary<string, string> { ["b"] = "fire" }), Is.Not.EqualTo(Hash(true, null)));
            Assert.That(Hash(true, s => s.Units[0].Branched = new List<string> { "smash" }), Is.Not.EqualTo(Hash(true, null)));
        }

        // ------------------------------------------------------------------ modes

        [Test]
        public void Modes_1v1_4v1_4v4_EveryRuleProcs()
        {
            // 1v1 (run fights, gauntlet), 4v1 (Dungeon) and 4v4 (Endless, PvP): every carrier logs its rule and the fight ends.
            var r = Rules;
            var hex = TestContent.Db.MonsterSkills["hex"];
            Fighter Carrier(string id, string rule)
            {
                var f = Mon(id, x => x.Spd = 400, "fire", rule == RuleModifiers.DeadBranch ? new[] { rule, RuleModifiers.Corruption } : new[] { rule });
                f.Skills.Add(Dmg("blast", StatIds.Int, 0.8, 3, "water", SkillTarget.AllEnemies));
                f.Skills.Add(hex);
                f.Skills.Add(new CombatSkill { Id = "confuse", Kind = SkillKind.Debuff, Target = SkillTarget.AllEnemies, Stat = StatIds.Dex, Power = 0.2, Cooldown = 3, Duration = 3 });
                return f;
            }
            Fighter Foe(string id) => Mon(id, x => x.Hp = 1500, "nature");
            var team = LaneRules.Take(4).Select((id, i) => Carrier("a" + i, id)).ToArray();
            var team2 = new[] { Carrier("a0", RuleModifiers.DeadBranch), Carrier("a1", RuleModifiers.Hemorrhage) };
            var cases = new (Fighter[] a, Fighter[] b)[]
            {
                (new[] { Carrier("a0", RuleModifiers.Hemorrhage) }, new[] { Foe("b0") }),
                (new[] { Carrier("a0", RuleModifiers.DeadBranch) }, new[] { Foe("b0") }),
                (team, new[] { Mon("boss", x => x.Hp = 6000, "dark") }),
                (team, Enumerable.Range(0, 4).Select(i => Foe("b" + i)).ToArray()),
                (team2, Enumerable.Range(0, 4).Select(i => Foe("b" + i)).ToArray()),
            };
            foreach (var (a, b) in cases)
            {
                var log = new List<FightAction>();
                var res = BattleSim.Resolve(r, a, b, 7, log.Add);
                Assert.That(res.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut));
                foreach (var f in a)
                    Assert.That(log.Any(x => x.Actor == f.Id && x.Event == FightAction.RulePrefix + f.Rules[0]), Is.True, f.Rules[0] + " in " + a.Length + "v" + b.Length);
            }
        }
    }
}
