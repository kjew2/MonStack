using System;
using System.Collections.Generic;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Combat
{
    public sealed partial class FightUnit
    {
        internal double LineageIronPressure, LineageRedReserve, LineageRefundBank, LineageLegacy;
        internal double LineageBleedOverflow, LineageDuelCharge, LineagePrivilege;
        internal int LineageRecovery, LineageResonance, LineageLastLocks;
        internal bool LineageRetaliation, LineageDoorOpened, LineageAwakened, LineageRestored, LineageSacrificed, LineageLegacyReleased, LineageSignatureKept;
        internal string LineageDuelTarget, LineageElement, LineageSignature;
    }

    public static partial class BattleSim
    {
        static bool Advanced(FightUnit u, string tree, bool x = false) => Has(u, "lineage_" + tree + (x ? "_x" : "_v"));
        static void AdvancedLog(RuleCtx c, FightUnit u, string tree, double amount, string target = null, bool x = false) =>
            c.Log?.Invoke(new FightAction(u.Id, "rule:lineage_" + tree + (x ? "_x" : "_v"), target ?? u.Id, amount));
        static FightUnit AdvancedAlly(RuleCtx c, FightUnit u)
        {
            foreach (var a in c.State.Units) if (a != u && a.Alive && a.Side == u.Side && !a.Risen) return a;
            return null;
        }
        static FightUnit AdvancedEnemy(RuleCtx c, FightUnit u, string except = null)
        {
            foreach (var a in c.State.Units) if (a.Alive && a.Side != u.Side && a.Id != except) return a;
            return null;
        }

        // These mechanics add no action grants or random draws. Reserves and preparation are finite, and all
        // numerical tuning uses the shared content-backed lineage coefficients and the original rule constants.
        static void LineageAdvancedBeforeAction(RuleCtx c, FightUnit u, ref bool skip)
        {
            if (skip || !u.Alive) return;
            var r = c.Rules;
            if (Advanced(u, "deaths_door"))
            {
                if (u.Hp > r.LineageCap * u.MaxHp) u.LineageRetaliation = false;
                if (u.LineageRecovery > 0)
                {
                    u.LineageRecovery--;
                    double shield = u.Shield;
                    Heal(c, u, r.LineageGain * (u.MaxHp - u.Hp), ref shield);
                    u.Shield = shield;
                    AdvancedLog(c, u, "deaths_door", u.LineageRecovery);
                }
            }
            if (Advanced(u, "latent_power") && !HasEffectFrom(u, LatentSource) && u.Tally.Actions >= Math.Max(1, r.LatentActions - r.LineageWindow))
            {
                // A short anticipation window, replaced by the original full awakening rather than stacked forever.
                AddEffect(r, u, StatIds.DmgDealt, r.LineageGain, r.LineageWindow, "lineage_latent_power_v");
                AdvancedLog(c, u, "latent_power", r.LineageGain);
            }
            if (Advanced(u, "lone_wolf"))
            {
                var enemy = AdvancedEnemy(c, u);
                if (enemy != null && (CountOnSide(c.State, u.Side) == 1 || Advanced(u, "lone_wolf", true)))
                {
                    if (u.LineageDuelTarget != enemy.Id) { u.LineageDuelTarget = enemy.Id; u.LineageDuelCharge = 0; }
                    u.LineageDuelCharge = Math.Min(r.LineageCap, u.LineageDuelCharge + r.LineageGain);
                    AdvancedLog(c, u, "lone_wolf", u.LineageDuelCharge, enemy.Id);
                }
            }
            if (Advanced(u, "one_more") && u.LineageRefundBank > 0)
            {
                var ally = Advanced(u, "one_more", true) ? AdvancedAlly(c, u) : null;
                var recipient = ally ?? u;
                double spent = Math.Min(u.LineageRefundBank, Math.Max(0, r.GaugeSize - recipient.Gauge));
                recipient.Gauge += spent; u.LineageRefundBank -= spent;
                if (spent > 0) AdvancedLog(c, u, "one_more", spent / r.GaugeSize, recipient.Id, true);
            }
            if (Advanced(u, "arise", true) && !u.LineageSacrificed)
            {
                foreach (var a in c.State.Units)
                    if (a.Alive && a.Risen && a.Side == u.Side && a.LineageDuelTarget == u.Id)
                    {
                        // Sacrifice an add once; no revival, no new action, no death recursion.
                        u.LineageSacrificed = true; a.Hp = 0; a.KilledBy = u.Id;
                        AddEffect(r, u, StatIds.DmgDealt, r.LineageCap, r.LineageWindow, "lineage_arise_x");
                        double shield = u.Shield; Heal(c, u, a.MaxHp * r.LineageGain, ref shield); u.Shield = shield;
                        AdvancedLog(c, u, "arise", a.MaxHp, a.Id, true); break;
                    }
            }
            if (Advanced(u, "corruption", true) && !u.LineageRestored && u.LineageLastLocks >= r.LineageWindow && u.Locked != null && u.Locked.Count > 0)
            {
                // One exhausted original skill returns once, with a recovery delay and reduced subsequent damage.
                int index = u.Locked[0]; u.Locked.RemoveAt(0); u.Cooldowns[index] = r.LineageWindow;
                u.LineageRestored = true;
                AdvancedLog(c, u, "corruption", index, x: true);
            }
        }

        static void LineageAdvancedAfterAction(RuleCtx c, FightUnit u)
        {
            var r = c.Rules;
            if (!u.Alive) return;
            if (Advanced(u, "resentment") && u.ResentPool > 0)
            {
                double saved = u.ResentPool * r.ResentDecay * r.LineageGain;
                u.LineageRedReserve = Math.Min(r.LineageCap * u.MaxHp, u.LineageRedReserve + saved);
                AdvancedLog(c, u, "resentment", u.LineageRedReserve);
            }
            if (Advanced(u, "latent_power", true) && !u.LineageAwakened && u.Tally.Actions >= r.LatentActions)
            {
                u.LineageAwakened = true;
                for (int i = 0; i < u.Cooldowns.Length; i++) u.Cooldowns[i] = Math.Max(0, u.Cooldowns[i] - r.LineageWindow);
                AdvancedLog(c, u, "latent_power", r.LineageWindow, x: true);
            }
            if (Advanced(u, "last_rite"))
            {
                u.LineageLegacy = Math.Min(r.LineageCap, u.LineageLegacy + r.LineageGain);
                if (Advanced(u, "last_rite", true) && !u.LineageLegacyReleased && u.LineageLegacy >= r.LineageCap)
                {
                    var ally = AdvancedAlly(c, u);
                    if (ally != null)
                    {
                        u.LineageLegacyReleased = true;
                        double amount = u.MaxHp * u.LineageLegacy * r.LineageGain;
                        ally.Shield += amount; u.LineageLegacy *= 1 - r.LineageGain;
                        AdvancedLog(c, u, "last_rite", amount, ally.Id, true);
                    }
                }
            }
        }

        static void LineageAdvancedIncoming(RuleCtx c, HitInfo h)
        {
            if (h.Magical || !Advanced(h.Tgt, "iron_reflexes")) return;
            var r = c.Rules;
            // Physical impact stores a fraction of converted defensive pressure for the next response.
            double pressure = Math.Min(r.LineageCap * h.Tgt.MaxHp, r.IronDex * Eff(r, h.Tgt, StatIds.Dex) * r.LineageGain);
            h.Tgt.LineageIronPressure = Math.Min(r.LineageCap * h.Tgt.MaxHp, h.Tgt.LineageIronPressure + pressure);
            h.Raw *= 1 - r.LineageGain;
            AdvancedLog(c, h.Tgt, "iron_reflexes", pressure, h.Src.Id);
        }

        static void LineageAdvancedOutgoing(RuleCtx c, HitInfo h)
        {
            var r = c.Rules; var u = h.Src;
            if (Advanced(u, "iron_reflexes", true) && !h.Magical && u.LineageIronPressure > 0)
            {
                h.Raw += u.LineageIronPressure; AdvancedLog(c, u, "iron_reflexes", u.LineageIronPressure, h.Tgt.Id, true); u.LineageIronPressure = 0;
            }
            if (Advanced(u, "resentment", true) && u.LineageRedReserve > 0 && !h.Triggered)
            {
                h.Raw += u.LineageRedReserve; u.LineageRedReserve = 0;
                // Revenge gives up some ongoing red-pool STR rather than providing free permanent power.
                u.ResentPool *= 1 - r.LineageGain; AdvancedLog(c, u, "resentment", u.ResentPool, h.Tgt.Id, true);
            }
            if (Advanced(u, "deaths_door", true) && u.LineageRetaliation)
            {
                if (u.Hp <= r.LineageCap * u.MaxHp) { h.Raw *= 1 + r.LineageCap; AdvancedLog(c, u, "deaths_door", r.LineageCap, h.Tgt.Id, true); }
                u.LineageRetaliation = false;
            }
            if (Advanced(u, "lone_wolf") && h.Tgt.Id == u.LineageDuelTarget)
                h.Raw *= 1 + u.LineageDuelCharge * (CountOnSide(c.State, u.Side) == 1 ? 1 : r.LineageGain);
            if (Advanced(u, "privileged_status"))
            {
                bool attrition = false, control = false, exposure = false;
                foreach (var e in h.Tgt.Effects)
                    if (e.Frac < 0) { if (e.Stat == StatIds.Def || e.Stat == StatIds.Wis) exposure = true; else if (e.Stat == StatIds.Spd || e.Stat == StatIds.Dex) control = true; else attrition = true; }
                if (exposure) h.Mitigation *= 1 - r.LineageGain;
                if (control) u.Gauge = Math.Min(r.GaugeSize, u.Gauge + r.GaugeSize * r.LineageGain * r.LineageGain);
                if (attrition)
                {
                    u.LineagePrivilege = Math.Min(r.LineageCap, u.LineagePrivilege + r.LineageGain);
                    double shield = u.Shield;
                    Heal(c, u, (u.MaxHp - u.Hp) * r.LineageGain * r.LineageGain, ref shield); u.Shield = shield;
                }
                if (exposure || control || attrition) AdvancedLog(c, u, "privileged_status", u.LineagePrivilege, h.Tgt.Id);
                if (Advanced(u, "privileged_status", true) && u.LineagePrivilege >= r.LineageCap)
                {
                    h.Raw *= 1 + u.LineagePrivilege; u.LineagePrivilege = 0;
                    // Cash out one negative effect, giving up sustained debuff pressure.
                    for (int i = 0; i < h.Tgt.Effects.Count; i++) if (h.Tgt.Effects[i].Frac < 0) { h.Tgt.Effects.RemoveAt(i); break; }
                    AdvancedLog(c, u, "privileged_status", r.LineageCap, h.Tgt.Id, true);
                }
            }
            if (Advanced(u, "equilibrium") && h.Element != null)
            {
                if (u.LineageElement != null && u.LineageElement != h.Element) u.LineageResonance = Math.Min(r.LineageWindow, u.LineageResonance + 1);
                else u.LineageResonance = 0;
                u.LineageElement = h.Element;
                if (u.LineageResonance >= r.LineageWindow)
                {
                    u.LineageResonance = 0;
                    var ally = Advanced(u, "equilibrium", true) ? AdvancedAlly(c, u) : null;
                    if (ally != null && ally.Source.Element != h.Element) AddEffect(r, ally, StatIds.DmgDealt, r.LineageCap, r.LineageWindow, "lineage_equilibrium_x");
                    else h.Raw *= 1 + (Advanced(u, "equilibrium", true) ? r.LineageCap : r.LineageGain);
                    AdvancedLog(c, u, "equilibrium", r.LineageGain, ally?.Id ?? h.Tgt.Id, Advanced(u, "equilibrium", true));
                }
            }
            if (Advanced(u, "corruption") && h.Skill != null)
            {
                double residue = Math.Min(r.LineageCap, u.LineageLastLocks * r.LineageGain);
                // Restoration spends the residue and limits damage; locks still happen normally after the recovered cast.
                h.Raw *= u.LineageRestored ? 1 - r.LineageGain : 1 + residue;
                if (residue > 0) AdvancedLog(c, u, "corruption", residue, h.Tgt.Id);
            }

        }

        static void LineageAdvancedLanded(RuleCtx c, HitInfo h)
        {
            var r = c.Rules; var u = h.Src;
            if (Advanced(u, "one_more") && !u.OneMoreUsed && !u.Stealing && (h.Crit || r.ElementMult(h.Element, h.Tgt.Source.Element) > 1))
            {
                double unused = Math.Max(0, r.OneMore * r.GaugeSize - Math.Max(0, r.GaugeSize - u.Gauge));
                u.LineageRefundBank = Math.Min(r.LineageCap * r.GaugeSize, u.LineageRefundBank + unused * r.LineageGain);
                if (unused > 0) AdvancedLog(c, u, "one_more", u.LineageRefundBank / r.GaugeSize);
                if (!Advanced(u, "one_more", true) && u.LineageRefundBank > 0)
                {
                    // V carries the bank into ordinary gauge fill; X may direct it to an ally instead.
                    double used = Math.Min(u.LineageRefundBank, Math.Max(0, r.GaugeSize - u.Gauge));
                    u.Gauge += used; u.LineageRefundBank -= used;
                }
            }
            if (Advanced(u, "hemorrhage") && h.Tgt.Alive)
            {
                double next = h.Tgt.Bleed + r.BleedPerHit * Eff(r, u, StatIds.Dex) / r.BleedDexRef;
                u.LineageBleedOverflow = next >= BleedGauge ? Math.Min(BleedGauge * r.LineageCap, Math.Max(BleedGauge * r.LineageGain, next - BleedGauge)) : 0;
            }
        }

        static void LineageAdvancedLandedAfter(RuleCtx c, HitInfo h)
        {
            var u = h.Src;
            if (!Advanced(u, "hemorrhage") || u.LineageBleedOverflow <= 0) return;
            h.Tgt.Bleed = u.LineageBleedOverflow;
            if (Advanced(u, "hemorrhage", true))
            {
                var other = AdvancedEnemy(c, u, h.Tgt.Id);
                if (other != null) other.Bleed = Math.Min(BleedGauge - c.Rules.LineageGain, other.Bleed + u.LineageBleedOverflow);
                else h.Tgt.Bleed = Math.Min(BleedGauge - c.Rules.LineageGain, h.Tgt.Bleed + u.LineageBleedOverflow * c.Rules.LineageGain);
            }
            AdvancedLog(c, u, "hemorrhage", u.LineageBleedOverflow, h.Tgt.Id); u.LineageBleedOverflow = 0;
        }

        static void LineageAdvancedWouldDie(RuleCtx c, FightUnit src, FightUnit tgt, ref bool saved)
        {
            if (!saved || !Advanced(tgt, "deaths_door")) return;
            // Only the initial save opens recovery; repeated successful rolls never replenish it.
            if (!tgt.LineageDoorOpened) { tgt.LineageDoorOpened = true; tgt.LineageRecovery = c.Rules.LineageWindow; }
            if (Advanced(tgt, "deaths_door", true)) tgt.LineageRetaliation = true;
        }

        static void LineageAdvancedSkillUsed(RuleCtx c, FightUnit u, int index, CombatSkill sk)
        {
            if (Advanced(u, "corruption")) u.LineageLastLocks = Math.Max(u.LineageLastLocks, u.Locked?.Count ?? 0);
            if (Advanced(u, "dead_branch", true) && u.LineageSignature == null && u.Branched != null && u.Branched.Count > 0)
                u.LineageSignature = u.Branched[0];
        }

        static bool LineageAdvancedKeepSignature(RuleCtx c, FightUnit u, int index)
        {
            if (!Advanced(u, "dead_branch", true) || u.LineageSignatureKept || u.LineageSignature == null
                || index < 0 || index >= u.Source.Skills.Count || u.Source.Skills[index].Id != u.LineageSignature) return false;
            u.LineageSignatureKept = true;
            AdvancedLog(c, u, "dead_branch", index, x: true);
            return true;
        }

        static void LineageAdvancedBranchOptions(RuleCtx c, FightUnit u, List<CombatSkill> options)
        {
            if (!Advanced(u, "dead_branch") || options.Count <= 1) return;
            // Favor a missing active role. Selection within that role remains the original one battle-stream draw.
            foreach (var candidate in options)
            {
                bool present = false;
                for (int i = 0; i < u.Source.Skills.Count; i++) if (!u.SkillLocked(i) && u.Source.Skills[i].Kind == candidate.Kind) { present = true; break; }
                if (present) continue;
                var kind = candidate.Kind;
                options.RemoveAll(x => x.Kind != kind);
                AdvancedLog(c, u, "dead_branch", options.Count); break;
            }
        }

        static void LineageAdvancedDeath(RuleCtx c, FightUnit u)
        {
            if (Advanced(u, "last_rite") && u.LineageLegacy > 0)
            {
                foreach (var a in c.State.Units) if (a.Alive && a.Side == u.Side)
                {
                    double amount = u.MaxHp * u.LineageLegacy * c.Rules.RiteShare;
                    a.Shield += amount; AdvancedLog(c, u, "last_rite", amount, a.Id);
                }
                u.LineageLegacy = 0;
            }
            if (u.KilledBy == null) return;
            FightUnit owner = null;
            foreach (var a in c.State.Units) if (a.Id == u.KilledBy) { owner = a; break; }
            if (owner == null || !Advanced(owner, "arise")) return;
            foreach (var a in c.State.Units)
                if (a.Risen && a.Side == owner.Side && a.Id.StartsWith(u.Id + RisenSuffix, StringComparison.Ordinal) && a.LineageDuelTarget == null)
                {
                    a.LineageDuelTarget = owner.Id;
                    // Protection identity: the risen add provides a bounded barrier to its summoner when raised.
                    owner.Shield += a.MaxHp * c.Rules.LineageGain;
                    AdvancedLog(c, owner, "arise", a.MaxHp * c.Rules.LineageGain, a.Id); break;
                }
        }

        static void LineageAdvancedHash(CanonicalWriter w, FightUnit u)
        {
            bool any = false;
            foreach (var rule in u.Source.Rules) if (rule.StartsWith("lineage_", StringComparison.Ordinal)) { any = true; break; }
            if (!any && u.LineageDuelTarget == null) return;
            w.F64(u.LineageIronPressure); w.F64(u.LineageRedReserve); w.F64(u.LineageRefundBank); w.F64(u.LineageLegacy);
            w.F64(u.LineageBleedOverflow); w.F64(u.LineageDuelCharge); w.F64(u.LineagePrivilege);
            w.I32(u.LineageRecovery); w.I32(u.LineageResonance); w.I32(u.LineageLastLocks);
            w.Bool(u.LineageRetaliation); w.Bool(u.LineageDoorOpened); w.Bool(u.LineageAwakened); w.Bool(u.LineageRestored); w.Bool(u.LineageSacrificed); w.Bool(u.LineageLegacyReleased); w.Bool(u.LineageSignatureKept);
            w.Str(u.LineageDuelTarget); w.Str(u.LineageElement); w.Str(u.LineageSignature);
        }
    }
}
