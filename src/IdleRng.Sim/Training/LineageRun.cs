using System.Collections.Generic;
using IdleRng.Sim.Account;

namespace IdleRng.Sim.Training
{
    /// <summary>Birth snapshot, inherited family progress, and focus high-water mark retained through overwrites.</summary>
    public sealed class LineageRun
    {
        public string TreeId { get; set; } = "fortune";
        public int Rank { get; set; }
        public int Deepest { get; set; }
        public int Primary { get; set; }
        public int Supporting { get; set; }
        public string SupportingTreeId { get; set; }
        public SortedDictionary<string, FortunaLineage> Progress { get; set; }
        public LineageRun Clone()
        {
            var copy = (LineageRun)MemberwiseClone();
            copy.Progress = TreeLineages.CloneProgress(Progress);
            return copy;
        }
    }
}
