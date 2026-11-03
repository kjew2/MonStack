using IdleRng.Sim.Content;

namespace IdleRng.Sim.Combat
{
    // The rule hooks of the idea-pool lanes (specs/16 section 6.4, epic #132). The fight calls each Hook* method at a fixed
    // point; it calls the same hook of lanes A to E in that order. Each lane's hooks are partial methods that the lane
    // implements in its own file (Rules/LaneA.cs to LaneE.cs); an unimplemented one is compiled away, so a hook is a no-op
    // until its lane fills it. Species chase rules (specs/14 section 3.1) fill partials named after the species
    // (Rules/<Species>.cs), called after lane E; the Healed, HpPaid and HitMitigated hooks exist for them. Rules: every rule runs only behind Has(unit, RuleModifiers.X); a new random draw only for a
    // unit that carries it; new state is hashed only for carriers; every proc is logged as rule:<id> with its key number.
    public static partial class BattleSim
    {
        /// <summary>OnFightStart: the first Step, before the BattleStart triggers (all units are in place).</summary>
        static void HookFightStart(RuleCtx c)
        {
            OnFightStart_A(c);
            OnFightStart_B(c);
            OnFightStart_C(c);
            OnFightStart_D(c);
            OnFightStart_E(c);
            OnFightStart_PyreStag(c);
        }

        static partial void OnFightStart_A(RuleCtx c);
        static partial void OnFightStart_B(RuleCtx c);
        static partial void OnFightStart_C(RuleCtx c);
        static partial void OnFightStart_D(RuleCtx c);
        static partial void OnFightStart_E(RuleCtx c);
        static partial void OnFightStart_PyreStag(RuleCtx c);

        /// <summary>BeforeAction: the start of the unit's action, before it is counted; set skip to lose the action (nothing else of it happens).</summary>
        static void HookBeforeAction(RuleCtx c, FightUnit u, ref bool skip)
        {
            BeforeAction_A(c, u, ref skip);
            BeforeAction_B(c, u, ref skip);
            BeforeAction_C(c, u, ref skip);
            BeforeAction_D(c, u, ref skip);
            BeforeAction_E(c, u, ref skip);
            BeforeAction_PyreStag(c, u, ref skip);
        }

        static partial void BeforeAction_A(RuleCtx c, FightUnit u, ref bool skip);
        static partial void BeforeAction_B(RuleCtx c, FightUnit u, ref bool skip);
        static partial void BeforeAction_C(RuleCtx c, FightUnit u, ref bool skip);
        static partial void BeforeAction_D(RuleCtx c, FightUnit u, ref bool skip);
        static partial void BeforeAction_E(RuleCtx c, FightUnit u, ref bool skip);
        static partial void BeforeAction_PyreStag(RuleCtx c, FightUnit u, ref bool skip);

        /// <summary>AfterAction: after the unit's action (and after each extra action), before deaths, boss phases and the outcome check.</summary>
        static void HookAfterAction(RuleCtx c, FightUnit u)
        {
            AfterAction_A(c, u);
            AfterAction_B(c, u);
            AfterAction_C(c, u);
            AfterAction_D(c, u);
            AfterAction_E(c, u);
            AfterAction_PyreStag(c, u);
        }

        static partial void AfterAction_A(RuleCtx c, FightUnit u);
        static partial void AfterAction_B(RuleCtx c, FightUnit u);
        static partial void AfterAction_C(RuleCtx c, FightUnit u);
        static partial void AfterAction_D(RuleCtx c, FightUnit u);
        static partial void AfterAction_E(RuleCtx c, FightUnit u);
        static partial void AfterAction_PyreStag(RuleCtx c, FightUnit u);

