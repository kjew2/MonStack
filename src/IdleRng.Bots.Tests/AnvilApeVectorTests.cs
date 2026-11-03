using System.Collections.Generic;
using System.Linq;
using IdleRng.Bots.Golden;
using IdleRng.Sim.Combat;
using NUnit.Framework;

namespace IdleRng.Bots.Tests
{
    /// <summary>Species Anvil Ape (#153): the golden vector "2v3 anvil ape brittle and shatter" makes all three species rules fire.</summary>
    public class AnvilApeVectorTests
    {
        [Test]
        public void TheAnvilApeVector_FiresEverySpeciesRule()
        {
            var v = BattleVectors.Vectors().Single(x => x.Name == "2v3 anvil ape brittle and shatter");
            var log = new List<FightAction>();
            var r = BattleSim.Resolve(FightRules.FromContent(Content.Db), v.A, v.B, v.Seed, log.Add);
            foreach (var rule in new[] { RuleModifiers.AnvilRhythm, RuleModifiers.BrittleGuard, RuleModifiers.Shatterpoint, RuleModifiers.GuardBreak })
                Assert.That(log.Any(x => x.Event == FightAction.RulePrefix + rule), Is.True, rule);
            Assert.That(r.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut));
        }
    }
}
