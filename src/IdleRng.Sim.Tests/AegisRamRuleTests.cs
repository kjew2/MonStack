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
    /// Species Aegis Ram (#147): fleece_aegis, brave_debt and ram_shock (specs/16 section 6.2, Combat/Rules/AegisRam.cs), with lane
    /// C's brave_heart as its native tree 1.
    /// </summary>
    public class AegisRamRuleTests
    {
        static FightRules Rules => FightRules.FromContent(TestContent.Db);

        static readonly string[] SpeciesRules = { RuleModifiers.FleeceAegis, RuleModifiers.BraveDebt, RuleModifiers.RamShock };

        /// <summary>Always the same draw: 0.99 = no crit or evade, and the last enemy as a random target.</summary>
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

        /// <summary>Never evades (DEX 0), deals no damage (STR 0) and does not die (huge HP).</summary>
        static Fighter Dummy(string id, Action<MonsterStats> tweak = null) =>
            Mon(id, x => { x.Str = 0; x.Dex = 0; x.Hp = 99999; tweak?.Invoke(x); });

        static List<FightAction> Run(FightRules r, FightState s, int ticks, double draw = 0.99)
        {
            var log = new List<FightAction>();
            for (int t = 0; t < ticks && s.Outcome == FightOutcome.Ongoing; t++) BattleSim.Step(r, s, new Fixed(draw), log.Add);
            return log;
        }

        /// <summary>Steps until <paramref name="u"/> has taken <paramref name="actions"/> actions (or 500 ticks).</summary>
        static List<FightAction> RunUntilActions(FightRules r, FightState s, FightUnit u, int actions)
        {
            var log = new List<FightAction>();
            while (u.Tally.Actions < actions && s.Ticks < 500 && s.Outcome == FightOutcome.Ongoing) BattleSim.Step(r, s, new Fixed(0.99), log.Add);
            return log;
        }

        /// <summary>The hits <paramref name="actor"/> landed as actions or triggers (no rule events, no counters).</summary>
        static List<FightAction> Hits(List<FightAction> log, string actor) =>
            log.Where(x => x.Event == null && !x.Counter && !x.Evaded && x.Actor == actor && x.Target != actor).ToList();

        // ------------------------------------------------------------------ content

        [Test]
        public void Content_SpeciesKit()
        {
            var db = TestContent.Db;
            var sp = db.Species["aegis_ram"];
            Assert.That((sp.Element, sp.NativeTree1, sp.NativeTree2, sp.ChaseTreeRun, sp.ChaseTreeBook),
                        Is.EqualTo(("light", "brave_heart", "gilded_fleece", "headlong_oath", "thunderhoof")));
            Assert.That(db.SpeciesList[0].Id, Is.Not.EqualTo("aegis_ram"), "the starter stays first");

            // brave_heart is the ram's, native-only now (kj, 2026-11-03): never in an Awakening draft.
            var brave = db.Trees["brave_heart"];
            Assert.That((brave.Kind, brave.OfferStat, brave.OfferThreshold), Is.EqualTo((TreeKind.Native, (string)null, 0)));
            Assert.That(db.SpeciesList.Count(s => s.NativeTree1 == "brave_heart" || s.NativeTree2 == "brave_heart"), Is.EqualTo(1));
            Assert.That(brave.Nodes.Last().RuleId, Is.EqualTo(RuleModifiers.BraveHeart));

            // Gilded Fleece: Fleece Ward at index 4, the fleece_aegis capstone.
            var fleece = db.Trees["gilded_fleece"];
            var ward = fleece.Nodes[4].Skill;
            Assert.That((fleece.Kind, fleece.Nodes.Last().RuleId), Is.EqualTo((TreeKind.Native, RuleModifiers.FleeceAegis)));
            Assert.That((ward.Id, ward.Kind, ward.Target, ward.Scales, ward.Element), Is.EqualTo(("fleece_ward", SkillKind.Shield, SkillTarget.Self, StatIds.Def, "light")));

            var oath = db.Trees["headlong_oath"];
            var hoof = db.Trees["thunderhoof"];
            Assert.That((oath.Kind, oath.Species, oath.BookGated, oath.Nodes.Last().RuleId), Is.EqualTo((TreeKind.Chase, "aegis_ram", false, RuleModifiers.BraveDebt)));
            Assert.That(oath.Conditions.Select(c => c.Ref), Is.EquivalentTo(new[] { "brave_heart", "str", "wins" }));
            Assert.That((hoof.Kind, hoof.Species, hoof.BookGated, hoof.SecondChase, hoof.Nodes.Last().RuleId),
                        Is.EqualTo((TreeKind.Chase, "aegis_ram", true, true, RuleModifiers.RamShock)));
            Assert.That(hoof.Conditions.Select(c => c.Ref), Is.EquivalentTo(new[] { "ramshorn_hymnal_n0", "gilded_fleece_n4", "damage_light" }));
            var book = db.Trees["ramshorn_hymnal"];
            Assert.That(book.Kind, Is.EqualTo(TreeKind.Book));
            Assert.That(book.Nodes[4].Skill.Id, Is.EqualTo("horn_charge"));
            Assert.That((book.Nodes[7].Kind, book.Nodes[7].Trigger, book.Nodes[7].Skill.Id),
                        Is.EqualTo((NodeKind.Trigger, (FightTrigger?)FightTrigger.OnBasicAttack, "shofar_call")));
            Assert.That(db.SkillBooks.Values.Count(b => b.TreeId == "ramshorn_hymnal"), Is.EqualTo(1));
            Assert.That(db.BookDrops.Count(d => db.SkillBooks[d.BookId].TreeId == "ramshorn_hymnal"), Is.EqualTo(1));
            foreach (var t in new[] { oath, hoof }) Assert.That(db.Text(t.ApexNameKey), Is.Not.EqualTo(t.ApexNameKey), t.Id);

            Assert.That(db.Achievements.Values.Count(a => a.UnlocksSpecies == "aegis_ram"), Is.EqualTo(1));

            var r = Rules;
            var k = db.ConstantValues;
            Assert.That((r.FleeceShield, r.FleeceCap, (double)r.DebtMax, r.RamKnock, r.RamRamp),
                        Is.EqualTo((k["MON_FLEECE_SHIELD"], k["MON_FLEECE_CAP"], k["MON_DEBT_MAX"], k["MON_RAM_KNOCK"], k["MON_RAM_RAMP"])));
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
                         r => r.FleeceShield = -0.1, r => r.FleeceShield = double.PositiveInfinity, r => r.FleeceCap = 1.1, r => r.FleeceCap = -0.1,
                         r => r.DebtMax = -1, r => r.RamKnock = -0.1, r => r.RamKnock = 1.1, r => r.RamRamp = -0.1, r => r.RamRamp = double.NaN,
                     })
            {
                var r = Rules;
                bad(r);
                Assert.Throws<ArgumentException>(() => BattleSim.Start(r, a, b));
            }
        }

        // ------------------------------------------------------------------ fleece_aegis

        /// <summary>The ram's Defaults before its first action against a weak hitter, with or without the fleece.</summary>
        static (int defaults, FightUnit ram, List<FightAction> log) BankAgainstChip(bool fleece)
        {
            var r = Rules;
            var a = Mon("a", x => x.Def = 600, RuleModifiers.BraveHeart);
            if (fleece) a.Rules.Add(RuleModifiers.FleeceAegis);
            var s = BattleSim.Start(r, new[] { a }, new[] { Mon("b", x => { x.Str = 60; x.Hp = 99999; x.Dex = 0; }) });
            var log = RunUntilActions(r, s, s.Units[0], 1);
            return (log.Count(x => x.Actor == "a" && x.Event == BattleSim.BraveEvent && x.Amount > 0), s.Units[0], log);
        }

        [Test]
        public void FleeceAegis_KeepsABraveHeartCarrierDefaulting_ToMaxPips_ThenItActs()
        {
            var r = Rules;
            var (without, _, logWithout) = BankAgainstChip(false);
            Assert.That(Hits(logWithout, "b"), Is.Not.Empty, "the foe's hit breaks the condition");
            Assert.That(without, Is.EqualTo(1), "without the fleece, the first hit ends the banking");

            var (with, ram, log) = BankAgainstChip(true);
            Assert.That(with, Is.EqualTo(r.BraveMax), "each Default ends with a fleece shield, so it banks every pip");
            Assert.That(ram.Hp, Is.EqualTo(ram.MaxHp), "the fleece took the chip damage");
            var fleeces = log.Where(x => x.Event == BattleSim.FleeceEvent).ToList();
            Assert.That(fleeces, Has.Count.EqualTo(r.BraveMax), "one fleece per Default");
            Assert.That(fleeces.All(x => x.Actor == "a" && x.Target == "a" && x.Amount > 0));
            Assert.That(ram.Tally.Actions, Is.EqualTo(1 + Math.Min(r.BraveMax, r.ExtraActionsCap)), "at max pips it acts (no endless Default)");

            // The turn it attacked dealt damage: no fleece at its end.
            var s = BattleSim.Start(r, new[] { Mon("a", null, RuleModifiers.FleeceAegis) }, new[] { Dummy("b") });
            var log2 = RunUntilActions(r, s, s.Units[0], 3);
            Assert.That(Hits(log2, "a"), Is.Not.Empty);
            Assert.That(log2.Any(x => x.Event == BattleSim.FleeceEvent), Is.False, "an attacking unit gets no fleece");
        }

        [Test]
        public void FleeceAegis_ShieldsTheLeastShieldedAlly_UpToTheCap_NeverLowering()
        {
            var r = Rules;
            // a Defaults (brave_heart at full HP) on its first turn: a turn with no damage.
            FightState Start(params double[] shields)
            {
                var side = new List<Fighter> { Mon("a", null, RuleModifiers.BraveHeart, RuleModifiers.FleeceAegis) };
                for (int i = 1; i < shields.Length; i++) side.Add(Dummy("a" + i, x => x.Spd = 1));
                var s = BattleSim.Start(r, side, new[] { Dummy("b", x => x.Spd = 1) });
                for (int i = 0; i < shields.Length; i++) s.Units[i].Shield = shields[i];
                return s;
            }
            FightAction? FirstFleece(FightState s)
            {
                FightAction? first = null;
                for (int t = 0; t < 200 && first == null; t++)
                    BattleSim.Step(r, s, new Fixed(0.99), x => { if (first == null && x.Event == BattleSim.FleeceEvent) first = x; });
                return first;
            }

            var s1 = Start(50, 100, 0, 0);
            var f1 = FirstFleece(s1).Value;
            double amount = r.FleeceShield * s1.Units[0].Source.Stats.Def;
            Assert.That((f1.Actor, f1.Target, f1.Amount), Is.EqualTo(("a", "a2", amount)), "the least shield; ties to the first in unit order");
            Assert.That(s1.Units[2].Shield, Is.EqualTo(amount));

            // The pool fills up to the cap, and a bigger shield is never cut.
            var s2 = Start(0);
            double cap = r.FleeceCap * s2.Units[0].MaxHp;
            s2.Units[0].Shield = cap - 10;
            Assert.That(FirstFleece(s2)?.Amount, Is.EqualTo(10).Within(1e-9));
            Assert.That(s2.Units[0].Shield, Is.EqualTo(cap).Within(1e-9));
            var s3 = Start(0);
            s3.Units[0].Shield = cap + 100;
            Assert.That(FirstFleece(s3), Is.Null);
            Assert.That(s3.Units[0].Shield, Is.EqualTo(cap + 100));

            // A barricade carrier's pool fills up to the barricade's higher cap (the ShieldCap hook), as an overheal shield does.
            var s5 = BattleSim.Start(r, new[] { Mon("a", null, RuleModifiers.BraveHeart, RuleModifiers.FleeceAegis, RuleModifiers.Barricade) },
                                     new[] { Dummy("b", x => x.Spd = 1) });
            s5.Units[0].Shield = cap;
            Assume.That(r.BarricadeCap, Is.GreaterThan(r.FleeceCap));
            Assert.That(FirstFleece(s5)?.Amount, Is.EqualTo(Math.Min(amount, (r.BarricadeCap - r.FleeceCap) * s5.Units[0].MaxHp)).Within(1e-9));

            // Off: no rule, no fleece.
            var s4 = BattleSim.Start(r, new[] { Mon("a", null, RuleModifiers.BraveHeart) }, new[] { Dummy("b") });
            Assert.That(Run(r, s4, 200).Any(x => x.Event == BattleSim.FleeceEvent), Is.False);
        }

        // ------------------------------------------------------------------ brave_debt

        [Test]
        public void BraveDebt_BorrowsAfterItsPips_ThenRepaysBySkipping_Unguarded()
        {
            var r = Rules;
            var s = BattleSim.Start(r, new[] { Mon("a", null, RuleModifiers.BraveHeart, RuleModifiers.BraveDebt) }, new[] { Dummy("b") });
            var a = s.Units[0];
            a.Hp = a.MaxHp - 1;   // hurt and unshielded: no Default, it acts with 0 pips
            var log = RunUntilActions(r, s, a, 1);
            int borrowed = Math.Min(r.DebtMax, r.ExtraActionsCap);
            Assert.That(a.Tally.Actions, Is.EqualTo(1 + borrowed), "it borrows past its (empty) bank");
            Assert.That(a.BravePips, Is.EqualTo(-borrowed));
            Assert.That(log.Where(x => x.Event == BattleSim.DebtEvent).Select(x => x.Amount), Is.EqualTo(Enumerable.Range(1, borrowed).Select(i => (double)-i)),
                        "each borrow is logged with the pips after it");

            // Its next turns are skipped, unguarded and with no Default, one pip back each, until it is at 0; then it acts again.
            var log2 = RunUntilActions(r, s, a, 2 + borrowed);
            var repays = log2.Where(x => x.Event == BattleSim.DebtEvent && x.Actor == "a").ToList();
            Assert.That(repays.Take(borrowed).Select(x => x.Amount), Is.EqualTo(Enumerable.Range(0, borrowed).Select(i => (double)(i - borrowed + 1))));
            Assert.That(log2.Any(x => x.Event == BattleSim.BraveEvent && x.Amount > 0), Is.False, "no Default while in debt");
            Assert.That(a.Tally.Actions, Is.GreaterThan(1 + borrowed), "debt paid, it acts again");

            // Off: brave_heart alone never goes below 0 pips.
            var s2 = BattleSim.Start(r, new[] { Mon("a", null, RuleModifiers.BraveHeart) }, new[] { Dummy("b") });
            s2.Units[0].Hp = s2.Units[0].MaxHp - 1;
            RunUntilActions(r, s2, s2.Units[0], 1);
            Assert.That((s2.Units[0].Tally.Actions, s2.Units[0].BravePips), Is.EqualTo((1, 0)));
        }

        [Test]
        public void BraveDebt_StopsWhenItsTargetDies()
        {
            var r = Rules;
            // Fixed(0.99) aims at the last enemy: the weak one, which the first hit kills; the other one lives.
            var s = BattleSim.Start(r, new[] { Mon("a", null, RuleModifiers.BraveDebt) }, new[] { Dummy("b1"), Dummy("b2", x => x.Hp = 1) });
            var a = s.Units[0];
            RunUntilActions(r, s, a, 1);
            Assert.That(s.Units[2].Alive, Is.False);
            Assert.That(s.Outcome, Is.EqualTo(FightOutcome.Ongoing));
            Assert.That((a.Tally.Actions, a.BravePips), Is.EqualTo((1, 0)), "no borrowing once its target fell");
        }

        [Test]
        public void BraveDebt_StaysUnderTheExtraActionsCap_WithDoubleTurnStart()
        {
            foreach (int cap in new[] { 0, 1, 3 })
            {
                var r = Rules;
                r.ExtraActionsCap = cap;
                var s = BattleSim.Start(r, new[] { Mon("a", null, RuleModifiers.BraveHeart, RuleModifiers.BraveDebt, RuleModifiers.DoubleTurnStart, RuleModifiers.OneMore) },
                                        new[] { Dummy("b", x => x.Spd = 1) });
                var a = s.Units[0];
                a.Hp = a.MaxHp - 1;
                var log = RunUntilActions(r, s, a, 1);
                int doubles = log.Count(x => x.Event == FightAction.DoubleTurnEvent), borrowed = log.Count(x => x.Event == BattleSim.DebtEvent);
                Assert.That(a.Tally.Actions, Is.EqualTo(1 + cap), "double turns and borrowed actions fill the cap, never more: cap " + cap);
                Assert.That(doubles + borrowed, Is.EqualTo(cap));
                Assert.That(a.BravePips, Is.EqualTo(-borrowed).And.GreaterThanOrEqualTo(-r.DebtMax), "it borrows only what fits, cap " + cap);
                if (cap == 3) Assert.That(borrowed, Is.GreaterThan(0));
            }
        }

        [Test]
        public void BraveDebt_WithoutBraveHeart_BorrowsFromZero_AndHashesItsPips()
        {
            var r = Rules;
            var s = BattleSim.Start(r, new[] { Mon("a", null, RuleModifiers.BraveDebt) }, new[] { Dummy("b") });
            var a = s.Units[0];
            RunUntilActions(r, s, a, 1);
            int borrowed = Math.Min(r.DebtMax, r.ExtraActionsCap);
            Assert.That((a.Tally.Actions, a.BravePips), Is.EqualTo((1 + borrowed, -borrowed)), "a copied Headlong Oath borrows from 0 pips");

            var c = s.Clone();
            c.Units[0].BravePips++;
            Assert.That(BattleSim.StateHash(c), Is.Not.EqualTo(BattleSim.StateHash(s)), "pips are hashed for a carrier");
            var plain = BattleSim.Start(r, new[] { Mon("a") }, new[] { Dummy("b") });
            var p2 = plain.Clone();
            p2.Units[0].BravePips = 5;
            Assert.That(BattleSim.StateHash(p2), Is.EqualTo(BattleSim.StateHash(plain)), "and not for other units");
        }

        // ------------------------------------------------------------------ ram_shock

        /// <summary>a banks its pips against a harmless foe of the same speed, then charges; returns the log of its first turn that acted.</summary>
        static (List<FightAction> log, FightState s) Charge(params string[] rules)
        {
            var r = Rules;
            var a = Mon("a", null, rules);
            a.Rules.Add(RuleModifiers.BraveHeart);
            a.Triggers.Add(new TriggeredSkill(FightTrigger.OnBasicAttack,
                new CombatSkill { Id = "shofar", Kind = SkillKind.Damage, Target = SkillTarget.Enemy, Scales = StatIds.Str, Power = 0.3 }));
            var s = BattleSim.Start(r, new[] { a }, new[] { Dummy("b") });
            var log = RunUntilActions(r, s, s.Units[0], 1);
            return (log, s);
        }

        [Test]
        public void RamShock_ChainPushesTheGaugeByPlace_AndRampsItsHits_NotTheTriggers()
        {
            var r = Rules;
            int pips = Math.Min(r.BraveMax, r.ExtraActionsCap);
            Assume.That(pips, Is.GreaterThanOrEqualTo(2));
            var (log, _) = Charge(RuleModifiers.RamShock);
            var (plain, _) = Charge();

            var shocks = log.Where(x => x.Event == BattleSim.RamShockEvent).ToList();
            Assert.That(shocks.Select(x => x.Amount), Is.EqualTo(Enumerable.Range(1, pips).Select(i => r.RamKnock * i).ToList()).Within(1e-9),
                        "each pip action knocks back MON_RAM_KNOCK x its place; triggered hits and the first action do not");
            Assert.That(shocks.All(x => x.Actor == "a" && x.Target == "b"));

            // Each basic attack fires its trigger first: the triggered hit, then the attack.
            var hits = Hits(log, "a");
            var hitsPlain = Hits(plain, "a");
            Assert.That(hits, Has.Count.EqualTo(2 * (1 + pips)));
            for (int i = 0; i <= pips; i++)
            {
                double ramp = 1 + r.RamRamp * Math.Max(0, i - 1);
                Assert.That(hits[2 * i].Amount, Is.EqualTo(hitsPlain[2 * i].Amount).Within(1e-6), "trigger " + i + " unramped");
                Assert.That(hits[2 * i + 1].Amount, Is.EqualTo(hitsPlain[2 * i + 1].Amount * ramp).Within(1e-6), "action " + i);
            }
            Assert.That(plain.Any(x => x.Event == BattleSim.RamShockEvent), Is.False, "off: no knockback");
        }

        [Test]
        public void RamShock_NeverPushesAGaugeBelowZero()
        {
            var r = Rules;
            var a = Mon("a", null, RuleModifiers.BraveHeart, RuleModifiers.RamShock);
            var s = BattleSim.Start(r, new[] { a }, new[] { Dummy("b", x => x.Spd = 1) });   // b's gauge barely fills
            var b = s.Units[1];
            var log = RunUntilActions(r, s, s.Units[0], 1);
            var shocks = log.Where(x => x.Event == BattleSim.RamShockEvent).ToList();
            Assert.That(shocks, Is.Not.Empty);
            Assert.That(shocks.All(x => x.Amount < r.RamKnock), "the push stops at an empty gauge");
            Assert.That(b.Gauge, Is.GreaterThanOrEqualTo(0));
        }

        // ------------------------------------------------------------------ the whole kit

        [Test]
        public void TwoFullRams_FinishBeforeTheTimeLimit()
        {
            var r = Rules;
            string[] kit = { RuleModifiers.BraveHeart, RuleModifiers.FleeceAegis, RuleModifiers.BraveDebt, RuleModifiers.RamShock };
            var s = BattleSim.Start(r, new[] { Mon("a", null, kit) }, new[] { Mon("b", x => x.Spd = 310, kit) });
            Run(r, s, r.TimeLimitTicks);
            Assert.That(s.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut).And.Not.EqualTo(FightOutcome.Ongoing));
            Assert.That(s.Ticks, Is.LessThan(r.TimeLimitTicks));
        }
    }
}