        /// <summary>ExtraAction: after an action: set evt (the log event, rule:&lt;id&gt;) to grant an extra action now; leave it if already set. MON_EXTRA_ACTIONS_CAP applies.</summary>
        static void HookExtraAction(RuleCtx c, FightUnit u, ref string evt)
        {
            ExtraAction_A(c, u, ref evt);
            ExtraAction_B(c, u, ref evt);
            ExtraAction_C(c, u, ref evt);
            ExtraAction_D(c, u, ref evt);
            ExtraAction_E(c, u, ref evt);
        }

        static partial void ExtraAction_A(RuleCtx c, FightUnit u, ref string evt);
        static partial void ExtraAction_B(RuleCtx c, FightUnit u, ref string evt);
        static partial void ExtraAction_C(RuleCtx c, FightUnit u, ref string evt);
        static partial void ExtraAction_D(RuleCtx c, FightUnit u, ref string evt);
        static partial void ExtraAction_E(RuleCtx c, FightUnit u, ref string evt);

        /// <summary>OnGaugeTick: each tick, the living unit's gauge fill before it is added.</summary>
        static void HookGaugeTick(RuleCtx c, FightUnit u, ref double fill)
        {
            OnGaugeTick_A(c, u, ref fill);
            OnGaugeTick_B(c, u, ref fill);
            OnGaugeTick_C(c, u, ref fill);
            OnGaugeTick_D(c, u, ref fill);
            OnGaugeTick_E(c, u, ref fill);
        }

        static partial void OnGaugeTick_A(RuleCtx c, FightUnit u, ref double fill);
        static partial void OnGaugeTick_B(RuleCtx c, FightUnit u, ref double fill);
        static partial void OnGaugeTick_C(RuleCtx c, FightUnit u, ref double fill);
        static partial void OnGaugeTick_D(RuleCtx c, FightUnit u, ref double fill);
        static partial void OnGaugeTick_E(RuleCtx c, FightUnit u, ref double fill);

        /// <summary>BeforeHitResolves: the start of a hit, before the decoy and every roll; set h.Cancelled to stop it.</summary>
        static void HookBeforeHit(RuleCtx c, HitInfo h)
        {
            BeforeHitResolves_A(c, h);
            BeforeHitResolves_B(c, h);
            BeforeHitResolves_C(c, h);
            BeforeHitResolves_D(c, h);
            BeforeHitResolves_E(c, h);
        }

        static partial void BeforeHitResolves_A(RuleCtx c, HitInfo h);
        static partial void BeforeHitResolves_B(RuleCtx c, HitInfo h);
        static partial void BeforeHitResolves_C(RuleCtx c, HitInfo h);
        static partial void BeforeHitResolves_D(RuleCtx c, HitInfo h);
        static partial void BeforeHitResolves_E(RuleCtx c, HitInfo h);

        /// <summary>ConvertHit: the conversion step: once per hit, before the avoid roll and every multiplier (stat and element conversions).</summary>
        static void HookConvert(RuleCtx c, HitInfo h)
        {
            ConvertHit_A(c, h);
            ConvertHit_B(c, h);
            ConvertHit_C(c, h);
            ConvertHit_D(c, h);
            ConvertHit_E(c, h);
            ConvertHit_KeepshellCrab(c, h);
        }

        static partial void ConvertHit_A(RuleCtx c, HitInfo h);
        static partial void ConvertHit_B(RuleCtx c, HitInfo h);
        static partial void ConvertHit_C(RuleCtx c, HitInfo h);
        static partial void ConvertHit_D(RuleCtx c, HitInfo h);
        static partial void ConvertHit_E(RuleCtx c, HitInfo h);
        static partial void ConvertHit_KeepshellCrab(RuleCtx c, HitInfo h);

        /// <summary>
        /// HitMitigated (species rules): a landed hit's damage after mitigation, crit and the tower's multipliers, before the shield
        /// takes its part; only hits come here (not counters, true or max-HP damage).
        /// </summary>
        static void HookHitMitigated(RuleCtx c, HitInfo h, ref double damage)
        {
            OnHitMitigated_KeepshellCrab(c, h, ref damage);
        }

