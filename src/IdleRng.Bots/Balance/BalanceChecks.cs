using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IdleRng.Bots.Balance
{
    public enum CheckStatus { Pass, Warn, Skip }

    /// <summary>One loose sanity check over a balance report: its status, the worst measured value and the limit it is held to.</summary>
    public sealed class CheckResult
    {
        public string Id { get; set; }

        /// <summary>What passing means, in words.</summary>
        public string Rule { get; set; }
        public CheckStatus Status { get; set; }

        /// <summary>The worst value measured across what the check covers (NaN when skipped).</summary>
        public double Value { get; set; } = double.NaN;
        public double Limit { get; set; }

        /// <summary>Where the worst value comes from (a cell, a species), or why the check was skipped.</summary>
        public string Detail { get; set; }

        /// <summary>Species x policy cells (or species, or tower teams) the check looked at.</summary>
        public int N { get; set; }
    }

    /// <summary>
    /// Loose "is it absolutely unplayable?" sanity checks over a balance report (specs/09 section 7, issue #89). kj's stance
    /// (2026-10-31): no tight targets while meta-progression is still being added; some species are meant to be stronger than
    /// others, so there is no parity check; injury is checked only for "not instant or total". A WARN is a report line, not a
    /// build failure (the CLI's --strict exits non-zero on one, for later use). Thresholds live here, not in content.
    /// </summary>
    public static class BalanceChecks
    {
        public const string FocusPolicy = "focus";
        public const string BalancedPolicy = "balanced";

        /// <summary>progress-fights: under focus, every species' median rank fights won is at least this.</summary>
        public const double MinMedianFightsWon = 1;

        /// <summary>progress-grade: under focus, the share of a species' runs stuck at the lowest grade stays below this.</summary>
        public const double MaxLowestGradeShare = 0.9;

        /// <summary>policy-matters: focus mean rank minus balanced mean rank, per species, is at least this (no margin).</summary>
        public const double MinFocusOverBalanced = 0;

        /// <summary>injury-resting: a policy that rests (any run with a Rest) is injured in at most this share of runs.</summary>
        public const double MaxRestingInjuryRate = 0.5;

        /// <summary>
        /// injury-total: in every cell, the mean share of a run's cycles lost to injury (injured cycles plus cycles sat out) stays
        /// below this. Replaces injury-instant (runs ended by injury by cycle 2), which an injury can no longer do since the rework
        /// (specs/14 section 4.1): injury may cripple a run, but not swallow all of it.
        /// </summary>
        public const double MaxInjuryLossShare = 0.9;

        /// <summary>gauntlet-not-solved: no cell clears the gauntlet in more than this share of runs.</summary>
        public const double MaxGauntletClearRate = 0.5;

        /// <summary>tower-reachable: the full focus team clears at least one floor on more than this share of days.</summary>
        public const double MinTowerFirstFloorShare = 0.5;

        /// <summary>tower-not-solved: the full focus team's clear rate stays below this.</summary>
        public const double MaxTowerClearRate = 1;

        /// <summary>endless-reachable: the focus team clears Endless Tower floor 1 in more than this share of seasons.</summary>
        public const double MinEndlessFirstFloorShare = 0.5;

        /// <summary>endless-not-capped: the focus team reaches ENDLESS_MAX_FLOORS in fewer than this share of seasons.</summary>
        public const double MaxEndlessCappedRate = 1;

        /// <summary>books: bringing every book lowers no cell's mean rank by more than this (a canary for broken books).</summary>
        public const double MaxBookGradeDrop = 1.5;

        /// <summary>
        /// capstone-ladder (kj, 2026-10-31, specs/15 section 5.5): under focus, every species' book-gated chase capstone adds more
        /// gauntlet floors than its in-run one (the book chase is the rarest and strongest).
        /// </summary>
        public const double MinBookOverRunGain = 0;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static List<CheckResult> Evaluate(BalanceReport r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            var focus = r.Cells.Where(c => c.Policy == FocusPolicy).ToList();
            var checks = new List<CheckResult>
            {
                Min("progress-fights", $"under {FocusPolicy}, every species' median rank fights won >= {N(MinMedianFightsWon)}",
                    focus, c => c.FightsWon.P50, c => c.Species, MinMedianFightsWon, strict: false, none: $"no {FocusPolicy} cells"),
                Max("progress-grade", $"under {FocusPolicy}, every species ends at the lowest grade in < {Pct(MaxLowestGradeShare)} of runs",
                    focus, c => c.N == 0 ? 0 : (double)c.Grades[0] / c.N, c => c.Species, MaxLowestGradeShare, strict: true, none: $"no {FocusPolicy} cells"),
                AllS(r, focus),
                PolicyMatters(r),
                Max("injury-resting", $"no resting policy is injured in > {Pct(MaxRestingInjuryRate)} of runs",
                    r.Cells.Where(c => c.Rests.Max > 0).ToList(), c => c.InjuryRate, Cell, MaxRestingInjuryRate, strict: false, none: "no cell rests"),
                Max("injury-total", $"in every cell, injuries cost < {Pct(MaxInjuryLossShare)} of a run's cycles on average",
                    r.Cells, c => c.InjuryLoss.Mean, Cell, MaxInjuryLossShare, strict: true, none: "no cells"),
                GauntletProgress(r),
                Max("gauntlet-not-solved", $"no cell clears the gauntlet in > {Pct(MaxGauntletClearRate)} of runs",
                    r.Cells, c => c.GauntletClearRate, Cell, MaxGauntletClearRate, strict: false, none: "no cells"),
                Min("capstone-ladder", $"under {FocusPolicy}, every species' book chase tree adds more gauntlet floors than its in-run one",
                    focus, c => c.CapstoneGainBook.Mean - c.CapstoneGainRun.Mean, c => c.Species, MinBookOverRunGain, strict: true, none: $"no {FocusPolicy} cells"),
            };
            var team = FocusTeam(r);
            string noTeam = !r.Options.Tower ? "tower not run (--no-tower)" : $"no full {FocusPolicy} team (needs {FocusPolicy} and at least two species)";
            var teams = team == null ? new List<TowerSummary>() : new List<TowerSummary> { team };
            checks.Add(Min("tower-reachable", $"the full {FocusPolicy} team clears floor 1 on > {Pct(MinTowerFirstFloorShare)} of days",
                teams, t => t.N == 0 ? 0 : (double)t.ReachedFloor1 / t.N, t => t.Team, MinTowerFirstFloorShare, strict: true, none: noTeam));
            checks.Add(Max("tower-not-solved", $"the full {FocusPolicy} team clears the tower on < {Pct(MaxTowerClearRate)} of days",
                teams, t => t.ClearRate, t => t.Team, MaxTowerClearRate, strict: true, none: noTeam));
            var endless = r.Endless.Where(e => e.Policy == FocusPolicy).ToList();
            string noEndless = !r.Options.Endless ? "Endless Tower not run (--no-endless)" : $"no {FocusPolicy} team of MON_SIDE_SIZE species";
            checks.Add(Min("endless-reachable", $"the {FocusPolicy} team clears Endless Tower floor 1 in > {Pct(MinEndlessFirstFloorShare)} of seasons",
                endless, e => e.N == 0 ? 0 : (double)e.ReachedFloor1 / e.N, e => e.Team, MinEndlessFirstFloorShare, strict: true, none: noEndless));
            checks.Add(Max("endless-not-capped", $"the {FocusPolicy} team reaches the Endless Tower's safety cap in < {Pct(MaxEndlessCappedRate)} of seasons",
                endless, e => e.CappedRate, e => e.Team, MaxEndlessCappedRate, strict: true, none: noEndless));
            checks.Add(Books(r));
            return checks;
        }

        static string Cell(CellSummary c) => $"{c.Species} {c.Policy}";

        /// <summary>WARN when the smallest value is below the limit (or at it, when strict).</summary>
        static CheckResult Min<T>(string id, string rule, IList<T> items, Func<T, double> value, Func<T, string> name, double limit, bool strict, string none)
        {
            var res = new CheckResult { Id = id, Rule = rule, Limit = limit, N = items.Count };
            if (items.Count == 0) return Skip(res, none);
            var worst = items.OrderBy(value).First();
            res.Value = value(worst);
            res.Status = strict ? (res.Value > limit ? CheckStatus.Pass : CheckStatus.Warn) : (res.Value >= limit ? CheckStatus.Pass : CheckStatus.Warn);
            res.Detail = "lowest: " + name(worst);
            return res;
        }

        /// <summary>WARN when the largest value is above the limit (or at it, when strict).</summary>
        static CheckResult Max<T>(string id, string rule, IList<T> items, Func<T, double> value, Func<T, string> name, double limit, bool strict, string none)
        {
            var res = new CheckResult { Id = id, Rule = rule, Limit = limit, N = items.Count };
            if (items.Count == 0) return Skip(res, none);
            var worst = items.OrderByDescending(value).First();
            res.Value = value(worst);
            res.Status = strict ? (res.Value < limit ? CheckStatus.Pass : CheckStatus.Warn) : (res.Value <= limit ? CheckStatus.Pass : CheckStatus.Warn);
            res.Detail = "highest: " + name(worst);
            return res;
        }

        static CheckResult Skip(CheckResult res, string why)
        {
            res.Status = CheckStatus.Skip;
            res.Value = double.NaN;
            res.Detail = why;
            return res;
        }

        /// <summary>
        /// not-all-s: some species are meant to be stronger (later unlocks), so one species maxing out is fine; every species
        /// under focus ending at the top grade in every run is not (nothing left to progress). Value: share of species maxed.
        /// </summary>
        static CheckResult AllS(BalanceReport r, List<CellSummary> focus)
        {
            int top = r.RankIds.Count - 1;
            string topId = r.RankIds[top].ToUpperInvariant();
            var res = new CheckResult
            {
                Id = "not-all-s", Rule = $"under {FocusPolicy}, not every species ends at {topId} in 100% of runs", Limit = 1, N = focus.Count,
            };
            if (focus.Count == 0) return Skip(res, $"no {FocusPolicy} cells");
            var maxed = focus.Where(c => c.N > 0 && c.Grades[top] == c.N).Select(c => c.Species).ToList();
            res.Value = (double)maxed.Count / focus.Count;
            res.Status = res.Value < 1 ? CheckStatus.Pass : CheckStatus.Warn;
            res.Detail = maxed.Count == 0 ? "no species maxed" : "maxed: " + string.Join(",", maxed);
            return res;
        }

        static CheckResult PolicyMatters(BalanceReport r)
        {
            var pairs = r.Cells.Where(c => c.Policy == FocusPolicy)
                .Select(f => (f, b: r.Cells.FirstOrDefault(c => c.Policy == BalancedPolicy && c.Species == f.Species)))
                .Where(p => p.b != null).ToList();
            return Min("policy-matters", $"{FocusPolicy} mean rank >= {BalancedPolicy} mean rank for every species", pairs,
                p => p.f.Grade.Mean - p.b.Grade.Mean, p => p.f.Species, MinFocusOverBalanced, strict: false,
                none: $"needs both {FocusPolicy} and {BalancedPolicy}");
        }

        /// <summary>gauntlet-progress: per species, the best policy's median floor; the lowest of those must pass the first boss floor.</summary>
        static CheckResult GauntletProgress(BalanceReport r)
        {
            var best = r.Cells.GroupBy(c => c.Species, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(c => c.GauntletFloor.P50).First()).ToList();
            return Min("gauntlet-progress", $"every species has a policy whose median gauntlet floor > the first boss floor ({r.GauntletBossEvery})",
                best, c => c.GauntletFloor.P50, c => $"{c.Species} (best: {c.Policy})", r.GauntletBossEvery, strict: true, none: "no cells");
        }

        static CheckResult Books(BalanceReport r)
        {
            var pairs = r.BookCells.Select(b => (b, n: r.Cells.FirstOrDefault(c => c.Species == b.Species && c.Policy == b.Policy)))
                .Where(p => p.n != null).ToList();
            string none = r.Options.Books != null && r.Options.Books.Count > 0 || r.Options.OwnBooks ? "the main pass already brings books"
                        : !r.Options.BookCanary ? "book canary off (--no-book-canary)" : "no cells";
            return Min("books", $"bringing every book lowers no cell's mean rank by more than {N(MaxBookGradeDrop)}", pairs,
                p => p.b.Grade.Mean - p.n.Grade.Mean, p => Cell(p.n), -MaxBookGradeDrop, strict: false, none: none);
        }

        /// <summary>The policy's team of one of each species (the first MON_SIDE_SIZE), the last tower row of that policy.</summary>
        static TowerSummary FocusTeam(BalanceReport r) =>
            r.Tower.LastOrDefault(t => t.Policy == FocusPolicy && t.Team.Contains("+"));

        /// <summary>The balance verb's exit code: 0 unless strict (the opt-in --strict) and a check WARNs. SKIP never fails.</summary>
        public static int ExitCode(IEnumerable<CheckResult> checks, bool strict) =>
            strict && checks.Any(k => k.Status == CheckStatus.Warn) ? 1 : 0;

        public static string StatusName(CheckStatus s) => s == CheckStatus.Pass ? "PASS" : s == CheckStatus.Warn ? "WARN" : "SKIP";

        static string N(double x) => x.ToString("0.##", Inv);

        static string Pct(double x) => (x * 100).ToString("0.#", Inv) + "%";
    }
}
