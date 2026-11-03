using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Species Lodge Beaver (#146): its content and the rules mudwork, tail_slap and holdback.</summary>
    public class LodgeBeaverRuleTests
    {
        static FightRules Rules => FightRules.FromContent(TestContent.Db);

        static readonly string[] SpeciesRules = { RuleModifiers.Mudwork, RuleModifiers.TailSlap, RuleModifiers.Holdback };

        /// <summary>Always the same draw: 0 = every evade and crit roll succeeds, 0.99 = none does.</summary>
        sealed class Fixed : IRandomSource
        {
            readonly double _v;
            public Fixed(double v) { _v = v; }
            public double NextDouble() => _v;
        }

        static Fighter Mon(string id, Action<MonsterStats> tweak = null, string element = null, params string[] rules)
        {
            var st = new MonsterStats { Hp = 300, Str = 300, Int = 0, Wis = 300, Dex = 0, Def = 300, Spd = 300, Luk = 0 };
            tweak?.Invoke(st);
            var f = new Fighter { Id = id, Stats = st, Element = element };
            f.Rules.AddRange(rules);
            return f;
        }

        /// <summary>A fast unit: it acts many times before the slow, very tough one acts once.</summary>
        static Fighter Fast(string id, Action<MonsterStats> tweak = null, params string[] rules) =>
            Mon(id, x => { x.Spd = 900; tweak?.Invoke(x); }, null, rules);

        static Fighter Wall(string id, Action<MonsterStats> tweak = null, params string[] rules) =>
            Mon(id, x => { x.Spd = 10; x.Hp = 99999; tweak?.Invoke(x); }, null, rules);

        static CombatSkill Heal(string id, double power, SkillTarget target = SkillTarget.Self) =>
            new CombatSkill { Id = id, Kind = SkillKind.Heal, Target = target, Scales = StatIds.Wis, Power = power, Cooldown = 3 };

        static CombatSkill Shield(string id, double power, int duration = 3) =>
            new CombatSkill { Id = id, Kind = SkillKind.Shield, Target = SkillTarget.Self, Scales = StatIds.Wis, Power = power, Cooldown = 4, Duration = duration };

        static CombatSkill Dmg(string id, double power, SkillTarget target = SkillTarget.Enemy) =>
            new CombatSkill { Id = id, Kind = SkillKind.Damage, Target = target, Scales = StatIds.Str, Power = power };

        static Fighter With(Fighter f, params CombatSkill[] skills) { f.Skills.AddRange(skills); return f; }

        static Fighter OnAction(Fighter f, CombatSkill sk) { f.Triggers.Add(new TriggeredSkill(FightTrigger.OnAction, sk)); return f; }

        /// <summary>Steps until side A's first unit has taken <paramref name="actions"/> actions (side B must not act).</summary>
        static List<FightAction> Play(FightRules r, FightState s, int actions, double draw = 0.99)
        {
            var log = new List<FightAction>();
            while (s.Units[0].Tally.Actions < actions && s.Outcome == FightOutcome.Ongoing) BattleSim.Step(r, s, new Fixed(draw), log.Add);
            Assert.That(s.Units[1].Tally.Actions, Is.EqualTo(0), "side B never acted");
            return log;
        }

        static FightState Start(FightRules r, Fighter a, params Fighter[] b) => BattleSim.Start(r, new[] { a }, b);

        // ------------------------------------------------------------------ content and constants

        [Test]
        public void Content_SpeciesTreesAndConstants()
        {
            var db = TestContent.Db;
            var r = Rules;
            var k = db.ConstantValues;
            Assert.That((r.MudCap, (double)r.MudTurns, r.SlapShield, r.DamFlow, r.DamSpill, r.DamBreak), Is.EqualTo((k["MON_MUD_CAP"],
                k["MON_MUD_TURNS"], k["MON_SLAP_SHIELD"], k["MON_DAM_FLOW"], k["MON_DAM_SPILL"], k["MON_DAM_BREAK"])));
            var sp = db.SpeciesList.Single(x => x.Id == "lodge_beaver");
            Assert.That((sp.Element, sp.NativeTree1, sp.NativeTree2, sp.ChaseTreeRun, sp.ChaseTreeBook),
                Is.EqualTo(("nature", "barricade", "mudwork", "tail_slap", "great_dam")));
            Assert.That(db.SpeciesList[0].Id, Is.Not.EqualTo("lodge_beaver"), "appended, never the starter");

            // The idea-pool tree is native-only now: never offered by an Awakening, owned by this species alone.
            var barricade = db.Trees["barricade"];
            Assert.That((barricade.Kind, barricade.OfferStat, barricade.OfferThreshold), Is.EqualTo((TreeKind.Native, (string)null, 0)));
            Assert.That(db.SpeciesList.Count(x => x.NativeTree1 == "barricade" || x.NativeTree2 == "barricade"), Is.EqualTo(1));

            var caps = new Dictionary<string, string>
            {
                ["mudwork"] = RuleModifiers.Mudwork, ["tail_slap"] = RuleModifiers.TailSlap, ["great_dam"] = RuleModifiers.Holdback,
            };
            foreach (var kv in caps)
            {
                var t = db.Trees[kv.Key];
                Assert.That(t.Nodes.Count, Is.EqualTo(8), kv.Key);
                Assert.That(t.Nodes.Sum(n => n.Cost), Is.EqualTo(11), kv.Key);
                Assert.That((t.Nodes[7].Kind, t.Nodes[7].RuleId), Is.EqualTo((NodeKind.Rule, kv.Value)), kv.Key);
                Assert.That(t.Nodes[4].Kind, Is.EqualTo(NodeKind.Skill), kv.Key);
                Assert.That(RuleModifiers.IsKnown(kv.Value), kv.Value);
                var desc = BattleText.RuleDesc(db, kv.Value);
                Assert.That(TextTemplate.HasPlaceholder(desc), Is.False, desc);
                Assert.That(db.Text("rule." + kv.Value + ".name"), Is.Not.EqualTo("rule." + kv.Value + ".name"), kv.Value);
            }
            Assert.That(db.Trees["mudwork"].Kind, Is.EqualTo(TreeKind.Native));
            // The second chase is a harder in-run chase: no book node, both natives maxed, every in-run condition asked again.
            var dam = db.Trees["great_dam"];
            Assert.That((dam.Kind, dam.BookGated, dam.SecondChase), Is.EqualTo((TreeKind.Chase, false, true)));
            Assert.That(db.Achievements["dam_builder"].UnlocksSpecies, Is.EqualTo("lodge_beaver"));
            Assert.That(db.Text("tree.tail_slap.apex"), Is.EqualTo("Logjam Warden"));
            Assert.That(db.Text("tree.great_dam.apex"), Is.EqualTo("Floodgate Colossus"));
            Assert.That((db.MonsterSkills["mud_patch"].Kind, db.MonsterSkills["mud_patch"].Target), Is.EqualTo((SkillKind.Heal, SkillTarget.Self)));
            Assert.That(db.MonsterSkills["tail_thump"].Scales, Is.EqualTo(StatIds.Str));
            Assert.That(db.MonsterSkills["sluice_gate"].Kind, Is.EqualTo(SkillKind.Shield));
        }

        [Test]
        public void Validation_RejectsConstantsOutOfRange()
        {
            var a = new[] { Mon("a") };
            var b = new[] { Mon("b") };
            foreach (var bad in new Action<FightRules>[]
                     {
                         r => r.MudCap = -0.1, r => r.MudCap = 1.1, r => r.MudCap = double.NaN, r => r.MudTurns = 0,
                         r => r.SlapShield = -0.1, r => r.SlapShield = double.PositiveInfinity, r => r.DamFlow = 0, r => r.DamFlow = 1.1,
                         r => r.DamFlow = double.NaN, r => r.DamSpill = -1, r => r.DamSpill = double.PositiveInfinity, r => r.DamBreak = 0,
                         r => r.DamBreak = double.PositiveInfinity,
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
            BattleSim.Resolve(Rules, new[] { With(Mon("a", null, "nature"), Shield("gate", 1.2)) }, new[] { Mon("b", null, "fire") }, 1, log.Add);
            Assert.That(log.Any(x => x.Event != null && SpeciesRules.Any(id => x.Event == FightAction.RulePrefix + id)), Is.False);
        }

        [Test]
        public void EveryRule_In1v1_4v1_And4v4_IsDeterministic_AndCloneContinuesIdentically()
        {
            var r = Rules;
            Fighter Beaver(string id, string rule) => With(Mon(id, x => { x.Hp = 500; x.Str = 700; x.Def = 600; x.Wis = 400; x.Spd = 120; x.Luk = 100; }, "nature",
                RuleModifiers.Barricade, RuleModifiers.Mudwork, rule), Heal("mud_patch", 1.0), Dmg("tail_thump", 1.2), Shield("sluice_gate", 1.2));
            Fighter Brawler(string id) => Mon(id, x => { x.Str = 700; x.Def = 200; x.Spd = 120; }, "fire");
            foreach (var rule in SpeciesRules)
            {
                var shapes = new (Fighter[] A, Fighter[] B)[]
                {
                    (new[] { Beaver("c", rule) }, new[] { Brawler("foe") }),
                    (new[] { Beaver("c1", rule), Beaver("c2", rule), Mon("m3"), With(Mon("m4"), Heal("mend", 1.0, SkillTarget.LowestAlly)) },
                     new[] { Mon("boss", x => { x.Hp = 2500; x.Str = 450; x.Def = 500; x.Spd = 250; }) }),
                    (new[] { Beaver("c1", rule), Beaver("c2", rule), Mon("m3"), Mon("m4") },
                     new[] { Beaver("d1", rule), Brawler("e2"), Mon("e3", x => x.Str = 120), Brawler("e4") }),
                };
                foreach (var (A, B) in shapes)
                {
                    var once = BattleSim.Resolve(r, A, B, 11);
                    Assert.That(BattleSim.Resolve(r, A, B, 11).Hash, Is.EqualTo(once.Hash), rule);
                    Assert.That(once.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut), rule + " " + A.Length + "v" + B.Length);

                    var s = BattleSim.Start(r, A, B);
                    var rng = new RngStream(BattleSim.StreamName, 11);
                    var log = new List<FightAction>();
                    for (int i = 0; i < 300 && s.Outcome == FightOutcome.Ongoing; i++) BattleSim.Step(r, s, rng, log.Add);
                    Assert.That(log.Any(x => x.Event == FightAction.RulePrefix + rule), Is.True, rule + " fired in " + A.Length + "v" + B.Length);
                    var copy = s.Clone();
                    var rngCopy = rng.Clone();
                    while (BattleSim.Step(r, copy, rngCopy) == FightOutcome.Ongoing) { }
                    Assert.That(BattleSim.StateHash(copy), Is.EqualTo(once.Hash), rule);
                }
            }
        }

        // ------------------------------------------------------------------ mudwork

        [Test]
        public void Mudwork_SelfHealBecomesShield_AndTheAiCastsItAtFullHp()
        {
            var r = Rules;
            // Without mudwork the AI waits for missing HP: at full HP it never heals.
            var plain = Start(r, With(Fast("a"), Heal("patch", 0.5)), Wall("b"));
            Assert.That(Play(r, plain, 3).Any(x => x.Skill?.Id == "patch"), Is.False);

            var s = Start(r, With(Fast("a", null, RuleModifiers.Mudwork), Heal("patch", 0.5)), Wall("b"));
            var u = s.Units[0];
            u.Hp = u.MaxHp / 2;
            var log = Play(r, s, 1);
            Assert.That(log.Single(x => x.Skill?.Id == "patch").Amount, Is.EqualTo(0), "nothing healed");
            Assert.That(u.Hp, Is.EqualTo(u.MaxHp / 2), "no HP from the heal");
            double heal = 0.5 * 300;
            Assert.That(heal, Is.LessThan(r.MudCap * u.MaxHp), "the test heal is under the cap");
            Assert.That(u.Shield, Is.EqualTo(heal).Within(1e-9));
            Assert.That(u.ShieldTicks, Is.GreaterThan(0).And.LessThanOrEqualTo(r.MudTurns * r.TurnTicks), "a timed shield");
            Assert.That(u.Tally.HealingDone, Is.EqualTo(0));
            Assert.That(log.Single(x => x.Event == BattleSim.MudEvent).Amount, Is.EqualTo(heal).Within(1e-9));
        }

        [Test]
        public void Mudwork_WithoutBarricade_TakesTheLarger_WithBarricade_Stacks()
        {
            var r = Rules;
            var mend = Heal("mend", 0.5);
            var s = Start(r, OnAction(Fast("a", x => x.Hp = 3000, RuleModifiers.Mudwork), mend), Wall("b"));
            Play(r, s, 4);
            Assert.That(s.Units[0].Shield, Is.EqualTo(150).Within(1e-9), "larger of: one heal's shield");

            var st = Start(r, OnAction(Fast("a", x => x.Hp = 3000, RuleModifiers.Mudwork, RuleModifiers.Barricade), mend), Wall("b"));
            Play(r, st, 4);
            Assert.That(st.Units[0].Shield, Is.EqualTo(600).Within(1e-9), "four logs on the pile");
            Assert.That(st.Units[0].ShieldTicks, Is.EqualTo(0), "barricade: no expiry");

            // The pile stops at barricade's cap, above mudwork's own.
            var cap = Start(r, OnAction(Fast("a", x => x.Hp = 300, RuleModifiers.Mudwork, RuleModifiers.Barricade), Heal("mend", 2)), Wall("b"));
            Play(r, cap, 6);
            Assert.That(cap.Units[0].Shield, Is.EqualTo(r.BarricadeCap * cap.Units[0].MaxHp).Within(1e-9));

            // Alone a big heal is cut to mudwork's cap.
            var big = Start(r, OnAction(Fast("a", x => x.Hp = 300, RuleModifiers.Mudwork), Heal("mend", 20)), Wall("b"));
            Play(r, big, 1);
            Assert.That(big.Units[0].Shield, Is.EqualTo(r.MudCap * big.Units[0].MaxHp).Within(1e-9));
        }

        [Test]
        public void Mudwork_ConvertsRegenLifestealAndAlliesHeals_AfterTheHealCut()
        {
            var r = Rules;
            // tidal_overflow's regeneration: shield, up to the larger of the two caps
            var tide = Start(r, Fast("a", x => x.Hp = 300, RuleModifiers.Mudwork, RuleModifiers.TidalOverflow), Wall("b"));
            Play(r, tide, 1);
            double regen = r.TideRegen * tide.Units[0].MaxHp;
            Assert.That(tide.Units[0].Shield, Is.EqualTo(Math.Min(regen, Math.Max(r.MudCap, r.TideShieldCap) * tide.Units[0].MaxHp)).Within(1e-9));
            Assert.That(tide.Units[0].Hp, Is.EqualTo(tide.Units[0].MaxHp));

            // abyssal_drain's lifesteal on a hit
            var drain = Start(r, Fast("a", x => x.Hp = 3000, RuleModifiers.Mudwork, RuleModifiers.AbyssalDrain), Wall("b"));
            drain.Units[0].Hp = 10;
            var log = Play(r, drain, 1);
            double dealt = log.Single(x => x.Actor == "a" && x.Event == null && x.Target == "b").Amount;
            Assert.That(drain.Units[0].Shield, Is.EqualTo(r.DrainFrac * dealt).Within(1e-9));
            Assert.That(drain.Units[0].Hp, Is.EqualTo(10));

            // The heal cut (a heal mutator, like the Dungeon's) shrinks the converted heal.
            var cut = Rules;
            cut.Mutators = new FightMutators().Add(MutatorKinds.HealMult, 0.5);
            var half = Start(cut, OnAction(Fast("a", x => x.Hp = 3000, RuleModifiers.Mudwork), Heal("mend", 0.5)), Wall("b"));
            Play(cut, half, 1);
            Assert.That(half.Units[0].Shield, Is.EqualTo(75).Within(1e-9));

            // An ally's heal on the beaver becomes its shield too.
            var team = BattleSim.Start(r, new[] { Fast("a", x => x.Hp = 3000, RuleModifiers.Mudwork), With(Fast("h", x => x.Spd = 800), Heal("mend", 1, SkillTarget.LowestAlly)) },
                new[] { Wall("b") });
            team.Units[0].Hp = team.Units[0].MaxHp / 2;
            var tlog = new List<FightAction>();
            while (team.Units[1].Tally.Actions < 1) BattleSim.Step(r, team, new Fixed(0.99), tlog.Add);
            Assert.That(team.Units[0].Shield, Is.EqualTo(300).Within(1e-9));
            Assert.That(team.Units[0].Hp, Is.EqualTo(team.Units[0].MaxHp / 2));
        }

        [Test]
        public void Mudwork_ComesBeforeOtherHealConversions()
        {
            var r = Rules;
            // pyre_mercy and martyr_flame on the same unit see nothing: no HP was healed.
            var s = Start(r, OnAction(Fast("a", x => x.Hp = 3000, RuleModifiers.Mudwork, RuleModifiers.PyreMercy, RuleModifiers.MartyrFlame), Heal("mend", 0.5)), Wall("b"));
            s.Units[0].Hp = 100;
            var log = Play(r, s, 2);
            Assert.That(log.Any(x => x.Event == BattleSim.MercyEvent || x.Event == FightAction.RulePrefix + RuleModifiers.MartyrFlame), Is.False);
            Assert.That(s.Units[0].Shield, Is.EqualTo(150).Within(1e-9));
        }

        [Test]
        public void ShieldAi_BarricadeStacksWhileTheCastFits_HoldbackCountsTheWash()
        {
            var r = Rules;
            bool Casts(Action<FightUnit> set, params string[] rules)
            {
                var s = Start(r, With(Fast("a", x => x.Hp = 1000, rules), Shield("gate", 1.0)), Wall("b"));
                set(s.Units[0]);
                return Play(r, s, 1).Any(x => x.Skill?.Id == "gate");
            }
            double cap(FightUnit u) => r.BarricadeCap * u.MaxHp;   // 6000 at 1000 HP; the cast is 300
            Assert.That(Casts(u => u.Shield = 400), Is.False, "plain: a bigger shield is kept, the cast would change nothing");
            Assert.That(Casts(u => u.Shield = 400, RuleModifiers.Barricade), Is.True, "barricade: it stacks");
            Assert.That(Casts(u => u.Shield = cap(u) - 300, RuleModifiers.Barricade), Is.True, "fits in full");
            Assert.That(Casts(u => u.Shield = cap(u) - 299, RuleModifiers.Barricade), Is.False, "would overflow the cap");
            Assert.That(Casts(u => { u.Shield = 400; u.Reservoir = 50 / (1 - r.DamFlow); }, RuleModifiers.Holdback), Is.False,
                "washes 50; the 250 left is under the shield it has");
            Assert.That(Casts(u => { u.Shield = 400; u.Reservoir = 1000; }, RuleModifiers.Holdback), Is.True, "all of it washes out");
            Assert.That(Casts(u => { u.Shield = 100; u.Reservoir = 50 / (1 - r.DamFlow); }, RuleModifiers.Holdback), Is.True, "the rest beats the shield");
        }

        // ------------------------------------------------------------------ tail_slap

        [Test]
        public void TailSlap_AddsAShareOfTheShield_WithoutSpendingIt()
        {
            var r = Rules;
            double raw = BattleSim.BasicAttackRaw(r, 300, 0);
            // no shield: no bonus, no log
            var bare = Start(r, Fast("a", null, RuleModifiers.TailSlap), Wall("b"));
            var log = Play(r, bare, 3);
            Assert.That(log.Where(x => x.Actor == "a" && x.Target == "b" && x.Event == null).Select(x => x.Amount),
                Is.All.EqualTo(BattleSim.Mitigate(r, raw, 300)).Within(1e-9));
            Assert.That(log.Any(x => x.Event == BattleSim.SlapEvent), Is.False);

            var s = Start(r, Fast("a", null, RuleModifiers.TailSlap), Wall("b"));
            s.Units[0].Shield = 500;
            log = Play(r, s, 3);
            double add = r.SlapShield * 500;
            Assert.That(log.Where(x => x.Actor == "a" && x.Target == "b" && x.Event == null).Select(x => x.Amount),
                Is.All.EqualTo(BattleSim.Mitigate(r, raw + add, 300)).Within(1e-9));
            Assert.That(log.Where(x => x.Event == BattleSim.SlapEvent).Select(x => (x.Actor, x.Target, x.Amount)), Is.All.EqualTo(("a", "b", add)));
            Assert.That(s.Units[0].Shield, Is.EqualTo(500), "the wall swings, it does not come down");
        }

        [Test]
        public void TailSlap_AreaSkillAddsToEachTarget_TrueDamageGetsNothing_EvadedHitsLogNothing()
        {
            var r = Rules;
            var quake = Dmg("quake", 1.0, SkillTarget.AllEnemies);
            var s = BattleSim.Start(r, new[] { With(Fast("a", null, RuleModifiers.TailSlap), quake) }, new[] { Wall("b1"), Wall("b2") });
            s.Units[0].Shield = 400;
            var log = Play(r, s, 1);
            Assert.That(log.Where(x => x.Event == BattleSim.SlapEvent).Select(x => x.Target), Is.EquivalentTo(new[] { "b1", "b2" }));

            // counter_mitigated's counter is true damage: it never carries the bonus, however big the wall
            var walled = Start(r, Fast("a"), Wall("b", x => x.Def = 900, RuleModifiers.CounterMitigated, RuleModifiers.TailSlap));
            walled.Units[1].Shield = 1e6;
            var wl = Play(r, walled, 2);
            Assert.That(wl.Count(x => x.Counter && x.Actor == "b"), Is.EqualTo(2));
            Assert.That(wl.Any(x => x.Event == BattleSim.SlapEvent), Is.False);

            // every roll succeeds: each hit is evaded, so nothing is logged
            var ev = Start(r, Fast("a", null, RuleModifiers.TailSlap), Wall("b", x => x.Dex = 900));
            ev.Units[0].Shield = 400;
            Assert.That(Play(r, ev, 3, 0).Any(x => x.Event == BattleSim.SlapEvent), Is.False);
        }

        // ------------------------------------------------------------------ holdback

        [Test]
        public void Holdback_HoldsHpDamageBack_ButCountsItAsDealt()
        {
            var r = Rules;
            var s = Start(r, Fast("a"), Wall("b", null, RuleModifiers.Holdback));
            var b = s.Units[1];
            b.Shield = 50;
            var log = Play(r, s, 3);
            double hit = BattleSim.Mitigate(r, BattleSim.BasicAttackRaw(r, 300, 0), 300);
            var hits = log.Where(x => x.Actor == "a" && x.Event == null).ToList();
            Assert.That(hits[0].Amount, Is.EqualTo(hit - 50).Within(1e-9), "the first hit breaks the shield; the rest is held back");
            Assert.That(hits.Skip(1).Select(x => x.Amount), Is.All.EqualTo(hit).Within(1e-9));
            Assert.That(b.Hp, Is.EqualTo(b.MaxHp), "no HP lost yet");
            Assert.That((b.Shield, b.Reservoir), Is.EqualTo((0.0, 3 * hit - 50)).Within(1e-9));
            Assert.That(s.Units[0].Tally.DamageDealt, Is.EqualTo(3 * hit - 50).Within(1e-9), "counted when stored");
            Assert.That(b.Tally.DamageTaken, Is.EqualTo(3 * hit - 50).Within(1e-9));
            Assert.That(b.DamLastHit, Is.EqualTo("a"));
            var holds = log.Where(x => x.Event == BattleSim.HoldEvent).ToList();
            Assert.That(holds.Select(x => (x.Actor, x.Target)), Is.All.EqualTo(("b", "a")));
            Assert.That(holds.Last().Amount, Is.EqualTo(b.Reservoir).Within(1e-9));
        }

        [Test]
        public void Holdback_StoredBlowStillFiresOnHitTaken()
        {
            var r = Rules;
            var thorns = new CombatSkill { Id = "thorns", Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = StatIds.Def, Power = 0.1 };
            var dam = Wall("b", null, RuleModifiers.Holdback);
            dam.Triggers.Add(new TriggeredSkill(FightTrigger.OnHitTaken, thorns));
            var s = Start(r, Fast("a"), dam);
            var log = Play(r, s, 3);
            Assert.That(log.Count(x => x.Actor == "b" && x.Skill?.Id == "thorns"), Is.EqualTo(3));
        }

        [Test]
        public void Holdback_DrainsAtTheStartOfEachOwnAction_NotAsAHit()
        {
            var r = Rules;
            var s = Start(r, Fast("a", null, RuleModifiers.Holdback), Wall("b"));
            var a = s.Units[0];
            a.Reservoir = 100;
            a.DamLastHit = "b";
            var log = Play(r, s, 2);
            double left = 100 * (1 - r.DamFlow) * (1 - r.DamFlow);
            Assert.That(a.Reservoir, Is.EqualTo(left).Within(1e-9));
            Assert.That(a.MaxHp - a.Hp, Is.EqualTo(100 - left).Within(1e-9));
            Assert.That(a.Tally.DamageTaken, Is.EqualTo(0), "not a hit: no tally");
            Assert.That(s.Units[1].Tally.DamageDealt, Is.EqualTo(0));
            var drains = log.Where(x => x.Event == BattleSim.HoldEvent).ToList();
            Assert.That(drains.Select(x => (x.Actor, x.Target)), Is.All.EqualTo(("a", "a")));
            Assert.That(drains.Count, Is.EqualTo(2));
            Assert.That(drains[0].Amount, Is.EqualTo(100 * (1 - r.DamFlow)).Within(1e-9));
        }

        [Test]
        public void Holdback_DeathByDrain_IsCreditedToTheLastEnemyThatHit()
        {
            var r = Rules;
            var s = Start(r, Fast("a", null, RuleModifiers.Holdback), Wall("b"));
            var a = s.Units[0];
            a.Hp = 10;
            a.Reservoir = 1000;
            a.DamLastHit = "b";
            var log = new List<FightAction>();
            while (s.Outcome == FightOutcome.Ongoing) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(s.Outcome, Is.EqualTo(FightOutcome.SideBWon));
            Assert.That(a.Tally.Actions, Is.EqualTo(0), "it died before it could act");
            Assert.That((a.KilledBy, s.Units[1].Tally.Kills), Is.EqualTo(("b", 1)));
        }

        [Test]
        public void Holdback_DeathsDoorAndRootRebirth_CatchTheDrainThatWouldKill()
        {
            var r = Rules;
            var door = Start(r, Fast("a", null, RuleModifiers.Holdback, RuleModifiers.DeathsDoor), Wall("b"));
            door.Units[0].Hp = 10;
            door.Units[0].Reservoir = 1000;
            var log = Play(r, door, 1);
            Assert.That(door.Units[0].Alive, Is.True);
            Assert.That(door.Units[0].Hp, Is.EqualTo(1));
            Assert.That(log.Any(x => x.Event == FightAction.RulePrefix + RuleModifiers.DeathsDoor), Is.True);

            var root = Start(r, Fast("a", null, RuleModifiers.Holdback, RuleModifiers.RootRebirth), Wall("b"));
            root.Units[0].Hp = 10;
            root.Units[0].Reservoir = 1000;
            var rl = Play(r, root, 1);
            Assert.That(root.Units[0].Hp, Is.EqualTo(r.RebirthHeal * root.Units[0].MaxHp).Within(1e-9));
            Assert.That(rl.Any(x => x.Event == FightAction.RebirthEvent), Is.True);
        }

        [Test]
        public void Holdback_TheDamBreaks_AtTheBreakHeight()
        {
            var r = Rules;
            // Each hit is 40% of max HP: the third fills the dam past MON_DAM_BREAK x max HP and everything lands at once.
            Assume.That(r.DamBreak, Is.EqualTo(1.0));
            double str = 0.4 * BattleSim.MaxHp(r, 100) / BattleSim.BasicAttackRaw(r, 1, 0);
            var s = Start(r, Fast("a", x => x.Str = str), Wall("b", x => { x.Hp = 100; x.Def = 0; }, RuleModifiers.Holdback));
            var b = s.Units[1];
            var log = new List<FightAction>();
            while (s.Units[0].Tally.Actions < 2) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That((b.Alive, b.Hp), Is.EqualTo((true, b.MaxHp)));
            while (s.Outcome == FightOutcome.Ongoing) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            Assert.That(s.Units[0].Tally.Actions, Is.EqualTo(3));
            Assert.That((b.Alive, b.Reservoir, b.KilledBy, s.Units[0].Tally.Kills), Is.EqualTo((false, 0.0, "a", 1)));
            Assert.That(log.Last(x => x.Event == BattleSim.HoldEvent).Amount, Is.EqualTo(0));
        }

        [Test]
        public void Holdback_AShieldWashesTheReservoirOut_AndHitsBack()
        {
            var r = Rules;
            // A shield bigger than the Reservoir: it empties it, the rest is shield, and half the washed-out damage hits the foe.
            var s = Start(r, With(Fast("a", null, RuleModifiers.Holdback), Shield("gate", 1.2)), Wall("b", x => x.Hp = 1000));
            var a = s.Units[0];
            a.Reservoir = 100;
            var log = Play(r, s, 1);
            double stored = 100 * (1 - r.DamFlow);   // the drain comes first, at the start of the action
            Assert.That(a.Reservoir, Is.EqualTo(0));
            Assert.That(a.Shield, Is.EqualTo(1.2 * 300 - stored).Within(1e-9));
            var foe = s.Units[1];
            Assert.That(foe.MaxHp - foe.Hp, Is.EqualTo(r.DamSpill * stored).Within(1e-9), "true damage: no mitigation");
            Assert.That(a.Tally.DamageByElement[RuleModifiers.NatureElement], Is.EqualTo(r.DamSpill * stored).Within(1e-9));
            var wash = log.Last(x => x.Event == BattleSim.HoldEvent);
            Assert.That((wash.Actor, wash.Target, wash.Amount), Is.EqualTo(("a", "b", 0.0)));

            // A shield smaller than the Reservoir: all of it washes out, none is left for the shield.
            var small = Start(r, With(Fast("a", null, RuleModifiers.Holdback), Shield("gate", 0.1)), Wall("b", x => x.Hp = 1000));
            small.Units[0].Reservoir = 1000;
            Play(r, small, 1);
            Assert.That(small.Units[0].Reservoir, Is.EqualTo(1000 * (1 - r.DamFlow) - 30).Within(1e-9));
            Assert.That((small.Units[0].Shield, small.Units[0].ShieldTicks), Is.EqualTo((0.0, 0)));
            Assert.That(small.Units[1].MaxHp - small.Units[1].Hp, Is.EqualTo(r.DamSpill * 30).Within(1e-9));
        }

        [Test]
        public void Holdback_WithMudwork_HealsWashTheDamOut()
        {
            var r = Rules;
            var s = Start(r, With(Fast("a", x => x.Hp = 3000, RuleModifiers.Holdback, RuleModifiers.Mudwork, RuleModifiers.Barricade), Heal("patch", 1.0)),
                Wall("b", x => x.Hp = 1000));
            var a = s.Units[0];
            a.Reservoir = 100;
            var log = Play(r, s, 1);
            Assert.That(log.Any(x => x.Skill?.Id == "patch"), Is.True, "the AI casts it to wash the dam out");
            Assert.That(a.Reservoir, Is.EqualTo(0));
            Assert.That(a.Shield, Is.EqualTo(300 - 100 * (1 - r.DamFlow)).Within(1e-9));
        }

        [Test]
        public void Holdback_TwoCarriers_EndBeforeTheTimeLimit_AndTheReservoirIsHashedForCarriersOnly()
        {
            var r = Rules;
            // The Reservoir always drains and every wash hits back: two dams whose blows outweigh their shields wear each other
            // down (nothing is held back forever).
            Fighter Dam(string id) => With(Mon(id, x => { x.Hp = 300; x.Str = 1200; x.Def = 500; x.Spd = 60; }, "nature",
                RuleModifiers.Holdback, RuleModifiers.Mudwork), Heal("mud_patch", 1.0), Shield("sluice_gate", 1.2));
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var res = BattleSim.Resolve(r, new[] { Dam("a") }, new[] { Dam("b") }, seed);
                Assert.That(res.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut), "seed " + seed);
            }

            FightState Fight(params string[] rules)
            {
                var s = Start(r, Fast("a"), Wall("b", null, rules));
                while (s.Units[0].Tally.Actions < 2) BattleSim.Step(r, s, new Fixed(0.99));
                return s;
            }
            var x1 = Fight(RuleModifiers.Holdback);
            var x2 = Fight(RuleModifiers.Holdback);
            Assert.That(BattleSim.StateHash(x1), Is.EqualTo(BattleSim.StateHash(x2)));
            x2.Units[1].Reservoir += 1;
            Assert.That(BattleSim.StateHash(x1), Is.Not.EqualTo(BattleSim.StateHash(x2)));
            var plain = Fight();
            string h = BattleSim.StateHash(plain);
            plain.Units[1].Reservoir = 7;
            Assert.That(BattleSim.StateHash(plain), Is.EqualTo(h), "not hashed without the rule");
        }
    }
}
