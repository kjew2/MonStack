using System.Collections.Generic;
using System.Linq;
using IdleRng.Runtime.Save;
using IdleRng.Sim.Account;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Server.Tests
{
    public class FortunaServerTests
    {
        static Sim.Content.ContentDb Db => Content.Db;

        // Install an established account directly in the fixture store, retaining the normal head/part hashes.
        static Device Established(Rig rig)
        {
            var d = rig.Device("a"); d.Open();
            var a = d.Acct;
            a.Shards = 10000; a.RunsStarted = 2;
            a.Vault.Add(new VaultEntry { Id = 0, SpeciesId = "slime", Trees = { new VaultTree { TreeId = "fortune", Bought = 2 } } });
            a.Vault.Add(new VaultEntry { Id = 1, SpeciesId = "slime" });
            a.Fortuna.Rank = 4; a.Fortuna.Points = 179;
            var items = AccountParts.Split(a);
            var h = rig.Store.Head(Rig.Player);
            var write = new Dictionary<string, string>();
            foreach (var kv in items)
            {
                if (kv.Key.StartsWith(AccountParts.EntryPrefix))
                {
                    int id = int.Parse(kv.Key.Substring(AccountParts.EntryPrefix.Length));
                    string key = AccountHead.ItemKey(AccountHead.EntryName(id), 0, "fixture");
                    h.Entries[id] = key; write[key] = kv.Value;
                }
                else
                {
                    write[h.Parts[kv.Key]] = kv.Value;
                    h.Hashes[kv.Key] = AccountParts.Hash(kv.Value);
                }
            }
            h.Hash = AccountParts.AccountHashOf(h.Hashes);
            rig.Store.Inner.WriteAsync(Rig.Player, write).Wait();
            var stored = rig.Store.Inner.ReadHeadAsync(Rig.Player).Result;
            Assert.That(rig.Store.Inner.WriteHeadAsync(Rig.Player, h.ToJson(), stored.WriteLock).Result, Is.True);
            d.Open(); return d;
        }

        [Test]
        public void BreedCheckpoint_ReplaysSameBirthAndChargesOnce_ParentsStayUnchanged()
        {
            var rig = new Rig(); var d = Established(rig);
            var parents = d.Acct.Vault.Select(e => AccountParts.Split(d.Acct)[AccountParts.EntryKey(e.Id)]).ToArray();
            d.Do("breed", 0, 1);
            var req = d.Request();
            Assert.That(d.Commit(req).Accepted, Is.True);
            var again = d.Commit(req);
            Assert.That(again.Accepted, Is.True, "retry of committed request is idempotent");
            var reloaded = rig.Device("a"); reloaded.Open();
            Assert.That(reloaded.Acct.Shards, Is.EqualTo(10000 - FortunaBreeding.Cost(Db)));
            Assert.That(reloaded.Acct.Active.Lineage.Rank, Is.EqualTo(4));
            Assert.That(TrainingRun.StateHash(reloaded.Acct.Active), Is.EqualTo(TrainingRun.StateHash(d.Acct.Active)));
            Assert.That(reloaded.Acct.Vault.Count, Is.EqualTo(2));
            Assert.That(reloaded.Acct.Vault.Select(e => AccountParts.Split(reloaded.Acct)[AccountParts.EntryKey(e.Id)]), Is.EqualTo(parents));
        }

        [Test]
        public void SealCheckpoint_AwardsNodesAndBreakthroughOnce_EvenWhenCommitIsRetried()
        {
            var rig = new Rig(); var d = Established(rig);
            d.Do("breed", 0, 1); Assert.That(d.Commit().Accepted, Is.True);
            // Spend only what the run actually has, ensuring lineage progress comes through legal commands.
            d.Do("spend", 2);
            Assert.That(d.Commit().Accepted, Is.True);
            var opened = rig.Device("a"); opened.Open();
            Assert.That(opened.Acct.Active.Lineage.Deepest, Is.EqualTo(1));
            d = opened;
            for (int guard = 0; d.Acct.Active.Phase != RunPhase.Finished && guard < 300; guard++)
            {
                var s = d.Acct.Active;
                while (s.Offers.Count > 0) d.Do("discard");
                if (s.Phase == RunPhase.AwaitFight) d.Do("fight"); else d.Do("rest");
                if (d.Pending.Count >= 20) Assert.That(d.Commit().Accepted, Is.True);
            }
            Assert.That(d.Acct.Active.Phase, Is.EqualTo(RunPhase.Finished));
            d.Do("finishRun", rig.Now);
            var req = d.Request();
            Assert.That(d.Commit(req).Accepted, Is.True);
            var expected = AccountParts.Split(d.Acct)[AccountParts.Progress];
            Assert.That(d.Acct.Fortuna.LastAward, Is.EqualTo(1));
            Assert.That(d.Acct.Fortuna.Attempts, Is.EqualTo(1));
            Assert.That(d.Commit(req).Accepted, Is.True);
            var again = rig.Device("a"); again.Open();
            Assert.That(again.Acct.Active, Is.Null);
            Assert.That(again.Acct.Vault.Count, Is.EqualTo(3));
            Assert.That(AccountParts.Split(again.Acct)[AccountParts.Progress], Is.EqualTo(expected));
            Assert.That(again.Hash(), Is.EqualTo(d.Hash()));
        }
    }
}
