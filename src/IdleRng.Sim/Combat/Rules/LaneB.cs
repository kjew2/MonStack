using System;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Combat
{
    // Idea-pool lane B: hits taken and survival (#135): resentment, vantage, deaths_door, barricade and mana_ward (specs/16
    // section 6.2). Every rule runs only for a unit that carries it, draws only for it and hashes its state only for it. The
    // lane's state is value fields of FightUnit, so Clone needs nothing more.
    public static partial class BattleSim
    {
        static readonly string ResentEvent = FightAction.RulePrefix + RuleModifiers.Resentment;
        static readonly string VantageEvent = FightAction.RulePrefix + RuleModifiers.Vantage;
        static readonly string DoorEvent = FightAction.RulePrefix + RuleModifiers.DeathsDoor;
        static readonly string BarricadeEvent = FightAction.RulePrefix + RuleModifiers.Barricade;
        static readonly string WardEvent = FightAction.RulePrefix + RuleModifiers.ManaWard;

        /// <summary>mana_ward: the ward of MON_WARD_WIS x WIS, at the start of the fight (before the BattleStart passives).</summary>
        static partial void OnFightStart_B(RuleCtx c)
        {
            if (!(c.Rules.WardWis > 0)) return;
            // every ward first, then the log lines, so each line's snapshot (the web's HP bars) already shows all the wards
            foreach (var u in c.State.Units)
                if (u.Alive && Has(u, RuleModifiers.ManaWard)) u.Ward = c.Rules.WardWis * Eff(c.Rules, u, StatIds.Wis);
            foreach (var u in c.State.Units)
                if (u.Ward > 0 && Has(u, RuleModifiers.ManaWard)) c.Log?.Invoke(new FightAction(u.Id, WardEvent, u.Id, u.Ward));
        }

        /// <summary>mana_ward: while the ward holds, each own action ticks the cooldowns MON_WARD_CDR more (whole ticks, the rest carried).</summary>
        static partial void BeforeAction_B(RuleCtx c, FightUnit u, ref bool skip)
        {
            if (skip || !(u.Ward > 0) || !(c.Rules.WardCdr > 0) || !Has(u, RuleModifiers.ManaWard)) return;
            u.WardCdr += c.Rules.WardCdr;
            while (u.WardCdr >= 1)
            {
                u.WardCdr -= 1;
                for (int i = 0; i < u.Cooldowns.Length; i++) if (u.Cooldowns[i] > 0) u.Cooldowns[i]--;
            }
        }

        /// <summary>resentment: each own action removes MON_RESENT_DECAY of the red pool.</summary>
        static partial void AfterAction_B(RuleCtx c, FightUnit u)
        {
            if (u.ResentPool > 0 && Has(u, RuleModifiers.Resentment)) u.ResentPool *= 1 - c.Rules.ResentDecay;
        }

        /// <summary>resentment: the red pool adds to STR as a flat bonus.</summary>
        static partial void StatFlat_B(FightRules r, FightUnit u, string stat, ref double flat)
        {
            if (u.ResentPool > 0 && stat == StatIds.Str) flat += u.ResentPool;
        }

        /// <summary>
        /// vantage: below MON_VANTAGE_HP of max HP, an enemy's single-target damaging action (not a triggered effect, not a counter)
        /// aimed at the unit meets a basic attack first, once per enemy action; if that kills the attacker (or the unit), the hit
        /// is cancelled. The strike is a counter (GuardKind.Counter): it is never countered, by vantage or counter_mitigated.
        /// Iaido Mantis (#159): while it waits under still_hunt the window is open at any HP (and area actions are answered before
        /// they start, BeforeAoe); sever_art may cut the action off without a kill.
        /// </summary>
        static partial void BeforeHitResolves_B(RuleCtx c, HitInfo h)
        {
            var t = h.Tgt;
            var src = h.Src;
            var r = c.Rules;
            if (h.Aoe || h.Triggered || !t.Alive || !src.Alive || src.Side == t.Side) return;
            if (!VantageOpen(r, t) || !Has(t, RuleModifiers.Vantage) || InGuard(c.State, GuardKind.Counter)) return;
            if (VantageCounter(c, t, src)) h.Cancelled = true;
        }

        /// <summary>vantage's window: below MON_VANTAGE_HP of max HP, or at any HP while it waits under still_hunt (Iaido Mantis).</summary>
        static bool VantageOpen(FightRules r, FightUnit t) => t.Hunting || r.VantageHp > 0 && t.Hp < r.VantageHp * t.MaxHp;

        /// <summary>
        /// vantage's strike: once per enemy action (an echo is the same action), the carrier <paramref name="t"/> basic-attacks
        /// <paramref name="src"/> as a counter. True when the enemy's action is cancelled: the strike killed the attacker (or the
        /// unit died), or a species rule cut the action off (sever_art). False also when it did not strike.
        /// </summary>
        static bool VantageCounter(RuleCtx c, FightUnit t, FightUnit src)
        {
            var r = c.Rules;
            if (t.VantageFrom == src.Id && t.VantageAction == src.Tally.Actions) return false;   // once per enemy action
            t.VantageFrom = src.Id;
            t.VantageAction = src.Tally.Actions;
            if (!EnterGuard(r, c.State, GuardKind.Counter, true, out var saved)) return false;
            double dealt;
            try
            {
                t.Tally.Counters++;
                c.Log?.Invoke(new FightAction(t.Id, VantageEvent, src.Id, 0));
                double str = Eff(r, t, StatIds.Str), intel = Eff(r, t, StatIds.Int);
                dealt = Hit(r, c.State, t, src, BasicAttackRaw(r, str, intel), BasicAttackMagical(str, intel), t.Source.Element, null, false, c.Rng, c.Log);
                GainEntropy(r, c.State, t, r.Tower?.EntropyPerCounter ?? 0, c.Rng, c.Log);
            }
            finally { ExitGuard(c.State, saved); }
            if (!src.Alive || !t.Alive) return true;
            bool cut = false;
            HookVantageCounter(c, t, src, dealt, ref cut);
            return cut;
        }

        /// <summary>
        /// Any damage after the shield: mana_ward's ward takes MON_WARD_SHARE of it first, then resentment banks MON_RESENT_FRAC of
        /// what is left (up to the HP the unit has) when an enemy dealt it.
        /// </summary>
        static partial void ModifyHpDamage_B(RuleCtx c, FightUnit src, FightUnit tgt, ref double amount)
        {
            if (!(amount > 0)) return;
            if (tgt.Ward > 0 && Has(tgt, RuleModifiers.ManaWard))
            {
                double take = Math.Min(tgt.Ward, c.Rules.WardShare * amount);
                if (take > 0)
                {
                    tgt.Ward -= take;
                    amount -= take;
                    c.Log?.Invoke(new FightAction(tgt.Id, WardEvent, tgt.Id, tgt.Ward));
                }
            }
            if (src.Side != tgt.Side && c.Rules.ResentFrac > 0 && Has(tgt, RuleModifiers.Resentment))
            {
                double gain = c.Rules.ResentFrac * Math.Min(amount, Math.Max(0.0, tgt.Hp));
                if (gain > 0)
                {
                    tgt.ResentPool += gain;
                    c.Log?.Invoke(new FightAction(tgt.Id, ResentEvent, tgt.Id, tgt.ResentPool));
                }
            }
        }

        /// <summary>
        /// deaths_door: the first blow that would kill leaves the unit at 1 HP, on Death's Door (logged with odds 1). Each later
        /// blow that would kill is survived at 1 HP with chance MON_DOOR_START - MON_DOOR_STEP x blows already survived (one draw;
        /// none once the chance is 0), logged with those odds either way.
        /// </summary>
        static partial void OnWouldDie_B(RuleCtx c, FightUnit src, FightUnit tgt, ref bool saved)
        {
            if (saved || !Has(tgt, RuleModifiers.DeathsDoor)) return;
            if (!tgt.OnDoor)
            {
                tgt.OnDoor = true;
                tgt.Hp = Math.Min(1.0, tgt.MaxHp);
                saved = true;
                c.Log?.Invoke(new FightAction(tgt.Id, DoorEvent, tgt.Id, 1));
                return;
            }
            double odds = Math.Max(0.0, c.Rules.DoorStart - c.Rules.DoorStep * tgt.DoorHeld);
            bool held = odds > 0 && Draw(c.Rng) < odds;
            c.Log?.Invoke(new FightAction(tgt.Id, DoorEvent, tgt.Id, odds));
            if (!held) return;
            tgt.DoorHeld++;
            tgt.Hp = Math.Min(1.0, tgt.MaxHp);
            saved = true;
        }

        /// <summary>barricade: the unit's shields never expire and stack, up to MON_BARRICADE_CAP of max HP (a bigger single shield still counts whole); logged with the shield it now has.</summary>
        static partial void ShieldGain_B(RuleCtx c, FightUnit tgt, double amount, ref double shield, ref int ticks)
        {
            if (!Has(tgt, RuleModifiers.Barricade)) return;
            ticks = 0;
            shield = Math.Max(shield, Math.Min(tgt.Shield + amount, c.Rules.BarricadeCap * tgt.MaxHp));
            c.Log?.Invoke(new FightAction(tgt.Id, BarricadeEvent, tgt.Id, shield));
        }

        /// <summary>barricade: overhealing turns into shield up to the larger of MON_BARRICADE_CAP and the unit's other caps.</summary>
        static partial void ShieldCap_B(FightRules r, FightUnit t, ref double capFrac)
        {
            if (Has(t, RuleModifiers.Barricade)) capFrac = Math.Max(capFrac, r.BarricadeCap);
        }

        static partial void HashUnit_B(CanonicalWriter w, FightState s, FightUnit u)
        {
            if (Has(u, RuleModifiers.Resentment)) w.F64(u.ResentPool);
            if (Has(u, RuleModifiers.DeathsDoor)) { w.Bool(u.OnDoor); w.I32(u.DoorHeld); }
            if (Has(u, RuleModifiers.ManaWard)) { w.F64(u.Ward); w.F64(u.WardCdr); }
        }

        static partial void ValidateRules_B(FightRules r, ref bool ok)
        {
            if (!(r.ResentFrac >= 0 && !double.IsInfinity(r.ResentFrac) && In01(r.ResentDecay) && In01(r.VantageHp) && In01(r.DoorStart)
                  && In01(r.DoorStep) && In01(r.BarricadeCap) && r.WardWis >= 0 && !double.IsInfinity(r.WardWis) && In01(r.WardShare)
                  && r.WardCdr >= 0 && !double.IsInfinity(r.WardCdr)))
                ok = false;
        }
    }

    public sealed partial class FightRules
    {
        /// <summary>resentment: the share of the HP damage an enemy deals the unit that goes into its red pool (flat STR).</summary>
        public double ResentFrac { get; set; }

        /// <summary>resentment: the share of the red pool each of the unit's own actions removes.</summary>
        public double ResentDecay { get; set; }

        /// <summary>vantage: the unit strikes first while below this fraction of its max HP.</summary>
        public double VantageHp { get; set; }

        /// <summary>deaths_door: the chance to survive the first killing blow after reaching Death's Door.</summary>
        public double DoorStart { get; set; }

        /// <summary>deaths_door: the survival chance lost per killing blow already survived on Death's Door.</summary>
        public double DoorStep { get; set; }

        /// <summary>barricade: the unit's shields stack up to this fraction of its max HP (also its overheal-to-shield cap).</summary>
        public double BarricadeCap { get; set; }

        /// <summary>mana_ward: the ward at fight start, a multiple of the unit's WIS.</summary>
        public double WardWis { get; set; }

        /// <summary>mana_ward: the share of each blow's HP damage the ward takes first.</summary>
        public double WardShare { get; set; }

        /// <summary>mana_ward: extra cooldown ticks per own action while the ward holds.</summary>
        public double WardCdr { get; set; }

        static partial void Load_B(FightRules r, ContentDb c)
        {
            double D(string k) => c.ConstantValues.TryGetValue(k, out var v) ? v : 0;
            r.ResentFrac = D("MON_RESENT_FRAC");
            r.ResentDecay = D("MON_RESENT_DECAY");
            r.VantageHp = D("MON_VANTAGE_HP");
            r.DoorStart = D("MON_DOOR_START");
            r.DoorStep = D("MON_DOOR_STEP");
            r.BarricadeCap = D("MON_BARRICADE_CAP");
            r.WardWis = D("MON_WARD_WIS");
            r.WardShare = D("MON_WARD_SHARE");
            r.WardCdr = D("MON_WARD_CDR");
        }
    }

    public sealed partial class FightUnit
    {
        /// <summary>resentment: the red pool, a flat STR bonus that each own action wears down.</summary>
        public double ResentPool { get; set; }

        /// <summary>vantage: the enemy, and its action count, of the last action the unit struck first against. Not hashed: it never matches a later action.</summary>
        internal string VantageFrom { get; set; }
        internal int VantageAction { get; set; }

        /// <summary>deaths_door: the unit is on Death's Door (its free save is spent).</summary>
        public bool OnDoor { get; set; }

        /// <summary>deaths_door: killing blows survived on Death's Door by the roll.</summary>
        public int DoorHeld { get; set; }

        /// <summary>mana_ward: the ward left, a pool apart from the shield.</summary>
        public double Ward { get; set; }

        /// <summary>mana_ward: the fraction of an extra cooldown tick carried to the next action.</summary>
        public double WardCdr { get; set; }
    }
}
