using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IdleRng.Runtime.Save;
using IdleRng.Sim.Account;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
using Newtonsoft.Json;

namespace IdleRng.Server
{
    /// <summary>
    /// The <c>profile</c> projection (specs/27 section 3.3): what other players see of an account on a profile card. Rebuilt from
    /// the account after an accepted commit, never read back by a commit. JSON is lean, as the account's items (a field at its
    /// starting value is left out).
    /// </summary>
    public sealed class PlayerProfile
    {
        /// <summary>The display name (the account's <c>Name</c>, specs/28 section 6.1), or null for none.</summary>
        public string Name { get; set; }

        /// <summary>The best grade a sealed run reached (a fight_ranks id, e.g. "b"), or null before the first seal.</summary>
        public string BestGrade { get; set; }

        /// <summary>The best grade's index in fight_ranks (Records.BestRank); -1 before the first seal.</summary>
        public int BestRank { get; set; } = -1;

        /// <summary>The most Dungeon floors one attempt ever cleared (Tower.ClearedEver).</summary>
        public int BestDungeonFloor { get; set; }

        /// <summary>Achievements earned.</summary>
        public int Achievements { get; set; }

        /// <summary>The showcase monster's result (<see cref="Profiles.ShowcaseOf"/>), without the player's lock; null for an empty Vault.</summary>
        public VaultEntry Showcase { get; set; }

        /// <summary>The server time of the commit that wrote it, Unix seconds.</summary>
        public long AtSec { get; set; }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None, SaveSerializer.Settings);

        public static PlayerProfile FromJson(string json) => JsonConvert.DeserializeObject<PlayerProfile>(json, SaveSerializer.Settings);
    }

    /// <summary>
    /// Where profiles live: on UGS a Cloud Save <b>protected</b> player item (only the server writes it; specs/27 section 3.3.1,
    /// step 4), memory in tests and in the page. Only the server writes; no call takes a profile from a client.
    /// </summary>
    public interface IProfileStore
    {
        /// <summary>Replace the player's profile.</summary>
        Task WriteAsync(string playerId, PlayerProfile profile);

        /// <summary>The player's profile, or null when none was written yet.</summary>
        Task<PlayerProfile> ReadAsync(string playerId);
    }

    /// <summary>Profiles in memory, for tests and the page's local test server. Thread-safe; keeps each profile as its JSON, so a read never shares objects with a write.</summary>
    public sealed class MemoryProfileStore : IProfileStore
    {
        readonly object _gate = new object();
        readonly Dictionary<string, string> _items = new Dictionary<string, string>(StringComparer.Ordinal);

        public Task WriteAsync(string playerId, PlayerProfile profile)
        {
            if (playerId == null) throw new ArgumentNullException(nameof(playerId));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            string json = profile.ToJson();
            lock (_gate) _items[playerId] = json;
            return Task.CompletedTask;
        }

        public Task<PlayerProfile> ReadAsync(string playerId)
        {
            if (playerId == null) throw new ArgumentNullException(nameof(playerId));
            string json;
            lock (_gate) if (!_items.TryGetValue(playerId, out json)) return Task.FromResult<PlayerProfile>(null);
            return Task.FromResult(PlayerProfile.FromJson(json));
        }
    }

    /// <summary>
    /// The profile projection (specs/27 section 3.3): <see cref="AccountServer"/> writes it after an accepted commit that changed a
    /// part it is built from (<see cref="Sources"/>), and loads all of those parts for such a commit so the profile is never built
    /// from a stand-in.
    /// </summary>
    public sealed class Profiles
    {
        /// <summary>The parts a profile is built from: the name, achievements and records (progress), the Dungeon best, the Vault.</summary>
        public const AccountPart Sources = AccountPart.Progress | AccountPart.Dungeon | AccountPart.Vault | AccountPart.Entries;

        readonly IProfileStore _store;

        public Profiles(IProfileStore store) { _store = store ?? throw new ArgumentNullException(nameof(store)); }

        /// <summary>The profile of an account, as of server time <paramref name="nowMs"/>.</summary>
        public static PlayerProfile Of(ContentDb c, AccountState a, long nowMs)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (a == null) throw new ArgumentNullException(nameof(a));
            int best = a.Records?.BestRank ?? -1;
            var show = ShowcaseOf(a);
            if (show != null)
            {
                show = JsonConvert.DeserializeObject<VaultEntry>(JsonConvert.SerializeObject(show, SaveSerializer.Settings), SaveSerializer.Settings);
                show.Locked = false;   // the player's flag, not the monster's (specs/27 section 3.1)
            }
            return new PlayerProfile
            {
                Name = a.Name,
                BestRank = best,
                BestGrade = best >= 0 && best < c.FightRanks.Count ? c.FightRanks[best].Id : null,
                BestDungeonFloor = a.Tower?.ClearedEver ?? 0,
                Achievements = a.Achievements?.Count ?? 0,
                Showcase = show,
                AtSec = nowMs / 1000,
            };
        }

        /// <summary>
        /// The showcase monster: the Vault's best by grade, then by gauntlet floor, then the earliest sealed (lowest id); null for
        /// an empty Vault (agent, 2026-11-03: chosen by the server until the player can pick one; kj can veto).
        /// </summary>
        public static VaultEntry ShowcaseOf(AccountState a)
        {
            VaultEntry best = null;
            foreach (var e in a.Vault)
                if (best == null || e.RankIndex > best.RankIndex
                    || (e.RankIndex == best.RankIndex && (e.GauntletFloor > best.GauntletFloor || (e.GauntletFloor == best.GauntletFloor && e.Id < best.Id))))
                    best = e;
            return best;
        }

        /// <summary>Write the account's profile.</summary>
        public Task WriteAsync(string playerId, PlayerProfile profile) => _store.WriteAsync(playerId, profile);

        /// <summary>
        /// A player's profile as another player sees it: the name filled in with the generated one (EndlessBoard.FallbackName)
        /// when they have none. Null when no profile was written yet.
        /// </summary>
        public async Task<PlayerProfile> ReadAsync(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) throw new ArgumentException("a player id is required", nameof(playerId));
            var p = await _store.ReadAsync(playerId).ConfigureAwait(false);
            if (p != null && p.Name == null) p.Name = EndlessBoard.FallbackName(playerId);
            return p;
        }
    }
}
