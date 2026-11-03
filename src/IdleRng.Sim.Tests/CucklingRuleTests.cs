using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Species Cuckling (#143): its kit in content and its three rules, stolen_turn, usurp and foundling_egg.</summary>
    public class CucklingRuleTests
    {
        static FightRules Rules => FightRules.FromContent(TestContent.Db);

        static readonly string[] SpeciesRules = { RuleModifiers.StolenTurn, RuleModifiers.Usurp, RuleModifiers.FoundlingEgg };

        /// <summary>Always the same draw: 0.99 = no evade or crit roll succeeds (and a random pick takes the last unit).</summary>
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

        /// <summary>A thief at SPD 600 against a foe at SPD 1000 (gauge 1000): the foe's turn comes on tick 1, the thief's gauge is 60% full.</summary>
        static Fighter Thief(string id, params string[] rules) => Mon(id, x => { x.Spd = 600; x.Hp = 3000; }, rules);

        static Fighter Quick(string id, Action<MonsterStats> tweak = null) => Mon(id, x => { x.Spd = 1000; x.Hp = 3000; tweak?.Invoke(x); });

        static CombatSkill Bolt(string id, int cooldown) =>
            new CombatSkill { Id = id, Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = StatIds.Str, Power = 1.5, Cooldown = cooldown };

        static (FightState S, List<FightAction> Log) Ticks(FightRules r, Fighter[] a, Fighter[] b, int ticks)
        {
            var s = BattleSim.Start(r, a, b);
            var log = new List<FightAction>();
            for (int i = 0; i < ticks && s.Outcome == FightOutcome.Ongoing; i++) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            return (s, log);
        }

        // ------------------------------------------------------------------ content

        [Test]
        public void Content_SpeciesKit()
        {
            var db = TestContent.Db;
            var sp = db.Species["cuckling"];
            Assert.That((sp.Element, sp.NativeTree1, sp.NativeTree2, sp.ChaseTreeRun, sp.ChaseTreeBook),
                        Is.EqualTo(("dark", "nest_thief", "mimic_core", "usurper_cuckoo", "clutch_empress")));
            Assert.That(db.SpeciesList[0].Id, Is.Not.EqualTo("cuckling"), "the starter stays first");
            Assert.That(db.SpeciesList.Max(s => s.Base.Luk), Is.EqualTo(sp.Base.Luk), "the highest base LUK of any species");

            // mimic_core is Cuckling's Brood Nest, native-only now (kj, 2026-11-03): never in an Awakening draft.
            var nest = db.Trees["mimic_core"];
            Assert.That((nest.Kind, nest.OfferStat, nest.OfferThreshold), Is.EqualTo((TreeKind.Native, (string)null, 0)));
            Assert.That(db.SpeciesList.Count(s => s.NativeTree1 == "mimic_core" || s.NativeTree2 == "mimic_core"), Is.EqualTo(1));
            Assert.That(db.Text(nest.NameKey), Is.EqualTo("Brood Nest"));
            Assert.That((nest.Nodes[4].Skill.Id, nest.Nodes[7].RuleId), Is.EqualTo(("windfall", RuleModifiers.MimicCore)));

            var thief = db.Trees["nest_thief"];
            Assert.That((thief.Kind, thief.Nodes[4].Skill.Id, thief.Nodes[7].RuleId, thief.Nodes[7].Cost),
                        Is.EqualTo((TreeKind.Native, "coin_toss", RuleModifiers.StolenTurn, 3)));

            var usurper = db.Trees["usurper_cuckoo"];
            var empress = db.Trees["clutch_empress"];
            Assert.That((usurper.Kind, usurper.Species, usurper.BookGated, usurper.Nodes.Last().RuleId),
                        Is.EqualTo((TreeKind.Chase, "cuckling", false, RuleModifiers.Usurp)));
            Assert.That(usurper.Conditions.Select(c => c.Ref), Is.EquivalentTo(new[] { "nest_thief", "spd", "kills" }));
            Assert.That((empress.Kind, empress.Species, empress.BookGated, empress.SecondChase, empress.Nodes.Last().RuleId),
                        Is.EqualTo((TreeKind.Chase, "cuckling", true, true, RuleModifiers.FoundlingEgg)));
            Assert.That(empress.Conditions.Select(c => c.Ref), Is.EquivalentTo(new[] { "foundling_ledger_n0", "mimic_core_n4", "damage_dark" }));
            Assert.That(db.Trees["foundling_ledger"].Kind, Is.EqualTo(TreeKind.Book));
            Assert.That(db.SkillBooks.Values.Count(b => b.TreeId == "foundling_ledger"), Is.EqualTo(1));
            Assert.That(db.BookDrops.Count(d => db.SkillBooks[d.BookId].TreeId == "foundling_ledger"), Is.EqualTo(1));
            foreach (var t in new[] { usurper, empress }) Assert.That(db.Text(t.ApexNameKey), Is.Not.EqualTo(t.ApexNameKey), t.Id);
            Assert.That(db.Achievements.Values.Count(a => a.UnlocksSpecies == "cuckling"), Is.EqualTo(1));

            var r = Rules;
            var k = db.ConstantValues;
            Assert.That((double)r.UsurpSteals, Is.EqualTo(k["MON_USURP_STEALS"]));
            Assert.That((r.StealGauge, (double)r.StealsPerFoe, r.EggHp, (double)r.HatchActions, r.HatchStats),
                        Is.EqualTo((k["MON_STEAL_GAUGE"], k["MON_STEALS_PER_FOE"], k["MON_EGG_HP"], k["MON_HATCH_ACTIONS"], k["MON_HATCH_STATS"])));
            foreach (var id in SpeciesRules)
            {
                Assert.That(RuleModifiers.IsKnown(id), id);
                var desc = BattleText.RuleDesc(db, id);
                Assert.That(TextTemplate.HasPlaceholder(desc), Is.False, desc);
                Assert.That(db.Text("rule." + id + ".name"), Is.Not.EqualTo("rule." + id + ".name"), id);
            }
        }

        [Test]
        public void Validation_RejectsConstantsOutOfRange()
        {
            var a = new[] { Mon("a") };
            var b = new[] { Mon("b") };
            foreach (var bad in new Action<FightRules>[]
                     {
                         r => r.StealGauge = -0.1, r => r.StealGauge = 1.1, r => r.StealGauge = double.NaN, r => r.StealsPerFoe = 0, r => r.UsurpSteals = 0,
                         r => r.EggHp = 0, r => r.EggHp = 1.5, r => r.HatchActions = 0, r => r.HatchStats = 0, r => r.HatchStats = 2,
                     })
            {
                var r = Rules;
                bad(r);
                Assert.Throws<ArgumentException>(() => BattleSim.Start(r, a, b));
            }
        }

        // ------------------------------------------------------------------ stolen_turn

        [Test]
        public void StolenTurn_TakesTheEnemysTurn_OncePerFoe()
        {
            var r = Rules;
            var (s, log) = Ticks(r, new[] { Thief("a", RuleModifiers.StolenTurn) }, new[] { Quick("b") }, 1);
            var steal = log.Single(x => x.Event == BattleSim.StealEvent);
            Assert.That((steal.Actor, steal.Target, steal.Amount), Is.EqualTo(("a", "b", 0.0)), "no steals left on b");
            Assert.That((s.Units[0].Tally.Actions, s.Units[1].Tally.Actions), Is.EqualTo((1, 0)), "a acted in b's place");
            Assert.That((s.Units[0].Gauge, s.Units[1].Gauge), Is.EqualTo((0.0, 0.0)), "b's turn is spent; a's gauge is empty");
            Assert.That(s.Units[0].StolenFrom, Is.EqualTo(new[] { "b" }));

            // Tick 2: b's turn comes again with a's gauge at 60%, but a has stolen from b already.
            BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(log.Count(x => x.Event == BattleSim.StealEvent), Is.EqualTo(1));
            Assert.That(s.Units[1].Tally.Actions, Is.EqualTo(1));

            // MON_STEALS_PER_FOE 2: the second turn is stolen too.
            r.StealsPerFoe = 2;
            (s, log) = Ticks(r, new[] { Thief("a", RuleModifiers.StolenTurn) }, new[] { Quick("b") }, 2);
            Assert.That(log.Where(x => x.Event == BattleSim.StealEvent).Select(x => x.Amount), Is.EqualTo(new[] { 1.0, 0.0 }));
            Assert.That(s.Units[1].Tally.Actions, Is.EqualTo(0));

            // Without the rule, or under MON_STEAL_GAUGE: b takes its turn.
            (s, log) = Ticks(Rules, new[] { Thief("a") }, new[] { Quick("b") }, 1);
            Assert.That((s.Units[0].Tally.Actions, s.Units[1].Tally.Actions), Is.EqualTo((0, 1)));
            var low = Mon("a", x => { x.Spd = 400; x.Hp = 3000; }, RuleModifiers.StolenTurn);
            (s, log) = Ticks(Rules, new[] { low }, new[] { Quick("b") }, 1);
            Assert.That(log.Any(x => x.Event == BattleSim.StealEvent), Is.False, "40% is under the 50% gauge");
            Assert.That(s.Units[1].Tally.Actions, Is.EqualTo(1));
        }

        [Test]
        public void StolenTurn_OnePerEnemy_In4v4_TheFullestGaugeSteals()
        {
            var r = Rules;
            var a = new[] { Thief("a1", RuleModifiers.StolenTurn), Mon("a2", x => { x.Spd = 700; x.Hp = 3000; }, RuleModifiers.StolenTurn) };
            var b = new[] { Quick("b1"), Quick("b2") };
            var (s, log) = Ticks(r, a, b, 1);
            var steals = log.Where(x => x.Event == BattleSim.StealEvent).Select(x => (x.Actor, x.Target)).ToList();
            Assert.That(steals, Is.EqualTo(new[] { ("a2", "b1"), ("a1", "b2") }), "a2's 70% beats a1's 60%; then a1 takes the next");
            Assert.That(s.Units.Where(u => u.Side == 1).Sum(u => u.Tally.Actions), Is.EqualTo(0), "both enemy turns were lost");
        }

        [Test]
        public void StolenTurn_IsTheTurnsOnlyAction_NoExtraActionNoOneMoreRefund_NotStolenBack()
        {
            var r = Rules;
            // double_turn_start would follow the unit's first action with another; one_more would refund on a strong-element hit.
            var a = Thief("a", RuleModifiers.StolenTurn, RuleModifiers.DoubleTurnStart, RuleModifiers.OneMore);
            a.Element = "dark";
            var b = Quick("b", x => x.Str = 0);
            b.Element = "light";
            b.Rules.Add(RuleModifiers.StolenTurn);   // b steals too, but a stolen action is never stolen back
            var (s, log) = Ticks(r, new[] { a }, new[] { b }, 1);
            Assert.That(log.Count(x => x.Event == BattleSim.StealEvent), Is.EqualTo(1));
            Assert.That(s.Units[0].Tally.Actions, Is.EqualTo(1), "no double_turn_start extra action after a stolen one");
            Assert.That(log.Any(x => x.Event == BattleSim.OneMoreEvent), Is.False, "one_more refunds nothing inside a stolen turn");
            Assert.That(s.Units[0].Gauge, Is.EqualTo(0));

            // The same unit on its own turn: the extra action and the refund both come.
            a.Rules.Remove(RuleModifiers.StolenTurn);
            b.Rules.Remove(RuleModifiers.StolenTurn);
            var slow = Quick("b", x => { x.Str = 0; x.Spd = 10; });
            slow.Element = "light";
            (s, log) = Ticks(r, new[] { a }, new[] { slow }, 2);
            Assert.That(log.Any(x => x.Event == FightAction.DoubleTurnEvent), Is.True);
            Assert.That(log.Any(x => x.Event == BattleSim.OneMoreEvent), Is.True);
        }

        // ------------------------------------------------------------------ usurp

        [Test]
        public void Usurp_CastsTheVictimsReadySkill_WithItsOwnStats_AndPutsItOnCooldown()
        {
            var r = Rules;
            var b = Quick("b");
            b.Skills.Add(Bolt("fang", 4));
            var (s, log) = Ticks(r, new[] { Thief("a", RuleModifiers.StolenTurn, RuleModifiers.Usurp) }, new[] { b }, 1);
            var u = log.Single(x => x.Event == BattleSim.UsurpEvent);
            Assert.That((u.Actor, u.Target, u.Amount), Is.EqualTo(("a", "b", 1.0)));
            var cast = log.Single(x => x.Skill?.Id == "fang");
            Assert.That((cast.Actor, cast.Target), Is.EqualTo(("a", "b")), "cast by the thief on the victim's side");
            Assert.That(s.Units[1].Cooldowns[0], Is.EqualTo(4), "the victim's copy goes on cooldown");
            Assert.That(s.Units[0].Source.Skills, Is.Empty, "the thief's kit is unchanged");

            // The thief's stats: double its STR, double the damage (no crit, no evade, same mitigation).
            var strong = Thief("a", RuleModifiers.StolenTurn, RuleModifiers.Usurp);
            strong.Stats.Str *= 2;
            var b2 = Quick("b");
            b2.Skills.Add(Bolt("fang", 4));
            var (_, log2) = Ticks(r, new[] { strong }, new[] { b2 }, 1);
            Assert.That(log2.Single(x => x.Skill?.Id == "fang").Amount, Is.GreaterThan(cast.Amount * 1.5));

            // Nothing ready on the victim: a basic attack, logged with 0.
            var c = Quick("b");
            c.Skills.Add(Bolt("fang", 4));
            (s, log) = Ticks(r, new[] { Thief("a", RuleModifiers.StolenTurn, RuleModifiers.Usurp) }, new[] { c }, 0);
            s.Units[1].Cooldowns[0] = 2;
            BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(log.Single(x => x.Event == BattleSim.UsurpEvent).Amount, Is.EqualTo(0));
            Assert.That(log.Any(x => x.Actor == "a" && x.Event == null && x.Skill == null), Is.True, "a basic attack");

            // A usurp carrier steals MON_USURP_STEALS turns per enemy (2 here), a plain thief MON_STEALS_PER_FOE (1).
            r.UsurpSteals = 2;
            (s, log) = Ticks(r, new[] { Thief("a", RuleModifiers.StolenTurn, RuleModifiers.Usurp) }, new[] { Quick("b") }, 2);
            Assert.That(log.Count(x => x.Event == BattleSim.StealEvent), Is.EqualTo(2));
            (s, log) = Ticks(r, new[] { Thief("a", RuleModifiers.StolenTurn) }, new[] { Quick("b") }, 2);
            Assert.That(log.Count(x => x.Event == BattleSim.StealEvent), Is.EqualTo(1));

            // usurp without a stolen turn does nothing: the unit uses its own AI.
            var d = Quick("b");
            d.Skills.Add(Bolt("fang", 4));
            (s, log) = Ticks(r, new[] { Thief("a", RuleModifiers.Usurp) }, new[] { d }, 1);
            Assert.That(log.Any(x => x.Event == BattleSim.UsurpEvent), Is.False);
            Assert.That(log.Single(x => x.Skill?.Id == "fang").Actor, Is.EqualTo("b"));
        }

        // ------------------------------------------------------------------ foundling_egg

        static Fighter Hen(string id, params string[] rules)
        {
            var f = Mon(id, x => { x.Spd = 500; x.Hp = 1000; }, rules);
            f.Skills.Add(Bolt("peck", 2));
            f.Skills.Add(new CombatSkill { Id = "fluff", Kind = SkillKind.Passive, Target = SkillTarget.Self, Stat = StatIds.Def, Power = 0.1 });
            return f;
        }

        [Test]
        public void FoundlingEgg_LaysAnEggOnAFullSide_ThatNeverActs_ThenHatches()
        {
            var r = Rules;
            var a = new[] { Hen("a", RuleModifiers.FoundlingEgg), Mon("a2"), Mon("a3"), Mon("a4") };
            var b = new[] { Mon("b", x => { x.Spd = 10; x.Hp = 99999; x.Str = 0; }) };
            var (s, log) = Ticks(r, a, b, 1);
            Assert.That(s.Units.Count(u => u.Side == 0), Is.EqualTo(r.SideSize + 1), "a fifth unit on a full side");
            var egg = s.Units.Single(u => u.Id == "a" + BattleSim.EggSuffix);
            var hen = s.Units[0];
            Assert.That((egg.IsAdd, egg.Brood, egg.Egg, egg.Side, hen.EggIndex), Is.EqualTo((true, true, true, 0, s.Units.IndexOf(egg))));
            Assert.That(egg.MaxHp, Is.EqualTo(r.EggHp * hen.MaxHp).Within(1e-9));
            var laid = log.First(x => x.Event == BattleSim.EggEvent);
            Assert.That((laid.Actor, laid.Target, laid.Amount), Is.EqualTo(("a", egg.Id, egg.MaxHp)));

            while (hen.Tally.Actions < r.HatchActions - 1) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That((egg.Egg, egg.Gauge, egg.Tally.Actions), Is.EqualTo((true, 0.0, 0)), "an egg never fills its gauge");
            while (hen.Tally.Actions < r.HatchActions) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(egg.Egg, Is.False, "hatched after the carrier's MON_HATCH_ACTIONS-th action");
            Assert.That(egg.Source.Skills.Select(k => k.Id), Is.EqualTo(new[] { "peck" }), "its active skills, no passives");
            Assert.That((egg.Source.Rules.Count, egg.Source.Triggers.Count), Is.EqualTo((0, 0)));
            Assert.That(egg.Source.Stats.Str, Is.EqualTo(r.HatchStats * 300).Within(1e-9));
            Assert.That((egg.MaxHp, egg.Hp), Is.EqualTo((BattleSim.MaxHp(r, r.HatchStats * 1000), BattleSim.MaxHp(r, r.HatchStats * 1000))));
            var hatched = log.Last(x => x.Event == BattleSim.EggEvent);
            Assert.That((hatched.Actor, hatched.Target, hatched.Amount), Is.EqualTo((egg.Id, egg.Id, egg.MaxHp)));
            for (int i = 0; i < 60 && egg.Tally.Actions == 0; i++) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(log.Any(x => x.Actor == egg.Id && x.Skill?.Id == "peck"), Is.True, "the Foundling acts with the carrier's skill");

            // Without the rule: no egg.
            (s, _) = Ticks(r, new[] { Hen("a") }, b, 1);
            Assert.That(s.Units.Count, Is.EqualTo(2));
        }

        [Test]
        public void FoundlingEgg_NeverKeepsTheSideAlive_NeverCountsForKills_NeverRises()
        {
            var r = Rules;
            // A carrier at 1 HP: the foe kills it, and the egg left alone does not keep its side in the fight.
            var s = BattleSim.Start(r, new[] { Hen("a", RuleModifiers.FoundlingEgg) }, new[] { Quick("b", x => x.Str = 2000) });
            s.Units[0].Hp = 1;
            var log = new List<FightAction>();
            while (BattleSim.Step(r, s, new Fixed(0.3), log.Add) == FightOutcome.Ongoing) { }   // 0.3: no evade or crit, picks the carrier
            Assert.That(s.Outcome, Is.EqualTo(FightOutcome.SideBWon));
            Assert.That(s.Units.Single(u => u.Brood).Alive, Is.True, "the egg was never hit");

            // A foe with arise kills the egg (0.99 picks the last enemy, the egg): no kill and no risen egg.
            var foe = Quick("b", x => x.Str = 5000);
            foe.Rules.Add(RuleModifiers.Arise);
            s = BattleSim.Start(r, new[] { Hen("a", RuleModifiers.FoundlingEgg), Mon("a2", x => x.Hp = 99999) }, new[] { foe });
            for (int i = 0; i < 20 && !s.Units.Any(u => u.Brood && !u.Alive); i++) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            var egg = s.Units.Single(u => u.Brood);
            Assert.That((egg.Alive, egg.KilledBy), Is.EqualTo((false, "b")));
            Assert.That(s.Units[2].Tally.Kills, Is.EqualTo(0), "an egg never counts for kills");
            Assert.That(s.Units.Any(u => u.Risen), Is.False, "nor rises through arise");
        }

        // ------------------------------------------------------------------ hash, modes, clone

        [Test]
        public void NoCarrier_SameFightAsBefore_StateHashOnlyForCarriers()
        {
            var r = Rules;
            var s = BattleSim.Start(r, new[] { Mon("a") }, new[] { Mon("b") });
            string before = BattleSim.StateHash(s);
            s.Units[0].StolenFrom = new List<string> { "b" };
            s.Units[0].EggIndex = 1;
            Assert.That(BattleSim.StateHash(s), Is.EqualTo(before), "a unit without the rules hashes none of their state");

            s = BattleSim.Start(r, new[] { Mon("a", null, RuleModifiers.StolenTurn, RuleModifiers.FoundlingEgg) }, new[] { Mon("b") });
            before = BattleSim.StateHash(s);
            s.Units[0].StolenFrom = new List<string> { "b" };
            Assert.That(BattleSim.StateHash(s), Is.Not.EqualTo(before));
            before = BattleSim.StateHash(s);
            s.Units[0].EggIndex = 1;
            Assert.That(BattleSim.StateHash(s), Is.Not.EqualTo(before));
        }

        static Fighter Carrier(string id, string rule)
        {
            var f = Mon(id, x => { x.Hp = 600; x.Spd = 450; x.Luk = 300; }, RuleModifiers.StolenTurn, rule);
            f.Skills.Add(new CombatSkill { Id = "coin", Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = StatIds.Luk, Power = 2.0, Cooldown = 3 });
            return f;
        }

        static Fighter Foe(string id)
        {
            var f = Mon(id, x => { x.Hp = 500; x.Spd = 700; });
            f.Skills.Add(Bolt("fang", 3));
            return f;
        }

        [Test]
        public void EveryRule_In1v1_4v1_And4v4_IsDeterministic_AndCloneContinuesIdentically()
        {
            var r = Rules;
            foreach (var rule in SpeciesRules)
            {
                var shapes = new (Fighter[] A, Fighter[] B)[]
                {
                    (new[] { Carrier("c", rule) }, new[] { Foe("foe") }),
                    (new[] { Carrier("c1", rule), Carrier("c2", rule), Mon("m3"), Mon("m4") },
                     new[] { Mon("boss", x => { x.Hp = 2500; x.Str = 500; x.Spd = 250; }) }),
                    (new[] { Carrier("c1", rule), Carrier("c2", rule), Mon("m3"), Mon("m4") },
                     new[] { Carrier("d1", rule), Foe("e2"), Foe("e3"), Foe("e4") }),
                };
                foreach (var (A, B) in shapes)
                {
                    var once = BattleSim.Resolve(r, A, B, 11);
                    Assert.That(BattleSim.Resolve(r, A, B, 11).Hash, Is.EqualTo(once.Hash), rule);
                    Assert.That(once.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut), rule + " " + A.Length + "v" + B.Length);

                    var s = BattleSim.Start(r, A, B);
                    var rng = new RngStream(BattleSim.StreamName, 11);
                    var log = new List<FightAction>();
                    for (int i = 0; i < 40 && s.Outcome == FightOutcome.Ongoing; i++) BattleSim.Step(r, s, rng, log.Add);
                    Assert.That(log.Any(x => x.Event == FightAction.RulePrefix + rule), Is.True, rule + " fired in " + A.Length + "v" + B.Length);
                    var copy = s.Clone();
                    var rngCopy = rng.Clone();
                    while (BattleSim.Step(r, copy, rngCopy) == FightOutcome.Ongoing) { }
                    Assert.That(BattleSim.StateHash(copy), Is.EqualTo(once.Hash), rule);
                }
            }
        }
    }
}
