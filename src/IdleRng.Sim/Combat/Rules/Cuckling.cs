using System;
using System.Collections.Generic;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;

namespace IdleRng.Sim.Combat
{
    // Species Cuckling (#143): its native and chase rules (specs/16 section 6.2). Its native tree 2 carries lane F's mimic_core.
    //   stolen_turn    when an enemy's turn comes (its gauge paid) and the unit's gauge is at least MON_STEAL_GAUGE full, the
    //                  unit acts in its place: the enemy loses the turn, the unit's gauge drops to 0. MON_STEALS_PER_FOE per
    //                  enemy per fight. Step calls StealTurn before each turn.
    //   usurp          a stolen turn is spent on the victim's own first ready skill (rescue pass first, as the default AI), cast
    //                  with the unit's stats; the victim's copy goes on cooldown. None ready: a basic attack. The carrier may
    //                  steal MON_USURP_STEALS turns per enemy (the issue's "raise steals per foe" lever for the chase).
    //   foundling_egg  at fight start the unit lays an egg on its side, even a full one: an add (never keeps the side alive)
    //                  with MON_EGG_HP of its max HP that never acts; after MON_HATCH_ACTIONS of the unit's actions it hatches
    //                  into a Foundling with MON_HATCH_STATS of the unit's stats and its active skills, no triggers or rules.
    // Loop guards: a stolen action is run here, not through the turn order, so it is never stolen again and no extra action
    // follows it (it is the turn's only action, so it uses up MON_EXTRA_ACTIONS_CAP); one_more refunds nothing inside it. No rule
    // draws. Egg and Foundling (Brood) never count for kills and never rise through arise. State is hashed for carriers only
    // (StolenFrom, EggIndex) and for the Brood unit itself.
    public static partial class BattleSim
    {
        /// <summary>stolen_turn: Actor = the thief, Target = the enemy that lost its turn, Amount = steals left on that enemy.</summary>
        public const string StealEvent = FightAction.RulePrefix + RuleModifiers.StolenTurn;

        /// <summary>usurp: Actor = the thief, Target = the victim, Amount = 1 when it casts the victim's skill (logged next), 0 for a basic attack.</summary>
        public const string UsurpEvent = FightAction.RulePrefix + RuleModifiers.Usurp;

        /// <summary>foundling_egg: laid (Actor = the carrier, Target = the egg, Amount = its HP) and hatched (Actor = Target = the Foundling, Amount = its max HP).</summary>
        public const string EggEvent = FightAction.RulePrefix + RuleModifiers.FoundlingEgg;

        /// <summary>The suffix of an egg's id: the carrier's id plus this (plus a number if that is taken).</summary>
        public const string EggSuffix = "#egg";

        /// <summary>
        /// stolen_turn, before <paramref name="victim"/>'s turn (its gauge already paid): the living enemy carrier with the fullest
        /// gauge of at least MON_STEAL_GAUGE (ties in unit order) that has steals left on it takes the turn instead. Returns false
        /// when nobody steals; else runs the stolen action and sets <paramref name="outcome"/>.
        /// </summary>
        static bool StealTurn(RuleCtx c, FightUnit victim, out FightOutcome outcome)
        {
            outcome = FightOutcome.Ongoing;
            var r = c.Rules;
            var s = c.State;
            FightUnit thief = null;
            foreach (var u in s.Units)
                if (u.Side != victim.Side && u.Alive && u.Gauge >= r.StealGauge * r.GaugeSize && Has(u, RuleModifiers.StolenTurn)
                    && Steals(u, victim.Id) < StealLimit(r, u) && (thief == null || u.Gauge > thief.Gauge))
                    thief = u;
            if (thief == null) return false;
            thief.StolenFrom ??= new List<string>(4);
            thief.StolenFrom.Add(victim.Id);
            thief.Gauge = 0;
            c.Log?.Invoke(new FightAction(thief.Id, StealEvent, victim.Id, StealLimit(r, thief) - Steals(thief, victim.Id)));
            thief.TurnAction = 0;
            thief.Stealing = true;
            thief.UsurpVictim = Has(thief, RuleModifiers.Usurp) ? victim : null;
            try { Act(r, s, thief, c.Rng, c.Log); }
            finally { thief.Stealing = false; thief.UsurpVictim = null; }
            HookAfterAction(c, thief);
            ProcessDeaths(c);
            CheckPhases(r, s, c.Rng, c.Log);
            outcome = Decide(s, thief.Side);
            return true;
        }

