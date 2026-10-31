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
        public void GauntletSpread_CountsEveryMonster_AndReports()
        {
            var db = Content.Db;
            var r = GauntletSpread.Collect(db, new[] { "focus" }, 2);
            Assert.That(r.Floors.Count, Is.EqualTo(db.SpeciesList.Count * 2));
            int digits = 0;
            foreach (var d in r.ByDigit) digits += d;
            Assert.That(digits + r.Cleared, Is.EqualTo(r.Floors.Count), "every stall lands in one floor digit");
            Assert.That(r.OnBoss, Is.LessThanOrEqualTo(digits));
            StringAssert.Contains("stalls on boss floors", GauntletSpread.Report(r));
        }

        [Test]
        public void UnknownPolicy_Throws() => Assert.Throws<ArgumentException>(() => RunPolicies.Create("nope"));
    }
}
