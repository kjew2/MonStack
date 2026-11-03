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
        public void EveryPolicy_SealedMonster_FightsAsTheRunDid_AfterASaveRoundTrip()
        {
            // A Vault entry keeps its result, not its replay (specs/27 section 3.5): its fighter, built from the stored stats and
            // trees after the save round trip (rolled trees lose their in-memory node list), must be the finished run's fighter.
            var db = Content.Db;
            var books = new List<string>(db.SkillBooks.Keys);
            var a = IdleRng.Sim.Account.AccountOps.New(db, 9);
            var want = new List<IdleRng.Sim.Combat.Fighter>();
            var labels = new List<string>();
            int id = 0;
            foreach (var sp in db.SpeciesList)
                foreach (var name in RunPolicies.Names)
                    foreach (var withBooks in new[] { false, true })
                        foreach (ulong seed in new ulong[] { 3, 11 })
                        {
                            var s = RunBot.Play(db, RunPolicies.Create(name), sp.Id, seed, null, withBooks ? books : null);
                            want.Add(TrainingRun.MonsterFighter(db, s));
                            labels.Add($"{sp.Id} {name} books={withBooks} seed={seed}");
                            a.Vault.Add(Vault.Seal(db, s, 0, id++));
                        }
            var b = IdleRng.Runtime.Save.AccountSave.Decode(db, IdleRng.Runtime.Save.AccountSave.Encode(a));
            Assert.That(b.Vault.Count, Is.EqualTo(want.Count));
            Assert.That(b.Vault.Any(e => e.Trees.Any(t => t.Roll != null)), Is.True, "some monster has a rolled tree");
            for (int i = 0; i < want.Count; i++)
            {
                var got = b.Vault[i].Fighter(db);
                var w = want[i];
                string what = labels[i];
                Assert.That(got.Element, Is.EqualTo(w.Element), what);
                foreach (var st in IdleRng.Sim.Combat.StatIds.Trained) Assert.That(got.Stats.Get(st), Is.EqualTo(w.Stats.Get(st)), what + " " + st);
                Assert.That(got.Skills.Select(k => k.Id), Is.EqualTo(w.Skills.Select(k => k.Id)), what + " skills");
                Assert.That(got.Triggers.Select(t => t.Trigger + ":" + t.Skill.Id), Is.EqualTo(w.Triggers.Select(t => t.Trigger + ":" + t.Skill.Id)), what + " triggers");
                Assert.That(got.Rules, Is.EqualTo(w.Rules), what + " rules");
                Assert.That(b.Vault[i].Id, Is.EqualTo(i), what + " id");
            }
        }

        [Test]
        public void ARunSavedAsItsState_ContinuesExactly_FromAnyPoint()
        {
            // Save v10 keeps the run in progress as its state, not its log (specs/27 section 3.5). Saving and loading the account
            // in the middle of a run, at several points, must not change how the run ends.
            var db = Content.Db;
            var books = new List<string>(db.SkillBooks.Keys);
            foreach (var sp in db.SpeciesList)
                foreach (var name in RunPolicies.Names)
                {
                    var straight = RunBot.Play(db, RunPolicies.Create(name), sp.Id, 13, null, books);
                    var a = IdleRng.Sim.Account.AccountOps.New(db, 1);
                    a.Active = TrainingRun.Start(db, sp.Id, 13, books);
                    var policy = RunPolicies.Create(name);
                    for (int step = 0; step < 100000; step++)   // as RunBot.Play: until the policy stops (it spends after the end)
                    {
                        if (step % 7 == 3)   // reload from the save every few commands
                            a = IdleRng.Runtime.Save.AccountSave.Decode(db, IdleRng.Runtime.Save.AccountSave.Encode(a), out var notice);
                        var cmd = policy.Next(db, a.Active);
                        if (cmd == null) break;
                        TrainingRun.Apply(db, a.Active, cmd);
                    }
                    Assert.That(TrainingRun.StateHash(a.Active), Is.EqualTo(TrainingRun.StateHash(straight)), $"{sp.Id} {name}");
                    Assert.That(a.Active.BooksUsed, Is.EqualTo(straight.BooksUsed), $"{sp.Id} {name}: books used survive the reloads");
                }
        }

        [Test]
        public void MonoPolicy_DrillsOnlyItsStat_UntilCapped()
        {
            var db = Content.Db;
            foreach (var stat in IdleRng.Sim.Combat.StatIds.Trained)
            {
                var s = RunBot.Play(db, RunPolicies.Create(RunPolicies.MonoPrefix + stat), "drake", 3);
                Assert.That(s.Phase, Is.EqualTo(RunPhase.Finished), stat);
                var drilled = s.Log.Where(l => l.Kind == RunCommandKind.Drill).Select(l => l.Stat).ToList();
                Assert.That(drilled, Is.Not.Empty, stat);
                if (s.Stats.Get(stat) < db.Constants.StatMax) Assert.That(drilled, Is.All.EqualTo(stat), stat);
            }
            Assert.Throws<ArgumentException>(() => RunPolicies.Create("mono-xyz"));
            Assert.That(RunPolicies.Names, Has.None.StartsWith(RunPolicies.MonoPrefix), "mono policies are opt-in");
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

        [Test]
        public void EveryPolicy_PlaysTheLaneGRunRules_WithoutDeadEnding()
        {
            // Idea-pool lane G (#140): each run-rule capstone held from cycle 1; every policy still finishes the run, and a vowed
            // run never rests (the bots ask TrainingRun.CanRest).
            var db = Content.Db;
            foreach (var tree in new[] { "iron_vow", "cursed_mark", "devourer", "second_breath", "ascetic", "hungry_core" })
                foreach (var name in RunPolicies.Names)
                {
                    var s = RunBot.Play(db, RunPolicies.Create(name), "drake", 11, null, null, null, new RunModifiers { StartSp = 11 },
                                        r => RunBot.HoldTrees(db, r, new[] { tree }));
                    Assert.That(s.Phase, Is.EqualTo(RunPhase.Finished), $"{tree} {name}");
                    Assert.That(s.Offers, Is.Empty);
                    if (tree == "iron_vow") Assert.That(s.Rests, Is.Zero, name);
                }
        }
    }
}
