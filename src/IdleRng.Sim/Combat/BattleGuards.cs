using System;
using IdleRng.Sim.Rng;

namespace IdleRng.Sim.Combat
{
    /// <summary>
    /// What the loop guards track (specs/16 section 6.4): the six fight triggers, then the rule procs that act like triggers. An
    /// effect of a kind never fires another effect of the same kind while it runs. Lanes add their procs at the end (at most 32).
    /// </summary>
    internal enum GuardKind
    {
        BattleStart = FightTrigger.BattleStart, OnAction = FightTrigger.OnAction, OnBasicAttack = FightTrigger.OnBasicAttack,
        OnCrit = FightTrigger.OnCrit, OnHitTaken = FightTrigger.OnHitTaken, OnEvade = FightTrigger.OnEvade,

        /// <summary>A counterattack after a hit taken (counter_mitigated; lane B's vantage): no counter of a counter.</summary>
        Counter = 8,

        /// <summary>echo_cast's repeat (part of the cast, so it adds no depth): an echo is never echoed again.</summary>
        Echo = 9,
    }

    /// <summary>What every rule hook gets: the fight's rules, its state, its random source and its log (null = no log).</summary>
    internal readonly struct RuleCtx
    {
        public readonly FightRules Rules;
        public readonly FightState State;
        public readonly IRandomSource Rng;
        public readonly Action<FightAction> Log;

        public RuleCtx(FightRules rules, FightState state, IRandomSource rng, Action<FightAction> log)
        {
            Rules = rules; State = state; Rng = rng; Log = log;
        }
    }

    /// <summary>
    /// One hit as it moves through BattleSim.Hit (specs/16 section 6.4), for the hit hooks. Raw is the damage before mitigation;
    /// Evade the target's avoid chance; Mitigation the DEF or WIS (by Magical) that mitigates it, before fire_pierce. The
    /// conversion step may rewrite Raw, Magical, Element, Evade and Mitigation (keeping Mitigation in step with Magical);
    /// after it only the multiplier hooks change Raw.
    /// </summary>
    internal sealed class HitInfo
    {
        public FightUnit Src;
        public FightUnit Tgt;
        public double Raw;
        public bool Magical;
        public string Element;

        /// <summary>The skill or triggered effect that hits; null for a basic attack.</summary>
        public CombatSkill Skill;
        public bool Aoe;

        /// <summary>The hit comes from a triggered effect (trigger depth above 0 when it started).</summary>
        public bool Triggered;
        public double Evade;
        public double Mitigation;
        public bool Crit;
        public bool Graze;

        /// <summary>HP damage dealt (after shields), and the damage mitigation took off; set once the hit lands.</summary>
        public double Dealt;
        public double Mitigated;

        /// <summary>Set by the before-hit hook: the hit does not happen (no decoy, no roll, no draw).</summary>
        public bool Cancelled;
    }

    public static partial class BattleSim
    {
        /// <summary>
        /// Enter an effect of <paramref name="kind"/>: refused while an effect of the same kind runs, or, when it
        /// <paramref name="counts"/> as a trigger level, once MON_TRIGGER_DEPTH levels are already running. On success the caller
        /// runs the effect and then calls <see cref="ExitGuard"/> with <paramref name="saved"/>.
        /// </summary>
        static bool EnterGuard(FightRules r, FightState s, GuardKind kind, bool counts, out GuardState saved)
        {
            saved = new GuardState(s.TriggerDepth, s.GuardMask);
            int bit = 1 << (int)kind;
            if ((s.GuardMask & bit) != 0 || counts && s.TriggerDepth >= r.TriggerDepth) return false;
            s.GuardMask |= bit;
            if (counts) s.TriggerDepth++;
            return true;
        }

        static void ExitGuard(FightState s, GuardState saved)
        {
            s.TriggerDepth = saved.Depth;
            s.GuardMask = saved.Mask;
        }

        /// <summary>Whether an effect of <paramref name="kind"/> is running now (a lane's rule asks before it reacts).</summary>
        static bool InGuard(FightState s, GuardKind kind) => (s.GuardMask & (1 << (int)kind)) != 0;

        readonly struct GuardState
        {
            public readonly int Depth;
            public readonly int Mask;
            public GuardState(int depth, int mask) { Depth = depth; Mask = mask; }
        }

        /// <summary>Whether the unit may use its skill at <paramref name="i"/> now: its cooldown is over, and no lane rule holds it back.</summary>
        static bool Ready(RuleCtx c, FightUnit u, int i)
        {
            bool ok = u.Cooldowns[i] <= 0;
            HookSkillReady(c, u, i, ref ok);
            return ok;
        }

        /// <summary>
        /// Each unit that died since the last call gets its death hook once, in Units order: after every action and after the
        /// end-of-tick ageing. KilledBy names the unit whose blow killed it (null for self-inflicted entropy recoil).
        /// </summary>
        static void ProcessDeaths(RuleCtx c)
        {
            var units = c.State.Units;
            for (int i = 0; i < units.Count; i++)   // a death hook may add a unit; it is visited too
            {
                var u = units[i];
                if (u.Alive || u.DeathSeen) continue;
                u.DeathSeen = true;
                HookDeath(c, u);
            }
        }
    }
}
