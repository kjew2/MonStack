using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IdleRng.Bots.Training;
using IdleRng.Sim.Account;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Gauntlet;
using IdleRng.Sim.Tower;
using IdleRng.Sim.Training;

namespace IdleRng.Bots.Balance
{
    /// <summary>What the balance harness plays (specs/09 section 7). Every run starts from a fresh account (no talents).</summary>
    public sealed class BalanceOptions
    {
        /// <summary>Seeds per species x policy: SeedBase, SeedBase + 1, ...</summary>
        public int Seeds { get; set; } = 200;
        public ulong SeedBase { get; set; } = 1;

        /// <summary>Species ids, in this order; null = every species in content order.</summary>
        public IList<string> Species { get; set; }

        /// <summary>Run policy names, in this order; null = RunPolicies.Names.</summary>
        public IList<string> Policies { get; set; }

        /// <summary>Skill books every run brings; null or empty = none.</summary>
        public IList<string> Books { get; set; }

        /// <summary>
        /// Each run brings only its species' own skill books: those whose tree a chase tree of the species names (the measure of
        /// the book chase capstone target, specs/15 section 10; issue #111). Not with Books.
        /// </summary>
        public bool OwnBooks { get; set; }

        /// <summary>
        /// Talents every run starts with, each at max rank (null or empty = a fresh account, no talents). The mods are what an
        /// account with those talents and every other talent off would start with (AccountOps.Modifiers, at most LEGENDARY_SLOTS
        /// legendaries).
        /// </summary>
        public IList<string> Talents { get; set; }

        /// <summary>Also send the sealed monsters up the Dungeon (alone, and one of each species as a team).</summary>
        public bool Tower { get; set; } = true;

        /// <summary>Tower day for seed index i is TowerDayBase + i, so the lineups vary across the sample.</summary>
        public int TowerDayBase { get; set; }

        /// <summary>Also send one of each species (the first MON_SIDE_SIZE, from the same seed) up the Endless Tower (specs/28).</summary>
        public bool Endless { get; set; } = true;

        /// <summary>Endless Tower season for seed index i is EndlessSeasonBase + i.</summary>
        public int EndlessSeasonBase { get; set; }

        /// <summary>
        /// When the main pass brings no books, replay every species x policy with every skill book (no tower) for the books
        /// sanity check (BalanceChecks). Ignored when Books or OwnBooks is set.
        /// </summary>
        public bool BookCanary { get; set; } = true;

        /// <summary>
        /// The trees the tree power section measures (TreePower), in this order; null = every generic tree whose capstone is a
        /// rule node (TreePower.Trees). Any tree id works, so a test can measure a chase tree's capstone.
        /// </summary>
        public IList<string> PowerTrees { get; set; }

        /// <summary>Worker threads (1 = serial). Results are collected by index, so the report does not depend on it.</summary>
        public int Jobs { get; set; } = 1;
    }

    /// <summary>One bot run, sealed, and its gauntlet climb.</summary>
    public sealed class RunSample
    {
        public string Species { get; set; }
        public string Policy { get; set; }
        public ulong Seed { get; set; }
        public int RankIndex { get; set; }
        public RunEnd End { get; set; }
        public int Cycles { get; set; }
        public int FightsFought { get; set; }
        public int FightsWon { get; set; }
        public int Drills { get; set; }
        public int Rests { get; set; }
        public int Milestones { get; set; }

        /// <summary>Injuries the run took, and the whole cycles it sat out because of them (specs/14 section 4.1).</summary>
        public int Injuries { get; set; }
        public int CyclesLost { get; set; }

        /// <summary>The run's Training Shards (AccountOps.Payout).</summary>
        public long Shards { get; set; }
        public int GauntletFloor { get; set; }
        public bool GauntletCleared { get; set; }
        public string GauntletKiller { get; set; }

        /// <summary>The gauntlet ended on a boss floor (GauntletSim.IsBoss); false when cleared. Issue #99.</summary>
        public bool GauntletBossStall { get; set; }

        /// <summary>
        /// Chase power (specs/15 section 5.5): the gauntlet floor of the run's monster without any chase tree (its chase tree
        /// nodes, flat stats included, taken out), then with its species' in-run, or its second (book-gated or harder in-run; the
        /// "Book" names stay from the chase_tree_book column), chase tree fully bought on top (same seed).
        /// </summary>
        public int CapstoneBaseFloor { get; set; }
        public int CapstoneRunFloor { get; set; }
        public int CapstoneBookFloor { get; set; }

        /// <summary>
        /// Fusion power (specs/15 section 5.5): per fusing pair the species can hold (no parent is another species' native tree),
        /// the floors the fused tree adds on top of both parents maxed (no chase trees); the mean over those pairs. NaN when none.
        /// </summary>
        public double FusionGain { get; set; } = double.NaN;

        /// <summary>The species' chase trees in content order: conditions held, unlocked, capstone and its cycle (ChaseTracker).</summary>
        public List<ChaseOutcome> Chase { get; set; } = new List<ChaseOutcome>();

