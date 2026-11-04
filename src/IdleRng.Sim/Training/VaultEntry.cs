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

        /// <summary>The fusion tree this one was absorbed into (specs/15 section 5.6), or null for a slotted tree.</summary>
        public string FusedInto { get; set; }

        /// <summary>A rolled Awakening tree's rolls (specs/15 section 4.1), or null. The fighter's nodes come from it.</summary>
        public TreeRoll Roll { get; set; }
    }

    /// <summary>
    /// A finished monster, frozen as its result (specs/13 T4, kj 2026-10-30: final stats, slotted trees with bought nodes, rank,
    /// species and run date). It keeps no replay log (kj 2026-11-01, specs/27 section 3.5): <see cref="Fighter"/> is built from
    /// the stored stats and trees, so a sealed monster fights with the stats it was sealed with under later rule changes.
    /// Unspent skill points are lost. Entries sealed before issue #89 may have End = Injured (an injury used to end the run).
    /// </summary>
    public sealed class VaultEntry
    {
        /// <summary>
        /// The index of the run that made it (RunsStarted when it started), unique in the account (specs/27 section 3.1).
        /// -1 for an entry whose run could not be identified when an old save was migrated.
        /// </summary>
        public int Id { get; set; } = -1;

        public LineageRun Lineage { get; set; }

        public string SpeciesId { get; set; }

        /// <summary>Skill books the run brought in (used or not).</summary>
        public List<string> Books { get; set; } = new List<string>();

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

        /// <summary>
        /// Tree nodes (ids, in kit order) a run rule added to the kit beyond the trees' bought nodes, as they were at sealing
        /// (TrainingRun.KitExtra: a Mimic Core copy, Mastery's kept capstones). Null when there were none (lean JSON).
        /// </summary>
        public List<string> KitExtra { get; set; }

        /// <summary>
        /// The end-of-run gauntlet's result (specs/19), run at sealing: the floor the monster fell on (GAUNTLET_FLOORS + 1 when it
        /// cleared the tower), the gauntlet enemy that beat it (null when cleared) and the Legacy Title's string key (its text may
        /// contain {killer}). Only the result is kept: the wheel shows the floors once, from the seal (kj 2026-11-01).
        /// </summary>
        public int GauntletFloor { get; set; }
        public string GauntletKiller { get; set; }
        public string LegacyTitle { get; set; }

        /// <summary>Cleared the whole gauntlet: it has a result and no killer. A method, so it is not saved.</summary>
        public bool ClearedGauntlet() => LegacyTitle != null && GauntletKiller == null;

        /// <summary>Wall-clock time the run was sealed. The sim has no clock: the caller supplies it (0 = not recorded).</summary>
        public long SealedUtcMs { get; set; }

        /// <summary>
        /// The player's lock (specs/18 section 7): a locked entry cannot be dismantled. Account state, not part of the sealed
        /// monster: it toggles freely (AccountOps.SetLocked), and the server keeps it in the Vault index, not the entry (specs/27).
        /// </summary>
        public bool Locked { get; set; }

        /// <summary>The monster as a battle fighter, built from its stored stats and trees (no replay).</summary>
        public Fighter Fighter(ContentDb c)
        {
            var trees = new List<(string, TreeRoll, int)>(Trees.Count);
            foreach (var t in Trees) trees.Add((t.TreeId, t.Roll, t.Bought));
            return TrainingRun.BuildFighter(c, SpeciesId, Stats, trees, KitExtra);
        }
    }

    public static class Vault
    {
        /// <summary>
        /// Freeze a finished run and send the monster up the gauntlet (specs/19). Throws if the run is still going. <paramref name="id"/>
        /// is the run's index in the account (AccountOps.FinishRun passes it).
        /// </summary>
        public static VaultEntry Seal(ContentDb c, RunState s, long sealedUtcMs = 0, int id = -1) => Seal(c, s, sealedUtcMs, id, out _);

        /// <summary>Seal, also handing back the gauntlet floor by floor (for the wheel shown once after sealing).</summary>
        public static VaultEntry Seal(ContentDb c, RunState s, long sealedUtcMs, int id, out Gauntlet.GauntletResult gauntlet)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (s.Phase != RunPhase.Finished) throw new InvalidOperationException("only a finished run can be vaulted");

            var e = new VaultEntry
            {
                Lineage = s.Lineage?.Clone(), Id = id, SpeciesId = s.SpeciesId, End = s.End, RankIndex = s.Rank, Grade = c.FightRanks[s.Rank].Id,
                Cycles = s.Cycle, FightsFought = s.FightsFought, FightsWon = s.FightsWon, Stats = s.Stats.Clone(),
                SealedUtcMs = sealedUtcMs,
            };
            // Books brought into the run = unused ones now + the ones used.
            e.Books.AddRange(s.Books);
            e.Books.AddRange(s.BooksUsed);
            foreach (var slot in s.Slots)
            {
                if (slot == null) continue;
                foreach (var a in slot.Absorbed) e.Trees.Add(new VaultTree { TreeId = a.TreeId, Bought = a.Bought, FusedInto = slot.TreeId, Roll = a.Roll });
                e.Trees.Add(new VaultTree { TreeId = slot.TreeId, Bought = slot.Bought, Roll = slot.Roll });
            }
            var extra = TrainingRun.KitExtra(c, s);
            if (extra.Count > 0) e.KitExtra = extra;
            var g = Gauntlet.GauntletSim.Run(c, TrainingRun.MonsterFighter(c, s), s.Seed);
            e.GauntletFloor = g.FloorReached;
            e.GauntletKiller = g.KillerId;
            e.LegacyTitle = g.TitleKey;
            gauntlet = g;
            return e;
        }
    }
}
