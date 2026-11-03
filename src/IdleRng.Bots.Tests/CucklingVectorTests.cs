using System.Collections.Generic;
using System.Linq;
using IdleRng.Bots.Golden;
using IdleRng.Sim.Combat;
using NUnit.Framework;

namespace IdleRng.Bots.Tests
{
    /// <summary>Species Cuckling (#143): the golden vector "2v3 usurper cuckoo and clutch empress" makes all three of its rules fire.</summary>
    public class CucklingVectorTests
    {
        [Test]
        public void TheCucklingVector_FiresEveryRule()
        {
            var v = BattleVectors.Vectors().Single(x => x.Name == "2v3 usurper cuckoo and clutch empress");
            var log = new List<FightAction>();
            var r = BattleSim.Resolve(FightRules.FromContent(Content.Db), v.A, v.B, v.Seed, log.Add);
            foreach (var rule in new[] { RuleModifiers.StolenTurn, RuleModifiers.Usurp, RuleModifiers.FoundlingEgg })
                Assert.That(log.Any(x => x.Event == FightAction.RulePrefix + rule), Is.True, rule);
            Assert.That(log.Count(x => x.Event == BattleSim.EggEvent), Is.EqualTo(2), "the egg is laid and hatches");
            Assert.That(r.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut));
        }
    }
}
