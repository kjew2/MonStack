using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Species Anvil Ape (#153): its content and the rules anvil_rhythm, brittle_guard and shatterpoint.</summary>
    public class AnvilApeRuleTests
    {
        static FightRules Rules => FightRules.FromContent(TestContent.Db);

        /// <summary>The same draw until changed: 0 = every evade and crit roll succeeds, 0.99 = none does.</summary>
        sealed class Dial : IRandomSource
        {
            public double V;
            public Dial(double v) { V = v; }
            public double NextDouble() => V;
        }

        static Fighter Mon(string id, Action<MonsterStats> tweak = null, params string[] rules)
        {
            var st = new MonsterStats { Hp = 300, Str = 50, Int = 0, Wis = 300, Dex = 300, Def = 300, Spd = 300, Luk = 0 };
            tweak?.Invoke(st);
            var f = new Fighter { Id = id, Stats = st };
            f.Rules.AddRange(rules);
            return f;
        }

        /// <summary>Deals no damage, never evades (DEX 0) and does not die (huge HP).</summary>
        static Fighter Dummy(string id, Action<MonsterStats> tweak = null) =>
            Mon(id, x => { x.Str = 0; x.Int = 0; x.Dex = 0; x.Hp = 99999; tweak?.Invoke(x); });

        static string Ev(string rule) => FightAction.RulePrefix + rule;

        static List<FightAction> Run(FightRules r, FightState s, IRandomSource rng, int ticks, Func<FightState, bool> until = null)
        {
            var log = new List<FightAction>();
            for (int t = 0; t < ticks && s.Outcome == FightOutcome.Ongoing && !(until?.Invoke(s) ?? false); t++)
                BattleSim.Step(r, s, rng, log.Add);
            return log;
        }

        /// <summary>The hits the unit dealt as actions (basic attacks and skills, evaded ones too; not counters or rule events).</summary>
        static List<FightAction> Hits(List<FightAction> log, string id) =>
            log.Where(x => x.Actor == id && x.Event == null && !x.Counter && x.Target != id).ToList();

        // ------------------------------------------------------------------ content and constants

        [Test]
        public void Content_SpeciesTreesAndConstants()
        {
            var db = TestContent.Db;
            var k = db.ConstantValues;
            var r = Rules;
            Assert.That((r.RhythmHits, r.BrittleStep, r.BrittleMin, r.ShatterFrac, r.ShatterShards, r.ShatterBoss),
                Is.EqualTo(((int)k["MON_RHYTHM_HITS"], (int)k["MON_BRITTLE_STEP"], (int)k["MON_BRITTLE_MIN"], k["MON_SHATTER_FRAC"],
                            (int)k["MON_SHATTER_SHARDS"], k["MON_SHATTER_BOSS"])));
            var sp = db.SpeciesList.Single(x => x.Id == "anvil_ape");
            Assert.That((sp.Element, sp.NativeTree1, sp.NativeTree2, sp.ChaseTreeRun, sp.ChaseTreeBook),
                Is.EqualTo(("fire", "guard_break", "ringing_anvil", "brittle_quench", "shatterpoint")));
            Assert.That(db.SpeciesList[0].Id, Is.Not.EqualTo("anvil_ape"), "appended, never the starter");

            // The idea-pool tree is native-only now: never offered by an Awakening, owned by this species alone.
            var gb = db.Trees["guard_break"];
            Assert.That((gb.Kind, gb.OfferStat, gb.OfferThreshold), Is.EqualTo((TreeKind.Native, (string)null, 0)));
            Assert.That(db.SpeciesList.Count(x => x.NativeTree1 == "guard_break" || x.NativeTree2 == "guard_break"), Is.EqualTo(1));

            var caps = new Dictionary<string, string>
            {
                ["ringing_anvil"] = RuleModifiers.AnvilRhythm, ["brittle_quench"] = RuleModifiers.BrittleGuard, ["shatterpoint"] = RuleModifiers.Shatterpoint,
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
            Assert.That(db.Trees["ringing_anvil"].Kind, Is.EqualTo(TreeKind.Native));
            // No book: the second chase is a harder in-run chase.
            var shatter = db.Trees["shatterpoint"];
            Assert.That((shatter.BookGated, shatter.SecondChase), Is.EqualTo((false, true)));
            Assert.That(db.SkillBooks.Values.Any(b => b.TreeId == "shatterpoint"), Is.False, "no book item");
            Assert.That(db.Achievements["heavy_hands"].UnlocksSpecies, Is.EqualTo("anvil_ape"));
            Assert.That(db.Text("tree.brittle_quench.apex"), Is.EqualTo("Slagglass Titan"));
            Assert.That(db.Text("tree.shatterpoint.apex"), Is.EqualTo("Worldsmith Silverback"));

            // Its skills are fire and scale on STR.
            foreach (var id in new[] { "hammer_tap", "slag_hammer", "anvilfall" })
                Assert.That((db.MonsterSkills[id].Scales, db.MonsterSkills[id].Element), Is.EqualTo((StatIds.Str, "fire")), id);
            Assert.That(db.MonsterSkills["hammer_tap"].Cooldown, Is.EqualTo(1), "keeps the rhythm count moving");
        }

        [Test]
        public void Validation_RejectsConstantsOutOfRange()
        {
            var a = new[] { Mon("a") };
            var b = new[] { Mon("b") };
            foreach (var bad in new Action<FightRules>[]
                     {
                         r => r.RhythmHits = 1, r => r.BrittleStep = -1, r => r.BrittleMin = 0, r => r.BrittleMin = r.BreakPips + 1,
                         r => r.ShatterFrac = 0, r => r.ShatterFrac = 1, r => r.ShatterFrac = double.NaN, r => r.ShatterShards = -1,
                         r => r.ShatterBoss = -0.1, r => r.ShatterBoss = 1.1,
                     })
            {
                var r = Rules;
                bad(r);
                Assert.Throws<ArgumentException>(() => BattleSim.Start(r, a, b));
            }
            Assert.DoesNotThrow(() => BattleSim.Start(Rules, a, b));
        }

        // ------------------------------------------------------------------ anvil_rhythm

        [Test]
        public void Rhythm_EveryNthLandedHitCrits_WithoutADraw()
        {
            var r = Rules;
            foreach (bool on in new[] { true, false })
            {
                var s = BattleSim.Start(r, new[] { Mon("a", null, on ? new[] { RuleModifiers.AnvilRhythm } : new string[0]) }, new[] { Dummy("b") });
                var log = Run(r, s, new Dial(0.99), 40);   // no roll ever succeeds
                var hits = Hits(log, "a");
                Assert.That(hits.Count, Is.GreaterThan(3 * r.RhythmHits));
                var expected = hits.Select((x, i) => on && (i + 1) % r.RhythmHits == 0).ToList();
                Assert.That(hits.Select(x => x.Crit).ToList(), Is.EqualTo(expected), on ? "every Nth hit" : "no crit without the rule");
                Assert.That(log.Count(x => x.Event == Ev(RuleModifiers.AnvilRhythm)), Is.EqualTo(on ? expected.Count(c => c) : 0));
                Assert.That(s.Units[0].Tally.Crits, Is.EqualTo(expected.Count(c => c)), "a forced crit is a crit (tally, OnCrit)");
            }
        }

        [Test]
        public void Rhythm_ARolledCritKeepsTheCountWithoutALogLine()
        {
            var r = Rules;
            var s = BattleSim.Start(r, new[] { Mon("a", null, RuleModifiers.AnvilRhythm) }, new[] { Dummy("b") });
            var log = Run(r, s, new Dial(0), 30);   // every crit roll succeeds
            Assert.That(Hits(log, "a").All(x => x.Crit), Is.True);
            Assert.That(log.Any(x => x.Event == Ev(RuleModifiers.AnvilRhythm)), Is.False, "nothing forced");
        }

        /// <summary>
        /// The third attempt of four is avoided (a physical hit is evaded, a magical one grazes); the fourth lands. An evade does not
        /// count, and a graze cannot crit and leaves the count one short: either way the fourth attempt takes the rhythm crit.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Rhythm_EvadesDoNotCount_AndAGrazeWaits(bool magical)
        {
            var r = Rules;
            foreach (bool on in new[] { true, false })
            {
                var a = Mon("a", x => { x.Dex = 0; if (magical) { x.Str = 0; x.Int = 50; } }, on ? new[] { RuleModifiers.AnvilRhythm } : new string[0]);
                var s = BattleSim.Start(r, new[] { a }, new[] { Dummy("b", x => x.Dex = 999) });
                var log = new List<FightAction>();
                var rng = new Dial(0.99);
                for (int t = 0; t < 60 && Hits(log, "a").Count < 7; t++)
                {
                    rng.V = Hits(log, "a").Count == 2 ? 0 : 0.99;   // the third attempt is avoided
                    BattleSim.Step(r, s, rng, log.Add);
                }
                var hits = Hits(log, "a");
                Assert.That(hits.Count, Is.GreaterThanOrEqualTo(7));
                Assert.That(magical ? hits[2].Graze : hits[2].Evaded, Is.True, "the third attempt is avoided");
                Assert.That(hits[2].Crit, Is.False);
                var crits = hits.Take(7).Select(x => x.Crit).ToList();
                Assert.That(crits, Is.EqualTo(on ? new[] { false, false, false, true, false, false, true } : new bool[7]));
            }
        }

        [Test]
        public void Rhythm_CountIsHashedOnlyForCarriers()
        {
            var r = Rules;
            string Hash(bool carrier, int count)
            {
                var s = BattleSim.Start(r, new[] { Mon("a", null, carrier ? new[] { RuleModifiers.AnvilRhythm } : new string[0]) }, new[] { Dummy("b") });
                s.Units[0].RhythmCount = count;
                return BattleSim.StateHash(s);
            }
            Assert.That(Hash(false, 2), Is.EqualTo(Hash(false, 0)));
            Assert.That(Hash(true, 2), Is.Not.EqualTo(Hash(true, 0)));
        }

        // ------------------------------------------------------------------ brittle_guard

        [Test]
        public void Brittle_EachBreakRefillsFewerPips_DownToTheFloor()
        {
            var r = Rules;
            foreach (bool on in new[] { true, false })
            {
                var rules = on ? new[] { RuleModifiers.GuardBreak, RuleModifiers.BrittleGuard } : new[] { RuleModifiers.GuardBreak };
                var s = BattleSim.Start(r, new[] { Mon("a", null, rules) }, new[] { Dummy("b") });
                var log = Run(r, s, new Dial(0), 200);   // every hit crits
                var left = log.Where(x => x.Event == BattleSim.BreakEvent && x.Actor == "a").Select(x => (int)x.Amount).ToList();
                var expected = new List<int>();
                for (int pips = r.BreakPips; expected.Count < left.Count; pips = on ? Math.Max(r.BrittleMin, pips - r.BrittleStep) : pips)
                    for (int p = pips - 1; p >= 0; p--) expected.Add(p);
                Assert.That(left, Is.EqualTo(expected.Take(left.Count).ToList()), "pips left after each crit");
                var brittle = log.Where(x => x.Event == BattleSim.BrittleEvent).Select(x => (int)x.Amount).ToList();
                var refills = new List<int>();
                for (int p = r.BreakPips; Math.Max(r.BrittleMin, p - r.BrittleStep) < p; p -= r.BrittleStep) refills.Add(Math.Max(r.BrittleMin, p - r.BrittleStep));
                Assert.That(brittle, Is.EqualTo(on ? refills : new List<int>()), "logged with the new refill, until the floor");
                Assert.That(left.Count(x => x == 0), Is.GreaterThan(refills.Count + 2), "it keeps breaking at the floor");
            }
        }

        [Test]
        public void Brittle_ABrokenEnemyAlwaysActsOnceBetweenBreaks()
        {
            var r = Rules;
            var s = BattleSim.Start(r, new[] { Mon("a", x => x.Spd = 999, RuleModifiers.GuardBreak, RuleModifiers.BrittleGuard) }, new[] { Dummy("b") });
            var log = Run(r, s, new Dial(0), 300);
            int lost = 0, acted = 0;
            bool sawBreak = false;
            foreach (var x in log.Where(x => x.Actor == "b"))
            {
                if (x.Event == BattleSim.BreakEvent)
                {
                    if (sawBreak) Assert.That(acted, Is.GreaterThanOrEqualTo(1), "acted between two lost actions");
                    sawBreak = true;
                    acted = 0;
                    lost++;
                }
                else if (x.Event == null) acted++;
            }
            Assert.That(lost, Is.GreaterThan(5), "it keeps breaking");
        }

        [Test]
        public void Brittle_TwoCarriersShareTheRefill()
        {
            var r = Rules;
            var s = BattleSim.Start(r, new[]
            {
                Mon("a1", null, RuleModifiers.GuardBreak, RuleModifiers.BrittleGuard), Mon("a2", null, RuleModifiers.GuardBreak, RuleModifiers.BrittleGuard),
            }, new[] { Dummy("b") });
            var log = Run(r, s, new Dial(0), 50, st => st.Units[2].BreakState != 0);
            Assert.That(s.Units[2].BreakCap, Is.EqualTo(r.BreakPips - r.BrittleStep), "one step per break, not per carrier");
            Assert.That(log.Count(x => x.Event == BattleSim.BrittleEvent), Is.EqualTo(1));
        }

        [Test]
        public void Brittle_RefillIsHashedOnlyFacingACarrier()
        {
            var r = Rules;
            string Hash(bool carrier, int cap)
            {
                var a = Mon("a", null, carrier ? new[] { RuleModifiers.GuardBreak, RuleModifiers.BrittleGuard } : new[] { RuleModifiers.GuardBreak });
                var s = BattleSim.Start(r, new[] { a }, new[] { Dummy("b") });
                s.Units[1].BreakCap = cap;
                return BattleSim.StateHash(s);
            }
            Assert.That(Hash(false, 2), Is.EqualTo(Hash(false, 0)));
            Assert.That(Hash(true, 2), Is.Not.EqualTo(Hash(true, 0)));
        }

        // ------------------------------------------------------------------ shatterpoint

        /// <summary>Steps until a has made its first hit (every roll a crit).</summary>
        static (FightState S, List<FightAction> Log) FirstHit(FightRules r, FightState s)
        {
            var log = new List<FightAction>();
            for (int t = 0; t < 50 && Hits(log, "a").Count == 0; t++) BattleSim.Step(r, s, new Dial(0), log.Add);
            return (s, log);
        }

        /// <summary>a (fast) against b, which is <paramref name="stripped"/> pips down and at <paramref name="hpFrac"/> of its max HP.</summary>
        static FightState Setup(FightRules r, bool on, double hpFrac, int stripped, params Fighter[] more)
        {
            var rules = on ? new[] { RuleModifiers.GuardBreak, RuleModifiers.Shatterpoint } : new[] { RuleModifiers.GuardBreak };
            var s = BattleSim.Start(r, new[] { Mon("a", x => x.Spd = 999, rules) }, new[] { Dummy("b") }.Concat(more).ToArray());
            s.Units[1].BreakStripped = stripped;
            s.Units[1].Hp = hpFrac * s.Units[1].MaxHp;
            return s;
        }

        [Test]
        public void Shatter_TheBreakingHitOnALowEnemyKillsIt()
        {
            var r = Rules;
            foreach (bool on in new[] { true, false })
            {
                var s = Setup(r, on, r.ShatterFrac, r.BreakPips - 1);
                var (_, log) = FirstHit(r, s);
                var b = s.Units[1];
                Assert.That(b.BreakState, Is.Not.EqualTo(0), "the hit broke it");
                Assert.That(b.Alive, Is.EqualTo(!on));
                var ev = log.Where(x => x.Event == BattleSim.ShatterEvent).ToList();
                Assert.That(ev.Count, Is.EqualTo(on ? 1 : 0));
                if (!on) continue;
                Assert.That(ev[0].Amount, Is.GreaterThan(0).And.LessThanOrEqualTo(r.ShatterFrac * b.MaxHp), "logged with the HP it had left");
                Assert.That((b.KilledBy, s.Units[0].Tally.Kills), Is.EqualTo(("a", 1)), "the kill is the unit's");
            }
        }

        [Test]
        public void Shatter_NeedsABrokenEnemy_AndLowHp()
        {
            var r = Rules;
            var s = Setup(r, true, r.ShatterFrac, 0);   // the hit strips one pip: not broken
            var (_, log) = FirstHit(r, s);
            Assert.That((s.Units[1].Alive, s.Units[1].BreakState), Is.EqualTo((true, 0)));
            s = Setup(r, true, 0.9, r.BreakPips - 1);   // broken, but its HP is high
            (_, log) = FirstHit(r, s);
            Assert.That((s.Units[1].Alive, s.Units[1].BreakState != 0), Is.EqualTo((true, true)));
            Assert.That(log.Any(x => x.Event == BattleSim.ShatterEvent), Is.False);
        }

        [Test]
        public void Shatter_ShardsStripTheOtherEnemies_AndAShardBreakNeverChains()
        {
            var r = Rules;
            var s = Setup(r, true, r.ShatterFrac, r.BreakPips - 1, Dummy("c"), Dummy("d"));
            s.Units[3].BreakStripped = r.BreakPips - 1;   // d is one pip short of a break and low: the shards break it
            s.Units[3].Hp = 0.05 * s.Units[3].MaxHp;
            var (_, log) = FirstHit(r, s);
            Assert.That(s.Units[1].Alive, Is.False, "b shattered");
            Assert.That(s.Units[2].BreakStripped, Is.EqualTo(r.ShatterShards), "c lost the shards' pips");
            Assert.That(s.Units[3].BreakState, Is.Not.EqualTo(0), "d broke from the shards");
            Assert.That(s.Units[3].Alive, Is.True, "a shard break is not a shatter: nothing chains");
            Assert.That(log.Count(x => x.Event == BattleSim.ShatterEvent), Is.EqualTo(1));
        }

        [Test]
        public void Shatter_ABossTakesTheChunkOncePerBreak_AndNeverShatters()
        {
            var r = Rules;
            r.Tower = new TowerRules
            {
                EntropyCap = 100, EntropyPerTrigger = 100, EntropyPerEcho = 100, EntropyPerCounter = 100, EntropyDecay = 0,
                RecoilFrac = 0.1, StunGauge = 500, EnrageFrac = 0.25, EnrageTurns = 3, ConvertShield = 0.2, ConvertTurns = 3,
                CorruptStartTick = 100000, CorruptEveryTicks = 100, CorruptHealPerLevel = 0.2, CorruptHealFloor = 0.2, CorruptDmgPerLevel = 0.1,
            };
            var boss = Dummy("b");
            boss.Phases.Add(new BossPhase { EnterHp = 0.001, Kind = PhaseKinds.Enrage, Buff = 0 });
            var s = BattleSim.Start(r, new[] { Mon("a", x => x.Spd = 999, RuleModifiers.GuardBreak, RuleModifiers.Shatterpoint) }, new[] { boss });
            var b = s.Units[1];
            b.BreakStripped = r.BreakPips - 1;
            b.Hp = 0.2 * b.MaxHp;
            var log = new List<FightAction>();
            // Hit until the boss has acted out its break (the hits while it is broken take no second chunk).
            for (int t = 0; t < 100 && Hits(log, "a").Count < 4; t++) BattleSim.Step(r, s, new Dial(0), log.Add);
            Assert.That(b.Alive, Is.True, "a boss never shatters");
            var chunks = log.Where(x => x.Event == BattleSim.ShatterEvent).ToList();
            Assert.That(chunks.Count, Is.EqualTo(1), "once per break");
            Assert.That(chunks[0].Amount, Is.EqualTo(r.ShatterBoss * b.MaxHp).Within(1e-6));
        }

        [Test]
        public void Shatter_ChunkIsHashedOnlyFacingACarrier()
        {
            var r = Rules;
            string Hash(bool carrier, bool chunked)
            {
                var a = Mon("a", null, carrier ? new[] { RuleModifiers.Shatterpoint } : new string[0]);
                var s = BattleSim.Start(r, new[] { a }, new[] { Dummy("b") });
                s.Units[1].ShatterChunked = chunked;
                return BattleSim.StateHash(s);
            }
            Assert.That(Hash(false, true), Is.EqualTo(Hash(false, false)));
            Assert.That(Hash(true, true), Is.Not.EqualTo(Hash(true, false)));
        }
    }
}
