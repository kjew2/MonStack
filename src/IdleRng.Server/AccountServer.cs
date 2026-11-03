using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IdleRng.Runtime.Save;
using IdleRng.Sim.Tower;
using IdleRng.Sim.Account;
using IdleRng.Sim.Content;

namespace IdleRng.Server
{
    /// <summary>
    /// The account server (specs/27): <see cref="OpenAsync"/> hands a device the account, <see cref="CommitAsync"/> re-applies
    /// the commands the client played since its last commit with the same engine and keeps the result only when its hash equals
    /// the client's. Storage is copy-on-write under one write-locked <c>head</c> item (section 4). The caller (the Cloud Code
    /// module, or the page's local test server) authenticates the player and passes the player id.
    /// </summary>
    public sealed class AccountServer
    {
        static readonly string[] PartNames =
        {
            AccountParts.Training, AccountParts.Wallet, AccountParts.Collection, AccountParts.Progress, AccountParts.Dungeon,
            AccountParts.Endless, AccountParts.Rulebooks, AccountParts.Vault,
        };

        const AccountPart AllParts = AccountPart.Training | AccountPart.Wallet | AccountPart.Collection | AccountPart.Progress
                                     | AccountPart.Dungeon | AccountPart.Endless | AccountPart.Rulebooks | AccountPart.Vault | AccountPart.Entries;

        const int MaxAttempts = 5;
        const long DayMs = 86_400_000L;

        readonly ContentDb _c;
        readonly IAccountStore _store;
        readonly ServerConfig _cfg;

        /// <summary>
        /// A new account's parts, standing in for the parts a commit does not load: a command's result does not depend on the
        /// parts it does not declare (AccountCommands.Reads and Writes, proved by AccountPartsTests), and only loaded parts are
        /// written back.
        /// </summary>
        readonly IReadOnlyDictionary<string, string> _standIn;

        public AccountServer(ContentDb content, IAccountStore store, ServerConfig config = null)
        {
            _c = content ?? throw new ArgumentNullException(nameof(content));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _cfg = config ?? new ServerConfig();
            _standIn = AccountParts.Split(AccountOps.New(_c, 0));
        }

        public ContentDb Content => _c;

        // ------------------------------------------------------------------ open

        /// <summary>
        /// The account for a device (section 5.1). Creates it on the first call (genesis: the server draws the seed). A device
        /// that does not hold the account gets NotHolder unless it claims, which makes it the holder. Also deletes items left by
        /// commits that never landed. Throws ArgumentException for a missing player or device id.
        /// </summary>
        public async Task<OpenResult> OpenAsync(string playerId, OpenRequest request)
        {
            CheckPlayer(playerId);
            string device = request?.DeviceId;
            if (!ValidDevice(device)) throw new ArgumentException("a device id is required (at most " + _cfg.MaxDeviceIdLength + " characters)");

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                long now = _cfg.NowMs();
                var stored = await _store.ReadHeadAsync(playerId).ConfigureAwait(false);
                AccountHead head;
                bool created = false;
                if (stored == null)
                {
                    head = await GenesisAsync(playerId, device, now).ConfigureAwait(false);
                    if (head == null) continue;   // another open created it first: open that one
                    created = true;
                }
                else
                {
                    head = AccountHead.FromJson(stored.Json);
                    if (head.Holder.DeviceId != device)
                    {
                        if (!request.Claim) return new OpenResult { Code = Rejection.NotHolder, ServerSec = now / 1000, Day = DayOf(now), ContentVersion = _c.Version };
                        head.Holder = new Holder { DeviceId = device, SinceSec = now / 1000 };
                        if (!await _store.WriteHeadAsync(playerId, head.ToJson(), stored.WriteLock).ConfigureAwait(false)) continue;
                    }
                }

                string basis = head.ToJson();
                await SweepAsync(playerId, head).ConfigureAwait(false);
                var (account, notice) = await TryLoadAsync(playerId, head, AllParts, basis).ConfigureAwait(false);
                if (account == null) continue;   // a commit landed meanwhile and deleted what this head named: open the new head
                return new OpenResult
                {
                    Account = Encoding.UTF8.GetString(AccountSave.Encode(account)),
                    Notice = notice,
                    Seq = head.Seq,
                    Hash = head.Hash,
                    Created = created,
                    ContentVersion = _c.Version,
                    ServerSec = now / 1000,
                    Day = DayOf(now),
                };
            }
            throw new InvalidOperationException("the account kept changing while it was opened; try again");
        }

