using System;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Combat
{
    // Species Lodge Beaver (#146, epic #160), specs/16 section 6.2. Its rules move everything into its one shield pool (the wall):
    //   mudwork    every heal it would receive becomes shield instead (after the tower's heal cut), up to MON_MUD_CAP of max HP or
    //              a larger cap it has (barricade, tidal_overflow, abyssal_drain), lasting MON_MUD_TURNS turns; it never regains HP
    //              from a heal. A granted shield, not a heal: no Healed hook, no healing tally.
    //   tail_slap  each hit it lands adds MON_SLAP_SHIELD x its current shield to the hit's raw damage (the conversion step, once
    //              per hit, before every multiplier); the shield is not spent
    //   holdback   HP damage past its shield goes into a Reservoir instead of HP (counted as dealt when stored). At the start of
    //              each own action MON_DAM_FLOW of the Reservoir drains into HP (not a hit: no triggers, tallies or rolls; a death
    //              is credited to the last enemy that damaged it). Every shield it gains washes stored damage out first, dealing
    //              MON_DAM_SPILL x the amount washed out to an enemy as nature true damage. At MON_DAM_BREAK x max HP the dam
    //              breaks: the whole Reservoir lands on HP at once.
    // Only holdback draws (the enemy a release hits, in teams) and keeps state (the Reservoir and the last enemy that damaged
    // it), hashed for carriers.
    public static partial class BattleSim
    {
        /// <summary>mudwork turned a heal into shield: Amount is the heal converted.</summary>
        public const string MudEvent = FightAction.RulePrefix + RuleModifiers.Mudwork;

        /// <summary>tail_slap on a landed hit: Actor is the carrier, Target the target, Amount the damage the shield added.</summary>
        public const string SlapEvent = FightAction.RulePrefix + RuleModifiers.TailSlap;

        /// <summary>
        /// holdback: Amount is the Reservoir after the change. Target is the attacker when a blow was held back, the carrier itself
        /// when the Reservoir drained or the dam broke, and the enemy the release hit when a shield washed stored damage out.
        /// </summary>
        public const string HoldEvent = FightAction.RulePrefix + RuleModifiers.Holdback;

        /// <summary>mudwork: the heal lands as nothing; its amount becomes a shield of the unit's (through ShieldIn, so holdback washes first).</summary>
        static partial void HealIn_LodgeBeaver(RuleCtx c, FightUnit t, double amount, ref bool taken)
        {
            if (taken || !Has(t, RuleModifiers.Mudwork)) return;
            taken = true;
            if (!(amount > 0) || !t.Alive) return;
            var r = c.Rules;
            GainShield(c, t, amount, r.MudTurns * r.TurnTicks, MudCap(r, t) * t.MaxHp);
            c.Log?.Invoke(new FightAction(t.Id, MudEvent, t.Id, amount));
        }

        /// <summary>mudwork's shield cap, a fraction of max HP: MON_MUD_CAP or the larger cap of barricade, tidal_overflow or abyssal_drain.</summary>
        static double MudCap(FightRules r, FightUnit t)
        {
            double capFrac = r.MudCap;
            if (Has(t, RuleModifiers.TidalOverflow)) capFrac = Math.Max(capFrac, r.TideShieldCap);
            if (Has(t, RuleModifiers.AbyssalDrain)) capFrac = Math.Max(capFrac, r.DrainShieldCap);
            HookShieldCap(r, t, ref capFrac);   // barricade
            return capFrac;
        }

        /// <summary>tail_slap: the conversion step, after the Keepshell Crab's: the carrier's current shield feeds the hit.</summary>
        static partial void ConvertHit_LodgeBeaver(RuleCtx c, HitInfo h)
        {
            var src = h.Src;
            if (!(src.Shield > 0) || !(c.Rules.SlapShield > 0) || !Has(src, RuleModifiers.TailSlap)) return;
            double add = c.Rules.SlapShield * src.Shield * h.FlatShare;   // a fountain_jets jet takes its share
            h.Raw += add;
            h.Slapped = add;
        }

        /// <summary>tail_slap's log line, once the hit has landed (an evaded hit logs nothing).</summary>
        static partial void OnHitMitigated_LodgeBeaver(RuleCtx c, HitInfo h, ref double damage)
        {
            if (h.Slapped > 0) c.Log?.Invoke(new FightAction(h.Src.Id, SlapEvent, h.Tgt.Id, h.Slapped));
        }

        /// <summary>
        /// holdback: the HP loss goes into the Reservoir (held: it counts as dealt now, up to the HP the unit has) and leaves HP alone;
        /// once the Reservoir reaches MON_DAM_BREAK x max HP the dam breaks and all of it lands on HP here (src gets a kill it causes).
        /// </summary>
        static partial void HpHeld_LodgeBeaver(RuleCtx c, FightUnit src, FightUnit tgt, ref double amount, ref double held)
        {
            if (!Has(tgt, RuleModifiers.Holdback) || !tgt.Alive) return;
            if (src.Side != tgt.Side) tgt.DamLastHit = src.Id;
            if (!(amount > 0)) return;
            held = Math.Min(amount, tgt.Hp);
            tgt.Reservoir += amount;
            amount = 0;
            c.Log?.Invoke(new FightAction(tgt.Id, HoldEvent, src.Id, tgt.Reservoir));
            if (tgt.Reservoir < c.Rules.DamBreak * tgt.MaxHp) return;
            double flood = tgt.Reservoir;   // the dam breaks
            tgt.Reservoir = 0;
            tgt.Hp = Math.Max(0.0, tgt.Hp - flood);
            c.Log?.Invoke(new FightAction(tgt.Id, HoldEvent, tgt.Id, 0));
        }

        /// <summary>
        /// holdback: at the start of each own action (before its OnAction triggers, even one another rule skips), MON_DAM_FLOW of the
        /// Reservoir drains into HP. Not a hit. A drain that would kill meets root_rebirth and the would-die hook like a blow; a death
        /// skips the action and is credited to the last enemy that damaged the unit.
        /// </summary>
        static partial void BeforeAction_LodgeBeaver(RuleCtx c, FightUnit u, ref bool skip)
        {
            if (!(u.Reservoir > 0) || !u.Alive || !Has(u, RuleModifiers.Holdback)) return;
            double drain = c.Rules.DamFlow * u.Reservoir;
            u.Reservoir -= drain;
            if (u.Reservoir < 1e-9) u.Reservoir = 0;
            c.Log?.Invoke(new FightAction(u.Id, HoldEvent, u.Id, u.Reservoir));
            double loss = Math.Min(u.Hp, drain);
            u.Hp -= loss;
            if (u.Alive) return;
            FightUnit killer = null;
            foreach (var k in c.State.Units) if (k.Id == u.DamLastHit) { killer = k; break; }
            bool saved = false;
            if (u.RebirthHeal > 0)   // root_rebirth: it survives this one drain, once per fight
            {
                u.Hp = u.RebirthHeal * u.MaxHp;
                u.RebirthHeal = 0;
                u.RebornPending = true;
                saved = true;
            }
            else HookWouldDie(c, killer ?? u, u, ref saved);
            Reborn(u, c.Log);
            if (saved && u.Alive) return;
            u.Hp = 0;
            if (killer != null)
            {
                killer.Tally.Kills++;
                u.KilledBy = killer.Id;
            }
            skip = true;
        }

        /// <summary>
        /// holdback: a shield the unit gains washes stored damage out of the Reservoir first; only the rest becomes shield. The
        /// amount washed out is released: MON_DAM_SPILL of it hits an enemy (a taunting one, else random in teams: one draw) as
        /// nature true damage, through its shield (no roll, no crit, no trigger, no conversion).
        /// </summary>
        static partial void ShieldIn_LodgeBeaver(RuleCtx c, FightUnit t, ref double amount)
        {
            if (!(t.Reservoir > 0) || !(amount > 0) || !t.Alive || !Has(t, RuleModifiers.Holdback)) return;
            double washed = Math.Min(t.Reservoir, amount);
            t.Reservoir -= washed;
            if (t.Reservoir < 1e-9) t.Reservoir = 0;
            amount -= washed;
            double spill = c.Rules.DamSpill * washed;
            var e = spill > 0 ? PickEnemy(c.State, t, c.Rng) : null;
            if (e != null) Absorb(c, t, e, spill, RuleModifiers.NatureElement);
            c.Log?.Invoke(new FightAction(t.Id, HoldEvent, e?.Id ?? t.Id, t.Reservoir));
            if (e != null) Reborn(e, c.Log);
        }

        /// <summary>
        /// The AI (specs/16 section 5.2) for a shield, and for a mudwork carrier's heal on itself: whether gaining amount would do
        /// something for t. Under holdback, whatever it washes out of the Reservoir is used; the rest must raise the shield. Without
        /// barricade the rest raises it when it beats the shield it has (the larger is kept); under barricade, when it beats it or
        /// stacks in full under the cap (no cast is wasted on a wall that is nearly full).
        /// </summary>
        static bool ShieldWanted(FightRules r, FightUnit t, double amount)
        {
            if (!t.Alive || !(amount > 0)) return false;
            double washed = t.Reservoir > 0 && Has(t, RuleModifiers.Holdback) ? Math.Min(t.Reservoir, amount) : 0, rest = amount - washed;
            if (t.Shield < rest || rest <= 0) return true;
            return Has(t, RuleModifiers.Barricade) && t.Shield + rest <= r.BarricadeCap * t.MaxHp;
        }

        /// <summary>A mudwork carrier's heal on itself, as the AI sees it: the shield it would become (capped like the rule caps it).</summary>
        static double MudShield(FightRules r, FightUnit t, double heal) => Math.Min(heal, MudCap(r, t) * t.MaxHp);

        static partial void HashUnit_LodgeBeaver(CanonicalWriter w, FightState s, FightUnit u)
        {
            if (Has(u, RuleModifiers.Holdback)) { w.F64(u.Reservoir); w.Str(u.DamLastHit); }
        }

        static partial void ValidateRules_LodgeBeaver(FightRules r, ref bool ok)
        {
            if (!(In01(r.MudCap) && r.MudTurns >= 1 && r.SlapShield >= 0 && !double.IsInfinity(r.SlapShield) && r.DamFlow > 0 && r.DamFlow <= 1
                  && r.DamSpill >= 0 && !double.IsInfinity(r.DamSpill) && r.DamBreak > 0 && !double.IsInfinity(r.DamBreak)))
                ok = false;
        }
    }

    public sealed partial class FightRules
    {
        /// <summary>mudwork: a converted heal's shield cap, fraction of max HP (MON_MUD_CAP); a larger cap the unit has wins.</summary>
        public double MudCap { get; set; }

        /// <summary>mudwork: turns a converted heal's shield lasts (MON_MUD_TURNS).</summary>
        public int MudTurns { get; set; } = 1;

        /// <summary>tail_slap: the share of the unit's current shield each landed hit adds to its raw damage (MON_SLAP_SHIELD).</summary>
        public double SlapShield { get; set; }

        /// <summary>holdback: the share of the Reservoir that drains into HP at the start of each own action, in (0, 1] (MON_DAM_FLOW).</summary>
        public double DamFlow { get; set; } = 1;

        /// <summary>holdback: true damage released per point of stored damage a shield washes out (MON_DAM_SPILL).</summary>
        public double DamSpill { get; set; }

        /// <summary>holdback: the dam breaks once the Reservoir holds this x max HP (MON_DAM_BREAK).</summary>
        public double DamBreak { get; set; } = 1;

        static partial void Load_LodgeBeaver(FightRules r, ContentDb c)
        {
            double D(string k, double fallback) => c.ConstantValues.TryGetValue(k, out var v) ? v : fallback;
            r.MudCap = D("MON_MUD_CAP", 0);
            r.MudTurns = (int)D("MON_MUD_TURNS", 1);
            r.SlapShield = D("MON_SLAP_SHIELD", 0);
            r.DamFlow = D("MON_DAM_FLOW", 1);
            r.DamSpill = D("MON_DAM_SPILL", 0);
            r.DamBreak = D("MON_DAM_BREAK", 1);
        }
    }

    public sealed partial class FightUnit
    {
        /// <summary>holdback: HP damage held back behind the dam, not yet taken.</summary>
        public double Reservoir { get; set; }

        /// <summary>holdback: the last enemy that damaged the unit (credited with a death by drain); null before any.</summary>
        public string DamLastHit { get; set; }
    }
}
