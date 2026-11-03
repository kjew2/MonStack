using System.Collections.Generic;
using System.Linq;
using IdleRng.Bots.Golden;
using IdleRng.Sim.Combat;
using NUnit.Framework;

namespace IdleRng.Bots.Tests
{
    /// <summary>Species Candlemane (#141): the golden vector "2v2 martyr flame and hollow sun" makes both chase rules fire.</summary>
    public class CandlemaneVectorTests
    {
        [Test]
        public void TheCandlemaneVector_FiresBothChaseRules()
        {
            var v = BattleVectors.Vectors().Single(x => x.Name == "2v2 martyr flame and hollow sun");
            var log = new List<FightAction>();
            var r = BattleSim.Resolve(FightRules.FromContent(Content.Db), v.A, v.B, v.Seed, log.Add);
            foreach (var rule in new[] { RuleModifiers.MartyrFlame, RuleModifiers.HollowSun, RuleModifiers.BloodMagic })
                Assert.That(log.Any(x => x.Event == FightAction.RulePrefix + rule), Is.True, rule);
            Assert.That(r.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut));
        }
    }
}
