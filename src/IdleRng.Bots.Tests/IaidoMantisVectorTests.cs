using System.Collections.Generic;
using System.Linq;
using IdleRng.Bots.Golden;
using IdleRng.Sim.Combat;
using NUnit.Framework;

namespace IdleRng.Bots.Tests
{
    /// <summary>Species Iaido Mantis (#159): the golden vector "2v3 iai, still hunt and severed arts" makes all its rules fire.</summary>
    public class IaidoMantisVectorTests
    {
        [Test]
        public void TheIaidoMantisVector_FiresEveryRule()
        {
            var v = BattleVectors.Vectors().Single(x => x.Name == "2v3 iai, still hunt and severed arts");
            var log = new List<FightAction>();
            var r = BattleSim.Resolve(FightRules.FromContent(Content.Db), v.A, v.B, v.Seed, log.Add);
            foreach (var rule in new[] { RuleModifiers.Vantage, RuleModifiers.IaiDraw, RuleModifiers.StillHunt, RuleModifiers.SeverArt })
                Assert.That(log.Any(x => x.Event == FightAction.RulePrefix + rule), Is.True, rule);
            Assert.That(r.Outcome, Is.Not.EqualTo(FightOutcome.TimedOut));
        }
    }
}
