using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Species Iaido Mantis (#159): its kit in content and its three rules, iai_draw, still_hunt and sever_art, on vantage.</summary>
    public class IaidoMantisRuleTests
    {
        static FightRules Rules => FightRules.FromContent(TestContent.Db);

        /// <summary>The content rules with no avoid or crit chance: hits land and nothing crits unless a rule adds crit rate.</summary>
        static FightRules Plain
        {
            get
            {
                var r = Rules;
                r.EvadeBase = 0; r.EvadePerDex = 0; r.CritBase = 0; r.CritPerLuk = 0;
                return r;
            }
        }

        static readonly string[] SpeciesRules = { RuleModifiers.IaiDraw, RuleModifiers.StillHunt, RuleModifiers.SeverArt };

        sealed class Fixed : IRandomSource
        {
            readonly double _v;
            public Fixed(double v) { _v = v; }
            public double NextDouble() => _v;
        }

        static Fighter Mon(string id, Action<MonsterStats> tweak = null, params string[] rules)
        {
            var st = new MonsterStats { Hp = 300, Str = 300, Int = 0, Wis = 300, Dex = 300, Def = 300, Spd = 100, Luk = 0 };
            tweak?.Invoke(st);
            var f = new Fighter { Id = id, Stats = st };
            f.Rules.AddRange(rules);
            return f;
        }

        static Fighter Fast(string id, params string[] rules) => Mon(id, x => { x.Spd = 900; x.Hp = 99999; }, rules);
        static Fighter Slow(string id, params string[] rules) => Mon(id, x => { x.Spd = 10; x.Hp = 99999; }, rules);
        static Fighter Harmless(string id, params string[] rules) => Mon(id, x => { x.Str = 0; x.Int = 0; x.Spd = 10; x.Hp = 99999; }, rules);

        static CombatSkill Dmg(string id, SkillTarget target = SkillTarget.Enemy, int cooldown = 0, string scales = StatIds.Str, double power = 1) =>
            new CombatSkill { Id = id, Kind = SkillKind.Damage, Target = target, Scales = scales, Power = power, Cooldown = cooldown };

        static List<FightAction> Play(FightRules r, FightState s, IRandomSource rng, Func<bool> until)
        {
            var log = new List<FightAction>();
            for (int i = 0; i < 5000 && !until() && s.Outcome == FightOutcome.Ongoing; i++) BattleSim.Step(r, s, rng, log.Add);
            return log;
        }

        static int Events(List<FightAction> log, string rule) => log.Count(x => x.Event == FightAction.RulePrefix + rule);

        /// <summary>The HP damage of the hits <paramref name="actor"/> landed on <paramref name="target"/> (no rule events).</summary>
        static List<FightAction> Hits(List<FightAction> log, string actor, string target) =>
            log.Where(x => x.Event == null && !x.Counter && !x.Evaded && x.Actor == actor && x.Target == target).ToList();

        // ------------------------------------------------------------------ content

        [Test]
        public void Content_SpeciesKit()
        {
            var db = TestContent.Db;
            var sp = db.Species["iaido_mantis"];
            Assert.That((sp.Element, sp.NativeTree1, sp.NativeTree2, sp.ChaseTreeRun, sp.ChaseTreeBook),
                        Is.EqualTo(("nature", "vantage", "leaf_scabbard", "still_hunt", "severed_arts")));
            Assert.That(db.SpeciesList[0].Id, Is.Not.EqualTo("iaido_mantis"), "the starter stays first");
            Assert.That(sp.Base.Spd, Is.GreaterThan(db.SpeciesList.Where(x => x.Id != sp.Id).Max(x => x.Base.Spd)), "the highest base SPD");

            // vantage is the mantis's, native-only now (kj, 2026-11-03): never in an Awakening draft.
            var vantage = db.Trees["vantage"];
            Assert.That((vantage.Kind, vantage.OfferStat, vantage.OfferThreshold), Is.EqualTo((TreeKind.Native, (string)null, 0)));
            Assert.That(db.SpeciesList.Count(s => s.NativeTree1 == "vantage" || s.NativeTree2 == "vantage"), Is.EqualTo(1));
            Assert.That(vantage.Nodes.Last().RuleId, Is.EqualTo(RuleModifiers.Vantage));

            // Leaf Scabbard: Draw Cut at index 4, the iai_draw capstone.
            var scabbard = db.Trees["leaf_scabbard"];
            var cut = scabbard.Nodes[4].Skill;
            Assert.That((scabbard.Kind, scabbard.Nodes.Last().RuleId), Is.EqualTo((TreeKind.Native, RuleModifiers.IaiDraw)));
            Assert.That((cut.Id, cut.Kind, cut.Target, cut.Scales, cut.Element), Is.EqualTo(("draw_cut", SkillKind.Damage, SkillTarget.Enemy, StatIds.Str, "nature")));

            // The chase trees: the in-run one asks for no book, the second names the Scroll; the book tree ends in Blade Echo.
            var hunt = db.Trees["still_hunt"];
            var sever = db.Trees["severed_arts"];
            Assert.That((hunt.Kind, hunt.Species, hunt.BookGated, hunt.Nodes.Last().RuleId), Is.EqualTo((TreeKind.Chase, "iaido_mantis", false, RuleModifiers.StillHunt)));
            Assert.That(hunt.Conditions.Select(c => c.Ref), Is.EquivalentTo(new[] { "vantage", "spd", "wins" }));
            Assert.That((sever.Kind, sever.Species, sever.BookGated, sever.SecondChase, sever.Nodes.Last().RuleId),
                        Is.EqualTo((TreeKind.Chase, "iaido_mantis", true, true, RuleModifiers.SeverArt)));
            Assert.That(sever.Conditions.Select(c => c.Ref), Is.EquivalentTo(new[] { "unseen_blade_n0", "leaf_scabbard_n4", "crits" }));
            var book = db.Trees["unseen_blade"];
            Assert.That(book.Kind, Is.EqualTo(TreeKind.Book));
            Assert.That(book.Nodes[4].Skill.Target, Is.EqualTo(SkillTarget.AllEnemies));
            Assert.That((book.Nodes[7].Kind, book.Nodes[7].Trigger, book.Nodes[7].Skill.Id),
                        Is.EqualTo((NodeKind.Trigger, (FightTrigger?)FightTrigger.OnHitTaken, "blade_echo")));
            Assert.That(db.SkillBooks.Values.Count(b => b.TreeId == "unseen_blade"), Is.EqualTo(1));
            Assert.That(db.BookDrops.Count(d => db.SkillBooks[d.BookId].TreeId == "unseen_blade"), Is.EqualTo(1));
            foreach (var t in new[] { hunt, sever }) Assert.That(db.Text(t.ApexNameKey), Is.Not.EqualTo(t.ApexNameKey), t.Id);

            Assert.That(db.Achievements.Values.Count(a => a.UnlocksSpecies == "iaido_mantis"), Is.EqualTo(1));

            var r = Rules;
            var k = db.ConstantValues;
            Assert.That((r.IaiBonus, r.IaiCrit, (double)r.HuntWaits, r.HuntKeep, r.SeverFrac, r.SeverDmg, (double)r.SeverMax),
                        Is.EqualTo((k["MON_IAI_BONUS"], k["MON_IAI_CRIT"], k["MON_HUNT_WAITS"], k["MON_HUNT_KEEP"], k["MON_SEVER_FRAC"],
                                    k["MON_SEVER_DMG"], k["MON_SEVER_MAX"])));
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
                         r => r.IaiBonus = -0.1, r => r.IaiBonus = double.PositiveInfinity, r => r.IaiCrit = 1.1, r => r.IaiCrit = double.NaN,
                         r => r.HuntWaits = 0, r => r.HuntKeep = 0, r => r.HuntKeep = 1.5, r => r.SeverFrac = 0, r => r.SeverFrac = 1.1,
                         r => r.SeverDmg = -1, r => r.SeverMax = -1,
                     })
            {
                var r = Rules;
                bad(r);
                Assert.Throws<ArgumentException>(() => BattleSim.Start(r, a, b));
            }
        }

        // ------------------------------------------------------------------ iai_draw

        /// <summary>A fast attacker against a slow vantage carrier at 40% HP; returns the log up to the attacker's first action.</summary>
        static (FightState S, List<FightAction> Log) CounterFight(FightRules r, Fighter carrier, IRandomSource rng = null)
        {
            var s = BattleSim.Start(r, new[] { Fast("a") }, new[] { carrier });
            s.Units[1].Hp = 0.4 * s.Units[1].MaxHp;
            var log = Play(r, s, rng, () => s.Units[0].Tally.Actions >= 1);
            return (s, log);
        }

        [Test]
        public void IaiDraw_BoostsAVantageCounter_AndAddsCritRate()
        {
            var r = Plain;
            var off = CounterFight(r, Slow("v", RuleModifiers.Vantage)).Log;
            var on = CounterFight(r, Slow("v", RuleModifiers.Vantage, RuleModifiers.IaiDraw)).Log;
            double plain = Hits(off, "v", "a").Single().Amount, drawn = Hits(on, "v", "a").Single().Amount;
            Assert.That(drawn, Is.EqualTo(plain * (1 + r.IaiBonus)).Within(1e-6));
            var iai = on.Single(x => x.Event == BattleSim.IaiEvent);
            Assert.That((iai.Actor, iai.Target), Is.EqualTo(("v", "a")));
            Assert.That(iai.Amount, Is.EqualTo(drawn - plain).Within(1e-6), "logged with the damage the bonus added");
            Assert.That(on.FindIndex(x => x.Event == BattleSim.IaiEvent), Is.GreaterThan(on.FindIndex(x => x.Event == null && x.Actor == "v")));
            Assert.That(Events(off, RuleModifiers.IaiDraw), Is.EqualTo(0));

            // crit rate: a draw of 0.1 crits only with the +20% (no base crit in these rules)
            Assert.That(Hits(CounterFight(r, Slow("v", RuleModifiers.Vantage, RuleModifiers.IaiDraw), new Fixed(0.1)).Log, "v", "a").Single().Crit, Is.True);
            Assert.That(Hits(CounterFight(r, Slow("v", RuleModifiers.Vantage), new Fixed(0.1)).Log, "v", "a").Single().Crit, Is.False);
        }

        [Test]
        public void IaiDraw_BoostsASpikedShellHit_ButNotAnOnBasicAttackFollowUpInItsOwnAction()
        {
            var r = Plain;
            Fighter Thorny(params string[] rules)
            {
                var f = Slow("v", rules);
                f.Triggers.Add(new TriggeredSkill(FightTrigger.OnHitTaken, Dmg("spiked_shell", scales: StatIds.Def, power: 0.3)));
                return f;
            }
            List<FightAction> Hit(Fighter v)
            {
                var s = BattleSim.Start(r, new[] { Fast("a") }, new[] { v });
                return Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
            }
            double plain = Hits(Hit(Thorny()), "v", "a").Single().Amount;
            var on = Hit(Thorny(RuleModifiers.IaiDraw));
            Assert.That(Hits(on, "v", "a").Single().Amount, Is.EqualTo(plain * (1 + r.IaiBonus)).Within(1e-6));
            Assert.That(Events(on, RuleModifiers.IaiDraw), Is.EqualTo(1));

            // Its own action: the basic attack and its on_basic_attack follow-up are not drawn.
            List<FightAction> Own(params string[] rules)
            {
                var f = Fast("v", rules);
                f.Triggers.Add(new TriggeredSkill(FightTrigger.OnBasicAttack, Dmg("follow", power: 0.5)));
                var s = BattleSim.Start(r, new[] { f }, new[] { Harmless("b") });
                return Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
            }
            var ownOff = Own();
            var ownOn = Own(RuleModifiers.IaiDraw);
            Assert.That(Events(ownOn, RuleModifiers.IaiDraw), Is.EqualTo(0));
            Assert.That(Hits(ownOn, "v", "b").Select(x => x.Amount), Is.EqualTo(Hits(ownOff, "v", "b").Select(x => x.Amount)));
            Assert.That(Hits(ownOn, "v", "b").Count, Is.EqualTo(2), "the follow-up and the basic attack");
        }

        [Test]
        public void AVantageCounter_DoesNotFireOnBasicAttackTriggers()
        {
            // The counter is a strike, not a basic-attack action: the carrier's on_basic_attack follow-up waits for its own turn.
            var r = Plain;
            var v = Slow("v", RuleModifiers.Vantage, RuleModifiers.IaiDraw);
            v.Triggers.Add(new TriggeredSkill(FightTrigger.OnBasicAttack, Dmg("follow", power: 0.5)));
            var (s, log) = CounterFight(r, v);
            Assert.That(s.Units[1].Tally.Actions, Is.EqualTo(0));
            Assert.That(Events(log, RuleModifiers.Vantage), Is.EqualTo(1));
            Assert.That(log.Any(x => x.Skill?.Id == "follow"), Is.False);
        }

        // ------------------------------------------------------------------ still_hunt

        [Test]
        public void StillHunt_WaitsUpToTheCapInARow_ThenActs_AndWaitingReadiesNoSkill()
        {
            var r = Plain;
            var h = Mon("h", x => x.Spd = 300, RuleModifiers.Vantage, RuleModifiers.StillHunt);
            h.Skills.Add(Dmg("cut", cooldown: 2));
            var s = BattleSim.Start(r, new[] { h }, new[] { Harmless("b") });
            var u = s.Units[0];
            var waits = new List<double>();
            var cooldownAtWait = new List<int>();
            var log = new List<FightAction>();
            for (int i = 0; i < 5000 && u.Tally.Actions < 3; i++)
                BattleSim.Step(r, s, null, x =>
                {
                    log.Add(x);
                    if (x.Event == BattleSim.HuntEvent) { waits.Add(x.Amount); cooldownAtWait.Add(u.Cooldowns[0]); }
                });
            Assert.That(r.HuntWaits, Is.EqualTo(2), "the test reads the placeholder cap");
            Assert.That(waits, Is.EqualTo(new double[] { 1, 2, 1, 2, 1, 2 }), "two waits before each action");
            Assert.That(cooldownAtWait.Skip(2).Take(2), Is.EqualTo(new[] { 2, 2 }), "the cooldown stays while it waits");
            var used = log.Where(x => x.Actor == "h" && x.Event == null).Select(x => x.Skill?.Id ?? "basic").ToList();
            Assert.That(used, Is.EqualTo(new[] { "cut", "basic", "cut" }), "cooldowns count its own actions only");

            // A wait gives back MON_HUNT_KEEP of the gauge: a twin without the rule acts in the same tick and keeps less. (At 0.5,
            // so the one wait does not refill the gauge past a full one: a second wait would follow in the same tick.)
            r.HuntKeep = 0.5;
            var twin = Mon("t", x => x.Spd = 300);
            s = BattleSim.Start(r, new[] { Mon("h", x => x.Spd = 300, RuleModifiers.Vantage, RuleModifiers.StillHunt), twin }, new[] { Harmless("b") });
            s.Units[0].Gauge = s.Units[1].Gauge = r.GaugeSize;
            BattleSim.Step(r, s, null);
            Assert.That(s.Units[0].Gauge - s.Units[1].Gauge, Is.EqualTo(r.HuntKeep * r.GaugeSize).Within(1e-9));
            Assert.That(s.Units[0].Hunting, Is.True);

            // Without vantage there is nothing to wait for: it acts.
            s = BattleSim.Start(r, new[] { Mon("h", x => x.Spd = 300, RuleModifiers.StillHunt) }, new[] { Harmless("b") });
            log = Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
            Assert.That(Events(log, RuleModifiers.StillHunt), Is.EqualTo(0));
        }

        [Test]
        public void StillHunt_WhileWaiting_VantageAnswersAtFullHp()
        {
            var r = Plain;
            List<FightAction> Fight(params string[] rules)
            {
                var s = BattleSim.Start(r, new[] { Fast("a") }, new[] { Slow("h", rules) });
                s.Units[1].Hunting = Array.IndexOf(rules, RuleModifiers.StillHunt) >= 0;   // it waited on its last turn
                return Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
            }
            Assert.That(Events(Fight(RuleModifiers.Vantage, RuleModifiers.StillHunt), RuleModifiers.Vantage), Is.EqualTo(1));
            Assert.That(Events(Fight(RuleModifiers.Vantage), RuleModifiers.Vantage), Is.EqualTo(0), "full HP: plain vantage waits");

            // its turn closes the window: at full HP and acting, it no longer counters
            var s2 = BattleSim.Start(r, new[] { Harmless("a") }, new[] { Mon("h", x => x.Spd = 300, RuleModifiers.Vantage, RuleModifiers.StillHunt) });
            Play(r, s2, null, () => s2.Units[1].Tally.Actions >= 1);
            Assert.That(s2.Units[1].Hunting, Is.False);
            Assert.That(s2.Units[1].HuntWaits, Is.EqualTo(0));
        }

        /// <summary>A fast attacker with only an area skill against [an ally, a waiting carrier].</summary>
        static (FightState S, List<FightAction> Log) AreaFight(FightRules r, Fighter carrier, double attackerHp = 99999)
        {
            var a = Mon("a", x => { x.Spd = 900; x.Hp = attackerHp; });
            a.Skills.Add(Dmg("sweep", SkillTarget.AllEnemies));
            var s = BattleSim.Start(r, new[] { a }, new[] { Slow("b0"), carrier });
            s.Units[2].Hunting = true;
            var log = Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
            return (s, log);
        }

        [Test]
        public void StillHunt_AWaitingCarrierCountersAnAreaAttackOnce_OnTheAttacker_BeforeItLands()
        {
            var r = Plain;
            var (s, log) = AreaFight(r, Slow("h", RuleModifiers.Vantage, RuleModifiers.StillHunt));
            var counters = log.Where(x => x.Event == FightAction.RulePrefix + RuleModifiers.Vantage).ToList();
            Assert.That(counters.Count, Is.EqualTo(1));
            Assert.That((counters[0].Actor, counters[0].Target), Is.EqualTo(("h", "a")));
            int first = log.FindIndex(x => x.Skill?.Id == "sweep");
            Assert.That(log.IndexOf(counters[0]), Is.LessThan(first), "before the first target is hit");
            Assert.That(log.Count(x => x.Skill?.Id == "sweep"), Is.EqualTo(2), "it still hits both");

            // plain vantage below half HP ignores the area attack
            var plain = Slow("h", RuleModifiers.Vantage);
            var a = Mon("a", x => { x.Spd = 900; x.Hp = 99999; });
            a.Skills.Add(Dmg("sweep", SkillTarget.AllEnemies));
            var s2 = BattleSim.Start(r, new[] { a }, new[] { Slow("b0"), plain });
            s2.Units[2].Hp = 0.4 * s2.Units[2].MaxHp;
            var log2 = Play(r, s2, null, () => s2.Units[0].Tally.Actions >= 1);
            Assert.That(Events(log2, RuleModifiers.Vantage), Is.EqualTo(0));

            // a counter that kills the attacker cancels the whole area attack
            var (s3, log3) = AreaFight(r, Mon("h", x => { x.Spd = 10; x.Hp = 99999; x.Str = 3000; }, RuleModifiers.Vantage, RuleModifiers.StillHunt), attackerHp: 1);
            Assert.That(s3.Units[0].Alive, Is.False);
            Assert.That(log3.Count(x => x.Skill?.Id == "sweep"), Is.EqualTo(0));
        }

        [Test]
        public void StillHunt_TwoCarriersAgainstEachOther_EndBeforeTheTimeLimit()
        {
            var r = Rules;
            for (ulong seed = 1; seed <= 5; seed++)
            {
                var res = BattleSim.Resolve(r, new[] { Mon("a", null, RuleModifiers.Vantage, RuleModifiers.StillHunt) },
                                            new[] { Mon("b", null, RuleModifiers.Vantage, RuleModifiers.StillHunt) }, seed);
                Assert.That(res.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut), "seed " + seed);
                Assert.That(res.Ticks, Is.LessThan(r.TimeLimitTicks));
            }
        }

        // ------------------------------------------------------------------ sever_art

        /// <summary>The attacker casts a single-target skill (cooldown 3) at a vantage carrier at 40% HP.</summary>
        static (FightState S, List<FightAction> Log) SeverFight(FightRules r, params string[] rules)
        {
            var a = Fast("a");
            a.Skills.Add(Dmg("heavy", cooldown: 3));
            var s = BattleSim.Start(r, new[] { a }, new[] { Slow("v", rules) });
            s.Units[1].Hp = 0.4 * s.Units[1].MaxHp;
            var log = Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
            return (s, log);
        }

        [Test]
        public void SeverArt_ADeepCounterCutsTheActionOff_TheSkillStillGoesOnCooldown()
        {
            var r = Plain;
            r.SeverFrac = 1e-6;   // any counter is deep enough
            var (s, log) = SeverFight(r, RuleModifiers.Vantage, RuleModifiers.SeverArt);
            Assert.That(Hits(log, "a", "v"), Is.Empty, "the cut-off skill deals nothing");
            Assert.That(s.Units[0].Alive, Is.True, "cut off without a kill");
            Assert.That(s.Units[0].Cooldowns[0], Is.EqualTo(3));
            var cut = log.Single(x => x.Event == BattleSim.SeverEvent);
            Assert.That((cut.Actor, cut.Target, cut.Amount), Is.EqualTo(("v", "a", 1.0)));
            Assert.That(s.Units[1].SeverCuts["a"], Is.EqualTo(1));

            // A shallow counter lets it through.
            r.SeverFrac = 1;
            (s, log) = SeverFight(r, RuleModifiers.Vantage, RuleModifiers.SeverArt);
            Assert.That(Hits(log, "a", "v").Count, Is.EqualTo(1));
            Assert.That(Events(log, RuleModifiers.SeverArt), Is.EqualTo(0));

            // Off: plain vantage only cancels on a kill.
            r.SeverFrac = 1e-6;
            (s, log) = SeverFight(r, RuleModifiers.Vantage);
            Assert.That(Hits(log, "a", "v").Count, Is.EqualTo(1));
        }

        [Test]
        public void SeverArt_EachCutAddsDamageAgainstThatEnemy_UpToTheCap()
        {
            var r = Plain;
            double Strike(int cuts, params string[] rules)
            {
                var s = BattleSim.Start(r, new[] { Fast("v", rules) }, new[] { Harmless("b") });
                if (cuts > 0) s.Units[0].SeverCuts = new Dictionary<string, int> { ["b"] = cuts };
                var log = Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
                return Hits(log, "v", "b").Single().Amount;
            }
            double none = Strike(0, RuleModifiers.SeverArt);
            Assert.That(Strike(1, RuleModifiers.SeverArt), Is.EqualTo(none * (1 + r.SeverDmg)).Within(1e-6));
            Assert.That(Strike(r.SeverMax, RuleModifiers.SeverArt), Is.EqualTo(none * (1 + r.SeverDmg * r.SeverMax)).Within(1e-6));
            Assert.That(Strike(r.SeverMax + 3, RuleModifiers.SeverArt), Is.EqualTo(Strike(r.SeverMax, RuleModifiers.SeverArt)).Within(1e-9), "stops rising");
            Assert.That(Strike(3), Is.EqualTo(none).Within(1e-9), "without the rule cuts add nothing");
        }

        [Test]
        public void SeverArt_ACutOffAreaAttackHitsNoOne()
        {
            var r = Plain;
            r.SeverFrac = 1e-6;
            var (s, log) = AreaFight(r, Slow("h", RuleModifiers.Vantage, RuleModifiers.StillHunt, RuleModifiers.SeverArt));
            Assert.That(s.Units[0].Alive, Is.True);
            Assert.That(log.Count(x => x.Skill?.Id == "sweep"), Is.EqualTo(0));
            Assert.That(Events(log, RuleModifiers.SeverArt), Is.EqualTo(1));
            Assert.That(s.Units[1].Tally.DamageTaken + s.Units[2].Tally.DamageTaken, Is.EqualTo(0));
        }

        [Test]
        public void SeverArt_AnEchoOfACutOffSkillIsCutOffToo()
        {
            var r = Plain;
            r.SeverFrac = 1e-6;
            var a = Fast("a", RuleModifiers.EchoCast);
            a.Skills.Add(Dmg("heavy", cooldown: 3));
            var s = BattleSim.Start(r, new[] { a }, new[] { Slow("v", RuleModifiers.Vantage, RuleModifiers.SeverArt) });
            s.Units[1].Hp = 0.4 * s.Units[1].MaxHp;
            s.Units[0].DecoysLeft = 0;   // echo_cast's mirror twin would take the counter
            var log = Play(r, s, null, () => s.Units[0].Tally.Actions >= 1);
            Assert.That(Hits(log, "a", "v"), Is.Empty);
            Assert.That(Events(log, RuleModifiers.Vantage), Is.EqualTo(1), "one counter per action");

            // The next action is not cut off by the old cut (its counter is shallow now): the skill is on cooldown, so a basic attack.
            r.SeverFrac = 1;
            log = Play(r, s, null, () => s.Units[0].Tally.Actions >= 2);
            Assert.That(Hits(log, "a", "v").Count, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ off, hash, modes

        [Test]
        public void NoCarrier_SameFightAsBefore_StateHashOnlyForCarriers()
        {
            var r = Rules;
            var off = Rules;
            off.IaiBonus = off.IaiCrit = 0; off.HuntWaits = 1; off.HuntKeep = 1; off.SeverFrac = 1; off.SeverDmg = 0; off.SeverMax = 0;
            Fighter[] A() => new[] { Mon("a", x => x.Spd = 400, RuleModifiers.Vantage), Mon("a2") };
            Fighter[] B() => new[] { Mon("b"), Mon("b2", x => x.Luk = 200) };
            for (ulong seed = 1; seed <= 4; seed++)
                Assert.That(BattleSim.Resolve(r, A(), B(), seed).Hash, Is.EqualTo(BattleSim.Resolve(off, A(), B(), seed).Hash));

            var s = BattleSim.Start(r, A(), B());
            string before = BattleSim.StateHash(s);
            s.Units[0].HuntWaits = 1;
            s.Units[0].Hunting = true;
            s.Units[0].SeverCuts = new Dictionary<string, int> { ["b"] = 2 };
            Assert.That(BattleSim.StateHash(s), Is.EqualTo(before), "a unit without the rules hashes none of their state");

            s = BattleSim.Start(r, new[] { Mon("a", null, RuleModifiers.StillHunt, RuleModifiers.SeverArt) }, B());
            before = BattleSim.StateHash(s);
            s.Units[0].Hunting = true;
            Assert.That(BattleSim.StateHash(s), Is.Not.EqualTo(before));
            before = BattleSim.StateHash(s);
            s.Units[0].SeverCuts = new Dictionary<string, int> { ["b"] = 2 };
            Assert.That(BattleSim.StateHash(s), Is.Not.EqualTo(before));
        }

        /// <summary>A mantis-like carrier: vantage and still_hunt (so the window is open early), a Spiked Shell, and the rule.</summary>
        static Fighter Carrier(string id, string rule)
        {
            var f = Mon(id, x => { x.Spd = 350; x.Str = 450; x.Hp = 400; }, RuleModifiers.Vantage, RuleModifiers.StillHunt);
            if (rule != RuleModifiers.StillHunt) f.Rules.Add(rule);
            f.Skills.Add(Dmg("draw_cut", cooldown: 3, power: 1.7));
            f.Triggers.Add(new TriggeredSkill(FightTrigger.OnHitTaken, Dmg("blade_echo", scales: StatIds.Spd, power: 0.4)));
            return f;
        }

        [Test]
        public void EveryRule_In1v1_4v1_And4v4_IsDeterministic_AndCloneContinuesIdentically()
        {
            var r = Rules;
            r.SeverFrac = 0.005;   // these test fighters' counters are shallow against a boss: let sever_art cut there too
            foreach (var rule in SpeciesRules)
            {
                Fighter Boss()
                {
                    var b = Mon("boss", x => { x.Hp = 2500; x.Str = 500; x.Spd = 250; });
                    b.Skills.Add(Dmg("quake", SkillTarget.AllEnemies, cooldown: 2, power: 0.8));
                    return b;
                }
                var shapes = new (Fighter[] A, Fighter[] B)[]
                {
                    (new[] { Carrier("c", rule) }, new[] { Mon("foe", x => { x.Hp = 500; x.Spd = 200; x.Def = 80; }) }),
                    (new[] { Carrier("c1", rule), Carrier("c2", rule), Mon("m3"), Mon("m4") }, new[] { Boss() }),
                    (new[] { Carrier("c1", rule), Carrier("c2", rule), Mon("m3"), Mon("m4") },
                     new[] { Carrier("d1", rule), Mon("e2"), Mon("e3"), Mon("e4") }),
                };
                foreach (var (A, B) in shapes)
                {
                    string at = rule + " " + A.Length + "v" + B.Length;
                    var once = BattleSim.Resolve(r, A, B, 11);
                    Assert.That(BattleSim.Resolve(r, A, B, 11).Hash, Is.EqualTo(once.Hash), at);
                    Assert.That(once.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut), at);

                    var s = BattleSim.Start(r, A, B);
                    var rng = new RngStream(BattleSim.StreamName, 11);
                    var log = new List<FightAction>();
                    for (int i = 0; i < 2000 && s.Outcome == FightOutcome.Ongoing && Events(log, rule) == 0; i++) BattleSim.Step(r, s, rng, log.Add);
                    Assert.That(Events(log, rule), Is.GreaterThan(0), rule + " fired in " + at);
                    if (s.Outcome != FightOutcome.Ongoing) continue;
                    var copy = s.Clone();
                    var rngCopy = rng.Clone();
                    while (BattleSim.Step(r, copy, rngCopy) == FightOutcome.Ongoing) { }
                    Assert.That(BattleSim.StateHash(copy), Is.EqualTo(once.Hash), at);
                }
            }
        }
    }
}
