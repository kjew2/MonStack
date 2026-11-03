using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Combat
{
    // Idea-pool lane D: debuffs, stacking and skill locks (#137), through OnHitLanded, ModifyOutgoing, SkillReady and
    // OnSkillUsed (specs/16 section 6.2).
    //   hemorrhage         each landed hit fills the target's bleed gauge (MON_BLEED_PER_HIT x DEX / MON_BLEED_DEX_REF); at
    //                      BleedGauge it bursts for MON_BLEED_BURST of the target's max HP as true damage and resets
    //   privileged_status  x(1 + MON_PRIV_BONUS) on a target with at least MON_PRIV_DEBUFFS debuffs (BattleSim.DebuffCount)
    //   equilibrium        a hit marks its target with its element; the next hit of another element on it deals x(1 +
    //                      MON_EQ_BONUS) and clears the mark
    //   corruption         each active skill cast as an action is cast again at once at full power, then locked for the fight
    //   dead_branch        each lock of one of its skills gains it a random active skill from the pool (one draw, carriers only)
    // State: the bleed gauge on every unit (hashed when some unit carries hemorrhage), the element marks on the equilibrium
    // carrier, the locked skills and the skills dead_branch drew (hashed for corruption and dead_branch carriers).
    public static partial class BattleSim
    {
        /// <summary>hemorrhage: the bleed gauge bursts when it reaches this.</summary>
        public const double BleedGauge = 100;

        /// <summary>hemorrhage: the gauge burst; Target took Amount true damage.</summary>
        public const string BleedEvent = FightAction.RulePrefix + RuleModifiers.Hemorrhage;

        /// <summary>privileged_status: the bonus applied to a hit; Amount is the target's debuff count.</summary>
        public const string PrivEvent = FightAction.RulePrefix + RuleModifiers.PrivilegedStatus;

        /// <summary>equilibrium: a hit of another element consumed the target's mark; Amount is the damage bonus.</summary>
        public const string EqEvent = FightAction.RulePrefix + RuleModifiers.Equilibrium;

        /// <summary>corruption: the skill just cast is cast again (logged before the second cast); Amount is the unit's locked skills, this one included.</summary>
        public const string CorruptEvent = FightAction.RulePrefix + RuleModifiers.Corruption;

        /// <summary>dead_branch: the unit gained a random skill; Amount is its index in the unit's skill list.</summary>
        public const string BranchEvent = FightAction.RulePrefix + RuleModifiers.DeadBranch;

        // ------------------------------------------------------------------ hemorrhage

        static partial void OnHitLanded_D(RuleCtx c, HitInfo h)
        {
            var r = c.Rules;
            if (!Has(h.Src, RuleModifiers.Hemorrhage) || !h.Tgt.Alive) return;
            var tgt = h.Tgt;
            tgt.Bleed += r.BleedPerHit * Eff(r, h.Src, StatIds.Dex) / r.BleedDexRef;
            if (tgt.Bleed < BleedGauge) return;
            tgt.Bleed = 0;
            double dmg = Absorb(c, h.Src, tgt, r.BleedBurst * tgt.MaxHp, null);
            c.Log?.Invoke(new FightAction(h.Src.Id, BleedEvent, tgt.Id, dmg));
            Reborn(tgt, c.Log);
        }

        // ------------------------------------------------------------------ privileged_status and equilibrium

        static partial void ModifyOutgoing_D(RuleCtx c, HitInfo h)
        {
            var r = c.Rules;
            var src = h.Src;
            if (Has(src, RuleModifiers.PrivilegedStatus))
            {
                int debuffs = DebuffCount(h.Tgt);
                if (debuffs >= r.PrivDebuffs)
                {
                    h.Raw *= 1 + r.PrivBonus;
                    c.Log?.Invoke(new FightAction(src.Id, PrivEvent, h.Tgt.Id, debuffs));
                }
            }
            if (h.Element != null && Has(src, RuleModifiers.Equilibrium))
            {
                // A hit that consumes the mark leaves none (agent decision, #137): alternating elements pays every other hit.
                var marks = src.EqMarks ??= new SortedDictionary<string, string>(StringComparer.Ordinal);
                if (marks.TryGetValue(h.Tgt.Id, out var mark) && mark != h.Element)
                {
                    marks.Remove(h.Tgt.Id);
                    h.Raw *= 1 + r.EqBonus;
                    c.Log?.Invoke(new FightAction(src.Id, EqEvent, h.Tgt.Id, r.EqBonus));
                }
                else marks[h.Tgt.Id] = h.Element;
            }
        }

        // ------------------------------------------------------------------ corruption and dead_branch

        /// <summary>A locked skill is never ready again this fight (whatever lane A's blood_magic says).</summary>
        static partial void SkillReady_D(RuleCtx c, FightUnit u, int index, ref bool ready)
        {
            if (u.SkillLocked(index)) ready = false;
        }

        /// <summary>
        /// corruption: after the cast (and its echo_cast repeat, and lane A's blood_magic cost), cast the skill again at full power
        /// as part of the same cast (no depth; never echoed or corrupted again), then lock it.
        /// </summary>
        static partial void OnSkillUsed_D(RuleCtx c, FightUnit u, int index, CombatSkill sk)
        {
            if (!Has(u, RuleModifiers.Corruption) || u.SkillLocked(index)) return;
            if (u.Alive && EnterGuard(c.Rules, c.State, GuardKind.Corrupt, false, out var saved))
            {
                try
                {
                    c.Log?.Invoke(new FightAction(u.Id, CorruptEvent, u.Id, (u.Locked?.Count ?? 0) + 1));
                    Use(c.Rules, c.State, u, sk, c.Rng, c.Log, triggered: false);
                }
                finally { ExitGuard(c.State, saved); }
            }
            LockSkill(c, u, index);
        }

        /// <summary>
        /// Lock the unit's skill at <paramref name="index"/> for the rest of the fight (a lane may call this for its own lock
        /// sources). With dead_branch, each new lock of a living unit gains it a random active skill from the pool.
        /// </summary>
        static void LockSkill(RuleCtx c, FightUnit u, int index)
        {
            if (u.SkillLocked(index)) return;
            (u.Locked ??= new List<int>()).Add(index);
            if (u.Alive && Has(u, RuleModifiers.DeadBranch)) GainBranchSkill(c, u);
        }

        /// <summary>
        /// dead_branch: one draw on the battle stream picks a pool skill the unit does not have (by id); it joins the end of the
        /// unit's skill list, ready at once. An empty pool (or one the unit already has all of) draws nothing.
        /// </summary>
        static void GainBranchSkill(RuleCtx c, FightUnit u)
        {
            var pool = c.Rules.BranchPool;
            if (pool == null || pool.Count == 0) return;
            var options = new List<CombatSkill>(pool.Count);
            foreach (var sk in pool) if (!HasSkill(u, sk.Id)) options.Add(sk);
            if (options.Count == 0) return;
            var pick = options.Count == 1 || c.Rng == null ? options[0]
                : options[Math.Min(options.Count - 1, (int)(c.Rng.NextDouble() * options.Count))];
            var src = u.Source;
            var skills = new List<CombatSkill>(src.Skills) { pick };
            var cd = new int[skills.Count];
            Array.Copy(u.Cooldowns, cd, u.Cooldowns.Length);
            u.Cooldowns = cd;
            u.Source = new Fighter
            {
                Id = src.Id, Element = src.Element, Stats = src.Stats, Skills = skills, Triggers = src.Triggers, Rules = src.Rules,
                Phases = src.Phases, Gambits = src.Gambits,
            };
            (u.Branched ??= new List<string>()).Add(pick.Id);
            c.Log?.Invoke(new FightAction(u.Id, BranchEvent, u.Id, skills.Count - 1));
        }

        static bool HasSkill(FightUnit u, string id)
        {
            foreach (var sk in u.Source.Skills) if (sk.Id == id) return true;
            return false;
        }

        // ------------------------------------------------------------------ hashing and validation

        static partial void HashUnit_D(CanonicalWriter w, FightState s, FightUnit u)
        {
            if (Has(u, RuleModifiers.Equilibrium))
            {
                var marks = u.EqMarks;
                w.I32(marks?.Count ?? 0);
                if (marks != null) foreach (var kv in marks) { w.Str(kv.Key); w.Str(kv.Value); }
            }
            if (Has(u, RuleModifiers.Corruption) || Has(u, RuleModifiers.DeadBranch))
            {
                w.I32(u.Locked?.Count ?? 0);
                if (u.Locked != null) foreach (var i in u.Locked) w.I32(i);
            }
            if (Has(u, RuleModifiers.DeadBranch))
            {
                w.I32(u.Branched?.Count ?? 0);
                if (u.Branched != null) foreach (var id in u.Branched) w.Str(id);
            }
        }

        /// <summary>hemorrhage: the bleed gauge lives on the targets, so every unit's is hashed once some unit carries the rule.</summary>
        static partial void HashFight_D(CanonicalWriter w, FightState s)
        {
            bool any = false;
            foreach (var u in s.Units) any |= Has(u, RuleModifiers.Hemorrhage);
            if (!any) return;
            foreach (var u in s.Units) w.F64(u.Bleed);
        }

        static partial void ValidateRules_D(FightRules r, ref bool ok)
        {
            if (!(r.BleedPerHit >= 0 && !double.IsInfinity(r.BleedPerHit) && r.BleedDexRef > 0 && !double.IsInfinity(r.BleedDexRef)
                  && In01(r.BleedBurst) && r.PrivBonus >= 0 && !double.IsInfinity(r.PrivBonus) && r.PrivDebuffs >= 1
                  && r.EqBonus >= 0 && !double.IsInfinity(r.EqBonus)))
                ok = false;
        }
    }

    public sealed partial class FightUnit
    {
        /// <summary>hemorrhage: this unit's bleed gauge, filled by the hits of carriers (0 up to BattleSim.BleedGauge).</summary>
        public double Bleed { get; set; }

        /// <summary>equilibrium: target id -> the element of the unit's last unconsumed hit on it; null until its first mark.</summary>
        internal SortedDictionary<string, string> EqMarks { get; set; }

        /// <summary>Skill indices locked for the rest of the fight (corruption), in lock order; null = none.</summary>
        internal List<int> Locked { get; set; }

        /// <summary>dead_branch: the ids of the skills it drew, in order; null = none.</summary>
        internal List<string> Branched { get; set; }

        /// <summary>Whether the skill at <paramref name="index"/> (indexed like Source.Skills) is locked for the rest of the fight.</summary>
        public bool SkillLocked(int index) => Locked != null && Locked.Contains(index);

        partial void Clone_D(FightUnit copy)
        {
            if (EqMarks != null) copy.EqMarks = new SortedDictionary<string, string>(EqMarks, StringComparer.Ordinal);
            if (Locked != null) copy.Locked = new List<int>(Locked);
            if (Branched != null) copy.Branched = new List<string>(Branched);
        }
    }

    public sealed partial class FightRules
    {
        /// <summary>hemorrhage: bleed added per hit at MON_BLEED_DEX_REF DEX (MON_BLEED_PER_HIT).</summary>
        public double BleedPerHit { get; set; }

        /// <summary>hemorrhage: the DEX at which a hit adds exactly MON_BLEED_PER_HIT (MON_BLEED_DEX_REF).</summary>
        public double BleedDexRef { get; set; } = 1;

        /// <summary>hemorrhage: the burst's true damage, fraction of the target's max HP (MON_BLEED_BURST).</summary>
        public double BleedBurst { get; set; }

        /// <summary>privileged_status: the damage bonus (MON_PRIV_BONUS).</summary>
        public double PrivBonus { get; set; }

        /// <summary>privileged_status: the debuffs the target needs (MON_PRIV_DEBUFFS).</summary>
        public int PrivDebuffs { get; set; } = 1;

        /// <summary>equilibrium: the damage bonus of a hit that consumes a mark (MON_EQ_BONUS).</summary>
        public double EqBonus { get; set; }

        /// <summary>
        /// dead_branch: the skills it may draw, sorted by id: the active skills of the skill nodes of native, generic and book trees
        /// (not chase or fusion trees, trigger effects or boss skills). Empty for hand-built rules.
        /// </summary>
        public IReadOnlyList<CombatSkill> BranchPool { get; set; } = Array.Empty<CombatSkill>();

        static partial void Load_D(FightRules r, ContentDb c)
        {
            var k = c.ConstantValues;
            r.BleedPerHit = k["MON_BLEED_PER_HIT"];
            r.BleedDexRef = k["MON_BLEED_DEX_REF"];
            r.BleedBurst = k["MON_BLEED_BURST"];
            r.PrivBonus = k["MON_PRIV_BONUS"];
            r.PrivDebuffs = (int)k["MON_PRIV_DEBUFFS"];
            r.EqBonus = k["MON_EQ_BONUS"];
            r.BranchPool = Pools.GetValue(c, BranchPoolOf);
        }

        /// <summary>The dead_branch pool per content pack, built once (FromContent runs for every fight).</summary>
        static readonly ConditionalWeakTable<ContentDb, IReadOnlyList<CombatSkill>> Pools = new ConditionalWeakTable<ContentDb, IReadOnlyList<CombatSkill>>();

        /// <summary>The dead_branch pool from content (see <see cref="BranchPool"/>).</summary>
        public static IReadOnlyList<CombatSkill> BranchPoolOf(ContentDb c)
        {
            var byId = new SortedDictionary<string, CombatSkill>(StringComparer.Ordinal);
            foreach (var t in c.TreeList)
            {
                if (t.Kind != TreeKind.Native && t.Kind != TreeKind.Generic && t.Kind != TreeKind.Book) continue;
                foreach (var n in t.Nodes)
                    if (n.Kind == NodeKind.Skill && n.Skill != null && n.Skill.Kind != SkillKind.Passive) byId[n.Skill.Id] = n.Skill;
            }
            return new List<CombatSkill>(byId.Values).AsReadOnly();
        }
    }
}