        /// <summary>Write a new account at commit 0, held by <paramref name="device"/>. Null when another call created one first.</summary>
        async Task<AccountHead> GenesisAsync(string playerId, string device, long now)
        {
            var account = AccountOps.New(_c, _cfg.Random());
            var parts = AccountParts.Split(account);
            string tag = Tag();
            var head = new AccountHead
            {
                Seq = 0,
                Holder = new Holder { DeviceId = device, SinceSec = now / 1000 },
                Schema = AccountState.CurrentSchemaVersion,
                Content = _c.Version,
            };
            var write = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in PartNames)
            {
                string key = AccountHead.ItemKey(name, 0, tag);
                write[key] = parts[name];
                head.Parts[name] = key;
                head.Hashes[name] = AccountParts.Hash(parts[name]);
            }
            head.Hash = AccountParts.AccountHashOf(head.Hashes);
            await _store.WriteAsync(playerId, write).ConfigureAwait(false);
            if (await _store.WriteHeadAsync(playerId, head.ToJson(), null).ConfigureAwait(false)) return head;
            await TryDeleteAsync(playerId, write.Keys.ToList()).ConfigureAwait(false);
            return null;
        }

        // ------------------------------------------------------------------ commit

        /// <summary>
        /// Re-apply a client's commands and keep the result when it matches (section 4). Returns Accepted with the new commit
        /// number and hash, or a rejection: NotHolder, Stale, ContentStale, Refused (with the command's index and reason) or
        /// Mismatch. A retry of the last accepted commit returns Accepted again without applying anything. Throws
        /// ArgumentException for a missing player id and AccountDamagedException when a stored item fails its hash.
        /// </summary>
        public async Task<CommitResult> CommitAsync(string playerId, CommitRequest request)
        {
            CheckPlayer(playerId);
            long now = _cfg.NowMs();
            var result = await CommitCoreAsync(playerId, request, now).ConfigureAwait(false);
            result.ServerSec = now / 1000;
            result.Day = DayOf(now);
            return result;
        }

        async Task<CommitResult> CommitCoreAsync(string playerId, CommitRequest req, long now)
        {
            if (req == null || req.Commands == null) return CommitResult.Reject(Rejection.Refused, -1, "malformed commit");
            if (!ValidDevice(req.DeviceId)) return CommitResult.Reject(Rejection.Refused, -1, "a device id is required");

            var stored = await _store.ReadHeadAsync(playerId).ConfigureAwait(false);
            if (stored == null) return CommitResult.Reject(Rejection.Stale, -1, "no account yet: open it first");
            var head = AccountHead.FromJson(stored.Json);
            if (head.Holder.DeviceId != req.DeviceId) return CommitResult.Reject(Rejection.NotHolder, -1, "the account was opened on another device");
            if (head.Seq == req.FromSeq + 1 && head.Hash == req.ToHash && head.PrevHash == req.FromHash)
                return new CommitResult { Seq = head.Seq, Hash = head.Hash };   // a retry of the commit that landed last
            if (head.Seq != req.FromSeq || head.Hash != req.FromHash)
                return CommitResult.Reject(Rejection.Stale, -1, "the account is at commit " + head.Seq + ", not " + req.FromSeq);
            if (req.ContentVersion != _c.Version)
                return CommitResult.Reject(Rejection.ContentStale, -1, "the server runs content " + _c.Version + ", the client " + req.ContentVersion);
            if (head.Schema != AccountState.CurrentSchemaVersion)
                // TODO(#127): migrate stored parts when the save schema moves; until then a stored account is always current.
                throw new InvalidOperationException("the stored account is in save schema v" + head.Schema + ", the server reads v" + AccountState.CurrentSchemaVersion);

            // Parse and screen every command before loading anything.
            int n = req.Commands.Count;
            if (n == 0) return CommitResult.Reject(Rejection.Refused, -1, "a commit carries at least one command");
            if (n > _cfg.CommitMaxCommands) return CommitResult.Reject(Rejection.Refused, _cfg.CommitMaxCommands, "a commit carries at most " + _cfg.CommitMaxCommands + " commands");
            var commands = new List<AccountCommand>(n);
            bool debug = false, finished = false;
            int climbs = 0;
            var touched = AccountPart.None;
            for (int i = 0; i < n; i++)
            {
                var w = req.Commands[i];
                AccountCommand cmd;
                try
                {
                    if (w == null || !AccountCommand.IsCommand(w.K)) throw new ArgumentException("unknown command '" + w?.K + "'");
                    cmd = AccountCommand.Parse(w.K, new JsonArgs(w.A));
                }
                catch (ArgumentException e) { return CommitResult.Reject(Rejection.Refused, i, e.Message); }
                if (cmd.Kind == CommandKind.FinishRun)
                {
                    if (finished) return CommitResult.Reject(Rejection.Refused, i, "a commit carries at most one finishRun");
                    finished = true;
                }
                if (cmd.Kind == CommandKind.RunEndless && ++climbs > _cfg.CommitMaxClimbs)
                    return CommitResult.Reject(Rejection.Refused, i, "a commit carries at most " + _cfg.CommitMaxClimbs + " Endless Tower climbs");
                if (cmd.Kind == CommandKind.DebugGrantBook)
                {
                    if (!_cfg.DebugPlayers.Contains(playerId)) return CommitResult.Reject(Rejection.Refused, i, "debug commands are not enabled for this player");
                    debug = true;
                }
                touched |= AccountCommands.Reads(cmd.Kind) | AccountCommands.Writes(cmd.Kind);
                commands.Add(cmd);
            }
            // Content moved since the last commit: load and rewrite every part, so what loading under the new content changes
            // (a run that no longer loads, an achievement added) is kept, exactly as the client's load did it (section 7).
            if (head.Content != _c.Version) touched = AllParts;
            if ((touched & (AccountPart.Vault | AccountPart.Entries)) != 0) touched |= AccountPart.Vault | AccountPart.Entries;   // the index pins the entries

            var (account, _) = await TryLoadAsync(playerId, head, touched, stored.Json).ConfigureAwait(false);
            if (account == null) return CommitResult.Reject(Rejection.Stale, -1, "another commit landed first");
            for (int i = 0; i < n; i++)
            {
                try
                {
                    CheckInputs(commands[i], now);
                    AccountCommands.Apply(_c, account, commands[i]);
                }
                catch (Exception e)   // the commands are the client's: any failure is a refusal, never a server error
                {
                    return CommitResult.Reject(Rejection.Refused, i, AccountCommand.NameOf(commands[i].Kind) + ": " + e.Message);
                }
            }

            var after = AccountParts.Split(account);
            var hashes = new SortedDictionary<string, string>(head.Hashes, StringComparer.Ordinal);
            var changed = new List<string>();
            foreach (var name in PartNames)
            {
                if ((AccountParts.PartOf(name) & touched) == 0) continue;
                string h = AccountParts.Hash(after[name]);
                if (h == head.Hashes[name]) continue;
                hashes[name] = h;
                changed.Add(name);
            }
            string hash = AccountParts.AccountHashOf(hashes);
            if (hash != req.ToHash)
                return new CommitResult { Code = Rejection.Mismatch, Reason = "the result differs from the client's", Hash = hash };

            // Copy-on-write: new versions under new keys, then the head swap, then delete what the new head no longer names.
            int seq = head.Seq + 1;
            string tag = Tag();
            var next = new AccountHead
            {
                Seq = seq,
                Hash = hash,
                PrevHash = head.Hash,
                Parts = new SortedDictionary<string, string>(head.Parts, StringComparer.Ordinal),
                Hashes = hashes,
                Entries = new SortedDictionary<int, string>(head.Entries),
                Holder = head.Holder,
                Schema = AccountState.CurrentSchemaVersion,
                Content = _c.Version,
                DebugUsed = head.DebugUsed || debug,
            };
            var write = new Dictionary<string, string>(StringComparer.Ordinal);
            var superseded = new List<string>();
            foreach (var name in changed)
            {
                string key = AccountHead.ItemKey(name, seq, tag);
                write[key] = after[name];
                superseded.Add(head.Parts[name]);
                next.Parts[name] = key;
            }
            if ((touched & AccountPart.Entries) != 0)
            {
                var ids = new HashSet<int>();
                foreach (var kv in after)
                {
                    if (!kv.Key.StartsWith(AccountParts.EntryPrefix, StringComparison.Ordinal)) continue;
                    int id = int.Parse(kv.Key.Substring(AccountParts.EntryPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture);
                    ids.Add(id);
                    if (head.Entries.ContainsKey(id)) continue;   // an entry never changes once written
                    string key = AccountHead.ItemKey(AccountHead.EntryName(id), seq, tag);
                    write[key] = kv.Value;
                    next.Entries[id] = key;
                }
                foreach (var kv in head.Entries)
                    if (!ids.Contains(kv.Key)) { superseded.Add(kv.Value); next.Entries.Remove(kv.Key); }
            }

            if (write.Count > 0) await _store.WriteAsync(playerId, write).ConfigureAwait(false);
            if (!await _store.WriteHeadAsync(playerId, next.ToJson(), stored.WriteLock).ConfigureAwait(false))
            {
                await TryDeleteAsync(playerId, write.Keys.ToList()).ConfigureAwait(false);
                return CommitResult.Reject(Rejection.Stale, -1, "another commit landed first");
            }
            await TryDeleteAsync(playerId, superseded).ConfigureAwait(false);
            return new CommitResult { Seq = seq, Hash = hash };
        }

        /// <summary>The inputs a client supplies besides its choices (section 6.2). Throws InvalidOperationException to refuse.</summary>
        void CheckInputs(AccountCommand cmd, long now)
        {
            switch (cmd.Kind)
            {
                case CommandKind.FinishRun:
                    if (cmd.Time > now + _cfg.ClockSlackSec * 1000L) throw new InvalidOperationException("the seal time is after the server's time");
                    break;
                case CommandKind.RunTower:
                    int today = DayOf(now);
                    if (cmd.A != today && cmd.A != today - 1) throw new InvalidOperationException("day " + cmd.A + " is not the server's day (" + today + ") or the day before");
                    break;
                case CommandKind.RunEndless:
                    // The season from server time (#21), with the same one-day slack as the Dungeon: a climb sent just before
                    // Monday 00:00 UTC may land just after it.
                    int season = EndlessSim.SeasonOf(DayOf(now)), lastSeason = EndlessSim.SeasonOf(Math.Max(0, DayOf(now) - 1));
                    if (cmd.A != season && cmd.A != lastSeason) throw new InvalidOperationException("season " + cmd.A + " is not the server's season (" + season + ")");
                    break;
            }
        }

        // ------------------------------------------------------------------ storage

        /// <summary>
        /// The account's items by part name (and AccountParts.EntryKey for entries), each checked against the head's hash. Parts
        /// outside <paramref name="parts"/> are stood in for by a new account's; entries are loaded with the vault part.
        /// </summary>
        async Task<Dictionary<string, string>> LoadAsync(string playerId, AccountHead head, AccountPart parts)
        {
            var keys = new List<string>();
            foreach (var name in PartNames)
                if ((AccountParts.PartOf(name) & parts) != 0)
                    keys.Add(head.Parts.TryGetValue(name, out var k) ? k : throw new AccountDamagedException("the head names no " + name + " part"));
            bool entries = (parts & AccountPart.Vault) != 0;
            if (entries) keys.AddRange(head.Entries.Values);
            var found = await _store.ReadAsync(playerId, keys).ConfigureAwait(false);

            var items = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in PartNames)
            {
                if ((AccountParts.PartOf(name) & parts) == 0) { items[name] = _standIn[name]; continue; }
                if (!found.TryGetValue(head.Parts[name], out var text)) throw new AccountDamagedException("the " + name + " part is missing (" + head.Parts[name] + ")");
                if (!head.Hashes.TryGetValue(name, out var h) || AccountParts.Hash(text) != h) throw new AccountDamagedException("the " + name + " part does not match its hash");
                items[name] = text;
            }
            if (entries)
                foreach (var kv in head.Entries)
                    items[AccountParts.EntryKey(kv.Key)] = found.TryGetValue(kv.Value, out var text) ? text
                        : throw new AccountDamagedException("Vault entry " + kv.Key + " is missing (" + kv.Value + ")");
            return items;
        }

        AccountState Join(IReadOnlyDictionary<string, string> items, out string notice)
        {
            try { return AccountParts.Join(_c, items, out notice); }
            catch (SaveInvalidException e) { throw new AccountDamagedException("the stored account does not load: " + e.Message, e); }
        }

        /// <summary>
        /// Delete items left by commits that never landed (a crash between the item writes and the head swap, or a lost race):
        /// every key in item-key form from this commit or earlier that the head does not name. Keys from later commits belong to
        /// one in flight and are left alone.
        /// </summary>
        async Task SweepAsync(string playerId, AccountHead head)
        {
            var live = new HashSet<string>(head.Parts.Values.Concat(head.Entries.Values), StringComparer.Ordinal);
            var keys = await _store.ListKeysAsync(playerId).ConfigureAwait(false);
            var dead = new List<string>();
            foreach (var k in keys)
            {
                int seq = AccountHead.SeqOf(k);
                if (seq >= 0 && seq <= head.Seq && !live.Contains(k)) dead.Add(k);
            }
            await TryDeleteAsync(playerId, dead).ConfigureAwait(false);
        }

        /// <summary>
        /// <see cref="LoadAsync"/> then Join. A null account when an item is missing or damaged because the head moved on since
        /// it was read (a commit that landed meanwhile deletes the items it superseded); AccountDamagedException when the head
        /// whose JSON is <paramref name="basis"/> is still current.
        /// </summary>
        async Task<(AccountState account, string notice)> TryLoadAsync(string playerId, AccountHead head, AccountPart parts, string basis)
        {
            try
            {
                var account = Join(await LoadAsync(playerId, head, parts).ConfigureAwait(false), out var notice);
                return (account, notice);
            }
            catch (AccountDamagedException)
            {
                var current = await _store.ReadHeadAsync(playerId).ConfigureAwait(false);
                if (current != null && current.Json == basis) throw;
                return (null, null);
            }
        }

        /// <summary>Best effort: what is left is swept by the next open.</summary>
        async Task TryDeleteAsync(string playerId, List<string> keys)
        {
            if (keys.Count == 0) return;
            try { await _store.DeleteAsync(playerId, keys).ConfigureAwait(false); }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>The Dungeon day of a Unix time in milliseconds: the UTC day number (specs/21 section 1).</summary>
        public static int DayOf(long unixMs) => (int)Math.Floor(unixMs / (double)DayMs);

        string Tag() => (_cfg.Random() & 0xFFFFFFFFUL).ToString("x8", CultureInfo.InvariantCulture);

        bool ValidDevice(string device) => !string.IsNullOrEmpty(device) && device.Length <= _cfg.MaxDeviceIdLength;

        static void CheckPlayer(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) throw new ArgumentException("a player id is required", nameof(playerId));
        }
    }
}
