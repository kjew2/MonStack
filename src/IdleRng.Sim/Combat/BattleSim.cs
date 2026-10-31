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
    /// server (specs/10 section 9.2).
    /// </summary>
    public static class BattleSim
    {
        public const string StreamName = "battle_sim";
        public const int HashVersion = 1;
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
                  && r.SpdMin > 0 && r.CritDmg >= 1 && r.StatFloor > 0 && r.StatFloor <= 1 && r.StackCap >= 1))
                throw new ArgumentException("fight rules out of range (specs/16 section 7)", nameof(r));
            if (!(In01(r.EchoPower) && In01(r.TideRegen) && In01(r.TideShieldCap) && In01(r.DrainFrac) && In01(r.FirePierce) && r.DoubleTurns >= 0))
                throw new ArgumentException("rule modifier constants out of range (specs/16 section 7)", nameof(r));
            if (r.Tower != null && !r.Tower.Valid) throw new ArgumentException("tower rules out of range (specs/21 section 5)", nameof(r));
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
            return u;
        }

        static void ValidateFighter(FightRules rules, Fighter f, string side, int slot)
        {
            string at = $"{side}[{slot}]";
            if (f == null) throw new ArgumentException($"{at} is null", side);
            if (string.IsNullOrEmpty(f.Id)) throw new ArgumentException($"{at} needs an id", side);
            if (f.Stats == null || f.Skills == null || f.Triggers == null || f.Rules == null || f.Phases == null)
                throw new ArgumentException($"{at} ({f.Id}): Stats, Skills, Triggers, Rules and Phases must not be null", side);
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
            if (!s.Started)
            {
                s.Started = true;
                foreach (var u in s.Units) Fire(rules, s, u, FightTrigger.BattleStart, rng, onAction);
                var start = Decide(s, 0);
                if (start != FightOutcome.Ongoing) return s.Outcome = start;
            }
            s.Ticks++;
            if (s.Tower) s.PeakCorruption = Math.Max(s.PeakCorruption, CorruptionLevel(rules, s.Ticks));
            foreach (var u in s.Units)
                if (u.Alive) u.Gauge += GaugeFill(rules, Eff(rules, u, StatIds.Spd));

            for (int n = 0; n < MaxActionsPerTick; n++)
            {
                var actor = NextActor(s, rules.GaugeSize);
                if (actor == null) break;
                actor.Gauge -= rules.GaugeSize;
                bool extra = false;
                do
                {
                    if (extra) onAction?.Invoke(new FightAction(actor.Id, FightAction.DoubleTurnEvent, actor.Id, 0));
                    Act(rules, s, actor, rng, onAction);
                    CheckPhases(rules, s, rng, onAction);
                    var o = Decide(s, actor.Side);
                    if (o != FightOutcome.Ongoing) return s.Outcome = o;
                    extra = true;
                } while (ExtraTurn(rules, actor));
            }

            int strikeSide = AgeAll(rules, s, onAction);
            if (strikeSide >= 0)
            {
                var o = Decide(s, strikeSide);
                if (o != FightOutcome.Ongoing) return s.Outcome = o;
            }
            if (s.Ticks >= rules.TimeLimitTicks) s.Outcome = FightOutcome.TimedOut;
            return s.Outcome;
        }

        /// <summary>double_turn_start: after each of the unit's first DoubleTurns actions (an odd action count) it acts again at once.</summary>
        static bool ExtraTurn(FightRules r, FightUnit u) =>
            u.Alive && u.Tally.Actions % 2 == 1 && u.Tally.Actions < 2 * r.DoubleTurns && Has(u, RuleModifiers.DoubleTurnStart);

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
        static int AgeAll(FightRules r, FightState s, Action<FightAction> onAction)
        {
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
                        foreach (var e in Enemies(s, u)) total += Absorb(u, e, amt, null);
                        onAction?.Invoke(new FightAction(u.Id, "channel_strike", null, total));
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
            double heal = Math.Min(u.MaxHp - u.Hp, u.CheckHealFrac * u.MaxHp * HealMult(r, s));
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
            foreach (var sk in list)
                if (u.Alive)
                {
                    Use(r, s, u, sk, rng, onAction, triggered: true);
                    if (trigger != FightTrigger.BattleStart) GainEntropy(r, s, u, r.Tower?.EntropyPerTrigger ?? 0, onAction);
                }
        }

        // ------------------------------------------------------------------ actions

        static void Act(FightRules r, FightState s, FightUnit u, IRandomSource rng, Action<FightAction> onAction)
        {
            u.Tally.Actions++;
            if (r.Tower != null) u.Entropy = Math.Max(0, u.Entropy - r.Tower.EntropyDecay);
            if (r.TideRegen > 0 && Has(u, RuleModifiers.TidalOverflow))
            {
                double shield = 0, healed = Heal(r, u, r.TideRegen * u.MaxHp * HealMult(r, s), ref shield);
                u.Tally.HealingDone += healed;
                if (healed + shield > 0) onAction?.Invoke(new FightAction(u.Id, FightAction.TideEvent, u.Id, healed + shield));
            }
            Fire(r, s, u, FightTrigger.OnAction, rng, onAction);
            if (!u.Alive) return;
            for (int i = 0; i < u.Cooldowns.Length; i++) if (u.Cooldowns[i] > 0) u.Cooldowns[i]--;
            var skills = u.Source.Skills;
            // Rescue pass first (heal, shield), then everything else; each pass is in list order.
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < skills.Count; i++)
                {
                    var sk = skills[i];
                    if ((sk.Kind == SkillKind.Heal || sk.Kind == SkillKind.Shield) != (pass == 0)) continue;
                    if (sk.Kind == SkillKind.Passive || u.Cooldowns[i] > 0 || !Useful(r, s, u, sk)) continue;
                    u.Cooldowns[i] = sk.Cooldown;
                    Use(r, s, u, sk, rng, onAction, triggered: false);
                    if (u.Alive && Has(u, RuleModifiers.EchoCast)
                        && (sk.Kind == SkillKind.Damage || sk.Kind == SkillKind.Heal || sk.Kind == SkillKind.Shield))
                    {
                        Use(r, s, u, Echo(sk, r.EchoPower), rng, onAction, triggered: false);
                        GainEntropy(r, s, u, r.Tower?.EntropyPerEcho ?? 0, onAction);
                    }
                    return;
                }
            BasicAttack(r, s, u, rng, onAction);
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

        /// <summary>Default AI (specs/16 section 5.2): the first ready skill that would do something: heal and shield first, then the rest, each in list order.</summary>
        static bool Useful(FightRules r, FightState s, FightUnit u, CombatSkill sk)
        {
            switch (sk.Kind)
            {
                case SkillKind.Damage: return AnyEnemy(s, u);
                case SkillKind.Heal: return AnyAllyBelow(s, u, r.AiHealBelow);
                case SkillKind.Shield: return AnyAllyBelow(s, u, r.AiShieldBelow);
                case SkillKind.Taunt: return u.TauntTicks == 0;
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
                    double amt = Scaled(r, src, sk) * HealMult(r, s), total = 0;
                    var targets = Allies(s, src, sk.Target);
                    double shield = 0;
                    foreach (var t in targets) total += Heal(r, t, amt, ref shield);
                    src.Tally.HealingDone += total;
                    onAction?.Invoke(new FightAction(src.Id, sk, targets.Count == 1 ? targets[0].Id : null, total, false, false, aoe, false));
                    break;
                }
                case SkillKind.Shield:
                {
                    double amt = Scaled(r, src, sk) * HealMult(r, s);
                    foreach (var t in Allies(s, src, sk.Target))
                    {
                        t.Shield = Math.Max(t.Shield, amt);
                        t.ShieldTicks = Math.Max(t.ShieldTicks, ticks);
                    }
                    onAction?.Invoke(new FightAction(src.Id, sk, null, amt, false, false, aoe, false));
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
        /// One hit (specs/16 section 5.3): evade roll (physical only), crit roll, element, mitigation K/(K+DEF or WIS), shield,
        /// tallies, then OnEvade / OnHitTaken / OnCrit triggers and the counter rule. Draws: evade then crit, each only when its
        /// chance is above zero. Returns the damage dealt.
        /// </summary>
        static double Hit(FightRules r, FightState s, FightUnit src, FightUnit tgt, double raw, bool magical, string element, CombatSkill sk,
                          bool aoe, IRandomSource rng, Action<FightAction> onAction)
        {
            if (!magical)
            {
                double evade = EvadeChance(r, Eff(r, tgt, StatIds.Dex), Eff(r, src, StatIds.Dex));
                if (evade > 0 && Draw(rng) < evade)
                {
                    tgt.Tally.Evades++;
                    onAction?.Invoke(new FightAction(src.Id, sk, tgt.Id, 0, false, true, aoe, false));
                    Fire(r, s, tgt, FightTrigger.OnEvade, rng, onAction);
                    return 0;
                }
            }

            bool crit = false;
            double rate = CritChance(r, Eff(r, src, StatIds.Luk), Bonus(src, StatIds.CritRate));
            if (rate > 0 && Draw(rng) < rate) { crit = true; raw *= Math.Max(1.0, r.CritDmg + Bonus(src, StatIds.CritDmg)); }
            bool pierce = element == RuleModifiers.FireElement && Has(src, RuleModifiers.FirePierce);
            double elementMult = r.ElementMult(element, tgt.Source.Element);
            raw *= pierce ? Math.Max(1.0, elementMult) : elementMult;
            double dmgBonus = Bonus(src, StatIds.DmgDealt);
            if (dmgBonus != 0) raw *= Math.Max(0.0, 1 + dmgBonus);
            double mitStat = Eff(r, tgt, magical ? StatIds.Wis : StatIds.Def);
            if (pierce) mitStat *= 1 - r.FirePierce;
            double after = Mitigate(r, raw, mitStat);
            double mitigated = raw - after;
            if (r.Tower != null)
            {
                after *= 1 + CorruptionLevel(r, s.Ticks) * r.Tower.CorruptDmgPerLevel;
                if (tgt.Guard > 0 && GuardActive(s, tgt)) after *= Math.Max(0.0, 1 - tgt.Guard);
            }
            double dealt = Absorb(src, tgt, after, element);

            if (crit) src.Tally.Crits++;
            onAction?.Invoke(new FightAction(src.Id, sk, tgt.Id, dealt, crit, false, aoe, false));
            if (dealt > 0 && r.DrainFrac > 0 && src.Alive && Has(src, RuleModifiers.AbyssalDrain))
            {
                double shield = 0, healed = Heal(r, src, r.DrainFrac * dealt * HealMult(r, s), ref shield);
                src.Tally.HealingDone += healed;
                if (healed + shield > 0) onAction?.Invoke(new FightAction(src.Id, FightAction.DrainEvent, src.Id, healed + shield));
            }
            Fire(r, s, tgt, FightTrigger.OnHitTaken, rng, onAction);
            if (crit) Fire(r, s, src, FightTrigger.OnCrit, rng, onAction);

            if (!magical && !aoe && mitigated > 0 && tgt.Alive && src.Alive && Has(tgt, RuleModifiers.CounterMitigated))
            {
                tgt.Tally.Counters++;
                double c = Absorb(tgt, src, mitigated, null);
                onAction?.Invoke(new FightAction(tgt.Id, null, src.Id, c, false, false, false, true));
                GainEntropy(r, s, tgt, r.Tower?.EntropyPerCounter ?? 0, onAction);
            }
            return dealt;
        }

        /// <summary>
        /// Heal a unit by <paramref name="amount"/> (already scaled by HealMult), capped at max HP; returns the HP restored. With
        /// tidal_overflow the overflow becomes shield, up to TideShieldCap of max HP (added to <paramref name="shield"/>). It joins the
        /// unit's one shield pool, so a timed shield's expiry clears it too.
        /// </summary>
        static double Heal(FightRules r, FightUnit t, double amount, ref double shield)
        {
            double before = t.Hp, sum = t.Hp + amount;
            t.Hp = Math.Min(t.MaxHp, sum);
            double over = sum - t.MaxHp;
            if (over > 0 && Has(t, RuleModifiers.TidalOverflow))
            {
                double cap = r.TideShieldCap * t.MaxHp;
                if (t.Shield < cap)
                {
                    double add = Math.Min(cap - t.Shield, over);
                    t.Shield += add;
                    shield += add;
                }
            }
            return t.Hp - before;
        }

        /// <summary>Shield first, then HP. Records damage and kills. Element null = no per-element tally.</summary>
        static double Absorb(FightUnit src, FightUnit tgt, double amount, string element)
        {
            double absorbed = Math.Min(tgt.Shield, amount);
            tgt.Shield -= absorbed;
            double dealt = Math.Min(tgt.Hp, amount - absorbed);
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
            if (wasAlive && !tgt.Alive) src.Tally.Kills++;
            if (tgt.ChannelTicks > 0) tgt.ChannelDealt += absorbed + dealt;
            return dealt;
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

        /// <summary>A taunting enemy (lowest slot) if any; else a random living enemy (one draw).</summary>
        static FightUnit PickEnemy(FightState s, FightUnit u, IRandomSource rng)
        {
            foreach (var e in s.Units) if (e.Side != u.Side && e.Alive && e.TauntTicks > 0) return e;
            var list = Enemies(s, u);
            if (list.Count == 0) return null;
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

        static bool AnyAllyBelow(FightState s, FightUnit u, double frac)
        {
            foreach (var a in s.Units) if (a.Side == u.Side && a.Alive && a.Hp < frac * a.MaxHp) return true;
            return false;
        }

        static bool HasEffectFrom(FightUnit u, string source)
        {
            foreach (var e in u.Effects) if (e.Source == source) return true;
            return false;
        }

        static bool Has(FightUnit u, string rule) => u.Source.Rules.Contains(rule);

        // ------------------------------------------------------------------ the tower (specs/21 section 3)

        /// <summary>Arena corruption level at a tick: max(0, (tick - CORRUPT_START_TICK) / CORRUPT_EVERY_TICKS), integer division. 0 without tower rules.</summary>
        public static int CorruptionLevel(FightRules r, int ticks)
        {
            var t = r.Tower;
            if (t == null || ticks < t.CorruptStartTick) return 0;
            return (ticks - t.CorruptStartTick) / t.CorruptEveryTicks;
        }

        /// <summary>Healing and shield multiplier from corruption: max(CORRUPT_HEAL_FLOOR, 1 - level x CORRUPT_HEAL_PER_LEVEL). Exactly 1 without tower rules.</summary>
        public static double HealMult(FightRules r, FightState s) =>
            r.Tower == null ? 1.0 : Math.Max(r.Tower.CorruptHealFloor, 1 - CorruptionLevel(r, s.Ticks) * r.Tower.CorruptHealPerLevel);

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
        /// by the unit's vent count; a unit with entropy_convert shields itself instead.
        /// </summary>
        static void GainEntropy(FightRules r, FightState s, FightUnit u, double amount, Action<FightAction> onAction)
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
                u.Shield = Math.Max(u.Shield, amt);
                u.ShieldTicks = Math.Max(u.ShieldTicks, t.ConvertTurns * r.TurnTicks);
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
                    var add = NewUnit(r, WithId(p.Add, id), u.Side, CountOnSide(s, u.Side));
                    add.IsAdd = true;
                    s.Units.Add(add);
                    Fire(r, s, add, FightTrigger.BattleStart, rng, onAction);
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
                    Triggers = u.Source.Triggers, Rules = u.Source.Rules, Phases = u.Source.Phases,
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

        static Fighter WithId(Fighter f, string id) => new Fighter
        {
            Id = id, Element = f.Element, Stats = f.Stats, Skills = f.Skills, Triggers = f.Triggers, Rules = f.Rules, Phases = f.Phases,
        };

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
        public static double EvadeChance(FightRules r, double targetDex, double attackerDex) =>
            Clamp(r.EvadeBase + (targetDex - attackerDex) * r.EvadePerDex, 0, r.EvadeCap);

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
        /// Current value of a trained stat: base x (1 + sum of effect fractions), floored at STAT_FLOOR_FRAC of base. "hp" is max HP,
        /// which already carries the HP effects (RecalcMaxHp).
        /// </summary>
        public static double Eff(FightRules r, FightUnit u, string stat)
        {
            if (stat == StatIds.Hp) return u.MaxHp;
            return u.Source.Stats.Get(stat) * Math.Max(r.StatFloor, 1 + Bonus(u, stat));
        }

        /// <summary>Sum of effect values on <paramref name="stat"/> (the whole value for crit_rate and crit_dmg bonuses).</summary>
        static double Bonus(FightUnit u, string stat)
        {
            double sum = 0;
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
                w.I32(t.Crits); w.I32(t.Evades); w.I32(t.Kills); w.I32(t.Counters);
                w.I32(t.DamageByElement.Count);
                foreach (var kv in t.DamageByElement) { w.Str(kv.Key); w.F64(kv.Value); }
                if (s.Tower)   // only tower fights hash the tower fields, so every other fight keeps its hash
                {
                    w.F64(u.Entropy); w.I32(u.Vents); w.I32(u.PhaseNext); w.Bool(u.IsAdd); w.I32(u.Spawned); w.F64(u.Guard);
                    w.Bool(u.CheckPending); w.F64(u.CheckHealFrac); w.F64(u.CheckBuff);
                    w.I32(u.ChannelTicks); w.F64(u.ChannelNeed); w.F64(u.ChannelStrike); w.F64(u.ChannelDealt); w.F64(u.EnrageStep);
                }
            }
            if (s.Tower) w.I32(s.PeakCorruption);
            return Sha256.Hex(Sha256.Compute(w.ToArray()));
        }
    }
}
