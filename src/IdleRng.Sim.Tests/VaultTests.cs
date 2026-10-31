using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>The Vault entry (specs/13 T4): a finished run frozen with its grade, build and replayable log.</summary>
    public class VaultTests
    {
        static Content.ContentDb Db => TestContent.Db;

        /// <summary>Plays rests, a drill on str, every fight, and spends skill points on slot 0 when it can.</summary>
        static RunState Play(string species, ulong seed)
        {
            var s = TrainingRun.Start(Db, species, seed);
            int guard = 0;
            while (s.Phase != RunPhase.Finished && guard++ < 5000)
            {
                while (s.Offers.Count > 0) TrainingRun.DiscardOffer(Db, s);
                if (s.Phase == RunPhase.AwaitFight) { TrainingRun.Fight(Db, s); continue; }
                if (s.Sp > 0 && s.Slots[0].Bought < Db.Trees[s.Slots[0].TreeId].Nodes.Count && Db.Trees[s.Slots[0].TreeId].Nodes[s.Slots[0].Bought].Cost <= s.Sp)
                    TrainingRun.SpendPoint(Db, s, 0);
                if (s.Energy < 40) TrainingRun.Rest(Db, s); else TrainingRun.Drill(Db, s, "str");
            }
            return s;
        }

        [Test]
        public void Seal_RefusesAnUnfinishedRun()
        {
            var s = TrainingRun.Start(Db, Db.SpeciesList[0].Id, 1);
            Assert.Throws<System.InvalidOperationException>(() => Vault.Seal(Db, s));
        }

        [Test]
        public void Seal_FreezesGradeStatsTreesAndRunInfo()
        {
            var s = Play(Db.SpeciesList[0].Id, 11);
            Assert.That(s.Phase, Is.EqualTo(RunPhase.Finished));
            var e = Vault.Seal(Db, s, 1234);

            Assert.That(e.SpeciesId, Is.EqualTo(s.SpeciesId));
            Assert.That(e.Seed, Is.EqualTo(s.Seed));
            Assert.That(e.Grade, Is.EqualTo(Db.FightRanks[s.Rank].Id));
            Assert.That(e.RankIndex, Is.EqualTo(s.Rank));
            Assert.That(e.End, Is.EqualTo(s.End));
            Assert.That(e.Cycles, Is.EqualTo(s.Cycle));
            Assert.That(e.FightsWon, Is.EqualTo(s.FightsWon));
            Assert.That(e.SealedUtcMs, Is.EqualTo(1234));
            foreach (var id in StatIds.Trained) Assert.That(e.Stats.Get(id), Is.EqualTo(s.Stats.Get(id)));
            Assert.That(e.Stats, Is.Not.SameAs(s.Stats), "the entry owns its stats");
            Assert.That(e.Trees.Select(t => (t.TreeId, t.Bought)), Is.EqualTo(s.Slots.Where(t => t != null).Select(t => (t.TreeId, t.Bought))));
            Assert.That(e.RunHash, Is.EqualTo(TrainingRun.StateHash(s)));
        }

        [Test]
        public void Entry_ReplaysToTheSameRun_AndRebuildsTheFighter()
        {
            var s = Play(Db.SpeciesList[1].Id, 99);
            var e = Vault.Seal(Db, s);
            var again = TrainingRun.Replay(Db, e.SpeciesId, e.Seed, e.Log, e.Books);
            Assert.That(TrainingRun.StateHash(again), Is.EqualTo(e.RunHash));

            var f = e.Fighter(Db);
            var direct = TrainingRun.MonsterFighter(Db, s);
            Assert.That(f.Element, Is.EqualTo(direct.Element));
            Assert.That(f.Skills.Select(k => k.Id), Is.EqualTo(direct.Skills.Select(k => k.Id)));
            foreach (var id in StatIds.Trained) Assert.That(f.Stats.Get(id), Is.EqualTo(direct.Stats.Get(id)));
        }

        [Test]
        public void Seal_KeepsTheLadder_AndAPrePoolEntryReplaysAgainstTheFlatRanks()
        {
            var s = Play(Db.SpeciesList[0].Id, 31);
            var e = Vault.Seal(Db, s);
            Assert.That(e.Ladder, Is.EqualTo(s.Ladder));
            Assert.That(e.Ladder, Is.Not.SameAs(s.Ladder));
            Assert.That(TrainingRun.StateHash(TrainingRun.Replay(Db, e.SpeciesId, e.Seed, e.Log, e.Books, e.Mods, e.Ladder)), Is.EqualTo(e.RunHash));

            // An entry sealed before the pools has no ladder (empty after loading): its run fought the flat fight_ranks rows.
            var legacy = TrainingRun.Start(Db, Db.SpeciesList[0].Id, 31, null, null, new System.Collections.Generic.List<string>());
            while (legacy.Phase != RunPhase.Finished)
            {
                while (legacy.Offers.Count > 0) TrainingRun.DiscardOffer(Db, legacy);
                if (legacy.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, legacy); else TrainingRun.Rest(Db, legacy);
            }
            var old = Vault.Seal(Db, legacy);
            Assert.That(old.Ladder, Is.Empty);
            var f = old.Fighter(Db);
            Assert.That(f.Stats.Get(StatIds.Hp), Is.EqualTo(legacy.Stats.Get(StatIds.Hp)));
            old.Ladder = null;   // a hand-edited save could carry null: still the flat ranks
            Assert.That(old.Fighter(Db).Stats.Get(StatIds.Hp), Is.EqualTo(legacy.Stats.Get(StatIds.Hp)));
        }

        [Test]
        public void Seal_KeepsBooksUsedAndUnused()
        {
            var book = Db.SkillBooks.Keys.First();
            var s = TrainingRun.Start(Db, Db.SpeciesList[0].Id, 5, new[] { book });
            TrainingRun.UseBook(Db, s, book);
            TrainingRun.DiscardOffer(Db, s);
            while (s.Phase != RunPhase.Finished)
            {
                while (s.Offers.Count > 0) TrainingRun.DiscardOffer(Db, s);
                if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s); else TrainingRun.Rest(Db, s);
            }
            var e = Vault.Seal(Db, s);
            Assert.That(e.Books, Is.EqualTo(new[] { book }));
            Assert.That(TrainingRun.StateHash(TrainingRun.Replay(Db, e.SpeciesId, e.Seed, e.Log, e.Books)), Is.EqualTo(e.RunHash));
        }
    
        [Test]
        public void Seal_RunsTheGauntlet_AndTheWheelCanReplayIt()
        {
            var db = TestContent.Db;
            var s = TrainingRun.Start(db, "drake", 6);
            while (s.Phase != RunPhase.Finished)
            {
                while (s.Offers.Count > 0) TrainingRun.DiscardOffer(db, s);
                if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(db, s);
                else TrainingRun.Rest(db, s);
            }
            var e = Vault.Seal(db, s);
            var g = IdleRng.Sim.Gauntlet.GauntletSim.Run(db, e.Fighter(db), e.Seed);
            Assert.That(e.GauntletHash, Is.EqualTo(g.Hash));
            Assert.That(e.GauntletFloor, Is.EqualTo(g.FloorReached));
            Assert.That(e.GauntletCleared, Is.EqualTo(g.Cleared));
            Assert.That(e.GauntletKiller, Is.EqualTo(g.KillerId));
            Assert.That(e.LegacyTitle, Is.EqualTo(IdleRng.Sim.Gauntlet.GauntletSim.Title(db, e.GauntletFloor).TitleKey));
            Assert.That(e.GauntletFloor, Is.GreaterThanOrEqualTo(1));
        }
}
}
