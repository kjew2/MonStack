using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using IdleRng.Server;
using Newtonsoft.Json;

namespace MonStackAccount
{
    /// <summary>
    /// Leaderboards and display names in Cloud Save private custom items (specs/27 section 3.3, specs/28 section 6.1), in place of
    /// UGS Leaderboards (agent, 2026-11-03, kj can veto): a board per season (<c>endless-&lt;season&gt;</c>) would need a UGS
    /// leaderboard created ahead of every week, and taking a debug account off a board needs the Leaderboards admin API with a
    /// service account key inside the module; the shown name would also have to live outside the score. Here a board is a custom
    /// entity named by its board id, holding <see cref="Shards"/> items (<c>rows-0</c> to <c>rows-f</c>, a player's shard by the
    /// first hex digit of the SHA-256 of their id), each a map of player id to <c>[score, atMs]</c>. Names live the same way on
    /// the custom entity <c>board-names</c> (<c>names-0</c> to <c>names-f</c>). A submit, remove or rename rewrites one shard
    /// under its write lock (re-read and retried on a conflict); a read loads the board's shards in one Cloud Save call and the
    /// names it shows in one more. Fine for thousands of players a week; past that, keep the rows ranked or move to UGS
    /// Leaderboards.
    /// </summary>
    public sealed class CloudSaveLeaderboardStore : ILeaderboardStore
    {
        public const int Shards = 16;
        public const string NamesEntity = "board-names";
        const int MaxAttempts = 8;

        static readonly JsonSerializerSettings Json = new JsonSerializerSettings { Culture = CultureInfo.InvariantCulture };

        readonly ICloudSave _save;

        public CloudSaveLeaderboardStore(ICloudSave save) { _save = save ?? throw new ArgumentNullException(nameof(save)); }

        /// <summary>The shard (0 to 15) of a player: the high nibble of the first byte of the SHA-256 of their id.</summary>
        public static int ShardOf(string playerId)
        {
            using (var sha = SHA256.Create()) return sha.ComputeHash(Encoding.UTF8.GetBytes(playerId))[0] >> 4;
        }

        public static string RowsKey(int shard) => "rows-" + shard.ToString("x", CultureInfo.InvariantCulture);
        public static string NamesKey(int shard) => "names-" + shard.ToString("x", CultureInfo.InvariantCulture);

        static IEnumerable<string> AllShards(Func<int, string> key) => Enumerable.Range(0, Shards).Select(key);

        // ------------------------------------------------------------------ writes

        public Task SubmitAsync(string board, string playerId, int score, long atMs)
        {
            if (playerId == null) throw new ArgumentNullException(nameof(playerId));
            return UpdateAsync<long[]>(CloudSaveOwner.Entity(Check(board)), RowsKey(ShardOf(playerId)), rows =>
            {
                if (rows.TryGetValue(playerId, out var r) && r.Length == 2 && score <= r[0]) return false;
                rows[playerId] = new long[] { score, atMs };
                return true;
            });
        }

        public Task RemoveAsync(string board, string playerId)
        {
            if (playerId == null) throw new ArgumentNullException(nameof(playerId));
            return UpdateAsync<long[]>(CloudSaveOwner.Entity(Check(board)), RowsKey(ShardOf(playerId)), rows => rows.Remove(playerId));
        }

        public Task SetNameAsync(string playerId, string name)
        {
            if (playerId == null) throw new ArgumentNullException(nameof(playerId));
            return UpdateAsync<string>(CloudSaveOwner.Entity(NamesEntity), NamesKey(ShardOf(playerId)), names =>
            {
                if (name == null) return names.Remove(playerId);
                if (names.TryGetValue(playerId, out var n) && n == name) return false;
                names[playerId] = name;
                return true;
            });
        }

