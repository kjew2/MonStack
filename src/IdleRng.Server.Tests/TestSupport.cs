using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IdleRng.Bots.Training;
using IdleRng.Runtime.Save;
using IdleRng.Sim.Account;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Server.Tests
{
    internal static class Content
    {
        static readonly Lazy<ContentDb> _db = new Lazy<ContentDb>(() =>
            ContentDb.LoadFromDirectory(FileContentSource.FindContentDirectory(TestContext.CurrentContext.TestDirectory)
                ?? throw new InvalidOperationException("Content/manifest.json not found above the test directory")));

        public static ContentDb Db => _db.Value;
    }

    /// <summary>A store that counts calls, records the keys read and written, and can run a hook or fail before a head write.</summary>
    internal sealed class SpyStore : IAccountStore
    {
        public readonly MemoryAccountStore Inner = new MemoryAccountStore();
        public readonly List<string> Read = new List<string>();
        public readonly List<string> Written = new List<string>();
        public int HeadWrites;

        /// <summary>Runs once before the next head write (then clears itself).</summary>
        public Func<Task> BeforeHeadWrite;

        /// <summary>When set, the next head write throws (a crash between the item writes and the swap).</summary>
        public bool FailNextHeadWrite;

        public Task<StoredHead> ReadHeadAsync(string playerId) => Inner.ReadHeadAsync(playerId);

        public async Task<bool> WriteHeadAsync(string playerId, string json, string expectedLock)
        {
            var hook = BeforeHeadWrite;
            BeforeHeadWrite = null;
            if (hook != null) await hook();
            if (FailNextHeadWrite) { FailNextHeadWrite = false; throw new InvalidOperationException("crash"); }
            bool ok = await Inner.WriteHeadAsync(playerId, json, expectedLock);
            if (ok) HeadWrites++;
            return ok;
        }

        /// <summary>Runs once before the next item read (then clears itself).</summary>
        public Action BeforeRead;

        public Task<IReadOnlyDictionary<string, string>> ReadAsync(string playerId, IReadOnlyCollection<string> keys)
        {
            var hook = BeforeRead;
            BeforeRead = null;
            hook?.Invoke();
            Read.AddRange(keys);
            return Inner.ReadAsync(playerId, keys);
        }

        public Task WriteAsync(string playerId, IReadOnlyDictionary<string, string> items)
        {
            Written.AddRange(items.Keys);
            return Inner.WriteAsync(playerId, items);
        }

        public Task DeleteAsync(string playerId, IReadOnlyCollection<string> keys) => Inner.DeleteAsync(playerId, keys);
        public Task<IReadOnlyList<string>> ListKeysAsync(string playerId) => Inner.ListKeysAsync(playerId);

        public void ClearLog() { Read.Clear(); Written.Clear(); HeadWrites = 0; }

        public AccountHead Head(string playerId) => AccountHead.FromJson(Inner.ReadHeadAsync(playerId).Result.Json);

        public List<string> Keys(string playerId) => Inner.ListKeysAsync(playerId).Result.ToList();

        /// <summary>The keys the head names, plus the head: what the store holds when nothing is left over.</summary>
        public List<string> Referenced(string playerId)
        {
            var h = Head(playerId);
            return h.Parts.Values.Concat(h.Entries.Values).Append(MemoryAccountStore.HeadKey).OrderBy(k => k, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>A server with a fixed clock and a deterministic random source.</summary>
    internal sealed class Rig
    {
        public const string Player = "player-1";
        public static readonly long Start = 20391L * 86_400_000L + 12 * 3_600_000L;   // noon UTC on Dungeon day 20391

        public readonly SpyStore Store = new SpyStore();
        public readonly ServerConfig Config;
        public readonly AccountServer Server;
        public long Now = Start;
        ulong _rng = 12345;

        public Rig(params string[] debugPlayers)
        {
            Config = new ServerConfig
            {
                NowMs = () => Now,
                Random = () => Sim.Rng.SplitMix64.Derive(_rng++, 7),
                DebugPlayers = new HashSet<string>(debugPlayers, StringComparer.Ordinal),
            };
            Server = new AccountServer(Content.Db, Store, Config);
        }

        public Device Device(string id) => new Device(this, id);
    }

    /// <summary>
    /// A client as specs/27 section 5 describes it: plays on its own engine, keeps the commands since its base, and commits
    /// them with the hash of its account.
    /// </summary>
    internal sealed class Device
    {
        static ContentDb Db => Content.Db;

        readonly Rig _rig;
        public readonly string Id;
        public string Player = Rig.Player;
        public AccountState Acct;
        public int BaseSeq;
        public string BaseHash;
        public readonly List<WireCommand> Pending = new List<WireCommand>();

        public Device(Rig rig, string id) { _rig = rig; Id = id; }

        public OpenResult Open(bool claim = false)
        {
            var r = _rig.Server.OpenAsync(Player, new OpenRequest { DeviceId = Id, Claim = claim }).Result;
            if (r.Accepted)
            {
                Acct = AccountSave.Decode(Db, Encoding.UTF8.GetBytes(r.Account));
                BaseSeq = r.Seq;
                BaseHash = r.Hash;
                Pending.Clear();
            }
            return r;
        }

        /// <summary>Apply a command locally (as the browser does) and queue it for the next commit.</summary>
        public object Do(string k, params object[] args)
        {
            var w = new WireCommand(k, args);
            var res = AccountCommands.Apply(Db, Acct, AccountCommand.Parse(k, new JsonArgs(w.A)));
            Pending.Add(w);
            return res;
        }

        /// <summary>Queue a command without applying it locally (a forged or refused one).</summary>
        public void Send(string k, params object[] args) => Pending.Add(new WireCommand(k, args));

        public string Hash() => AccountParts.AccountHash(AccountParts.Split(Acct));

        public CommitRequest Request() => new CommitRequest
        {
            DeviceId = Id, FromSeq = BaseSeq, FromHash = BaseHash, Commands = new List<WireCommand>(Pending), ToHash = Hash(), ContentVersion = Db.Version,
        };

        public CommitResult Commit() => Commit(Request());

        public CommitResult Commit(CommitRequest req)
        {
            var r = _rig.Server.CommitAsync(Player, req).Result;
            if (r.Accepted)
            {
                BaseSeq = r.Seq;
                BaseHash = r.Hash;
                Pending.Clear();
            }
            return r;
        }

        /// <summary>Play the run in progress to its end with a bot policy, committing every <paramref name="every"/> commands.</summary>
        public void PlayOut(string policy = "chaser", int every = 25)
        {
            var p = RunPolicies.Create(policy);
            for (int guard = 0; Acct.Active.Phase != RunPhase.Finished && guard < 1000; guard++)
            {
                var c = p.Next(Db, Acct.Active);
                switch (c.Kind)
                {
                    case RunCommandKind.Drill: Do("drill", c.Stat); break;
                    case RunCommandKind.Rest: Do("rest"); break;
                    case RunCommandKind.Fight: Do("fight"); break;
                    case RunCommandKind.TakeTree: Do("take", c.Choice, c.Slot); break;
                    case RunCommandKind.DiscardOffer: Do("discard"); break;
                    case RunCommandKind.RerollDraft: Do("reroll"); break;
                    case RunCommandKind.SpendPoint: Do("spend", c.Slot); break;
                    default: Do("book", c.Book); break;
                }
                if (Pending.Count >= every) Assert.That(Commit().Accepted, Is.True, "a mid-run checkpoint");
            }
        }

        /// <summary>Start, play and seal one run of the starter species, then close the vendor; committed at each checkpoint.</summary>
        public void SealOne(long sealMs)
        {
            Do("startRun", Db.StarterSpecies.Id);
            Assert.That(Commit().Accepted, Is.True, "startRun");
            PlayOut();
            Do("finishRun", sealMs);
            Do("closeVendor");
            Assert.That(Commit().Accepted, Is.True, "finishRun");
        }
    }
}
