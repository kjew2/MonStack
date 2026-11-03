using System;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;

namespace IdleRng.Sim.Combat
{
    // Species Fountain Axolotl (#150): its three rules (specs/16 section 6.2), called from BattleSim.Hit, Use and BasicAttack.
    //   clear_spring   the weak matchup of the unit's own element is gone both ways (1 instead of ELEMENT_WEAK_MULT on its hits of
    //                  its element, 1 instead of ELEMENT_STRONG_MULT on hits that beat its element); strong matchups still count
    //   seep           its hits are mitigated by the lower of the target's DEF and WIS; MON_SEEP_SHIELD of each passes shields
    //   fountain_jets  each single-target hit of its active skills and basic attacks splits into MON_JETS jets of MON_JET_POWER
    // None of them draws a number of its own (a jet's rolls are the rolls of a hit) or keeps state, so nothing is hashed.
    public static partial class BattleSim
    {
        /// <summary>clear_spring changed a hit's element multiplier: Actor is the carrier, Amount the multiplier it cancelled.</summary>
        public const string ClearSpringEvent = FightAction.RulePrefix + RuleModifiers.ClearSpring;

        /// <summary>seep changed a hit (a lower defence, or damage past a shield): Amount is the defence that mitigated it.</summary>
        public const string SeepEvent = FightAction.RulePrefix + RuleModifiers.Seep;

        /// <summary>fountain_jets after a split hit: Amount is how many of the MON_JETS jets landed (not evaded or taken by a decoy).</summary>
        public const string JetsEvent = FightAction.RulePrefix + RuleModifiers.FountainJets;

        /// <summary>
        /// A single-target hit of an active skill or a basic attack: one hit, or MON_JETS jets for a fountain_jets carrier. Triggered
        /// hits never split (the wave 0 loop guard); an echo or a corrupted recast is part of the cast and splits at its own power.
        /// Each jet is a whole hit: its own decoy, avoid, crit and fortune rolls, hooks, triggers and tallies. Flat per-hit additions
        /// are split across the jets. Jets stop when the target or the attacker falls; the ones left are lost.
        /// </summary>
        static void Strike(FightRules r, FightState s, FightUnit src, FightUnit tgt, double raw, bool magical, string element,
                           CombatSkill sk, bool triggered, IRandomSource rng, Action<FightAction> onAction)
        {
            if (triggered || s.TriggerDepth > 0 || !Has(src, RuleModifiers.FountainJets))
            {
                Hit(r, s, src, tgt, raw, magical, element, sk, false, rng, onAction);
                return;
            }
            int jets = r.Jets, landed = 0;
            for (int j = 0; j < jets && src.Alive && tgt.Alive; j++)
            {
                Hit(r, s, src, tgt, raw * r.JetPower, magical, element, sk, false, rng, onAction, 1.0 / jets, out bool ok);
                if (ok) landed++;
            }
            onAction?.Invoke(new FightAction(src.Id, JetsEvent, tgt.Id, landed));
        }

        /// <summary>
        /// seep, after every conversion: the hit is mitigated by the lower of the target's DEF and WIS (this also drops the DEX that
        /// iron_reflexes adds against physical hits). Returns the defence used, or -1 when the attacker does not carry seep.
        /// </summary>
        static double SeepConvert(RuleCtx c, HitInfo h)
        {
            if (!Has(h.Src, RuleModifiers.Seep)) return -1;
            var r = c.Rules;
            double low = Math.Min(Eff(r, h.Tgt, StatIds.Def), Eff(r, h.Tgt, StatIds.Wis));
            if (low < h.Mitigation)
            {
                h.Mitigation = low;
                h.SeepLowered = true;
            }
            return h.Mitigation;
        }

        /// <summary>seep: the part of a hit's damage that passes the target's shield straight to HP.</summary>
        static double SeepPass(FightRules r, double damage) => r.SeepShield * damage;

        /// <summary>seep's log line, once per hit it changed: a lower defence, or damage the shield would have taken.</summary>
        static void LogSeep(RuleCtx c, HitInfo h, double defence, double shieldBefore, double damage)
        {
            if (c.Log == null) return;
            double bypassed = Math.Min(shieldBefore, damage) - (shieldBefore - h.Tgt.Shield);
            if (h.SeepLowered || bypassed > 1e-9) c.Log(new FightAction(h.Src.Id, SeepEvent, h.Tgt.Id, defence));
        }

        /// <summary>
        /// clear_spring on a hit's element multiplier: an attacker carrier's hit of its own element on the element that beats it uses
        /// 1, and a hit of the element that beats a target carrier's element uses 1. Light and dark beat each other: their strong
        /// matchup wins (BattleSim's order), so a light or dark carrier keeps its strong hits and loses the strong hits it takes.
        /// </summary>
        static double ClearSpringMult(RuleCtx c, FightUnit src, FightUnit tgt, string element, double mult)
        {
            if (element == null || mult == 1.0) return mult;
            var r = c.Rules;
            string tEl = tgt.Source.Element;
            bool strong = Beats(r, element, tEl);
            if (!strong && Beats(r, tEl, element) && element == src.Source.Element && Has(src, RuleModifiers.ClearSpring))
            {
                c.Log?.Invoke(new FightAction(src.Id, ClearSpringEvent, tgt.Id, mult));
                return 1.0;
            }
            if (strong && Has(tgt, RuleModifiers.ClearSpring))
            {
                c.Log?.Invoke(new FightAction(tgt.Id, ClearSpringEvent, src.Id, mult));
                return 1.0;
            }
            return mult;
        }

        /// <summary>Element <paramref name="a"/> is strong against element <paramref name="b"/>.</summary>
        static bool Beats(FightRules r, string a, string b) => a != null && b != null && r.StrongVs.TryGetValue(a, out var x) && x == b;

        static void ValidateRules_FountainAxolotl(FightRules r, ref bool ok)
        {
            if (!(In01(r.SeepShield) && r.Jets >= 2 && r.Jets <= 6 && In01(r.JetPower))) ok = false;
        }
    }

    public sealed partial class FightRules
    {
        /// <summary>seep: the fraction of each hit that passes through shields straight to HP (MON_SEEP_SHIELD).</summary>
        public double SeepShield { get; set; }

        /// <summary>fountain_jets: the jets each split hit becomes (MON_JETS, 2 to 6).</summary>
        public int Jets { get; set; }

        /// <summary>fountain_jets: each jet's share of the hit's power (MON_JET_POWER).</summary>
        public double JetPower { get; set; }

        static void Load_FountainAxolotl(FightRules r, ContentDb c)
        {
            var k = c.ConstantValues;
            r.SeepShield = k["MON_SEEP_SHIELD"];
            r.Jets = (int)k["MON_JETS"];
            r.JetPower = k["MON_JET_POWER"];
        }
    }
}
