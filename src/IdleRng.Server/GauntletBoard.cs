using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IdleRng.Sim.Content;
using IdleRng.Sim.Gauntlet;
using IdleRng.Sim.Training;

namespace IdleRng.Server
{
    /// <summary>One row of a gauntlet board as players see it: a sealed monster, its trainer's name and its result. No player ids.</summary>
    public sealed class GauntletRow
    {
        public int Rank { get; set; }

        /// <summary>The trainer's display name (or the generated one).</summary>
        public string Name { get; set; }

        public string Species { get; set; }
        public string SpeciesName { get; set; }

        /// <summary>The floor reached: GAUNTLET_FLOORS + 1 for a cleared tower.</summary>
        public int Floor { get; set; }

        public bool Cleared { get; set; }

        /// <summary>"Floor 42: Professional Punching Bag", or "All 100 floors: Tower Breaker" (the Vault card's text).</summary>
        public string Label { get; set; }

        /// <summary>The reader's own monster.</summary>
        public bool You { get; set; }
    }

    /// <summary>A gauntlet board as a player reads it: the top rows, their own best row (also when below the top), how many rows.</summary>
    public sealed class GauntletBoardView
    {
        /// <summary>The species the board ranks, or null for the overall board.</summary>
        public string Species { get; set; }

        public int Total { get; set; }
        public List<GauntletRow> Top { get; set; } = new List<GauntletRow>();

        /// <summary>The reader's best monster on this board, or null when they have none on it.</summary>
        public GauntletRow You { get; set; }
    }

    /// <summary>
    /// The gauntlet boards (specs/19 section 9, kj 2026-11-03): every sealed monster's gauntlet result, one row per monster, all
    /// time, on an overall board (<c>gauntlet</c>) and its species' board (<c>gauntlet-&lt;species&gt;</c>). Ranked by the floor
    /// reached (a cleared tower above every floor), ties to whoever the server first saw with it. Rows stay when the monster is
    /// dismantled. Written by the server after an accepted commit that seals a run (specs/27 section 3.3); accounts that used a
    /// debug command are left out (specs/27 section 6.3). The store's tag keeps the result as sealed: species, Legacy Title key
    /// and killer, so a later content change does not rewrite a row.
    /// </summary>
    public sealed class GauntletBoard
    {
        /// <summary>GAUNTLET_BOARD_TOP: rows a read returns (placeholder, as ENDLESS_BOARD_TOP).</summary>
        public const int DefaultTop = 20;

        public const int MaxTop = EndlessBoard.MaxTop;

        public const string OverallId = "gauntlet";

        readonly ContentDb _c;
        readonly ILeaderboardStore _store;

        public GauntletBoard(ContentDb content, ILeaderboardStore store)
        {
            _c = content ?? throw new ArgumentNullException(nameof(content));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>The board of a species, or the overall board for null.</summary>
        public static string BoardId(string species) => species == null ? OverallId : OverallId + "-" + species;

        /// <summary>The row id of an entry: unique per account (the run's index), so per board.</summary>
        public static string RowId(string playerId, VaultEntry e) => playerId + ":" + e.Id;

        /// <summary>The Vault card's text for a gauntlet result (specs/19 section 3).</summary>
        public static string Label(ContentDb c, int floor, string titleKey, string killer)
        {
            bool cleared = killer == null;
            return (cleared ? "All " + (floor - 1) + " floors" : "Floor " + floor) + ": " + GauntletSim.TitleText(c, titleKey, killer);
        }

        static string Tag(VaultEntry e) => e.SpeciesId + "|" + e.LegacyTitle + "|" + (e.GauntletKiller ?? "");

        /// <summary>
        /// Put the account's sealed monsters on the boards (keep-best per row: a monster already there changes nothing, so a
        /// monster sealed before the boards existed joins at its trainer's next seal). Entries with no gauntlet result (sealed
        /// before the gauntlet) or no run id are skipped.
        /// </summary>
        public async Task RecordAsync(string playerId, IEnumerable<VaultEntry> entries, long nowMs)
        {
            if (playerId == null) throw new ArgumentNullException(nameof(playerId));
            var ranked = (entries ?? Enumerable.Empty<VaultEntry>()).Where(e => e != null && e.LegacyTitle != null && e.Id >= 0 && e.SpeciesId != null).ToList();
            if (ranked.Count == 0) return;
            LeaderboardRow Row(VaultEntry e) => new LeaderboardRow { RowId = RowId(playerId, e), Score = e.GauntletFloor, Tag = Tag(e) };
            await _store.SubmitRowsAsync(BoardId(null), playerId, ranked.Select(Row).ToList(), nowMs).ConfigureAwait(false);
            foreach (var g in ranked.GroupBy(e => e.SpeciesId, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
                await _store.SubmitRowsAsync(BoardId(g.Key), playerId, g.Select(Row).ToList(), nowMs).ConfigureAwait(false);
        }

        /// <summary>Take the player off the overall board and every species' board (an account that just used a debug command).</summary>
        public async Task RemoveAsync(string playerId)
        {
            await _store.RemoveAsync(BoardId(null), playerId).ConfigureAwait(false);
            foreach (var sp in _c.SpeciesList) await _store.RemoveAsync(BoardId(sp.Id), playerId).ConfigureAwait(false);
        }

        /// <summary>
        /// The board of <paramref name="species"/> (null: overall) as <paramref name="playerId"/> sees it: the best
        /// <paramref name="top"/> rows (1 to MaxTop) and their own best. Throws ArgumentException for a species not in the content.
        /// </summary>
        public async Task<GauntletBoardView> ReadAsync(string playerId, string species, int top = DefaultTop)
        {
            if (string.IsNullOrEmpty(playerId)) throw new ArgumentException("a player id is required", nameof(playerId));
            if (species != null && !_c.Species.ContainsKey(species)) throw new ArgumentException("no such species: " + species, nameof(species));
            top = Math.Max(1, Math.Min(MaxTop, top));
            string id = BoardId(species);
            var (rows, total) = await _store.TopAsync(id, top).ConfigureAwait(false);
            var view = new GauntletBoardView { Species = species, Total = total };
            foreach (var e in rows) view.Top.Add(Row(e, playerId));
            var mine = await _store.EntryAsync(id, playerId).ConfigureAwait(false);
            if (mine != null) view.You = Row(mine, playerId);
            return view;
        }

        GauntletRow Row(LeaderboardEntry e, string reader)
        {
            var parts = (e.Tag ?? "").Split('|');
            string sp = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : null;
            string title = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
            string killer = parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null;
            bool cleared = killer == null;
            return new GauntletRow
            {
                Rank = e.Rank,
                Name = e.Name ?? EndlessBoard.FallbackName(e.PlayerId),
                Species = sp,
                SpeciesName = sp != null && _c.Species.TryGetValue(sp, out var def) ? _c.Text(def.NameKey) : sp,
                Floor = e.Score,
                Cleared = cleared,
                Label = title == null ? "Floor " + e.Score : Label(_c, e.Score, title, killer),
                You = e.PlayerId == reader,
            };
        }
    }
}
