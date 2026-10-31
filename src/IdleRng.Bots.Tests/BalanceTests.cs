using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Bots.Balance;
using IdleRng.Bots.Training;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
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
        }

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
    }
}
