using System;
using System.Collections.Generic;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;

namespace IdleRng.Sim.Battle
{
    public enum BattleOutcome { Ongoing, Won, Wiped }

    /// <summary>Where a triggered effect fires (specs/11 section 8.5). Kit passives use BattleStart; uniques (#64) add more.</summary>
    public enum BattleTrigger { BattleStart, OnAction, OnBasicAttack, OnCrit, OnHitTaken }

    /// <summary>An effect that fires at a trigger point. The effect uses the kit skill vocabulary (SkillDef).</summary>
    public readonly struct TriggeredEffect
    {
        public TriggeredEffect(BattleTrigger trigger, SkillDef effect) { Trigger = trigger; Effect = effect; }
        public BattleTrigger Trigger { get; }
        public SkillDef Effect { get; }
    }

    /// <summary>One action, for events and the UI. Skill is null for a basic attack or a boss attack.</summary>
    public readonly struct BattleActionInfo
    {
        public BattleActionInfo(string actor, SkillDef skill, string target, double amount, bool crit, bool aoe)
        {
            Actor = actor; Skill = skill; Target = target; Amount = amount; Crit = crit; Aoe = aoe;
        }

        /// <summary>Hero id, or null for the boss.</summary>
        public string Actor { get; }
        public SkillDef Skill { get; }

        /// <summary>Hero id, "boss", or null for multi-target effects.</summary>
        public string Target { get; }
        public double Amount { get; }
        public bool Crit { get; }
        public bool Aoe { get; }
    }

    /// <summary>
    /// The 4v1 ATB boss battle (specs/11 section 8). Pure functions over a BattleState plus a random source: the same
    /// state and draws give the same battle, which keeps replay, goldens and server verification possible.
    /// </summary>
    public static class BattleEngine
    {
        public const string BossTarget = "boss";
        const int MaxActionsPerTick = 64;

        // ------------------------------------------------------------------ setup

        /// <summary>A companion snapshot: base stats x level x promotion, with HP/ATK/DEF also x the account power multiplier.</summary>
        public static BattleUnit CreateCompanion(ContentDb c, string heroId, int level, int promotions, double powerMult)
        {
            var h = c.Heroes[heroId];
            double m = c.LevelMult(level) * c.PromoMult(promotions) * powerMult;
            var u = new BattleUnit
            {
                HeroId = heroId, Promotions = promotions, Element = h.Element,
                MaxHp = h.Hp * m, Atk = h.Atk * m, Def = h.Def * m, Spd = h.Spd, CritRate = h.CritRate, CritDmg = h.CritDmg,
                Cooldowns = new int[c.Kits[heroId].Count],
            };
            u.Hp = u.MaxHp;
            return u;
        }

        /// <summary>The boss of <paramref name="stage"/>: HP from the boss rule, ATK/DEF as multiples of the stage's enemy HP.</summary>
        public static BattleUnit CreateBoss(ContentDb c, int stage)
        {
            var rule = c.BossRuleFor(stage);
            double hp = c.EnemyHp(stage);
            var u = new BattleUnit
            {
                Element = c.BossElement(stage), MaxHp = rule.HpMult * hp, Atk = rule.AtkMult * hp, Def = rule.DefMult * hp,
                Spd = rule.Spd, CritRate = 0, CritDmg = 1,
            };
            u.Hp = u.MaxHp;
            return u;
        }

        public static BattleState Start(ContentDb c, BattleUnit[] party, BattleUnit boss, int stage)
        {
            var b = new BattleState { Stage = stage, DefK = c.Constants.DefKMult * c.EnemyHp(stage), Party = party, Boss = boss };
            foreach (var u in party)
                if (u != null) Fire(c, b, u, BattleTrigger.BattleStart, null);
            return b;
        }

        // ------------------------------------------------------------------ triggers (the hook uniques plug into)

        /// <summary>
        /// Every triggered effect a unit carries. Phase 2: unlocked kit passives at BattleStart. Uniques (#64) add their
        /// entries here, derived from the unit's snapshot, so a saved battle re-derives the same list after a reload.
        /// </summary>
        public static void CollectTriggers(ContentDb c, BattleUnit u, List<TriggeredEffect> into)
        {
            if (u.HeroId == null) return;
            foreach (var sk in c.Kits[u.HeroId])
                if (sk.Kind == SkillKind.Passive && sk.UnlockPromotion <= u.Promotions)
                    into.Add(new TriggeredEffect(BattleTrigger.BattleStart, sk));
        }

