namespace IdleRng.Sim.Training
{
    /// <summary>Birth snapshot plus progress high-water mark, retained through overwrites. Specs/29.</summary>
    public sealed class LineageRun
    {
        public int Rank { get; set; }
        public int Deepest { get; set; }
        public int Primary { get; set; }
        public int Supporting { get; set; }
        public LineageRun Clone() => (LineageRun)MemberwiseClone();
    }
}
