using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using IdleRng.Sim.Account;

namespace IdleRng.Server
{
    /// <summary>One row of the Endless Tower season board as players see it: no player ids, the reader's own row marked.</summary>
    public sealed class BoardRow
    {
        public int Rank { get; set; }
        public string Name { get; set; }
        public int Floor { get; set; }
        public bool You { get; set; }
    }

    /// <summary>The board a player reads: the top rows, their own row (also when it is below the top), and how many are on it.</summary>
    public sealed class BoardView
    {
        public int Season { get; set; }
        public int Total { get; set; }
        public List<BoardRow> Top { get; set; } = new List<BoardRow>();

        /// <summary>The reader's row, or null when they have not cleared a floor this season (or are left out).</summary>
        public BoardRow You { get; set; }
    }

    /// <summary>
    /// The Endless Tower season board (specs/28 section 6), a projection the server writes when it accepts a commit (specs/27
    /// section 3.3): one board per weekly season, ranked by the floors cleared that season, ties to whoever reached the floor
    /// first by the server's clock. Accounts that used a debug command are left out (specs/27 section 6.3). Rows show the
    /// player's display name, or a generated one from their player id until they set one.
    /// </summary>
    public sealed class EndlessBoard
    {
        /// <summary>ENDLESS_BOARD_TOP: rows a read returns (placeholder).</summary>
        public const int DefaultTop = 20;

        /// <summary>The most rows a read may ask for.</summary>
        public const int MaxTop = 100;

        readonly ILeaderboardStore _store;

        public EndlessBoard(ILeaderboardStore store) { _store = store ?? throw new ArgumentNullException(nameof(store)); }

        /// <summary>The board id of a season (letters, digits and '-', as UGS leaderboard ids allow).</summary>
        public static string BoardId(int season) => "endless-" + season.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// The name shown for a player with none: "Trainer " and four hex digits of the SHA-256 of the player id. The same on
        /// every host and read; not unique.
        /// </summary>
        public static string FallbackName(string playerId)
        {
            if (playerId == null) throw new ArgumentNullException(nameof(playerId));
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(Encoding.UTF8.GetBytes(playerId));
                return "Trainer " + h[0].ToString("X2", CultureInfo.InvariantCulture) + h[1].ToString("X2", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// After an accepted commit with a climb: put the account's season best on its board (a better one replaces the player's
        /// row with this time; the same or lower changes nothing). Nothing for an account that has not cleared a floor in its
        /// season.
        /// </summary>
        public Task RecordAsync(string playerId, EndlessProgress p, long nowMs)
        {
            if (p == null || p.Season < 0 || p.ClearedSeason <= 0) return Task.CompletedTask;
            return _store.SubmitAsync(BoardId(p.Season), playerId, p.ClearedSeason, nowMs);
        }

        /// <summary>The name every board shows for the player (null: the generated one).</summary>
        public Task SetNameAsync(string playerId, string name) => _store.SetNameAsync(playerId, name);

        /// <summary>Take the player off the boards of these seasons (an account that just used a debug command).</summary>
        public async Task RemoveAsync(string playerId, IEnumerable<int> seasons)
        {
            foreach (var s in seasons) await _store.RemoveAsync(BoardId(s), playerId).ConfigureAwait(false);
        }

        /// <summary>The season's board as <paramref name="playerId"/> sees it: the best <paramref name="top"/> rows (1 to MaxTop) and their own.</summary>
        public async Task<BoardView> ReadAsync(string playerId, int season, int top = DefaultTop)
        {
            if (string.IsNullOrEmpty(playerId)) throw new ArgumentException("a player id is required", nameof(playerId));
            if (season < 0) throw new ArgumentException("the season must not be negative", nameof(season));
            top = Math.Max(1, Math.Min(MaxTop, top));
            string id = BoardId(season);
            var (rows, total) = await _store.TopAsync(id, top).ConfigureAwait(false);
            var view = new BoardView { Season = season, Total = total };
            foreach (var e in rows) view.Top.Add(Row(e, playerId));
            var mine = await _store.EntryAsync(id, playerId).ConfigureAwait(false);
            if (mine != null) view.You = Row(mine, playerId);
            return view;
        }

        static BoardRow Row(LeaderboardEntry e, string reader) =>
            new BoardRow { Rank = e.Rank, Name = e.Name ?? FallbackName(e.PlayerId), Floor = e.Score, You = e.PlayerId == reader };
    }
}
