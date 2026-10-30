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
    /// server (specs/10 section 9.2). Built beside the idle boss battle (IdleRng.Sim.Battle), which retires at specs/13 T7.
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
            var s = new FightState();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            AddSide(rules, s, sideA, 0, ids, nameof(sideA));
            AddSide(rules, s, sideB, 1, ids, nameof(sideB));
            return s;
        }

        static void Validate(FightRules r)
        {
            if (!(r.GaugeSize > 0 && r.TurnTicks > 0 && r.TimeLimitTicks > 0 && r.SideSize > 0 && r.HpPerPoint > 0 && r.DefK > 0
                  && r.SpdMin > 0 && r.CritDmg >= 1))
                throw new ArgumentException("fight rules out of range (specs/16 section 7)", nameof(r));
        }

        static void AddSide(FightRules rules, FightState s, IReadOnlyList<Fighter> side, int sideIndex, HashSet<string> ids, string name)
        {
            if (side == null) throw new ArgumentNullException(name);
            if (side.Count < 1 || side.Count > rules.SideSize)
                throw new ArgumentException($"a side needs 1..{rules.SideSize} fighters, got {side.Count}", name);
            for (int i = 0; i < side.Count; i++)
            {
                var f = side[i];
                ValidateFighter(f, name, i);
                if (!ids.Add(f.Id)) throw new ArgumentException($"fighter id '{f.Id}' appears twice", name);
                var u = new FightUnit
                {
                    Source = f, Side = sideIndex, Slot = i,
                    MaxHp = Math.Max(1.0, f.Stats.Hp * rules.HpPerPoint),
                    Cooldowns = new int[f.Skills.Count],
                };
                u.Hp = u.MaxHp;
                s.Units.Add(u);
            }
        }

        static void ValidateFighter(Fighter f, string side, int slot)
        {
            string at = $"{side}[{slot}]";
            if (f == null) throw new ArgumentException($"{at} is null", side);
            if (string.IsNullOrEmpty(f.Id)) throw new ArgumentException($"{at} needs an id", side);
            if (f.Stats == null || f.Skills == null || f.Triggers == null || f.Rules == null)
                throw new ArgumentException($"{at} ({f.Id}): Stats, Skills, Triggers and Rules must not be null", side);
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

        /// <summary>Run a whole fight on a fresh stream seeded with <paramref name="seed"/>.</summary>
        public static FightResult Resolve(FightRules rules, IReadOnlyList<Fighter> sideA, IReadOnlyList<Fighter> sideB, ulong seed,
                                          Action<FightAction> onAction = null)
        {
            var s = Start(rules, sideA, sideB);
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
            foreach (var u in s.Units)
                if (u.Alive) u.Gauge += Math.Max(rules.SpdMin, Eff(u, StatIds.Spd));

            for (int n = 0; n < MaxActionsPerTick; n++)
            {
                var actor = NextActor(s, rules.GaugeSize);
                if (actor == null) break;
                actor.Gauge -= rules.GaugeSize;
                Act(rules, s, actor, rng, onAction);
                var o = Decide(s, actor.Side);
                if (o != FightOutcome.Ongoing) return s.Outcome = o;
            }

            foreach (var u in s.Units) Age(u);
            if (s.Ticks >= rules.TimeLimitTicks) s.Outcome = FightOutcome.TimedOut;
            return s.Outcome;
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
            foreach (var u in s.Units) if (u.Side == side && u.Alive) return true;
            return false;
        }

        static void Age(FightUnit u)
        {
            for (int i = u.Effects.Count - 1; i >= 0; i--)
            {
                var e = u.Effects[i];
                if (e.TicksLeft < 0) continue;
                if (--e.TicksLeft <= 0) u.Effects.RemoveAt(i);
            }
            if (u.ShieldTicks > 0 && --u.ShieldTicks == 0) u.Shield = 0;
            if (u.TauntTicks > 0) u.TauntTicks--;
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
                if (u.Alive) Use(r, s, u, sk, rng, onAction, triggered: true);
        }

        // ------------------------------------------------------------------ actions

        static void Act(FightRules r, FightState s, FightUnit u, IRandomSource rng, Action<FightAction> onAction)
        {
            u.Tally.Actions++;
            Fire(r, s, u, FightTrigger.OnAction, rng, onAction);
            if (!u.Alive) return;
            for (int i = 0; i < u.Cooldowns.Length; i++) if (u.Cooldowns[i] > 0) u.Cooldowns[i]--;
            var skills = u.Source.Skills;
            for (int i = 0; i < skills.Count; i++)
            {
                var sk = skills[i];
                if (sk.Kind == SkillKind.Passive || u.Cooldowns[i] > 0 || !Useful(r, s, u, sk)) continue;
                u.Cooldowns[i] = sk.Cooldown;
                Use(r, s, u, sk, rng, onAction, triggered: false);
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
            double str = Eff(u, StatIds.Str), intel = Eff(u, StatIds.Int);
            bool magical = intel > str;
            Hit(r, s, u, target, r.BasicAttackPower * Math.Max(str, intel), magical, u.Source.Element, null, false, rng, onAction);
        }

        /// <summary>Default AI (specs/16 section 5.2): the first ready skill, in list order, that would do something.</summary>
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
                    double raw = Scaled(src, sk);
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
                    double amt = Scaled(src, sk), total = 0;
                    var targets = Allies(s, src, sk.Target);
                    foreach (var t in targets)
                    {
                        double before = t.Hp;
                        t.Hp = Math.Min(t.MaxHp, t.Hp + amt);
                        total += t.Hp - before;
                    }
                    src.Tally.HealingDone += total;
                    onAction?.Invoke(new FightAction(src.Id, sk, targets.Count == 1 ? targets[0].Id : null, total, false, false, aoe, false));
                    break;
                }
                case SkillKind.Shield:
                {
                    double amt = Scaled(src, sk);
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
                        AddEffect(t, sk.Stat, sk.Power, permanent ? -1 : ticks, sk.Id);
                    if (!permanent || triggered) onAction?.Invoke(new FightAction(src.Id, sk, null, sk.Power, false, false, aoe, false));
                    break;
                }
                case SkillKind.Debuff:
                {
                    if (sk.Target == SkillTarget.AllEnemies)
                        foreach (var t in Enemies(s, src)) AddEffect(t, sk.Stat, -sk.Power, ticks, sk.Id);
                    else
                    {
                        var t = PickEnemy(s, src, rng);
                        if (t != null) AddEffect(t, sk.Stat, -sk.Power, ticks, sk.Id);
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
                double evade = Clamp(r.EvadeBase + (Eff(tgt, StatIds.Dex) - Eff(src, StatIds.Dex)) * r.EvadePerDex, 0, r.EvadeCap);
                if (evade > 0 && Draw(rng) < evade)
                {
                    tgt.Tally.Evades++;
                    onAction?.Invoke(new FightAction(src.Id, sk, tgt.Id, 0, false, true, aoe, false));
                    Fire(r, s, tgt, FightTrigger.OnEvade, rng, onAction);
                    return 0;
                }
            }

            bool crit = false;
            double rate = Clamp(r.CritBase + Eff(src, StatIds.Luk) * r.CritPerLuk + Bonus(src, StatIds.CritRate), 0, r.CritCap);
            if (rate > 0 && Draw(rng) < rate) { crit = true; raw *= Math.Max(1.0, r.CritDmg + Bonus(src, StatIds.CritDmg)); }
            raw *= r.ElementMult(element, tgt.Source.Element);
            double after = raw * r.DefK / (r.DefK + Eff(tgt, magical ? StatIds.Wis : StatIds.Def));
            double mitigated = raw - after;
            double dealt = Absorb(src, tgt, after, element);

            if (crit) src.Tally.Crits++;
            onAction?.Invoke(new FightAction(src.Id, sk, tgt.Id, dealt, crit, false, aoe, false));
            Fire(r, s, tgt, FightTrigger.OnHitTaken, rng, onAction);
            if (crit) Fire(r, s, src, FightTrigger.OnCrit, rng, onAction);

            if (!magical && !aoe && mitigated > 0 && tgt.Alive && src.Alive && Has(tgt, RuleModifiers.CounterMitigated))
            {
                tgt.Tally.Counters++;
                double c = Absorb(tgt, src, mitigated, null);
                onAction?.Invoke(new FightAction(tgt.Id, null, src.Id, c, false, false, false, true));
            }
            return dealt;
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

        // ------------------------------------------------------------------ stats

        static void AddEffect(FightUnit u, string stat, double frac, int ticks, string source)
        {
            if (stat == StatIds.Hp && ticks < 0)
            {
                // a permanent HP bonus raises max HP and current HP together
                double m = Math.Max(0.1, 1 + frac);
                u.MaxHp *= m;
                u.Hp *= m;
                return;
            }
            u.Effects.Add(new FightEffect { Stat = stat, Frac = frac, TicksLeft = ticks, Source = source });
        }

        static double Scaled(FightUnit src, CombatSkill sk) => (sk.Scales == StatIds.Hp ? src.MaxHp : Eff(src, sk.Scales)) * sk.Power;

        /// <summary>
        /// Current value of a trained stat: base x (1 + sum of effect fractions), floored at 10% of base. "hp" is max HP.
        /// </summary>
        public static double Eff(FightUnit u, string stat)
        {
            if (stat == StatIds.Hp) return u.MaxHp;
            return u.Source.Stats.Get(stat) * Math.Max(0.1, 1 + Bonus(u, stat));
        }

        /// <summary>Sum of effect values on <paramref name="stat"/> (the whole value for crit_rate and crit_dmg bonuses).</summary>
        static double Bonus(FightUnit u, string stat)
        {
            double sum = 0;
            foreach (var e in u.Effects) if (e.Stat == stat) sum += e.Frac;
            return sum;
        }

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        // ------------------------------------------------------------------ hashing

        /// <summary>
        /// Lowercase hex SHA-256 over a canonical encoding of the whole fight (same conventions as StateHasher): the golden
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
            }
            return Sha256.Hex(Sha256.Compute(w.ToArray()));
        }
    }
}
