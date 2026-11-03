using System;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Combat
{
    // Species Aegis Ram (#147): its three rules (specs/16 section 6.2). Its native tree 1 carries lane C's brave_heart.
    //   fleece_aegis  at the end of each turn in which the unit dealt no damage (a Default, a debt skip, a support action), the
    //                 living ally with the least shield (itself in 1v1; ties to the first in unit order) gets MON_FLEECE_SHIELD x
    //                 the unit's DEF of shield, joining its one shield pool up to MON_FLEECE_CAP of its max HP (never lowering it)
    //   brave_debt    after its pips are spent, while a target of its action lives, it borrows up to MON_DEBT_MAX more extra actions
    //                 (pips below 0, under MON_EXTRA_ACTIONS_CAP); below 0 pips each turn is skipped, unguarded, for +1 pip
    //   ram_shock     each pip or debt extra action that lands a hit on an enemy pushes its gauge back by MON_RAM_KNOCK x
    //                 BATTLE_GAUGE x its place in the chain (never below 0) and hits for +MON_RAM_RAMP per earlier one
    // None draws. Pips are hashed for every brave_heart carrier (lane C) and for a brave_debt carrier without it. The rest
    // (turn-start marks, the chain place, the action's target) is set at the start of each turn or action and used within the
    // same Step, so it is not hashed.
    public static partial class BattleSim
    {
        /// <summary>fleece_aegis: Actor = the carrier, Target = the ally shielded, Amount = the shield added.</summary>
        public const string FleeceEvent = FightAction.RulePrefix + RuleModifiers.FleeceAegis;

        /// <summary>
        /// brave_debt: a borrowed action (logged before it, Amount = the pips after borrowing, below 0) or a turn skipped to repay
        /// (Actor = Target = the unit, Amount = the pips after repaying, at most 0).
        /// </summary>
        public const string DebtEvent = FightAction.RulePrefix + RuleModifiers.BraveDebt;

        /// <summary>ram_shock on a landed chain hit: Target = the enemy, Amount = the gauge it lost, as a share of BATTLE_GAUGE.</summary>
        public const string RamShockEvent = FightAction.RulePrefix + RuleModifiers.RamShock;

        static partial void BeforeAction_AegisRam(RuleCtx c, FightUnit u, ref bool skip)
        {
            u.DebtTarget = -1;   // each action aims anew
            if (u.TurnAction != 0) return;
            // A new turn: its marks start over.
            u.RamChain = 0;
            u.BraveTurnActions = u.Tally.Actions;   // brave_debt borrows only after a turn it acted in (lane C sets it for brave_heart too)
            u.FleeceDealt = u.Tally.DamageDealt;
            u.FleeceHit = false;
            if (skip) return;   // an earlier lane took the turn: the debt waits for the next one
            if (u.BravePips < 0)
            {
                skip = true;   // repay: no action, no Default, no guard
                u.BravePips++;
                c.Log?.Invoke(new FightAction(u.Id, DebtEvent, u.Id, u.BravePips));
            }
        }

        /// <summary>ram_shock: the chain place belongs to one action; ExtraAction sets the next one's.</summary>
        static partial void AfterAction_AegisRam(RuleCtx c, FightUnit u) => u.RamPlace = 0;

        /// <summary>
        /// brave_debt after lane C's pips: one more borrowed action while a target of this action lives, down to -MON_DEBT_MAX pips.
        /// ram_shock: a pip or debt action takes the next place in the chain.
        /// </summary>
        static partial void ExtraAction_AegisRam(RuleCtx c, FightUnit u, ref string evt)
        {
            if (evt == null && u.BravePips <= 0 && u.BravePips > -c.Rules.DebtMax && u.Tally.Actions > u.BraveTurnActions
                && TargetLives(c.State, u) && Has(u, RuleModifiers.BraveDebt))
            {
                u.BravePips--;
                evt = DebtEvent;   // Step logs it before the action, with the pips after borrowing
            }
            if ((evt == BraveEvent || evt == DebtEvent) && Has(u, RuleModifiers.RamShock)) u.RamPlace = ++u.RamChain;
        }

        /// <summary>brave_debt: some enemy that this action aimed a hit at is still alive.</summary>
        static bool TargetLives(FightState s, FightUnit u)
        {
            int i = u.DebtTarget;
            return i >= 0 && i < s.Units.Count && s.Units[i].Alive;
        }

        /// <summary>brave_debt: the action's target (the first one that still lives, for an area action), before every roll.</summary>
        static partial void BeforeHitResolves_AegisRam(RuleCtx c, HitInfo h)
        {
            var src = h.Src;
            if (h.Cancelled || h.Triggered || h.Tgt.Side == src.Side || !Has(src, RuleModifiers.BraveDebt)) return;
            if (TargetLives(c.State, src)) return;
            src.DebtTarget = c.State.Units.IndexOf(h.Tgt);
        }

        /// <summary>ram_shock: a chain hit (not a triggered one) on an enemy hits for +MON_RAM_RAMP per earlier place.</summary>
        static partial void ModifyOutgoing_AegisRam(RuleCtx c, HitInfo h)
        {
            var src = h.Src;
            if (src.RamPlace > 1 && !h.Triggered && h.Tgt.Side != src.Side && Has(src, RuleModifiers.RamShock))
                h.Raw *= 1 + c.Rules.RamRamp * (src.RamPlace - 1);
        }

        /// <summary>ram_shock: a landed chain hit pushes the enemy's gauge back; fleece_aegis: a landed hit on an enemy is damage dealt.</summary>
        static partial void OnHitLanded_AegisRam(RuleCtx c, HitInfo h)
        {
            FightUnit src = h.Src, tgt = h.Tgt;
            if (tgt.Side == src.Side) return;
            if (Has(src, RuleModifiers.FleeceAegis)) src.FleeceHit = true;
            if (src.RamPlace <= 0 || h.Triggered || !tgt.Alive || !Has(src, RuleModifiers.RamShock)) return;
            var r = c.Rules;
            double push = Math.Min(Math.Max(0.0, tgt.Gauge), r.RamKnock * r.GaugeSize * src.RamPlace);
            tgt.Gauge -= push;
            c.Log?.Invoke(new FightAction(src.Id, RamShockEvent, tgt.Id, push / r.GaugeSize));
        }

        /// <summary>
        /// fleece_aegis, at the end of a turn (its gauge-paid action and every extra action, or the turn it skipped): if the unit
        /// lives and dealt no damage, the least-shielded living ally gets the fleece shield.
        /// </summary>
        static partial void TurnEnd_AegisRam(RuleCtx c, FightUnit u)
        {
            if (!u.Alive || u.FleeceHit || u.Tally.DamageDealt > u.FleeceDealt || !Has(u, RuleModifiers.FleeceAegis)) return;
            var r = c.Rules;
            FightUnit t = null;
            foreach (var x in c.State.Units)
                if (x.Side == u.Side && x.Alive && (t == null || x.Shield < t.Shield)) t = x;
            if (t == null) return;
            double capFrac = r.FleeceCap;
            HookShieldCap(r, t, ref capFrac);   // barricade's higher cap applies, as for an overheal shield
            double amount = r.FleeceShield * Eff(r, u, StatIds.Def);
            HookShieldIn(c, t, ref amount);   // holdback: the shield washes stored damage out first, as an overheal shield does
            double add = Math.Min(Math.Max(0.0, capFrac * t.MaxHp - t.Shield), amount);
            if (!(add > 0)) return;
            t.Shield += add;
            c.Log?.Invoke(new FightAction(u.Id, FleeceEvent, t.Id, add));
        }

        static partial void HashUnit_AegisRam(CanonicalWriter w, FightState s, FightUnit u)
        {
            if (Has(u, RuleModifiers.BraveDebt) && !Has(u, RuleModifiers.BraveHeart)) w.I32(u.BravePips);   // lane C hashes a brave_heart carrier's
        }

        static partial void ValidateRules_AegisRam(FightRules r, ref bool ok)
        {
            if (!(r.FleeceShield >= 0 && !double.IsInfinity(r.FleeceShield) && In01(r.FleeceCap) && r.DebtMax >= 0 && In01(r.RamKnock)
                  && r.RamRamp >= 0 && !double.IsInfinity(r.RamRamp)))
                ok = false;
        }
    }

    public sealed partial class FightRules
    {
        /// <summary>fleece_aegis: the shield given, a multiple of the unit's DEF (MON_FLEECE_SHIELD).</summary>
        public double FleeceShield { get; set; }

        /// <summary>fleece_aegis: the ally's shield pool is filled up to this share of its max HP (MON_FLEECE_CAP).</summary>
        public double FleeceCap { get; set; }

        /// <summary>brave_debt: the most pips the unit may borrow below 0 (MON_DEBT_MAX).</summary>
        public int DebtMax { get; set; }

        /// <summary>ram_shock: the gauge pushed back per place in the chain, a share of BATTLE_GAUGE (MON_RAM_KNOCK).</summary>
        public double RamKnock { get; set; }

        /// <summary>ram_shock: the damage bonus per earlier place in the chain (MON_RAM_RAMP).</summary>
        public double RamRamp { get; set; }

        static partial void Load_AegisRam(FightRules r, ContentDb c)
        {
            double D(string k) => c.ConstantValues.TryGetValue(k, out var v) ? v : 0;
            r.FleeceShield = D("MON_FLEECE_SHIELD");
            r.FleeceCap = D("MON_FLEECE_CAP");
            r.DebtMax = (int)D("MON_DEBT_MAX");
            r.RamKnock = D("MON_RAM_KNOCK");
            r.RamRamp = D("MON_RAM_RAMP");
        }
    }

    public sealed partial class FightUnit
    {
        /// <summary>brave_debt: the index in FightState.Units of a living enemy the running action aimed at (-1: none). Not hashed.</summary>
        internal int DebtTarget { get; set; } = -1;

        /// <summary>ram_shock: pip and debt actions taken so far this turn. Not hashed: 0 at each turn start.</summary>
        internal int RamChain { get; set; }

        /// <summary>ram_shock: the running action's place in the chain (0: not a pip or debt action). Not hashed: 0 between actions.</summary>
        internal int RamPlace { get; set; }

        /// <summary>fleece_aegis: its damage dealt when the turn started. Not hashed: set at each turn start.</summary>
        internal double FleeceDealt { get; set; }

        /// <summary>fleece_aegis: it landed a hit on an enemy this turn (even one a shield took whole). Not hashed: reset each turn.</summary>
        internal bool FleeceHit { get; set; }
    }
}
