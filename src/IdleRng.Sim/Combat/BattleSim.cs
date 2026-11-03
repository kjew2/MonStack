using System;
using System.Collections.Generic;
using System.Text;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;

namespace IdleRng.Sim.Combat
{
    /// <summary>
    /// The monster battle (specs/16): two sides of 1..SideSize units, ATB turn order, automatic actions. Pure functions over a
    /// FightState plus a random source, so the same fighters, rules and draws give the same fight on the client, the bots and a
    /// server (specs/10 section 9.2). Partial: the trigger and rule guards are in BattleGuards.cs, the rule hooks in
    /// Rules/RuleHooks.cs and each idea-pool lane's rules in Rules/Lane*.cs (specs/16 section 6.4).
    /// </summary>
    public static partial class BattleSim
    {
        public const string StreamName = "battle_sim";
        public const int HashVersion = 2;   // v2: UnitTally.Grazes (specs/25)
        const int MaxActionsPerTick = 64;
        static readonly byte[] HashMagic = Encoding.ASCII.GetBytes("IDLERNG-FIGHTHASH-v1");

        // ------------------------------------------------------------------ setup

        /// <summary>
        /// A fight ready to step. Validates both sides (throws ArgumentException) and derives battle values from the stat sheets.
        /// BattleStart triggers fire on the first Step, so they can draw from the same random source as the rest of the fight.
        /// </summary>
        public static FightState Start(FightRules rules, IReadOnlyList<Fighter> sideA, IReadOnlyList<Fighter> sideB)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            Validate(rules);
            var s = new FightState { Tower = rules.Tower != null };
            var ids = new HashSet<string>(StringComparer.Ordinal);
            AddSide(rules, s, sideA, 0, ids, nameof(sideA));
            AddSide(rules, s, sideB, 1, ids, nameof(sideB));
            return s;
        }

        static void Validate(FightRules r)
        {
            if (!(r.GaugeSize > 0 && r.TurnTicks > 0 && r.TimeLimitTicks > 0 && r.SideSize > 0 && r.HpPerPoint > 0 && r.DefK > 0
                  && r.SpdMin > 0 && r.CritDmg >= 1 && r.CritDmgPerDex >= 0 && In01(r.GrazeMult) && r.FortuneK > 0 && In01(r.FortuneCap) && r.StatFloor > 0 && r.StatFloor <= 1 && r.StackCap >= 1))
                throw new ArgumentException("fight rules out of range (specs/16 section 7)", nameof(r));
            if (!(In01(r.EchoPower) && In01(r.TideRegen) && In01(r.TideShieldCap) && r.DrainFrac >= 0 && In01(r.FirePierce) && r.DoubleTurns >= 0
                  && In01(r.RebirthHeal) && In01(r.WailStagger) && In01(r.WailWither) && In01(r.VeilEvadeCap) && r.FeastStr >= 0
                  && r.CounterMult >= 0 && r.FireBonus >= 0 && In01(r.VeilEvadeBonus) && r.FeastDmg >= 0 && r.FeastStart >= 0 && r.EchoDecoys >= 0
                  && In01(r.DrainShieldCap) && r.TriggerDepth >= 1 && r.ExtraActionsCap >= 0))
                throw new ArgumentException("rule modifier constants out of range (specs/16 section 7)", nameof(r));
            bool lanesOk = true;
            HookValidateRules(r, ref lanesOk);
            if (!lanesOk) throw new ArgumentException("rule modifier constants out of range (specs/16 section 7)", nameof(r));
            if (r.Tower != null && !r.Tower.Valid) throw new ArgumentException("tower rules out of range (specs/21 section 5)", nameof(r));
            if (r.Mutators != null)
                foreach (var kv in r.Mutators.Element)   // the rest is checked by FightMutators.Add; this table can be written directly
                    if (kv.Key == null || !(kv.Value >= 0) || double.IsInfinity(kv.Value))
                        throw new ArgumentException("element_mult mutators need an element and a finite value >= 0 (specs/28 section 3.2)", nameof(r));
        }

        static void AddSide(FightRules rules, FightState s, IReadOnlyList<Fighter> side, int sideIndex, HashSet<string> ids, string name)
        {
            if (side == null) throw new ArgumentNullException(name);
            if (side.Count < 1 || side.Count > rules.SideSize)
                throw new ArgumentException($"a side needs 1..{rules.SideSize} fighters, got {side.Count}", name);
            for (int i = 0; i < side.Count; i++)
            {
                var f = side[i];
                ValidateFighter(rules, f, name, i);
                if (!ids.Add(f.Id)) throw new ArgumentException($"fighter id '{f.Id}' appears twice", name);
                s.Units.Add(NewUnit(rules, f, sideIndex, i));
            }
        }

        static FightUnit NewUnit(FightRules rules, Fighter f, int side, int slot)
        {
            var u = new FightUnit
            {
                Source = f, Side = side, Slot = slot,
                MaxHp = MaxHp(rules, f.Stats.Hp),
                Cooldowns = new int[f.Skills.Count],
            };
            u.BaseMaxHp = u.MaxHp;
            u.Hp = u.MaxHp;
            if (Has(u, RuleModifiers.RootRebirth)) u.RebirthHeal = rules.RebirthHeal;
            if (Has(u, RuleModifiers.EchoCast)) u.DecoysLeft = rules.EchoDecoys;
            if (Has(u, RuleModifiers.NightFeast)) u.FeastDmgBonus = rules.FeastStart;
            if (rules.Mutators != null && rules.Mutators.StartShield > 0) u.Shield = rules.Mutators.StartShield * u.MaxHp;   // no expiry
            return u;
        }

        static void ValidateFighter(FightRules rules, Fighter f, string side, int slot)
        {
            string at = $"{side}[{slot}]";
            if (f == null) throw new ArgumentException($"{at} is null", side);
            if (string.IsNullOrEmpty(f.Id)) throw new ArgumentException($"{at} needs an id", side);
            if (f.Stats == null || f.Skills == null || f.Triggers == null || f.Rules == null || f.Phases == null || f.Gambits == null)
                throw new ArgumentException($"{at} ({f.Id}): Stats, Skills, Triggers, Rules, Phases and Gambits must not be null", side);
            foreach (var id in StatIds.Trained)
            {
                double v = f.Stats.Get(id);
                if (!(v >= 0) || double.IsInfinity(v)) throw new ArgumentException($"{at} ({f.Id}): stat {id} must be finite and >= 0", side);
            }
            foreach (var sk in f.Skills) ValidateSkill(sk, at, f.Id, side);
            foreach (var t in f.Triggers)
            {
                if (t == null || t.Skill == null) throw new ArgumentException($"{at} ({f.Id}): null trigger", side);
                ValidateSkill(t.Skill, at, f.Id, side);
            }
            foreach (var r in f.Rules)
                if (!RuleModifiers.IsKnown(r)) throw new ArgumentException($"{at} ({f.Id}): unknown rule modifier '{r}'", side);
            ValidatePhases(rules, f, at, side);
            ValidateGambits(f, at, side);
        }

        /// <summary>Each rule: a known condition with a value in range, one of the unit's active skills or "basic", a known target.</summary>
        static void ValidateGambits(Fighter f, string at, string side)
        {
            for (int g = 0; g < f.Gambits.Count; g++)
            {
                var rule = f.Gambits[g];
                string where = $"{at} ({f.Id}) gambit {g}";
                if (rule == null) throw new ArgumentException($"{where} is null", side);
                if (!GambitConditions.IsKnown(rule.When)) throw new ArgumentException($"{where}: unknown condition '{rule.When}'", side);
                if (GambitConditions.TakesFraction(rule.When) && !(rule.Value > 0 && rule.Value <= 1))
                    throw new ArgumentException($"{where}: '{rule.When}' needs an HP fraction in (0, 1]", side);
                if (GambitConditions.TakesCount(rule.When) && !(rule.Value >= 1 && rule.Value <= int.MaxValue && rule.Value == Math.Floor(rule.Value)))
                    throw new ArgumentException($"{where}: '{rule.When}' needs a whole count of at least 1", side);
                if (!GambitTargets.IsKnown(rule.Target)) throw new ArgumentException($"{where}: unknown target '{rule.Target}'", side);
                if (rule.Skill != GambitRule.Basic && GambitSkill(f.Skills, rule.Skill) < 0)
                    throw new ArgumentException($"{where}: '{rule.Skill}' is not one of the unit's active skills", side);
            }
        }

        /// <summary>The index of the first active (non-passive) skill with this id, or -1.</summary>
        static int GambitSkill(List<CombatSkill> skills, string id)
        {
            for (int i = 0; i < skills.Count; i++) if (skills[i].Id == id && skills[i].Kind != SkillKind.Passive) return i;
            return -1;
        }

