namespace IdleRng.Sim.Combat
{
    // Idea-pool lane B: hits taken and survival (#135): BeforeHitResolves, ModifyIncoming, ModifyHpDamage, OnHitTaken and OnWouldDie.
    // Implement the lane's hooks here as `static partial void <Hook>_B(...)` (signatures in RuleHooks.cs), its constants as
    // FightRules properties loaded in `Load_B` and checked in `ValidateRules_B`, and its per-unit state as FightUnit fields
    // (hashed in `HashUnit_B`, deep-copied in `Clone_B`). Respect the loop guards (BattleGuards.cs, specs/16 section 6.4).
    public static partial class BattleSim
    {
    }
}
