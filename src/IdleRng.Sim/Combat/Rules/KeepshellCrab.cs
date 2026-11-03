using System;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Combat
{
    // Species Keepshell Crab (#152): its three rules (specs/16 section 6.2), each reading DEF.
    //   tide_moat   against magical hits the unit mitigates with the higher of its WIS and MON_MOAT_DEF x its DEF
    //   unbreached  a hit on the unit that would deal at most its wall (MON_WALL_DEF x DEF, after mitigation and crit, before
    //               shields) deals 0; each blocked hit lowers the wall by MON_WALL_ERODE of its starting height, so it always falls
    //   shellcrack  the unit's physical hits ignore the target's DEF and add MON_CRACK_DEF x that DEF to the raw damage
    // None of them draws. Only unbreached keeps state (the wall's starting height and the hits it blocked), hashed for carriers.
    public static partial class BattleSim
    {
        /// <summary>tide_moat on a magical hit where the moat beat WIS: Actor is the carrier, Amount the defence it used.</summary>
        public const string MoatEvent = FightAction.RulePrefix + RuleModifiers.TideMoat;

        /// <summary>unbreached blocked a hit: Actor is the carrier, Target the attacker, Amount the wall left after this block.</summary>
        public const string WallEvent = FightAction.RulePrefix + RuleModifiers.Unbreached;

        /// <summary>shellcrack on a physical hit: Actor is the carrier, Target the target, Amount the target's DEF turned into damage.</summary>
        public const string CrackEvent = FightAction.RulePrefix + RuleModifiers.Shellcrack;

        /// <summary>
        /// The conversion step, after lanes A to E: the attacker's shellcrack first (lane A's fortress_fist has already made its basic
        /// attack physical), then the target's tide_moat (it reads the final damage type). Applied once per hit, before every
        /// multiplier, so crit and element scale the cracked damage too.
        /// </summary>
        static partial void ConvertHit_KeepshellCrab(RuleCtx c, HitInfo h)
        {
            var r = c.Rules;
            if (!h.Magical && Has(h.Src, RuleModifiers.Shellcrack))
            {
                // The target's DEF stops nothing (this also drops the DEX iron_reflexes adds, and fire_pierce has nothing left to
                // pierce): it feeds the hit instead.
                double add = r.CrackDef * Eff(r, h.Tgt, StatIds.Def);
                h.Mitigation = 0;
                h.Raw += add;
                if (add > 0) c.Log?.Invoke(new FightAction(h.Src.Id, CrackEvent, h.Tgt.Id, add));
            }
            if (h.Magical && Has(h.Tgt, RuleModifiers.TideMoat))
            {
                double moat = r.MoatDef * Eff(r, h.Tgt, StatIds.Def);
                if (moat > h.Mitigation)
                {
                    h.Mitigation = moat;
                    c.Log?.Invoke(new FightAction(h.Tgt.Id, MoatEvent, h.Src.Id, moat));
                }
            }
        }

        /// <summary>
        /// unbreached, on a hit's damage after mitigation, crit and the tower's multipliers, before the shield: a hit of at most the
        /// wall deals 0 (it still counts as a hit taken, so OnHitTaken triggers fire with 0 damage) and wears the wall down. Hits
        /// that get through do not erode it. Only hits come here: true damage and max-HP damage never meet the wall.
        /// </summary>
        static partial void OnHitMitigated_KeepshellCrab(RuleCtx c, HitInfo h, ref double damage)
        {
            var t = h.Tgt;
            if (!Has(t, RuleModifiers.Unbreached)) return;
            var r = c.Rules;
            if (!t.WallSet)   // the wall's starting height: its DEF at the first hit it meets (passives and buffs included)
            {
                t.WallSet = true;
                t.WallStart = r.WallDef * Eff(r, t, StatIds.Def);
            }
            double wall = WallLeft(r, t);
            if (wall <= 0 || damage > wall) return;
            damage = 0;
            t.WallBlocks++;
            c.Log?.Invoke(new FightAction(t.Id, WallEvent, h.Src.Id, WallLeft(r, t)));
        }

        /// <summary>The wall left: its starting height less MON_WALL_ERODE of it per blocked hit; 0 once 1/MON_WALL_ERODE hits were blocked.</summary>
        static double WallLeft(FightRules r, FightUnit t)
        {
            double left = 1 - r.WallErode * t.WallBlocks;
            return left <= 1e-9 ? 0 : t.WallStart * left;
        }

        static partial void HashUnit_KeepshellCrab(CanonicalWriter w, FightState s, FightUnit u)
        {
            if (Has(u, RuleModifiers.Unbreached)) { w.Bool(u.WallSet); w.F64(u.WallStart); w.I32(u.WallBlocks); }
        }

        static partial void ValidateRules_KeepshellCrab(FightRules r, ref bool ok)
        {
            if (!(r.MoatDef >= 0 && !double.IsInfinity(r.MoatDef) && r.WallDef >= 0 && !double.IsInfinity(r.WallDef)
                  && r.WallErode > 0 && r.WallErode <= 1 && r.CrackDef >= 0 && !double.IsInfinity(r.CrackDef)))
                ok = false;
        }
    }

    public sealed partial class FightRules
    {
        /// <summary>tide_moat: the share of DEF the unit may mitigate magical hits with instead of WIS (MON_MOAT_DEF).</summary>
        public double MoatDef { get; set; }

        /// <summary>unbreached: the wall's starting height, a share of DEF (MON_WALL_DEF).</summary>
        public double WallDef { get; set; }

        /// <summary>unbreached: the share of the wall's starting height each blocked hit takes off, in (0, 1] (MON_WALL_ERODE).</summary>
        public double WallErode { get; set; }

        /// <summary>shellcrack: the share of the target's DEF a physical hit adds to its raw damage (MON_CRACK_DEF).</summary>
        public double CrackDef { get; set; }

        static partial void Load_KeepshellCrab(FightRules r, ContentDb c)
        {
            var k = c.ConstantValues;
            r.MoatDef = k["MON_MOAT_DEF"];
            r.WallDef = k["MON_WALL_DEF"];
            r.WallErode = k["MON_WALL_ERODE"];
            r.CrackDef = k["MON_CRACK_DEF"];
        }
    }

    public sealed partial class FightUnit
    {
        /// <summary>unbreached: the wall's starting height is set (at the first hit the unit meets).</summary>
        public bool WallSet { get; set; }

        /// <summary>unbreached: the wall's starting height, MON_WALL_DEF x DEF at the first hit.</summary>
        public double WallStart { get; set; }

        /// <summary>unbreached: the hits the wall blocked this fight.</summary>
        public int WallBlocks { get; set; }
    }
}