        /// <summary>Turns the unit may steal from each enemy: MON_STEALS_PER_FOE, or MON_USURP_STEALS for a usurp carrier if that is more.</summary>
        static int StealLimit(FightRules r, FightUnit u) => Has(u, RuleModifiers.Usurp) ? Math.Max(r.StealsPerFoe, r.UsurpSteals) : r.StealsPerFoe;

        static int Steals(FightUnit thief, string victim)
        {
            if (thief.StolenFrom == null) return 0;
            int n = 0;
            foreach (var id in thief.StolenFrom) if (id == victim) n++;
            return n;
        }

        /// <summary>
        /// usurp, as the stolen action's choice (Act calls it after the unit's OnAction triggers): the victim's first ready active
        /// skill that would do something for the unit (heal and shield first, then the rest, each in the victim's list order) is
        /// used with the unit's stats and goes on cooldown for the victim; with none, a basic attack. Always acts (returns true)
        /// while the unit has a victim.
        /// </summary>
        static bool Usurp(FightRules r, FightState s, FightUnit u, IRandomSource rng, Action<FightAction> onAction)
        {
            var victim = u.UsurpVictim;
            if (victim == null) return false;
            u.UsurpVictim = null;
            var ctx = new RuleCtx(r, s, rng, onAction);
            var skills = victim.Source.Skills;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < skills.Count; i++)
                {
                    var sk = skills[i];
                    if ((sk.Kind == SkillKind.Heal || sk.Kind == SkillKind.Shield) != (pass == 0)) continue;
                    if (sk.Kind == SkillKind.Passive || !Ready(ctx, victim, i) || !Useful(r, s, u, sk)) continue;
                    victim.Cooldowns[i] = sk.Cooldown;
                    onAction?.Invoke(new FightAction(u.Id, UsurpEvent, victim.Id, 1));
                    Use(r, s, u, sk, rng, onAction, triggered: false);
                    return true;
                }
            onAction?.Invoke(new FightAction(u.Id, UsurpEvent, victim.Id, 0));
            BasicAttack(r, s, u, rng, onAction);
            return true;
        }

        /// <summary>foundling_egg: each living carrier lays its egg (after lanes A to E, so lone_wolf has already counted its side).</summary>
        static partial void OnFightStart_Cuckling(RuleCtx c)
        {
            var r = c.Rules;
            var s = c.State;
            for (int i = 0, n = s.Units.Count; i < n; i++)   // eggs laid now lay nothing
            {
                var u = s.Units[i];
                if (!u.Alive || !Has(u, RuleModifiers.FoundlingEgg)) continue;
                string id = u.Id + EggSuffix;
                for (int k = 2; HasId(s, id); k++) id = u.Id + EggSuffix + k;
                var st = Scaled(u.Source.Stats, r.HatchStats);
                st.Hp = r.EggHp * u.MaxHp / r.HpPerPoint;
                var f = new Fighter { Id = id, Element = u.Source.Element, Stats = st };
                u.EggIndex = s.Units.Count;
                var egg = Summon(r, s, f, u.Side, c.Rng, c.Log, new FightAction(u.Id, EggEvent, id, MaxHp(r, st.Hp)));
                egg.Brood = true;
                egg.Egg = true;
            }
        }

        /// <summary>foundling_egg: an egg never fills its gauge.</summary>
        static partial void OnGaugeTick_Cuckling(RuleCtx c, FightUnit u, ref double fill)
        {
            if (u.Egg) fill = 0;
        }

        /// <summary>foundling_egg: after the carrier's MON_HATCH_ACTIONS-th action, its living egg hatches.</summary>
        static partial void AfterAction_Cuckling(RuleCtx c, FightUnit u)
        {
            if (u.EggIndex < 0 || u.Tally.Actions < c.Rules.HatchActions || !Has(u, RuleModifiers.FoundlingEgg)) return;
            var egg = c.State.Units[u.EggIndex];
            if (!egg.Egg || !egg.Alive) return;
            var r = c.Rules;
            var skills = new List<CombatSkill>();
            foreach (var sk in u.Source.Skills) if (sk.Kind != SkillKind.Passive) skills.Add(sk);
            egg.Source = new Fighter { Id = egg.Id, Element = u.Source.Element, Stats = Scaled(u.Source.Stats, r.HatchStats), Skills = skills };
            egg.Cooldowns = new int[skills.Count];
            egg.Egg = false;
            egg.Gauge = 0;
            egg.BaseMaxHp = MaxHp(r, egg.Source.Stats.Hp);
            egg.MaxHp = egg.BaseMaxHp * Math.Max(r.StatFloor, 1 + Bonus(egg, StatIds.Hp));
            egg.Hp = egg.MaxHp;   // a new creature: it hatches whole
            c.Log?.Invoke(new FightAction(egg.Id, EggEvent, egg.Id, egg.MaxHp));
        }

