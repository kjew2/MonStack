using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;

namespace IdleRng.Bots.Training
{
    /// <summary>How one of the run species' chase trees (specs/15 section 5.1) went in one run.</summary>
    public sealed class ChaseOutcome
    {
        public string TreeId { get; set; }

        /// <summary>Every condition held at once at some point (the tree unlocked and was offered or slotted).</summary>
        public bool Unlocked { get; set; }

        /// <summary>Cycle the capstone (the tree's last node) was bought, when the run ended with it bought; 0 = it did not.</summary>
        public int CapstoneCycle { get; set; }

        /// <summary>The tree's condition ids and short labels (TreeDef.Conditions order), e.g. "heavy_armor maxed", "def>=500".</summary>
        public string[] ConditionIds { get; set; }
        public string[] ConditionLabels { get; set; }

        /// <summary>Per condition (same order): it held at some point of the run.</summary>
        public bool[] ConditionsHeld { get; set; }

        public bool Capstone => CapstoneCycle > 0;
    }

    /// <summary>
    /// Watches a run for the balance report (specs/09 section 7): which of the species' chase conditions held at some point,
    /// whether each chase tree unlocked, and the cycle its capstone was bought. Feed it every state of the run
    /// (<see cref="RunBot.Play"/>'s observer); a capstone that leaves its slot again (its conditions broke) is forgotten.
    /// </summary>
    public sealed class ChaseTracker
    {
        readonly ContentDb _c;
        readonly List<TreeDef> _trees;
        readonly List<ChaseOutcome> _out;

        public ChaseTracker(ContentDb c, string speciesId)
        {
            _c = c;
            _trees = c.TreeList.Where(t => t.Kind == TreeKind.Chase && t.Species == speciesId).ToList();
            _out = _trees.Select(t => new ChaseOutcome
            {
                TreeId = t.Id, ConditionsHeld = new bool[t.Conditions.Count],
                ConditionIds = t.Conditions.Select(cd => cd.Id).ToArray(), ConditionLabels = t.Conditions.Select(Label).ToArray(),
            }).ToList();
        }

        /// <summary>A short label for a chase condition: "heavy_armor maxed", "spellcraft_n4", "def>=500", "wins>=2".</summary>
        public static string Label(ChaseConditionDef cd)
        {
            switch (cd.Type)
            {
                case ConditionType.TreeMaxed: return cd.Ref + " maxed";
                case ConditionType.NodeOwned: return cd.Ref;
                default: return cd.Ref + ">=" + cd.Amount.ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>One outcome per chase tree of the species, in content order.</summary>
        public IReadOnlyList<ChaseOutcome> Outcomes => _out;

        public void Observe(RunState s)
        {
            for (int i = 0; i < _trees.Count; i++)
            {
                var t = _trees[i];
                var o = _out[i];
                bool all = true;
                for (int j = 0; j < t.Conditions.Count; j++)
                {
                    if (TrainingRun.ConditionHolds(_c, s, t.Conditions[j])) o.ConditionsHeld[j] = true;
                    else all = false;
                }
                if (all) o.Unlocked = true;
                var slot = s.Slots.FirstOrDefault(x => x != null && x.TreeId == t.Id);
                bool maxed = slot != null && slot.Bought >= t.Nodes.Count;
                if (!maxed) o.CapstoneCycle = 0;
                else if (o.CapstoneCycle == 0) o.CapstoneCycle = s.Cycle;
            }
        }
    }
}
