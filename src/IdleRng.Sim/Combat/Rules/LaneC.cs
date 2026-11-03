using System;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Combat
{
    // Idea-pool lane C: turn gauge (#136): BeforeAction, AfterAction, ExtraAction (under MON_EXTRA_ACTIONS_CAP), the incoming
    // multipliers and the landed-hit hook (specs/16 section 6.2).
    //   brave_heart   at the start of its turn, at full HP or shielded and under MON_BRAVE_MAX pips: Default (skip, +1 pip,
    //                 MON_BRAVE_GUARD less hit damage until its next turn); else act, then spend its pips as extra actions
    //   one_more      a crit or strong-element hit refunds MON_ONE_MORE of a full gauge, once per action, never past full
    //   guard_break   its crits and strong-element hits strip an enemy's guard pips (MON_BREAK_PIPS); at 0 the enemy loses its
    //                 next action and takes x(1 + MON_BREAK_BONUS) hit damage until it acts again, then its pips refill
    //   latent_power  after its MON_LATENT_ACTIONS-th action, its stats but HP rise by MON_LATENT_BONUS for the rest of the fight
    // None of them draws. State is hashed only for carriers (guard pips: for units with a guard_break carrier on the other side).
    public static partial class BattleSim
    {
        /// <summary>
        /// brave_heart. A Default: Actor = Target = the unit, Amount = the pips it holds now (at least 1). A pip spent: Step logs
        /// it before the extra action, with Amount 0.
        /// </summary>
        public const string BraveEvent = FightAction.RulePrefix + RuleModifiers.BraveHeart;

        /// <summary>one_more after a hit: Amount is the share of a full gauge refunded (at most MON_ONE_MORE).</summary>
        public const string OneMoreEvent = FightAction.RulePrefix + RuleModifiers.OneMore;

        /// <summary>
        /// guard_break. A pip stripped: Actor = the carrier, Target = the enemy, Amount = the pips it has left (0 = it broke).
        /// The action a broken unit loses: Actor = Target = that unit, Amount 0.
        /// </summary>
        public const string BreakEvent = FightAction.RulePrefix + RuleModifiers.GuardBreak;

        /// <summary>latent_power when it wakes: Amount is the stat rise (MON_LATENT_BONUS).</summary>
        public const string LatentEvent = FightAction.RulePrefix + RuleModifiers.LatentPower;

        /// <summary>The source id of latent_power's permanent stat effects.</summary>
        const string LatentSource = RuleModifiers.LatentPower;

        /// <summary>guard_break states: none; broken (it loses its next action); the action is lost (vulnerable until it acts).</summary>
        const int BreakNone = 0, BreakBroken = 1, BreakSpent = 2;

        static partial void BeforeAction_C(RuleCtx c, FightUnit u, ref bool skip)
        {
            var r = c.Rules;
            // one_more: one refund per carrier per action, whoever acts.
            foreach (var x in c.State.Units) if (x.OneMoreUsed) x.OneMoreUsed = false;
            bool brave = Has(u, RuleModifiers.BraveHeart);
            if (brave && u.TurnAction == 0)   // a new turn: the Default's guard ends
            {
                u.BraveGuarded = false;
                u.BraveTurnActions = u.Tally.Actions;
            }
            if (skip) return;   // an earlier lane took the action

            // guard_break: a broken unit loses this action; the next one it takes ends the break and refills its pips.
            if (u.BreakState == BreakBroken)
            {
                u.BreakState = BreakSpent;
                skip = true;
                c.Log?.Invoke(new FightAction(u.Id, BreakEvent, u.Id, 0));
                return;
            }
            if (u.BreakState == BreakSpent)
            {
                u.BreakState = BreakNone;
                u.BreakStripped = 0;
            }

            if (brave && u.TurnAction == 0 && u.BravePips < r.BraveMax && (u.Hp >= u.MaxHp || u.Shield > 0))
            {
                skip = true;   // Default: no action (no cooldowns, no OnAction triggers), +1 pip, guarded until the next turn
                u.BravePips++;
                u.BraveGuarded = true;
                c.Log?.Invoke(new FightAction(u.Id, BraveEvent, u.Id, u.BravePips));
            }
        }

        /// <summary>brave_heart: after an action of a turn in which the unit really acted, each pip it holds is one more action.</summary>
        static partial void ExtraAction_C(RuleCtx c, FightUnit u, ref string evt)
        {
            if (evt != null || u.BravePips <= 0 || !Has(u, RuleModifiers.BraveHeart)) return;
            if (u.Tally.Actions <= u.BraveTurnActions) return;   // it Defaulted or lost the turn: nothing to follow up
            u.BravePips--;
            evt = BraveEvent;
        }

        /// <summary>latent_power: once the unit has taken MON_LATENT_ACTIONS actions, permanent effects raise its stats but HP.</summary>
        static partial void AfterAction_C(RuleCtx c, FightUnit u)
        {
            var r = c.Rules;
            if (!u.Alive || u.Tally.Actions < r.LatentActions || !Has(u, RuleModifiers.LatentPower) || HasEffectFrom(u, LatentSource)) return;
            foreach (var stat in StatIds.Trained)
                if (stat != StatIds.Hp) AddEffect(r, u, stat, r.LatentBonus, -1, LatentSource);
            c.Log?.Invoke(new FightAction(u.Id, LatentEvent, u.Id, r.LatentBonus));
        }

        static partial void ModifyIncoming_C(RuleCtx c, HitInfo h)
        {
            if (h.Tgt.BraveGuarded && Has(h.Tgt, RuleModifiers.BraveHeart)) h.Raw *= 1 - c.Rules.BraveGuard;
            if (h.Tgt.BreakState != BreakNone) h.Raw *= 1 + c.Rules.BreakBonus;
        }

        /// <summary>one_more and guard_break: a landed crit, or a hit whose element is strong against the target.</summary>
        static partial void OnHitLanded_C(RuleCtx c, HitInfo h)
        {
            var r = c.Rules;
            FightUnit src = h.Src, tgt = h.Tgt;
            bool oneMore = Has(src, RuleModifiers.OneMore), guardBreak = Has(src, RuleModifiers.GuardBreak);
            if (!oneMore && !guardBreak) return;
            if (!h.Crit && !(r.ElementMult(h.Element, tgt.Source.Element) > 1)) return;
            if (oneMore && src.Alive && !src.OneMoreUsed && !src.Stealing)   // no refund inside a stolen turn (stolen_turn's loop guard)
            {
                src.OneMoreUsed = true;
                double refund = Math.Min(r.OneMore * r.GaugeSize, Math.Max(0.0, r.GaugeSize - src.Gauge));
                if (refund > 0)
                {
                    src.Gauge += refund;
                    c.Log?.Invoke(new FightAction(src.Id, OneMoreEvent, src.Id, refund / r.GaugeSize));
                }
            }
            if (guardBreak && tgt.Alive && tgt.Side != src.Side && tgt.BreakState == BreakNone)
            {
                tgt.BreakStripped++;
                int left = Math.Max(0, r.BreakPips - tgt.BreakStripped);
                if (left == 0) tgt.BreakState = BreakBroken;
                c.Log?.Invoke(new FightAction(src.Id, BreakEvent, tgt.Id, left));
            }
        }

        static partial void HashUnit_C(CanonicalWriter w, FightState s, FightUnit u)
        {
            if (Has(u, RuleModifiers.BraveHeart)) { w.I32(u.BravePips); w.Bool(u.BraveGuarded); }
            if (Has(u, RuleModifiers.OneMore)) w.Bool(u.OneMoreUsed);
            foreach (var e in s.Units)
                if (e.Side != u.Side && Has(e, RuleModifiers.GuardBreak))
                {
                    w.I32(u.BreakStripped); w.I32(u.BreakState);
                    break;
                }
        }

        static partial void ValidateRules_C(FightRules r, ref bool ok)
        {
            if (!(r.BraveMax >= 0 && In01(r.BraveGuard) && In01(r.OneMore) && r.BreakPips >= 1 && r.BreakBonus >= 0 && r.LatentActions >= 1
                  && r.LatentBonus >= 0)) ok = false;
        }
    }

    public sealed partial class FightUnit
    {
        /// <summary>brave_heart: pips held (each one is an extra action after a turn it acts in).</summary>
        public int BravePips { get; set; }

        /// <summary>brave_heart: it Defaulted this turn, so hits on it deal MON_BRAVE_GUARD less until its next turn.</summary>
        public bool BraveGuarded { get; set; }

        /// <summary>brave_heart: its action count at the start of its turn (it spends pips only after a turn it acted in). Not hashed: set at each turn start.</summary>
        internal int BraveTurnActions { get; set; }

        /// <summary>one_more: the refund was used in the action running now.</summary>
        public bool OneMoreUsed { get; set; }

        /// <summary>guard_break: guard pips the enemy's carriers have stripped from this unit (shared by every carrier).</summary>
        public int BreakStripped { get; set; }

        /// <summary>guard_break: 0 = not broken, 1 = broken (loses its next action), 2 = lost it (takes the bonus until it acts).</summary>
        public int BreakState { get; set; }
    }

    public sealed partial class FightRules
    {
        /// <summary>brave_heart: it Defaults only while holding fewer pips than this (MON_BRAVE_MAX; 0 = never).</summary>
        public int BraveMax { get; set; }

        /// <summary>brave_heart: hit damage reduction after a Default, until its next turn (MON_BRAVE_GUARD).</summary>
        public double BraveGuard { get; set; }

        /// <summary>one_more: the fraction of a full gauge a crit or strong-element hit refunds (MON_ONE_MORE).</summary>
        public double OneMore { get; set; }

        /// <summary>guard_break: guard pips of each enemy (MON_BREAK_PIPS).</summary>
        public int BreakPips { get; set; }

        /// <summary>guard_break: a broken unit takes x(1 + this) hit damage (MON_BREAK_BONUS).</summary>
        public double BreakBonus { get; set; }

        /// <summary>latent_power: the action count after which it wakes (MON_LATENT_ACTIONS).</summary>
        public int LatentActions { get; set; }

        /// <summary>latent_power: the rise of each stat but HP, a fraction of base (MON_LATENT_BONUS).</summary>
        public double LatentBonus { get; set; }

        static partial void Load_C(FightRules r, ContentDb c)
        {
            var k = c.ConstantValues;
            r.BraveMax = (int)k["MON_BRAVE_MAX"];
            r.BraveGuard = k["MON_BRAVE_GUARD"];
            r.OneMore = k["MON_ONE_MORE"];
            r.BreakPips = (int)k["MON_BREAK_PIPS"];
            r.BreakBonus = k["MON_BREAK_BONUS"];
            r.LatentActions = (int)k["MON_LATENT_ACTIONS"];
            r.LatentBonus = k["MON_LATENT_BONUS"];
        }
    }
}
