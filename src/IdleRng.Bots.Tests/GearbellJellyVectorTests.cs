using System.Collections.Generic;
using System.Linq;
using IdleRng.Bots.Golden;
using IdleRng.Sim.Combat;
using NUnit.Framework;

namespace IdleRng.Bots.Tests
{
    /// <summary>Species Gearbell Jelly (#158): the golden vector "2v3 gearbell jelly toll and escapement" makes all three species rules fire.</summary>
    public class GearbellJellyVectorTests
    {
        [Test]
        public void TheGearbellJellyVector_FiresEverySpeciesRule()
        {
            var v = BattleVectors.Vectors().Single(x => x.Name == "2v3 gearbell jelly toll and escapement");
            var log = new List<FightAction>();
            var r = BattleSim.Resolve(FightRules.FromContent(Content.Db), v.A, v.B, v.Seed, log.Add);
            foreach (var rule in new[] { RuleModifiers.WardRewind, RuleModifiers.WardToll, RuleModifiers.Escapement, RuleModifiers.ManaWard })
                Assert.That(log.Any(x => x.Event == FightAction.RulePrefix + rule), Is.True, rule);
            Assert.That(r.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut));
        }
    }
}