        static void ValidatePhases(FightRules rules, Fighter f, string at, string side)
        {
            if (f.Phases.Count == 0) return;
            if (rules.Tower == null) throw new ArgumentException($"{at} ({f.Id}): boss phases need FightRules.Tower", side);
            double prev = 1;
            foreach (var p in f.Phases)
            {
                if (p == null || !PhaseKinds.IsKnown(p.Kind) || p.AddSkills == null)
                    throw new ArgumentException($"{at} ({f.Id}): unknown or null boss phase", side);
                if (!(p.EnterHp > 0 && p.EnterHp < prev))
                    throw new ArgumentException($"{at} ({f.Id}): phase enter_hp must be strictly decreasing and inside (0, 1)", side);
                prev = p.EnterHp;
                bool ok;
                switch (p.Kind)
                {
                    case PhaseKinds.ShieldCheck: ok = p.Power > 0 && p.Duration > 0 && p.HealFrac >= 0 && p.Buff >= 0; break;
                    case PhaseKinds.Split: ok = p.Count >= 1 && p.Guard >= 0 && p.Guard <= 1 && p.Add != null; break;
                    case PhaseKinds.Channel: ok = p.Power > 0 && p.Duration > 0 && p.Strike >= 0; break;
                    default: ok = p.Buff >= 0; break;
                }
                if (!ok) throw new ArgumentException($"{at} ({f.Id}): phase '{p.Kind}' has out-of-range values (specs/21 section 3.2)", side);
                if (p.Add != null)
                {
                    ValidateFighter(rules, p.Add, side, 0);
                    if (p.Add.Phases.Count > 0) throw new ArgumentException($"{at} ({f.Id}): an add cannot have phases", side);
                }
                foreach (var sk in p.AddSkills) ValidateSkill(sk, at, f.Id, side);
            }
        }

        static void ValidateSkill(CombatSkill sk, string at, string fid, string side)
        {
            if (sk == null) throw new ArgumentException($"{at} ({fid}): null skill", side);
            string where = $"{at} ({fid}) skill '{sk.Id}'";
            if (!(sk.Power >= 0) || double.IsInfinity(sk.Power) || sk.Cooldown < 0 || sk.Duration < 0)
                throw new ArgumentException($"{where}: power must be finite and >= 0, cooldown and duration >= 0", side);
            bool needsStat = sk.Kind == SkillKind.Buff || sk.Kind == SkillKind.Debuff || sk.Kind == SkillKind.Passive;
            if (needsStat && !StatIds.IsEffectStat(sk.Stat)) throw new ArgumentException($"{where}: stat '{sk.Stat}' is not an effect stat", side);
            bool needsScales = sk.Kind == SkillKind.Damage || sk.Kind == SkillKind.Heal || sk.Kind == SkillKind.Shield;
            if (needsScales && !StatIds.IsTrained(sk.Scales)) throw new ArgumentException($"{where}: scales '{sk.Scales}' is not a trained stat", side);
        }

        /// <summary>
        /// Run a whole fight on a fresh stream seeded with <paramref name="seed"/>. <paramref name="onStart"/> sees the new state
        /// before the first tick, so an observer (the web bridge) can read live HP and cooldowns from inside
        /// <paramref name="onAction"/>; observers must not change the state.
        /// </summary>
        public static FightResult Resolve(FightRules rules, IReadOnlyList<Fighter> sideA, IReadOnlyList<Fighter> sideB, ulong seed,
                                          Action<FightAction> onAction = null, Action<FightState> onStart = null)
        {
            var s = Start(rules, sideA, sideB);
            onStart?.Invoke(s);
            var rng = new RngStream(StreamName, seed);
            while (Step(rules, s, rng, onAction) == FightOutcome.Ongoing) { }
            return new FightResult(s, StateHash(s));
        }

        // ------------------------------------------------------------------ one tick

        /// <summary>
        /// Advance one tick: fill gauges, run every action that is due (highest gauge first, ties in Units order), age timed
        /// effects, then check the turn limit. Returns the outcome; a finished fight is left unchanged.
        /// </summary>
        public static FightOutcome Step(FightRules rules, FightState s, IRandomSource rng, Action<FightAction> onAction = null)
        {
            if (s.Outcome != FightOutcome.Ongoing) return s.Outcome;
            var ctx = new RuleCtx(rules, s, rng, onAction);
            if (!s.Started)
            {
                s.Started = true;
                HookFightStart(ctx);
                foreach (var u in s.Units) Fire(rules, s, u, FightTrigger.BattleStart, rng, onAction);
                ProcessDeaths(ctx);
                var start = Decide(s, 0);
                if (start != FightOutcome.Ongoing) return s.Outcome = start;
            }
            s.Ticks++;
            if (s.Tower) s.PeakCorruption = Math.Max(s.PeakCorruption, CorruptionLevel(rules, s.Ticks));
            foreach (var u in s.Units)
                if (u.Alive)
                {
                    double fill = GaugeFill(rules, Eff(rules, u, StatIds.Spd)) * (rules.Mutators?.Speed ?? 1);
                    HookGaugeTick(ctx, u, ref fill);
                    u.Gauge += fill;
                }

            for (int n = 0; n < MaxActionsPerTick; n++)
            {
                var actor = NextActor(s, rules.GaugeSize);
                if (actor == null) break;
                actor.Gauge -= rules.GaugeSize;
                // One turn: the action the gauge paid for, then at most MON_EXTRA_ACTIONS_CAP extra actions (specs/16 section 6.4).
                string extra = null;
                int extras = 0;
                do
                {
                    if (extra != null) onAction?.Invoke(new FightAction(actor.Id, extra, actor.Id, 0));
                    actor.TurnAction = extras;
                    int counted = actor.Tally.Actions;
                    Act(rules, s, actor, rng, onAction);
                    bool acted = actor.Tally.Actions > counted;   // a rule hook may skip the action (BeforeAction): no extra action follows it
                    HookAfterAction(ctx, actor);
                    ProcessDeaths(ctx);
                    CheckPhases(rules, s, rng, onAction);
                    var o = Decide(s, actor.Side);
                    if (o != FightOutcome.Ongoing) return s.Outcome = o;
                    extra = acted && extras < rules.ExtraActionsCap ? ExtraAction(ctx, actor) : null;
                    extras++;
                } while (extra != null);
            }

            int strikeSide = AgeAll(rules, s, rng, onAction);
            ProcessDeaths(ctx);
            if (strikeSide >= 0)
            {
                var o = Decide(s, strikeSide);
                if (o != FightOutcome.Ongoing) return s.Outcome = o;
            }
            if (s.Ticks >= rules.TimeLimitTicks) s.Outcome = FightOutcome.TimedOut;
            return s.Outcome;
        }

        /// <summary>
        /// The event of the extra action <paramref name="u"/> takes now, or null for none. double_turn_start first (after each of
        /// the unit's first DoubleTurns actions, an odd action count, it acts again at once), then the lanes' sources in lane order.
        /// The caller applies MON_EXTRA_ACTIONS_CAP.
        /// </summary>
        static string ExtraAction(RuleCtx c, FightUnit u)
        {
            if (!u.Alive) return null;
            if (u.Tally.Actions % 2 == 1 && u.Tally.Actions < 2 * c.Rules.DoubleTurns && Has(u, RuleModifiers.DoubleTurnStart))
                return FightAction.DoubleTurnEvent;
            string evt = null;
            HookExtraAction(c, u, ref evt);
            return evt;
        }

        static FightUnit NextActor(FightState s, double gauge)
        {
            FightUnit best = null;
            foreach (var u in s.Units)
                if (u.Alive && u.Gauge >= gauge && (best == null || u.Gauge > best.Gauge)) best = u;
            return best;
        }

        /// <summary>A side wins when the other has no living unit. If one action wipes both, the acting side wins.</summary>
        static FightOutcome Decide(FightState s, int actingSide)
        {
            bool aAlive = SideAlive(s, 0), bAlive = SideAlive(s, 1);
            if (aAlive && bAlive) return FightOutcome.Ongoing;
            if (!aAlive && !bAlive) return actingSide == 0 ? FightOutcome.SideAWon : FightOutcome.SideBWon;
            return aAlive ? FightOutcome.SideAWon : FightOutcome.SideBWon;
        }

        static bool SideAlive(FightState s, int side)
        {
            foreach (var u in s.Units) if (u.Side == side && u.Alive && !u.IsAdd) return true;
            return false;
        }

        /// <summary>
        /// End of tick: age timed effects, shields and taunts, then the tower's timed parts (specs/21 section 3.2): the enrage
        /// buff, a shield_check shield's expiry and a channel's countdown. Returns the side of a unit whose channel strike landed
        /// (the caller re-checks the outcome), or -1.
        /// </summary>
        static int AgeAll(FightRules r, FightState s, IRandomSource rng, Action<FightAction> onAction)
        {
            var ctx = new RuleCtx(r, s, rng, onAction);
            int strikeSide = -1;
            foreach (var u in s.Units)
            {
                if (u.EnrageStep > 0 && u.Alive && s.Ticks % r.TurnTicks == 0) BumpEnrage(r, u);
                Age(r, u);
                if (u.CheckPending && u.Shield <= 0) u.CheckPending = false;   // broken in time
                if (u.ShieldTicks > 0 && --u.ShieldTicks == 0)
                {
                    if (u.CheckPending && u.Alive) CheckFailed(r, s, u, onAction);
                    u.CheckPending = false;
                    u.Shield = 0;
                }
                if (u.ChannelTicks > 0 && u.Alive)
                {
                    if (u.ChannelDealt >= u.ChannelNeed)
                    {
                        u.ChannelTicks = 0;
                        onAction?.Invoke(new FightAction(u.Id, "channel_broken", null, u.ChannelDealt));
                    }
                    else if (--u.ChannelTicks == 0)
                    {
                        double amt = u.ChannelStrike * Math.Max(Eff(r, u, StatIds.Str), Eff(r, u, StatIds.Int)), total = 0;
                        var hit = Enemies(s, u);
                        foreach (var e in hit) total += Absorb(ctx, u, e, amt, null);
                        onAction?.Invoke(new FightAction(u.Id, "channel_strike", null, total));
                        foreach (var e in hit) Reborn(e, onAction);
                        strikeSide = u.Side;
                    }
                }
            }
            return strikeSide;
        }