        static void Fire(ContentDb c, BattleState b, BattleUnit u, BattleTrigger trigger, Action<BattleActionInfo> onAction)
        {
            var list = new List<TriggeredEffect>();
            CollectTriggers(c, u, list);
            foreach (var t in list)
                if (t.Trigger == trigger) Apply(c, b, u, t.Effect, null, onAction);
        }

        // ------------------------------------------------------------------ one tick

        /// <summary>Advance the battle one tick: fill gauges, run every action that is due, then age timed effects.</summary>
        public static BattleOutcome Step(ContentDb c, BattleState b, IRandomSource rng, Action<BattleActionInfo> onAction = null)
        {
            var k = c.Constants;
            foreach (var u in b.Party)
                if (u != null && u.Alive) u.Gauge += Eff(u, "spd");
            if (b.Boss.Alive) b.Boss.Gauge += Eff(b.Boss, "spd");

            for (int n = 0; n < MaxActionsPerTick; n++)
            {
                var actor = NextActor(b, k.BattleGauge);
                if (actor == null) break;
                actor.Gauge -= k.BattleGauge;
                if (actor == b.Boss) BossAct(c, b, rng, onAction);
                else CompanionAct(c, b, actor, rng, onAction);
                var o = Outcome(b);
                if (o != BattleOutcome.Ongoing) return o;
            }

            foreach (var u in b.Party) if (u != null) Age(u);
            Age(b.Boss);
            return Outcome(b);
        }

        /// <summary>Highest gauge at or above the threshold acts first; ties go to the lower party slot, the boss last.</summary>
        static BattleUnit NextActor(BattleState b, double gauge)
        {
            BattleUnit best = null;
            foreach (var u in b.Party)
                if (u != null && u.Alive && u.Gauge >= gauge && (best == null || u.Gauge > best.Gauge)) best = u;
            if (b.Boss.Alive && b.Boss.Gauge >= gauge && (best == null || b.Boss.Gauge > best.Gauge)) best = b.Boss;
            return best;
        }

        public static BattleOutcome Outcome(BattleState b)
        {
            if (!b.Boss.Alive) return BattleOutcome.Won;
            foreach (var u in b.Party) if (u != null && u.Alive) return BattleOutcome.Ongoing;
            return BattleOutcome.Wiped;
        }

        static void Age(BattleUnit u)
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

        // ------------------------------------------------------------------ actions

        static void CompanionAct(ContentDb c, BattleState b, BattleUnit u, IRandomSource rng, Action<BattleActionInfo> onAction)
        {
            Fire(c, b, u, BattleTrigger.OnAction, onAction);
            for (int i = 0; i < u.Cooldowns.Length; i++) if (u.Cooldowns[i] > 0) u.Cooldowns[i]--;
            var kit = c.Kits[u.HeroId];
            for (int i = 0; i < kit.Count; i++)
            {
                var sk = kit[i];
                if (sk.Kind == SkillKind.Passive || sk.UnlockPromotion > u.Promotions || u.Cooldowns[i] > 0) continue;
                if (!Useful(c, b, u, sk)) continue;
                u.Cooldowns[i] = sk.Cooldown;
                Apply(c, b, u, sk, rng, onAction);
                return;
            }
            // basic attack
            Fire(c, b, u, BattleTrigger.OnBasicAttack, onAction);
            bool crit;
            double dmg = Damage(c, b, u, b.Boss, Eff(u, "atk") * c.Constants.BasicAttackPower, rng, out crit);
            onAction?.Invoke(new BattleActionInfo(u.HeroId, null, BossTarget, dmg, crit, false));
            if (crit) Fire(c, b, u, BattleTrigger.OnCrit, onAction);
        }

        static void BossAct(ContentDb c, BattleState b, IRandomSource rng, Action<BattleActionInfo> onAction)
        {
            var boss = b.Boss;
            b.BossActions++;
            var rule = c.BossRuleFor(b.Stage);
            if (rule.AoeEvery > 0 && b.BossActions % rule.AoeEvery == 0)
            {
                double total = 0;
                foreach (var t in b.Party)
                    if (t != null && t.Alive) { total += Damage(c, b, boss, t, Eff(boss, "atk") * rule.AoePower, rng, out _); Fire(c, b, t, BattleTrigger.OnHitTaken, onAction); }
                onAction?.Invoke(new BattleActionInfo(null, null, null, total, false, true));
                return;
            }
            var target = TauntTarget(b) ?? RandomLiving(b, rng);
            if (target == null) return;
            double dmg = Damage(c, b, boss, target, Eff(boss, "atk"), rng, out bool crit);
            onAction?.Invoke(new BattleActionInfo(null, null, target.HeroId, dmg, crit, false));
            Fire(c, b, target, BattleTrigger.OnHitTaken, onAction);
        }