        /// <summary>The earliest cycle a capstone the run ended with was bought; 0 = the run ended with no capstone.</summary>
        public int CapstoneCycle => Chase.Where(x => x.Capstone).Select(x => x.CapstoneCycle).DefaultIfEmpty(0).Min();

        /// <summary>The run's rank ladder (RunState.Ladder) and each rank fight it fought (specs/14 section 5).</summary>
        public List<string> Ladder { get; set; } = new List<string>();
        public List<FightRecord> Fights { get; set; } = new List<FightRecord>();

        /// <summary>The finished run, for building tower teams; not reported.</summary>
        internal RunState State { get; set; }
    }

    /// <summary>One pool monster across a cell's runs: how many ladders rolled it, and the rank fights against it and won.</summary>
    public sealed class OpponentSummary
    {
        public string Id { get; set; }
        public int Rank { get; set; }
        public int Rolled { get; set; }
        public int Fought { get; set; }
        public int Won { get; set; }
        public double WinRate => Fought == 0 ? 0 : (double)Won / Fought;
    }

    /// <summary>One Dungeon attempt by a team of sealed monsters on a day.</summary>
    public sealed class TowerSample
    {
        public string Policy { get; set; }
        public string Team { get; set; }
        public ulong Seed { get; set; }
        public int Day { get; set; }
        public int FloorsCleared { get; set; }
        public bool Cleared { get; set; }

        /// <summary>TOWER_SHARDS_PER_FLOOR x floors cleared: what one ticket pays.</summary>
        public long Shards { get; set; }

        /// <summary>Skill Books the cleared floors drop (TowerSim.BookDrop).</summary>
        public int Books { get; set; }
    }

    /// <summary>One Endless Tower climb by a team of sealed monsters in a season (specs/28).</summary>
    public sealed class EndlessSample
    {
        public string Policy { get; set; }
        public string Team { get; set; }
        public ulong Seed { get; set; }
        public int Season { get; set; }
        public int FloorsCleared { get; set; }
        public bool Capped { get; set; }

        /// <summary>What stopped the climb: "elite", the losing floor's shape check (aoe, focus, open), or "capped".</summary>
        public string StoppedBy { get; set; }
    }

    /// <summary>Endless Tower results for one policy x team.</summary>
    public sealed class EndlessSummary
    {
        public string Policy { get; set; }
        public string Team { get; set; }
        public int N { get; set; }
        public Distribution FloorsCleared { get; set; }
        public int Capped { get; set; }
        public double CappedRate => N == 0 ? 0 : (double)Capped / N;
        public int ReachedFloor1 { get; set; }

        /// <summary>What stopped the climbs, most common first.</summary>
        public List<Tally> StoppedBy { get; set; }

        public static EndlessSummary From(string policy, string team, IReadOnlyList<EndlessSample> runs) => new EndlessSummary
        {
            Policy = policy, Team = team, N = runs.Count,
            FloorsCleared = Distribution.Of(runs.Select(r => r.FloorsCleared)),
            Capped = runs.Count(r => r.Capped),
            ReachedFloor1 = runs.Count(r => r.FloorsCleared >= 1),
            StoppedBy = runs.GroupBy(r => r.StoppedBy, StringComparer.Ordinal)
                .Select(g => new Tally { Key = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).ThenBy(x => x.Key, StringComparer.Ordinal).ToList(),
        };
    }

    /// <summary>A metric value counted across runs (a killer, an end reason) with its count.</summary>
    public sealed class Tally
    {
        public string Key { get; set; }
        public int Count { get; set; }
    }

    /// <summary>One chase condition across a cell's runs: how many runs it held in at some point.</summary>
    public sealed class ConditionSummary
    {
        public string Id { get; set; }

        /// <summary>Short label (ChaseTracker.Label), e.g. "heavy_armor maxed", "def>=500".</summary>
        public string Label { get; set; }
        public int Held { get; set; }
        public double HeldRate { get; set; }
    }

    /// <summary>One chase tree across a cell's runs (specs/09 section 7): unlocked, ended with its capstone, and when.</summary>
    public sealed class ChaseSummary
    {
        public string TreeId { get; set; }
        public int N { get; set; }
        public int Unlocked { get; set; }
        public double UnlockRate => N == 0 ? 0 : (double)Unlocked / N;
        public int Capstones { get; set; }
        public double CapstoneRate => N == 0 ? 0 : (double)Capstones / N;

        /// <summary>Cycle the capstone was bought, over the runs that ended with it.</summary>
        public Distribution CapstoneCycle { get; set; }
        public List<ConditionSummary> Conditions { get; set; }
    }

    /// <summary>Everything the report says about one species x policy.</summary>
    public sealed class CellSummary
    {
        public string Species { get; set; }
        public string Policy { get; set; }
        public int N { get; set; }

        /// <summary>Runs ending at each rank, indexed like ContentDb.FightRanks.</summary>
        public int[] Grades { get; set; }
        public Distribution Grade { get; set; }
        public int Completed { get; set; }
        /// <summary>Runs with at least one injury, and their share (an injury costs cycles, it no longer ends the run).</summary>
        public int Injured { get; set; }
        public double InjuryRate => N == 0 ? 0 : (double)Injured / N;