        static partial void OnHitMitigated_KeepshellCrab(RuleCtx c, HitInfo h, ref double damage);

        /// <summary>ModifyOutgoing: the attacker's damage multipliers on h.Raw, after crit, element and dmg_dealt, before mitigation.</summary>
        static void HookOutgoing(RuleCtx c, HitInfo h)
        {
            ModifyOutgoing_A(c, h);
            ModifyOutgoing_B(c, h);
            ModifyOutgoing_C(c, h);
            ModifyOutgoing_D(c, h);
            ModifyOutgoing_E(c, h);
            ModifyOutgoing_PyreStag(c, h);
        }

        static partial void ModifyOutgoing_A(RuleCtx c, HitInfo h);
        static partial void ModifyOutgoing_B(RuleCtx c, HitInfo h);
        static partial void ModifyOutgoing_C(RuleCtx c, HitInfo h);
        static partial void ModifyOutgoing_D(RuleCtx c, HitInfo h);
        static partial void ModifyOutgoing_E(RuleCtx c, HitInfo h);
        static partial void ModifyOutgoing_PyreStag(RuleCtx c, HitInfo h);

        /// <summary>ModifyIncoming: the target's damage multipliers on h.Raw, right after ModifyOutgoing.</summary>
        static void HookIncoming(RuleCtx c, HitInfo h)
        {
            ModifyIncoming_A(c, h);
            ModifyIncoming_B(c, h);
            ModifyIncoming_C(c, h);
            ModifyIncoming_D(c, h);
            ModifyIncoming_E(c, h);
        }

        static partial void ModifyIncoming_A(RuleCtx c, HitInfo h);
        static partial void ModifyIncoming_B(RuleCtx c, HitInfo h);
        static partial void ModifyIncoming_C(RuleCtx c, HitInfo h);
        static partial void ModifyIncoming_D(RuleCtx c, HitInfo h);
        static partial void ModifyIncoming_E(RuleCtx c, HitInfo h);

        /// <summary>OnHitLanded: a hit that was not evaded has landed (h.Dealt set), for the attacker's rules; before OnHitTaken triggers.</summary>
        static void HookHitLanded(RuleCtx c, HitInfo h)
        {
            OnHitLanded_A(c, h);
            OnHitLanded_B(c, h);
            OnHitLanded_C(c, h);
            OnHitLanded_D(c, h);
            OnHitLanded_E(c, h);
            OnHitLanded_Candlemane(c, h);
        }

        static partial void OnHitLanded_A(RuleCtx c, HitInfo h);
        static partial void OnHitLanded_B(RuleCtx c, HitInfo h);
        static partial void OnHitLanded_C(RuleCtx c, HitInfo h);
        static partial void OnHitLanded_D(RuleCtx c, HitInfo h);
        static partial void OnHitLanded_E(RuleCtx c, HitInfo h);
        static partial void OnHitLanded_Candlemane(RuleCtx c, HitInfo h);

        /// <summary>OnHitTaken: the same point, for the target's rules.</summary>
        static void HookHitTaken(RuleCtx c, HitInfo h)
        {
            OnHitTaken_A(c, h);
            OnHitTaken_B(c, h);
            OnHitTaken_C(c, h);
            OnHitTaken_D(c, h);
            OnHitTaken_E(c, h);
            OnHitTaken_Candlemane(c, h);
        }

        static partial void OnHitTaken_A(RuleCtx c, HitInfo h);
        static partial void OnHitTaken_B(RuleCtx c, HitInfo h);
        static partial void OnHitTaken_C(RuleCtx c, HitInfo h);
        static partial void OnHitTaken_D(RuleCtx c, HitInfo h);
        static partial void OnHitTaken_E(RuleCtx c, HitInfo h);
        static partial void OnHitTaken_Candlemane(RuleCtx c, HitInfo h);

