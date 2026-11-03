using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Bots.Balance;
using IdleRng.Bots.Training;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
using IdleRng.TestFixtures;
using NUnit.Framework;

namespace IdleRng.Bots.Tests
{
    /// <summary>The run-balance harness (specs/09 section 7): the aggregation maths, determinism and the output forms.</summary>
    public class BalanceTests
    {
        [Test]
        public void Distribution_MeanSdAndNearestRankPercentiles()
        {
            var d = Distribution.Of(new[] { 5, 1, 4, 2, 3, 6, 7, 8, 9, 10 });
            Assert.That(d.N, Is.EqualTo(10));
            Assert.That(d.Mean, Is.EqualTo(5.5));
            Assert.That(d.Sd, Is.EqualTo(Math.Sqrt(8.25)).Within(1e-12));
            Assert.That((d.Min, d.P10, d.P50, d.P90, d.Max), Is.EqualTo((1.0, 1.0, 5.0, 9.0, 10.0)));
        }

        [Test]
        public void TalentMods_MaxRank_OneLegendary()
        {
            var db = Content.Db;
            Assert.That(BalanceHarness.TalentMods(db, null), Is.Null);
            Assert.That(BalanceHarness.TalentMods(db, new string[0]), Is.Null);
            var m = BalanceHarness.TalentMods(db, new[] { "glass_cannon", "frugal_drills" });
            Assert.That(m.GlassCannonBonus, Is.EqualTo(db.Talents["glass_cannon"].PerRank));
            Assert.That(m.DrillCostMult, Is.EqualTo(1 + db.Talents["frugal_drills"].PerRank * db.Talents["frugal_drills"].MaxRank).Within(1e-12));
            Assert.That(m.ChaseChance, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => BalanceHarness.TalentMods(db, new[] { "glass_cannon", "echo_training" }), "one legendary slot");
            Assert.Throws<ArgumentException>(() => BalanceHarness.TalentMods(db, new[] { "nope" }));
            var s = BalanceHarness.Play(db, "focus", "slime", 1, null, m);
            Assert.That(s.RankIndex, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void Percentile_UsesIntegerRanks()
        {
            // 0.1 x 30 is 3.0000000000000004 in floating point: a float ceil would pick rank 4.
            var thirty = Enumerable.Range(1, 30).Select(x => (double)x).ToArray();
            Assert.That(Distribution.Percentile(thirty, 10), Is.EqualTo(3));
            Assert.That(Distribution.Percentile(thirty, 0), Is.EqualTo(1));
            Assert.That(Distribution.Percentile(thirty, 100), Is.EqualTo(30));
            Assert.That(Distribution.Percentile(new[] { 7.0 }, 90), Is.EqualTo(7));
            Assert.Throws<ArgumentException>(() => Distribution.Percentile(new double[0], 50));
            Assert.Throws<ArgumentOutOfRangeException>(() => Distribution.Percentile(thirty, 101));
        }

        [Test]
        public void Distribution_Empty_IsAllZero()
        {
            var d = Distribution.Of(new double[0]);
            Assert.That((d.N, d.Mean, d.Sd, d.Min, d.P50, d.Max), Is.EqualTo((0, 0.0, 0.0, 0.0, 0.0, 0.0)));
        }

        static RunSample Sample(int rank, RunEnd end, int cycles, int fought, int won, long shards, int floor, string killer, bool boss = false,
                                int injuries = 0) =>
            new RunSample
            {
                Injuries = injuries, CyclesLost = injuries,
                Species = "slime", Policy = "focus", RankIndex = rank, End = end, Cycles = cycles, FightsFought = fought, FightsWon = won,
                Shards = shards, GauntletFloor = floor, GauntletCleared = killer == null, GauntletKiller = killer, GauntletBossStall = boss,
            };

        [Test]
        public void CellSummary_CountsGradesEndsFightsAndKillers()
        {
            var runs = new[]
            {
                Sample(0, RunEnd.Completed, 2, 1, 0, 10, 20, "hydra", boss: true, injuries: 3),
                Sample(2, RunEnd.Completed, 12, 12, 2, 80, 40, "golem", injuries: 1),
                Sample(2, RunEnd.Completed, 12, 12, 2, 80, 30, "hydra"),
                Sample(5, RunEnd.Completed, 12, 12, 5, 110, 101, null),
            };
            var s = CellSummary.From("slime", "focus", 6, runs);
            Assert.That(s.N, Is.EqualTo(4));
            Assert.That(s.Grades, Is.EqualTo(new[] { 1, 0, 2, 0, 0, 1 }));
            Assert.That((s.Completed, s.Injured, s.InjuryRate), Is.EqualTo((4, 2, 0.5)), "injured = runs with at least one injury");
            Assert.That((s.Injuries.Mean, s.Injuries.Max, s.CyclesLost.Mean), Is.EqualTo((1.0, 3.0, 1.0)));
            Assert.That(s.InjuryLoss.Max, Is.EqualTo(1.0), "6 of 2 cycles caps at the whole run");
            Assert.That(s.InjuryLoss.Mean, Is.EqualTo((1.0 + 2.0 / 12) / 4).Within(1e-12));
            Assert.That(s.FightWinRate, Is.EqualTo(9.0 / 37));
            Assert.That(s.FightsLost.Mean, Is.EqualTo((1 + 10 + 10 + 7) / 4.0));
            Assert.That(s.Cycles.Min, Is.EqualTo(2));
            Assert.That(s.Shards.Mean, Is.EqualTo(70));
            Assert.That(s.Grade.Mean, Is.EqualTo(9 / 4.0));
            Assert.That((s.GauntletCleared, s.GauntletClearRate), Is.EqualTo((1, 0.25)));
            Assert.That(s.GauntletFloor.Max, Is.EqualTo(101));
            Assert.That((s.GauntletBossStalls, s.GauntletBossStallRate), Is.EqualTo((1, 1.0 / 3)), "share of the runs that did not clear");
            Assert.That(s.Killers.Select(k => (k.Key, k.Count)), Is.EqualTo(new[] { ("hydra", 2), ("golem", 1) }));
        }

        static ChaseOutcome Chase(string tree, bool unlocked, int capstoneCycle, params bool[] held) => new ChaseOutcome
        {
            TreeId = tree, Unlocked = unlocked, CapstoneCycle = capstoneCycle, ConditionsHeld = held,
            ConditionIds = held.Select((_, j) => $"{tree}_c{j}").ToArray(), ConditionLabels = held.Select((_, j) => $"l{j}").ToArray(),
        };

        [Test]
        public void CellSummary_CountsChaseCapstonesCyclesAndConditions()
        {
            var runs = new[]
            {
                Sample(0, RunEnd.Completed, 12, 0, 0, 0, 5, "a"),
                Sample(0, RunEnd.Completed, 12, 0, 0, 0, 5, "a"),
                Sample(0, RunEnd.Completed, 12, 0, 0, 0, 5, "a"),
                Sample(0, RunEnd.Completed, 12, 0, 0, 0, 5, "a"),
            };
            runs[0].Chase = new List<ChaseOutcome> { Chase("run", true, 11, true, true), Chase("book", true, 12, true) };
            runs[1].Chase = new List<ChaseOutcome> { Chase("run", true, 0, true, true), Chase("book", false, 0, false) };
            runs[2].Chase = new List<ChaseOutcome> { Chase("run", false, 0, true, false), Chase("book", true, 9, true) };
            runs[3].Chase = new List<ChaseOutcome> { Chase("run", false, 0, false, false), Chase("book", false, 0, false) };
            Assert.That(runs.Select(r => r.CapstoneCycle), Is.EqualTo(new[] { 11, 0, 9, 0 }), "earliest capstone the run ended with");

            var s = CellSummary.From("slime", "focus", 3, runs);
            Assert.That((s.Capstoned, s.CapstoneRate), Is.EqualTo((2, 0.5)));
            Assert.That((s.CapstoneCycle.N, s.CapstoneCycle.Mean), Is.EqualTo((2, 10.0)));
            Assert.That(s.Chase.Select(x => x.TreeId), Is.EqualTo(new[] { "run", "book" }), "content order, as the runs list them");
            var run = s.Chase[0];
            Assert.That((run.Unlocked, run.UnlockRate, run.Capstones, run.CapstoneRate), Is.EqualTo((2, 0.5, 1, 0.25)));
            Assert.That((run.CapstoneCycle.N, run.CapstoneCycle.Mean), Is.EqualTo((1, 11.0)));
            Assert.That(run.Conditions.Select(k => (k.Id, k.Label, k.Held, k.HeldRate)),
                        Is.EqualTo(new[] { ("run_c0", "l0", 3, 0.75), ("run_c1", "l1", 2, 0.5) }));
            Assert.That((s.Chase[1].Capstones, s.Chase[1].CapstoneCycle.Mean), Is.EqualTo((2, 10.5)));

            var none = CellSummary.From("slime", "focus", 3, new[] { Sample(0, RunEnd.Completed, 12, 0, 0, 0, 5, "a") });
            Assert.That((none.Capstoned, none.CapstoneCycle.N, none.Chase.Count), Is.EqualTo((0, 0, 0)), "a run with no chase trees");
        }

        [Test]
        public void ChaseTracker_RecordsConditionsUnlockAndCapstoneCycle()
        {
            var db = Content.Db;
            foreach (var sp in db.SpeciesList)
            {
                var tracker = new ChaseTracker(db, sp.Id);
                int cycleAtCapstone = 0;
                var s = RunBot.Play(db, RunPolicies.Create("chaser"), sp.Id, 3, null, null, st =>
                {
                    tracker.Observe(st);
                    if (cycleAtCapstone == 0 && tracker.Outcomes.Any(o => o.Capstone)) cycleAtCapstone = st.Cycle;
                });
                var r = BalanceHarness.Play(db, "chaser", sp.Id, 3);
                var trees = db.TreeList.Where(t => t.Kind == TreeKind.Chase && t.Species == sp.Id).ToList();
                Assert.That(r.Chase.Select(x => x.TreeId), Is.EqualTo(trees.Select(t => t.Id)));
                for (int i = 0; i < trees.Count; i++)
                {
                    Assert.That(r.Chase[i].ConditionIds, Is.EqualTo(trees[i].Conditions.Select(cd => cd.Id)));
                    var slot = s.Slots.FirstOrDefault(x => x != null && x.TreeId == trees[i].Id);
                    bool capstone = slot != null && slot.Bought == trees[i].Nodes.Count;
                    Assert.That(r.Chase[i].Capstone, Is.EqualTo(capstone), $"{sp.Id} {trees[i].Id}: capstone = the run ended with the last node bought");
                    if (capstone) Assert.That(r.Chase[i].Unlocked && r.Chase[i].ConditionsHeld.All(h => h), Is.True);
                }
                Assert.That(r.CapstoneCycle, Is.EqualTo(cycleAtCapstone), sp.Id);
            }
        }

        [Test]
        public void CellSummary_StallModHistogram_CountsOnlyRunsThatDidNotClear()
        {
            var runs = new[]
            {
                Sample(0, RunEnd.Completed, 12, 0, 0, 0, 20, "hydra", boss: true),
                Sample(0, RunEnd.Completed, 12, 0, 0, 0, 30, "titan", boss: true),
                Sample(0, RunEnd.Completed, 12, 0, 0, 0, 23, "imp"),
                Sample(0, RunEnd.Completed, 12, 0, 0, 0, 9, "wisp"),
                Sample(0, RunEnd.Completed, 12, 0, 0, 0, 101, null),
            };
            var s = CellSummary.From("x", "y", 3, runs, 10);
            Assert.That(s.GauntletStallMod, Is.EqualTo(new[] { 2, 0, 0, 1, 0, 0, 0, 0, 0, 1 }), "a cleared run (floor 101) is not a stall");
            Assert.That(s.GauntletStallMod[0], Is.EqualTo(s.GauntletBossStalls), "x0 is the boss floors");
            Assert.That(CellSummary.From("x", "y", 3, runs).GauntletStallMod, Is.Empty, "no histogram without the boss spacing");
            Assert.Throws<ArgumentOutOfRangeException>(() => CellSummary.From("x", "y", 3, runs, -1));
        }

        [Test]
        public void CellSummary_KillerTiesSortById_AndBadRankThrows()
        {
            var s = CellSummary.From("x", "y", 3, new[] { Sample(0, RunEnd.Completed, 12, 0, 0, 0, 5, "b"), Sample(0, RunEnd.Completed, 12, 0, 0, 0, 5, "a") });
            Assert.That(s.Killers.Select(k => k.Key), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(s.FightWinRate, Is.EqualTo(0), "no fights fought");
            Assert.Throws<ArgumentException>(() => CellSummary.From("x", "y", 3, new[] { Sample(3, RunEnd.Completed, 12, 0, 0, 0, 5, "a") }));
        }

        [Test]
        public void TowerSummary_Aggregates()
        {
            var t = TowerSummary.From("focus", "slime", new[]
            {
                new TowerSample { FloorsCleared = 20, Cleared = true, Shards = 80, Books = 4 },
                new TowerSample { FloorsCleared = 10, Shards = 40, Books = 1 },
            });
            Assert.That((t.N, t.Cleared, t.ClearRate), Is.EqualTo((2, 1, 0.5)));
            Assert.That((t.FloorsCleared.Mean, t.Shards.Mean, t.Books.Mean), Is.EqualTo((15.0, 60.0, 2.5)));
        }

        static BalanceOptions Small(int jobs) => new BalanceOptions { Seeds = 3, SeedBase = 11, Jobs = jobs };

        [Test]
        public void Run_IsDeterministic_AndIndependentOfJobs()
        {
            var db = Content.Db;
            string serial = BalanceWriter.Json(BalanceHarness.Run(db, Small(1)));
            Assert.That(BalanceWriter.Json(BalanceHarness.Run(db, Small(1))), Is.EqualTo(serial));
            Assert.That(BalanceWriter.Json(BalanceHarness.Run(db, Small(4))), Is.EqualTo(serial));
        }

        [Test]
        public void Run_CellsMatchSingleRuns()
        {
            var db = Content.Db;
            var r = BalanceHarness.Run(db, Small(2));
            var species = db.SpeciesList.Select(s => s.Id).ToList();
            Assert.That(r.Cells.Count, Is.EqualTo(species.Count * IdleRng.Bots.Training.RunPolicies.Names.Length));
            Assert.That(r.Tower.Count, Is.EqualTo(IdleRng.Bots.Training.RunPolicies.Names.Length * (species.Count + 1)));
            var cell = r.Cells.Single(c => c.Species == species[1] && c.Policy == "focus");
            var runs = Enumerable.Range(11, 3).Select(seed => BalanceHarness.Play(db, "focus", species[1], (ulong)seed)).ToList();
            Assert.That(cell.Grades.Sum(), Is.EqualTo(3));
            Assert.That(cell.Grade.Mean, Is.EqualTo(runs.Average(x => x.RankIndex)));
            Assert.That(cell.GauntletFloor.Mean, Is.EqualTo(runs.Average(x => x.GauntletFloor)));
            Assert.That(cell.Shards.Mean, Is.EqualTo(runs.Average(x => (double)x.Shards)));

            // The shards and gauntlet match what sealing the run into the Vault records.
            var state = IdleRng.Bots.Training.RunBot.Play(db, IdleRng.Bots.Training.RunPolicies.Create("focus"), species[1], 11);
            var sealedEntry = Vault.Seal(db, state);
            Assert.That(runs[0].GauntletFloor, Is.EqualTo(sealedEntry.GauntletFloor));
            Assert.That(runs[0].RankIndex, Is.EqualTo(sealedEntry.RankIndex));
            Assert.That(runs[0].Shards, Is.EqualTo(IdleRng.Sim.Account.AccountOps.Payout(db, state)));
        }

        [Test]
        public void Run_FiltersAndRejectsBadOptions()
        {
            var db = Content.Db;
            string sp = db.SpeciesList[0].Id;
            var r = BalanceHarness.Run(db, new BalanceOptions { Seeds = 2, Species = new[] { sp }, Policies = new[] { "balanced" }, Tower = false });
            Assert.That(r.Cells.Select(c => (c.Species, c.Policy)), Is.EqualTo(new[] { (sp, "balanced") }));
            Assert.That(r.Tower, Is.Empty);
            Assert.Throws<ArgumentException>(() => BalanceHarness.Run(db, new BalanceOptions { Seeds = 0 }));
            Assert.Throws<ArgumentException>(() => BalanceHarness.Run(db, new BalanceOptions { Species = new[] { "nope" } }));
            Assert.Throws<ArgumentException>(() => BalanceHarness.Run(db, new BalanceOptions { Species = new[] { sp, sp } }));
            Assert.Throws<ArgumentException>(() => BalanceHarness.Run(db, new BalanceOptions { Policies = new[] { "nope" } }));
            Assert.Throws<ArgumentException>(() => BalanceHarness.Run(db, new BalanceOptions { Books = new[] { "nope" } }));
            Assert.Throws<ArgumentException>(() => BalanceHarness.Run(db, new BalanceOptions { Books = new[] { db.SkillBooks.Keys.First() }, OwnBooks = true }));
        }

        [Test]
        public void OwnBooks_EachSpeciesBringsOnlyItsOwnBook_AndSkipsTheCanary()
        {
            var db = Content.Db;
            var species = db.SpeciesList.Select(s => s.Id).ToArray();
            var r = BalanceHarness.Run(db, new BalanceOptions { Seeds = 2, Tower = false, OwnBooks = true, Species = species, Policies = new[] { "focus" } });
            Assert.That(r.BookCells, Is.Empty, "own books is a main pass with books: no canary");
            Assert.That(r.Checks.Single(k => k.Id == "books").Status, Is.EqualTo(CheckStatus.Skip));
            foreach (var sp in db.SpeciesList)
            {
                var own = db.SkillBooks.Keys.Where(b => ChaseTarget.IsOwnBook(db, sp.Id, b)).ToList();
                // A book species has one own book; a species whose second chase is a harder in-run one (#160) has none and plays bookless.
                Assert.That(own, Has.Count.EqualTo(db.Trees[sp.ChaseTreeBook].BookGated ? 1 : 0), sp.Id + " has one own book, or none without a book chase");
                var cell = r.Cells.Single(c => c.Species == sp.Id);
                var want = CellSummary.From(sp.Id, "focus", db.FightRanks.Count,
                    new[] { BalanceHarness.Play(db, "focus", sp.Id, 1, own), BalanceHarness.Play(db, "focus", sp.Id, 2, own) });
                Assert.That(cell.Grade.Mean, Is.EqualTo(want.Grade.Mean), sp.Id + ": same runs as bringing its own book");
                Assert.That(cell.CapstoneRate, Is.EqualTo(want.CapstoneRate), sp.Id);
            }
            StringAssert.Contains("books own", BalanceWriter.Text(r));
        }

        [Test]
        public void NonPlanningPolicies_PutPointsIntoASlottedChaseTreeFirst()
        {
            // Issue #111: once a chase tree is slotted, focus buys its nodes before any other tree's, banking for a node it
            // cannot afford yet; the old slot-order spend left it only what the trees before it did not take.
            var db = Content.Db;
            int chaseBuys = 0;
            foreach (var sp in db.SpeciesList)
                for (ulong seed = 1; seed <= 3; seed++)
                {
                    RunState prev = null;
                    RunBot.Play(db, RunPolicies.Create("focus"), sp.Id, seed, null, null, st =>
                    {
                        if (prev != null && prev.Phase != RunPhase.Finished && st.Sp < prev.Sp)
                        {
                            // A node was bought: if the previous state held an unfinished chase tree, it went there.
                            var open = Enumerable.Range(0, prev.Slots.Length).Where(i => prev.Slots[i] != null
                                && db.Trees[prev.Slots[i].TreeId].Kind == TreeKind.Chase
                                && prev.Slots[i].Bought < db.Trees[prev.Slots[i].TreeId].Nodes.Count).ToList();
                            if (open.Count > 0 && open.All(i => st.Slots[i] != null && st.Slots[i].TreeId == prev.Slots[i].TreeId))
                            {
                                chaseBuys++;
                                Assert.That(open.Any(i => st.Slots[i].Bought == prev.Slots[i].Bought + 1), Is.True, $"{sp.Id} seed {seed} cycle {st.Cycle}");
                            }
                        }
                        prev = Snapshot(st);
                    });
                }
            Assert.That(chaseBuys, Is.GreaterThan(0), "some focus run slotted a chase tree and bought into it");
        }

        [Test]
        public void Policies_NeverOverwriteAPrerequisiteOfASlottedChaseTree()
        {
            // A chase offer used to overwrite the least-invested slot even when it held another slotted chase tree's prerequisite,
            // which locked that tree again and threw its capstone away (issue #111). So a capstone, once bought, stays.
            var db = Content.Db;
            foreach (var sp in db.SpeciesList)
            {
                var own = db.SkillBooks.Keys.Where(b => ChaseTarget.IsOwnBook(db, sp.Id, b)).ToList();
                foreach (var policy in new[] { "focus", "hoarder", "chaser" })
                    for (ulong seed = 1; seed <= 4; seed++)
                    {
                        var maxed = new HashSet<string>();
                        var end = RunBot.Play(db, RunPolicies.Create(policy), sp.Id, seed, null, own, st =>
                        {
                            foreach (var t in st.Slots)
                                if (t != null && db.Trees[t.TreeId].Kind == TreeKind.Chase && t.Bought == db.Trees[t.TreeId].Nodes.Count) maxed.Add(t.TreeId);
                        });
                        foreach (var id in maxed)
                            Assert.That(end.Slots.Any(t => t != null && t.TreeId == id), Is.True, $"{sp.Id} {policy} seed {seed}: {id} kept");
                    }
            }
        }

        static RunState Snapshot(RunState s) => new RunState
        {
            Sp = s.Sp, Phase = s.Phase, Cycle = s.Cycle,
            Slots = s.Slots.Select(t => t == null ? null : new TreeSlot { TreeId = t.TreeId, Bought = t.Bought }).ToArray(),
        };

        [Test]
        public void Writers_ProduceParseableJson_AndOneCsvRowPerMetric()
        {
            var db = Content.Db;
            var r = BalanceHarness.Run(db, Small(2));
            var json = (Dictionary<string, object>)MiniJson.Parse(BalanceWriter.Json(r));
            Assert.That(((List<object>)json["cells"]).Count, Is.EqualTo(r.Cells.Count));
            Assert.That(((List<object>)json["tower"]).Count, Is.EqualTo(r.Tower.Count));

            var csv = BalanceWriter.Csv(r).TrimEnd('\n').Split('\n');
            Assert.That(csv[0], Is.EqualTo("section,species,policy,team,metric,n,mean,sd,min,p10,p50,p90,max"));
            Assert.That(csv.Skip(1).All(l => l.Split(',').Length == 13), Is.True);
            Assert.That(csv.Count(l => l.StartsWith("tower,", StringComparison.Ordinal)), Is.EqualTo(r.Tower.Count * 4));
            int every = db.Constants.GauntletBossEvery;
            Assert.That(csv.Count(l => l.StartsWith("gauntlet,", StringComparison.Ordinal) && l.Contains(",stall_mod_")),
                Is.EqualTo(r.Cells.Count * every), "one stall-floor row per cell and floor mod GAUNTLET_BOSS_EVERY");
            foreach (var cell in (List<object>)json["cells"])
            {
                var d = (Dictionary<string, object>)cell;
                var mod = ((List<object>)d["gauntletStallMod"]).Select(Convert.ToDouble).ToList();
                Assert.That(mod.Count, Is.EqualTo(every));
                Assert.That(mod.Sum(), Is.EqualTo(Convert.ToDouble(d["n"]) - Convert.ToDouble(d["gauntletCleared"])), "the histogram counts every stall");
                Assert.That(mod[0], Is.EqualTo(Convert.ToDouble(d["gauntletBossStalls"])), "x0 is the boss floors");
            }
            int chaseRows = r.Cells.Sum(c => 2 + c.Chase.Sum(x => 3 + x.Conditions.Count));
            Assert.That(csv.Count(l => l.StartsWith("chase,", StringComparison.Ordinal)), Is.EqualTo(chaseRows));
            Assert.That(csv.Count(l => l.StartsWith("books_chase,", StringComparison.Ordinal)), Is.EqualTo(r.BookCells.Sum(c => 2 + c.Chase.Sum(x => 3 + x.Conditions.Count))));
            Assert.That(r.Cells.All(c => c.Chase.Count == 2), Is.True, "every species has its two chase trees");

            var cell0 = (Dictionary<string, object>)((List<object>)json["cells"])[0];
            Assert.That(cell0.ContainsKey("capstoneRate") && cell0.ContainsKey("capstoneCycle"), Is.True);
            var chase0 = (Dictionary<string, object>)((List<object>)cell0["chase"])[0];
            Assert.That(chase0["tree"], Is.EqualTo(r.Cells[0].Chase[0].TreeId));
            Assert.That(((List<object>)chase0["conditions"]).Count, Is.EqualTo(r.Cells[0].Chase[0].Conditions.Count));
            Assert.That(((Dictionary<string, object>)((List<object>)json["bookCells"])[0]).ContainsKey("chase"), Is.True);

            string text = BalanceWriter.Text(r);
            StringAssert.Contains("## Chase capstones", text);
            StringAssert.Contains("## Grades", text);
            StringAssert.Contains("## Gauntlet stall floors", text);
            StringAssert.Contains("## Dungeon", text);
            Assert.Throws<ArgumentException>(() => BalanceWriter.Write(r, "xml"));
        }

        [Test]
        public void TreePower_Trees_AreTheGenericTreesWithARuleCapstone()
        {
            var db = Content.Db;
            var trees = TreePower.Trees(db);
            Assert.That(trees.All(t => t.Kind == TreeKind.Generic && t.Nodes[t.Nodes.Count - 1].Kind == NodeKind.Rule), Is.True);
            Assert.That(trees.Select(t => t.Id), Is.EqualTo(db.TreeList.Where(t => t.Kind == TreeKind.Generic && t.Nodes.Count > 0
                && t.Nodes[t.Nodes.Count - 1].Kind == NodeKind.Rule).Select(t => t.Id)), "content order");
        }

        [Test]
        public void TreePower_Summarise_PerSpeciesThenPooled_AndFlagsAboveTheGuideline()
        {
            var db = Content.Db;
            var trees = db.TreeList.Take(2).ToList();
            var runSpecies = new[] { "x", "y", "x" };
            var samples = new IReadOnlyList<(int, int)>[]
            {
                new[] { (3, 5), (4, 4), (5, 9) },
                new[] { (1, 10), (2, 11), (3, 12) },
            };
            var rows = TreePower.Summarise(trees, new[] { "x", "y", "z" }, runSpecies, samples);
            Assert.That(rows.Select(x => (x.TreeId, x.Species)), Is.EqualTo(new[]
            {
                (trees[0].Id, "x"), (trees[0].Id, "y"), (trees[0].Id, TreePower.AllSpecies),
                (trees[1].Id, "x"), (trees[1].Id, "y"), (trees[1].Id, TreePower.AllSpecies),
            }), "a species with no runs gets no row");
            var x0 = rows[0];
            Assert.That((x0.N, x0.BaseFloor.Mean, x0.Gain.Mean, x0.Gain.Min, x0.Gain.Max), Is.EqualTo((2, 4.0, 3.0, 2.0, 4.0)));
            Assert.That((rows[2].N, rows[2].Gain.Mean), Is.EqualTo((3, 2.0)));
            Assert.That(rows[2].Flag, Is.EqualTo(""), "within the guideline");
            Assert.That(rows[5].Gain.Mean, Is.EqualTo(9.0));
            Assert.That(rows[5].Flag, Is.EqualTo("above"), $"a mean gain above {TreePower.GuideMax} floors is flagged");
            Assert.That(rows[0].Stat, Is.EqualTo(trees[0].OfferStat ?? "any"));
            Assert.Throws<ArgumentException>(() => TreePower.Summarise(trees, new[] { "x" }, runSpecies, samples.Take(1).ToArray()));
        }

        [Test]
        public void TreePower_MeasuresTheCapstoneOnTopOfTheRestOfTheTree()
        {
            // No generic tree has a rule capstone yet (the idea-pool lanes add them), so measure a chase tree's rule capstone.
            var db = Content.Db;
            var sp = db.Species["slime"];
            var tree = db.Trees[sp.ChaseTreeRun];
            Assert.That(tree.Nodes[tree.Nodes.Count - 1].Kind, Is.EqualTo(NodeKind.Rule));
            var o = new BalanceOptions { Seeds = 3, SeedBase = 5, Tower = false, Endless = false, BookCanary = false, Species = new[] { "slime", "drake" },
                                         Policies = new[] { "balanced", "focus" }, PowerTrees = new[] { tree.Id } };
            var r = BalanceHarness.Run(db, o);
            Assert.That(r.TreePowerRan, Is.True);
            Assert.That(r.TreePower.Select(x => x.Species), Is.EqualTo(new[] { "slime", "drake", TreePower.AllSpecies }));
            var slime = r.TreePower[0];
            var want = Enumerable.Range(5, 3).Select(seed =>
                TreePower.Measure(db, RunBot.Play(db, RunPolicies.Create(TreePower.Policy), "slime", (ulong)seed), tree)).ToList();
            Assert.That((slime.N, slime.BaseFloor.Mean, slime.Gain.Mean), Is.EqualTo((3, want.Average(x => x.Base), want.Average(x => x.With - x.Base))));
            Assert.That(r.TreePower[2].N, Is.EqualTo(6));

            // The two builds differ only by the capstone rule.
            var state = RunBot.Play(db, RunPolicies.Create(TreePower.Policy), "slime", 5);
            var without = BalanceHarness.BuildFighter(db, state, new[] { (tree.Id, tree.Nodes.Count - 1) });
            var with = BalanceHarness.BuildFighter(db, state, new[] { (tree.Id, tree.Nodes.Count) });
            Assert.That(with.Rules.Except(without.Rules), Is.EqualTo(new[] { tree.Nodes[tree.Nodes.Count - 1].RuleId }));
            Assert.That(StatIds.Trained.Select(id => with.Stats.Get(id)), Is.EqualTo(StatIds.Trained.Select(id => without.Stats.Get(id))));

            // Same numbers whatever the thread count; no focus policy, no section.
            o.Jobs = 4;
            Assert.That(BalanceWriter.Json(BalanceHarness.Run(db, o)), Is.EqualTo(BalanceWriter.Json(r)));
            var noFocus = BalanceHarness.Run(db, new BalanceOptions { Seeds = 1, Tower = false, Endless = false, BookCanary = false,
                                                                     Species = new[] { "slime" }, Policies = new[] { "balanced" }, PowerTrees = new[] { tree.Id } });
            Assert.That((noFocus.TreePowerRan, noFocus.TreePower.Count), Is.EqualTo((false, 0)));
            StringAssert.Contains("not run: needs the focus policy", BalanceWriter.Text(noFocus));
            Assert.Throws<ArgumentException>(() => BalanceHarness.Run(db, new BalanceOptions { Seeds = 1, PowerTrees = new[] { "nope" } }));
            Assert.Throws<ArgumentException>(() => BalanceHarness.Run(db, new BalanceOptions { Seeds = 1, PowerTrees = new[] { tree.Id, tree.Id } }));
            Assert.Throws<ArgumentOutOfRangeException>(() => BalanceHarness.BuildFighter(db, state, new[] { (tree.Id, tree.Nodes.Count + 1) }));
        }

        [Test]
        public void TreePower_Writers_TextJsonAndCsv()
        {
            var db = Content.Db;
            var tree = db.Trees[db.Species["slime"].ChaseTreeRun];
            var r = BalanceHarness.Run(db, new BalanceOptions { Seeds = 2, Tower = false, Endless = false, BookCanary = false, Species = new[] { "slime" },
                                                               Policies = new[] { "focus" }, PowerTrees = new[] { tree.Id } });
            StringAssert.Contains("## Tree power", BalanceWriter.Text(r));
            var tp = (Dictionary<string, object>)((Dictionary<string, object>)MiniJson.Parse(BalanceWriter.Json(r)))["treePower"];
            Assert.That((tp["ran"], tp["policy"], Convert.ToDouble(tp["guideMin"]), Convert.ToDouble(tp["guideMax"])),
                Is.EqualTo((true, "focus", (double)TreePower.GuideMin, (double)TreePower.GuideMax)));
            var rows = (List<object>)tp["rows"];
            Assert.That(rows.Count, Is.EqualTo(2));
            var row = (Dictionary<string, object>)rows[0];
            Assert.That((row["tree"], row["species"]), Is.EqualTo((tree.Id, "slime")));
            Assert.That(row.ContainsKey("baseFloor") && row.ContainsKey("gain") && row.ContainsKey("flag"), Is.True);
            var csv = BalanceWriter.Csv(r).Split('\n').Where(l => l.StartsWith("tree_power,", StringComparison.Ordinal)).ToList();
            Assert.That(csv.Count, Is.EqualTo(4), "base floor and gain, per species and pooled");
            Assert.That(csv.Any(l => l.StartsWith($"tree_power,all,focus,,{tree.Id}_gain,2,", StringComparison.Ordinal)), Is.True);

            // Today's content: the section runs and says no tree qualifies (the lanes add them).
            var today = BalanceHarness.Run(db, new BalanceOptions { Seeds = 1, Tower = false, Endless = false, BookCanary = false, Species = new[] { "slime" },
                                                                   Policies = new[] { "focus" } });
            Assert.That(today.TreePower.Select(x => x.TreeId).Distinct(), Is.EqualTo(TreePower.Trees(db).Select(t => t.Id)));
            if (TreePower.Trees(db).Count == 0) StringAssert.Contains("(no generic tree has a rule capstone)", BalanceWriter.Text(today));
        }

        [Test]
        public void Opponents_TallyEveryRankFight_AgainstTheLadder()
        {
            var db = Content.Db;
            var r = BalanceHarness.Run(db, new BalanceOptions { Seeds = 6, Tower = false, BookCanary = false, Species = new[] { "slime", "drake" }, Policies = new[] { "focus" } });
            foreach (var c in r.Cells)
            {
                Assert.That(c.Opponents.Sum(o => o.Fought), Is.EqualTo((int)Math.Round(c.FightsWon.Mean * c.N + c.FightsLost.Mean * c.N)), c.Species + ": every fight is tallied");
                Assert.That(c.Opponents.Sum(o => o.Won), Is.EqualTo((int)Math.Round(c.FightsWon.Mean * c.N)), c.Species + ": every win is tallied");
                for (int rank = 0; rank < db.FightRanks.Count; rank++)
                    Assert.That(c.Opponents.Where(o => o.Rank == rank).Sum(o => o.Rolled), Is.EqualTo(c.N), "each run rolled one opponent at rank " + rank);
                foreach (var o in c.Opponents) Assert.That(db.RankEnemies[o.Id].RankIndex, Is.EqualTo(o.Rank));
            }
            // A fight is against the ladder's monster at the rank it was fought at.
            var fights = new FightTracker();
            var s = RunBot.Play(db, RunPolicies.Create("focus"), "slime", 3, null, null, fights.Observe);
            Assert.That(fights.Fights.Count, Is.EqualTo(s.FightsFought));
            Assert.That(fights.Fights.Count(f => f.Won), Is.EqualTo(s.FightsWon));
            Assert.That(fights.Fights.All(f => f.Opponent == s.Ladder[f.Rank]), Is.True);
            StringAssert.Contains("## Rank fights by opponent", BalanceWriter.Text(r));
            StringAssert.Contains("opponent,slime,focus,,", BalanceWriter.Csv(r));
        }
    
        [Test]
        public void SpeciesGroundwork_NativeOnlyTreesLeaveTreePower_AndASecondChaseWithoutABookIsMeasured()
        {
            // #160: a tree a species lane turns native-only is no longer measured as a generic tree.
            var native = ContentFixtures.WithNativeTrees(ContentFixtures.SpeciesTrees);
            Assert.That(TreePower.Trees(native).Select(t => t.Id), Has.None.AnyOf(ContentFixtures.SpeciesTrees));
            Assert.That(TreePower.Trees(native), Is.Not.Empty, "the other generic rule trees stay");

            // A second chase tree with no book (a harder in-run chase): chase power, the chase table and the ladder check read it.
            var db = ContentFixtures.WithDrakeSecondChaseWithoutBook();
            Assert.That(db.SkillBooks.Keys.Where(b => ChaseTarget.IsOwnBook(db, "drake", b)), Is.Empty, "drake has no own book now");
            var r = BalanceHarness.Run(db, new BalanceOptions { Seeds = 4, Tower = false, Endless = false, BookCanary = false, Species = new[] { "drake" },
                                                              Policies = new[] { "focus", "chaser" }, OwnBooks = true });
            foreach (var cell in r.Cells)
            {
                Assert.That(cell.CapstoneGainBook.N, Is.EqualTo(4), cell.Policy);
                Assert.That(cell.Chase.Select(x => x.TreeId), Is.EquivalentTo(new[] { "unbreakable_will", "inferno_sovereign" }), cell.Policy);
            }
            Assert.That(r.Checks.Single(x => x.Id == "capstone-ladder").Status, Is.Not.EqualTo(CheckStatus.Skip));
        }
}
}
