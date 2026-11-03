using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Species Fountain Axolotl (#150): its content and the rules clear_spring, seep and fountain_jets.</summary>
    public class FountainAxolotlRuleTests
    {
        static FightRules Rules => FightRules.FromContent(TestContent.Db);

        static readonly string[] SpeciesRules = { RuleModifiers.ClearSpring, RuleModifiers.Seep, RuleModifiers.FountainJets };

        /// <summary>Always the same draw: 0 = every evade and crit roll succeeds, 0.99 = none does.</summary>
        sealed class Fixed : IRandomSource
        {
            readonly double _v;
            public Fixed(double v) { _v = v; }
            public double NextDouble() => _v;
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

        static CombatSkill Dmg(string id, double power, int cd = 0, string element = null, SkillTarget target = SkillTarget.Enemy) =>
            new CombatSkill { Id = id, Kind = SkillKind.Damage, Target = target, Scales = StatIds.Int, Power = power, Cooldown = cd, Element = element };

        static Fighter With(Fighter f, params CombatSkill[] skills) { f.Skills.AddRange(skills); return f; }

        /// <summary>Steps until side A's first unit has taken <paramref name="actions"/> actions (side B must not act).</summary>
        static List<FightAction> Play(FightRules r, Fighter a, Fighter b, int actions, double draw, Action<FightState> setup = null)
        {
            var s = BattleSim.Start(r, new[] { a }, new[] { b });
            setup?.Invoke(s);
            var log = new List<FightAction>();
            while (s.Units[0].Tally.Actions < actions && s.Outcome == FightOutcome.Ongoing) BattleSim.Step(r, s, new Fixed(draw), log.Add);
            Assert.That(s.Units[1].Tally.Actions, Is.EqualTo(0), "side B never acted");
            return log;
        }

        /// <summary>Side A's hits on b (basic attacks and skills, not rule events).</summary>
        static List<FightAction> Hits(List<FightAction> log) => log.Where(x => x.Actor == "a" && x.Event == null && x.Target == "b" && !x.Counter).ToList();

        // ------------------------------------------------------------------ content and constants

        [Test]
        public void Content_SpeciesTreesAndConstants()
        {
            var db = TestContent.Db;
            var r = Rules;
            Assert.That((r.SeepShield, r.Jets, r.JetPower), Is.EqualTo((db.ConstantValues["MON_SEEP_SHIELD"], (int)db.ConstantValues["MON_JETS"],
                db.ConstantValues["MON_JET_POWER"])));
            var sp = db.SpeciesList.Single(x => x.Id == "fountain_axolotl");
            Assert.That((sp.Element, sp.NativeTree1, sp.NativeTree2, sp.ChaseTreeRun, sp.ChaseTreeBook),
                Is.EqualTo(("water", "elemental_avatar", "clear_spring", "aquifer", "six_fountains")));
            Assert.That(db.SpeciesList[0].Id, Is.Not.EqualTo("fountain_axolotl"), "appended, never the starter");

            // The idea-pool tree is native-only now: never offered by an Awakening, owned by this species alone.
            var avatar = db.Trees["elemental_avatar"];
            Assert.That((avatar.Kind, avatar.OfferStat, avatar.OfferThreshold), Is.EqualTo((TreeKind.Native, (string)null, 0)));
            Assert.That(db.SpeciesList.Count(x => x.NativeTree1 == "elemental_avatar" || x.NativeTree2 == "elemental_avatar"), Is.EqualTo(1));

            var caps = new Dictionary<string, string>
            {
                ["clear_spring"] = RuleModifiers.ClearSpring, ["aquifer"] = RuleModifiers.Seep, ["six_fountains"] = RuleModifiers.FountainJets,
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
            Assert.That(db.Trees["clear_spring"].Kind, Is.EqualTo(TreeKind.Native));
            Assert.That(db.Trees["aquifer"].Nodes.Sum(n => n.Cost), Is.EqualTo(11), "in-run chase: 11 SP");
            Assert.That(db.Trees["six_fountains"].Nodes.Sum(n => n.Cost), Is.EqualTo(9), "book chase: priced like Arcane Overflow");
            Assert.That(db.Trees["six_fountains"].BookGated, Is.True);
            Assert.That(db.Trees["springs_almanac"].Kind, Is.EqualTo(TreeKind.Book));
            Assert.That(db.SkillBooks["book_springs"].TreeId, Is.EqualTo("springs_almanac"));
            Assert.That(db.BookDrops.Single(b => b.BookId == "book_springs").MinFloor, Is.EqualTo(10));
            var unlock = db.Achievements["deep_spring"];
            Assert.That(unlock.UnlocksSpecies, Is.EqualTo("fountain_axolotl"));
            Assert.That(db.Text("tree.aquifer.apex"), Is.EqualTo("Aquifer Olm"));
            Assert.That(db.Text("tree.six_fountains.apex"), Is.EqualTo("Xolotl Wellspring"));
        }

        [Test]
        public void Validation_RejectsConstantsOutOfRange()
        {
            var a = new[] { Mon("a") };
            var b = new[] { Mon("b") };
            foreach (var bad in new Action<FightRules>[]
                     {
                         r => r.SeepShield = -0.1, r => r.SeepShield = 1.1, r => r.Jets = 1, r => r.Jets = 7,
                         r => r.JetPower = 1.1, r => r.JetPower = double.NaN,
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
            BattleSim.Resolve(Rules, new[] { Mon("a", null, "water") }, new[] { Mon("b", null, "nature") }, 1, log.Add);
            Assert.That(log.Any(x => x.Event != null && SpeciesRules.Any(id => x.Event == FightAction.RulePrefix + id)), Is.False);
        }

        // ------------------------------------------------------------------ clear_spring

        [Test]
        public void ClearSpring_CancelsTheWeakMatchup_OfItsOwnElement()
        {
            var r = Rules;
            double plain = BattleSim.Mitigate(r, BattleSim.BasicAttackRaw(r, 300, 300), 300);
            var off = Hits(Play(r, Fast("a", null, "water"), Wall("b", null, "nature"), 3, 0.99));
            Assert.That(off.Select(x => x.Amount), Is.All.EqualTo(plain * r.ElementWeakMult).Within(1e-9));
            var log = Play(r, Fast("a", null, "water", RuleModifiers.ClearSpring), Wall("b", null, "nature"), 3, 0.99);
            Assert.That(Hits(log).Select(x => x.Amount), Is.All.EqualTo(plain).Within(1e-9));
            var ev = log.Where(x => x.Event == BattleSim.ClearSpringEvent).ToList();
            Assert.That(ev.Count, Is.EqualTo(3));
            Assert.That(ev.All(x => x.Actor == "a" && x.Target == "b" && Math.Abs(x.Amount - r.ElementWeakMult) < 1e-12), Is.True);

            // A strong matchup still counts.
            var strong = Hits(Play(r, Fast("a", null, "water", RuleModifiers.ClearSpring), Wall("b", null, "fire"), 3, 0.99));
            Assert.That(strong.Select(x => x.Amount), Is.All.EqualTo(plain * r.ElementStrongMult).Within(1e-9));
        }

        [Test]
        public void ClearSpring_WithoutAvatar_OnlyItsOwnElement()
        {
            // A fire skill of a water carrier against water stays weak: only hits of its own element lose the weakness.
            var r = Rules;
            var flame = Dmg("flame", 1.0, 0, "fire");
            double hit = BattleSim.Mitigate(r, 300 * 1.0, 300);
            var log = Play(r, With(Fast("a", null, "water", RuleModifiers.ClearSpring), flame), Wall("b", null, "water"), 1, 0.99);
            Assert.That(Hits(log).Single().Amount, Is.EqualTo(hit * r.ElementWeakMult).Within(1e-9));
            Assert.That(log.Any(x => x.Event == BattleSim.ClearSpringEvent), Is.False);

            // With elemental_avatar every hit is water, so the same skill on a nature target is not weak any more.
            var avatar = Play(r, With(Fast("a", null, "water", RuleModifiers.ClearSpring, RuleModifiers.ElementalAvatar), flame), Wall("b", null, "nature"), 1, 0.99);
            Assert.That(Hits(avatar).Single().Amount, Is.EqualTo(hit * (1 + r.AvatarBonus)).Within(1e-9));
            Assert.That(avatar.Count(x => x.Event == BattleSim.ClearSpringEvent), Is.EqualTo(1));
        }

        [Test]
        public void ClearSpring_TargetTakesNoStrongHits_OfTheElementThatBeatsIt()
        {
            var r = Rules;
            double plain = BattleSim.Mitigate(r, BattleSim.BasicAttackRaw(r, 300, 300), 300);
            var off = Hits(Play(r, Fast("a", null, "nature"), Wall("b", null, "water"), 3, 0.99));
            Assert.That(off.Select(x => x.Amount), Is.All.EqualTo(plain * r.ElementStrongMult).Within(1e-9));
            var log = Play(r, Fast("a", null, "nature"), Wall("b", null, "water", RuleModifiers.ClearSpring), 3, 0.99);
            Assert.That(Hits(log).Select(x => x.Amount), Is.All.EqualTo(plain).Within(1e-9));
            Assert.That(log.Where(x => x.Event == BattleSim.ClearSpringEvent).All(x => x.Actor == "b" && x.Target == "a"), Is.True);
        }

        [Test]
        public void ClearSpring_LightAndDark_CancelsTheHitsItTakes_KeepsItsOwn()
        {
            // Light and dark beat each other. A light carrier takes dark hits at 1 and still deals its light hits at the strong rate.
            var r = Rules;
            double plain = BattleSim.Mitigate(r, BattleSim.BasicAttackRaw(r, 300, 300), 300);
            Assert.That(Hits(Play(r, Fast("a", null, "dark"), Wall("b", null, "light", RuleModifiers.ClearSpring), 2, 0.99)).Select(x => x.Amount),
                Is.All.EqualTo(plain).Within(1e-9));
            Assert.That(Hits(Play(r, Fast("a", null, "light", RuleModifiers.ClearSpring), Wall("b", null, "dark"), 2, 0.99)).Select(x => x.Amount),
                Is.All.EqualTo(plain * r.ElementStrongMult).Within(1e-9));
        }

        // ------------------------------------------------------------------ seep

        [Test]
        public void Seep_MitigatedByTheLowerOfDefAndWis()
        {
            var r = Rules;
            double raw = BattleSim.BasicAttackRaw(r, 300, 300);   // physical: STR is not below INT
            Action<MonsterStats> armoured = x => { x.Def = 600; x.Wis = 100; };
            Assert.That(Hits(Play(r, Fast("a"), Wall("b", armoured), 2, 0.99)).Select(x => x.Amount),
                Is.All.EqualTo(BattleSim.Mitigate(r, raw, 600)).Within(1e-9));
            var log = Play(r, Fast("a", null, null, RuleModifiers.Seep), Wall("b", armoured), 2, 0.99);
            Assert.That(Hits(log).Select(x => x.Amount), Is.All.EqualTo(BattleSim.Mitigate(r, raw, 100)).Within(1e-9));
            var ev = log.Where(x => x.Event == BattleSim.SeepEvent).ToList();
            Assert.That(ev.Count, Is.EqualTo(2));
            Assert.That(ev.All(x => x.Amount == 100), Is.True, "the log names the defence used");

            // Magical hits against a low DEF use DEF.
            Action<MonsterStats> mage = x => x.Int = 400;
            var magic = Hits(Play(r, Fast("a", mage, null, RuleModifiers.Seep), Wall("b", x => { x.Def = 50; x.Wis = 500; }), 2, 0.99));
            Assert.That(magic.Select(x => x.Amount), Is.All.EqualTo(BattleSim.Mitigate(r, BattleSim.BasicAttackRaw(r, 300, 400), 50)).Within(1e-9));
        }

        [Test]
        public void Seep_EqualDefAndWis_ChangesNothing()
        {
            var r = Rules;
            var off = Hits(Play(r, Fast("a"), Wall("b"), 3, 0.99));
            var log = Play(r, Fast("a", null, null, RuleModifiers.Seep), Wall("b"), 3, 0.99);
            Assert.That(Hits(log).Select(x => x.Amount), Is.EqualTo(off.Select(x => x.Amount)));
            Assert.That(log.Any(x => x.Event == BattleSim.SeepEvent), Is.False);
        }

        [Test]
        public void Seep_ShieldBiggerThanTheHit_PartGoesStraightToHp()
        {
            var r = Rules;
            double hit = BattleSim.Mitigate(r, BattleSim.BasicAttackRaw(r, 300, 300), 300);
            FightState s1 = null, s2 = null;
            var off = Hits(Play(r, Fast("a"), Wall("b"), 1, 0.99, s => { s.Units[1].Shield = 1e6; s1 = s; }));
            Assert.That(off.Single().Amount, Is.EqualTo(0), "the shield takes it all");
            Assert.That(s1.Units[1].Shield, Is.EqualTo(1e6 - hit).Within(1e-6));

            var log = Play(r, Fast("a", null, null, RuleModifiers.Seep), Wall("b"), 1, 0.99, s => { s.Units[1].Shield = 1e6; s2 = s; });
            Assert.That(Hits(log).Single().Amount, Is.EqualTo(r.SeepShield * hit).Within(1e-9));
            Assert.That(s2.Units[1].Shield, Is.EqualTo(1e6 - (1 - r.SeepShield) * hit).Within(1e-6));
            Assert.That(s2.Units[1].MaxHp - s2.Units[1].Hp, Is.EqualTo(r.SeepShield * hit).Within(1e-9));
            Assert.That(log.Count(x => x.Event == BattleSim.SeepEvent), Is.EqualTo(1), "logged: the shield was bypassed");
        }

        // ------------------------------------------------------------------ fountain_jets

        [Test]
        public void Jets_SplitABasicAttack_IntoSeparateHits()
        {
            var r = Rules;
            double raw = BattleSim.BasicAttackRaw(r, 300, 300);
            var log = Play(r, Fast("a", null, null, RuleModifiers.FountainJets), Wall("b"), 1, 0.99);
            var hits = Hits(log);
            Assert.That(hits.Count, Is.EqualTo(r.Jets));
            Assert.That(hits.Select(x => x.Amount), Is.All.EqualTo(BattleSim.Mitigate(r, raw * r.JetPower, 300)).Within(1e-9));
            var ev = log.Single(x => x.Event == BattleSim.JetsEvent);
            Assert.That((ev.Actor, ev.Target, ev.Amount), Is.EqualTo(("a", "b", (double)r.Jets)));
            Assert.That(log.IndexOf(ev), Is.GreaterThan(log.IndexOf(hits.Last())), "logged after the jets");
        }

        [Test]
        public void Jets_TargetDiesAfterJetOne_TheRestAreLost()
        {
            var r = Rules;
            var log = Play(r, Fast("a", null, null, RuleModifiers.FountainJets), Wall("b", x => x.Hp = 1), 1, 0.99);
            Assert.That(Hits(log).Count, Is.EqualTo(1));
            Assert.That(log.Single(x => x.Event == BattleSim.JetsEvent).Amount, Is.EqualTo(1));
        }

        [Test]
        public void Jets_EachJetRollsEvade_AndFiresOnEvade()
        {
            var r = Rules;
            var dodge = new CombatSkill { Id = "dodge", Kind = SkillKind.Buff, Target = SkillTarget.Self, Stat = StatIds.Dex, Power = 0.01, Duration = 1 };
            var b = Wall("b", x => x.Dex = 900);
            b.Triggers.Add(new TriggeredSkill(FightTrigger.OnEvade, dodge));
            FightState st = null;
            var log = Play(r, Fast("a", x => x.Dex = 0, null, RuleModifiers.FountainJets), b, 1, 0, s => st = s);
            Assert.That(Hits(log).All(x => x.Evaded), Is.True);
            Assert.That(st.Units[1].Tally.Evades, Is.EqualTo(r.Jets), "each jet counts for the evade tally");
            Assert.That(log.Count(x => x.Skill?.Id == "dodge"), Is.EqualTo(r.Jets), "OnEvade fires per jet");
            Assert.That(log.Single(x => x.Event == BattleSim.JetsEvent).Amount, Is.EqualTo(0), "no jet landed");
        }

        [Test]
        public void Jets_UmbralVeil_TurnsEachJetsGrazeIntoAMiss()
        {
            var r = Rules;
            FightState st = null;
            var log = Play(r, Fast("a", x => { x.Dex = 0; x.Int = 400; }, null, RuleModifiers.FountainJets),
                Wall("b", x => x.Dex = 900, null, RuleModifiers.UmbralVeil), 1, 0, s => st = s);
            Assert.That(Hits(log).Count, Is.EqualTo(r.Jets));
            Assert.That(Hits(log).All(x => x.Evaded && !x.Graze), Is.True);
            Assert.That(st.Units[1].Tally.Evades, Is.EqualTo(r.Jets));
        }

        [Test]
        public void Jets_EachJetCrits_AndFiresOnCrit_TriggeredHitsDoNotSplit()
        {
            var r = Rules;
            var spray = Dmg("spray", 0.35, 0, "water");
            var a = Fast("a", x => x.Dex = 900, null, RuleModifiers.FountainJets);   // no evade chance: every draw of 0 is a crit
            a.Triggers.Add(new TriggeredSkill(FightTrigger.OnCrit, spray));
            FightState st = null;
            var log = Play(r, a, Wall("b", x => x.Dex = 0), 1, 0, s => st = s);
            var jets = Hits(log).Where(x => x.Skill == null).ToList();
            Assert.That(jets.Count, Is.EqualTo(r.Jets));
            Assert.That(jets.All(x => x.Crit), Is.True);
            Assert.That(Hits(log).Count(x => x.Skill?.Id == "spray"), Is.EqualTo(r.Jets), "one spray per jet crit, none split");
            Assert.That(log.Count(x => x.Event == BattleSim.JetsEvent), Is.EqualTo(1), "only the basic attack split");
            Assert.That(st.Units[0].Tally.Crits, Is.EqualTo(2 * r.Jets));
        }

        [Test]
        public void Jets_AreaSkillsDoNotSplit()
        {
            var r = Rules;
            var rain = Dmg("rain", 0.9, 3, "water", SkillTarget.AllEnemies);
            var log = Play(r, With(Fast("a", null, null, RuleModifiers.FountainJets), rain), Wall("b"), 1, 0.99);
            Assert.That(Hits(log).Count, Is.EqualTo(1));
            Assert.That(log.Any(x => x.Event == BattleSim.JetsEvent), Is.False);
        }

        [Test]
        public void Jets_EchoCast_SplitsTheEchoToo_OneAction()
        {
            var r = Rules;
            var bolt = Dmg("bolt", 2.0, 3);
            FightState st = null;
            var log = Play(r, With(Fast("a", null, null, RuleModifiers.FountainJets, RuleModifiers.EchoCast), bolt), Wall("b"), 1, 0.99, s => st = s);
            var bolts = Hits(log).Where(x => x.Skill?.Id == "bolt").ToList();
            Assert.That(bolts.Count, Is.EqualTo(2 * r.Jets), "the cast and its echo each split");
            double jet = BattleSim.Mitigate(r, 300 * 2.0 * r.JetPower, 300);
            Assert.That(bolts.Take(r.Jets).Select(x => x.Amount), Is.All.EqualTo(jet).Within(1e-9));
            Assert.That(bolts.Skip(r.Jets).Select(x => x.Amount), Is.All.EqualTo(jet * r.EchoPower).Within(1e-9));
            Assert.That(log.Count(x => x.Event == BattleSim.JetsEvent), Is.EqualTo(2));
            Assert.That(st.Units[0].Tally.Actions, Is.EqualTo(1), "jets are hits, not actions");
        }

        [Test]
        public void Jets_FortressFist_SplitsItsFlatAddAcrossTheJets()
        {
            var r = Rules;
            double flat = r.FortressDef * 300;   // STR and INT count as 0: a basic attack is the flat add alone
            var one = Hits(Play(r, Fast("a", null, null, RuleModifiers.FortressFist), Wall("b"), 1, 0.99)).Single().Amount;
            Assert.That(one, Is.EqualTo(BattleSim.Mitigate(r, flat, 300)).Within(1e-9));
            var jets = Hits(Play(r, Fast("a", null, null, RuleModifiers.FortressFist, RuleModifiers.FountainJets), Wall("b"), 1, 0.99));
            Assert.That(jets.Count, Is.EqualTo(r.Jets));
            Assert.That(jets.Sum(x => x.Amount), Is.EqualTo(one).Within(1e-9), "the flat add is split, not repeated");
        }

        [Test]
        public void Jets_TotalPower_IsJetsTimesJetPower()
        {
            var r = Rules;
            var plain = Hits(Play(r, Fast("a"), Wall("b"), 4, 0.99)).Sum(x => x.Amount);
            var jets = Hits(Play(r, Fast("a", null, null, RuleModifiers.FountainJets), Wall("b"), 4, 0.99)).Sum(x => x.Amount);
            Assert.That(jets / plain, Is.EqualTo(r.Jets * r.JetPower).Within(1e-9));
        }

        [Test]
        public void Jets_Hemorrhage_EachJetFillsTheBleedGauge()
        {
            var r = Rules;
            double perHit = r.BleedPerHit * 300 / r.BleedDexRef;
            Assume.That(r.Jets * perHit, Is.LessThan(BattleSim.BleedGauge), "no burst in one action");
            FightState one = null, split = null;
            Play(r, Fast("a", null, null, RuleModifiers.Hemorrhage), Wall("b"), 1, 0.99, s => one = s);
            Play(r, Fast("a", null, null, RuleModifiers.Hemorrhage, RuleModifiers.FountainJets), Wall("b"), 1, 0.99, s => split = s);
            Assert.That(one.Units[1].Bleed, Is.EqualTo(perHit).Within(1e-9));
            Assert.That(split.Units[1].Bleed, Is.EqualTo(r.Jets * perHit).Within(1e-9), "a gauge multiplies with the jets");
        }

        [Test]
        public void Jets_GuardBreak_EachCritJetStripsAPip()
        {
            var r = Rules;
            Func<Fighter, List<FightAction>> breaks = a => Play(r, a, Wall("b", x => x.Dex = 0), 1, 0).Where(x => x.Event == BattleSim.BreakEvent).ToList();
            Assert.That(breaks(Fast("a", x => x.Dex = 900, null, RuleModifiers.GuardBreak)).Count, Is.EqualTo(1));
            var split = breaks(Fast("a", x => x.Dex = 900, null, RuleModifiers.GuardBreak, RuleModifiers.FountainJets));
            Assert.That(split.Count, Is.EqualTo(Math.Min(r.Jets, r.BreakPips)), "one pip per crit jet, until the guard breaks");
        }

        // ------------------------------------------------------------------ the species kit

        [Test]
        public void FullKit_ResolvesInEveryMode_Deterministically()
        {
            // 1v1, 4v1 and 4v4 with all three rules plus elemental_avatar: the same seed gives the same fight, and it ends.
            var r = Rules;
            string[] kit = { RuleModifiers.ElementalAvatar, RuleModifiers.ClearSpring, RuleModifiers.Seep, RuleModifiers.FountainJets };
            Fighter Axo(string id) => With(Mon(id, x => { x.Int = 500; x.Luk = 400; }, "water", kit), Dmg("riptide", 1.3, 2, "water"),
                Dmg("rain", 0.9, 3, "water", SkillTarget.AllEnemies));
            Fighter Foe(string id) => Mon(id, x => { x.Def = 500; x.Wis = 100; }, "nature", RuleModifiers.Barricade);
            foreach (var (a, b) in new[]
                     {
                         (new[] { Axo("a") }, new[] { Foe("b") }),
                         (new[] { Axo("a1"), Axo("a2"), Axo("a3"), Axo("a4") }, new[] { Foe("boss") }),
                         (new[] { Axo("a1"), Axo("a2"), Axo("a3"), Axo("a4") }, new[] { Foe("b1"), Foe("b2"), Foe("b3"), Foe("b4") }),
                     })
            {
                var one = BattleSim.Resolve(r, a, b, 7);
                var two = BattleSim.Resolve(r, a, b, 7);
                Assert.That(one.Hash, Is.EqualTo(two.Hash));
                Assert.That(one.Outcome, Is.Not.EqualTo(FightOutcome.Ongoing));
            }
        }
    }
}
