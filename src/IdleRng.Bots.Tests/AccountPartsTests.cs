using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Bots.Training;
using IdleRng.Runtime.Save;
using IdleRng.Sim.Account;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Bots.Tests
{
    /// <summary>
    /// The account split into stored parts (specs/27 section 3) and the parts each command declares (AccountCommands.Reads and
    /// Writes): a command changes only the parts it writes, and its result does not depend on the parts it does not declare.
    /// </summary>
    public class AccountPartsTests
    {
        static Sim.Content.ContentDb Db => Content.Db;

        [Test]
        public void EveryAccountField_IsInAPart()
        {
            var listed = new HashSet<string>(AccountParts.Fields.Values.SelectMany(f => f).Concat(AccountParts.Unlisted));
            foreach (var p in typeof(AccountState).GetProperties())
                Assert.That(listed, Does.Contain(p.Name), "AccountState." + p.Name + " is in no part (AccountParts.Fields)");
            Assert.That(AccountParts.Fields.Values.SelectMany(f => f).Count(), Is.EqualTo(AccountParts.Fields.Values.SelectMany(f => f).Distinct().Count()), "a field is in one part");
        }

        [Test]
        public void SplitThenJoin_GivesTheSameAccount_AndTheSameHash()
        {
            var a = Busy(61);
            var items = AccountParts.Split(a);
            Assert.That(items.Keys, Is.SupersetOf(new[] { "training", "wallet", "collection", "progress", "dungeon", "vault" }));
            Assert.That(items.Keys.Count(k => k.StartsWith("mon.")), Is.EqualTo(a.Vault.Count));
            var b = AccountParts.Join(Db, items, out var notice);
            Assert.That(notice, Is.Null);
            Assert.That(TrainingRun.StateHash(b.Active), Is.EqualTo(TrainingRun.StateHash(a.Active)), "the run in progress is rebuilt");
            var again = AccountParts.Split(b);
            Assert.That(again, Is.EqualTo(items), "split(join(x)) == x, item by item");
            Assert.That(AccountParts.AccountHash(again), Is.EqualTo(AccountParts.AccountHash(items)));
        }

        [Test]
        public void Join_RefusesAMissingPart_OrAnEntryThatDoesNotMatchItsHash()
        {
            var items = AccountParts.Split(Busy(62));
            var noWallet = new Dictionary<string, string>(items);
            noWallet.Remove("wallet");
            Assert.Throws<SaveInvalidException>(() => AccountParts.Join(Db, noWallet, out _));
            var key = items.Keys.First(k => k.StartsWith("mon."));
            var edited = new Dictionary<string, string>(items) { [key] = items[key].Replace("\"Grade\":\"", "\"Grade\":\"s") };
            Assert.Throws<SaveInvalidException>(() => AccountParts.Join(Db, edited, out _), "an edited monster");
            var badId = new Dictionary<string, string>(items) { ["vault"] = items["vault"].Replace("\"Order\":[", "\"Order\":[\"x\",") };
            Assert.Throws<SaveInvalidException>(() => AccountParts.Join(Db, badId, out _), "a damaged vault index");
            var gone = new Dictionary<string, string>(items);
            gone.Remove(key);
            Assert.Throws<SaveInvalidException>(() => AccountParts.Join(Db, gone, out _), "a listed monster missing");
        }

        [Test]
        public void Locking_ChangesOnlyTheVaultIndex_AndTheAccountHash()
        {
            var a = Busy(63);
            var before = AccountParts.Split(a);
            AccountOps.SetLocked(Db, a, 0, true);
            var after = AccountParts.Split(a);
            Assert.That(Changed(before, after), Is.EqualTo(new[] { "vault" }), "the monster's own item does not change");
            Assert.That(AccountParts.AccountHash(after), Is.Not.EqualTo(AccountParts.AccountHash(before)));
        }

        [Test]
        public void EveryCommand_ChangesOnlyWhatItWrites_AndReadsOnlyWhatItDeclares()
        {
            var a = AccountOps.New(Db, 64);
            foreach (var sp in Db.SpeciesList) AccountOps.UnlockSpecies(Db, a, sp.Id);
            AccountOps.GrantBooks(Db, a, Db.SkillBooks.Keys.First(), 2);
            var decoy = Decoy();
            var seen = new HashSet<CommandKind>();
            void Run(AccountCommand cmd) { Check(a, cmd, decoy); seen.Add(cmd.Kind); }

            for (int round = 0; round < 2; round++)
            {
                var books = round == 0 ? new List<string> { Db.SkillBooks.Keys.First() } : null;
                Run(new AccountCommand { Kind = CommandKind.StartRun, Id = "drake", Ids = books });
                var policy = RunPolicies.Create("chaser");
                for (int guard = 0; a.Active.Phase != RunPhase.Finished && guard < 500; guard++)
                    Run(FromRun(policy.Next(Db, a.Active)));
                Run(new AccountCommand { Kind = CommandKind.FinishRun, Time = 5 });
                a.Shards += 5000;
                if (a.Vendor != null && a.Vendor.Offers.Count > 0)
                {
                    Run(new AccountCommand { Kind = CommandKind.BuyOffer, A = 0 });
                    if (AccountOps.CanRerollVendor(Db, a)) Run(new AccountCommand { Kind = CommandKind.RerollVendor });
                }
                Run(new AccountCommand { Kind = CommandKind.CloseVendor });
            }
            Run(new AccountCommand { Kind = CommandKind.SetLoadout, Ids = new List<string>() });
            Run(new AccountCommand { Kind = CommandKind.Lock, A = 0, Flag = true });
            Run(new AccountCommand { Kind = CommandKind.RunTower, A = 20391, Team = new List<int> { 0, 1 } });
            Run(new AccountCommand { Kind = CommandKind.Lock, A = 0, Flag = false });
            Run(new AccountCommand { Kind = CommandKind.Dismantle, A = 0 });
            Run(new AccountCommand { Kind = CommandKind.DebugGrantBook, Id = Db.SkillBooks.Keys.First(), A = 1 });
            Run(new AccountCommand { Kind = CommandKind.StartRun, Id = "slime" });
            Run(new AccountCommand { Kind = CommandKind.Abandon });

            var missing = Enum.GetValues(typeof(CommandKind)).Cast<CommandKind>().Where(k => !seen.Contains(k)
                && k != CommandKind.Reroll && k != CommandKind.Book && k != CommandKind.Discard).ToList();
            Assert.That(missing, Is.Empty, "every command was checked (draft rerolls, discards and books when a policy uses them)");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Apply <paramref name="cmd"/> to <paramref name="a"/> (it moves on), checking it against a copy whose undeclared parts
        /// come from <paramref name="decoy"/>: the written parts must come out identical, and nothing else may change.
        /// </summary>
        static void Check(AccountState a, AccountCommand cmd, IReadOnlyDictionary<string, string> decoy)
        {
            var before = AccountParts.Split(a);
            var keep = AccountCommands.Writes(cmd.Kind) | AccountCommands.Reads(cmd.Kind);
            if ((keep & (AccountPart.Vault | AccountPart.Entries)) != 0) keep |= AccountPart.Vault | AccountPart.Entries;   // stored together
            var mixed = new Dictionary<string, string>();
            foreach (var kv in before) if ((AccountParts.PartOf(kv.Key) & keep) != 0) mixed[kv.Key] = kv.Value;
            foreach (var kv in decoy) if ((AccountParts.PartOf(kv.Key) & keep) == 0) mixed[kv.Key] = kv.Value;
            var copy = AccountParts.Join(Db, mixed, out _);

            AccountCommands.Apply(Db, a, cmd);
            AccountCommands.Apply(Db, copy, cmd);
            var after = AccountParts.Split(a);
            var copyAfter = AccountParts.Split(copy);
            string what = AccountCommand.NameOf(cmd.Kind);

            var writes = AccountCommands.Writes(cmd.Kind);
            foreach (var k in Changed(before, after))
                Assert.That(writes & AccountParts.PartOf(k), Is.Not.EqualTo(AccountPart.None), what + " changed " + k + " without declaring it");
            foreach (var k in after.Keys.Union(copyAfter.Keys))
                if ((AccountParts.PartOf(k) & writes) != 0)
                    Assert.That(copyAfter.TryGetValue(k, out var v) ? v : null, Is.EqualTo(after.TryGetValue(k, out var w) ? w : null),
                        what + " wrote " + k + " differently when its undeclared parts changed: it reads something it does not declare");
        }

        static IEnumerable<string> Changed(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after) =>
            before.Keys.Union(after.Keys).Where(k => !(before.TryGetValue(k, out var x) && after.TryGetValue(k, out var y) && x == y)).OrderBy(k => k, StringComparer.Ordinal);

        /// <summary>Another account's parts: different seed, wallet, Dungeon progress and Vault, a fresh collection and progress.</summary>
        static IReadOnlyDictionary<string, string> Decoy()
        {
            var d = AccountOps.New(Db, 999);
            var run = AccountOps.StartRun(Db, d, d.OwnedSpecies[0]);
            PlayOut(run);
            AccountOps.FinishRun(Db, d, 1);
            AccountOps.CloseVendor(Db, d);
            d.Records = new AccountRecords();   // nothing earned, so loading the decoy unlocks nothing
            d.Achievements.Clear();
            d.Shards = 777;
            AccountOps.GrantBooks(Db, d, Db.SkillBooks.Keys.Last(), 3);
            d.Tower.Tickets = 2;
            d.TalentsOff.Add(Db.TalentList[0].Id);
            return AccountParts.Split(d);
        }

        /// <summary>An account with a sealed monster, talents, books and a run in progress.</summary>
        static AccountState Busy(ulong seed)
        {
            var a = AccountOps.New(Db, seed);
            foreach (var sp in Db.SpeciesList) AccountOps.UnlockSpecies(Db, a, sp.Id);
            a.Talents[Db.TalentList[0].Id] = 1;
            PlayOut(AccountOps.StartRun(Db, a, "drake"));
            AccountOps.FinishRun(Db, a, 1000);
            AccountOps.GrantBooks(Db, a, Db.SkillBooks.Keys.First(), 1);
            var run = AccountOps.StartRun(Db, a, "sprite", new[] { Db.SkillBooks.Keys.First() });
            TrainingRun.Drill(Db, run, Sim.Combat.StatIds.Int);
            return a;
        }

        static void PlayOut(RunState run)
        {
            while (run.Phase != RunPhase.Finished)
            {
                while (run.Offers.Count > 0) TrainingRun.DiscardOffer(Db, run);
                if (run.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, run); else TrainingRun.Rest(Db, run);
            }
        }

        static AccountCommand FromRun(RunCommand c)
        {
            switch (c.Kind)
            {
                case RunCommandKind.Drill: return new AccountCommand { Kind = CommandKind.Drill, Id = c.Stat };
                case RunCommandKind.Rest: return new AccountCommand { Kind = CommandKind.Rest };
                case RunCommandKind.Fight: return new AccountCommand { Kind = CommandKind.Fight };
                case RunCommandKind.TakeTree: return new AccountCommand { Kind = CommandKind.Take, A = c.Choice, B = c.Slot };
                case RunCommandKind.DiscardOffer: return new AccountCommand { Kind = CommandKind.Discard };
                case RunCommandKind.RerollDraft: return new AccountCommand { Kind = CommandKind.Reroll };
                case RunCommandKind.SpendPoint: return new AccountCommand { Kind = CommandKind.Spend, A = c.Slot };
                default: return new AccountCommand { Kind = CommandKind.Book, Id = c.Book };
            }
        }
    }
}
