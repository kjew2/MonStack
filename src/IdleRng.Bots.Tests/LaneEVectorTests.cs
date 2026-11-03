using System.Collections.Generic;
using System.Linq;
using IdleRng.Bots.Golden;
using IdleRng.Sim.Combat;
using NUnit.Framework;

namespace IdleRng.Bots.Tests
{
    /// <summary>Idea-pool lane E (#138): the golden vector "4v4 team and mode capstones" makes each of the lane's rules fire.</summary>
    public class LaneEVectorTests
    {
        [Test]
        public void TheLaneEVector_FiresEveryLaneERule()
        {
            var v = BattleVectors.Vectors().Single(x => x.Name == "4v4 team and mode capstones");
            var log = new List<FightAction>();
            var r = BattleSim.Resolve(FightRules.FromContent(Content.Db), v.A, v.B, v.Seed, log.Add);
            foreach (var rule in new[] { RuleModifiers.LoneWolf, RuleModifiers.Arise, RuleModifiers.LastRite })
                Assert.That(log.Any(x => x.Event == FightAction.RulePrefix + rule), Is.True, rule);
            Assert.That(r.Final.Units.Any(u => u.Risen), Is.True);
            Assert.That(r.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut));
        }
    }
}
