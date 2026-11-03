namespace IdleRng.Sim.Combat
{
    // Idea-pool lane D: debuffs, stacking and skill locks (#137): OnHitLanded, ModifyOutgoing, SkillReady and OnSkillUsed.
    // Implement the lane's hooks here as `static partial void <Hook>_D(...)` (signatures in RuleHooks.cs), its constants as
    // FightRules properties loaded in `Load_D` and checked in `ValidateRules_D`, and its per-unit state as FightUnit fields
    // (hashed in `HashUnit_D`, deep-copied in `Clone_D`). Respect the loop guards (BattleGuards.cs, specs/16 section 6.4).
    public static partial class BattleSim
    {
    }
}
