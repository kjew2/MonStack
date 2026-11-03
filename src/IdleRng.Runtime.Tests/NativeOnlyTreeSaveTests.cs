using System.Linq;
using IdleRng.Runtime.Save;
using IdleRng.Sim.Account;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
using IdleRng.TestFixtures;
using NUnit.Framework;

namespace IdleRng.Runtime.Tests
{
    /// <summary>
    /// A species lane turns its idea-pool tree native-only (#160, kj 2026-11-03). A save made while the tree was generic still
    /// loads with no notice: the run in progress keeps it slotted (and a pending Awakening that offers it), and a Vault entry
    /// holding it keeps it and still fights with its capstone (specs/06 section 5, rule 1 of #132).
    /// </summary>
    public class NativeOnlyTreeSaveTests
    {
        /// <summary>Puts an Awakening offer of <paramref name="tree"/> (rolled, as a draft would) first in the run's offers.</summary>
        static void OfferAwakening(ContentDb c, RunState s, string tree)
        {
            s.Offers.Insert(0, new TreeOffer { Source = OfferSource.Milestone, Stat = StatIds.Hp, Threshold = c.StatMilestones[0], Trees = { tree },
                                               Rolls = { TrainingRun.RollTree(c, s.Draft, c.Trees[tree]) } });
        }

        static void Max(ContentDb c, RunState s, int slot)
        {
            while (s.Slots[slot].Bought < c.Trees[s.Slots[slot].TreeId].Nodes.Count) { s.Sp = System.Math.Max(s.Sp, 10); TrainingRun.SpendPoint(c, s, slot); }
        }

        [Test]
        public void ASaveHoldingATreeThatTurnedNative_Loads_AndKeepsIt()
        {
            var shipped = TestContent.Db;
            var a = AccountOps.New(shipped, 31);
            AccountOps.UnlockSpecies(shipped, a, "drake");

            // A sealed monster that took Blood Magic from an Awakening and bought its capstone.
            var first = AccountOps.StartRun(shipped, a, "drake");
            OfferAwakening(shipped, first, "blood_magic");
            TrainingRun.TakeTree(shipped, first, 0, 2);
            Max(shipped, first, 2);
            while (first.Phase != RunPhase.Finished)
            {
                while (first.Offers.Count > 0) TrainingRun.DiscardOffer(shipped, first);
                if (first.Phase == RunPhase.AwaitFight) TrainingRun.Fight(shipped, first);
                else TrainingRun.Rest(shipped, first);
            }
            AccountOps.FinishRun(shipped, a, 1000);

            // A run in progress holding Mimic Core, with an Awakening that offers Devourer still pending.
            var run = AccountOps.StartRun(shipped, a, "drake");
            OfferAwakening(shipped, run, "mimic_core");
            TrainingRun.TakeTree(shipped, run, 0, 2);
            OfferAwakening(shipped, run, "devourer");

            var native = ContentFixtures.WithNativeTrees("blood_magic", "mimic_core", "devourer");
            Assert.That(native.Trees["blood_magic"].Kind, Is.EqualTo(TreeKind.Native));
            var b = AccountSave.Decode(native, AccountSave.Encode(a), out var notice);
            Assert.That(notice, Is.Null, "nothing is dropped");

            Assert.That(b.Active, Is.Not.Null);
            Assert.That(b.Active.Slots[2].TreeId, Is.EqualTo("mimic_core"), "the run keeps the tree slotted");
            Assert.That(b.Active.Offers[0].Trees, Is.EqualTo(new[] { "devourer" }), "and the pending offer");
            TrainingRun.TakeTree(native, b.Active, 0, 3);
            Assert.That(b.Active.Slots[3].TreeId, Is.EqualTo("devourer"), "which can still be taken");
            TrainingRun.Rest(native, b.Active);

            var entry = b.Vault.Single();
            Assert.That(entry.Trees.Select(t => t.TreeId), Does.Contain("blood_magic"));
            Assert.That(entry.Fighter(native).Rules, Does.Contain(RuleModifiers.BloodMagic), "the sealed monster still fights with its capstone");
        }
    }
}