        static void Age(FightRules r, FightUnit u)
        {
            bool hpChanged = false;
            for (int i = u.Effects.Count - 1; i >= 0; i--)
            {
                var e = u.Effects[i];
                if (e.TicksLeft < 0) continue;
                if (--e.TicksLeft <= 0)
                {
                    u.Effects.RemoveAt(i);
                    hpChanged |= e.Stat == StatIds.Hp;
                }
            }
            if (hpChanged) RecalcMaxHp(r, u);
            if (u.TauntTicks > 0) u.TauntTicks--;
        }

        static void CheckFailed(FightRules r, FightState s, FightUnit u, Action<FightAction> onAction)
        {
            double heal = Math.Min(u.MaxHp - u.Hp, u.CheckHealFrac * u.MaxHp * HealScale(r, s));
            u.Hp += heal;
            if (u.CheckBuff > 0) AddEffect(r, u, StatIds.DmgDealt, u.CheckBuff, -1, "phase_check");
            onAction?.Invoke(new FightAction(u.Id, "check_failed", null, heal));
        }

        /// <summary>The enrage phase's buff: one permanent effect that grows by EnrageStep every turn.</summary>
        static void BumpEnrage(FightRules r, FightUnit u)
        {
            foreach (var e in u.Effects)
                if (e.Source == "phase_enrage" && e.Stat == StatIds.DmgDealt) { e.Frac += u.EnrageStep; return; }
            AddEffect(r, u, StatIds.DmgDealt, u.EnrageStep, -1, "phase_enrage");
        }

        // ------------------------------------------------------------------ triggers

        /// <summary>Every effect a unit carries for <paramref name="trigger"/>: passives at BattleStart, then its trigger list.</summary>
        public static void CollectTriggers(FightUnit u, FightTrigger trigger, List<CombatSkill> into)
        {
            if (trigger == FightTrigger.BattleStart)
                foreach (var sk in u.Source.Skills)
                    if (sk.Kind == SkillKind.Passive) into.Add(sk);
            foreach (var t in u.Source.Triggers)
                if (t.Trigger == trigger) into.Add(t.Skill);
        }

        static void Fire(FightRules r, FightState s, FightUnit u, FightTrigger trigger, IRandomSource rng, Action<FightAction> onAction)
        {
            if (!u.Alive) return;
            // A trigger can fire another trigger (OnCrit inside an OnAction effect), so take a private copy of the list.
            var shared = s.Scratch;
            shared.Clear();
            CollectTriggers(u, trigger, shared);
            if (shared.Count == 0) return;
            var list = shared.ToArray();
            // Loop guards (specs/16 section 6.4): never inside an effect of the same trigger, never past MON_TRIGGER_DEPTH.
            if (!EnterGuard(r, s, (GuardKind)trigger, true, out var saved)) return;
            try
            {
                foreach (var sk in list)
                    if (u.Alive)
                    {
                        Use(r, s, u, sk, rng, onAction, triggered: true);
                        if (trigger != FightTrigger.BattleStart) GainEntropy(r, s, u, r.Tower?.EntropyPerTrigger ?? 0, rng, onAction);
                    }
            }
            finally { ExitGuard(s, saved); }
        }

        // ------------------------------------------------------------------ actions

