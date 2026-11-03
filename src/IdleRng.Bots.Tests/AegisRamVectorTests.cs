using System.Collections.Generic;
using System.Linq;
using IdleRng.Bots.Golden;
using IdleRng.Sim.Combat;
using NUnit.Framework;

namespace IdleRng.Bots.Tests
{
    /// <summary>Species Aegis Ram (#147): the golden vector "2v3 aries vanguard and chrysomallos" makes all its rules fire.</summary>
    public class AegisRamVectorTests
    {
        [Test]
        public void TheAegisRamVector_FiresEveryRule()
        {
            var v = BattleVectors.Vectors().Single(x => x.Name == "2v3 aries vanguard and chrysomallos");
            var log = new List<FightAction>();
            var r = BattleSim.Resolve(FightRules.FromContent(Content.Db), v.A, v.B, v.Seed, log.Add);
            foreach (var rule in new[] { RuleModifiers.BraveHeart, RuleModifiers.FleeceAegis, RuleModifiers.BraveDebt, RuleModifiers.RamShock })
                Assert.That(log.Any(x => x.Event == FightAction.RulePrefix + rule), Is.True, rule);
            Assert.That(log.Any(x => x.Event == BattleSim.FleeceEvent && x.Actor != x.Target), Is.True, "a fleece goes to the other ram");
            Assert.That(r.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut));
        }
    }
}
