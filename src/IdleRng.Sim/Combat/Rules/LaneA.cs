namespace IdleRng.Sim.Combat
{
    // Idea-pool lane A: conversion (#134): the conversion step, ModifyOutgoing and ModifyIncoming, skill readiness and cost.
    // Implement the lane's hooks here as `static partial void <Hook>_A(...)` (signatures in RuleHooks.cs), its constants as
    // FightRules properties loaded in `Load_A` and checked in `ValidateRules_A`, and its per-unit state as FightUnit fields
    // (hashed in `HashUnit_A`, deep-copied in `Clone_A`). Respect the loop guards (BattleGuards.cs, specs/16 section 6.4).
    public static partial class BattleSim
    {
    }
}