        /// <summary>
        /// Healed (species rules): a heal reached t (skills, triggers, rules; not a boss phase's failed check); healed is the HP it
        /// restored, lost the overheal that went nowhere (not into HP, not into a shield). Once per healed unit, after its HP changed.
        /// </summary>
        static void HookHealed(RuleCtx c, FightUnit t, double healed, double lost)
        {
            OnHealed_Candlemane(c, t, healed, lost);
        }

        static partial void OnHealed_Candlemane(RuleCtx c, FightUnit t, double healed, double lost);

        /// <summary>HpPaid (species rules): the unit paid HP as a cost (lane A's blood_magic, the Pyre Stag's pyre_tithe and Immolate); not damage, after its HP fell.</summary>
        static void HookHpPaid(RuleCtx c, FightUnit u, double paid)
        {
            OnHpPaid_Candlemane(c, u, paid);
        }

        static partial void OnHpPaid_Candlemane(RuleCtx c, FightUnit u, double paid);

        /// <summary>ModifyHpDamage: any damage (hits, counters, wail, channel strikes) after the shield took its part, before HP.</summary>
        static void HookHpDamage(RuleCtx c, FightUnit src, FightUnit tgt, ref double amount)
        {
            ModifyHpDamage_A(c, src, tgt, ref amount);
            ModifyHpDamage_B(c, src, tgt, ref amount);
            ModifyHpDamage_C(c, src, tgt, ref amount);
            ModifyHpDamage_D(c, src, tgt, ref amount);
            ModifyHpDamage_E(c, src, tgt, ref amount);
            ModifyHpDamage_PyreStag(c, src, tgt, ref amount);
        }

        static partial void ModifyHpDamage_A(RuleCtx c, FightUnit src, FightUnit tgt, ref double amount);
        static partial void ModifyHpDamage_B(RuleCtx c, FightUnit src, FightUnit tgt, ref double amount);
        static partial void ModifyHpDamage_C(RuleCtx c, FightUnit src, FightUnit tgt, ref double amount);
        static partial void ModifyHpDamage_D(RuleCtx c, FightUnit src, FightUnit tgt, ref double amount);
        static partial void ModifyHpDamage_E(RuleCtx c, FightUnit src, FightUnit tgt, ref double amount);
        static partial void ModifyHpDamage_PyreStag(RuleCtx c, FightUnit src, FightUnit tgt, ref double amount);

        /// <summary>OnWouldDie: a blow brought tgt to 0 HP and root_rebirth did not save it (or its own entropy recoil did: src is tgt then); set saved (and its HP) to keep it alive; leave it if already set.</summary>
        static void HookWouldDie(RuleCtx c, FightUnit src, FightUnit tgt, ref bool saved)
        {
            OnWouldDie_A(c, src, tgt, ref saved);
            OnWouldDie_B(c, src, tgt, ref saved);
            OnWouldDie_C(c, src, tgt, ref saved);
            OnWouldDie_D(c, src, tgt, ref saved);
            OnWouldDie_E(c, src, tgt, ref saved);
            OnWouldDie_PyreStag(c, src, tgt, ref saved);
        }

        static partial void OnWouldDie_A(RuleCtx c, FightUnit src, FightUnit tgt, ref bool saved);
        static partial void OnWouldDie_B(RuleCtx c, FightUnit src, FightUnit tgt, ref bool saved);
        static partial void OnWouldDie_C(RuleCtx c, FightUnit src, FightUnit tgt, ref bool saved);
        static partial void OnWouldDie_D(RuleCtx c, FightUnit src, FightUnit tgt, ref bool saved);
        static partial void OnWouldDie_E(RuleCtx c, FightUnit src, FightUnit tgt, ref bool saved);
        static partial void OnWouldDie_PyreStag(RuleCtx c, FightUnit src, FightUnit tgt, ref bool saved);

