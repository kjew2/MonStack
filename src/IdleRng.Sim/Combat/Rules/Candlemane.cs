using System;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Combat
{
    // Species Candlemane (#141): its two chase rules (specs/16 section 6.2). Its native tree 1 carries lane A's blood_magic.
    //   martyr_flame  every heal on the unit also deals MON_PYRE_CONV of the HP healed, plus the overheal it would lose, as light
    //                 true damage to its current target
    //   hollow_sun    HP it pays (blood_magic) or loses to hits is stored as Radiance (up to max HP); each hit it lands releases
    //                 MON_SUN_RELEASE of it as extra light true damage and heals it MON_SUN_HEAL of the release
    // Neither draws. Both rules' damage is not a hit (no avoid roll, crit, mitigation, element multiplier, hit hooks or triggers),
    // so it never comes back as a heal: the hollow_sun heal feeds martyr_flame once and stops there. State (hashed for carriers
    // only): PyreTarget and Radiance, value fields, so Clone needs nothing more.
    public static partial class BattleSim
    {
        /// <summary>martyr_flame after a heal: Target took the damage, Amount is the HP converted (before the target's shield).</summary>
        public const string PyreEvent = FightAction.RulePrefix + RuleModifiers.MartyrFlame;

        /// <summary>hollow_sun after a hit: Target took the release, Amount is the Radiance released (before the target's shield).</summary>
        public const string SunEvent = FightAction.RulePrefix + RuleModifiers.HollowSun;

        /// <summary>The element of both rules' damage (tallied as damage_light).</summary>
        const string LightElement = "light";

        /// <summary>martyr_flame: a heal on the unit converts to damage on its current target.</summary>
        static partial void OnHealed_Candlemane(RuleCtx c, FightUnit t, double healed, double lost)
        {
            if (!Has(t, RuleModifiers.MartyrFlame) || !t.Alive || t.PyreBusy) return;
            double amount = c.Rules.PyreConv * healed + lost;
            if (!(amount > 0)) return;
            var target = PyreTarget(c.State, t);
            if (target == null) return;
            t.PyreBusy = true;   // loop guard: nothing it causes may convert again (no path heals the unit today)
            try
            {
                Absorb(c, t, target, amount, LightElement);
                c.Log?.Invoke(new FightAction(t.Id, PyreEvent, target.Id, amount));
                Reborn(target, c.Log);
            }
            finally { t.PyreBusy = false; }
        }

        /// <summary>
        /// martyr_flame's current target: the enemy the unit last landed a hit on, while it lives; else a taunting enemy, else the
        /// first living enemy in unit order (no draw). Null when no enemy is left.
        /// </summary>
        static FightUnit PyreTarget(FightState s, FightUnit u)
        {
            int i = u.PyreTarget;
            if (i >= 0 && i < s.Units.Count && s.Units[i].Alive && s.Units[i].Side != u.Side) return s.Units[i];
            FightUnit first = null;
            foreach (var e in s.Units)
            {
                if (e.Side == u.Side || !e.Alive) continue;
                if (e.TauntTicks > 0) return e;
                first ??= e;
            }
            return first;
        }

        /// <summary>The attacker's side of a landed hit: martyr_flame remembers the target; hollow_sun releases Radiance on it.</summary>
        static partial void OnHitLanded_Candlemane(RuleCtx c, HitInfo h)
        {
            var src = h.Src;
            var tgt = h.Tgt;
            if (src.Side == tgt.Side) return;
            if (Has(src, RuleModifiers.MartyrFlame)) src.PyreTarget = c.State.Units.IndexOf(tgt);
            var r = c.Rules;
            if (!(src.Radiance > 0) || !(r.SunRelease > 0) || !src.Alive || !tgt.Alive || !Has(src, RuleModifiers.HollowSun)) return;
            double release = r.SunRelease * src.Radiance;
            src.Radiance -= release;
            Absorb(c, src, tgt, release, LightElement);
            c.Log?.Invoke(new FightAction(src.Id, SunEvent, tgt.Id, release));
            Reborn(tgt, c.Log);
            if (!src.Alive || !(r.SunHeal > 0)) return;
            double shield = 0;
            src.Tally.HealingDone += HealHooked(c, src, r.SunHeal * release * HealScale(r, c.State), ref shield);
        }

        /// <summary>hollow_sun: the HP a hit took from the unit charges its Radiance.</summary>
        static partial void OnHitTaken_Candlemane(RuleCtx c, HitInfo h)
        {
            if (h.Dealt > 0 && h.Src.Side != h.Tgt.Side && Has(h.Tgt, RuleModifiers.HollowSun)) Charge(h.Tgt, h.Dealt);
        }

        /// <summary>hollow_sun: HP the unit pays (blood_magic) charges its Radiance.</summary>
        static partial void OnHpPaid_Candlemane(RuleCtx c, FightUnit u, double paid)
        {
            if (paid > 0 && Has(u, RuleModifiers.HollowSun)) Charge(u, paid);
        }

        static void Charge(FightUnit u, double amount) => u.Radiance = Math.Min(u.MaxHp, u.Radiance + amount);

        static partial void HashUnit_Candlemane(CanonicalWriter w, FightState s, FightUnit u)
        {
            if (Has(u, RuleModifiers.MartyrFlame)) w.I32(u.PyreTarget);
            if (Has(u, RuleModifiers.HollowSun)) w.F64(u.Radiance);
        }

        static partial void ValidateRules_Candlemane(FightRules r, ref bool ok)
        {
            if (!(r.PyreConv >= 0 && !double.IsInfinity(r.PyreConv) && In01(r.SunRelease) && r.SunHeal >= 0 && !double.IsInfinity(r.SunHeal)))
                ok = false;
        }
    }

    public sealed partial class FightRules
    {
        /// <summary>martyr_flame: the share of the HP healed that converts to damage (MON_PYRE_CONV); overheal converts in full.</summary>
        public double PyreConv { get; set; }

        /// <summary>hollow_sun: the share of the stored Radiance each landed hit releases (MON_SUN_RELEASE).</summary>
        public double SunRelease { get; set; }

        /// <summary>hollow_sun: the share of a release the unit heals (MON_SUN_HEAL).</summary>
        public double SunHeal { get; set; }

        static partial void Load_Candlemane(FightRules r, ContentDb c)
        {
            double D(string k) => c.ConstantValues.TryGetValue(k, out var v) ? v : 0;
            r.PyreConv = D("MON_PYRE_CONV");
            r.SunRelease = D("MON_SUN_RELEASE");
            r.SunHeal = D("MON_SUN_HEAL");
        }
    }

    public sealed partial class FightUnit
    {
        /// <summary>martyr_flame: the index in FightState.Units of the enemy the unit last landed a hit on (-1: none yet).</summary>
        public int PyreTarget { get; set; } = -1;

        /// <summary>martyr_flame: a conversion is running (loop guard; never true between steps, so not hashed).</summary>
        internal bool PyreBusy { get; set; }

        /// <summary>hollow_sun: the stored Radiance, at most max HP.</summary>
        public double Radiance { get; set; }
    }
}
