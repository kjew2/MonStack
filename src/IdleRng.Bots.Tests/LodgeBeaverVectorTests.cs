using System.Collections.Generic;
using System.Linq;
using IdleRng.Bots.Golden;
using IdleRng.Sim.Combat;
using NUnit.Framework;

namespace IdleRng.Bots.Tests
{
    /// <summary>Species Lodge Beaver (#146): the golden vector "2v3 tail slap and holdback" makes all three species rules fire.</summary>
    public class LodgeBeaverVectorTests
    {
        [Test]
        public void TheLodgeBeaverVector_FiresEverySpeciesRule()
        {
            var v = BattleVectors.Vectors().Single(x => x.Name == "2v3 tail slap and holdback");
            var log = new List<FightAction>();
            var r = BattleSim.Resolve(FightRules.FromContent(Content.Db), v.A, v.B, v.Seed, log.Add);
            foreach (var rule in new[] { RuleModifiers.Mudwork, RuleModifiers.TailSlap, RuleModifiers.Holdback, RuleModifiers.Barricade })
                Assert.That(log.Any(x => x.Event == FightAction.RulePrefix + rule), Is.True, rule);
            Assert.That(r.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut));
        }
    }
}
