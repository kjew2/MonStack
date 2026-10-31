using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Bots.Training;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Bots.Tests
{
    /// <summary>Run bots (specs/14 section 9): every policy finishes every species, and its command log replays exactly.</summary>
    public class RunBotTests
    {
        [Test]
        public void EveryPolicy_FinishesEverySpecies_AndReplays()
        {
            var db = Content.Db;
            var books = new List<string>(db.SkillBooks.Keys);
            foreach (var sp in db.SpeciesList)
                foreach (var name in RunPolicies.Names)
                    foreach (var withBooks in new[] { false, true })
                    {
                        var lines = new List<string>();
                        var b = withBooks ? books : null;
                        var s = RunBot.Play(db, RunPolicies.Create(name), sp.Id, 3, lines.Add, b);
                        Assert.That(s.Phase, Is.EqualTo(RunPhase.Finished), $"{sp.Id} {name}");
                        Assert.That(s.Offers, Is.Empty, "bots resolve every offer");
                        Assert.That(lines.Count, Is.EqualTo(s.Log.Count));
                        var again = TrainingRun.Replay(db, sp.Id, 3, s.Log, b);
                        Assert.That(TrainingRun.StateHash(again), Is.EqualTo(TrainingRun.StateHash(s)), $"{sp.Id} {name} books={withBooks}");
                    }
        }

        [Test]
        public void Chaser_UsesOnlyItsOwnBook()
        {
            var db = Content.Db;
            var books = db.SkillBooks.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            foreach (var sp in db.SpeciesList)
            {
                var own = books.Where(b => ChaseTarget.IsOwnBook(db, sp.Id, b)).ToList();
                Assert.That(own, Has.Count.EqualTo(1), $"{sp.Id}: one book tree among its chase prerequisites");
                Assert.That(db.Trees[sp.ChaseTreeBook].Conditions.Any(cd => cd.Type == ConditionType.NodeOwned
                    && db.TreeNodes[cd.Ref].TreeId == db.SkillBooks[own[0]].TreeId), Is.True, "the book gates the species' book chase tree");
                var s = RunBot.Play(db, RunPolicies.Create("chaser"), sp.Id, 3, null, books);
                Assert.That(s.Books, Is.EquivalentTo(books.Except(own)), $"{sp.Id}: foreign books stay unused");
                Assert.That(s.Log.Count(l => l.Kind == RunCommandKind.UseBook), Is.EqualTo(1));
            }
        }

        [Test]
        public void ChaseTarget_ReadsConditionsFromContent()
        {
            var db = Content.Db;
            foreach (var sp in db.SpeciesList)
            {
                // No books: only the in-run chase tree can be reached; its stat conditions are the drill goals.
                var s = TrainingRun.Start(db, sp.Id, 1);
                var t = ChaseTarget.Of(db, s);
                var run = db.Trees[sp.ChaseTreeRun];
                Assert.That(t.Tree.Id, Is.EqualTo(run.Id), sp.Id);
                var stats = run.Conditions.Where(cd => cd.Type == ConditionType.StatAtLeast && s.Stats.Get(cd.Ref) < cd.Amount)
                               .Select(cd => (cd.Ref, cd.Amount));
                Assert.That(t.StatGoals, Is.EqualTo(stats));
                Assert.That(t.Needed, Does.Contain(run.Id));
                int sp0 = run.Nodes.Sum(n => n.Cost) + run.Conditions.Where(cd => cd.Type == ConditionType.TreeMaxed)
                    .Sum(cd => db.Trees[cd.Ref].Nodes.Sum(n => n.Cost));
                Assert.That(t.RemainingSp, Is.EqualTo(sp0), "nothing bought yet: the prerequisite trees and the chase tree in full");

                // With its own book the book chase tree becomes reachable too.
                var own = db.SkillBooks.Keys.Where(b => ChaseTarget.IsOwnBook(db, sp.Id, b)).ToList();
                var withBook = TrainingRun.Start(db, sp.Id, 1, own);
                var both = new[] { run.Id, sp.ChaseTreeBook };
                Assert.That(both, Does.Contain(ChaseTarget.Of(db, withBook).Tree.Id));
            }
        }

        [Test]
        public void UnknownPolicy_Throws() => Assert.Throws<ArgumentException>(() => RunPolicies.Create("nope"));
    }
}