        /// <summary>SkillReady: whether the unit's active skill at index may be used now (the AI and gambits ask; ready starts as cooldown over).</summary>
        static void HookSkillReady(RuleCtx c, FightUnit u, int index, ref bool ready)
        {
            SkillReady_A(c, u, index, ref ready);
            SkillReady_B(c, u, index, ref ready);
            SkillReady_C(c, u, index, ref ready);
            SkillReady_D(c, u, index, ref ready);
            SkillReady_E(c, u, index, ref ready);
        }

        static partial void SkillReady_A(RuleCtx c, FightUnit u, int index, ref bool ready);
        static partial void SkillReady_B(RuleCtx c, FightUnit u, int index, ref bool ready);
        static partial void SkillReady_C(RuleCtx c, FightUnit u, int index, ref bool ready);
        static partial void SkillReady_D(RuleCtx c, FightUnit u, int index, ref bool ready);
        static partial void SkillReady_E(RuleCtx c, FightUnit u, int index, ref bool ready);

        /// <summary>OnSkillUsed: after an active skill was cast as an action (cooldown set, echo done).</summary>
        static void HookSkillUsed(RuleCtx c, FightUnit u, int index, CombatSkill sk)
        {
            OnSkillUsed_A(c, u, index, sk);
            OnSkillUsed_B(c, u, index, sk);
            OnSkillUsed_C(c, u, index, sk);
            OnSkillUsed_D(c, u, index, sk);
            OnSkillUsed_E(c, u, index, sk);
            OnSkillUsed_PyreStag(c, u, index, sk);
        }

        static partial void OnSkillUsed_A(RuleCtx c, FightUnit u, int index, CombatSkill sk);
        static partial void OnSkillUsed_B(RuleCtx c, FightUnit u, int index, CombatSkill sk);
        static partial void OnSkillUsed_C(RuleCtx c, FightUnit u, int index, CombatSkill sk);
        static partial void OnSkillUsed_D(RuleCtx c, FightUnit u, int index, CombatSkill sk);
        static partial void OnSkillUsed_E(RuleCtx c, FightUnit u, int index, CombatSkill sk);
        static partial void OnSkillUsed_PyreStag(RuleCtx c, FightUnit u, int index, CombatSkill sk);

        /// <summary>OnDeath: once per unit that died, after the action or end-of-tick ageing that killed it (u.KilledBy names the killer).</summary>
        static void HookDeath(RuleCtx c, FightUnit u)
        {
            OnDeath_A(c, u);
            OnDeath_B(c, u);
            OnDeath_C(c, u);
            OnDeath_D(c, u);
            OnDeath_E(c, u);
        }

        static partial void OnDeath_A(RuleCtx c, FightUnit u);
        static partial void OnDeath_B(RuleCtx c, FightUnit u);
        static partial void OnDeath_C(RuleCtx c, FightUnit u);
        static partial void OnDeath_D(RuleCtx c, FightUnit u);
        static partial void OnDeath_E(RuleCtx c, FightUnit u);

        /// <summary>HashUnit: StateHash, per unit after the core rules' state: write the lane's state for carriers only.</summary>
        static void HookHashUnit(CanonicalWriter w, FightState s, FightUnit u)
        {
            HashUnit_A(w, s, u);
            HashUnit_B(w, s, u);
            HashUnit_C(w, s, u);
            HashUnit_D(w, s, u);
            HashUnit_E(w, s, u);
            HashUnit_Candlemane(w, s, u);
            HashUnit_PyreStag(w, s, u);
            HashUnit_KeepshellCrab(w, s, u);
        }

        static partial void HashUnit_A(CanonicalWriter w, FightState s, FightUnit u);
        static partial void HashUnit_B(CanonicalWriter w, FightState s, FightUnit u);
        static partial void HashUnit_C(CanonicalWriter w, FightState s, FightUnit u);
        static partial void HashUnit_D(CanonicalWriter w, FightState s, FightUnit u);
        static partial void HashUnit_E(CanonicalWriter w, FightState s, FightUnit u);
        static partial void HashUnit_Candlemane(CanonicalWriter w, FightState s, FightUnit u);
        static partial void HashUnit_PyreStag(CanonicalWriter w, FightState s, FightUnit u);
        static partial void HashUnit_KeepshellCrab(CanonicalWriter w, FightState s, FightUnit u);

