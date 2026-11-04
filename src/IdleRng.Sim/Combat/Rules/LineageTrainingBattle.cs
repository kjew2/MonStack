namespace IdleRng.Sim.Combat
{
    public static partial class BattleSim
    {
        // X curse has a smaller team expression: a living carrier protects an ally against a better-armored attacker.
        // One carrier suffices; multiple carriers never multiply the reduction. Solo fights receive no extra benefit.
        static void LineageTrainingIncoming(RuleCtx c, HitInfo h)
        {
            foreach (var ally in c.State.Units)
                if (ally.Alive && ally.Side == h.Tgt.Side && ally != h.Tgt && Has(ally, "lineage_cursed_mark_x")
                    && Eff(c.Rules, h.Src, StatIds.Def) > Eff(c.Rules, ally, StatIds.Def))
                { h.Raw *= 1 - c.Rules.LineageGain; return; }
        }
    }
}