        /// <summary>Read one shard, change it, write it back under its write lock; on a conflict start again from a fresh read.</summary>
        async Task UpdateAsync<T>(CloudSaveOwner owner, string key, Func<SortedDictionary<string, T>, bool> change)
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var item = await _save.GetOneAsync(owner, key).ConfigureAwait(false);
                var map = Parse<T>(item?.Value);
                if (!change(map)) return;
                string text = JsonConvert.SerializeObject(map, Formatting.None, Json);
                if (await _save.TryReplaceAsync(owner, key, text, item?.WriteLock).ConfigureAwait(false)) return;
            }
            throw new InvalidOperationException("the board item " + owner + "/" + key + " kept changing; try again");
        }

        static SortedDictionary<string, T> Parse<T>(string text)
        {
            var map = new SortedDictionary<string, T>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return map;
            var read = JsonConvert.DeserializeObject<Dictionary<string, T>>(text, Json);
            if (read != null) foreach (var kv in read) if (kv.Value != null) map[kv.Key] = kv.Value;
            return map;
        }

        // ------------------------------------------------------------------ reads

        sealed class Row { public string Player; public int Score; public long AtMs; }

        /// <summary>Every row of the board, ranked: highest score first, then the earlier time, then the player id.</summary>
        async Task<List<Row>> RankedAsync(string board)
        {
            var items = await _save.GetManyAsync(CloudSaveOwner.Entity(Check(board)), AllShards(RowsKey)).ConfigureAwait(false);
            var rows = new List<Row>();
            foreach (var it in items.Values)
                foreach (var kv in Parse<long[]>(it.Value))
                    if (kv.Value.Length == 2) rows.Add(new Row { Player = kv.Key, Score = (int)kv.Value[0], AtMs = kv.Value[1] });
            return rows.OrderByDescending(r => r.Score).ThenBy(r => r.AtMs).ThenBy(r => r.Player, StringComparer.Ordinal).ToList();
        }

        async Task<Dictionary<string, string>> NamesAsync(IEnumerable<string> players)
        {
            var ps = players.ToList();
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            if (ps.Count == 0) return names;
            var keys = ps.Select(p => NamesKey(ShardOf(p))).Distinct();
            var items = await _save.GetManyAsync(CloudSaveOwner.Entity(NamesEntity), keys).ConfigureAwait(false);
            foreach (var it in items.Values)
                foreach (var kv in Parse<string>(it.Value))
                    names[kv.Key] = kv.Value;
            return ps.Where(names.ContainsKey).ToDictionary(p => p, p => names[p], StringComparer.Ordinal);
        }

        static LeaderboardEntry Entry(Row r, int rank, Dictionary<string, string> names) =>
            new LeaderboardEntry { PlayerId = r.Player, Name = names.TryGetValue(r.Player, out var n) ? n : null, Score = r.Score, AtMs = r.AtMs, Rank = rank };

        public async Task<(IReadOnlyList<LeaderboardEntry> Top, int Total)> TopAsync(string board, int count)
        {
            var ranked = await RankedAsync(board).ConfigureAwait(false);
            var top = ranked.Take(Math.Max(0, count)).ToList();
            var names = await NamesAsync(top.Select(r => r.Player)).ConfigureAwait(false);
            return (top.Select((r, i) => Entry(r, i + 1, names)).ToList(), ranked.Count);
        }

        public async Task<LeaderboardEntry> EntryAsync(string board, string playerId)
        {
            var ranked = await RankedAsync(board).ConfigureAwait(false);
            int i = ranked.FindIndex(r => r.Player == playerId);
            if (i < 0) return null;
            var names = await NamesAsync(new[] { playerId }).ConfigureAwait(false);
            return Entry(ranked[i], i + 1, names);
        }

        /// <summary>A board id is a custom entity id: letters, digits, '-' and '_' (EndlessBoard.BoardId gives <c>endless-&lt;n&gt;</c>).</summary>
        static string Check(string board)
        {
            if (string.IsNullOrEmpty(board) || board.Length > 50 || board == NamesEntity
                || board.Any(c => !(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_')))
                throw new ArgumentException("not a board id: " + board, nameof(board));
            return board;
        }
    }
}
