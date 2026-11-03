namespace IdleRng.Sim.Combat
{
    // Idea-pool lane E: team size and mode (#138): OnFightStart, OnDeath and the kill path of OnWouldDie.
    // Implement the lane's hooks here as `static partial void <Hook>_E(...)` (signatures in RuleHooks.cs), its constants as
    // FightRules properties loaded in `Load_E` and checked in `ValidateRules_E`, and its per-unit state as FightUnit fields
    // (hashed in `HashUnit_E`, deep-copied in `Clone_E`). Respect the loop guards (BattleGuards.cs, specs/16 section 6.4).
    public static partial class BattleSim
    {
    }
}