        /// <summary>Injuries per run, and whole cycles sat out per run.</summary>
        public Distribution Injuries { get; set; }
        public Distribution CyclesLost { get; set; }
        public Distribution Cycles { get; set; }
        public Distribution FightsWon { get; set; }
        public Distribution FightsLost { get; set; }
        public Distribution Rests { get; set; }

        /// <summary>
        /// Share of a run's cycles lost to injury, per run: the injured cycles plus the whole cycles sat out, over the cycles the
        /// run reached (specs/14 section 4.1); 1 for a legacy run that an injury ended. The injury-total check reads its mean.
        /// </summary>
        public Distribution InjuryLoss { get; set; }

        /// <summary>Rank fights won / fought over every run of the cell (0 when none were fought).</summary>
        public double FightWinRate { get; set; }
        public Distribution Shards { get; set; }
        public Distribution GauntletFloor { get; set; }
        public int GauntletCleared { get; set; }
        public double GauntletClearRate => N == 0 ? 0 : (double)GauntletCleared / N;

        /// <summary>Runs whose gauntlet ended on a boss floor, and their share of the runs that did not clear (issue #99).</summary>
        public int GauntletBossStalls { get; set; }
        public double GauntletBossStallRate => N - GauntletCleared == 0 ? 0 : (double)GauntletBossStalls / (N - GauntletCleared);

        /// <summary>
        /// Where the runs that did not clear stalled: index i counts the ones whose floor reached mod GAUNTLET_BOSS_EVERY is i, so
        /// index 0 is the boss floors (issue #99). Empty when From was not given the boss spacing.
        /// </summary>
        public int[] GauntletStallMod { get; set; } = Array.Empty<int>();

        /// <summary>Gauntlet killers, most runs first (ties by id).</summary>
        public List<Tally> Killers { get; set; }

        /// <summary>Runs that ended with at least one chase capstone bought, and their share (issue #111).</summary>
        public int Capstoned { get; set; }
        public double CapstoneRate => N == 0 ? 0 : (double)Capstoned / N;

        /// <summary>Per capstoned run, the earliest cycle a capstone it ended with was bought.</summary>
        public Distribution CapstoneCycle { get; set; }

        /// <summary>Chase power (specs/15 section 5.5): the gauntlet floor without a chase tree, and the floors each whole chase tree adds.</summary>
        public Distribution CapstoneBase { get; set; }
        public Distribution CapstoneGainRun { get; set; }
        public Distribution CapstoneGainBook { get; set; }

        /// <summary>Fusion power: the floors a fused tree adds (runs of a species with no fusing pair are left out).</summary>
        public Distribution FusionGain { get; set; }

        /// <summary>Per chase tree of the species, in the order the runs list them (content order).</summary>
        public List<ChaseSummary> Chase { get; set; }

        /// <summary>Per pool monster any run rolled or fought, by rank then id (rank fights against legacy flat opponents are left out).</summary>
        public List<OpponentSummary> Opponents { get; set; } = new List<OpponentSummary>();

        /// <param name="bossEvery">GAUNTLET_BOSS_EVERY, for the stall-floor histogram; 0 leaves GauntletStallMod empty.</param>
        public static CellSummary From(string species, string policy, int rankCount, IReadOnlyList<RunSample> runs, int bossEvery = 0)
        {
            if (rankCount < 1) throw new ArgumentOutOfRangeException(nameof(rankCount));
            if (bossEvery < 0) throw new ArgumentOutOfRangeException(nameof(bossEvery));
            var s = new CellSummary { Species = species, Policy = policy, N = runs.Count, Grades = new int[rankCount], GauntletStallMod = new int[bossEvery] };
            long fought = 0, won = 0;
            foreach (var r in runs)
            {
                if (r.RankIndex < 0 || r.RankIndex >= rankCount) throw new ArgumentException($"rank {r.RankIndex} out of range");
                s.Grades[r.RankIndex]++;
                if (r.Injuries > 0 || r.End == RunEnd.Injured) s.Injured++;
                if (r.End == RunEnd.Completed) s.Completed++;
                if (r.GauntletCleared) s.GauntletCleared++;
                if (r.GauntletBossStall) s.GauntletBossStalls++;
                if (bossEvery > 0 && !r.GauntletCleared) s.GauntletStallMod[r.GauntletFloor % bossEvery]++;
                fought += r.FightsFought;
                won += r.FightsWon;
            }
            s.FightWinRate = fought == 0 ? 0 : (double)won / fought;
            s.Grade = Distribution.Of(runs.Select(r => r.RankIndex));
            s.Cycles = Distribution.Of(runs.Select(r => r.Cycles));
            s.Injuries = Distribution.Of(runs.Select(r => r.Injuries));
            s.CyclesLost = Distribution.Of(runs.Select(r => r.CyclesLost));
            s.InjuryLoss = Distribution.Of(runs.Select(r => r.End == RunEnd.Injured ? 1 : Math.Min(1, (double)(r.Injuries + r.CyclesLost) / Math.Max(1, r.Cycles))));
            s.FightsWon = Distribution.Of(runs.Select(r => r.FightsWon));
            s.FightsLost = Distribution.Of(runs.Select(r => r.FightsFought - r.FightsWon));
            s.Rests = Distribution.Of(runs.Select(r => r.Rests));
            s.Shards = Distribution.Of(runs.Select(r => (double)r.Shards));
            s.GauntletFloor = Distribution.Of(runs.Select(r => r.GauntletFloor));
            s.Killers = Count(runs.Where(r => r.GauntletKiller != null).Select(r => r.GauntletKiller));
            s.Capstoned = runs.Count(r => r.CapstoneCycle > 0);
            s.CapstoneCycle = Distribution.Of(runs.Where(r => r.CapstoneCycle > 0).Select(r => r.CapstoneCycle));
            s.CapstoneBase = Distribution.Of(runs.Select(r => r.CapstoneBaseFloor));
            s.CapstoneGainRun = Distribution.Of(runs.Select(r => r.CapstoneRunFloor - r.CapstoneBaseFloor));
            s.CapstoneGainBook = Distribution.Of(runs.Select(r => r.CapstoneBookFloor - r.CapstoneBaseFloor));
            s.FusionGain = Distribution.Of(runs.Where(r => !double.IsNaN(r.FusionGain)).Select(r => r.FusionGain));
            s.Chase = SummariseChase(runs);
            s.Opponents = SummariseOpponents(runs);
            return s;
        }