        /// <summary>HashFight: StateHash, at the end: fight-wide lane state, only when some unit carries the rule.</summary>
        static void HookHashFight(CanonicalWriter w, FightState s)
        {
            HashFight_A(w, s);
            HashFight_B(w, s);
            HashFight_C(w, s);
            HashFight_D(w, s);
            HashFight_E(w, s);
        }

        static partial void HashFight_A(CanonicalWriter w, FightState s);
        static partial void HashFight_B(CanonicalWriter w, FightState s);
        static partial void HashFight_C(CanonicalWriter w, FightState s);
        static partial void HashFight_D(CanonicalWriter w, FightState s);
        static partial void HashFight_E(CanonicalWriter w, FightState s);

        /// <summary>Validate: a hand-built FightRules with a lane's constants out of range is refused (set ok to false).</summary>
        static void HookValidateRules(FightRules r, ref bool ok)
        {
            ValidateRules_A(r, ref ok);
            ValidateRules_B(r, ref ok);
            ValidateRules_C(r, ref ok);
            ValidateRules_D(r, ref ok);
            ValidateRules_E(r, ref ok);
            ValidateRules_FountainAxolotl(r, ref ok);
            ValidateRules_Candlemane(r, ref ok);
            ValidateRules_PyreStag(r, ref ok);
            ValidateRules_KeepshellCrab(r, ref ok);
        }

        static partial void ValidateRules_A(FightRules r, ref bool ok);
        static partial void ValidateRules_B(FightRules r, ref bool ok);
        static partial void ValidateRules_C(FightRules r, ref bool ok);
        static partial void ValidateRules_D(FightRules r, ref bool ok);
        static partial void ValidateRules_E(FightRules r, ref bool ok);
        static partial void ValidateRules_Candlemane(FightRules r, ref bool ok);
        static partial void ValidateRules_PyreStag(FightRules r, ref bool ok);
        static partial void ValidateRules_KeepshellCrab(FightRules r, ref bool ok);

        /// <summary>StatFlat: a flat amount added to a trained stat's current value (after its effect multiplier and floor; not HP). Hot path: keep it cheap.</summary>
        static void HookStatFlat(FightRules r, FightUnit u, string stat, ref double flat)
        {
            StatFlat_A(r, u, stat, ref flat);
            StatFlat_B(r, u, stat, ref flat);
            StatFlat_C(r, u, stat, ref flat);
            StatFlat_D(r, u, stat, ref flat);
            StatFlat_E(r, u, stat, ref flat);
        }

        static partial void StatFlat_A(FightRules r, FightUnit u, string stat, ref double flat);
        static partial void StatFlat_B(FightRules r, FightUnit u, string stat, ref double flat);
        static partial void StatFlat_C(FightRules r, FightUnit u, string stat, ref double flat);
        static partial void StatFlat_D(FightRules r, FightUnit u, string stat, ref double flat);
        static partial void StatFlat_E(FightRules r, FightUnit u, string stat, ref double flat);

        /// <summary>HealIn: a heal of amount (already scaled) is about to land on t; set taken to stop it (t gains no HP and no overflow shield).</summary>
        static void HookHealIn(RuleCtx c, FightUnit t, double amount, ref bool taken)
        {
            HealIn_PyreStag(c, t, amount, ref taken);
        }

        static partial void HealIn_PyreStag(RuleCtx c, FightUnit t, double amount, ref bool taken);

