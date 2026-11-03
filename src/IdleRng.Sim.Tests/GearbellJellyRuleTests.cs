using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Species Gearbell Jelly (#158): its content and the rules ward_rewind, ward_toll and escapement.</summary>
    public class GearbellJellyRuleTests
    {
        static FightRules Rules => FightRules.FromContent(TestContent.Db);

        static readonly string[] SpeciesRules = { RuleModifiers.WardRewind, RuleModifiers.WardToll, RuleModifiers.Escapement };

        /// <summary>Always the same draw: 0 = every evade and crit roll succeeds, 0.99 = none does.</summary>
        sealed class Fixed : IRandomSource
        {
            readonly double _v;
            public Fixed(double v) { _v = v; }
            public double NextDouble() => _v;
        }

        static Fighter Mon(string id, Action<MonsterStats> tweak = null, string element = null, params string[] rules)
        {
            var st = new MonsterStats { Hp = 300, Str = 300, Int = 0, Wis = 300, Dex = 0, Def = 300, Spd = 100, Luk = 0 };
            tweak?.Invoke(st);
            var f = new Fighter { Id = id, Stats = st, Element = element };
            f.Rules.AddRange(rules);
            return f;
        }

        /// <summary>A target that never acts in these tests and never dies.</summary>
        static Fighter Wall(string id, Action<MonsterStats> tweak = null, params string[] rules) =>
            Mon(id, x => { x.Spd = 0; x.Hp = 99999; tweak?.Invoke(x); }, null, rules);

        static CombatSkill Dmg(string id, double power, int cooldown, SkillTarget target = SkillTarget.Enemy) =>
            new CombatSkill { Id = id, Kind = SkillKind.Damage, Target = target, Scales = StatIds.Wis, Power = power, Cooldown = cooldown, Element = "dark" };

        static Fighter With(Fighter f, params CombatSkill[] skills) { f.Skills.AddRange(skills); return f; }

        static string Ev(string rule) => FightAction.RulePrefix + rule;

        /// <summary>Steps a 1v1 for <paramref name="ticks"/> ticks (or to the end); <paramref name="afterFirst"/> runs after the first Step.</summary>
        static (FightState S, List<FightAction> Log) Run(FightRules r, Fighter a, Fighter b, int ticks, double draw, Action<FightState> afterFirst = null)
        {
            var s = BattleSim.Start(r, new[] { a }, new[] { b });
            var log = new List<FightAction>();
            var rng = new Fixed(draw);
            for (int i = 0; i < ticks && s.Outcome == FightOutcome.Ongoing; i++)
            {
                BattleSim.Step(r, s, rng, log.Add);
                if (i == 0) afterFirst?.Invoke(s);
            }
            return (s, log);
        }

        // ------------------------------------------------------------------ content and constants

        [Test]
        public void Content_SpeciesTreesAndConstants()
        {
            var db = TestContent.Db;
            var r = Rules;
            Assert.That((r.RewindWis, r.TollDmg, r.TollReform, (double)r.TollDelay, (double)r.TollMax, r.EscapePower),
                Is.EqualTo((db.ConstantValues["MON_REWIND_WIS"], db.ConstantValues["MON_TOLL_DMG"], db.ConstantValues["MON_TOLL_REFORM"],
                            db.ConstantValues["MON_TOLL_DELAY"], db.ConstantValues["MON_TOLL_MAX"], db.ConstantValues["MON_ESCAPE_POWER"])));
            var sp = db.SpeciesList.Single(x => x.Id == "gearbell_jelly");
            Assert.That((sp.Element, sp.NativeTree1, sp.NativeTree2, sp.ChaseTreeRun, sp.ChaseTreeBook),
                Is.EqualTo(("dark", "mana_ward", "rune_gearwork", "carillon", "perpetual_escapement")));
            Assert.That(db.SpeciesList[0].Id, Is.Not.EqualTo("gearbell_jelly"), "appended, never the starter");

            // The idea-pool tree is native-only now: never offered by an Awakening, owned by this species alone.
            var ward = db.Trees["mana_ward"];
            Assert.That((ward.Kind, ward.OfferStat, ward.OfferThreshold), Is.EqualTo((TreeKind.Native, (string)null, 0)));
            Assert.That(db.SpeciesList.Count(x => x.NativeTree1 == "mana_ward" || x.NativeTree2 == "mana_ward"), Is.EqualTo(1));

            var caps = new Dictionary<string, string>
            {
                ["rune_gearwork"] = RuleModifiers.WardRewind, ["carillon"] = RuleModifiers.WardToll, ["perpetual_escapement"] = RuleModifiers.Escapement,
            };
            foreach (var kv in caps)
            {
                var t = db.Trees[kv.Key];
                Assert.That(t.Nodes.Count, Is.EqualTo(8), kv.Key);
                Assert.That((t.Nodes[7].Kind, t.Nodes[7].RuleId), Is.EqualTo((NodeKind.Rule, kv.Value)), kv.Key);
                Assert.That(t.Nodes[4].Kind, Is.EqualTo(NodeKind.Skill), kv.Key);
                Assert.That(RuleModifiers.IsKnown(kv.Value), kv.Value);
                var desc = BattleText.RuleDesc(db, kv.Value);
                Assert.That(TextTemplate.HasPlaceholder(desc), Is.False, desc);
                Assert.That(db.Text("rule." + kv.Value + ".name"), Is.Not.EqualTo("rule." + kv.Value + ".name"), kv.Value);
            }
            Assert.That(db.Trees["rune_gearwork"].Kind, Is.EqualTo(TreeKind.Native));
            Assert.That(db.Trees["perpetual_escapement"].BookGated, Is.True);
            var codex = db.Trees["still_hours_codex"];
            Assert.That(codex.Kind, Is.EqualTo(TreeKind.Book));
            Assert.That((codex.Nodes[7].Kind, codex.Nodes[7].Skill.Id, codex.Nodes[7].Trigger), Is.EqualTo((NodeKind.Trigger, "second_hand", (FightTrigger?)FightTrigger.OnEvade)));
            Assert.That(db.SkillBooks["book_hours"].TreeId, Is.EqualTo("still_hours_codex"));
            Assert.That(db.BookDrops.Single(b => b.BookId == "book_hours").MinFloor, Is.EqualTo(10));
            Assert.That(db.Achievements["still_waters"].UnlocksSpecies, Is.EqualTo("gearbell_jelly"));
            Assert.That(db.Text("tree.carillon.apex"), Is.EqualTo("Carillon Medusa"));
            Assert.That(db.Text("tree.perpetual_escapement.apex"), Is.EqualTo("Orrery Moonbell"));

            // Its damage skills are dark and scale on WIS (WIS is both its armour and its weapon).
            foreach (var id in new[] { "bell_sting", "knell", "hour_hand", "second_hand", "chime_cascade" })
                Assert.That((db.MonsterSkills[id].Scales, db.MonsterSkills[id].Element), Is.EqualTo((StatIds.Wis, "dark")), id);
        }

        [Test]
        public void Validation_RejectsConstantsOutOfRange()
        {
            var a = new[] { Mon("a") };
            var b = new[] { Mon("b") };
            foreach (var bad in new Action<FightRules>[]
                     {
                         r => r.RewindWis = -0.1, r => r.RewindWis = double.PositiveInfinity, r => r.TollDmg = -1, r => r.TollDmg = double.NaN,
                         r => r.TollReform = 1.1, r => r.TollReform = -0.1, r => r.TollDelay = -1, r => r.TollMax = -1,
                         r => r.EscapePower = -0.1, r => r.EscapePower = double.NaN,
                     })
            {
                var r = Rules;
                bad(r);
                Assert.Throws<ArgumentException>(() => BattleSim.Start(r, a, b));
            }
        }

        [Test]
        public void NoCarrier_NoSpeciesEvents()
        {
            var log = new List<FightAction>();
            BattleSim.Resolve(Rules, new[] { Mon("a", null, "dark", RuleModifiers.ManaWard) }, new[] { Mon("b", null, "nature") }, 1, log.Add);
            Assert.That(log.Any(x => x.Event != null && SpeciesRules.Any(id => x.Event == Ev(id))), Is.False);
        }

        [Test]
        public void EveryRule_In1v1_4v1_And4v4_IsDeterministic_AndCloneContinuesIdentically()
        {
            var r = Rules;
            Fighter Jelly(string id, string rule) => With(Mon(id, x => { x.Hp = 400; x.Str = 0; x.Int = 0; x.Wis = 300; x.Dex = 300; x.Luk = 300; }, "dark",
                RuleModifiers.ManaWard, rule == RuleModifiers.Escapement ? RuleModifiers.WardRewind : rule, rule), Dmg("bell_sting", 1.3, 3), Dmg("knell", 0.8, 3, SkillTarget.AllEnemies));
            Fighter Brute(string id) => Mon(id, x => { x.Hp = 700; x.Str = 700; x.Def = 200; x.Wis = 300; }, "light");
            foreach (var rule in SpeciesRules)
            {
                var shapes = new (Fighter[] A, Fighter[] B)[]
                {
                    (new[] { Jelly("j", rule) }, new[] { Brute("foe") }),
                    (new[] { Jelly("j1", rule), Jelly("j2", rule), Mon("m3"), Brute("m4") },
                     new[] { Mon("boss", x => { x.Hp = 3000; x.Str = 700; x.Def = 400; x.Spd = 250; }) }),
                    (new[] { Jelly("j1", rule), Jelly("j2", rule), Mon("m3"), Mon("m4") },
                     new[] { Jelly("k1", rule), Brute("e2"), Mon("e3", x => x.Str = 450), Brute("e4") }),
                };
                foreach (var (A, B) in shapes)
                {
                    var once = BattleSim.Resolve(r, A, B, 11);
                    Assert.That(BattleSim.Resolve(r, A, B, 11).Hash, Is.EqualTo(once.Hash), rule);
                    Assert.That(once.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut), rule + " " + A.Length + "v" + B.Length);

                    var s = BattleSim.Start(r, A, B);
                    var rng = new RngStream(BattleSim.StreamName, 11);
                    var log = new List<FightAction>();
                    for (int i = 0; i < 80 && s.Outcome == FightOutcome.Ongoing; i++) BattleSim.Step(r, s, rng, log.Add);
                    Assert.That(log.Any(x => x.Event == Ev(rule)), Is.True, rule + " fired in " + A.Length + "v" + B.Length + ": " + once.Outcome + " " + s.Ticks);
                    var copy = s.Clone();
                    var rngCopy = rng.Clone();
                    while (BattleSim.Step(r, copy, rngCopy) == FightOutcome.Ongoing) { }
                    Assert.That(BattleSim.StateHash(copy), Is.EqualTo(once.Hash), rule);
                }
            }
        }

        // ------------------------------------------------------------------ ward_rewind

        /// <summary>A fast jelly with a cooldown-3 sting; its ward is cut to 100 after the first tick (before it acts).</summary>
        static (FightState S, List<FightAction> Log) Rewind(FightRules r, int cooldown, params string[] rules) =>
            Run(r, With(Mon("a", x => { x.Spd = 900; x.Str = 0; }, "dark", rules), Dmg("sting", 1.3, cooldown)), Wall("b"), 3, 0.99,
                s => s.Units[0].Ward = Math.Min(s.Units[0].Ward, 100));

        [Test]
        public void WardRewind_ACooldownSkillRestoresWard_UpToItsFullSize()
        {
            var r = Rules;
            double full = r.WardWis * 300, step = r.RewindWis * 300;
            Assert.That(step, Is.LessThan(full - 100));
            var (s, log) = Rewind(r, 3, RuleModifiers.ManaWard, RuleModifiers.WardRewind);
            var rewinds = log.Where(x => x.Event == BattleSim.RewindEvent).ToList();
            Assert.That(rewinds.Select(x => (x.Actor, x.Target, x.Amount)).ToList(), Is.EqualTo(new[] { ("a", "a", step) }),
                string.Join("; ", log.Select(x => x.Actor + " " + (x.Event ?? x.Skill?.Id ?? "basic") + " " + x.Amount)));
            Assert.That(s.Units[0].Ward, Is.EqualTo(100 + step).Within(1e-9));

            // Never above the ward's full size.
            r = Rules;
            r.RewindWis = 10;
            (s, log) = Rewind(r, 3, RuleModifiers.ManaWard, RuleModifiers.WardRewind);
            Assert.That(s.Units[0].Ward, Is.EqualTo(full).Within(1e-9));
            Assert.That(log.Single(x => x.Event == BattleSim.RewindEvent).Amount, Is.EqualTo(full - 100).Within(1e-9));
        }

        [Test]
        public void WardRewind_Off_OrWithoutAWard_OrOnAShortCooldown_DoesNothing()
        {
            var r = Rules;
            var off = Rewind(r, 3, RuleModifiers.ManaWard);
            Assert.That(off.S.Units[0].Ward, Is.EqualTo(100));
            var noWard = Rewind(r, 3, RuleModifiers.WardRewind);   // agent decision (#158): no ward rule, nothing to restore
            Assert.That(noWard.S.Units[0].Ward, Is.EqualTo(0));
            var shortCd = Rewind(r, 1, RuleModifiers.ManaWard, RuleModifiers.WardRewind);
            Assert.That(shortCd.S.Units[0].Ward, Is.EqualTo(100));
            foreach (var log in new[] { off.Log, noWard.Log, shortCd.Log }) Assert.That(log.Any(x => x.Event == BattleSim.RewindEvent), Is.False);
        }

        // ------------------------------------------------------------------ ward_toll

        /// <summary>A slow jelly (ward 1.5 x 100 WIS) under a fast, tough brute's basic attacks.</summary>
        static Fighter TollJelly(Action<MonsterStats> tweak = null, params string[] rules) =>
            Mon("a", x => { x.Spd = 10; x.Hp = 99999; x.Wis = 100; x.Str = 0; tweak?.Invoke(x); }, "dark", rules);

        static Fighter Brute() => Mon("b", x => { x.Spd = 900; x.Str = 400; x.Hp = 99999; x.Wis = 200; x.Def = 9999; }, "light");

        [Test]
        public void WardToll_ABrokenWardTollsEveryEnemy_ThenReformsAfterItsOwnActions()
        {
            var r = Rules;
            double full = r.WardWis * 100;
            var (s, log) = Run(r, TollJelly(null, RuleModifiers.ManaWard, RuleModifiers.WardToll), Brute(), 400, 0.99);
            var tolls = log.Where(x => x.Event == BattleSim.TollEvent).ToList();
            Assert.That(tolls.Count, Is.EqualTo(r.TollMax), "the ward keeps breaking and re-forming until the tolls run out");
            double expect = BattleSim.Mitigate(r, r.TollDmg * full * r.ElementMult("dark", "light"), 200);
            Assert.That(tolls.Select(x => (x.Actor, x.Target)), Is.All.EqualTo(("a", "b")));
            Assert.That(tolls.Select(x => x.Amount), Is.All.EqualTo(expect).Within(1e-9));

            // The first toll comes right after the brute's blow that broke the ward; the ward re-forms at MON_TOLL_REFORM of
            // full once the jelly has acted MON_TOLL_DELAY times since.
            int first = log.FindIndex(x => x.Event == BattleSim.TollEvent);
            var breakLine = log.Take(first).Last(x => x.Event == FightAction.RulePrefix + RuleModifiers.ManaWard);
            Assert.That(breakLine.Amount, Is.EqualTo(0));
            var reform = log.Skip(first).First(x => x.Event == FightAction.RulePrefix + RuleModifiers.ManaWard && x.Amount > 0);
            Assert.That(reform.Amount, Is.EqualTo(r.TollReform * full).Within(1e-9));
            Assert.That(s.Units[0].TollsUsed, Is.EqualTo(r.TollMax));

            // Off: the ward breaks once and stays broken; nothing tolls.
            (s, log) = Run(r, TollJelly(null, RuleModifiers.ManaWard), Brute(), 400, 0.99);
            Assert.That(log.Any(x => x.Event == BattleSim.TollEvent), Is.False);
            Assert.That(s.Units[0].Ward, Is.EqualTo(0));
        }

        [Test]
        public void WardToll_HonoursMaxAndDelay()
        {
            var r = Rules;
            r.TollMax = 1;
            var (_, log) = Run(r, TollJelly(null, RuleModifiers.ManaWard, RuleModifiers.WardToll), Brute(), 400, 0.99);
            Assert.That(log.Count(x => x.Event == BattleSim.TollEvent), Is.EqualTo(1));

            r = Rules;
            r.TollDelay = 0;   // the ward re-forms right after the toll
            (_, log) = Run(r, TollJelly(null, RuleModifiers.ManaWard, RuleModifiers.WardToll), Brute(), 3, 0.99);
            int first = log.FindIndex(x => x.Event == BattleSim.TollEvent);
            Assert.That(first, Is.GreaterThanOrEqualTo(0));
            Assert.That(log[first + 1].Event, Is.EqualTo(FightAction.RulePrefix + RuleModifiers.ManaWard));
            Assert.That(log[first + 1].Amount, Is.EqualTo(r.TollReform * r.WardWis * 100).Within(1e-9));
        }

        [Test]
        public void WardToll_ABlowThatBreaksTheWardAndKills_TollsNothing()
        {
            var r = Rules;
            var giant = Mon("b", x => { x.Spd = 900; x.Str = 9000; x.Hp = 99999; x.Def = 9999; }, "light");
            var (s, log) = Run(r, TollJelly(x => x.Hp = 1, RuleModifiers.ManaWard, RuleModifiers.WardToll), giant, 5, 0.99);
            Assert.That(s.Outcome, Is.EqualTo(FightOutcome.SideBWon));
            Assert.That(log.Any(x => x.Event == FightAction.RulePrefix + RuleModifiers.ManaWard && x.Amount == 0), Is.True, "the ward broke");
            Assert.That(log.Any(x => x.Event == BattleSim.TollEvent), Is.False);
        }

        [Test]
        public void WardToll_ItsDamageNeverTollsAWardItBreaks()
        {
            // Two tolling jellies: a's toll breaks b's small ward; b does not toll back.
            var r = Rules;
            r.TollDmg = 100;
            var a = TollJelly(null, RuleModifiers.ManaWard, RuleModifiers.WardToll);
            var b = Mon("b", x => { x.Spd = 900; x.Str = 400; x.Hp = 99999; x.Wis = 10; x.Def = 9999; }, "light", RuleModifiers.ManaWard, RuleModifiers.WardToll);
            var (s, log) = Run(r, a, b, 200, 0.99);
            Assert.That(log.Count(x => x.Event == BattleSim.TollEvent && x.Actor == "a"), Is.GreaterThan(0));
            Assert.That(log.Any(x => x.Event == BattleSim.TollEvent && x.Actor == "b"), Is.False);
            Assert.That(s.Units[1].TollsUsed, Is.EqualTo(0));
        }

        [Test]
        public void WardToll_WithRewind_ARefilledWardBreaksAndTollsAgainBeforeItsReform()
        {
            var r = Rules;
            r.TollDelay = 1000;   // never re-forms by itself: only Rewind brings it back
            var jelly = With(TollJelly(x => x.Spd = 300, RuleModifiers.ManaWard, RuleModifiers.WardToll, RuleModifiers.WardRewind), Dmg("sting", 0.1, 2));
            var (_, log) = Run(r, jelly, Brute(), 600, 0.99);
            Assert.That(log.Count(x => x.Event == BattleSim.TollEvent), Is.EqualTo(r.TollMax));
            Assert.That(log.Any(x => x.Event == BattleSim.RewindEvent), Is.True);

            var (_, noRewind) = Run(r, With(TollJelly(x => x.Spd = 300, RuleModifiers.ManaWard, RuleModifiers.WardToll), Dmg("sting", 0.1, 2)), Brute(), 600, 0.99);
            Assert.That(noRewind.Count(x => x.Event == BattleSim.TollEvent), Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ escapement

        /// <summary>A jelly at SPD_REF (one gauge turn per TurnTicks ticks) with an Hour Hand of cooldown 4, against a wall.</summary>
        static Fighter Clock(Action<MonsterStats> tweak = null, params string[] rules) =>
            With(Mon("a", x => { x.Spd = 100; x.Str = 50; tweak?.Invoke(x); }, "dark", rules), Dmg("hour_hand", 1.6, 4));

        [Test]
        public void Escapement_CooldownSkillsRunOnTheClock_OutOfTurn_GaugeTurnsBasicAttack()
        {
            var r = Rules;
            int turn = r.TurnTicks;
            var (s, log) = Run(r, Clock(null, RuleModifiers.Escapement), Wall("b"), 5 * turn, 0.99);
            var u = s.Units[0];
            var clock = log.Where(x => x.Event == BattleSim.EscapeEvent).ToList();
            // Ready at the first tick; cooldown 4 ticks down once per turn of time: cast again after 4 turns.
            Assert.That(clock.Count, Is.EqualTo(2));
            Assert.That(clock.Select(x => x.Amount), Is.All.EqualTo(0));
            var casts = log.Where(x => x.Actor == "a" && x.Event == null).ToList();
            Assert.That(casts.Count(x => x.Skill?.Id == "hour_hand"), Is.EqualTo(2));
            Assert.That(casts.Count(x => x.Skill == null), Is.EqualTo(u.Tally.Actions), "every gauge turn is a basic attack");
            Assert.That(u.Tally.Actions, Is.EqualTo(5), "clock casts are not actions");
            Assert.That(log.IndexOf(clock[0]), Is.EqualTo(0), "the first cast comes at the first tick, before any turn");

            // Off: it casts the skill on its own turns, every 5th action (cooldown 4).
            (s, log) = Run(r, Clock(), Wall("b"), 5 * turn, 0.99);
            Assert.That(log.Count(x => x.Skill?.Id == "hour_hand"), Is.EqualTo(2), "the 1st and 5th actions");
            Assert.That(log.Any(x => x.Event == BattleSim.EscapeEvent), Is.False);
        }

        [Test]
        public void Escapement_SpdDoesNotMove_TheClock()
        {
            var r = Rules;
            int turn = r.TurnTicks;
            int Casts(double spd) => Run(r, Clock(x => x.Spd = spd, RuleModifiers.Escapement), Wall("b"), 9 * turn, 0.99).Log
                .Count(x => x.Event == BattleSim.EscapeEvent);
            Assert.That(Casts(20), Is.EqualTo(Casts(400)));
        }

        [Test]
        public void Escapement_WardHaste_TicksTheClockFaster()
        {
            var r = Rules;
            int turn = r.TurnTicks;
            int Casts(params string[] rules) => Run(r, Clock(x => x.Wis = 300, rules), Wall("b"), 12 * turn, 0.99).Log
                .Count(x => x.Event == BattleSim.EscapeEvent);
            int plain = Casts(RuleModifiers.Escapement), warded = Casts(RuleModifiers.Escapement, RuleModifiers.ManaWard);
            Assert.That(warded, Is.GreaterThan(plain));
        }

        [Test]
        public void Escapement_CountsTowardTheExtraActionCap_WithDoubleTurnStart()
        {
            var r = Rules;
            r.ExtraActionsCap = 1;
            int turn = r.TurnTicks;
            // Without escapement the first action is followed by double_turn_start's extra action.
            var plain = Run(r, Clock(null, RuleModifiers.DoubleTurnStart), Wall("b"), turn, 0.99).Log;
            Assert.That(plain.Any(x => x.Event == FightAction.DoubleTurnEvent), Is.True);
            // With it, the clock cast at the first tick already spent the turn's one extra action.
            var (s, log) = Run(r, Clock(null, RuleModifiers.DoubleTurnStart, RuleModifiers.Escapement), Wall("b"), turn, 0.99);
            Assert.That(log.Count(x => x.Event == BattleSim.EscapeEvent), Is.EqualTo(1));
            Assert.That(log.Any(x => x.Event == FightAction.DoubleTurnEvent), Is.False);
            Assert.That(s.Units[0].ClockCasts, Is.EqualTo(0), "the turn spent the clock casts");
        }

        [Test]
        public void Escapement_AtMostTheCapBetweenTwoTurns_AndOnePerTick()
        {
            var r = Rules;
            r.ExtraActionsCap = 1;
            var two = With(Clock(null, RuleModifiers.Escapement), Dmg("knell", 0.8, 3, SkillTarget.AllEnemies));
            var (_, log) = Run(r, two, Wall("b"), r.TurnTicks - 1, 0.99);
            Assert.That(log.Count(x => x.Event == BattleSim.EscapeEvent), Is.EqualTo(1), "the second skill waits for the next turn");

            r.ExtraActionsCap = 2;
            (_, log) = Run(r, two, Wall("b"), 1, 0.99);
            Assert.That(log.Count(x => x.Event == BattleSim.EscapeEvent), Is.EqualTo(1), "one per tick");
            (_, log) = Run(r, two, Wall("b"), 2, 0.99);
            Assert.That(log.Where(x => x.Event == BattleSim.EscapeEvent).Select(x => x.Amount), Is.EqualTo(new double[] { 0, 1 }));
        }

        [Test]
        public void Escapement_AClockCastIsNeverEchoed_AndRunsAtEscapePower()
        {
            var r = Rules;
            var (_, log) = Run(r, Clock(null, RuleModifiers.Escapement, RuleModifiers.EchoCast), Wall("b"), 1, 0.99);
            Assert.That(log.Count(x => x.Skill?.Id == "hour_hand"), Is.EqualTo(1));

            double Hit(double power)
            {
                var rr = Rules;
                rr.EscapePower = power;
                return Run(rr, Clock(null, RuleModifiers.Escapement), Wall("b"), 1, 0.99).Log.Single(x => x.Skill?.Id == "hour_hand").Amount;
            }
            Assert.That(Hit(0.5), Is.EqualTo(Hit(1) * 0.5).Within(1e-6));
        }

        [Test]
        public void Escapement_AHealOnTheClockWaitsForAHurtTarget()
        {
            var r = Rules;
            var heal = new CombatSkill { Id = "mend", Kind = SkillKind.Heal, Target = SkillTarget.Self, Scales = StatIds.Wis, Power = 0.5, Cooldown = 3 };
            var jelly = With(Mon("a", x => x.Str = 50, "dark", RuleModifiers.Escapement), heal);
            var (_, full) = Run(r, jelly, Wall("b"), 5, 0.99);
            Assert.That(full.Any(x => x.Event == BattleSim.EscapeEvent), Is.False, "full HP: the heal waits");
            var (s, hurt) = Run(r, jelly, Wall("b"), 3, 0.99, st => st.Units[0].Hp = 1);
            Assert.That(hurt.Count(x => x.Event == BattleSim.EscapeEvent), Is.EqualTo(1));
            Assert.That(hurt.Single(x => x.Skill?.Id == "mend").Amount, Is.GreaterThan(0));
            Assert.That(s.Units[0].Tally.Actions, Is.EqualTo(0));
        }

        [Test]
        public void Escapement_ClockCastsFeedRewind()
        {
            var r = Rules;
            var (_, log) = Run(r, Clock(x => x.Wis = 300, RuleModifiers.Escapement, RuleModifiers.ManaWard, RuleModifiers.WardRewind), Wall("b"), 1, 0.99);
            Assert.That(log.Count(x => x.Event == BattleSim.EscapeEvent), Is.EqualTo(1));
            Assert.That(log.Any(x => x.Event == BattleSim.RewindEvent), Is.False, "a full ward has nothing to restore");
            var gambit = Clock(x => x.Wis = 300, RuleModifiers.Escapement, RuleModifiers.ManaWard, RuleModifiers.WardRewind);
            var st2 = BattleSim.Start(r, new[] { gambit }, new[] { Wall("b") });
            var log2 = new List<FightAction>();
            BattleSim.Step(r, st2, new Fixed(0.99), log2.Add);   // fight start: ward set, then the first clock cast
            st2.Units[0].Ward = 0;
            st2.Units[0].Cooldowns[0] = 0;
            BattleSim.Step(r, st2, new Fixed(0.99), log2.Add);
            Assert.That(log2.Count(x => x.Event == BattleSim.RewindEvent), Is.EqualTo(1));
            Assert.That(st2.Units[0].Ward, Is.EqualTo(r.RewindWis * 300).Within(1e-9), "a broken ward re-forms");
        }

        [Test]
        public void Escapement_GambitNamedSkills_FollowTheirRules_OnTheClock()
        {
            var r = Rules;
            var jelly = Clock(null, RuleModifiers.Escapement);
            jelly.Gambits.Add(new GambitRule(GambitConditions.SelfHpBelow, 0.5, "hour_hand"));
            var (_, log) = Run(r, jelly, Wall("b"), 3, 0.99);
            Assert.That(log.Any(x => x.Event == BattleSim.EscapeEvent), Is.False, "its rule does not hold at full HP");
            (_, log) = Run(r, jelly, Wall("b"), 3, 0.99, st => st.Units[0].Hp = 1);
            Assert.That(log.Count(x => x.Event == BattleSim.EscapeEvent), Is.EqualTo(1));
        }
    }
}
