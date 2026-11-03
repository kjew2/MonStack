using System;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Combat
{
    // Idea-pool lane A: conversion (#134): the conversion step, ModifyOutgoing, skill readiness and cost (specs/16 section 6.2).
    //   iron_reflexes     the target's evade and graze chance are 0; physical hits are mitigated by DEF + MON_IRON_DEX x DEX
    //   elemental_avatar  the attacker's hits take its own element, then x(1 + MON_AVATAR_BONUS)
    //   fortress_fist     the attacker's STR and INT count as 0 for damage; each hit adds MON_FORTRESS_DEF x its DEF
    //   blood_magic       no cooldowns on active skills; each cast costs MON_BLOOD_COST of max HP; no cast under MON_BLOOD_FLOOR
    // None of them draws or keeps state, so nothing is hashed. The always-on conversions are logged once at the start of each
    // fight with their key number; blood_magic is logged at each cast with the HP it cost.
    public static partial class BattleSim
    {
        /// <summary>iron_reflexes at fight start: Amount is the DEF its DEX adds against physical hits now.</summary>
        public const string IronEvent = FightAction.RulePrefix + RuleModifiers.IronReflexes;

        /// <summary>elemental_avatar at fight start: Amount is the damage bonus (MON_AVATAR_BONUS).</summary>
        public const string AvatarEvent = FightAction.RulePrefix + RuleModifiers.ElementalAvatar;

        /// <summary>fortress_fist at fight start: Amount is the raw damage each of its hits adds now.</summary>
        public const string FortressEvent = FightAction.RulePrefix + RuleModifiers.FortressFist;

        /// <summary>blood_magic after a cast: Amount is the HP the cast cost.</summary>
        public const string BloodEvent = FightAction.RulePrefix + RuleModifiers.BloodMagic;

        static partial void OnFightStart_A(RuleCtx c)
        {
            if (c.Log == null) return;
            var r = c.Rules;
            foreach (var u in c.State.Units)
            {
                if (!u.Alive) continue;
                if (Has(u, RuleModifiers.IronReflexes)) c.Log(new FightAction(u.Id, IronEvent, u.Id, r.IronDex * Eff(r, u, StatIds.Dex)));
                if (Avatar(u)) c.Log(new FightAction(u.Id, AvatarEvent, u.Id, r.AvatarBonus));
                if (Has(u, RuleModifiers.FortressFist)) c.Log(new FightAction(u.Id, FortressEvent, u.Id, r.FortressDef * Eff(r, u, StatIds.Def)));
            }
        }

        /// <summary>
        /// The conversion step: the attacker's conversions first (fortress_fist may make a basic attack physical, elemental_avatar
        /// sets the element), then the target's (iron_reflexes reads the final damage type).
        /// </summary>
        static partial void ConvertHit_A(RuleCtx c, HitInfo h)
        {
            var r = c.Rules;
            if (Has(h.Src, RuleModifiers.FortressFist))
            {
                // STR and INT count as 0 for damage: a basic attack (the higher of the two) and a skill scaling either bring no
                // damage of their own, and a basic attack is physical (INT is not above STR). Other scalings keep their damage.
                if (h.Skill == null)
                {
                    h.Raw = 0;
                    if (h.Magical)
                    {
                        h.Magical = false;
                        h.Mitigation = Eff(r, h.Tgt, StatIds.Def);
                    }
                }
                else if (h.Skill.Scales == StatIds.Str || h.Skill.Scales == StatIds.Int) h.Raw = 0;
                h.Raw += r.FortressDef * Eff(r, h.Src, StatIds.Def) * h.FlatShare;   // a fountain_jets jet takes its share
            }
            if (Avatar(h.Src)) h.Element = h.Src.Source.Element;
            if (Has(h.Tgt, RuleModifiers.IronReflexes))
            {
                h.Evade = 0;   // no evade and no graze, so umbral_veil's bonus and cap have nothing to act on
                if (!h.Magical) h.Mitigation = Eff(r, h.Tgt, StatIds.Def) + r.IronDex * Eff(r, h.Tgt, StatIds.Dex);
            }
        }

        static partial void ModifyOutgoing_A(RuleCtx c, HitInfo h)
        {
            if (Avatar(h.Src)) h.Raw *= 1 + c.Rules.AvatarBonus;
        }

        /// <summary>elemental_avatar acts only for a unit with an element.</summary>
        static bool Avatar(FightUnit u) => u.Source.Element != null && Has(u, RuleModifiers.ElementalAvatar);

        /// <summary>
        /// blood_magic: an active skill is ready whatever its cooldown, unless the cast would leave the unit under the floor. A heal
        /// on itself alone is ready only while it would restore at least twice what the cast costs (#141): with no cooldown, a heal
        /// that nets less than one cast's cost is cast again and again and the unit stops fighting.
        /// </summary>
        static partial void SkillReady_A(RuleCtx c, FightUnit u, int index, ref bool ready)
        {
            var sk = u.Source.Skills[index];
            if (!Has(u, RuleModifiers.BloodMagic) || sk.Kind == SkillKind.Passive) return;
            double cost = BloodCost(c.Rules, u);
            ready = u.Hp - cost >= c.Rules.BloodFloor * u.MaxHp
                    && !(sk.Kind == SkillKind.Heal && sk.Target == SkillTarget.Self && Scaled(c.Rules, u, sk) * HealScale(c.Rules, c.State) < 2 * cost);
        }

        /// <summary>blood_magic: the cast leaves no cooldown and costs HP (not damage: no shield, hooks, triggers or tallies).</summary>
        static partial void OnSkillUsed_A(RuleCtx c, FightUnit u, int index, CombatSkill sk)
        {
            if (!Has(u, RuleModifiers.BloodMagic)) return;
            u.Cooldowns[index] = 0;
            if (!u.Alive) return;
            double cost = BloodCost(c.Rules, u);
            u.Hp -= cost;
            c.Log?.Invoke(new FightAction(u.Id, BloodEvent, u.Id, cost));
            if (cost > 0) HookHpPaid(c, u, cost);   // species rules: Candlemane's hollow_sun stores it
        }

        /// <summary>The HP a blood_magic cast costs now: MON_BLOOD_COST of max HP, but never taking the unit below 1 HP.</summary>
        static double BloodCost(FightRules r, FightUnit u) => Math.Max(0.0, Math.Min(r.BloodCost * u.MaxHp, u.Hp - 1));

        static partial void ValidateRules_A(FightRules r, ref bool ok)
        {
            if (!(r.IronDex >= 0 && r.AvatarBonus >= 0 && r.FortressDef >= 0 && In01(r.BloodCost) && In01(r.BloodFloor))) ok = false;
        }
    }

    public sealed partial class FightRules
    {
        /// <summary>iron_reflexes: the share of DEX added to DEF against physical hits (MON_IRON_DEX).</summary>
        public double IronDex { get; set; }

        /// <summary>elemental_avatar: the damage bonus of the unit's hits (MON_AVATAR_BONUS).</summary>
        public double AvatarBonus { get; set; }

        /// <summary>fortress_fist: the share of DEF each hit adds to its raw damage (MON_FORTRESS_DEF).</summary>
        public double FortressDef { get; set; }

        /// <summary>blood_magic: a cast's HP cost, fraction of max HP (MON_BLOOD_COST).</summary>
        public double BloodCost { get; set; }

        /// <summary>blood_magic: the unit does not cast when the cost would leave it under this fraction of max HP (MON_BLOOD_FLOOR).</summary>
        public double BloodFloor { get; set; }

        static partial void Load_A(FightRules r, ContentDb c)
        {
            var k = c.ConstantValues;
            r.IronDex = k["MON_IRON_DEX"];
            r.AvatarBonus = k["MON_AVATAR_BONUS"];
            r.FortressDef = k["MON_FORTRESS_DEF"];
            r.BloodCost = k["MON_BLOOD_COST"];
            r.BloodFloor = k["MON_BLOOD_FLOOR"];
        }
    }
}