        /// <summary>ShieldGain: a skill or entropy_convert gives tgt a timed shield of amount; shield starts as the bigger of the old and new, ticks as the new timer (0 = no expiry).</summary>
        static void HookShieldGain(RuleCtx c, FightUnit tgt, double amount, ref double shield, ref int ticks)
        {
            ShieldGain_A(c, tgt, amount, ref shield, ref ticks);
            ShieldGain_B(c, tgt, amount, ref shield, ref ticks);
            ShieldGain_C(c, tgt, amount, ref shield, ref ticks);
            ShieldGain_D(c, tgt, amount, ref shield, ref ticks);
            ShieldGain_E(c, tgt, amount, ref shield, ref ticks);
        }

        static partial void ShieldGain_A(RuleCtx c, FightUnit tgt, double amount, ref double shield, ref int ticks);
        static partial void ShieldGain_B(RuleCtx c, FightUnit tgt, double amount, ref double shield, ref int ticks);
        static partial void ShieldGain_C(RuleCtx c, FightUnit tgt, double amount, ref double shield, ref int ticks);
        static partial void ShieldGain_D(RuleCtx c, FightUnit tgt, double amount, ref double shield, ref int ticks);
        static partial void ShieldGain_E(RuleCtx c, FightUnit tgt, double amount, ref double shield, ref int ticks);

        /// <summary>ShieldCap: the cap (fraction of max HP) up to which overhealing becomes shield (tidal_overflow, abyssal_drain); only asked when some cap applies.</summary>
        static void HookShieldCap(FightRules r, FightUnit t, ref double capFrac)
        {
            ShieldCap_A(r, t, ref capFrac);
            ShieldCap_B(r, t, ref capFrac);
            ShieldCap_C(r, t, ref capFrac);
            ShieldCap_D(r, t, ref capFrac);
            ShieldCap_E(r, t, ref capFrac);
        }

        static partial void ShieldCap_A(FightRules r, FightUnit t, ref double capFrac);
        static partial void ShieldCap_B(FightRules r, FightUnit t, ref double capFrac);
        static partial void ShieldCap_C(FightRules r, FightUnit t, ref double capFrac);
        static partial void ShieldCap_D(FightRules r, FightUnit t, ref double capFrac);
        static partial void ShieldCap_E(FightRules r, FightUnit t, ref double capFrac);
    }

    public sealed partial class FightRules
    {
        /// <summary>FromContent: each lane reads its constants (ContentDb.ConstantValues, MON_&lt;RULE&gt;_*) into its own properties.</summary>
        static void LoadLanes(FightRules r, ContentDb c)
        {
            Load_A(r, c);
            Load_B(r, c);
            Load_C(r, c);
            Load_D(r, c);
            Load_E(r, c);
            Load_FountainAxolotl(r, c);
            Load_Candlemane(r, c);
            Load_PyreStag(r, c);
            Load_KeepshellCrab(r, c);
        }

        static partial void Load_A(FightRules r, ContentDb c);
        static partial void Load_B(FightRules r, ContentDb c);
        static partial void Load_C(FightRules r, ContentDb c);
        static partial void Load_D(FightRules r, ContentDb c);
        static partial void Load_E(FightRules r, ContentDb c);
        static partial void Load_Candlemane(FightRules r, ContentDb c);
        static partial void Load_PyreStag(FightRules r, ContentDb c);
        static partial void Load_KeepshellCrab(FightRules r, ContentDb c);
    }

    public sealed partial class FightUnit
    {
        /// <summary>Clone: each lane deep-copies the reference-type state it added (value fields are already copied).</summary>
        void CloneLanes(FightUnit copy)
        {
            Clone_A(copy);
            Clone_B(copy);
            Clone_C(copy);
            Clone_D(copy);
            Clone_E(copy);
        }

        partial void Clone_A(FightUnit copy);
        partial void Clone_B(FightUnit copy);
        partial void Clone_C(FightUnit copy);
        partial void Clone_D(FightUnit copy);
        partial void Clone_E(FightUnit copy);
    }
}
