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
    }
}
