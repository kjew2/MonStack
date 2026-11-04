using System;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Combat
{
    // Lineage milestone effects never launch hits or grant actions. Window is measured in the carrier's own actions.
    public static partial class BattleSim
    {
        static bool LB(FightUnit u, string tree, bool x = false) => Has(u, "lineage_" + tree + (x ? "_x" : "_v"));
        static double LBGain(RuleCtx c, double value) => Math.Min(c.Rules.LineageCap, value + c.Rules.LineageGain);
        static void LBLog(RuleCtx c, FightUnit u, string tree, double amount, bool x = false, FightUnit target = null) =>
            c.Log?.Invoke(new FightAction(u.Id, "rule:lineage_" + tree + (x ? "_x" : "_v"), (target ?? u).Id, amount));
        static FightUnit LBPartner(RuleCtx c, FightUnit u)
        {
            FightUnit best = u;
            foreach (var ally in c.State.Units)
                if (ally.Alive && ally.Side == u.Side && ally != u && (best == u || ally.Hp / ally.MaxHp < best.Hp / best.MaxHp)) best = ally;
            return best;
        }
        static void LBHeal(RuleCtx c, FightUnit src, FightUnit target, double amount, string tree, bool x = false)
        {
            if (!target.Alive || amount <= 0) return;
            double shield = 0;
            // Use the common heal-in path; the flag prevents reserve payouts from charging another reserve.
            src.LBPaying = true;
            try { double healed = HealHooked(c, target, amount * HealScale(c.Rules, c.State), ref shield); src.Tally.HealingDone += healed; LBLog(c, src, tree, healed, x, target); }
            finally { src.LBPaying = false; }
        }
        static void LineageBasicConvert(RuleCtx c, HitInfo h)
        {
            var u = h.Tgt;
            bool shieldTracked = LB(u, "sage_mind") || LB(u, "bulwark");
            foreach (var ally in c.State.Units)
            {
                if (ally.Alive && ally.Side == u.Side && (LB(ally, "sage_mind", true) || LB(ally, "bulwark", true))) shieldTracked = true;
                if (ally.Alive && ally.Side == h.Src.Side && LB(ally, "fortune", true) && ally.LBFortuneTarget == h.Src.Id)
                { h.CritBonus += c.Rules.LineageGain; ally.LBFortuneTarget = null; LBLog(c, ally, "fortune", c.Rules.LineageGain, true, h.Src); }
            }
            if (shieldTracked) u.LBShieldBefore = u.Shield + u.Ward;
            if (LB(u, "nimble_feet")) h.Evade = Math.Min(c.Rules.EvadeCap, h.Evade + u.LBEvasion);
        }
        static void LineageBasicOutgoing(RuleCtx c, HitInfo h)
        {
            if (h.Triggered) return;
            var u = h.Src; var r = c.Rules; bool basic = h.Skill == null;
            if (LB(u, "brute_force") && basic && u.LBHeavy >= r.LineageCap - 1e-9)
            {
                h.Raw *= 1 + u.LBHeavy;
                if (LB(u, "brute_force", true)) { double removed = Math.Min(h.Tgt.Shield, h.Raw * r.LineageGain); h.Tgt.Shield -= removed; h.Raw += removed; LBLog(c, u, "brute_force", removed, true, h.Tgt); }
                LBLog(c, u, "brute_force", u.LBHeavy); u.LBHeavy = -r.LineageGain;
            }
            if (LB(u, "spellcraft") && !basic && h.Magical)
            {
                double charge = u.LBArcane;
                if (LB(u, "spellcraft", true) && u.LBLastSpell != null && u.LBLastSpell != h.Skill.Id) { charge = Math.Min(r.LineageCap, charge + r.LineageGain); LBLog(c, u, "spellcraft", charge, true); }
                h.Raw *= 1 + charge; if (charge > 0) LBLog(c, u, "spellcraft", charge); u.LBArcane = 0;
            }
            if (LB(u, "sage_mind") && u.LBSage > 0 && h.Skill?.Scales == StatIds.Wis) { h.Raw *= 1 + u.LBSage; LBLog(c, u, "sage_mind", u.LBSage); u.LBSage = 0; }
            if (LB(u, "bulwark") && u.LBBulwark > 0 && h.Skill?.Scales == StatIds.Def) { h.Raw *= 1 + u.LBBulwark; LBLog(c, u, "bulwark", u.LBBulwark); u.LBBulwark = 0; }
            if (LB(u, "battle_instinct") && u.LBReadTarget != null)
            {
                bool same = u.LBReadTarget == h.Tgt.Id;
                if (same || LB(u, "battle_instinct", true)) { h.Raw *= 1 + (same ? u.LBRead : u.LBRead * r.LineageGain); LBLog(c, u, "battle_instinct", u.LBRead, !same); }
                u.LBRead = 0;
            }
            if (LB(u, "lifeblood") && h.Skill?.Scales == StatIds.Hp) { h.Raw *= 1 + u.LBLife; u.LBLifeSpent = u.LBLife; if (u.LBLife > 0) LBLog(c, u, "lifeblood", u.LBLife); u.LBLife = 0; }
            if (LB(u, "berserker"))
            {
                h.Raw *= 1 + u.LBFury; if (u.LBFury > 0) LBLog(c, u, "berserker", u.LBFury);
                if (LB(u, "berserker", true) && u.LBFury >= r.LineageCap - 1e-9) { h.Raw *= 1 + r.LineageGain; u.LBFury = 0; u.LBVulnerable = r.LineageWindow + 1; LBLog(c, u, "berserker", r.LineageGain, true); }
            }
            if (LB(u, "runeweaver") && !basic && h.Magical)
            {
                h.Raw *= 1 + u.LBRune; if (u.LBRune > 0) LBLog(c, u, "runeweaver", u.LBRune);
                if (LB(u, "runeweaver", true) && u.LBRune > 0) { h.Mitigation *= 1 - u.LBRune; LBLog(c, u, "runeweaver", u.LBRune, true); }
                u.LBRune = 0;
            }
            if (LB(u, "hermit", true) && h.Skill?.Scales == StatIds.Wis) { h.Raw *= 1 + u.LBHermit; LBLog(c, u, "hermit", u.LBHermit, true); u.LBHermit = 0; }
            if (LB(u, "duelist"))
            {
                if (u.LBDuelTarget != h.Tgt.Id) u.LBDuel *= LB(u, "duelist", true) ? 1 - r.LineageGain : r.LineageGain;
                h.Raw *= 1 + u.LBDuel; if (u.LBDuel > 0) LBLog(c, u, "duelist", u.LBDuel);
                if (LB(u, "duelist", true) && u.LBDuel >= r.LineageCap - 1e-9) { h.Raw *= 1 + r.LineageGain; u.LBDuel = 0; LBLog(c, u, "duelist", r.LineageGain, true); }
                u.LBDuelTarget = h.Tgt.Id;
            }
            if (LB(u, "stonewall", true) && h.Skill?.Scales == StatIds.Def && u.LBStone > 0) { h.Raw *= 1 + u.LBStone; LBLog(c, u, "stonewall", u.LBStone, true); u.LBStone = 0; }
            if (LB(u, "quickstep", true) && !basic && u.LBQuick > 0) { u.Gauge += Math.Min(r.GaugeSize * u.LBQuick, Math.Max(0, r.GaugeSize - u.Gauge)); LBLog(c, u, "quickstep", u.LBQuick, true); u.LBQuick = 0; }
            if (LB(u, "lucky_star") && u.LBWindfall >= r.LineageCap - 1e-9) { h.Raw *= 1 + u.LBWindfall; LBLog(c, u, "lucky_star", u.LBWindfall); u.LBWindfall = 0; }
        }
        static void LineageBasicIncoming(RuleCtx c, HitInfo h)
        {
            var u = h.Tgt;
            if (LB(u, "stonewall") && !h.Magical) h.Raw *= 1 - u.LBStone;
            if (LB(u, "survivor", true) && u.LBSurviveFrom == h.Src.Id && u.LBSurviveWindow > 0) h.Raw *= 1 - c.Rules.LineageGain;
            if (LB(u, "berserker", true) && u.LBVulnerable > 0) h.Raw *= 1 + c.Rules.LineageGain;
            foreach (var source in c.State.Units)
                if (source.Alive && source.Side == u.Side && source.LBOpeningTarget == u.Id && source.LBOpeningWindow > 0 && LB(source, "nimble_feet", true)) h.Raw *= 1 - c.Rules.LineageGain;
        }
        static void LineageBasicHitLanded(RuleCtx c, HitInfo h)
        {
            if (h.Triggered) return;
            var u = h.Src;
            if (h.Skill == null)
            {
                if (LB(u, "brute_force")) u.LBHeavy = LBGain(c, u.LBHeavy);
                if (LB(u, "runeweaver")) u.LBRune = LBGain(c, u.LBRune);
                if (LB(u, "quickstep")) { u.LBQuick = LBGain(c, u.LBQuick); LBLog(c, u, "quickstep", u.LBQuick); }
            }
            if (LB(u, "battle_instinct")) { u.LBReadTarget = h.Tgt.Id; u.LBRead = h.Crit ? 0 : LBGain(c, u.LBRead); }
            if (LB(u, "duelist")) u.LBDuel = LBGain(c, u.LBDuel);
            if (LB(u, "lucky_star") && h.Crit) { u.LBWindfall = LBGain(c, u.LBWindfall); LBLog(c, u, "lucky_star", u.LBWindfall); }
            if (LB(u, "lifeblood", true) && u.LBLifeSpent > 0) { double paid = Math.Min(h.Dealt * u.LBLifeSpent, u.MaxHp * c.Rules.LineageGain); u.LBLifeSpent = 0; LBHeal(c, u, u, paid, "lifeblood", true); }
        }
        static void LineageBasicHitTaken(RuleCtx c, HitInfo h)
        {
            var u = h.Tgt; var r = c.Rules;
            if (LB(u, "nimble_feet")) { u.LBEvasion = LBGain(c, u.LBEvasion); LBLog(c, u, "nimble_feet", u.LBEvasion); }
            double absorbed = Math.Max(0, u.LBShieldBefore - u.Shield - u.Ward);
            if (absorbed > 0)
            {
                if (LB(u, "sage_mind")) u.LBSage = LBGain(c, u.LBSage);
                if (LB(u, "bulwark")) u.LBBulwark = LBGain(c, u.LBBulwark);
                foreach (var ally in c.State.Units)
                    if (ally.Alive && ally.Side == u.Side && ally != u)
                    {
                        if (LB(ally, "sage_mind", true)) ally.LBSage = LBGain(c, ally.LBSage);
                        if (LB(ally, "bulwark", true)) ally.LBBulwark = LBGain(c, ally.LBBulwark);
                    }
            }
            if (h.Dealt > 0)
            {
                if (LB(u, "survivor")) { u.LBRecovery = Math.Min(u.MaxHp * r.LineageCap, u.LBRecovery + h.Dealt * r.LineageGain); u.LBSurviveFrom = h.Src.Id; u.LBSurviveWindow = r.LineageWindow; }
                if (LB(u, "berserker")) u.LBFury = Math.Min(r.LineageCap, u.LBFury + h.Dealt / u.MaxHp);
                if (LB(u, "stonewall") && !h.Magical) { u.LBStone = LBGain(c, u.LBStone); u.LBStoneWindow = r.LineageWindow; LBLog(c, u, "stonewall", u.LBStone); }
            }
        }
        static void LineageBasicHealed(RuleCtx c, FightUnit t, double healed, double lost)
        {
            bool paying = false; foreach (var u in c.State.Units) if (u.LBPaying) paying = true;
            if (!paying && LB(t, "vital_core")) t.LBHeart = Math.Min(t.MaxHp * c.Rules.LineageCap, t.LBHeart + lost);
            if (!paying && LB(t, "lifeblood")) t.LBLife = Math.Min(c.Rules.LineageCap, t.LBLife + healed / t.MaxHp);
            if (LB(t, "berserker")) t.LBFury = Math.Max(0, t.LBFury - healed / t.MaxHp);
            if (!paying && healed > 0 && LB(t, "hermit")) t.LBHermit = 0;
        }
        static void LineageBasicSkillUsed(RuleCtx c, FightUnit u, int index, CombatSkill sk)
        {
            if (LB(u, "spellcraft") && sk.Kind == SkillKind.Damage) { u.LBArcane = LBGain(c, u.LBArcane); u.LBLastSpell = sk.Id; }
            if (LB(u, "quickstep")) u.LBQuick = 0;
            if (LB(u, "lucky_star", true) && (sk.Kind == SkillKind.Heal || sk.Kind == SkillKind.Shield) && u.LBWindfall > 0)
            {
                var ally = LBPartner(c, u); double amount = ally.MaxHp * u.LBWindfall * c.Rules.LineageGain; u.LBWindfall = 0;
                if (sk.Kind == SkillKind.Heal) LBHeal(c, u, ally, amount, "lucky_star", true);
                else { double add = Math.Min(amount, Math.Max(0, ally.MaxHp * c.Rules.LineageCap - ally.Shield)); ally.Shield += add; LBLog(c, u, "lucky_star", add, true, ally); }
            }
        }
        static void LineageBasicGaugeTick(RuleCtx c, FightUnit u, ref double fill)
        {
            if (LB(u, "overclock") && u.LBOverdriveRecovery == 0) fill *= 1 + u.LBOverdrive;
            if (LB(u, "quickstep")) fill *= 1 + u.LBQuick;
        }
        static void LineageBasicAfterAction(RuleCtx c, FightUnit u)
        {
            var r = c.Rules;
            if (LB(u, "vital_core") && u.LBHeart > 0)
            {
                var target = LB(u, "vital_core", true) ? LBPartner(c, u) : u;
                if (u.Hp / u.MaxHp < target.Hp / target.MaxHp) target = u;
                double paid = Math.Min(u.LBHeart, Math.Min(target.MaxHp - target.Hp, u.MaxHp * r.LineageGain));
                u.LBHeart -= paid; LBHeal(c, u, target, paid, "vital_core", target != u);
            }
            if (LB(u, "survivor") && u.LBRecovery > 0) { double paid = Math.Min(u.LBRecovery, Math.Min(u.MaxHp - u.Hp, u.MaxHp * r.LineageGain)); u.LBRecovery -= paid; LBHeal(c, u, u, paid, "survivor"); }
            if (LB(u, "hermit")) { u.LBHermit = LBGain(c, u.LBHermit); LBHeal(c, u, u, u.MaxHp * u.LBHermit * r.LineageGain, "hermit"); }
            if (LB(u, "overclock"))
            {
                if (u.LBOverdriveRecovery > 0) u.LBOverdriveRecovery--;
                else if (u.LBOverdrive >= r.LineageCap - 1e-9)
                {
                    if (LB(u, "overclock", true)) { var ally = LBPartner(c, u); ally.Gauge += Math.Min(r.GaugeSize * u.LBOverdrive, Math.Max(0, r.GaugeSize - ally.Gauge)); LBLog(c, u, "overclock", u.LBOverdrive, true, ally); }
                    u.LBOverdrive = 0; u.LBOverdriveRecovery = r.LineageWindow;
                }
                else { u.LBOverdrive = LBGain(c, u.LBOverdrive); LBLog(c, u, "overclock", u.LBOverdrive); }
            }
            if (LB(u, "nimble_feet") && u.Tally.Evades > u.LBSeenEvades)
            {
                u.LBEvasion = 0;
                if (LB(u, "nimble_feet", true)) { var ally = LBPartner(c, u); u.LBOpeningTarget = ally.Id; u.LBOpeningWindow = r.LineageWindow + 1; LBLog(c, u, "nimble_feet", r.LineageGain, true, ally); }
                u.LBSeenEvades = u.Tally.Evades;
            }
            if (LB(u, "fortune", true) && u.FortuneBank >= r.FortuneBankCap - 1e-9)
            {
                FightUnit ally = null;
                foreach (var candidate in c.State.Units)
                {
                    if (!candidate.Alive || candidate.Side != u.Side || candidate == u) continue;
                    double chance = CritChance(r, Eff(r, candidate, StatIds.Luk), Bonus(candidate, StatIds.CritRate) + (r.Mutators?.CritBonus ?? 0));
                    if (chance >= r.CritCap) continue;
                    if (ally == null || candidate.Hp / candidate.MaxHp < ally.Hp / ally.MaxHp) ally = candidate;
                }
                // Keep the V bank when no ally can use the gift, including solo fights.
                // The donor owns the pending gift; allies need no lineage rule themselves.
                if (ally != null && u.LBFortuneTarget == null)
                { u.FortuneBank = 0; u.LBFortuneTarget = ally.Id; LBLog(c, u, "fortune", r.LineageGain, true, ally); }
            }
            if (u.LBVulnerable > 0) u.LBVulnerable--;
            if (u.LBSurviveWindow > 0) u.LBSurviveWindow--;
            if (u.LBOpeningWindow > 0) u.LBOpeningWindow--;
            if (u.LBStoneWindow > 0 && --u.LBStoneWindow == 0) u.LBStone = 0;
        }
        static void LineageBasicHash(CanonicalWriter w, FightUnit u)
        {
            if (LB(u,"fortune",true)) w.Str(u.LBFortuneTarget);
            if (LB(u,"vital_core")) w.F64(u.LBHeart);
            if (LB(u,"brute_force")) w.F64(u.LBHeavy);
            if (LB(u,"spellcraft")) { w.F64(u.LBArcane); w.Str(u.LBLastSpell); }
            if (LB(u,"sage_mind")) w.F64(u.LBSage);
            if (LB(u,"bulwark")) w.F64(u.LBBulwark);
            if (LB(u,"nimble_feet")) { w.F64(u.LBEvasion); w.I32(u.LBSeenEvades); w.Str(u.LBOpeningTarget); w.I32(u.LBOpeningWindow); }
            if (LB(u,"overclock")) { w.F64(u.LBOverdrive); w.I32(u.LBOverdriveRecovery); }
            if (LB(u,"battle_instinct")) { w.F64(u.LBRead); w.Str(u.LBReadTarget); }
            if (LB(u,"survivor")) { w.F64(u.LBRecovery); w.Str(u.LBSurviveFrom); w.I32(u.LBSurviveWindow); }
            if (LB(u,"lifeblood")) { w.F64(u.LBLife); w.F64(u.LBLifeSpent); }
            if (LB(u,"berserker")) { w.F64(u.LBFury); w.I32(u.LBVulnerable); }
            if (LB(u,"runeweaver")) w.F64(u.LBRune);
            if (LB(u,"hermit")) w.F64(u.LBHermit);
            if (LB(u,"duelist")) { w.F64(u.LBDuel); w.Str(u.LBDuelTarget); }
            if (LB(u,"stonewall")) { w.F64(u.LBStone); w.I32(u.LBStoneWindow); }
            if (LB(u,"quickstep")) w.F64(u.LBQuick);
            if (LB(u,"lucky_star")) w.F64(u.LBWindfall);
        }
    }
    public sealed partial class FightUnit
    {
        public double LBHeart, LBHeavy, LBArcane, LBSage, LBBulwark, LBEvasion, LBOverdrive, LBRead, LBRecovery, LBLife, LBLifeSpent, LBFury, LBRune, LBHermit, LBDuel, LBStone, LBQuick, LBWindfall;
        internal double LBShieldBefore;
        internal bool LBPaying;
        public string LBFortuneTarget, LBLastSpell, LBReadTarget, LBSurviveFrom, LBDuelTarget, LBOpeningTarget;
        public int LBSeenEvades, LBOverdriveRecovery, LBVulnerable, LBSurviveWindow, LBOpeningWindow, LBStoneWindow;
    }
}
