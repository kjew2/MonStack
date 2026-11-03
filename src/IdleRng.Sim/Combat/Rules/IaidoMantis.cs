using System;
using System.Collections.Generic;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Combat
{
    // Species Iaido Mantis (#159): its three rules (specs/16 section 6.2). Its native tree 1 carries lane B's vantage.
    //   iai_draw    every hit the unit makes outside its own action (vantage counters, its on_hit_taken and on_evade effects, any
    //               hit while another unit acts) deals x(1 + MON_IAI_BONUS) with +MON_IAI_CRIT crit rate; a multiplier, it fires
    //               nothing itself
    //   still_hunt  when its turn comes and it has not waited MON_HUNT_WAITS times in a row, it waits: no action, MON_HUNT_KEEP of
    //               the gauge given back; until its next turn vantage's window is open at any HP and answers area actions too
    //   sever_art   a vantage counter dealing at least MON_SEVER_FRAC of the enemy's max HP cuts off the action it answers (the
    //               rest of it deals and applies nothing; the skill keeps its cooldown); each cut adds MON_SEVER_DMG damage against
    //               that enemy, up to MON_SEVER_MAX cuts
    // None draws. State hashed for carriers only: HuntWaits and Hunting (still_hunt), SeverCuts (sever_art). ActingId, Reacting and
    // CutAction are 0/null or stale between steps and never match a later action, so they are not hashed.
    public static partial class BattleSim
    {
        /// <summary>iai_draw on a landed out-of-turn hit: Target took it, Amount is the part of its damage the bonus added.</summary>
        public const string IaiEvent = FightAction.RulePrefix + RuleModifiers.IaiDraw;

        /// <summary>still_hunt's wait: Actor waits, Amount is the waits in a row so far.</summary>
        public const string HuntEvent = FightAction.RulePrefix + RuleModifiers.StillHunt;

        /// <summary>sever_art's cut: Target's action is cut off, Amount is the actions of it cut off so far.</summary>
        public const string SeverEvent = FightAction.RulePrefix + RuleModifiers.SeverArt;

        /// <summary>The acting unit is known to the hit hooks (iai_draw); still_hunt's wait replaces a turn's first action.</summary>
        static partial void BeforeAction_IaidoMantis(RuleCtx c, FightUnit u, ref bool skip)
        {
            c.State.ActingId = u.Id;
            if (u.TurnAction != 0 || !Has(u, RuleModifiers.StillHunt)) return;
            u.Hunting = false;   // its turn came: the window of the last wait closes
            if (skip) return;    // an earlier lane took the action
            var r = c.Rules;
            if (u.HuntWaits < r.HuntWaits && Has(u, RuleModifiers.Vantage))
            {
                skip = true;   // no action: no cooldowns, no OnAction triggers
                u.HuntWaits++;
                u.Hunting = true;
                u.Gauge += r.HuntKeep * r.GaugeSize;
                c.Log?.Invoke(new FightAction(u.Id, HuntEvent, u.Id, u.HuntWaits));
                return;
            }
            u.HuntWaits = 0;   // it acts: the next turn may wait again
        }

        static partial void AfterAction_IaidoMantis(RuleCtx c, FightUnit u) => c.State.ActingId = null;

        /// <summary>
        /// The rest of an action sever_art cut off deals nothing (an echo, a corruption recast, another jet); then iai_draw marks an
        /// out-of-turn hit of its carrier and adds its crit rate.
        /// </summary>
        static partial void BeforeHitResolves_IaidoMantis(RuleCtx c, HitInfo h)
        {
            if (h.Cancelled) return;
            var src = h.Src;
            if (!h.Triggered && src.CutAction == src.Tally.Actions) { h.Cancelled = true; return; }
            if (!Has(src, RuleModifiers.IaiDraw) || !OutOfTurn(c.State, src)) return;
            h.Iai = true;
            h.CritBonus += c.Rules.IaiCrit;
        }

        /// <summary>
        /// iai_draw's "outside its own action": the unit reacts in one of its own on_hit_taken or on_evade effects, or another unit's
        /// action is running (a vantage counter, a trigger of another unit's hit). Not at fight start (no action runs).
        /// </summary>
        static bool OutOfTurn(FightState s, FightUnit u) => u.Reacting > 0 || s.ActingId != null && s.ActingId != u.Id;

        /// <summary>
        /// still_hunt: an area action meets the vantage strike of each waiting carrier it would hit, in unit order, before any target
        /// is hit; once one kills the attacker or cuts the action off, the whole action is cancelled. An action already cut off
        /// (sever_art: its echo) is cancelled too.
        /// </summary>
        static partial void BeforeAoe_IaidoMantis(RuleCtx c, FightUnit src, ref bool cancelled)
        {
            if (cancelled) return;
            if (src.CutAction == src.Tally.Actions) { cancelled = true; return; }
            var units = c.State.Units;
            for (int i = 0; i < units.Count && !cancelled; i++)
            {
                var t = units[i];
                if (t.Side == src.Side || !t.Alive || !t.Hunting || !Has(t, RuleModifiers.Vantage)) continue;
                if (!src.Alive) { cancelled = true; return; }
                if (VantageCounter(c, t, src)) cancelled = true;
            }
        }

        /// <summary>sever_art: a deep enough vantage counter cuts the attacker's action off and counts the cut against it.</summary>
        static partial void OnVantageCounter_IaidoMantis(RuleCtx c, FightUnit t, FightUnit src, double dealt, ref bool cut)
        {
            if (cut || !Has(t, RuleModifiers.SeverArt) || !(dealt >= c.Rules.SeverFrac * src.MaxHp)) return;
            cut = true;
            src.CutAction = src.Tally.Actions;
            t.SeverCuts ??= new Dictionary<string, int>(StringComparer.Ordinal);
            t.SeverCuts.TryGetValue(src.Id, out int n);
            t.SeverCuts[src.Id] = ++n;
            c.Log?.Invoke(new FightAction(t.Id, SeverEvent, src.Id, n));
        }

        /// <summary>iai_draw's multiplier, and sever_art's damage against an enemy it has cut off.</summary>
        static partial void ModifyOutgoing_IaidoMantis(RuleCtx c, HitInfo h)
        {
            var r = c.Rules;
            if (h.Iai) h.Raw *= 1 + r.IaiBonus;
            var src = h.Src;
            if (src.SeverCuts != null && src.SeverCuts.TryGetValue(h.Tgt.Id, out int n) && Has(src, RuleModifiers.SeverArt))
                h.Raw *= 1 + r.SeverDmg * Math.Min(n, r.SeverMax);
        }

        /// <summary>iai_draw: a landed out-of-turn hit is logged with the share of its damage the bonus added.</summary>
        static partial void OnHitLanded_IaidoMantis(RuleCtx c, HitInfo h)
        {
            if (!h.Iai || !(c.Rules.IaiBonus > 0)) return;
            c.Log?.Invoke(new FightAction(h.Src.Id, IaiEvent, h.Tgt.Id, h.Dealt * c.Rules.IaiBonus / (1 + c.Rules.IaiBonus)));
        }

        static partial void HashUnit_IaidoMantis(CanonicalWriter w, FightState s, FightUnit u)
        {
            if (Has(u, RuleModifiers.StillHunt)) { w.I32(u.HuntWaits); w.Bool(u.Hunting); }
            if (!Has(u, RuleModifiers.SeverArt)) return;
            int count = u.SeverCuts?.Count ?? 0;
            w.I32(count);
            if (count == 0) return;
            var keys = new List<string>(u.SeverCuts.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) { w.Str(k); w.I32(u.SeverCuts[k]); }
        }

        static partial void ValidateRules_IaidoMantis(FightRules r, ref bool ok)
        {
            if (!(r.IaiBonus >= 0 && !double.IsInfinity(r.IaiBonus) && In01(r.IaiCrit) && r.HuntWaits >= 1 && r.HuntKeep > 0 && r.HuntKeep <= 1
                  && r.SeverFrac > 0 && r.SeverFrac <= 1 && r.SeverDmg >= 0 && !double.IsInfinity(r.SeverDmg) && r.SeverMax >= 0))
                ok = false;
        }
    }

    public sealed partial class FightState
    {
        /// <summary>iai_draw: the id of the unit whose action is running (null between actions and at fight start; not hashed).</summary>
        internal string ActingId { get; set; }
    }

    public sealed partial class FightRules
    {
        /// <summary>iai_draw: the damage bonus of an out-of-turn hit (MON_IAI_BONUS).</summary>
        public double IaiBonus { get; set; }

        /// <summary>iai_draw: the crit rate added to an out-of-turn hit (MON_IAI_CRIT).</summary>
        public double IaiCrit { get; set; }

        /// <summary>still_hunt: the most turns in a row the unit waits before it acts (MON_HUNT_WAITS, at least 1).</summary>
        public int HuntWaits { get; set; }

        /// <summary>still_hunt: the share of BATTLE_GAUGE a wait gives back (MON_HUNT_KEEP, in (0, 1]).</summary>
        public double HuntKeep { get; set; }

        /// <summary>sever_art: a counter's HP damage, as a share of the enemy's max HP, that cuts its action off (MON_SEVER_FRAC).</summary>
        public double SeverFrac { get; set; }

        /// <summary>sever_art: the damage bonus against an enemy per action of it cut off (MON_SEVER_DMG).</summary>
        public double SeverDmg { get; set; }

        /// <summary>sever_art: the cuts per enemy that add damage (MON_SEVER_MAX).</summary>
        public int SeverMax { get; set; }

        static partial void Load_IaidoMantis(FightRules r, ContentDb c)
        {
            double D(string k) => c.ConstantValues.TryGetValue(k, out var v) ? v : 0;
            r.IaiBonus = D("MON_IAI_BONUS");
            r.IaiCrit = D("MON_IAI_CRIT");
            r.HuntWaits = (int)D("MON_HUNT_WAITS");
            r.HuntKeep = D("MON_HUNT_KEEP");
            r.SeverFrac = D("MON_SEVER_FRAC");
            r.SeverDmg = D("MON_SEVER_DMG");
            r.SeverMax = (int)D("MON_SEVER_MAX");
        }
    }

    public sealed partial class FightUnit
    {
        /// <summary>iai_draw: the unit's own on_hit_taken or on_evade effects running now (0 between steps; not hashed).</summary>
        internal int Reacting { get; set; }

        /// <summary>still_hunt: the turns in a row the unit has waited (0 after it acts).</summary>
        public int HuntWaits { get; set; }

        /// <summary>still_hunt: the unit waited and its next turn has not come yet: vantage's window is open at any HP, area actions included.</summary>
        public bool Hunting { get; set; }

        /// <summary>sever_art: the unit's action count when an enemy's counter cut it off (-1: none); the rest of that action deals nothing. Not hashed.</summary>
        internal int CutAction { get; set; } = -1;

        /// <summary>sever_art: the actions of each enemy (by id) the unit has cut off; null until the first cut.</summary>
        public Dictionary<string, int> SeverCuts { get; set; }

        partial void Clone_IaidoMantis(FightUnit copy)
        {
            if (SeverCuts != null) copy.SeverCuts = new Dictionary<string, int>(SeverCuts, StringComparer.Ordinal);
        }
    }
}
