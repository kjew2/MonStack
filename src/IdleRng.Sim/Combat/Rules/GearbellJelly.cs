using System;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Combat
{
    // Species lane: the Gearbell Jelly (#158, epic #160), specs/16 section 6.2. Its rules wind, spend and clock lane B's mana_ward:
    //   ward_rewind  each active skill with a cooldown of 2 or more it casts restores MON_REWIND_WIS x WIS of its ward, up to the
    //                ward's full size (MON_WARD_WIS x WIS); a broken ward re-forms. Nothing without mana_ward
    //   ward_toll    its ward takes the larger of MON_WARD_SHARE and MON_TOLL_SHARE of each blow; when it breaks under damage it tolls (after the blow, if it still lives): MON_TOLL_DMG x the ward's full
    //                size as dark magical damage to every living enemy (WIS mitigates, can graze, never crits; not a hit). The ward
    //                re-forms at MON_TOLL_REFORM of full after MON_TOLL_DELAY of its own actions. At most MON_TOLL_MAX per fight
    //   escapement   its active skills' cooldowns tick on the fight clock (one per turn of time, MON_WARD_CDR faster while the ward
    //                holds), not on its actions; a cooldown skill that is ready and that the AI (or a gambit) would use is cast at
    //                once, out of turn, at MON_ESCAPE_POWER: a skill cast only (no gauge, no OnAction triggers, no cooldown tick,
    //                no echo, not an action). At most one per tick; each counts toward MON_EXTRA_ACTIONS_CAP of its next turn, so
    //                at most that many between two of its turns. Its own turns use skills with no cooldown, or basic attacks.
    // Loop guards: the toll is a triggered effect: its damage never tolls a ward it breaks, and it fires no hit hooks or triggers.
    // Only the toll draws (one graze roll per enemy).
    public static partial class BattleSim
    {
        /// <summary>ward_rewind: Amount is the ward restored.</summary>
        public const string RewindEvent = FightAction.RulePrefix + RuleModifiers.WardRewind;

        /// <summary>ward_toll: Amount is the dark damage dealt to Target (one line per enemy).</summary>
        public const string TollEvent = FightAction.RulePrefix + RuleModifiers.WardToll;

        /// <summary>escapement: a clock cast follows; Amount is the index of the skill cast.</summary>
        public const string EscapeEvent = FightAction.RulePrefix + RuleModifiers.Escapement;

        /// <summary>The element of the toll's damage.</summary>
        const string TollElement = "dark";

        // ------------------------------------------------------------------ ward_rewind

        /// <summary>ward_rewind: a cast skill with a cooldown of 2 or more winds the ward back (an echo or a trigger does not).</summary>
        static partial void OnSkillUsed_GearbellJelly(RuleCtx c, FightUnit u, int index, CombatSkill sk)
        {
            var r = c.Rules;
            if (sk.Cooldown < 2 || !u.Alive || !(r.RewindWis > 0) || !Has(u, RuleModifiers.WardRewind) || !Has(u, RuleModifiers.ManaWard)) return;
            double wis = Eff(r, u, StatIds.Wis);
            double add = Math.Min(r.WardWis * wis - u.Ward, r.RewindWis * wis);
            if (!(add > 0)) return;
            u.Ward += add;
            c.Log?.Invoke(new FightAction(u.Id, RewindEvent, u.Id, add));
        }

        // ------------------------------------------------------------------ ward_toll

        /// <summary>The share of each blow's HP damage the unit's mana_ward ward takes: MON_WARD_SHARE, or more with ward_toll.</summary>
        static double WardShareOf(FightRules r, FightUnit u) =>
            Has(u, RuleModifiers.WardToll) ? Math.Max(r.WardShare, r.TollShare) : r.WardShare;

        /// <summary>ward_toll: the broken ward tolls once the blow is over (a blow that also kills it tolls nothing).</summary>
        static partial void OnWardBroke_GearbellJelly(RuleCtx c, FightUnit src, FightUnit tgt)
        {
            if (src.Tolling || tgt.TollsUsed >= c.Rules.TollMax || !Has(tgt, RuleModifiers.WardToll)) return;
            tgt.TollPending = true;
        }

        /// <summary>After any action: pending tolls ring, then wards whose re-form is due come back.</summary>
        static partial void AfterAction_GearbellJelly(RuleCtx c, FightUnit actor)
        {
            RingTolls(c);
            var s = c.State;
            var r = c.Rules;
            for (int i = 0; i < s.Units.Count; i++)
            {
                var u = s.Units[i];
                if (!u.TollReforming || u.Tally.Actions < u.TollReformAt) continue;
                u.TollReforming = false;
                if (!u.Alive) continue;
                double full = r.WardWis * Eff(r, u, StatIds.Wis);
                double ward = Math.Max(u.Ward, r.TollReform * full);
                if (!(ward > u.Ward)) continue;
                u.Ward = ward;
                c.Log?.Invoke(new FightAction(u.Id, WardEvent, u.Id, u.Ward));
            }
        }

        /// <summary>Every pending toll rings, in Units order; true when one dealt its damage.</summary>
        static bool RingTolls(RuleCtx c)
        {
            var s = c.State;
            bool rang = false;
            for (int i = 0; i < s.Units.Count; i++)
                if (s.Units[i].TollPending) rang |= Toll(c, s.Units[i]);
            return rang;
        }

        /// <summary>One toll of <paramref name="u"/>: the dark burst on every living enemy, then the re-form starts counting.</summary>
        static bool Toll(RuleCtx c, FightUnit u)
        {
            u.TollPending = false;
            var r = c.Rules;
            if (!u.Alive || u.TollsUsed >= r.TollMax) return false;
            u.TollsUsed++;
            u.TollReforming = true;
            u.TollReformAt = u.Tally.Actions + r.TollDelay;
            double raw0 = r.TollDmg * r.WardWis * Eff(r, u, StatIds.Wis);
            if (!(raw0 > 0)) return false;
            var foes = Enemies(c.State, u);
            u.Tolling = true;
            try
            {
                foreach (var e in foes)
                {
                    if (!e.Alive) continue;
                    bool lucky = false, graze = Roll(c.Rng, EvadeChance(r, Eff(r, e, StatIds.Dex), Eff(r, u, StatIds.Dex)),
                                                     Fortune(r, Eff(r, e, StatIds.Luk)), ref lucky);
                    double raw = raw0 * (graze ? r.GrazeMult : 1) * r.ElementMult(TollElement, e.Source.Element);
                    double dealt = Absorb(c, u, e, Mitigate(r, raw, Eff(r, e, StatIds.Wis)), TollElement);
                    c.Log?.Invoke(new FightAction(u.Id, TollEvent, e.Id, dealt));
                    Reborn(e, c.Log);
                }
            }
            finally { u.Tolling = false; }
            return true;
        }

        // ------------------------------------------------------------------ escapement

        /// <summary>escapement: on its gauge turns a skill with a cooldown is never ready (it runs on the clock).</summary>
        static partial void SkillReady_GearbellJelly(RuleCtx c, FightUnit u, int index, ref bool ready)
        {
            if (ready && !u.OnClock && u.Source.Skills[index].Cooldown > 0 && Has(u, RuleModifiers.Escapement)) ready = false;
        }

        /// <summary>escapement: the clock casts since its last turn are spent from this turn's extra actions.</summary>
        static partial void TurnBudget_GearbellJelly(RuleCtx c, FightUnit u, ref int spent)
        {
            if (!Has(u, RuleModifiers.Escapement)) return;
            spent += u.ClockCasts;
            u.ClockCasts = 0;
        }

        /// <summary>
        /// Each tick: tolls left pending by the ageing ring; then each living escapement carrier's clock runs (every TurnTicks ticks
        /// its cooldowns tick once, plus mana_ward's haste while the ward holds) and it casts at most one ready cooldown skill.
        /// </summary>
        static partial void OnTick_GearbellJelly(RuleCtx c, ref int side)
        {
            var s = c.State;
            var r = c.Rules;
            for (int n = 0; n < s.Units.Count; n++)
                if (s.Units[n].TollPending && Toll(c, s.Units[n])) side = s.Units[n].Side;
            for (int n = 0; n < s.Units.Count; n++)   // a clock cast may summon: the new unit is visited too
            {
                var u = s.Units[n];
                if (!u.Alive || !Has(u, RuleModifiers.Escapement)) continue;
                if (++u.ClockTicks >= r.TurnTicks)
                {
                    u.ClockTicks = 0;
                    int ticks = 1;
                    if (u.Ward > 0 && r.WardCdr > 0 && Has(u, RuleModifiers.ManaWard))
                    {
                        u.WardCdr += r.WardCdr;
                        while (u.WardCdr >= 1) { u.WardCdr -= 1; ticks++; }
                    }
                    for (int i = 0; i < u.Cooldowns.Length; i++) u.Cooldowns[i] = Math.Max(0, u.Cooldowns[i] - ticks);
                }
                if (u.ClockCasts >= r.ExtraActionsCap || u.BreakState == BreakBroken || !AnyEnemy(s, u)) continue;
                int pick = ClockPick(c, u, out string target);
                if (pick < 0) continue;
                ClockCast(c, u, pick, target);
                side = u.Side;
                RingTolls(c);   // a ward the cast broke (in team fights, an enemy jelly's) tolls now
            }
        }

        /// <summary>
        /// The cooldown skill the carrier casts on the clock now, or -1: first a gambit rule naming one whose condition holds and
        /// that can go (its target pick comes out in <paramref name="target"/>), then the default AI's order (heal and shield first,
        /// then the rest, each only when useful); a skill some gambit names is cast only by its rules, as on a turn.
        /// </summary>
        static int ClockPick(RuleCtx c, FightUnit u, out string target)
        {
            target = null;
            var r = c.Rules;
            var s = c.State;
            var skills = u.Source.Skills;
            var book = u.Source.Gambits;
            u.OnClock = true;
            try
            {
                foreach (var rule in book)
                {
                    if (rule.Skill == GambitRule.Basic) continue;
                    int i = GambitSkill(skills, rule.Skill);
                    if (i < 0 || skills[i].Cooldown <= 0 || !GambitHolds(s, u, rule) || !Ready(c, u, i) || !CanGo(r, s, u, skills[i])) continue;
                    target = rule.Target;
                    return i;
                }
                for (int pass = 0; pass < 2; pass++)
                    for (int i = 0; i < skills.Count; i++)
                    {
                        var sk = skills[i];
                        if ((sk.Kind == SkillKind.Heal || sk.Kind == SkillKind.Shield) != (pass == 0)) continue;
                        if (sk.Kind == SkillKind.Passive || sk.Cooldown <= 0 || !Ready(c, u, i) || !Useful(r, s, u, sk)) continue;
                        if (book.Count > 0 && Named(book, sk.Id)) continue;
                        return i;
                    }
                return -1;
            }
            finally { u.OnClock = false; }
        }

        /// <summary>A clock cast: logged, cooldown set, the skill used at MON_ESCAPE_POWER; never echoed; the skill-used hook runs.</summary>
        static void ClockCast(RuleCtx c, FightUnit u, int i, string target)
        {
            var r = c.Rules;
            var sk = u.Source.Skills[i];
            u.ClockCasts++;
            u.Cooldowns[i] = sk.Cooldown;
            c.Log?.Invoke(new FightAction(u.Id, EscapeEvent, u.Id, i));
            u.PickRule = target;
            try { Use(r, c.State, u, r.EscapePower == 1 ? sk : Echo(sk, r.EscapePower), c.Rng, c.Log, triggered: false); }
            finally { u.PickRule = null; }
            HookSkillUsed(c, u, i, sk);
        }

        static partial void HashUnit_GearbellJelly(CanonicalWriter w, FightState s, FightUnit u)
        {
            if (Has(u, RuleModifiers.WardToll)) { w.Bool(u.TollPending); w.I32(u.TollsUsed); w.Bool(u.TollReforming); w.I32(u.TollReformAt); }
            if (Has(u, RuleModifiers.Escapement)) { w.I32(u.ClockTicks); w.I32(u.ClockCasts); }
        }

        static partial void ValidateRules_GearbellJelly(FightRules r, ref bool ok)
        {
            if (!(r.RewindWis >= 0 && !double.IsInfinity(r.RewindWis) && r.TollDmg >= 0 && !double.IsInfinity(r.TollDmg) && In01(r.TollReform) && In01(r.TollShare)
                  && r.TollDelay >= 0 && r.TollMax >= 0 && r.EscapePower >= 0 && !double.IsInfinity(r.EscapePower)))
                ok = false;
        }
    }

    public sealed partial class FightRules
    {
        /// <summary>ward_rewind: the ward each cast of a cooldown-2+ skill restores, a multiple of WIS (MON_REWIND_WIS).</summary>
        public double RewindWis { get; set; }

        /// <summary>ward_toll: the toll's damage, a multiple of the ward's full size (MON_TOLL_DMG).</summary>
        public double TollDmg { get; set; }

        /// <summary>ward_toll: the share of each blow the carrier's ward takes, if more than MON_WARD_SHARE (MON_TOLL_SHARE).</summary>
        public double TollShare { get; set; }

        /// <summary>ward_toll: the ward re-forms at this fraction of its full size (MON_TOLL_REFORM).</summary>
        public double TollReform { get; set; }

        /// <summary>ward_toll: the unit's own actions until the ward re-forms (MON_TOLL_DELAY).</summary>
        public int TollDelay { get; set; }

        /// <summary>ward_toll: most tolls per fight (MON_TOLL_MAX).</summary>
        public int TollMax { get; set; }

        /// <summary>escapement: the power factor of a clock cast (MON_ESCAPE_POWER).</summary>
        public double EscapePower { get; set; } = 1;

        static partial void Load_GearbellJelly(FightRules r, ContentDb c)
        {
            var k = c.ConstantValues;
            r.RewindWis = k["MON_REWIND_WIS"];
            r.TollDmg = k["MON_TOLL_DMG"];
            r.TollReform = k["MON_TOLL_REFORM"];
            r.TollShare = k["MON_TOLL_SHARE"];
            r.TollDelay = (int)k["MON_TOLL_DELAY"];
            r.TollMax = (int)k["MON_TOLL_MAX"];
            r.EscapePower = k["MON_ESCAPE_POWER"];
        }
    }

    public sealed partial class FightUnit
    {
        /// <summary>ward_toll: the ward broke this action; it tolls once the blow is over.</summary>
        public bool TollPending { get; set; }

        /// <summary>ward_toll: tolls rung this fight.</summary>
        public int TollsUsed { get; set; }

        /// <summary>ward_toll: the ward re-forms once the unit's action count reaches <see cref="TollReformAt"/>.</summary>
        public bool TollReforming { get; set; }
        public int TollReformAt { get; set; }

        /// <summary>ward_toll: its toll is dealing damage now (that damage never tolls a ward it breaks). Not hashed: false between steps.</summary>
        internal bool Tolling { get; set; }

        /// <summary>escapement: ticks since its clock last ticked the cooldowns.</summary>
        public int ClockTicks { get; set; }

        /// <summary>escapement: clock casts since its last gauge turn (they count toward that turn's extra-action cap).</summary>
        public int ClockCasts { get; set; }

        /// <summary>escapement: a clock pick is asking whether a skill is ready. Not hashed: false between steps.</summary>
        internal bool OnClock { get; set; }
    }
}
