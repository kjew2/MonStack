using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IdleRng.Server
{
    /// <summary>One player's place on a board.</summary>
    public sealed class LeaderboardEntry
    {
        public string PlayerId { get; set; }

        /// <summary>The player's display name, or null for none.</summary>
        public string Name { get; set; }

        public int Score { get; set; }

        /// <summary>When the player first reached this score, server time in Unix ms (the tie-break: earlier ranks higher).</summary>
        public long AtMs { get; set; }

        /// <summary>1 for the top; ranks are unique (ties are broken by time).</summary>
        public int Rank { get; set; }
    }

    /// <summary>
    /// Leaderboards and display names (specs/27 section 3.3): UGS Leaderboards and the player name on UGS (step 4), memory in tests
    /// and in the page. Only the server writes them; no call takes a score from a client (specs/10 section 4.7). A board ranks by
    /// score, highest first; equal scores rank by who reached them first.
    /// </summary>
    public interface ILeaderboardStore
    {
        /// <summary>Keep the player's best: a higher score replaces theirs (with this time), an equal or lower one changes nothing.</summary>
        Task SubmitAsync(string board, string playerId, int score, long atMs);

        /// <summary>Take the player off the board; no entry is fine.</summary>
        Task RemoveAsync(string board, string playerId);

        /// <summary>The best <paramref name="count"/> entries, top first, and how many the board holds.</summary>
        Task<(IReadOnlyList<LeaderboardEntry> Top, int Total)> TopAsync(string board, int count);

        /// <summary>The player's entry with its rank, or null when they are not on the board.</summary>
        Task<LeaderboardEntry> EntryAsync(string board, string playerId);

        /// <summary>Set (or with null clear) the name every board shows for the player.</summary>
        Task SetNameAsync(string playerId, string name);
    }

    /// <summary>Boards in memory, for tests and the page's local test server. Thread-safe.</summary>
    public sealed class MemoryLeaderboardStore : ILeaderboardStore
    {
        sealed class Row { public string Player; public int Score; public long AtMs; }

        readonly object _gate = new object();
        readonly Dictionary<string, Dictionary<string, Row>> _boards = new Dictionary<string, Dictionary<string, Row>>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _names = new Dictionary<string, string>(StringComparer.Ordinal);

        Dictionary<string, Row> Board(string board)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (!_boards.TryGetValue(board, out var rows)) _boards[board] = rows = new Dictionary<string, Row>(StringComparer.Ordinal);
            return rows;
        }

        /// <summary>Highest score first, then the earlier time, then the player id (so the order never depends on the dictionary).</summary>
        static List<Row> Ranked(Dictionary<string, Row> rows) =>
            rows.Values.OrderByDescending(r => r.Score).ThenBy(r => r.AtMs).ThenBy(r => r.Player, StringComparer.Ordinal).ToList();

        LeaderboardEntry Entry(Row r, int rank) =>
            new LeaderboardEntry { PlayerId = r.Player, Name = _names.TryGetValue(r.Player, out var n) ? n : null, Score = r.Score, AtMs = r.AtMs, Rank = rank };

        public Task SubmitAsync(string board, string playerId, int score, long atMs)
        {
            if (playerId == null) throw new ArgumentNullException(nameof(playerId));
            lock (_gate)
            {
                var rows = Board(board);
                if (!rows.TryGetValue(playerId, out var r) || score > r.Score) rows[playerId] = new Row { Player = playerId, Score = score, AtMs = atMs };
            }
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string board, string playerId)
        {
            lock (_gate) Board(board).Remove(playerId ?? throw new ArgumentNullException(nameof(playerId)));
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
