using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Account;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>The shared command dispatcher (specs/27 section 4): parsing by bridge name, applying, refusing.</summary>
    public class AccountCommandsTests
    {
        static Content.ContentDb Db => TestContent.Db;

        /// <summary>Positional arguments as plain objects (string, double, bool, string[] or int[]; null = absent).</summary>
        internal sealed class ObjArgs : ICommandArgs
        {
            readonly object[] _a;
            public ObjArgs(params object[] a) { _a = a; }
            public bool Has(int i) => i < _a.Length && _a[i] != null;
            object At(int i) => Has(i) ? _a[i] : throw new ArgumentException("argument " + i + " missing");
            public string Str(int i) => At(i) as string ?? throw new ArgumentException("argument " + i + " must be a string");
            public double Num(int i) => At(i) is double d ? d : At(i) is int n ? n : throw new ArgumentException("argument " + i + " must be a number");
            public bool Bool(int i) => At(i) is bool b ? b : throw new ArgumentException("argument " + i + " must be true or false");
            public List<string> StrList(int i) => At(i) is string[] s ? s.ToList() : throw new ArgumentException("argument " + i + " must be a list");
            public List<int> IntList(int i) => At(i) is int[] v ? v.ToList() : throw new ArgumentException("argument " + i + " must be a list");
        }

        static object Do(AccountState a, string name, params object[] args) =>
            AccountCommands.Apply(Db, a, AccountCommand.Parse(name, new ObjArgs(args)));

        [Test]
        public void Parse_MapsEveryBridgeName_AndItsArguments()
        {
            var start = AccountCommand.Parse("startRun", new ObjArgs("drake", new[] { "b1" }, new[] { "t1" }));
            Assert.That((start.Kind, start.Id, start.Ids, start.TalentsOff), Is.EqualTo((CommandKind.StartRun, "drake", new List<string> { "b1" }, new List<string> { "t1" })));
            var bare = AccountCommand.Parse("startRun", new ObjArgs("slime"));
            Assert.That(bare.Ids, Is.Null, "no books");
            Assert.That(bare.TalentsOff, Is.Null, "the saved loadout");
            Assert.That(AccountCommand.Parse("finishRun", new ObjArgs(1234.0)).Time, Is.EqualTo(1234));
            var tower = AccountCommand.Parse("runTower", new ObjArgs(20391, new[] { 2, 0 }));
            Assert.That((tower.A, tower.Team), Is.EqualTo((20391, new List<int> { 2, 0 })));
            var lk = AccountCommand.Parse("lock", new ObjArgs(3, true));
            Assert.That((lk.A, lk.Flag), Is.EqualTo((3, true)));
            Assert.That(AccountCommand.Parse("setLoadout", new ObjArgs()).Ids, Is.Empty, "no list = everything on");
            Assert.That(AccountCommand.Parse("debugGrantBook", new ObjArgs("book_echoes")).A, Is.EqualTo(1), "one book by default");
            var take = AccountCommand.Parse("take", new ObjArgs(1, 2));
            Assert.That((take.A, take.B), Is.EqualTo((1, 2)));
            Assert.That(AccountCommand.Parse("drill", new ObjArgs(StatIds.Str)).Id, Is.EqualTo(StatIds.Str));

            foreach (CommandKind k in Enum.GetValues(typeof(CommandKind)))
                Assert.That(AccountCommand.IsCommand(AccountCommand.NameOf(k)), Is.True, k.ToString());
            Assert.That(AccountCommand.IsCommand("gauntlet"), Is.False, "reads are not commands");
            Assert.Throws<ArgumentException>(() => AccountCommand.Parse("nope", new ObjArgs()));
            Assert.Throws<ArgumentException>(() => AccountCommand.Parse("drill", new ObjArgs(3.0)), "a stat must be a string");
            Assert.Throws<ArgumentException>(() => AccountCommand.Parse("lock", new ObjArgs(0)), "the flag is required");
        }

        [Test]
        public void Apply_PlaysAWholeAccountLoop_LikeAccountOps()
        {
            // The same account through the dispatcher and through AccountOps directly ends in the same state.
            var viaCmd = AccountOps.New(Db, 51);
            var direct = AccountOps.New(Db, 51);
            var run = (RunState)Do(viaCmd, "startRun", viaCmd.OwnedSpecies[0]);
            var dRun = AccountOps.StartRun(Db, direct, direct.OwnedSpecies[0]);
            while (run.Phase != RunPhase.Finished)
            {
                while (run.Offers.Count > 0) { Do(viaCmd, "discard"); TrainingRun.DiscardOffer(Db, dRun); }
                if (run.Phase == RunPhase.AwaitFight) { Do(viaCmd, "fight"); TrainingRun.Fight(Db, dRun); }
                else if (run.Energy < 40) { Do(viaCmd, "rest"); TrainingRun.Rest(Db, dRun); }
                else { Do(viaCmd, "drill", StatIds.Str); TrainingRun.Drill(Db, dRun, StatIds.Str); }
            }
            Assert.That(TrainingRun.StateHash(run), Is.EqualTo(TrainingRun.StateHash(dRun)));
            var f = (FinishResult)Do(viaCmd, "finishRun", 99.0);
            var df = AccountOps.FinishRun(Db, direct, 99);
            Assert.That((f.Entry.Grade, f.Entry.GauntletFloor, f.Entry.Stats.Str), Is.EqualTo((df.Entry.Grade, df.Entry.GauntletFloor, df.Entry.Stats.Str)));
            Assert.That(f.Shards, Is.EqualTo(df.Shards));
            viaCmd.Shards = direct.Shards = 10_000;
            var bought = (OfferBought)Do(viaCmd, "buyOffer", 0);
            Assert.That(bought.Talent, Is.EqualTo(direct.Vendor.Offers[0].Talent));
            Assert.That(bought.Cost, Is.EqualTo(AccountOps.BuyOffer(Db, direct, 0)));
            Assert.That(Do(viaCmd, "rerollVendor"), Is.EqualTo(AccountOps.RerollVendor(Db, direct)));
            Assert.That(Do(viaCmd, "closeVendor"), Is.Null);
            Assert.That(viaCmd.Vendor, Is.Null);
            Do(viaCmd, "lock", 0, true);
            Assert.That(viaCmd.Vault[0].Locked, Is.True);
            Assert.Throws<InvalidOperationException>(() => Do(viaCmd, "dismantle", 0), "locked");
            Do(viaCmd, "lock", 0, false);
            Assert.That(Do(viaCmd, "dismantle", 0), Is.EqualTo(AccountOps.Dismantle(Db, direct, 0)));
            Assert.That(viaCmd.Shards, Is.EqualTo(direct.Shards));
            Assert.That(viaCmd.Talents, Is.EqualTo(direct.Talents));
        }

        [Test]
        public void Apply_RefusesAndChangesNothing()
        {
            var a = AccountOps.New(Db, 52);
            Assert.That(Assert.Throws<InvalidOperationException>(() => Do(a, "drill", StatIds.Str)).Message, Is.EqualTo("no run"));
            Assert.Throws<InvalidOperationException>(() => Do(a, "finishRun", 0.0), "no run in progress");
            Assert.Throws<ArgumentException>(() => Do(a, "runTower", 0, new[] { 0 }), "no Vault entry 0");
            Assert.Throws<InvalidOperationException>(() => Do(a, "startRun", "no_such_species"), "not owned");
            Assert.That(a.RunsStarted, Is.EqualTo(0));
            Assert.That(a.Active, Is.Null);
        }

        [Test]
        public void LegacyQueuedBreed_CannotBecomeAnUnreviewedSacrifice()
        {
            var a = AccountOps.New(Db, 53); a.Shards = 500;
            a.Vault.Add(new VaultEntry { Id = 0, SpeciesId = "slime", Trees = { new VaultTree { TreeId = "fortune", Bought = 2 } } });
            a.Vault.Add(new VaultEntry { Id = 1, SpeciesId = "slime", Trees = { new VaultTree { TreeId = "brute_force", Bought = 5 } } });
            var parents = a.Vault.ToArray();
            var oldCommand = AccountCommand.Parse("breed", new ObjArgs(0, 1, null, "fortune"));
            Assert.That(Assert.Throws<InvalidOperationException>(() => AccountCommands.Apply(Db, a, oldCommand)).Message,
                Does.Contain("Review synthesis"));
            Assert.That(a.Vault, Is.EqualTo(parents));
            Assert.That(a.Shards, Is.EqualTo(500));
            Assert.That(a.Active, Is.Null);
            Assert.That(a.RunsStarted, Is.Zero);
        }

        [Test]
        public void EveryCommand_DeclaresWhatItWrites()
        {
            foreach (CommandKind k in Enum.GetValues(typeof(CommandKind)))
            {
                var w = AccountCommands.Writes(k);
                Assert.That(w, Is.Not.EqualTo(AccountPart.None), k.ToString());
                if (AccountCommands.IsRunCommand(k)) Assert.That(w, Is.EqualTo(AccountPart.Training), k + ": a run command touches only the run");
                Assert.That(AccountCommands.Reads(k) & w, Is.EqualTo(AccountPart.None), k + ": Reads lists only what it does not write");
            }
        }
    }
}