        static List<OpponentSummary> SummariseOpponents(IReadOnlyList<RunSample> runs)
        {
            var by = new Dictionary<string, OpponentSummary>(StringComparer.Ordinal);
            OpponentSummary Get(string id, int rank) =>
                by.TryGetValue(id, out var o) ? o : by[id] = new OpponentSummary { Id = id, Rank = rank };
            foreach (var r in runs)
            {
                for (int i = 0; i < r.Ladder.Count; i++) Get(r.Ladder[i], i).Rolled++;
                foreach (var f in r.Fights)
                {
                    if (f.Opponent == null) continue;
                    var o = Get(f.Opponent, f.Rank);
                    o.Fought++;
                    if (f.Won) o.Won++;
                }
            }
            return by.Values.OrderBy(o => o.Rank).ThenBy(o => o.Id, StringComparer.Ordinal).ToList();
        }

        static List<ChaseSummary> SummariseChase(IReadOnlyList<RunSample> runs)
        {
            var list = new List<ChaseSummary>();
            foreach (var g in runs.SelectMany(r => r.Chase).GroupBy(o => o.TreeId, StringComparer.Ordinal))
            {
                var outs = g.ToList();
                var first = outs[0];
                var cs = new ChaseSummary
                {
                    TreeId = g.Key, N = runs.Count, Unlocked = outs.Count(o => o.Unlocked), Capstones = outs.Count(o => o.Capstone),
                    CapstoneCycle = Distribution.Of(outs.Where(o => o.Capstone).Select(o => o.CapstoneCycle)),
                    Conditions = new List<ConditionSummary>(),
                };
                for (int j = 0; j < first.ConditionIds.Length; j++)
                {
                    int held = outs.Count(o => o.ConditionsHeld[j]);
                    cs.Conditions.Add(new ConditionSummary
                    {
                        Id = first.ConditionIds[j], Label = first.ConditionLabels[j], Held = held, HeldRate = (double)held / runs.Count,
                    });
                }
                list.Add(cs);
            }
            return list;
        }

