using System;
using System.Collections.Generic;
using IdleRng.Bots.Training;
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
            foreach (var sp in db.SpeciesList)
                foreach (var name in RunPolicies.Names)
                {
                    var lines = new List<string>();
                    var s = RunBot.Play(db, RunPolicies.Create(name), sp.Id, 3, lines.Add);
                    Assert.That(s.Phase, Is.EqualTo(RunPhase.Finished), $"{sp.Id} {name}");
                    Assert.That(lines.Count, Is.EqualTo(s.Log.Count));
                    var again = TrainingRun.Replay(db, sp.Id, 3, s.Log);
                    Assert.That(TrainingRun.StateHash(again), Is.EqualTo(TrainingRun.StateHash(s)), $"{sp.Id} {name}");
                }
        }

        [Test]
        public void UnknownPolicy_Throws() => Assert.Throws<ArgumentException>(() => RunPolicies.Create("nope"));
    }
}
