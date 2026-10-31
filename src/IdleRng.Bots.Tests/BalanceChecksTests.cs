using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Bots.Balance;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Bots.Tests
{
    /// <summary>The loose sanity checks (specs/09 section 7, issue #89) on hand-made reports: each one PASSes, WARNs or SKIPs.</summary>
    public class BalanceChecksTests
    {
        static readonly string[] Ranks = { "e", "d", "c", "b", "a", "s" };

        /// <summary>n runs of one cell, all alike.</summary>
        static CellSummary Cell(string species, string policy, int rank, int n = 10, int won = 3, int rests = 2, RunEnd end = RunEnd.Completed,
                                int cycles = 12, int floor = 30, bool cleared = false) =>
            CellSummary.From(species, policy, Ranks.Length, Enumerable.Range(0, n).Select(_ => new RunSample
            {
                Species = species, Policy = policy, RankIndex = rank, End = end, Cycles = cycles, FightsFought = 12, FightsWon = won,
                Rests = rests, GauntletFloor = cleared ? 101 : floor, GauntletCleared = cleared, GauntletKiller = cleared ? null : "golem",
            }).ToList());

        static TowerSummary Team(string policy, string team, int floors, int n = 10) =>
            TowerSummary.From(policy, team, Enumerable.Range(0, n).Select(_ => new TowerSample
            {
                Policy = policy, Team = team, FloorsCleared = floors, Cleared = floors == 20,
            }).ToList());

        /// <summary>A report every check passes: two species, focus above balanced, a tower team, a book canary pass.</summary>
        static BalanceReport Healthy()
        {
            var r = new BalanceReport
            {
                Options = new BalanceOptions(), RankIds = Ranks.ToList(), GauntletFloors = 100, TowerFloors = 20, GauntletBossEvery = 10,
                Cells = { Cell("a", "balanced", 2), Cell("b", "balanced", 1), Cell("a", "focus", 3), Cell("b", "focus", 2) },
                Tower = { Team("focus", "a", 5), Team("focus", "b", 3), Team("focus", "a+b", 12) },
            };
            r.BookCells = r.Cells.Select(c => Cell(c.Species, c.Policy, (int)c.Grade.Mean)).ToList();
            return r;
        }

        static CheckResult Get(BalanceReport r, string id) => BalanceChecks.Evaluate(r).Single(k => k.Id == id);

        static void Replace(List<CellSummary> cells, CellSummary c) =>
            cells[cells.FindIndex(x => x.Species == c.Species && x.Policy == c.Policy)] = c;

        [Test]
        public void Healthy_AllPass()
        {
            var checks = BalanceChecks.Evaluate(Healthy());
            Assert.That(checks.Select(k => k.Id), Is.EqualTo(new[]
            {
                "progress-fights", "progress-grade", "not-all-s", "policy-matters", "injury-resting", "injury-instant",
                "gauntlet-progress", "gauntlet-not-solved", "tower-reachable", "tower-not-solved", "books",
            }));
            Assert.That(checks.Where(k => k.Status != CheckStatus.Pass).Select(k => k.Id + " " + k.Detail), Is.Empty);
            Assert.That(BalanceChecks.ExitCode(checks, strict: true), Is.EqualTo(0));
        }

        [Test]
        public void Progress_WarnsOnNoFightsWonOrStuckAtLowestGrade()
        {
            var r = Healthy();
            Replace(r.Cells, Cell("b", "focus", 0, won: 0));
            Replace(r.Cells, Cell("b", "balanced", 0, won: 0));
            var fights = Get(r, "progress-fights");
            Assert.That((fights.Status, fights.Value, fights.Detail), Is.EqualTo((CheckStatus.Warn, 0.0, "lowest: b")));
            var grade = Get(r, "progress-grade");
            Assert.That((grade.Status, grade.Value), Is.EqualTo((CheckStatus.Warn, 1.0)));

            // 9 of 10 at E (exactly 90%) is not below the line; 8 of 10 is.
            var runs = Enumerable.Range(0, 10).Select(i => new RunSample { RankIndex = i < 9 ? 0 : 1, FightsWon = 1, End = RunEnd.Completed }).ToList();
            Replace(r.Cells, CellSummary.From("b", "focus", Ranks.Length, runs));
            Assert.That(Get(r, "progress-grade").Status, Is.EqualTo(CheckStatus.Warn), "90% at the lowest grade is not < 90%");
            runs[1].RankIndex = 1;
            Replace(r.Cells, CellSummary.From("b", "focus", Ranks.Length, runs));
            Assert.That(Get(r, "progress-grade").Status, Is.EqualTo(CheckStatus.Pass));
        }

        [Test]
        public void NotAllS_WarnsOnlyWhenEverySpeciesIsMaxed()
        {
            var r = Healthy();
            Replace(r.Cells, Cell("a", "focus", 5));
            var one = Get(r, "not-all-s");
            Assert.That((one.Status, one.Value, one.Detail), Is.EqualTo((CheckStatus.Pass, 0.5, "maxed: a")), "one stronger species is intended");
            Replace(r.Cells, Cell("b", "focus", 5));
            Assert.That(Get(r, "not-all-s").Status, Is.EqualTo(CheckStatus.Warn));
        }

        [Test]
        public void PolicyMatters_FocusMustNotTrailBalanced()
        {
            var r = Healthy();
            Replace(r.Cells, Cell("b", "focus", 1));
            Assert.That(Get(r, "policy-matters").Status, Is.EqualTo(CheckStatus.Pass), "equal is enough (no margin)");
            Replace(r.Cells, Cell("b", "focus", 0, won: 1));
            var k = Get(r, "policy-matters");
            Assert.That((k.Status, k.Value, k.Detail), Is.EqualTo((CheckStatus.Warn, -1.0, "lowest: b")));
        }

        [Test]
        public void Injury_RestingPoliciesAndInstantEnds()
        {
            var r = Healthy();
            // A policy that never rests may be injured every run, as long as it is not by cycle 2.
            r.Cells.Add(Cell("a", "reckless", 1, rests: 0, end: RunEnd.Injured, cycles: 5));
            Assert.That(Get(r, "injury-resting").Status, Is.EqualTo(CheckStatus.Pass));
            Assert.That(Get(r, "injury-instant").Status, Is.EqualTo(CheckStatus.Pass));

            r.Cells[r.Cells.Count - 1] = Cell("a", "reckless", 1, rests: 0, end: RunEnd.Injured, cycles: 2);
            var instant = Get(r, "injury-instant");
            Assert.That((instant.Status, instant.Value, instant.Detail), Is.EqualTo((CheckStatus.Warn, 1.0, "highest: a reckless")));

            Replace(r.Cells, Cell("a", "focus", 3, end: RunEnd.Injured, cycles: 8));
            var resting = Get(r, "injury-resting");
            Assert.That((resting.Status, resting.Value, resting.Detail), Is.EqualTo((CheckStatus.Warn, 1.0, "highest: a focus")));
            Assert.That(resting.N, Is.EqualTo(4), "the reckless cell never rests and is not counted");
        }

        [Test]
        public void Gauntlet_StuckBeforeFirstBossOrSolved()
        {
            var r = Healthy();
            Replace(r.Cells, Cell("b", "focus", 2, floor: 10));
            Replace(r.Cells, Cell("b", "balanced", 1, floor: 9));
            var k = Get(r, "gauntlet-progress");
            Assert.That((k.Status, k.Value, k.Detail), Is.EqualTo((CheckStatus.Warn, 10.0, "lowest: b (best: focus)")), "the boss floor itself is not past it");
            Replace(r.Cells, Cell("b", "balanced", 1, floor: 11));
            Assert.That(Get(r, "gauntlet-progress").Status, Is.EqualTo(CheckStatus.Pass), "any one policy is enough");

            Replace(r.Cells, Cell("a", "focus", 3, cleared: true));
            Assert.That(Get(r, "gauntlet-not-solved").Status, Is.EqualTo(CheckStatus.Warn));
        }

        [Test]
        public void Tower_UnreachableOrAlwaysCleared_AndSkippedWithoutATeam()
        {
            var r = Healthy();
            r.Tower[2] = Team("focus", "a+b", 0);
            Assert.That(Get(r, "tower-reachable").Status, Is.EqualTo(CheckStatus.Warn));
            r.Tower[2] = Team("focus", "a+b", 20);
            Assert.That(Get(r, "tower-reachable").Status, Is.EqualTo(CheckStatus.Pass));
            var solved = Get(r, "tower-not-solved");
            Assert.That((solved.Status, solved.Value), Is.EqualTo((CheckStatus.Warn, 1.0)));

            r.Tower.Clear();
            r.Options.Tower = false;
            var skip = Get(r, "tower-reachable");
            Assert.That((skip.Status, double.IsNaN(skip.Value), skip.Detail), Is.EqualTo((CheckStatus.Skip, true, "tower not run (--no-tower)")));
            Assert.That(BalanceChecks.ExitCode(BalanceChecks.Evaluate(r), strict: true), Is.EqualTo(0), "a SKIP never fails");
        }

        [Test]
        public void Books_WarnsOnALargeDrop_AndSkipsWithoutTheCanary()
        {
            var r = Healthy();
            Replace(r.BookCells, Cell("a", "focus", 2));
            Assert.That(Get(r, "books").Status, Is.EqualTo(CheckStatus.Pass), "a drop of 1 is within 1.5");
            Replace(r.BookCells, Cell("a", "focus", 1));
            var k = Get(r, "books");
            Assert.That((k.Status, k.Value, k.Detail), Is.EqualTo((CheckStatus.Warn, -2.0, "lowest: a focus")));
            Assert.That(BalanceChecks.ExitCode(BalanceChecks.Evaluate(r), strict: false), Is.EqualTo(0), "a WARN fails nothing without --strict");
            Assert.That(BalanceChecks.ExitCode(BalanceChecks.Evaluate(r), strict: true), Is.EqualTo(1));

            r.BookCells.Clear();
            r.Options.BookCanary = false;
            Assert.That(Get(r, "books").Status, Is.EqualTo(CheckStatus.Skip));
        }

        [Test]
        public void FocusChecks_SkipWithoutFocus()
        {
            var r = Healthy();
            r.Cells.RemoveAll(c => c.Policy == "focus");
            r.Tower.Clear();
            var checks = BalanceChecks.Evaluate(r);
            foreach (var id in new[] { "progress-fights", "progress-grade", "not-all-s", "policy-matters", "tower-reachable", "tower-not-solved" })
                Assert.That(checks.Single(k => k.Id == id).Status, Is.EqualTo(CheckStatus.Skip), id);
        }

        [Test]
        public void Harness_EvaluatesChecks_AndRunsTheBookCanaryOnlyWithoutBooks()
        {
            var db = Content.Db;
            var r = BalanceHarness.Run(db, new BalanceOptions { Seeds = 2, Jobs = 2 });
            Assert.That(r.Checks.Count, Is.EqualTo(BalanceChecks.Evaluate(r).Count));
            Assert.That(r.Checks.All(k => k.Status != CheckStatus.Skip), Is.True, "the default run measures every check");
            Assert.That(r.BookCells.Select(c => (c.Species, c.Policy)), Is.EqualTo(r.Cells.Select(c => (c.Species, c.Policy))));
            Assert.That(r.GauntletBossEvery, Is.EqualTo(db.Constants.GauntletBossEvery));

            var withBooks = BalanceHarness.Run(db, new BalanceOptions { Seeds = 1, Books = db.SkillBooks.Keys.ToList(), Tower = false });
            Assert.That(withBooks.BookCells, Is.Empty);
            Assert.That(withBooks.Checks.Single(k => k.Id == "books").Status, Is.EqualTo(CheckStatus.Skip));

            string text = BalanceWriter.Text(r);
            StringAssert.Contains("## Sanity checks", text);
            StringAssert.Contains("## Book canary", text);
            var json = (Dictionary<string, object>)MiniJson.Parse(BalanceWriter.Json(withBooks));
            var books = ((List<object>)json["checks"]).Cast<Dictionary<string, object>>().Single(k => (string)k["id"] == "books");
            Assert.That((books["status"], books["value"]), Is.EqualTo(("SKIP", (object)null)));
            var csv = BalanceWriter.Csv(r).TrimEnd('\n').Split('\n');
            Assert.That(csv.Count(l => l.StartsWith("check,", StringComparison.Ordinal)), Is.EqualTo(r.Checks.Count * 2));
            Assert.That(csv.Count(l => l.StartsWith("books,", StringComparison.Ordinal)), Is.EqualTo(r.BookCells.Count));
        }
    }
}