        static BattleUnit TauntTarget(BattleState b)
        {
            foreach (var u in b.Party) if (u != null && u.Alive && u.TauntTicks > 0) return u;
            return null;
        }

        static BattleUnit RandomLiving(BattleState b, IRandomSource rng)
        {
            int n = 0;
            foreach (var u in b.Party) if (u != null && u.Alive) n++;
            if (n == 0) return null;
            int pick = Math.Min(n - 1, (int)(rng.NextDouble() * n));
            foreach (var u in b.Party)
                if (u != null && u.Alive && pick-- == 0) return u;
            return null;
        }

        static BattleUnit LowestAlly(BattleState b)
        {
            BattleUnit best = null;
            foreach (var u in b.Party)
                if (u != null && u.Alive && (best == null || u.Hp / u.MaxHp < best.Hp / best.MaxHp)) best = u;
            return best;
        }

        static bool AnyAllyBelow(BattleState b, double frac)
        {
            foreach (var u in b.Party) if (u != null && u.Alive && u.Hp < frac * u.MaxHp) return true;
            return false;
        }

        static bool HasEffectFrom(BattleUnit u, string source)
        {
            foreach (var e in u.Effects) if (e.Source == source) return true;
            return false;
        }

        /// <summary>Simple companion AI (specs/11 section 8.3): use the first ready skill in kit order that would do something.</summary>
        static bool Useful(ContentDb c, BattleState b, BattleUnit u, SkillDef sk)
        {
            var k = c.Constants;
            switch (sk.Kind)
            {
                case SkillKind.Damage: return true;
                case SkillKind.Heal: return AnyAllyBelow(b, k.AiHealBelow);
                case SkillKind.Shield: return AnyAllyBelow(b, k.AiShieldBelow);
                case SkillKind.Taunt: return u.TauntTicks == 0;
                case SkillKind.Buff: return !HasEffectFrom(u, sk.Id);
                case SkillKind.Debuff: return !HasEffectFrom(b.Boss, sk.Id);
                default: return false;
            }
        }

        // ------------------------------------------------------------------ effects (shared by skills and triggers)

        /// <summary>
        /// Apply one effect from <paramref name="src"/>. Kit skills and triggered effects (passives now, uniques later) all
        /// come through here. Passive = permanent stat change. rng may be null only for effects that cannot crit.
        /// </summary>
        public static void Apply(ContentDb c, BattleState b, BattleUnit src, SkillDef sk, IRandomSource rng, Action<BattleActionInfo> onAction)
        {
            int ticks = sk.Duration * c.Constants.TurnTicks;
            bool fromBoss = src == b.Boss;
            switch (sk.Kind)
            {
                case SkillKind.Damage:
                {
                    // every target is on the other side; the 4v1 boss is the only enemy of a companion
                    double raw = Scaled(src, sk);
                    if (fromBoss)
                    {
                        foreach (var t in b.Party) if (t != null && t.Alive) Damage(c, b, src, t, raw, rng, out _);
                        onAction?.Invoke(new BattleActionInfo(null, sk, null, raw, false, true));
                    }
                    else
                    {
                        double dmg = Damage(c, b, src, b.Boss, raw, rng, out bool crit);
                        onAction?.Invoke(new BattleActionInfo(src.HeroId, sk, BossTarget, dmg, crit, sk.Target == SkillTarget.AllEnemies));
                        if (crit) Fire(c, b, src, BattleTrigger.OnCrit, onAction);
                    }
                    break;
                }
                case SkillKind.Heal:
                {
                    double amt = Scaled(src, sk), total = 0;
                    foreach (var t in Allies(b, src, sk.Target))
                    {
                        double before = t.Hp;
                        t.Hp = Math.Min(t.MaxHp, t.Hp + amt);
                        total += t.Hp - before;
                    }
                    onAction?.Invoke(new BattleActionInfo(src.HeroId, sk, sk.Target == SkillTarget.LowestAlly ? LowestAlly(b)?.HeroId : null, total, false, sk.Target == SkillTarget.AllAllies));
                    break;
                }
                case SkillKind.Shield:
                {
                    double amt = Scaled(src, sk);
                    foreach (var t in Allies(b, src, sk.Target))
                    {
                        t.Shield = Math.Max(t.Shield, amt);
                        t.ShieldTicks = Math.Max(t.ShieldTicks, ticks);
                    }
                    onAction?.Invoke(new BattleActionInfo(src.HeroId, sk, null, amt, false, sk.Target == SkillTarget.AllAllies));
                    break;
                }
                case SkillKind.Taunt:
                    src.TauntTicks = Math.Max(src.TauntTicks, ticks);
                    onAction?.Invoke(new BattleActionInfo(src.HeroId, sk, src.HeroId, 0, false, false));
                    break;
                case SkillKind.Buff:
                case SkillKind.Passive:
                {
                    bool permanent = sk.Kind == SkillKind.Passive;
                    foreach (var t in Allies(b, src, sk.Target))
                        AddEffect(t, sk.Stat, sk.Power, permanent ? -1 : ticks, sk.Id);
                    if (!permanent) onAction?.Invoke(new BattleActionInfo(src.HeroId, sk, null, sk.Power, false, sk.Target == SkillTarget.AllAllies));
                    break;
                }
                case SkillKind.Debuff:
                {
                    if (fromBoss) { foreach (var t in b.Party) if (t != null && t.Alive) AddEffect(t, sk.Stat, -sk.Power, ticks, sk.Id); }
                    else AddEffect(b.Boss, sk.Stat, -sk.Power, ticks, sk.Id);
                    onAction?.Invoke(new BattleActionInfo(src.HeroId, sk, fromBoss ? null : BossTarget, sk.Power, false, fromBoss));
                    break;
                }
            }
        }