        static MonsterStats Scaled(MonsterStats b, double f)
        {
            var st = b.Clone();
            st.Hp *= f; st.Str *= f; st.Int *= f; st.Wis *= f; st.Dex *= f; st.Def *= f; st.Spd *= f; st.Luk *= f;
            return st;
        }

        static partial void HashUnit_Cuckling(CanonicalWriter w, FightState s, FightUnit u)
        {
            if (Has(u, RuleModifiers.StolenTurn))
            {
                w.I32(u.StolenFrom?.Count ?? 0);
                if (u.StolenFrom != null) foreach (var id in u.StolenFrom) w.Str(id);
            }
            if (Has(u, RuleModifiers.FoundlingEgg)) w.I32(u.EggIndex);
            if (u.Brood) w.Bool(u.Egg);
        }

        static partial void ValidateRules_Cuckling(FightRules r, ref bool ok)
        {
            if (!(In01(r.StealGauge) && r.StealsPerFoe >= 1 && r.UsurpSteals >= 1 && r.EggHp > 0 && r.EggHp <= 1 && r.HatchActions >= 1 && r.HatchStats > 0 && r.HatchStats <= 1))
                ok = false;
        }
    }

    public sealed partial class FightRules
    {
        /// <summary>stolen_turn: the share of a full gauge the unit needs to steal a turn (MON_STEAL_GAUGE).</summary>
        public double StealGauge { get; set; }

        /// <summary>stolen_turn: turns it may steal from each enemy per fight (MON_STEALS_PER_FOE).</summary>
        public int StealsPerFoe { get; set; } = 1;

        /// <summary>usurp: turns a usurp carrier may steal from each enemy per fight, if more than MON_STEALS_PER_FOE (MON_USURP_STEALS).</summary>
        public int UsurpSteals { get; set; } = 1;

        /// <summary>foundling_egg: the egg's HP, a share of the carrier's max HP (MON_EGG_HP).</summary>
        public double EggHp { get; set; } = 1;

        /// <summary>foundling_egg: the carrier's actions before the egg hatches (MON_HATCH_ACTIONS).</summary>
        public int HatchActions { get; set; } = 1;

        /// <summary>foundling_egg: the Foundling's trained stats, a share of the carrier's (MON_HATCH_STATS).</summary>
        public double HatchStats { get; set; } = 1;

        static partial void Load_Cuckling(FightRules r, ContentDb c)
        {
            var k = c.ConstantValues;
            r.StealGauge = k["MON_STEAL_GAUGE"];
            r.StealsPerFoe = (int)k["MON_STEALS_PER_FOE"];
            r.UsurpSteals = (int)k["MON_USURP_STEALS"];
            r.EggHp = k["MON_EGG_HP"];
            r.HatchActions = (int)k["MON_HATCH_ACTIONS"];
            r.HatchStats = k["MON_HATCH_STATS"];
        }
    }

    public sealed partial class FightUnit
    {
        /// <summary>stolen_turn: the ids of the enemies whose turns it stole, once per steal (null: none yet).</summary>
        public List<string> StolenFrom { get; set; }

        /// <summary>stolen_turn: a stolen action is running (one_more refunds nothing in it). Never true between steps: not hashed.</summary>
        internal bool Stealing { get; set; }

        /// <summary>usurp: the victim whose skill the running stolen action casts. Set only for one action: not hashed.</summary>
        internal FightUnit UsurpVictim { get; set; }

        /// <summary>foundling_egg: the index in FightState.Units of the egg it laid (-1: none).</summary>
        public int EggIndex { get; set; } = -1;

        /// <summary>foundling_egg: this unit is an egg or the Foundling that hatched from it (never counts for kills).</summary>
        public bool Brood { get; set; }

        /// <summary>foundling_egg: this unit is an egg that has not hatched (it never acts).</summary>
        public bool Egg { get; set; }

        partial void Clone_Cuckling(FightUnit copy)
        {
            if (StolenFrom != null) copy.StolenFrom = new List<string>(StolenFrom);
        }
    }
}
