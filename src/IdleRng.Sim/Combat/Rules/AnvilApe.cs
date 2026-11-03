using System;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Combat
{
    // Species lane: the Anvil Ape (#153, epic #160), specs/16 section 6.2. Its rules feed, speed up and cash in lane C's guard_break:
    //   anvil_rhythm   it counts the hits it lands (skills, basic attacks, triggered hits; evaded or decoyed hits do not count):
    //                  every MON_RHYTHM_HITS-th is a crit. A graze cannot crit, so a graze that would be that hit leaves the count
    //                  one short and the next landed hit takes the crit. The crit roll is still drawn as usual (no draw added or
    //                  removed); a forced crit (the roll missed) is logged. A crit it forces fires OnCrit like any crit (wave 0's
    //                  trigger guard still holds inside a triggered effect)
    //   brittle_guard  each time an enemy's guard breaks (any carrier's hit or shatterpoint's shards), its pips refill
    //                  MON_BRITTLE_STEP fewer than they did last time, down to MON_BRITTLE_MIN. Per enemy, shared by every carrier
    //                  on the side. A broken enemy still acts once before its pips refill (lane C), so it gets every other action
    //   shatterpoint   its hit on a broken enemy (guard at 0, before it has acted again; the breaking hit counts) that leaves it at
    //                  or below MON_SHATTER_FRAC of max HP shatters it: its HP goes at once (past any shield), the kill is the
    //                  unit's, and every other living enemy loses MON_SHATTER_SHARDS guard pips (a shard break is not a shatter, so
    //                  nothing chains). A boss (a unit with boss phases) never shatters: once per break it takes MON_SHATTER_BOSS
    //                  of its max HP (through its shield, unmitigated) instead
    // None of them draws. State is hashed only for carriers: the rhythm count for anvil_rhythm units, an enemy's refill for units
    // facing a brittle_guard carrier, and the once-per-break chunk for units facing a shatterpoint carrier.
    public static partial class BattleSim
    {
        /// <summary>anvil_rhythm: the hit about to land is a forced crit; Target = its target, Amount = MON_RHYTHM_HITS.</summary>
        public const string RhythmEvent = FightAction.RulePrefix + RuleModifiers.AnvilRhythm;

        /// <summary>brittle_guard: Actor = the unit that broke the guard, Target = the enemy, Amount = the pips it refills to now.</summary>
        public const string BrittleEvent = FightAction.RulePrefix + RuleModifiers.BrittleGuard;

        /// <summary>shatterpoint: Target shattered (Amount = the HP it had left) or, a boss, took the chunk (Amount = the damage).</summary>
        public const string ShatterEvent = FightAction.RulePrefix + RuleModifiers.Shatterpoint;

        /// <summary>anvil_rhythm: count the landed hit; on the count's last hit the hit crits (a graze waits one short).</summary>
        static partial void CritRolled_AnvilApe(RuleCtx c, HitInfo h)
        {
            var src = h.Src;
            if (!Has(src, RuleModifiers.AnvilRhythm)) return;
            int n = c.Rules.RhythmHits;
            if (h.Graze)
            {
                src.RhythmCount = Math.Min(src.RhythmCount + 1, n - 1);
                return;
            }
            if (++src.RhythmCount < n) return;
            src.RhythmCount = 0;
            if (h.Crit) return;   // the roll already crit: the rhythm is kept, nothing forced
            h.Crit = true;
            c.Log?.Invoke(new FightAction(src.Id, RhythmEvent, h.Tgt.Id, n));
        }

        /// <summary>brittle_guard: a guard that broke refills smaller next time; shatterpoint: a new break may be chunked again.</summary>
        static partial void OnGuardBroke_AnvilApe(RuleCtx c, FightUnit src, FightUnit tgt)
        {
            tgt.ShatterChunked = false;
            if (!Facing(c.State, tgt, RuleModifiers.BrittleGuard)) return;
            var r = c.Rules;
            int pips = PipsOf(r, tgt), next = Math.Max(r.BrittleMin, pips - r.BrittleStep);
            if (next == pips) return;
            tgt.BreakCap = next;
            c.Log?.Invoke(new FightAction(src.Id, BrittleEvent, tgt.Id, next));
        }

        /// <summary>shatterpoint: after lane C's strip, a broken enemy low enough shatters (a boss takes the chunk).</summary>
        static partial void OnHitLanded_AnvilApe(RuleCtx c, HitInfo h)
        {
            FightUnit src = h.Src, tgt = h.Tgt;
            if (!Has(src, RuleModifiers.Shatterpoint) || !tgt.Alive || tgt.Side == src.Side || tgt.BreakState == BreakNone) return;
            var r = c.Rules;
            if (tgt.Source.Phases.Count > 0)   // a boss: once per break, a chunk instead
            {
                if (tgt.ShatterChunked || !(r.ShatterBoss > 0)) return;
                tgt.ShatterChunked = true;
                double dmg = Absorb(c, src, tgt, r.ShatterBoss * tgt.MaxHp, null);
                c.Log?.Invoke(new FightAction(src.Id, ShatterEvent, tgt.Id, dmg));
                Reborn(tgt, c.Log);
                return;
            }
            if (tgt.Hp > r.ShatterFrac * tgt.MaxHp) return;
            double left = tgt.Hp;
            Absorb(c, src, tgt, left, null, left);   // straight to HP, past any shield
            c.Log?.Invoke(new FightAction(src.Id, ShatterEvent, tgt.Id, left));
            Reborn(tgt, c.Log);
            if (tgt.Alive) return;   // a death save held it: no shards
            foreach (var e in c.State.Units)
                if (e != tgt && e.Side == tgt.Side && e.Alive) StripPips(c, src, e, r.ShatterShards);
        }

        /// <summary>Whether a unit on the other side of <paramref name="u"/> carries the rule (alive or not).</summary>
        static bool Facing(FightState s, FightUnit u, string rule)
        {
            foreach (var e in s.Units)
                if (e.Side != u.Side && Has(e, rule)) return true;
            return false;
        }

        static partial void HashUnit_AnvilApe(CanonicalWriter w, FightState s, FightUnit u)
        {
            if (Has(u, RuleModifiers.AnvilRhythm)) w.I32(u.RhythmCount);
            if (Facing(s, u, RuleModifiers.BrittleGuard)) w.I32(u.BreakCap);
            if (Facing(s, u, RuleModifiers.Shatterpoint)) w.Bool(u.ShatterChunked);
        }

        static partial void ValidateRules_AnvilApe(FightRules r, ref bool ok)
        {
            if (!(r.RhythmHits >= 2 && r.BrittleStep >= 0 && r.BrittleMin >= 1 && r.BrittleMin <= r.BreakPips && r.ShatterFrac > 0 && r.ShatterFrac < 1
                  && r.ShatterShards >= 0 && In01(r.ShatterBoss)))
                ok = false;
        }
    }

    public sealed partial class FightRules
    {
        /// <summary>anvil_rhythm: every this-many-th landed hit is a crit (MON_RHYTHM_HITS).</summary>
        public int RhythmHits { get; set; } = 3;

        /// <summary>brittle_guard: pips lost from the refill at each break (MON_BRITTLE_STEP).</summary>
        public int BrittleStep { get; set; }

        /// <summary>brittle_guard: the refill never goes below this (MON_BRITTLE_MIN).</summary>
        public int BrittleMin { get; set; } = 1;

        /// <summary>shatterpoint: a broken enemy at or below this fraction of max HP after the hit shatters (MON_SHATTER_FRAC).</summary>
        public double ShatterFrac { get; set; } = 0.25;

        /// <summary>shatterpoint: guard pips a shatter strips from every other living enemy (MON_SHATTER_SHARDS).</summary>
        public int ShatterShards { get; set; }

        /// <summary>shatterpoint: a boss's chunk, a fraction of its max HP, once per break (MON_SHATTER_BOSS).</summary>
        public double ShatterBoss { get; set; }

        static partial void Load_AnvilApe(FightRules r, ContentDb c)
        {
            var k = c.ConstantValues;
            r.RhythmHits = (int)k["MON_RHYTHM_HITS"];
            r.BrittleStep = (int)k["MON_BRITTLE_STEP"];
            r.BrittleMin = (int)k["MON_BRITTLE_MIN"];
            r.ShatterFrac = k["MON_SHATTER_FRAC"];
            r.ShatterShards = (int)k["MON_SHATTER_SHARDS"];
            r.ShatterBoss = k["MON_SHATTER_BOSS"];
        }
    }

    public sealed partial class FightUnit
    {
        /// <summary>anvil_rhythm: hits landed since its last rhythm crit.</summary>
        public int RhythmCount { get; set; }

        /// <summary>brittle_guard: the pips its guard refills to (0 = MON_BREAK_PIPS, the full guard).</summary>
        public int BreakCap { get; set; }

        /// <summary>shatterpoint: a boss took its chunk in the current break.</summary>
        public bool ShatterChunked { get; set; }
    }
}
