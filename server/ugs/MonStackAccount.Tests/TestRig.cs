using System;
using System.Collections.Generic;
using System.Text;
using IdleRng.Bots.Training;
using IdleRng.Runtime.Save;
using IdleRng.Server;
using IdleRng.Sim.Account;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace MonStackAccount.Tests
{
    /// <summary>The module over a fake Cloud Save, with a fixed clock and a deterministic random source.</summary>
    internal sealed class Rig
    {
        public static readonly long Start = 20391L * 86_400_000L + 12 * 3_600_000L;   // noon UTC on Dungeon day 20391

        public readonly FakeCloudSave Save = new FakeCloudSave();
        public readonly ModuleSettings Settings;
        public readonly AccountModule Module;
        public long Now = Start;
        ulong _rng = 4242;

        public Rig(params string[] debugPlayers)
        {
            Settings = new ModuleSettings
            {
                DebugPlayers = new HashSet<string>(debugPlayers, StringComparer.Ordinal),
                NowMs = () => Now,
                Random = () => IdleRng.Sim.Rng.SplitMix64.Derive(_rng++, 11),
            };
            Module = new AccountModule(_ => Save, Settings);
        }

        public static ContentDb Db => ModuleContent.Db;

        public Device Device(string player, string device) => new Device(this, player, device);
    }

    /// <summary>A browser as specs/27 section 5 describes it, calling the module's endpoints as a signed-in player.</summary>
    internal sealed class Device
    {
        readonly Rig _rig;
        public readonly string Player;
        public readonly string Id;
        public AccountState Acct;
        public int BaseSeq;
        public string BaseHash;
        public readonly List<WireCommand> Pending = new List<WireCommand>();

        public Device(Rig rig, string player, string id) { _rig = rig; Player = player; Id = id; }

        public FakeContext Context => new FakeContext(Player);

        public OpenReply Open(bool claim = false)
        {
            var r = _rig.Module.Open(Context, new OpenRequest { DeviceId = Id, Claim = claim }).GetAwaiter().GetResult();
            if (r.Accepted)
            {
                Acct = AccountSave.Decode(Rig.Db, Encoding.UTF8.GetBytes(Gzip.Unpack(r.Account)));
                BaseSeq = r.Seq;
                BaseHash = r.Hash;
                Pending.Clear();
            }
            return r;
        }

        public object Do(string k, params object[] args)
        {
            var w = new WireCommand(k, args);
            var res = AccountCommands.Apply(Rig.Db, Acct, AccountCommand.Parse(k, new JsonArgs(w.A)));
            Pending.Add(w);
            return res;
        }

        public void Send(string k, params object[] args) => Pending.Add(new WireCommand(k, args));

        public string Hash() => AccountParts.AccountHash(AccountParts.Split(Acct));

        public CommitRequest Request() => new CommitRequest
        {
            DeviceId = Id, FromSeq = BaseSeq, FromHash = BaseHash, Commands = new List<WireCommand>(Pending), ToHash = Hash(), ContentVersion = Rig.Db.Version,
        };

        public CommitReply Commit() => Commit(Request());

        public CommitReply Commit(CommitRequest req)
        {
            var r = _rig.Module.Commit(Context, req).GetAwaiter().GetResult();
            if (r.Accepted)
            {
                BaseSeq = r.Seq;
                BaseHash = r.Hash;
                Pending.Clear();
            }
            return r;
        }

        /// <summary>Start, play out with a bot policy (committing every 25 commands) and seal one starter run.</summary>
        public void SealOne(long sealMs, string policy = "chaser")
        {
            Do("startRun", Rig.Db.StarterSpecies.Id);
            Assert.That(Commit().Accepted, Is.True, "startRun");
            var p = RunPolicies.Create(policy);
            for (int guard = 0; Acct.Active.Phase != RunPhase.Finished && guard < 1000; guard++)
            {
                var c = p.Next(Rig.Db, Acct.Active);
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
                if (Pending.Count >= 25) Assert.That(Commit().Accepted, Is.True, "a mid-run checkpoint");
            }
            Do("finishRun", sealMs);
            Do("closeVendor");
            var r = Commit();
            Assert.That(r.Accepted, Is.True, "finishRun: " + r.Reason);
        }
    }
}
