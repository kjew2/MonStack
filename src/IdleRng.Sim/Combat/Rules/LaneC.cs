namespace IdleRng.Sim.Combat
{
    // Idea-pool lane C: turn gauge (#136): BeforeAction, AfterAction, ExtraAction (under MON_EXTRA_ACTIONS_CAP), OnGaugeTick and the hit hooks.
    // Implement the lane's hooks here as `static partial void <Hook>_C(...)` (signatures in RuleHooks.cs), its constants as
    // FightRules properties loaded in `Load_C` and checked in `ValidateRules_C`, and its per-unit state as FightUnit fields
    // (hashed in `HashUnit_C`, deep-copied in `Clone_C`). Respect the loop guards (BattleGuards.cs, specs/16 section 6.4).
    public static partial class BattleSim
    {
    }
}
