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

        /// <summary>Also send the sealed monsters up the Daily Tower (alone, and one of each species as a team).</summary>
        public bool Tower { get; set; } = true;

        /// <summary>Tower day for seed index i is TowerDayBase + i, so the lineups vary across the sample.</summary>
        public int TowerDayBase { get; set; }

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

        /// <summary>The run's Training Shards (AccountOps.Payout).</summary>
        public long Shards { get; set; }
        public int GauntletFloor { get; set; }
        public bool GauntletCleared { get; set; }
        public string GauntletKiller { get; set; }

        /// <summary>The finished run, for building tower teams; not reported.</summary>
        internal RunState State { get; set; }
    }

    /// <summary>One Daily Tower attempt by a team of sealed monsters on a day.</summary>
    public sealed class TowerSample
    {
        public string Policy { get; set; }
        public string Team { get; set; }
        public ulong Seed { get; set; }
        public int Day { get; set; }
        public int FloorsCleared { get; set; }
        public bool Cleared { get; set; }

        /// <summary>TOWER_SHARDS_PER_FLOOR x floors cleared: what a first attempt that day pays.</summary>
        public long Shards { get; set; }

        /// <summary>Skill Books the cleared floors drop (TowerSim.BookDrop).</summary>
        public int Books { get; set; }
    }

    /// <summary>A metric value counted across runs (a killer, an end reason) with its count.</summary>
    public sealed class Tally
    {
        public string Key { get; set; }
        public int Count { get; set; }
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
        public int Injured { get; set; }
        public double InjuryRate => N == 0 ? 0 : (double)Injured / N;
        public Distribution Cycles { get; set; }
        public Distribution FightsWon { get; set; }
        public Distribution FightsLost { get; set; }

        /// <summary>Rank fights won / fought over every run of the cell (0 when none were fought).</summary>
        public double FightWinRate { get; set; }
        public Distribution Shards { get; set; }
        public Distribution GauntletFloor { get; set; }
        public int GauntletCleared { get; set; }
        public double GauntletClearRate => N == 0 ? 0 : (double)GauntletCleared / N;

        /// <summary>Gauntlet killers, most runs first (ties by id).</summary>
        public List<Tally> Killers { get; set; }

        public static CellSummary From(string species, string policy, int rankCount, IReadOnlyList<RunSample> runs)
        {
            if (rankCount < 1) throw new ArgumentOutOfRangeException(nameof(rankCount));
            var s = new CellSummary { Species = species, Policy = policy, N = runs.Count, Grades = new int[rankCount] };
            long fought = 0, won = 0;
            foreach (var r in runs)
            {
                if (r.RankIndex < 0 || r.RankIndex >= rankCount) throw new ArgumentException($"rank {r.RankIndex} out of range");
                s.Grades[r.RankIndex]++;
                if (r.End == RunEnd.Injured) s.Injured++;
                else if (r.End == RunEnd.Completed) s.Completed++;
                if (r.GauntletCleared) s.GauntletCleared++;
                fought += r.FightsFought;
                won += r.FightsWon;
            }
            s.FightWinRate = fought == 0 ? 0 : (double)won / fought;
            s.Grade = Distribution.Of(runs.Select(r => r.RankIndex));
            s.Cycles = Distribution.Of(runs.Select(r => r.Cycles));
            s.FightsWon = Distribution.Of(runs.Select(r => r.FightsWon));
            s.FightsLost = Distribution.Of(runs.Select(r => r.FightsFought - r.FightsWon));
            s.Shards = Distribution.Of(runs.Select(r => (double)r.Shards));
            s.GauntletFloor = Distribution.Of(runs.Select(r => r.GauntletFloor));
            s.Killers = Count(runs.Where(r => r.GauntletKiller != null).Select(r => r.GauntletKiller));
            return s;
        }

        internal static List<Tally> Count(IEnumerable<string> keys) =>
            keys.GroupBy(k => k, StringComparer.Ordinal)
                .Select(g => new Tally { Key = g.Key, Count = g.Count() })
                .OrderByDescending(t => t.Count).ThenBy(t => t.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>Daily Tower results for one policy x team.</summary>
    public sealed class TowerSummary
    {
        public string Policy { get; set; }
        public string Team { get; set; }
        public int N { get; set; }
        public Distribution FloorsCleared { get; set; }
        public int Cleared { get; set; }
        public double ClearRate => N == 0 ? 0 : (double)Cleared / N;
        public Distribution Shards { get; set; }
        public Distribution Books { get; set; }

        public static TowerSummary From(string policy, string team, IReadOnlyList<TowerSample> runs) => new TowerSummary
        {
            Policy = policy, Team = team, N = runs.Count,
            FloorsCleared = Distribution.Of(runs.Select(r => r.FloorsCleared)),
            Cleared = runs.Count(r => r.Cleared),
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
        public List<CellSummary> Cells { get; set; } = new List<CellSummary>();
        public List<TowerSummary> Tower { get; set; } = new List<TowerSummary>();
    }

    /// <summary>
    /// The run-balance harness (specs/09 section 7): plays every species x policy over many seeds with the bots, seals each run
    /// (shards, gauntlet) and optionally sends the monsters up the Daily Tower, then summarises. A report, not a gate: it has
    /// no targets and fails on nothing. Deterministic: the same content and options give the same report, at any Jobs.
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

            // Every (policy, species, seed) run, in that order.
            int perPolicy = species.Count * o.Seeds;
            var runs = new RunSample[policies.Count * perPolicy];
            For(runs.Length, o.Jobs, i =>
            {
                string policy = policies[i / perPolicy];
                string sp = species[i % perPolicy / o.Seeds];
                ulong seed = o.SeedBase + (ulong)(i % o.Seeds);
                runs[i] = Play(c, policy, sp, seed, books);
            });

            var report = new BalanceReport
            {
                ContentVersion = c.Version, ContentHash = c.ContentHash, Options = o,
                RankIds = c.FightRanks.Select(r => r.Id).ToList(),
                GauntletFloors = c.Constants.GauntletFloors, TowerFloors = c.Constants.TowerFloors,
            };
            for (int p = 0; p < policies.Count; p++)
                for (int s = 0; s < species.Count; s++)
                    report.Cells.Add(CellSummary.From(species[s], policies[p], c.FightRanks.Count,
                        new ArraySegment<RunSample>(runs, p * perPolicy + s * o.Seeds, o.Seeds)));
            if (!o.Tower) return report;

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
            return report;
        }

        /// <summary>One bot run, sealed the way AccountOps.FinishRun seals it (payout, gauntlet on the run seed).</summary>
        public static RunSample Play(ContentDb c, string policy, string species, ulong seed, IEnumerable<string> books = null)
        {
            var s = RunBot.Play(c, RunPolicies.Create(policy), species, seed, null, books);
            var g = GauntletSim.Run(c, TrainingRun.MonsterFighter(c, s), s.Seed);
            return new RunSample
            {
                Species = species, Policy = policy, Seed = seed, RankIndex = s.Rank, End = s.End, Cycles = s.Cycle,
                FightsFought = s.FightsFought, FightsWon = s.FightsWon, Drills = s.Drills, Rests = s.Rests,
                Milestones = s.Milestones.Count, Shards = AccountOps.Payout(c, s),
                GauntletFloor = g.FloorReached, GauntletCleared = g.Cleared, GauntletKiller = g.KillerId, State = s,
            };
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