        internal static List<Tally> Count(IEnumerable<string> keys) =>
            keys.GroupBy(k => k, StringComparer.Ordinal)
                .Select(g => new Tally { Key = g.Key, Count = g.Count() })
                .OrderByDescending(t => t.Count).ThenBy(t => t.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>Dungeon results for one policy x team.</summary>
    public sealed class TowerSummary
    {
        public string Policy { get; set; }
        public string Team { get; set; }
        public int N { get; set; }
        public Distribution FloorsCleared { get; set; }
        public int Cleared { get; set; }
        public double ClearRate => N == 0 ? 0 : (double)Cleared / N;

        /// <summary>Attempts that cleared at least one floor.</summary>
        public int ReachedFloor1 { get; set; }
        public Distribution Shards { get; set; }
        public Distribution Books { get; set; }

        public static TowerSummary From(string policy, string team, IReadOnlyList<TowerSample> runs) => new TowerSummary
        {
            Policy = policy, Team = team, N = runs.Count,
            FloorsCleared = Distribution.Of(runs.Select(r => r.FloorsCleared)),
            Cleared = runs.Count(r => r.Cleared),
            ReachedFloor1 = runs.Count(r => r.FloorsCleared >= 1),
            Shards = Distribution.Of(runs.Select(r => (double)r.Shards)),
            Books = Distribution.Of(runs.Select(r => r.Books)),
        };
    }

    public sealed class BalanceReport
    {
        public string ContentVersion { get; set; }
        public string ContentHash { get; set; }
        public BalanceOptions Options { get; set; }

        /// <summary>FightRanks ids, lowest first (the grade columns).</summary>
        public List<string> RankIds { get; set; }
        public int GauntletFloors { get; set; }
        public int TowerFloors { get; set; }

        /// <summary>GAUNTLET_BOSS_EVERY: the first boss floor, for the gauntlet sanity check.</summary>
        public int GauntletBossEvery { get; set; }
        public List<CellSummary> Cells { get; set; } = new List<CellSummary>();
        public List<TowerSummary> Tower { get; set; } = new List<TowerSummary>();
        public List<EndlessSummary> Endless { get; set; } = new List<EndlessSummary>();

        /// <summary>The book canary pass (BalanceOptions.BookCanary): the same cells, every run bringing every skill book. Empty when not run.</summary>
        public List<CellSummary> BookCells { get; set; } = new List<CellSummary>();

        /// <summary>Whether the tree power section ran: it needs the focus policy (TreePower.Policy).</summary>
        public bool TreePowerRan { get; set; }

        /// <summary>Tree power (TreePower): per measured tree, a row per species and a pooled row. Empty when no tree qualifies.</summary>
        public List<TreePowerSummary> TreePower { get; set; } = new List<TreePowerSummary>();

        /// <summary>The loose sanity checks (BalanceChecks), evaluated over everything above.</summary>
        public List<CheckResult> Checks { get; set; } = new List<CheckResult>();
    }

    /// <summary>
    /// The run-balance harness (specs/09 section 7): plays every species x policy over many seeds with the bots, seals each run
    /// (shards, gauntlet) and optionally sends the monsters up the Dungeon, then summarises and runs the loose sanity checks
    /// (BalanceChecks). A report, not a gate: a WARN fails nothing. Deterministic: the same content and options give the same
    /// report, at any Jobs.
    /// </summary>
    public static class BalanceHarness
    {
        public static BalanceReport Run(ContentDb c, BalanceOptions o)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (o == null) throw new ArgumentNullException(nameof(o));
            if (o.Seeds < 1) throw new ArgumentException("--seeds must be at least 1");
            if (o.Jobs < 1) throw new ArgumentException("--jobs must be at least 1");
            if (o.TowerDayBase < 0) throw new ArgumentException("--tower-day must be >= 0");
            if (o.EndlessSeasonBase < 0) throw new ArgumentException("--endless-season must be >= 0");
            var species = (o.Species ?? c.SpeciesList.Select(s => s.Id).ToList()).ToList();
            var policies = (o.Policies ?? RunPolicies.Names).ToList();
            if (species.Count == 0 || policies.Count == 0) throw new ArgumentException("need at least one species and one policy");
            if (species.Distinct(StringComparer.Ordinal).Count() != species.Count) throw new ArgumentException("--species lists a species twice");
            if (policies.Distinct(StringComparer.Ordinal).Count() != policies.Count) throw new ArgumentException("--policies lists a policy twice");
            foreach (var sp in species)
                if (!c.Species.ContainsKey(sp)) throw new ArgumentException($"unknown species '{sp}'");
            foreach (var p in policies) RunPolicies.Create(p);   // throws on an unknown name
            var books = o.Books == null || o.Books.Count == 0 ? null : o.Books.ToList();
            if (books != null)
                foreach (var b in books)
                    if (!c.SkillBooks.ContainsKey(b)) throw new ArgumentException($"unknown skill book '{b}'");
            if (books != null && o.OwnBooks) throw new ArgumentException("pass either a list of books or own books, not both");
            var mods = TalentMods(c, o.Talents);
            var powerTrees = o.PowerTrees == null ? TreePower.Trees(c) : o.PowerTrees.Select(id =>
                c.Trees.TryGetValue(id, out var t) ? t : throw new ArgumentException($"unknown tree '{id}'")).ToList();
            if (powerTrees.Select(t => t.Id).Distinct().Count() != powerTrees.Count) throw new ArgumentException("a power tree is listed twice");
            if (powerTrees.Any(t => t.Nodes.Count == 0)) throw new ArgumentException("a power tree has no nodes");

            var runs = PlayAll(c, o, policies, species, books, mods);
            var report = new BalanceReport
            {
                ContentVersion = c.Version, ContentHash = c.ContentHash, Options = o,
                RankIds = c.FightRanks.Select(r => r.Id).ToList(),
                GauntletFloors = c.Constants.GauntletFloors, TowerFloors = c.Constants.TowerFloors,
                GauntletBossEvery = c.Constants.GauntletBossEvery,
            };
            report.Cells = Summarise(c, o, policies, species, runs);
            int focus = policies.IndexOf(TreePower.Policy);
            if (focus >= 0)
            {
                report.TreePowerRan = true;
                report.TreePower = MeasureTrees(c, o, species, new ArraySegment<RunSample>(runs, focus * species.Count * o.Seeds, species.Count * o.Seeds), powerTrees);
            }
            if (books == null && !o.OwnBooks && o.BookCanary && c.SkillBooks.Count > 0)
            {
                var all = c.SkillBooks.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                report.BookCells = Summarise(c, o, policies, species, PlayAll(c, o, policies, species, all, mods));
            }
            if (o.Tower) ClimbAll(c, o, policies, species, runs, report);
            if (o.Endless) ClimbEndlessAll(c, o, policies, species, runs, report);
            report.Checks = BalanceChecks.Evaluate(report);
            return report;
        }

        /// <summary>Every (policy, species, seed) run, in that order.</summary>
        /// <summary>The run modifiers of <see cref="BalanceOptions.Talents"/>, or null for none. Throws for an unknown talent or too many legendaries.</summary>
        public static RunModifiers TalentMods(ContentDb c, IList<string> talents)
        {
            if (talents == null || talents.Count == 0) return null;
            var acct = AccountOps.New(c, 0);
            foreach (var id in talents)
            {
                if (!c.Talents.TryGetValue(id, out var t)) throw new ArgumentException($"unknown talent '{id}'");
                acct.Talents[id] = t.MaxRank;
            }
            return AccountOps.Modifiers(c, acct, AccountOps.CheckLoadout(c, acct, new string[0]));
        }

        static RunSample[] PlayAll(ContentDb c, BalanceOptions o, List<string> policies, List<string> species, List<string> books, RunModifiers mods)
        {
            int perPolicy = species.Count * o.Seeds;
            var runs = new RunSample[policies.Count * perPolicy];
            var own = o.OwnBooks && books == null
                ? species.ToDictionary(sp => sp, sp => c.SkillBooks.Keys.Where(b => ChaseTarget.IsOwnBook(c, sp, b)).OrderBy(b => b, StringComparer.Ordinal).ToList(), StringComparer.Ordinal)
                : null;
            For(runs.Length, o.Jobs, i =>
            {
                string policy = policies[i / perPolicy];
                string sp = species[i % perPolicy / o.Seeds];
                ulong seed = o.SeedBase + (ulong)(i % o.Seeds);
                runs[i] = Play(c, policy, sp, seed, own != null ? own[sp] : books, mods);
            });
            return runs;
        }

        static List<CellSummary> Summarise(ContentDb c, BalanceOptions o, List<string> policies, List<string> species, RunSample[] runs)
        {
            int perPolicy = species.Count * o.Seeds;
            var cells = new List<CellSummary>();
            for (int p = 0; p < policies.Count; p++)
                for (int s = 0; s < species.Count; s++)
                    cells.Add(CellSummary.From(species[s], policies[p], c.FightRanks.Count,
                        new ArraySegment<RunSample>(runs, p * perPolicy + s * o.Seeds, o.Seeds), c.Constants.GauntletBossEvery));
            return cells;
        }

        /// <summary>Tree power over the focus runs: every (tree, run) pair, collected by index.</summary>
        static List<TreePowerSummary> MeasureTrees(ContentDb c, BalanceOptions o, List<string> species, IReadOnlyList<RunSample> focusRuns, List<TreeDef> trees)
        {
            var samples = new (int Base, int With)[trees.Count * focusRuns.Count];
            For(samples.Length, o.Jobs, i => samples[i] = TreePower.Measure(c, focusRuns[i % focusRuns.Count].State, trees[i / focusRuns.Count]));
            var perTree = Enumerable.Range(0, trees.Count)
                .Select(t => (IReadOnlyList<(int, int)>)new ArraySegment<(int, int)>(samples, t * focusRuns.Count, focusRuns.Count)).ToArray();
            return TreePower.Summarise(trees, species, focusRuns.Select(r => r.Species).ToList(), perTree);
        }

        static void ClimbAll(ContentDb c, BalanceOptions o, List<string> policies, List<string> species, RunSample[] runs, BalanceReport report)
        {
            int perPolicy = species.Count * o.Seeds;

            // Teams per policy: each species alone, then one of each species (first MON_SIDE_SIZE) from the same seed together.
            var teams = species.Select(sp => new[] { sp }).ToList();
            var all = species.Take(c.Constants.MonSideSize).ToArray();
            if (all.Length > 1) teams.Add(all);
            int perTeam = o.Seeds, perPolicyTower = teams.Count * perTeam;
            var towers = new TowerSample[policies.Count * perPolicyTower];
            For(towers.Length, o.Jobs, i =>
            {
                int p = i / perPolicyTower, t = i % perPolicyTower / perTeam, k = i % perTeam;
                var fighters = teams[t].Select(sp => TrainingRun.MonsterFighter(c,
                    runs[p * perPolicy + species.IndexOf(sp) * o.Seeds + k].State)).ToArray();
                towers[i] = ClimbTower(c, policies[p], string.Join("+", teams[t]), o.SeedBase + (ulong)k, o.TowerDayBase + k, fighters);
            });
            for (int p = 0; p < policies.Count; p++)
                for (int t = 0; t < teams.Count; t++)
                    report.Tower.Add(TowerSummary.From(policies[p], string.Join("+", teams[t]),
                        new ArraySegment<TowerSample>(towers, p * perPolicyTower + t * perTeam, perTeam)));
        }

        /// <summary>Per policy, one of each species (the first MON_SIDE_SIZE, same seed) climbs the Endless Tower; needs that many species.</summary>
        static void ClimbEndlessAll(ContentDb c, BalanceOptions o, List<string> policies, List<string> species, RunSample[] runs, BalanceReport report)
        {
            int size = c.Constants.MonSideSize, perPolicy = species.Count * o.Seeds;
            if (species.Count < size) return;
            var team = species.Take(size).ToArray();
            string name = string.Join("+", team);
            var climbs = new EndlessSample[policies.Count * o.Seeds];
            For(climbs.Length, o.Jobs, i =>
            {
                int p = i / o.Seeds, k = i % o.Seeds;
                var fighters = team.Select(sp => TrainingRun.MonsterFighter(c, runs[p * perPolicy + species.IndexOf(sp) * o.Seeds + k].State)).ToArray();
                climbs[i] = ClimbEndless(c, policies[p], name, o.SeedBase + (ulong)k, o.EndlessSeasonBase + k, fighters);
            });
            for (int p = 0; p < policies.Count; p++)
                report.Endless.Add(EndlessSummary.From(policies[p], name, new ArraySegment<EndlessSample>(climbs, p * o.Seeds, o.Seeds)));
        }

        public static EndlessSample ClimbEndless(ContentDb c, string policy, string team, ulong seed, int season, IReadOnlyList<Fighter> fighters)
        {
            var r = EndlessSim.Run(c, fighters, season);
            var last = r.Floors[r.Floors.Count - 1];
            string stopped = r.Capped ? "capped" : last.Elite ? "elite" : EndlessSim.Shape(c, last.ShapeId).Check;
            return new EndlessSample
            {
                Policy = policy, Team = team, Seed = seed, Season = season, FloorsCleared = r.FloorsCleared, Capped = r.Capped, StoppedBy = stopped,
            };
        }

        /// <summary>One bot run, sealed the way AccountOps.FinishRun seals it (payout, gauntlet on the run seed).</summary>
        public static RunSample Play(ContentDb c, string policy, string species, ulong seed, IEnumerable<string> books = null, RunModifiers mods = null)
        {
            var chase = new ChaseTracker(c, species);
            var fights = new FightTracker();
            var s = RunBot.Play(c, RunPolicies.Create(policy), species, seed, null, books, st => { chase.Observe(st); fights.Observe(st); }, mods);
            var monster = TrainingRun.MonsterFighter(c, s);
            var g = GauntletSim.Run(c, monster, s.Seed);
            var sp = c.Species[species];
            return new RunSample
            {
                CapstoneBaseFloor = GauntletSim.Run(c, ChaseFighter(c, s, null), s.Seed).FloorReached,
                CapstoneRunFloor = GauntletSim.Run(c, ChaseFighter(c, s, sp.ChaseTreeRun), s.Seed).FloorReached,
                CapstoneBookFloor = GauntletSim.Run(c, ChaseFighter(c, s, sp.ChaseTreeBook), s.Seed).FloorReached,
                FusionGain = FusionGainOf(c, s),
                Species = species, Policy = policy, Seed = seed, RankIndex = s.Rank, End = s.End, Cycles = s.Cycle,
                FightsFought = s.FightsFought, FightsWon = s.FightsWon, Drills = s.Drills, Rests = s.Rests,
                Milestones = s.Milestones.Count, Shards = AccountOps.Payout(c, s),
                Injuries = s.Injuries.Count, CyclesLost = s.Injuries.Sum(i => i.CyclesLost),
                GauntletFloor = g.FloorReached, GauntletCleared = g.Cleared, GauntletKiller = g.KillerId,
                GauntletBossStall = !g.Cleared && GauntletSim.IsBoss(c, g.FloorReached), State = s, Chase = chase.Outcomes.ToList(),
                Ladder = new List<string>(s.Ladder), Fights = fights.Fights,
            };
        }

        /// <summary>
        /// The run's monster (TrainingRun.MonsterFighter) without its slotted chase trees, their flat stats taken back out, plus
        /// <paramref name="chaseTree"/> fully bought on top (null = none): its flat stats (capped at STAT_MAX), skills, triggers and rule.
        /// </summary>
        public static Fighter ChaseFighter(ContentDb c, RunState s, string chaseTree) =>
            BuildFighter(c, s, chaseTree == null ? Array.Empty<string>() : new[] { chaseTree });

        /// <summary>
        /// The run's monster without its chase trees (their flat stats taken back out), with each of <paramref name="maxed"/> bought in
        /// full on top: a tree it already holds (slotted or absorbed) gets its missing nodes, any other tree is added (flat stats capped
        /// at STAT_MAX).
        /// </summary>
        public static Fighter BuildFighter(ContentDb c, RunState s, IEnumerable<string> maxed) =>
            BuildFighter(c, s, maxed.Select(id => (id, c.Trees[id].Nodes.Count)));

        /// <summary>
        /// As <see cref="BuildFighter(ContentDb, RunState, IEnumerable{string})"/>, with each tree bought to exactly
        /// <c>Bought</c> nodes: a held tree gets its missing nodes, or loses its last ones when it holds more (only skill, trigger
        /// and rule nodes can be taken back: their flat stats are not tracked per node); any other tree is added.
        /// </summary>
        public static Fighter BuildFighter(ContentDb c, RunState s, IEnumerable<(string TreeId, int Bought)> buys)
        {
            var stats = s.Stats.Clone();
            var held = new List<(string TreeId, int Bought)>();
            foreach (var t in TrainingRun.AllTrees(s))
            {
                if (c.Trees[t.TreeId].Kind == TreeKind.Chase)
                    for (int i = 0; i < StatIds.Trained.Length; i++) AddStat(stats, StatIds.Trained[i], -t.FlatAdded[i]);
                else held.Add((t.TreeId, t.Bought));
            }
            foreach (var (id, bought) in buys)
            {
                var tree = c.Trees[id];
                if (bought < 0 || bought > tree.Nodes.Count) throw new ArgumentOutOfRangeException(nameof(buys), $"{id}: {bought} of {tree.Nodes.Count} nodes");
                int k = held.FindIndex(x => x.TreeId == id);
                int from = k < 0 ? 0 : held[k].Bought;
                for (int i = from; i < bought; i++)
                {
                    var n = tree.Nodes[i];
                    if (n.Kind == NodeKind.Stat) AddStat(stats, n.Stat, Math.Max(0, Math.Min(n.Amount, c.Constants.StatMax - stats.Get(n.Stat))));
                }
                for (int i = bought; i < from; i++)
                    if (tree.Nodes[i].Kind == NodeKind.Stat)
                        throw new ArgumentException($"{id}: cannot take back node {i}, a flat stat node", nameof(buys));
                if (k < 0) held.Add((id, bought)); else held[k] = (id, bought);
            }
            var f = new Fighter { Id = TrainingRun.MonsterId, Element = c.Species[s.SpeciesId].Element, Stats = stats };
            foreach (var (id, bought) in held)
            {
                var nodes = c.Trees[id].Nodes;
                for (int i = 0; i < bought; i++)
                {
                    var n = nodes[i];
                    if (n.Kind == NodeKind.Skill) f.Skills.Add(n.Skill);
                    else if (n.Kind == NodeKind.Trigger) f.Triggers.Add(new TriggeredSkill(n.Trigger.Value, n.Skill));
                    else if (n.Kind == NodeKind.Rule && !f.Rules.Contains(n.RuleId)) f.Rules.Add(n.RuleId);
                }
            }
            return f;
        }

        /// <summary>The fusing pairs a species can hold: neither parent is another species' native tree.</summary>
        public static List<TreeDef> FusionsFor(ContentDb c, string species)
        {
            bool OthersNative(string id) => c.SpeciesList.Any(sp => sp.Id != species && (sp.NativeTree1 == id || sp.NativeTree2 == id));
            return c.TreeList.Where(t => t.Kind == TreeKind.Fusion && !OthersNative(t.FuseA) && !OthersNative(t.FuseB)).ToList();
        }

        static double FusionGainOf(ContentDb c, RunState s)
        {
            var pairs = FusionsFor(c, s.SpeciesId);
            if (pairs.Count == 0) return double.NaN;
            double sum = 0;
            foreach (var t in pairs)
            {
                int without = GauntletSim.Run(c, BuildFighter(c, s, new[] { t.FuseA, t.FuseB }), s.Seed).FloorReached;
                int with = GauntletSim.Run(c, BuildFighter(c, s, new[] { t.FuseA, t.FuseB, t.Id }), s.Seed).FloorReached;
                sum += with - without;
            }
            return sum / pairs.Count;
        }

        static void AddStat(MonsterStats st, string id, double v)
        {
            switch (id)
            {
                case StatIds.Hp: st.Hp += v; break;
                case StatIds.Str: st.Str += v; break;
                case StatIds.Int: st.Int += v; break;
                case StatIds.Wis: st.Wis += v; break;
                case StatIds.Dex: st.Dex += v; break;
                case StatIds.Def: st.Def += v; break;
                case StatIds.Spd: st.Spd += v; break;
                case StatIds.Luk: st.Luk += v; break;
                default: throw new ArgumentException($"not a trained stat: {id}");
            }
        }

        public static TowerSample ClimbTower(ContentDb c, string policy, string team, ulong seed, int day, IReadOnlyList<Fighter> fighters)
        {
            var r = TowerSim.Run(c, fighters, day);
            int books = 0;
            for (int f = 1; f <= r.FloorsCleared; f++) if (TowerSim.BookDrop(c, day, f) != null) books++;
            return new TowerSample
            {
                Policy = policy, Team = team, Seed = seed, Day = day, FloorsCleared = r.FloorsCleared, Cleared = r.Cleared,
                Shards = (long)c.Constants.TowerShardsPerFloor * r.FloorsCleared, Books = books,
            };
        }

        static void For(int n, int jobs, Action<int> body)
        {
            if (jobs <= 1) { for (int i = 0; i < n; i++) body(i); return; }
            Parallel.For(0, n, new ParallelOptions { MaxDegreeOfParallelism = jobs }, body);
        }
    }
}
