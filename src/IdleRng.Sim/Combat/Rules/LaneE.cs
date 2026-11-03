using System;
using System.Collections.Generic;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Combat
{
    // Idea-pool lane E: team size and mode (#138): lone_wolf, arise and last_rite (specs/16 section 6.2), through the fight-start
    // and death hooks. They swing between modes on purpose (specs/22 section 3.4): lone_wolf is for a unit alone on its side,
    // arise and last_rite need a team. Every rule runs only for a unit that carries it and hashes its state only for it (plus the
    // units it created or fed: a risen ally, last_rite's heirs). None of them draws.
    public static partial class BattleSim
    {
        static readonly string LoneEvent = FightAction.RulePrefix + RuleModifiers.LoneWolf;
        static readonly string AriseEvent = FightAction.RulePrefix + RuleModifiers.Arise;
        static readonly string RiteEvent = FightAction.RulePrefix + RuleModifiers.LastRite;

        /// <summary>The suffix of a risen ally's id: the fallen enemy's id plus this (plus a number if that is taken).</summary>
        public const string RisenSuffix = "#risen";

        /// <summary>
        /// lone_wolf: decided once, at the start of the fight. A carrier alone on its side gets every trained stat (max HP too)
        /// MON_LONE_BONUS higher for the fight; one with allies gets them MON_LONE_MALUS lower. Both are permanent effects of
        /// source rule:lone_wolf (so the floor of STAT_FLOOR_FRAC holds), set before the BattleStart passives; logged with the change.
        /// </summary>
        static partial void OnFightStart_E(RuleCtx c)
        {
            var units = c.State.Units;
            bool any = false;
            foreach (var u in units) any |= Has(u, RuleModifiers.LoneWolf);
            if (!any) return;
            var fracs = new double[units.Count];
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!Has(u, RuleModifiers.LoneWolf)) continue;
                fracs[i] = CountOnSide(c.State, u.Side) == 1 ? c.Rules.LoneBonus : -c.Rules.LoneMalus;
                if (fracs[i] == 0) continue;
                foreach (var stat in StatIds.Trained) AddEffect(c.Rules, u, stat, fracs[i], -1, LoneEvent);
            }
            // every change first, then the log lines, so each line's snapshot (the web's HP bars) already shows them all
            for (int i = 0; i < units.Count; i++)
                if (fracs[i] != 0) c.Log?.Invoke(new FightAction(units[i].Id, LoneEvent, units[i].Id, fracs[i]));
        }

        /// <summary>A dead unit: last_rite if it carried it, then arise if a carrier killed it.</summary>
        static partial void OnDeath_E(RuleCtx c, FightUnit u)
        {
            if (Has(u, RuleModifiers.LastRite)) LastRite(c, u);
            if (u.KilledBy != null) Arise(c, u);
        }

        /// <summary>
        /// last_rite: each living ally of the fallen carrier gains MON_RITE_SHARE of the carrier's base stats (its trained stats;
        /// for HP, the max HP they give), split evenly, for the rest of the fight: a flat amount on top of its stats, and the HP
        /// as max HP and current HP alike. Logged with each ally's share; nothing happens (and nothing is logged) with no ally left.
        /// </summary>
        static void LastRite(RuleCtx c, FightUnit u)
        {
            var r = c.Rules;
            if (!(r.RiteShare > 0)) return;
            var heirs = new List<FightUnit>(4);
            foreach (var a in c.State.Units) if (a != u && a.Side == u.Side && a.Alive) heirs.Add(a);
            if (heirs.Count == 0) return;
            double share = r.RiteShare / heirs.Count;
            foreach (var a in heirs)
            {
                a.RiteFlat ??= new double[StatIds.Trained.Length];
                for (int k = 0; k < StatIds.Trained.Length; k++)
                {
                    string stat = StatIds.Trained[k];
                    if (stat == StatIds.Hp)
                    {
                        double gain = share * MaxHp(r, u.Source.Stats.Hp);
                        a.RiteFlat[k] += gain;
                        // flat max HP: it goes under the timed HP effects' multiplier, so a later RecalcMaxHp keeps it
                        a.BaseMaxHp += gain / Math.Max(r.StatFloor, 1 + Bonus(a, StatIds.Hp));
                        a.MaxHp += gain;
                        a.Hp += gain;
                    }
                    else a.RiteFlat[k] += share * u.Source.Stats.Get(stat);
                }
            }
            c.Log?.Invoke(new FightAction(u.Id, RiteEvent, null, share));
        }

        /// <summary>last_rite: the heir's flat stat gains.</summary>
        static partial void StatFlat_E(FightRules r, FightUnit u, string stat, ref double flat)
        {
            if (u.RiteFlat != null) flat += u.RiteFlat[Array.IndexOf(StatIds.Trained, stat)];
        }

        /// <summary>
        /// arise: once per fight, the first enemy the living carrier kills rises on the carrier's side at MON_ARISE_STATS of its
        /// trained stats, with its skills and triggers but no rule modifiers, boss phases or gambits (a mid-fight unit:
        /// BattleSim.Summon; an add, so it never keeps the side alive). Not when the fallen was the last unit keeping its side
        /// alive (the fight is over; the rise is not spent), and never a risen unit (nor does that spend it).
        /// </summary>
        static void Arise(RuleCtx c, FightUnit victim)
        {
            var r = c.Rules;
            if (victim.Risen || !(r.AriseStats > 0)) return;
            FightUnit killer = null;
            foreach (var k in c.State.Units) if (k.Id == victim.KilledBy) { killer = k; break; }
            if (killer == null || !killer.Alive || killer.AriseUsed || killer.Side == victim.Side || !Has(killer, RuleModifiers.Arise)) return;
            if (!SideAlive(c.State, victim.Side)) return;
            killer.AriseUsed = true;
            var src = victim.Source;
            var st = src.Stats.Clone();
            st.Hp *= r.AriseStats; st.Str *= r.AriseStats; st.Int *= r.AriseStats; st.Wis *= r.AriseStats;
            st.Dex *= r.AriseStats; st.Def *= r.AriseStats; st.Spd *= r.AriseStats; st.Luk *= r.AriseStats;
            string id = victim.Id + RisenSuffix;
            for (int n = 2; HasId(c.State, id); n++) id = victim.Id + RisenSuffix + n;
            var f = new Fighter
            {
                Id = id, Element = src.Element, Stats = st,
                Skills = new List<CombatSkill>(src.Skills), Triggers = new List<TriggeredSkill>(src.Triggers),
            };
            var risen = Summon(r, c.State, f, killer.Side, c.Rng, c.Log, new FightAction(killer.Id, AriseEvent, id, r.AriseStats));
            risen.Risen = true;
        }

        static partial void HashUnit_E(CanonicalWriter w, FightState s, FightUnit u)
        {
            if (Has(u, RuleModifiers.Arise)) w.Bool(u.AriseUsed);
            if (u.Risen) w.Bool(true);
            if (u.RiteFlat != null) foreach (var v in u.RiteFlat) w.F64(v);
        }

        static partial void ValidateRules_E(FightRules r, ref bool ok)
        {
            if (!(r.LoneBonus >= 0 && !double.IsInfinity(r.LoneBonus) && In01(r.LoneMalus) && In01(r.AriseStats)
                  && r.RiteShare >= 0 && !double.IsInfinity(r.RiteShare)))
                ok = false;
        }
    }

    public sealed partial class FightRules
    {
        /// <summary>lone_wolf: the rise of every stat of a carrier alone on its side (a fraction of base).</summary>
        public double LoneBonus { get; set; }

        /// <summary>lone_wolf: the fall of every stat of a carrier with allies (a fraction of base).</summary>
        public double LoneMalus { get; set; }

        /// <summary>arise: the risen ally's trained stats, a fraction of the fallen enemy's.</summary>
        public double AriseStats { get; set; }

        /// <summary>last_rite: the share of the fallen carrier's base stats its living allies split.</summary>
        public double RiteShare { get; set; }

        static partial void Load_E(FightRules r, ContentDb c)
        {
            double D(string k) => c.ConstantValues.TryGetValue(k, out var v) ? v : 0;
            r.LoneBonus = D("MON_LONE_BONUS");
            r.LoneMalus = D("MON_LONE_MALUS");
            r.AriseStats = D("MON_ARISE_STATS");
            r.RiteShare = D("MON_RITE_SHARE");
        }
    }

    public sealed partial class FightUnit
    {
        /// <summary>arise: the carrier's one rise this fight is spent.</summary>
        public bool AriseUsed { get; set; }

        /// <summary>arise: this unit is a fallen enemy risen on the carrier's side (an add with no rules).</summary>
        public bool Risen { get; set; }

        /// <summary>last_rite: the flat amounts gained from fallen allies, per trained stat (StatIds.Trained order; HP as max HP); null for none.</summary>
        public double[] RiteFlat { get; set; }

        partial void Clone_E(FightUnit copy)
        {
            if (RiteFlat != null) copy.RiteFlat = (double[])RiteFlat.Clone();
        }
    }
}
