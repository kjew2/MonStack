using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IdleRng.Server
{
    /// <summary>One row's place on a board.</summary>
    public sealed class LeaderboardEntry
    {
        /// <summary>The row's key: the player id on a board with one row per player, else the id <see cref="LeaderboardRow"/> gave.</summary>
        public string RowId { get; set; }

        /// <summary>The player who owns the row (whose name it shows).</summary>
        public string PlayerId { get; set; }

        /// <summary>What the row's writer stored beside the score (opaque to the store), or null.</summary>
        public string Tag { get; set; }

        /// <summary>The player's display name, or null for none.</summary>
        public string Name { get; set; }

        public int Score { get; set; }

        /// <summary>When the player first reached this score, server time in Unix ms (the tie-break: earlier ranks higher).</summary>
        public long AtMs { get; set; }

        /// <summary>1 for the top; ranks are unique (ties are broken by time).</summary>
        public int Rank { get; set; }
    }

    /// <summary>One of a player's rows on a board with several rows per player (the gauntlet boards: one per monster).</summary>
    public sealed class LeaderboardRow
    {
        /// <summary>
        /// Unique on the board across players, which the writer guarantees (GauntletBoard.RowId starts with the player id); never a
        /// player id (a board holds one-row-per-player rows or these, not both). A store may refuse a row another player owns.
        /// </summary>
        public string RowId { get; set; }
        public int Score { get; set; }
        public string Tag { get; set; }
    }

    /// <summary>
    /// Leaderboards and display names (specs/27 section 3.3): Cloud Save custom items on UGS (CloudSaveLeaderboardStore, step 4),
    /// memory in tests and in the page. Only the server writes them; no call takes a score from a client (specs/10 section 4.7). A
    /// board ranks rows by score, highest first; equal scores rank by who reached them first. A board holds one row per player
    /// (<see cref="SubmitAsync"/>, keyed by the player id: the Endless board) or rows the player names (<see cref="SubmitRowsAsync"/>:
    /// a gauntlet board, one per monster), not both.
    /// </summary>
    public interface ILeaderboardStore
    {
        /// <summary>Keep the player's best: a higher score replaces theirs (with this time), an equal or lower one changes nothing.</summary>
        Task SubmitAsync(string board, string playerId, int score, long atMs);

        /// <summary>
        /// Keep the best of each of these rows, all owned by <paramref name="playerId"/>: a row new to the board is added with this
        /// time, a higher score replaces the row's score, tag and time, an equal or lower one changes nothing.
        /// </summary>
        Task SubmitRowsAsync(string board, string playerId, IReadOnlyList<LeaderboardRow> rows, long atMs);

        /// <summary>Take every row the player owns off the board; none is fine.</summary>
        Task RemoveAsync(string board, string playerId);

        /// <summary>The best <paramref name="count"/> entries, top first, and how many the board holds.</summary>
        Task<(IReadOnlyList<LeaderboardEntry> Top, int Total)> TopAsync(string board, int count);

        /// <summary>The player's best row with its rank, or null when they have none on the board.</summary>
        Task<LeaderboardEntry> EntryAsync(string board, string playerId);

        /// <summary>Set (or with null clear) the name every board shows for the player.</summary>
        Task SetNameAsync(string playerId, string name);
    }

    /// <summary>Boards in memory, for tests and the page's local test server. Thread-safe.</summary>
    public sealed class MemoryLeaderboardStore : ILeaderboardStore
    {
        sealed class Row { public string Key; public string Player; public int Score; public long AtMs; public string Tag; }

        readonly object _gate = new object();
        readonly Dictionary<string, Dictionary<string, Row>> _boards = new Dictionary<string, Dictionary<string, Row>>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _names = new Dictionary<string, string>(StringComparer.Ordinal);

        Dictionary<string, Row> Board(string board)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (!_boards.TryGetValue(board, out var rows)) _boards[board] = rows = new Dictionary<string, Row>(StringComparer.Ordinal);
            return rows;
        }

        /// <summary>Highest score first, then the earlier time, then the row id (so the order never depends on the dictionary).</summary>
        static List<Row> Ranked(Dictionary<string, Row> rows) =>
            rows.Values.OrderByDescending(r => r.Score).ThenBy(r => r.AtMs).ThenBy(r => r.Key, StringComparer.Ordinal).ToList();

        LeaderboardEntry Entry(Row r, int rank) => new LeaderboardEntry
        {
            RowId = r.Key, PlayerId = r.Player, Tag = r.Tag, Name = _names.TryGetValue(r.Player, out var n) ? n : null, Score = r.Score, AtMs = r.AtMs, Rank = rank,
        };

        static void Keep(Dictionary<string, Row> rows, string key, string player, int score, string tag, long atMs)
        {
            if (!rows.TryGetValue(key, out var r) || score > r.Score) rows[key] = new Row { Key = key, Player = player, Score = score, AtMs = atMs, Tag = tag };
        }

        public Task SubmitAsync(string board, string playerId, int score, long atMs)
        {
            if (playerId == null) throw new ArgumentNullException(nameof(playerId));
            lock (_gate) Keep(Board(board), playerId, playerId, score, null, atMs);
            return Task.CompletedTask;
        }

        public Task SubmitRowsAsync(string board, string playerId, IReadOnlyList<LeaderboardRow> rows, long atMs)
        {
            if (playerId == null) throw new ArgumentNullException(nameof(playerId));
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            lock (_gate)
            {
                var b = Board(board);
                foreach (var r in rows)
                {
                    if (r?.RowId == null) throw new ArgumentException("every row needs an id", nameof(rows));
                    if (b.TryGetValue(r.RowId, out var had) && had.Player != playerId) throw new ArgumentException("row " + r.RowId + " belongs to another player", nameof(rows));
                    Keep(b, r.RowId, playerId, r.Score, r.Tag, atMs);
                }
            }
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string board, string playerId)
        {
            if (playerId == null) throw new ArgumentNullException(nameof(playerId));
            lock (_gate)
            {
                var b = Board(board);
                foreach (var key in b.Values.Where(r => r.Player == playerId).Select(r => r.Key).ToList()) b.Remove(key);
            }
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<LeaderboardEntry> Top, int Total)> TopAsync(string board, int count)
        {
            lock (_gate)
            {
                var ranked = Ranked(Board(board));
                var top = ranked.Take(Math.Max(0, count)).Select((r, i) => Entry(r, i + 1)).ToList();
                return Task.FromResult<(IReadOnlyList<LeaderboardEntry>, int)>((top, ranked.Count));
            }
        }

        public Task<LeaderboardEntry> EntryAsync(string board, string playerId)
        {
            lock (_gate)
            {
                var ranked = Ranked(Board(board));
                int i = ranked.FindIndex(r => r.Player == playerId);
                return Task.FromResult(i < 0 ? null : Entry(ranked[i], i + 1));
            }
        }

        public Task SetNameAsync(string playerId, string name)
        {
            if (playerId == null) throw new ArgumentNullException(nameof(playerId));
            lock (_gate)
            {
                if (name == null) _names.Remove(playerId);
                else _names[playerId] = name;
            }
            return Task.CompletedTask;
        }
    }
}
