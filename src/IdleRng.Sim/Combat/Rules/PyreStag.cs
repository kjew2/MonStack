using System;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Combat
{
    // Species lane: the Pyre Stag (#142, epic #160), specs/16 section 6.2. Its rules spend its own HP:
    //   pyre_tithe     each action costs MON_TITHE_COST of max HP (never below 1 HP, not damage); its hits deal x(1 + the
    //                  fraction of max HP it is missing)
    //   ash_shroud     at fight start, MON_SHROUD_FRAC of its HP becomes a shield that does not expire (not damage)
    //   pyre_mercy     it cannot be healed: every heal it would receive burns an enemy instead, x MON_MERCY_MULT, as fire damage
    //   ember_eternal  a blow that would kill leaves it at 1 HP and burns the overflow from its max HP; each 1% of max HP burned
    //                  adds MON_EMBER_DMG damage; it dies once the burn would take max HP under MON_EMBER_FLOOR of its start
    // and the cost of its Immolate skill (MON_IMMOLATE_SELF of max HP per cast). An HP cost is never damage: no shield, no
    // HP-damage hook, no trigger, no tally (blood_magic's rule, lane A); it goes to the HpPaid hook. Only pyre_mercy draws (the
    // enemy it burns, in teams).
    public static partial class BattleSim
    {
        /// <summary>The Pyre Stag's Immolate skill: each cast as an action costs MON_IMMOLATE_SELF of the caster's max HP.</summary>
        public const string ImmolateSkill = "immolate";

        /// <summary>pyre_tithe after it paid: Amount is the HP the action cost.</summary>
        public const string TitheEvent = FightAction.RulePrefix + RuleModifiers.PyreTithe;

        /// <summary>ash_shroud at fight start: Amount is the HP turned into shield.</summary>
        public const string ShroudEvent = FightAction.RulePrefix + RuleModifiers.AshShroud;

        /// <summary>pyre_mercy: Amount is the fire damage a heal turned into (Target is the enemy it burned).</summary>
        public const string MercyEvent = FightAction.RulePrefix + RuleModifiers.PyreMercy;

        /// <summary>ember_eternal: Amount is the max HP a killing blow burned instead.</summary>
        public const string EmberEvent = FightAction.RulePrefix + RuleModifiers.EmberEternal;

        /// <summary>ash_shroud: every shroud first, then the log lines, so each line's snapshot already shows all of them.</summary>
        static partial void OnFightStart_PyreStag(RuleCtx c)
        {
            var r = c.Rules;
            if (!(r.ShroudFrac > 0)) return;
            foreach (var u in c.State.Units)
            {
                if (!u.Alive || !Has(u, RuleModifiers.AshShroud)) continue;
                double amt = r.ShroudFrac * u.Hp;
                u.Hp -= amt;
                u.ShroudAmount = amt;
                GainShield(c, u, amt, 0);
            }
            foreach (var u in c.State.Units)
                if (u.ShroudAmount > 0)
                {
                    c.Log?.Invoke(new FightAction(u.Id, ShroudEvent, u.Id, u.ShroudAmount));
                    u.ShroudAmount = 0;
                }
        }

        /// <summary>pyre_tithe: the action's HP cost, paid before it acts (so the action already hits harder).</summary>
        static partial void BeforeAction_PyreStag(RuleCtx c, FightUnit u, ref bool skip)
        {
            if (skip || !u.Alive || !(c.Rules.TitheCost > 0) || !Has(u, RuleModifiers.PyreTithe)) return;
            double pay = HpCost(c.Rules.TitheCost, u);
            if (!(pay > 0)) return;
            u.Hp -= pay;
            HookHpPaid(c, u, pay);
            c.Log?.Invoke(new FightAction(u.Id, TitheEvent, u.Id, pay));
        }

        /// <summary>Immolate: the cast's HP cost (an echo of the cast costs nothing more).</summary>
        static partial void OnSkillUsed_PyreStag(RuleCtx c, FightUnit u, int index, CombatSkill sk)
        {
            if (sk.Id != ImmolateSkill || !u.Alive || !(c.Rules.ImmolateSelf > 0)) return;
            double cost = HpCost(c.Rules.ImmolateSelf, u);
            u.Hp -= cost;
            if (cost > 0) HookHpPaid(c, u, cost);
        }

        /// <summary>An HP cost of frac x max HP, never taking the unit below 1 HP.</summary>
        static double HpCost(double frac, FightUnit u) => Math.Max(0.0, Math.Min(frac * u.MaxHp, u.Hp - 1));

        /// <summary>pyre_tithe and ember_eternal: the carrier's hits deal more the more HP it misses and the more max HP it burned.</summary>
        static partial void ModifyOutgoing_PyreStag(RuleCtx c, HitInfo h) => h.Raw *= PyreMult(c.Rules, h.Src);

        /// <summary>The pyre_tithe x ember_eternal damage multiplier of <paramref name="u"/> (1 without them).</summary>
        static double PyreMult(FightRules r, FightUnit u)
        {
            double m = 1;
            if (Has(u, RuleModifiers.PyreTithe) && u.MaxHp > 0) m *= 1 + Math.Max(0.0, 1 - u.Hp / u.MaxHp);
            if (u.EmberBurned > 0 && Has(u, RuleModifiers.EmberEternal)) m *= 1 + r.EmberDmg * 100 * EmberBurnedFrac(u);
            return m;
        }

        /// <summary>The fraction of its starting max HP ember_eternal has burned (start = max HP now + what it burned).</summary>
        static double EmberBurnedFrac(FightUnit u) => u.EmberBurned / (u.MaxHp + u.EmberBurned);

        /// <summary>pyre_mercy: the heal lands as nothing; its amount waits to burn an enemy at the end of the action.</summary>
        static partial void HealIn_PyreStag(RuleCtx c, FightUnit t, double amount, ref bool taken)
        {
            if (taken || !Has(t, RuleModifiers.PyreMercy)) return;
            taken = true;
            if (amount > 0 && t.Alive) t.MercyPending += amount;
        }

        /// <summary>
        /// pyre_mercy: after any action, each living carrier's pending heals burn one enemy (a taunting one, else random in
        /// teams: one draw) as fire damage: x the fire element edge and its pyre multiplier, mitigated by WIS, through the
        /// shield. Not a hit: no avoid or crit roll and no hit triggers. A dead carrier's pending heals are lost.
        /// </summary>
        static partial void AfterAction_PyreStag(RuleCtx c, FightUnit actor)
        {
            var s = c.State;
            var r = c.Rules;
            for (int i = 0; i < s.Units.Count; i++)
            {
                var u = s.Units[i];
                if (!(u.MercyPending > 0)) continue;
                double amount = u.MercyPending;
                u.MercyPending = 0;
                if (!u.Alive) continue;
                var target = PickEnemy(s, u, c.Rng);
                if (target == null) continue;
                double raw = r.MercyMult * amount * r.ElementMult(RuleModifiers.FireElement, target.Source.Element) * PyreMult(r, u);
                double dealt = Absorb(c, u, target, Mitigate(r, raw, Eff(r, target, StatIds.Wis)), RuleModifiers.FireElement);
                c.Log?.Invoke(new FightAction(u.Id, MercyEvent, target.Id, dealt));
                Reborn(target, c.Log);
            }
        }

        /// <summary>ember_eternal: notes how far past 1 HP a blow goes (the last HP-damage hook), for the would-die hook.</summary>
        static partial void ModifyHpDamage_PyreStag(RuleCtx c, FightUnit src, FightUnit tgt, ref double amount)
        {
            if (!Has(tgt, RuleModifiers.EmberEternal)) return;
            tgt.EmberOverflow = amount >= tgt.Hp ? amount - tgt.Hp + Math.Min(1.0, tgt.MaxHp) : 0;
        }

        /// <summary>
        /// ember_eternal, after root_rebirth and the lanes' saves: the blow's overflow past 1 HP comes out of max HP instead (at
        /// least 1, e.g. an entropy recoil, which reports none) and the unit stays at 1 HP; unless that would take max HP under
        /// MON_EMBER_FLOOR of its start, when it dies.
        /// </summary>
        static partial void OnWouldDie_PyreStag(RuleCtx c, FightUnit src, FightUnit tgt, ref bool saved)
        {
            if (!Has(tgt, RuleModifiers.EmberEternal)) return;
            double burn = Math.Max(1.0, tgt.EmberOverflow);
            tgt.EmberOverflow = 0;
            if (saved) return;
            var r = c.Rules;
            double start = tgt.MaxHp + tgt.EmberBurned, max = tgt.MaxHp - burn;
            if (max < r.EmberFloor * start || max < 1) return;
            tgt.EmberBurned += burn;
            // under the timed HP effects' multiplier, so a later RecalcMaxHp keeps the burn
            tgt.BaseMaxHp -= burn / Math.Max(r.StatFloor, 1 + Bonus(tgt, StatIds.Hp));
            tgt.MaxHp = max;
            tgt.Hp = 1;
            saved = true;
            c.Log?.Invoke(new FightAction(tgt.Id, EmberEvent, tgt.Id, burn));
        }

        static partial void HashUnit_PyreStag(CanonicalWriter w, FightState s, FightUnit u)
        {
            if (Has(u, RuleModifiers.PyreMercy)) w.F64(u.MercyPending);
            if (Has(u, RuleModifiers.EmberEternal)) { w.F64(u.EmberBurned); w.F64(u.EmberOverflow); }
        }

        static partial void ValidateRules_PyreStag(FightRules r, ref bool ok)
        {
            if (!(In01(r.ImmolateSelf) && In01(r.TitheCost) && r.ShroudFrac >= 0 && r.ShroudFrac < 1 && r.MercyMult >= 0
                  && !double.IsInfinity(r.MercyMult) && r.EmberDmg >= 0
                  && !double.IsInfinity(r.EmberDmg) && r.EmberFloor > 0 && r.EmberFloor <= 1))
                ok = false;
        }
    }

    public sealed partial class FightRules
    {
        /// <summary>Immolate: the HP each cast costs its caster, fraction of max HP (MON_IMMOLATE_SELF).</summary>
        public double ImmolateSelf { get; set; }

        /// <summary>pyre_tithe: the HP each action costs, fraction of max HP (MON_TITHE_COST).</summary>
        public double TitheCost { get; set; }

        /// <summary>ash_shroud: the fraction of its HP that becomes shield at fight start (MON_SHROUD_FRAC).</summary>
        public double ShroudFrac { get; set; }

        /// <summary>pyre_mercy: a heal burns for this multiple of its amount, before the element edge and mitigation (MON_MERCY_MULT).</summary>
        public double MercyMult { get; set; } = 1;

        /// <summary>ember_eternal: damage bonus per 1% of starting max HP burned (MON_EMBER_DMG).</summary>
        public double EmberDmg { get; set; }

        /// <summary>ember_eternal: the unit dies once max HP would burn under this fraction of its start (MON_EMBER_FLOOR).</summary>
        public double EmberFloor { get; set; } = 1;

        static partial void Load_PyreStag(FightRules r, ContentDb c)
        {
            double D(string k, double fallback) => c.ConstantValues.TryGetValue(k, out var v) ? v : fallback;
            r.ImmolateSelf = D("MON_IMMOLATE_SELF", 0);
            r.TitheCost = D("MON_TITHE_COST", 0);
            r.ShroudFrac = D("MON_SHROUD_FRAC", 0);
            r.MercyMult = D("MON_MERCY_MULT", 1);
            r.EmberDmg = D("MON_EMBER_DMG", 0);
            r.EmberFloor = D("MON_EMBER_FLOOR", 1);
        }
    }

    public sealed partial class FightUnit
    {
        /// <summary>ash_shroud: the HP shrouded at this fight start, until it is logged. Not hashed (always 0 between steps).</summary>
        internal double ShroudAmount { get; set; }

        /// <summary>pyre_mercy: heals taken this action, to burn an enemy when it ends.</summary>
        public double MercyPending { get; set; }

        /// <summary>ember_eternal: the max HP burned this fight instead of dying.</summary>
        public double EmberBurned { get; set; }

        /// <summary>ember_eternal: how far past 1 HP the last blow went (0 when it did not kill).</summary>
        public double EmberOverflow { get; set; }
    }
}
