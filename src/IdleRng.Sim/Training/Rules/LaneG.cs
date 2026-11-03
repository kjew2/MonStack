namespace IdleRng.Sim.Training
{
    // Idea-pool lane G: run rules (#140): ModifyDrillGain, ModifyDrillCost, RestAllowed, ModifyRestEnergy, OnCycleStart, OnRankFightStart and OnRankFightResult.
    // Implement the lane's hooks here as `static partial void <Hook>_G(...)` (signatures in RunHooks.cs). Run state the rules
    // add lives on RunState, is saved as lean JSON and is hashed in `HashRun_G` only when present.
    public static partial class TrainingRun
    {
    }
}
