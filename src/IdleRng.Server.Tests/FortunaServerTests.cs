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
        static Device Established(Rig rig, bool legacy = false)
        {
            var d = rig.Device("a"); d.Open(); var a = d.Acct; a.Shards = 10000; a.RunsStarted = 2;
            a.Vault.Add(new VaultEntry { Id = 0, SpeciesId = "slime", Trees = { new VaultTree { TreeId = "fortune", Bought = 2 } },
                Lineages = legacy ? null : new SortedDictionary<string, FortunaLineage> { ["fortune"] = new FortunaLineage { Rank = 4, Points = 179 } } });
            a.Vault.Add(new VaultEntry { Id = 1, SpeciesId = "slime", Trees = { new VaultTree { TreeId = "bulwark", Bought = 8 } } });
            a.Rulebooks[0] = ""; a.Rulebooks[1] = "";
            if (legacy) a.Lineages["fortune"] = new FortunaLineage { Rank = 4, Points = 179, Attempts = 2, Failures = 1 };
            var items = AccountParts.Split(a); var h = rig.Store.Head(Rig.Player); var write = new Dictionary<string, string>();
            if (legacy) h.Schema = 14;
            foreach (var kv in items)
            {
                if (kv.Key.StartsWith(AccountParts.EntryPrefix))
                {
                    int id = int.Parse(kv.Key.Substring(AccountParts.EntryPrefix.Length));
                    string key = AccountHead.ItemKey(AccountHead.EntryName(id), 0, "fixture"); h.Entries[id] = key; write[key] = kv.Value;
                }
                else { write[h.Parts[kv.Key]] = kv.Value; h.Hashes[kv.Key] = AccountParts.Hash(kv.Value); }
            }
            h.Hash = AccountParts.AccountHashOf(h.Hashes); rig.Store.Inner.WriteAsync(Rig.Player, write).Wait();
            var stored = rig.Store.Inner.ReadHeadAsync(Rig.Player).Result;
            Assert.That(rig.Store.Inner.WriteHeadAsync(Rig.Player, h.ToJson(), stored.WriteLock).Result, Is.True);
            d.Open(); return d;
        }

        [Test]
        public void BreedCheckpoint_ConsumesParentsAndRulebooksOnce_AndReplaysBothInheritances()
        {
            var rig = new Rig(); var d = Established(rig);
            d.Do("breed", 0, 1, null, "fortune", "bulwark"); var req = d.Request();
            Assert.That(d.Commit(req).Accepted, Is.True); Assert.That(d.Commit(req).Accepted, Is.True);
            var reloaded = rig.Device("a"); reloaded.Open();
            Assert.That(reloaded.Acct.Shards, Is.EqualTo(10000 - TreeLineages.Cost(Db)));
            Assert.That(reloaded.Acct.Active.Lineage.Rank, Is.EqualTo(4));
            Assert.That(reloaded.Acct.Active.Lineage.SupportingTreeId, Is.EqualTo("bulwark"));
            Assert.That(reloaded.Acct.Active.Slots[3].TreeId, Is.EqualTo("bulwark"));
            Assert.That(TrainingRun.StateHash(reloaded.Acct.Active), Is.EqualTo(TrainingRun.StateHash(d.Acct.Active)));
            Assert.That(reloaded.Acct.Vault, Is.Empty); Assert.That(reloaded.Acct.Rulebooks, Is.Empty);
            Assert.That(rig.Store.Head(Rig.Player).Entries, Is.Empty);
        }

        [Test]
        public void SealCheckpoint_AwardsPersonalProgressOnce_EvenWhenCommitIsRetried()
        {
            var rig = new Rig(); var d = Established(rig);
            d.Do("breed", 0, 1, null, "fortune", "bulwark"); Assert.That(d.Commit().Accepted, Is.True);
            d.Do("spend", 2); Assert.That(d.Commit().Accepted, Is.True);
            var opened = rig.Device("a"); opened.Open(); Assert.That(opened.Acct.Active.Lineage.Deepest, Is.EqualTo(1)); d = opened;
            for (int guard = 0; d.Acct.Active.Phase != RunPhase.Finished && guard < 300; guard++)
            {
                var s = d.Acct.Active;
                while (s.Offers.Count > 0) d.Do("discard");
                if (s.Phase == RunPhase.AwaitFight) d.Do("fight"); else d.Do("rest");
                if (d.Pending.Count >= 20) Assert.That(d.Commit().Accepted, Is.True);
            }
            Assert.That(d.Acct.Active.Phase, Is.EqualTo(RunPhase.Finished)); d.Do("finishRun", rig.Now); var req = d.Request();
            Assert.That(d.Commit(req).Accepted, Is.True);
            var entry = d.Acct.Vault.Single(); var p = entry.Lineages["fortune"];
            Assert.That(p.LastAward, Is.EqualTo(1)); Assert.That(p.Attempts, Is.EqualTo(1)); Assert.That(d.Acct.Lineages, Is.Empty);
            var expected = AccountParts.Split(d.Acct)[AccountParts.EntryKey(entry.Id)];
            Assert.That(d.Commit(req).Accepted, Is.True);
            var again = rig.Device("a"); again.Open(); Assert.That(again.Acct.Active, Is.Null); Assert.That(again.Acct.Vault.Count, Is.EqualTo(1));
            Assert.That(AccountParts.Split(again.Acct)[AccountParts.EntryKey(entry.Id)], Is.EqualTo(expected)); Assert.That(again.Hash(), Is.EqualTo(d.Hash()));
        }

        [Test]
        public void SchemaMigration_RewritesOldEntryBlobAlongWithItsNewPersonalLedgerHash()
        {
            var rig = new Rig(); var d = Established(rig, legacy: true);
            var before = rig.Store.Head(Rig.Player).Entries[0];
            Assert.That(d.Acct.Vault[0].Lineages["fortune"].Points, Is.EqualTo(179));
            d.Do("setName", "Synthesist"); Assert.That(d.Commit().Accepted, Is.True);
            var h = rig.Store.Head(Rig.Player); Assert.That(h.Schema, Is.EqualTo(AccountState.CurrentSchemaVersion));
            Assert.That(h.Entries[0], Is.Not.EqualTo(before), "migration changes old entry JSON and therefore needs a new pinned blob");
            var again = rig.Device("a"); again.Open();
            var p = again.Acct.Vault[0].Lineages["fortune"];
            Assert.That((p.Rank, p.Points, p.Attempts, p.Failures), Is.EqualTo((4, 179, 2, 1)));
            Assert.That(again.Acct.Lineages, Is.Empty); Assert.That(again.Hash(), Is.EqualTo(d.Hash()));
        }
    }
}