        static void Act(FightRules r, FightState s, FightUnit u, IRandomSource rng, Action<FightAction> onAction)
        {
            var ctx = new RuleCtx(r, s, rng, onAction);
            bool skip = false;
            HookBeforeAction(ctx, u, ref skip);
            if (skip || !u.Alive) return;
            u.Tally.Actions++;
            if (r.Tower != null) u.Entropy = Math.Max(0, u.Entropy - r.Tower.EntropyDecay);
            if (r.TideRegen > 0 && Has(u, RuleModifiers.TidalOverflow))
            {
                double shield = 0, healed = Heal(r, u, r.TideRegen * u.MaxHp * HealScale(r, s), ref shield);
                u.Tally.HealingDone += healed;
                if (healed + shield > 0) onAction?.Invoke(new FightAction(u.Id, FightAction.TideEvent, u.Id, healed + shield));
            }
            if ((r.WailStagger > 0 || r.WailWither > 0) && Has(u, RuleModifiers.WailStagger))
            {
                foreach (var e in Enemies(s, u))
                {
                    e.Gauge -= Math.Min(e.Gauge, r.WailStagger * r.GaugeSize);
                    double dmg = Absorb(ctx, u, e, r.WailWither * e.MaxHp, null);
                    onAction?.Invoke(new FightAction(u.Id, FightAction.StaggerEvent, e.Id, dmg));
                    Reborn(e, onAction);
                }
                if (!SideAlive(s, 1 - u.Side)) return;
            }
            Fire(r, s, u, FightTrigger.OnAction, rng, onAction);
            if (!u.Alive) return;
            for (int i = 0; i < u.Cooldowns.Length; i++) if (u.Cooldowns[i] > 0) u.Cooldowns[i]--;
            var skills = u.Source.Skills;
            var book = u.Source.Gambits;
            if (book.Count > 0 && ActGambit(r, s, u, rng, onAction)) return;
            // Rescue pass first (heal, shield), then everything else; each pass is in list order. A skill some gambit names is
            // used only by its rules (specs/28 section 4.3).
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < skills.Count; i++)
                {
                    var sk = skills[i];
                    if ((sk.Kind == SkillKind.Heal || sk.Kind == SkillKind.Shield) != (pass == 0)) continue;
                    if (sk.Kind == SkillKind.Passive || !Ready(ctx, u, i) || !Useful(r, s, u, sk)) continue;
                    if (book.Count > 0 && Named(book, sk.Id)) continue;
                    Cast(r, s, u, i, rng, onAction);
                    return;
                }
            BasicAttack(r, s, u, rng, onAction);
        }

        /// <summary>Use the unit's skill at index <paramref name="i"/> as its action: start its cooldown, use it, echo it.</summary>
        static void Cast(FightRules r, FightState s, FightUnit u, int i, IRandomSource rng, Action<FightAction> onAction)
        {
            var sk = u.Source.Skills[i];
            u.Cooldowns[i] = sk.Cooldown;
            Use(r, s, u, sk, rng, onAction, triggered: false);
            if (u.Alive && Has(u, RuleModifiers.EchoCast)
                && (sk.Kind == SkillKind.Damage || sk.Kind == SkillKind.Heal || sk.Kind == SkillKind.Shield)
                && EnterGuard(r, s, GuardKind.Echo, false, out var saved))   // an echo is part of the cast: no depth, never echoed again
            {
                try
                {
                    Use(r, s, u, Echo(sk, r.EchoPower), rng, onAction, triggered: false);
                    GainEntropy(r, s, u, r.Tower?.EntropyPerEcho ?? 0, rng, onAction);
                }
                finally { ExitGuard(s, saved); }
            }
            HookSkillUsed(new RuleCtx(r, s, rng, onAction), u, i, sk);
        }

        // ------------------------------------------------------------------ gambits (specs/28 section 4)

        /// <summary>
        /// The first rule whose condition holds and whose action can go now (the skill is ready and has something to do) is the
        /// unit's action: logged as a "gambit" event (Amount = the rule's index), then used with the rule's target pick. False when
        /// no rule fires: the default AI acts instead.
        /// </summary>
        static bool ActGambit(FightRules r, FightState s, FightUnit u, IRandomSource rng, Action<FightAction> onAction)
        {
            var book = u.Source.Gambits;
            for (int g = 0; g < book.Count; g++)
            {
                var rule = book[g];
                if (!GambitHolds(s, u, rule)) continue;
                int i = -1;
                if (rule.Skill != GambitRule.Basic)
                {
                    i = GambitSkill(u.Source.Skills, rule.Skill);
                    if (i < 0 || !Ready(new RuleCtx(r, s, rng, onAction), u, i) || !CanGo(r, s, u, u.Source.Skills[i])) continue;
                }
                onAction?.Invoke(new FightAction(u.Id, FightAction.GambitEvent, null, g));
                u.PickRule = rule.Target;
                if (i < 0) BasicAttack(r, s, u, rng, onAction);
                else Cast(r, s, u, i, rng, onAction);
                u.PickRule = null;
                return true;
            }
            return false;
        }

        static bool GambitHolds(FightState s, FightUnit u, GambitRule rule)
        {
            switch (rule.When)
            {
                case GambitConditions.Always: return true;
                case GambitConditions.SelfHpBelow: return u.Hp / u.MaxHp < rule.Value;
                case GambitConditions.AllyHpBelow:
                    foreach (var a in s.Units) if (a.Side == u.Side && a.Alive && a.Hp / a.MaxHp < rule.Value) return true;
                    return false;
                case GambitConditions.EnemyHpBelow:
                    foreach (var e in s.Units) if (e.Side != u.Side && e.Alive && e.Hp / e.MaxHp < rule.Value) return true;
                    return false;
                case GambitConditions.EnemiesAtLeast: return LivingOnSide(s, 1 - u.Side) >= rule.Value;
                case GambitConditions.FirstActions: return u.Tally.Actions <= rule.Value;
                default: return false;
            }
        }

        /// <summary>
        /// Whether a skill a rule names has something to do: the rule's condition replaces the default AI's timing (a heal needs
        /// only a hurt target, a shield or damage skill a living enemy), but a taunt, buff or debuff is not cast where it would
        /// change nothing, exactly as the default AI checks it.
        /// </summary>
        static bool CanGo(FightRules r, FightState s, FightUnit u, CombatSkill sk)
        {
            switch (sk.Kind)
            {
                case SkillKind.Damage:
                case SkillKind.Shield: return AnyEnemy(s, u);
                case SkillKind.Heal:
                    if (sk.Target != SkillTarget.LowestAlly && sk.Target != SkillTarget.AllAllies) return u.Hp < u.MaxHp;
                    foreach (var a in s.Units) if (a.Side == u.Side && a.Alive && a.Hp < a.MaxHp) return true;
                    return false;
                default: return Useful(r, s, u, sk);
            }
        }

        static bool Named(List<GambitRule> book, string skillId)
        {
            foreach (var rule in book) if (rule.Skill == skillId) return true;
            return false;
        }

        /// <summary>A gambit's pick among <paramref name="list"/> by HP fraction (lowest slot on a tie), or null for "auto".</summary>
        static FightUnit GambitPick(List<FightUnit> list, string pick)
        {
            if (pick == null || pick == GambitTargets.Auto || list.Count == 0) return null;
            bool lowest = pick == GambitTargets.LowestHp;
            FightUnit best = null;
            foreach (var e in list)
            {
                if (best == null) { best = e; continue; }
                double f = e.Hp / e.MaxHp, b = best.Hp / best.MaxHp;
                if (lowest ? f < b : f > b) best = e;
            }
            return best;
        }

        /// <summary>Basic attack: BasicAttackPower x the higher of STR and INT; magical when INT is higher.</summary>
        static void BasicAttack(FightRules r, FightState s, FightUnit u, IRandomSource rng, Action<FightAction> onAction)
        {
            Fire(r, s, u, FightTrigger.OnBasicAttack, rng, onAction);
            if (!u.Alive) return;
            var target = PickEnemy(s, u, rng);
            if (target == null) return;
            double str = Eff(r, u, StatIds.Str), intel = Eff(r, u, StatIds.Int);
            Hit(r, s, u, target, BasicAttackRaw(r, str, intel), BasicAttackMagical(str, intel), u.Source.Element, null, false, rng, onAction);
        }

        /// <summary>
        /// Default AI (specs/16 section 5.2): the first ready skill that would do something: heal and shield first, then the rest,
        /// each in list order. A heal waits until a target is missing the whole amount it restores (HealNeed); a shield is cast ahead
        /// of damage, whenever it would leave a target with more shield than it has (WantsShield).
        /// </summary>
        static bool Useful(FightRules r, FightState s, FightUnit u, CombatSkill sk)
        {
            switch (sk.Kind)
            {
                case SkillKind.Damage: return AnyEnemy(s, u);
                case SkillKind.Heal:
                {
                    double amt = Scaled(r, u, sk) * HealScale(r, s);
                    if (sk.Target != SkillTarget.LowestAlly && sk.Target != SkillTarget.AllAllies) return WantsHeal(r, u, amt);
                    foreach (var a in s.Units) if (a.Side == u.Side && WantsHeal(r, a, amt)) return true;
                    return false;
                }
                case SkillKind.Shield: return AnyEnemy(s, u) && WantsShield(s, u, sk, Scaled(r, u, sk) * HealScale(r, s));
                case SkillKind.Taunt: return u.TauntTicks == 0 && AnyOtherAlly(s, u);   // nobody to protect: a wasted action
                case SkillKind.Buff:
                {
                    var t = sk.Target == SkillTarget.LowestAlly ? LowestAlly(s, u) : u;
                    return t != null && !HasEffectFrom(t, sk.Id);
                }
                case SkillKind.Debuff:
                    foreach (var e in s.Units) if (e.Side != u.Side && e.Alive && !HasEffectFrom(e, sk.Id)) return true;
                    return false;
                default: return false;
            }
        }

        /// <summary>
        /// Use one skill or triggered effect. Kit skills and triggers all come through here. Passive = permanent stat change;
        /// a triggered effect of any other kind does what the skill would do.
        /// </summary>
        static void Use(FightRules r, FightState s, FightUnit src, CombatSkill sk, IRandomSource rng, Action<FightAction> onAction, bool triggered)
        {
            int ticks = sk.Duration * r.TurnTicks;
            bool aoe = sk.Target == SkillTarget.AllEnemies || sk.Target == SkillTarget.AllAllies;
            switch (sk.Kind)
            {
                case SkillKind.Damage:
                {
                    double raw = Scaled(r, src, sk);
                    string element = sk.Element ?? src.Source.Element;
                    if (sk.Target == SkillTarget.AllEnemies)
                    {
                        foreach (var t in Enemies(s, src))
                            if (src.Alive && t.Alive) Hit(r, s, src, t, raw, sk.Magical, element, sk, true, rng, onAction);
                    }
                    else
                    {
                        var t = PickEnemy(s, src, rng);
                        if (t != null) Hit(r, s, src, t, raw, sk.Magical, element, sk, false, rng, onAction);
                    }
                    break;
                }
                case SkillKind.Heal:
                {
                    double amt = Scaled(r, src, sk) * HealScale(r, s), total = 0;
                    var targets = sk.Target == SkillTarget.LowestAlly ? HealTarget(r, s, src, amt) : Allies(s, src, sk.Target);
                    double shield = 0;
                    foreach (var t in targets) total += Heal(r, t, amt, ref shield);
                    src.Tally.HealingDone += total;
                    onAction?.Invoke(new FightAction(src.Id, sk, targets.Count == 1 ? targets[0].Id : null, total, false, false, aoe, false));
                    break;
                }
                case SkillKind.Shield:
                {
                    double amt = Scaled(r, src, sk) * HealScale(r, s);
                    var targets = Allies(s, src, sk.Target);
                    var ctx = new RuleCtx(r, s, rng, onAction);
                    foreach (var t in targets) GainShield(ctx, t, amt, ticks);
                    onAction?.Invoke(new FightAction(src.Id, sk, targets.Count == 1 ? targets[0].Id : null, amt, false, false, aoe, false));
                    break;
                }
                case SkillKind.Taunt:
                    src.TauntTicks = Math.Max(src.TauntTicks, ticks);
                    onAction?.Invoke(new FightAction(src.Id, sk, src.Id, 0, false, false, false, false));
                    break;
                case SkillKind.Buff:
                case SkillKind.Passive:
                {
                    bool permanent = sk.Kind == SkillKind.Passive;
                    foreach (var t in Allies(s, src, sk.Target))
                        AddEffect(r, t, sk.Stat, sk.Power, permanent ? -1 : ticks, sk.Id);
                    if (!permanent || triggered) onAction?.Invoke(new FightAction(src.Id, sk, null, sk.Power, false, false, aoe, false));
                    break;
                }
                case SkillKind.Debuff:
                {
                    if (sk.Target == SkillTarget.AllEnemies)
                        foreach (var t in Enemies(s, src)) AddEffect(r, t, sk.Stat, -sk.Power, ticks, sk.Id);
                    else
                    {
                        var t = PickDebuffTarget(s, src, sk.Id, rng);
                        if (t != null) AddEffect(r, t, sk.Stat, -sk.Power, ticks, sk.Id);
                    }
                    onAction?.Invoke(new FightAction(src.Id, sk, null, sk.Power, false, false, aoe, false));
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ damage

        /// <summary>
        /// One hit (specs/16 section 5.3): the before-hit hook, the decoy, the conversion step (specs/16 section 6.4), avoid roll
        /// (a physical hit misses, a magical one grazes unless umbral_veil makes it a miss), crit roll (none on a graze), element,
        /// the damage multipliers, mitigation K/(K+DEF or WIS), shield, tallies, then the landed and taken hooks, OnHitTaken /
        /// OnCrit triggers and the counter rule (OnEvade on an evade). Fortune (LUK) rerolls a failed evade and a failed crit of its
        /// owner, and a crit landed against its owner. Draw order: see <see cref="Roll"/>. Returns the damage dealt.
        /// </summary>
        static double Hit(FightRules r, FightState s, FightUnit src, FightUnit tgt, double raw, bool magical, string element, CombatSkill sk,
                          bool aoe, IRandomSource rng, Action<FightAction> onAction)
        {
            var ctx = new RuleCtx(r, s, rng, onAction);
            var h = new HitInfo
            {
                Src = src, Tgt = tgt, Raw = raw, Magical = magical, Element = element, Skill = sk, Aoe = aoe, Triggered = s.TriggerDepth > 0,
            };
            HookBeforeHit(ctx, h);
            if (h.Cancelled) return 0;
            if (tgt.DecoysLeft > 0)   // echo_cast's mirror twin takes the hit instead
            {
                tgt.DecoysLeft--;
                onAction?.Invoke(new FightAction(src.Id, sk, tgt.Id, 0, false, true, aoe, false));
                onAction?.Invoke(new FightAction(tgt.Id, FightAction.DecoyEvent, tgt.Id, tgt.DecoysLeft));
                return 0;
            }

            // The conversion step: once per hit, before every multiplier and before the avoid roll.
            bool veil = Has(tgt, RuleModifiers.UmbralVeil);
            h.Evade = veil ? Clamp(EvadeChance(r, Eff(r, tgt, StatIds.Dex), Eff(r, src, StatIds.Dex), 1) + r.VeilEvadeBonus, 0, r.VeilEvadeCap)
                           : EvadeChance(r, Eff(r, tgt, StatIds.Dex), Eff(r, src, StatIds.Dex));
            h.Mitigation = Eff(r, tgt, magical ? StatIds.Wis : StatIds.Def);
            HookConvert(ctx, h);
            magical = h.Magical; element = h.Element; raw = h.Raw;

            double tgtFortune = Fortune(r, Eff(r, tgt, StatIds.Luk));
            bool lucky = false, graze = false;
            if (Roll(rng, h.Evade, tgtFortune, ref lucky))
            {
                if (magical && !veil) graze = true;
                else
                {
                    tgt.Tally.Evades++;
                    onAction?.Invoke(new FightAction(src.Id, sk, tgt.Id, 0, false, true, aoe, false, false, lucky));
                    Fire(r, s, tgt, FightTrigger.OnEvade, rng, onAction);
                    return 0;
                }
            }

            bool crit = false;
            if (graze) { raw *= r.GrazeMult; tgt.Tally.Grazes++; }
            else
            {
                double rate = CritChance(r, Eff(r, src, StatIds.Luk), Bonus(src, StatIds.CritRate) + (r.Mutators?.CritBonus ?? 0));
                bool critLucky = false, deniedLucky = false;
                crit = Roll(rng, rate, Fortune(r, Eff(r, src, StatIds.Luk)), ref critLucky);
                if (crit && tgtFortune > 0 && Draw(rng) < tgtFortune)   // the target's fortune makes a landed crit roll again
                {
                    crit = Draw(rng) < rate;
                    deniedLucky = !crit;
                }
                lucky |= crit ? critLucky : deniedLucky;
                if (crit) raw *= CritMult(r, Eff(r, src, StatIds.Dex), Bonus(src, StatIds.CritDmg));
            }
            h.Graze = graze; h.Crit = crit;
            bool pierce = element == RuleModifiers.FireElement && Has(src, RuleModifiers.FirePierce);
            double elementMult = r.ElementMult(element, tgt.Source.Element);
            raw *= pierce ? Math.Max(1.0, elementMult) * (1 + r.FireBonus) : elementMult;
            double dmgBonus = Bonus(src, StatIds.DmgDealt);
            if (dmgBonus != 0) raw *= Math.Max(0.0, 1 + dmgBonus);
            if (r.Mutators != null) raw *= r.Mutators.DamageMult(aoe, element);
            h.Raw = raw;
            HookOutgoing(ctx, h);
            HookIncoming(ctx, h);
            raw = h.Raw;
            double mitStat = h.Mitigation;
            if (pierce) mitStat *= 1 - r.FirePierce;
            double after = Mitigate(r, raw, mitStat);
            double mitigated = raw - after;
            if (r.Tower != null)
            {
                after *= 1 + CorruptionLevel(r, s.Ticks) * r.Tower.CorruptDmgPerLevel;
                if (tgt.Guard > 0 && GuardActive(s, tgt)) after *= Math.Max(0.0, 1 - tgt.Guard);
            }
            double dealt = Absorb(ctx, src, tgt, after, element);
            h.Dealt = dealt; h.Mitigated = mitigated;

            if (crit) src.Tally.Crits++;
            onAction?.Invoke(new FightAction(src.Id, sk, tgt.Id, dealt, crit, false, aoe, false, graze, lucky));
            if (dealt > 0 && r.DrainFrac > 0 && src.Alive && Has(src, RuleModifiers.AbyssalDrain))
            {
                double shield = 0, healed = Heal(r, src, r.DrainFrac * dealt * HealScale(r, s), ref shield, r.DrainShieldCap);
                src.Tally.HealingDone += healed;
                if (healed + shield > 0) onAction?.Invoke(new FightAction(src.Id, FightAction.DrainEvent, src.Id, healed + shield));
            }
            Reborn(tgt, onAction);
            if (dealt > 0 && (r.FeastStr > 0 || r.FeastDmg > 0) && src.Alive && Has(src, RuleModifiers.NightFeast))
            {
                src.FeastBonus += r.FeastStr;
                src.FeastDmgBonus += r.FeastDmg;
                onAction?.Invoke(new FightAction(src.Id, FightAction.FeastEvent, src.Id, src.FeastBonus + src.FeastDmgBonus));
            }
            HookHitLanded(ctx, h);
            HookHitTaken(ctx, h);
            Fire(r, s, tgt, FightTrigger.OnHitTaken, rng, onAction);
            if (crit) Fire(r, s, src, FightTrigger.OnCrit, rng, onAction);

            if (!magical && !aoe && mitigated > 0 && tgt.Alive && src.Alive && Has(tgt, RuleModifiers.CounterMitigated)
                && EnterGuard(r, s, GuardKind.Counter, true, out var saved))   // a counter is a triggered effect: no counter of a counter
            {
                try
                {
                    tgt.Tally.Counters++;
                    double c = Absorb(ctx, tgt, src, mitigated * r.CounterMult, null);
                    onAction?.Invoke(new FightAction(tgt.Id, null, src.Id, c, false, false, false, true));
                    Reborn(src, onAction);
                    GainEntropy(r, s, tgt, r.Tower?.EntropyPerCounter ?? 0, rng, onAction);
                }
                finally { ExitGuard(s, saved); }
            }
            return dealt;
        }

        /// <summary>
        /// Heal a unit by <paramref name="amount"/> (already scaled by HealMult), capped at max HP; returns the HP restored. With
        /// tidal_overflow the overflow becomes shield, up to TideShieldCap of max HP (added to <paramref name="shield"/>). It joins the
        /// unit's one shield pool, so a timed shield's expiry clears it too. The heal-in hook may take the heal first (nothing lands).
        /// </summary>
        static double Heal(FightRules r, FightUnit t, double amount, ref double shield, double overflowCap = 0)
        {
            bool taken = false;
            HookHealIn(r, t, amount, ref taken);   // pyre_mercy: the heal becomes fire damage instead
            if (taken) return 0;
            double before = t.Hp, sum = t.Hp + amount;
            t.Hp = Math.Min(t.MaxHp, sum);
            double over = sum - t.MaxHp;
            double capFrac = Math.Max(overflowCap, Has(t, RuleModifiers.TidalOverflow) ? r.TideShieldCap : 0);
            if (over > 0 && capFrac > 0)
            {
                HookShieldCap(r, t, ref capFrac);
                double cap = capFrac * t.MaxHp;
                if (t.Shield < cap)
                {
                    double add = Math.Min(cap - t.Shield, over);
                    t.Shield += add;
                    shield += add;
                }
            }
            return t.Hp - before;
        }

        /// <summary>
        /// Shield first, then HP (the lanes' HP-damage hook sits between them). Records damage and kills; a blow that would kill
        /// meets root_rebirth first, then the would-die hook. Element null = no per-element tally.
        /// </summary>
        static double Absorb(RuleCtx c, FightUnit src, FightUnit tgt, double amount, string element)
        {
            double absorbed = Math.Min(tgt.Shield, amount);
            tgt.Shield -= absorbed;
            double hpDamage = amount - absorbed;
            HookHpDamage(c, src, tgt, ref hpDamage);
            double dealt = Math.Min(tgt.Hp, hpDamage);
            bool wasAlive = tgt.Alive;
            tgt.Hp -= dealt;
            if (tgt.Hp < 0) tgt.Hp = 0;
            src.Tally.DamageDealt += dealt;
            tgt.Tally.DamageTaken += dealt;
            if (element != null)
            {
                src.Tally.DamageByElement.TryGetValue(element, out var prev);
                src.Tally.DamageByElement[element] = prev + dealt;
            }
            if (tgt.ChannelTicks > 0) tgt.ChannelDealt += absorbed + dealt;
            if (wasAlive && !tgt.Alive)
            {
                bool saved = false;
                if (tgt.RebirthHeal > 0)   // root_rebirth: it survives this one blow, once per fight
                {
                    tgt.Hp = tgt.RebirthHeal * tgt.MaxHp;
                    tgt.RebirthHeal = 0;
                    tgt.RebornPending = true;
                    saved = true;
                }
                else HookWouldDie(c, src, tgt, ref saved);
                if (!saved && !tgt.Alive)
                {
                    src.Tally.Kills++;
                    tgt.KilledBy = src.Id;
                }
            }
            return dealt;
        }

        /// <summary>
        /// A timed shield of <paramref name="amount"/> for <paramref name="ticks"/> (0 = no expiry): the unit keeps the bigger of its
        /// shield and this one, and the longer timer; the shield-gain hook may change both (specs/16 section 6.4).
        /// </summary>
        static void GainShield(RuleCtx c, FightUnit t, double amount, int ticks)
        {
            double shield = Math.Max(t.Shield, amount);
            HookShieldGain(c, t, amount, ref shield, ref ticks);
            t.Shield = shield;
            t.ShieldTicks = Math.Max(t.ShieldTicks, ticks);
        }

        /// <summary>Sends root_rebirth's event after the action that triggered it.</summary>
        static void Reborn(FightUnit u, Action<FightAction> onAction)
        {
            if (!u.RebornPending) return;
            u.RebornPending = false;
            onAction?.Invoke(new FightAction(u.Id, FightAction.RebirthEvent, u.Id, u.Hp));
        }

        static double Draw(IRandomSource rng) => rng == null ? 1.0 : rng.NextDouble();

        // ------------------------------------------------------------------ targeting

        static List<FightUnit> Enemies(FightState s, FightUnit u)
        {
            var list = new List<FightUnit>(4);
            foreach (var e in s.Units) if (e.Side != u.Side && e.Alive) list.Add(e);
            return list;
        }

        static bool AnyEnemy(FightState s, FightUnit u)
        {
            foreach (var e in s.Units) if (e.Side != u.Side && e.Alive) return true;
            return false;
        }

        /// <summary>
        /// A taunting enemy (lowest slot) if any; else the acting gambit's pick (specs/28 section 4.2); else a random living enemy
        /// (one draw).
        /// </summary>
        static FightUnit PickEnemy(FightState s, FightUnit u, IRandomSource rng)
        {
            foreach (var e in s.Units) if (e.Side != u.Side && e.Alive && e.TauntTicks > 0) return e;
            var list = Enemies(s, u);
            if (list.Count == 0) return null;
            var picked = GambitPick(list, u.PickRule);
            if (picked != null) return picked;
            if (list.Count == 1 || rng == null) return list[0];
            return list[Math.Min(list.Count - 1, (int)(rng.NextDouble() * list.Count))];
        }

        /// <summary>
        /// A single-target debuff's target (specs/16 section 5.2): the living enemies that lack its effect, or every living enemy when
        /// none does; among them a taunting one (lowest slot), else a random one (one draw; no draw when only one is left).
        /// </summary>
        static FightUnit PickDebuffTarget(FightState s, FightUnit u, string source, IRandomSource rng)
        {
            var list = new List<FightUnit>(4);
            foreach (var e in s.Units) if (e.Side != u.Side && e.Alive && !HasEffectFrom(e, source)) list.Add(e);
            if (list.Count == 0) return PickEnemy(s, u, rng);
            foreach (var e in list) if (e.TauntTicks > 0) return e;
            var picked = GambitPick(list, u.PickRule);
            if (picked != null) return picked;
            if (list.Count == 1 || rng == null) return list[0];
            return list[Math.Min(list.Count - 1, (int)(rng.NextDouble() * list.Count))];
        }

        static List<FightUnit> Allies(FightState s, FightUnit src, SkillTarget target)
        {
            var list = new List<FightUnit>(4);
            if (target == SkillTarget.Self) { list.Add(src); return list; }
            if (target == SkillTarget.LowestAlly)
            {
                var low = LowestAlly(s, src);
                if (low != null) list.Add(low);
                return list;
            }
            if (target == SkillTarget.AllAllies)
            {
                foreach (var a in s.Units) if (a.Side == src.Side && a.Alive) list.Add(a);
                return list;
            }
            list.Add(src);   // an enemy-targeted support skill falls back to its user
            return list;
        }

        static FightUnit LowestAlly(FightState s, FightUnit u)
        {
            FightUnit best = null;
            foreach (var a in s.Units)
                if (a.Side == u.Side && a.Alive && (best == null || a.Hp / a.MaxHp < best.Hp / best.MaxHp)) best = a;
            return best;
        }

        /// <summary>
        /// The HP a heal of <paramref name="amount"/> should find missing before the AI casts it on <paramref name="t"/> (specs/16
        /// section 5.2): the whole amount, so none of it is wasted; for a heal too big to land in full (above (1 - AI_HEAL_BELOW) of
        /// max HP), that much instead, so it is cast once the target is below AI_HEAL_BELOW.
        /// </summary>
        static double HealNeed(FightRules r, FightUnit t, double amount) => Math.Min(amount, (1 - r.AiHealBelow) * t.MaxHp);

        /// <summary>True when <paramref name="t"/> is alive, hurt and missing at least HealNeed.</summary>
        static bool WantsHeal(FightRules r, FightUnit t, double amount)
        {
            double missing = t.MaxHp - t.Hp;
            return t.Alive && missing > 0 && missing >= HealNeed(r, t, amount);
        }

        /// <summary>
        /// A lowest-ally heal's target: the living ally with the lowest HP fraction among those that WantsHeal (lowest slot on a tie),
        /// else the lowest ally (a triggered heal fires whether or not it fits).
        /// </summary>
        static List<FightUnit> HealTarget(FightRules r, FightState s, FightUnit src, double amount)
        {
            FightUnit best = null;
            foreach (var a in s.Units)
                if (a.Side == src.Side && WantsHeal(r, a, amount) && (best == null || a.Hp / a.MaxHp < best.Hp / best.MaxHp)) best = a;
            if (best != null) return new List<FightUnit>(1) { best };
            return Allies(s, src, SkillTarget.LowestAlly);
        }

        static bool AnyOtherAlly(FightState s, FightUnit u)
        {
            foreach (var a in s.Units) if (a != u && a.Side == u.Side && a.Alive) return true;
            return false;
        }

        /// <summary>
        /// Whether a shield of <paramref name="amount"/> would raise one of the skill's targets' shield (specs/16 section 5.2). A
        /// shield guards HP it has not lost yet, so unlike a heal it does not wait for damage: it only waits while the target's
        /// shield is already at least this big (casting then would change nothing but the timer).
        /// </summary>
        static bool WantsShield(FightState s, FightUnit u, CombatSkill sk, double amount)
        {
            if (sk.Target == SkillTarget.Self) return u.Shield < amount;
            if (sk.Target == SkillTarget.LowestAlly) { var t = LowestAlly(s, u); return t != null && t.Shield < amount; }
            foreach (var a in s.Units) if (a.Side == u.Side && a.Alive && a.Shield < amount) return true;
            return false;
        }

        static bool HasEffectFrom(FightUnit u, string source)
        {
            foreach (var e in u.Effects) if (e.Source == source) return true;
            return false;
        }

        /// <summary>
        /// The unit's active debuffs from any source (a shared helper for the idea-pool lanes, epic #132): its stat effects with a
        /// negative value, each stacked copy counted (debuff skills, triggered debuffs). Buffs, passives and rule state (a bleed
        /// gauge, an element mark) are not debuffs.
        /// </summary>
        internal static int DebuffCount(FightUnit u)
        {
            int n = 0;
            foreach (var e in u.Effects) if (e.Frac < 0) n++;
            return n;
        }

        static bool Has(FightUnit u, string rule) => u.Source.Rules.Contains(rule);

        // ------------------------------------------------------------------ the tower (specs/21 section 3)

        /// <summary>
        /// Arena corruption level at a tick: max(0, (tick - start) / CORRUPT_EVERY_TICKS), integer division, where start is
        /// CORRUPT_START_TICK less any corruption_early mutators (never below 0). 0 without tower rules.
        /// </summary>
        public static int CorruptionLevel(FightRules r, int ticks)
        {
            var t = r.Tower;
            if (t == null) return 0;
            int start = Math.Max(0, t.CorruptStartTick - (r.Mutators?.CorruptEarlyTicks ?? 0));
            if (ticks < start) return 0;
            return (ticks - start) / t.CorruptEveryTicks;
        }

        /// <summary>Healing and shield multiplier from corruption: max(CORRUPT_HEAL_FLOOR, 1 - level x CORRUPT_HEAL_PER_LEVEL). Exactly 1 without tower rules.</summary>
        public static double HealMult(FightRules r, FightState s) =>
            r.Tower == null ? 1.0 : Math.Max(r.Tower.CorruptHealFloor, 1 - CorruptionLevel(r, s.Ticks) * r.Tower.CorruptHealPerLevel);

        /// <summary>What heals and shields are multiplied by: corruption's HealMult times the heal_mult mutators (specs/28 section 3.2).</summary>
        static double HealScale(FightRules r, FightState s) => HealMult(r, s) * (r.Mutators?.Heal ?? 1);

        static bool GuardActive(FightState s, FightUnit boss)
        {
            foreach (var a in s.Units) if (a.IsAdd && a.Side == boss.Side && a.Alive) return true;
            return false;
        }

        public const string VentRecoil = "recoil", VentStun = "stun", VentEnrage = "enrage", VentConvert = "convert";
        static readonly string[] VentRotation = { VentRecoil, VentStun, VentEnrage };

        /// <summary>
        /// The kind the unit's entropy gauge vents as next time it fills (specs/21 section 3.1): convert with entropy_convert, else
        /// recoil, stun, then enrage by its vent count. Pure: playback reads it to show the next vent.
        /// </summary>
        public static string NextVent(FightUnit u) => Has(u, RuleModifiers.EntropyConvert) ? VentConvert : VentRotation[u.Vents % VentRotation.Length];

        /// <summary>
        /// Add entropy to a unit (scaled by its entropy_gain effects). At the cap the gauge vents and resets: recoil, stun, then enrage,
        /// by the unit's vent count; a unit with entropy_convert shields itself instead. A recoil that would kill meets the
        /// would-die hook (not root_rebirth, which only saves from another unit's damage).
        /// </summary>
        static void GainEntropy(FightRules r, FightState s, FightUnit u, double amount, IRandomSource rng, Action<FightAction> onAction)
        {
            var t = r.Tower;
            if (t == null || !u.Alive || !(amount > 0)) return;
            u.Entropy += amount * Math.Max(0.0, 1 + Bonus(u, StatIds.EntropyGain));
            if (u.Entropy < t.EntropyCap) return;
            u.Entropy = 0;
            string kind = NextVent(u);
            u.Vents++;
            if (kind == VentConvert)
            {
                double amt = t.ConvertShield * u.MaxHp;
                GainShield(new RuleCtx(r, s, rng, onAction), u, amt, t.ConvertTurns * r.TurnTicks);
                onAction?.Invoke(new FightAction(u.Id, "vent:convert", u.Id, amt));
                return;
            }
            switch (kind)
            {
                case VentRecoil:
                {
                    double dmg = t.RecoilFrac * u.MaxHp;
                    double absorbed = Math.Min(u.Shield, dmg);
                    u.Shield -= absorbed;
                    double lost = Math.Min(u.Hp, dmg - absorbed);
                    u.Hp -= lost;
                    u.Tally.DamageTaken += lost;
                    onAction?.Invoke(new FightAction(u.Id, "vent:recoil", u.Id, lost));
                    if (lost > 0 && !u.Alive)
                    {
                        bool saved = false;
                        HookWouldDie(new RuleCtx(r, s, rng, onAction), u, u, ref saved);
                    }
                    break;
                }
                case VentStun:
                    u.Gauge = Math.Max(0.0, u.Gauge - t.StunGauge);
                    onAction?.Invoke(new FightAction(u.Id, "vent:stun", u.Id, t.StunGauge));
                    break;
                default:
                    foreach (var e in Enemies(s, u)) AddEffect(r, e, StatIds.DmgDealt, t.EnrageFrac, t.EnrageTurns * r.TurnTicks, "vent_enrage");
                    onAction?.Invoke(new FightAction(u.Id, "vent:enrage", null, t.EnrageFrac));
                    break;
            }
        }

        /// <summary>After each action: every living boss whose HP has reached its next phase's threshold enters it (one phase per unit per action).</summary>
        static void CheckPhases(FightRules r, FightState s, IRandomSource rng, Action<FightAction> onAction)
        {
            if (!s.Tower) return;
            for (int i = 0, n = s.Units.Count; i < n; i++)   // adds spawned now are not checked
            {
                var u = s.Units[i];
                var phases = u.Source.Phases;
                if (!u.Alive || u.PhaseNext >= phases.Count) continue;
                var p = phases[u.PhaseNext];
                if (u.Hp > p.EnterHp * u.MaxHp) continue;
                u.PhaseNext++;
                EnterPhase(r, s, u, p, rng, onAction);
            }
        }

        static void EnterPhase(FightRules r, FightState s, FightUnit u, BossPhase p, IRandomSource rng, Action<FightAction> onAction)
        {
            double amount = 0;
            switch (p.Kind)
            {
                case PhaseKinds.ShieldCheck:
                    amount = p.Power * u.MaxHp;
                    u.Shield = amount;
                    u.ShieldTicks = p.Duration * r.TurnTicks;
                    u.CheckPending = true; u.CheckHealFrac = p.HealFrac; u.CheckBuff = p.Buff;
                    break;
                case PhaseKinds.Split:
                    u.Guard = p.Guard;
                    break;
                case PhaseKinds.Channel:
                    amount = p.Power * u.MaxHp;
                    u.ChannelTicks = p.Duration * r.TurnTicks; u.ChannelNeed = amount; u.ChannelStrike = p.Strike; u.ChannelDealt = 0;
                    break;
                default:
                    u.EnrageStep = p.Buff;
                    break;
            }
            onAction?.Invoke(new FightAction(u.Id, "phase:" + p.Kind, null, amount));

            if (p.Kind == PhaseKinds.Split)
                for (int k = 0; k < p.Count && LivingOnSide(s, u.Side) < r.SideSize; k++)
                {
                    string id;
                    do id = u.Id + "#" + (++u.Spawned); while (HasId(s, id));
                    Summon(r, s, WithId(p.Add, id), u.Side, rng, onAction);
                }

            if (p.AddSkills.Count > 0)
            {
                var skills = new List<CombatSkill>(u.Source.Skills);
                skills.AddRange(p.AddSkills);
                var cd = new int[skills.Count];
                Array.Copy(u.Cooldowns, cd, u.Cooldowns.Length);
                u.Cooldowns = cd;
                u.Source = new Fighter
                {
                    Id = u.Source.Id, Element = u.Source.Element, Stats = u.Source.Stats, Skills = skills,
                    Triggers = u.Source.Triggers, Rules = u.Source.Rules, Phases = u.Source.Phases, Gambits = u.Source.Gambits,
                };
                foreach (var sk in p.AddSkills)
                    if (sk.Kind == SkillKind.Passive && u.Alive) Use(r, s, u, sk, rng, onAction, triggered: true);
            }
        }

        static int LivingOnSide(FightState s, int side)
        {
            int n = 0;
            foreach (var u in s.Units) if (u.Side == side && u.Alive) n++;
            return n;
        }

        /// <summary>
        /// A unit that joins the fight mid-way (specs/16 section 6.4): a split phase's add, a lane rule's summon. It takes the next
        /// slot on its side, starts with an empty gauge and full HP, is an add (it never keeps its side alive, and the Dungeon
        /// leaves it out of the team tallies), and fires its BattleStart triggers (its passives) at once. The caller checks the id
        /// is new and the side's room. <paramref name="joined"/> (a lane rule's event) is logged once the unit is in, before its triggers.
        /// </summary>
        internal static FightUnit Summon(FightRules r, FightState s, Fighter f, int side, IRandomSource rng, Action<FightAction> onAction,
                                         FightAction? joined = null)
        {
            var add = NewUnit(r, f, side, CountOnSide(s, side));
            add.IsAdd = true;
            s.Units.Add(add);
            if (joined.HasValue) onAction?.Invoke(joined.Value);
            Fire(r, s, add, FightTrigger.BattleStart, rng, onAction);
            return add;
        }

        static int CountOnSide(FightState s, int side)
        {
            int n = 0;
            foreach (var u in s.Units) if (u.Side == side) n++;
            return n;
        }

        static bool HasId(FightState s, string id)
        {
            foreach (var u in s.Units) if (u.Id == id) return true;
            return false;
        }

        static Fighter WithId(Fighter f, string id) => f.WithId(id);

        // ------------------------------------------------------------------ formulas (specs/16 sections 3 and 5.3)
        // The fight uses these, and so do the stat tooltips (BattleText), so what the player reads is what the fight does.

        /// <summary>Max HP from the HP stat: max(1, HP x MON_HP_PER_POINT).</summary>
        public static double MaxHp(FightRules r, double hp) => Math.Max(1.0, hp * r.HpPerPoint);

        /// <summary>ATB gauge fill per tick: max(MON_SPD_MIN, SPD).</summary>
        public static double GaugeFill(FightRules r, double spd) => Math.Max(r.SpdMin, spd);

        /// <summary>Basic attack before crit, element and mitigation: BASIC_ATTACK_POWER x max(STR, INT).</summary>
        public static double BasicAttackRaw(FightRules r, double str, double intel) => r.BasicAttackPower * Math.Max(str, intel);

        /// <summary>The basic attack is magical when INT is higher than STR.</summary>
        public static bool BasicAttackMagical(double str, double intel) => intel > str;

        /// <summary>Chance to evade a physical hit: clamp(base + (target DEX - attacker DEX) x per_dex, 0, cap).</summary>
        public static double EvadeChance(FightRules r, double targetDex, double attackerDex) => EvadeChance(r, targetDex, attackerDex, r.EvadeCap);

        /// <summary>Evade chance with an explicit cap (umbral_veil raises it).</summary>
        public static double EvadeChance(FightRules r, double targetDex, double attackerDex, double cap) =>
            Clamp(r.EvadeBase + (targetDex - attackerDex) * r.EvadePerDex, 0, cap);

        /// <summary>Crit damage multiplier: max(1, MON_CRIT_DMG + DEX x MON_CRIT_DMG_PER_DEX + crit_dmg effects).</summary>
        public static double CritMult(FightRules r, double dex, double critDmgBonus) => Math.Max(1.0, r.CritDmg + dex * r.CritDmgPerDex + critDmgBonus);

        /// <summary>Fortune: min(MON_FORTUNE_CAP, LUK / (LUK + MON_FORTUNE_K)), the chance to reroll a roll that went against the unit.</summary>
        public static double Fortune(FightRules r, double luk) => luk <= 0 ? 0 : Math.Min(r.FortuneCap, luk / (luk + r.FortuneK));

        /// <summary>
        /// A roll the unit wants to succeed (its evade, its crit), with fortune: draw against <paramref name="chance"/>; on a
        /// failure, draw against <paramref name="fortune"/> and, on a success there, draw once more against the chance. Nothing is
        /// drawn when the chance is zero, and the fortune draw is skipped when fortune is zero. <paramref name="lucky"/> is set
        /// when the reroll turned the failure into a success.
        /// </summary>
        static bool Roll(IRandomSource rng, double chance, double fortune, ref bool lucky)
        {
            if (chance <= 0) return false;
            if (Draw(rng) < chance) return true;
            if (fortune <= 0 || Draw(rng) >= fortune) return false;
            bool ok = Draw(rng) < chance;
            lucky |= ok;
            return ok;
        }

        /// <summary>Crit chance: clamp(base + LUK x per_luk + crit_rate effects, 0, cap).</summary>
        public static double CritChance(FightRules r, double luk, double critRateBonus) =>
            Clamp(r.CritBase + luk * r.CritPerLuk + critRateBonus, 0, r.CritCap);

        /// <summary>Damage after mitigation: raw x K / (K + DEF), or WIS for magical damage.</summary>
        public static double Mitigate(FightRules r, double raw, double defOrWis) => raw * r.DefK / (r.DefK + defOrWis);

        // ------------------------------------------------------------------ stats

        /// <summary>
        /// Add a stat effect (specs/16 section 3). A permanent HP effect multiplies the unit's base max HP. A timed effect stacks
        /// up to EFFECT_STACK_CAP copies per source and stat; at the cap, the copy with the fewest ticks left is refreshed to the
        /// new duration instead. Any HP change rescales current HP so the unit keeps its HP fraction.
        /// </summary>
        static void AddEffect(FightRules r, FightUnit u, string stat, double frac, int ticks, string source)
        {
            if (stat == StatIds.Hp && ticks < 0)
            {
                u.BaseMaxHp *= Math.Max(r.StatFloor, 1 + frac);
                RecalcMaxHp(r, u);
                return;
            }
            if (ticks >= 0)
            {
                FightEffect oldest = null;
                int copies = 0;
                foreach (var e in u.Effects)
                {
                    if (e.Source != source || e.Stat != stat || e.TicksLeft < 0) continue;
                    copies++;
                    if (oldest == null || e.TicksLeft < oldest.TicksLeft) oldest = e;
                }
                if (copies >= r.StackCap)
                {
                    oldest.TicksLeft = Math.Max(oldest.TicksLeft, ticks);
                    return;
                }
            }
            u.Effects.Add(new FightEffect { Stat = stat, Frac = frac, TicksLeft = ticks, Source = source });
            if (stat == StatIds.Hp) RecalcMaxHp(r, u);
        }

        /// <summary>
        /// Max HP = base max HP x max(STAT_FLOOR_FRAC, 1 + timed HP effects); current HP scales with it, keeping its fraction of max.
        /// </summary>
        static void RecalcMaxHp(FightRules r, FightUnit u)
        {
            double max = u.BaseMaxHp * Math.Max(r.StatFloor, 1 + Bonus(u, StatIds.Hp));
            if (max == u.MaxHp) return;
            u.Hp *= max / u.MaxHp;
            u.MaxHp = max;
        }

        /// <summary>The echo_cast copy of a skill: same skill at <paramref name="power"/> of its power.</summary>
        static CombatSkill Echo(CombatSkill sk, double power) => new CombatSkill
        {
            Id = sk.Id, Kind = sk.Kind, Target = sk.Target, Stat = sk.Stat, Scales = sk.Scales, Power = sk.Power * power,
            Cooldown = sk.Cooldown, Duration = sk.Duration, Element = sk.Element,
        };

        static double Scaled(FightRules r, FightUnit src, CombatSkill sk) => (sk.Scales == StatIds.Hp ? src.MaxHp : Eff(r, src, sk.Scales)) * sk.Power;

        /// <summary>
        /// Current value of a trained stat: base x (1 + sum of effect fractions), floored at STAT_FLOOR_FRAC of base, plus the lanes'
        /// flat bonuses (the stat-flat hook). "hp" is max HP, which already carries the HP effects (RecalcMaxHp).
        /// </summary>
        public static double Eff(FightRules r, FightUnit u, string stat)
        {
            if (stat == StatIds.Hp) return u.MaxHp;
            double flat = 0;
            HookStatFlat(r, u, stat, ref flat);
            return u.Source.Stats.Get(stat) * Math.Max(r.StatFloor, 1 + Bonus(u, stat)) + flat;
        }

        /// <summary>Sum of effect values (plus night_feast's STR) on <paramref name="stat"/> (the whole value for crit_rate and crit_dmg bonuses).</summary>
        static double Bonus(FightUnit u, string stat)
        {
            double sum = stat == StatIds.Str ? u.FeastBonus : stat == StatIds.DmgDealt ? u.FeastDmgBonus : 0;   // night_feast counts like a permanent effect
            foreach (var e in u.Effects) if (e.Stat == stat) sum += e.Frac;
            return sum;
        }

        static bool In01(double v) => v >= 0 && v <= 1;

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        // ------------------------------------------------------------------ hashing

        /// <summary>
        /// Lowercase hex SHA-256 over a canonical encoding of the whole fight (the canonical conventions of specs/03 section 1): the golden
        /// files, the replay tests and a future PvP server compare fights by this. Changing the encoding bumps HashVersion.
        /// </summary>
        public static string StateHash(FightState s)
        {
            var w = new CanonicalWriter(512);
            w.Bytes(HashMagic);
            w.I32(s.Ticks);
            w.Bool(s.Started);
            w.I32((int)s.Outcome);
            w.I32(s.Units.Count);
            foreach (var u in s.Units)
            {
                w.Str(u.Id);
                w.I32(u.Side); w.I32(u.Slot);
                w.F64(u.MaxHp); w.F64(u.Hp); w.F64(u.Gauge); w.F64(u.Shield);
                w.I32(u.ShieldTicks); w.I32(u.TauntTicks);
                w.I32(u.Cooldowns.Length);
                foreach (var cd in u.Cooldowns) w.I32(cd);
                w.I32(u.Effects.Count);
                foreach (var e in u.Effects) { w.Str(e.Stat); w.F64(e.Frac); w.I32(e.TicksLeft); w.Str(e.Source); }
                var t = u.Tally;
                w.I32(t.Actions); w.F64(t.DamageDealt); w.F64(t.DamageTaken); w.F64(t.HealingDone);
                w.I32(t.Crits); w.I32(t.Evades); w.I32(t.Grazes); w.I32(t.Kills); w.I32(t.Counters);
                w.I32(t.DamageByElement.Count);
                foreach (var kv in t.DamageByElement) { w.Str(kv.Key); w.F64(kv.Value); }
                // Rule state is hashed only for units that carry the rule, so every other fight keeps its hash.
                if (Has(u, RuleModifiers.RootRebirth)) w.F64(u.RebirthHeal);
                if (Has(u, RuleModifiers.NightFeast)) { w.F64(u.FeastBonus); w.F64(u.FeastDmgBonus); }
                if (Has(u, RuleModifiers.EchoCast)) w.I32(u.DecoysLeft);
                HookHashUnit(w, s, u);   // the lanes' rule state, for carriers only
                if (s.Tower)   // only tower fights hash the tower fields, so every other fight keeps its hash
                {
                    w.F64(u.Entropy); w.I32(u.Vents); w.I32(u.PhaseNext); w.Bool(u.IsAdd); w.I32(u.Spawned); w.F64(u.Guard);
                    w.Bool(u.CheckPending); w.F64(u.CheckHealFrac); w.F64(u.CheckBuff);
                    w.I32(u.ChannelTicks); w.F64(u.ChannelNeed); w.F64(u.ChannelStrike); w.F64(u.ChannelDealt); w.F64(u.EnrageStep);
                }
            }
            if (s.Tower) w.I32(s.PeakCorruption);
            HookHashFight(w, s);
            return Sha256.Hex(Sha256.Compute(w.ToArray()));
        }
    }
}