        static IEnumerable<BattleUnit> Allies(BattleState b, BattleUnit src, SkillTarget target)
        {
            if (src == b.Boss || target == SkillTarget.Self) { yield return src; yield break; }
            if (target == SkillTarget.LowestAlly)
            {
                var low = LowestAlly(b);
                if (low != null) yield return low;
                yield break;
            }
            foreach (var u in b.Party) if (u != null && u.Alive) yield return u;
        }

        static void AddEffect(BattleUnit u, string stat, double frac, int ticks, string source)
        {
            if (stat == "hp" && ticks < 0)
            {
                // a permanent HP bonus raises max HP and current HP together
                u.MaxHp *= 1 + frac;
                u.Hp *= 1 + frac;
                return;
            }
            u.Effects.Add(new BattleEffect { Stat = stat, Frac = frac, TicksLeft = ticks, Source = source });
        }

        static double Scaled(BattleUnit src, SkillDef sk) => (sk.Scales == "hp" ? src.MaxHp : Eff(src, sk.Scales ?? "atk")) * sk.Power;

        /// <summary>
        /// Deal <paramref name="raw"/> to <paramref name="tgt"/>: crit roll (one draw when the attacker can crit), element
        /// multiplier, DEF mitigation K/(K+DEF), then the shield absorbs first. Returns the damage dealt.
        /// </summary>
        static double Damage(ContentDb c, BattleState b, BattleUnit src, BattleUnit tgt, double raw, IRandomSource rng, out bool crit)
        {
            crit = false;
            double rate = Clamp01(Eff(src, "crit_rate"));
            if (rate > 0 && rng != null && rng.NextDouble() < rate) { crit = true; raw *= Math.Max(1.0, Eff(src, "crit_dmg")); }
            raw *= c.ElementMult(src.Element, tgt.Element);
            double def = Eff(tgt, "def");
            raw *= b.DefK / (b.DefK + def);
            double absorbed = Math.Min(tgt.Shield, raw);
            tgt.Shield -= absorbed;
            double dealt = raw - absorbed;
            tgt.Hp = Math.Max(0, tgt.Hp - dealt);
            return dealt;
        }

        /// <summary>
        /// Current value of a stat with timed and permanent effects. hp/atk/def/spd: base x (1 + sum of fractions), floored at
        /// 10% of base; crit_rate/crit_dmg: base + sum.
        /// </summary>
        public static double Eff(BattleUnit u, string stat)
        {
            double sum = 0;
            foreach (var e in u.Effects) if (e.Stat == stat) sum += e.Frac;
            switch (stat)
            {
                case "atk": return u.Atk * Math.Max(0.1, 1 + sum);
                case "def": return u.Def * Math.Max(0.1, 1 + sum);
                case "spd": return u.Spd * Math.Max(0.1, 1 + sum);
                case "hp": return u.MaxHp;
                case "crit_rate": return u.CritRate + sum;
                case "crit_dmg": return u.CritDmg + sum;
                default: return 0;
            }
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
