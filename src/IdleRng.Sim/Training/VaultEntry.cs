using System;
using System.Collections.Generic;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Training
{
    /// <summary>A tree the monster ended the run with and how many of its nodes it had bought.</summary>
    public sealed class VaultTree
    {
        public string TreeId { get; set; }
        public int Bought { get; set; }
    }

    /// <summary>
    /// A finished monster, frozen (specs/13 T4, kj 2026-10-30: final stats, slotted trees with bought nodes, rank, species and
    /// run date). Carries the species, seed, books and command log, so the run replays exactly (TrainingRun.Replay) and
    /// <see cref="Fighter"/> rebuilds the battle snapshot from them. Unspent skill points are lost. Entries sealed
    /// before issue #89 may have End = Injured (an injury used to end the run); their logs still replay to the same fighter.
    /// </summary>
    public sealed class VaultEntry
    {
        public string SpeciesId { get; set; }
        public ulong Seed { get; set; }
        public List<string> Books { get; set; } = new List<string>();
        public List<RunCommand> Log { get; set; } = new List<RunCommand>();

        /// <summary>The account's talent bonuses the run started with (specs/18 section 3); Replay needs them.</summary>
        public RunModifiers Mods { get; set; } = new RunModifiers();

        /// <summary>
        /// The run's rank ladder (RunState.Ladder, specs/14 section 5); Replay needs it. Empty for an entry sealed before the rank
        /// pools (content 4.3): that run fought the flat fight_ranks opponents and replays against them.
        /// </summary>
        public List<string> Ladder { get; set; } = new List<string>();

        /// <summary>The MVP grade: the rank the monster reached (a fight_ranks id, e.g. "c").</summary>
        public string Grade { get; set; }
        public int RankIndex { get; set; }
        public RunEnd End { get; set; }

        /// <summary>Cycles played, including any sat out injured (an old Injured entry: up to the cycle the injury ended).</summary>
        public int Cycles { get; set; }
        public int FightsFought { get; set; }
        public int FightsWon { get; set; }
        public MonsterStats Stats { get; set; } = new MonsterStats();
        public List<VaultTree> Trees { get; set; } = new List<VaultTree>();

        /// <summary>Hash of the finished run (TrainingRun.StateHash): lets a server check the entry against its log.</summary>
        public string RunHash { get; set; }

        /// <summary>
        /// The end-of-run gauntlet (specs/19), run at sealing: the floor the monster fell on (GAUNTLET_FLOORS + 1 when it cleared
        /// the tower), the gauntlet enemy that beat it (null when cleared), the Legacy Title's string key (its text may contain
        /// {killer}) and the result hash. The wheel replays the floors with GauntletSim.Run(c, Fighter(c), Seed).
        /// </summary>
        public int GauntletFloor { get; set; }
        public bool GauntletCleared { get; set; }
        public string GauntletKiller { get; set; }
        public string LegacyTitle { get; set; }
        public string GauntletHash { get; set; }

        /// <summary>Wall-clock time the run was sealed. The sim has no clock: the caller supplies it (0 = not recorded).</summary>
        public long SealedUtcMs { get; set; }

        /// <summary>
        /// The player's lock (specs/18 section 7): a locked entry cannot be dismantled. Account state, not part of the sealed
        /// monster: it is not in RunHash and toggles freely (AccountOps.SetLocked).
        /// </summary>
        public bool Locked { get; set; }

        /// <summary>The monster as a battle fighter, rebuilt by replaying the run.</summary>
        public Fighter Fighter(ContentDb c) =>
            TrainingRun.MonsterFighter(c, TrainingRun.Replay(c, SpeciesId, Seed, Log, Books, Mods, Ladder ?? new List<string>()));
    }

    public static class Vault
    {
        /// <summary>Freeze a finished run and send the monster up the gauntlet (specs/19). Throws if the run is still going.</summary>
        public static VaultEntry Seal(ContentDb c, RunState s, long sealedUtcMs = 0)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (s.Phase != RunPhase.Finished) throw new InvalidOperationException("only a finished run can be vaulted");

            var e = new VaultEntry
            {
                SpeciesId = s.SpeciesId, Seed = s.Seed, End = s.End, RankIndex = s.Rank, Grade = c.FightRanks[s.Rank].Id,
                Cycles = s.Cycle, FightsFought = s.FightsFought, FightsWon = s.FightsWon, Stats = s.Stats.Clone(),
                RunHash = TrainingRun.StateHash(s), SealedUtcMs = sealedUtcMs,
                Log = s.Log.ConvertAll(l => l.Clone()), Mods = s.Mods.Clone(), Ladder = new List<string>(s.Ladder),
            };
            // Books brought into the run = unused ones now + the ones the log used.
            e.Books.AddRange(s.Books);
            foreach (var l in s.Log) if (l.Kind == RunCommandKind.UseBook) e.Books.Add(l.Book);
            foreach (var slot in s.Slots) if (slot != null) e.Trees.Add(new VaultTree { TreeId = slot.TreeId, Bought = slot.Bought });
            var g = Gauntlet.GauntletSim.Run(c, TrainingRun.MonsterFighter(c, s), s.Seed);
            e.GauntletFloor = g.FloorReached;
            e.GauntletCleared = g.Cleared;
            e.GauntletKiller = g.KillerId;
            e.LegacyTitle = g.TitleKey;
            e.GauntletHash = g.Hash;
            return e;
        }
    }
}
