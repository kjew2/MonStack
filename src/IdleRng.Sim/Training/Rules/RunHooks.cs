using System.Collections.Generic;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;

namespace IdleRng.Sim.Training
{
    // The run-rule hooks of the idea-pool lanes F and G (specs/14 section 2, epic #132). The run calls each Hook* method at a
    // fixed point, lane F then lane G; each lane implements its partial methods in Rules/LaneF.cs or LaneG.cs, and an
    // unimplemented one is compiled away, so a hook is a no-op until its lane fills it. A rule takes effect only for a run that
    // holds its capstone; its state goes into the save as lean JSON and into the run hash only when present (HashRun).
    public static partial class TrainingRun
    {
        /// <summary>BuildKit: BuildFighter, after the bought skill, trigger and rule nodes are in the kit (trees in slot order, absorbed trees first).</summary>
        static void HookKit(ContentDb c, string speciesId, IReadOnlyList<(string TreeId, TreeRoll Roll, int Bought)> trees, Fighter f)
        {
            BuildKit_F(c, speciesId, trees, f);
            BuildKit_G(c, speciesId, trees, f);
        }

        static partial void BuildKit_F(ContentDb c, string speciesId, IReadOnlyList<(string TreeId, TreeRoll Roll, int Bought)> trees, Fighter f);
        static partial void BuildKit_G(ContentDb c, string speciesId, IReadOnlyList<(string TreeId, TreeRoll Roll, int Bought)> trees, Fighter f);

        /// <summary>ModifyGrowth: Growth, the summed species and tree growth for the stat, before the talents' factors.</summary>
        static void HookGrowth(ContentDb c, RunState s, string stat, ref double growth)
        {
            ModifyGrowth_F(c, s, stat, ref growth);
            ModifyGrowth_G(c, s, stat, ref growth);
            LineageGrowth(c, s, stat, ref growth);
        }

        static partial void ModifyGrowth_F(ContentDb c, RunState s, string stat, ref double growth);
        static partial void ModifyGrowth_G(ContentDb c, RunState s, string stat, ref double growth);

        /// <summary>ModifyDrillGain: Drill, the floored gain before the stat cap.</summary>
        static void HookDrillGain(ContentDb c, RunState s, string stat, DrillTier tier, ref double gain)
        {
            ModifyDrillGain_F(c, s, stat, tier, ref gain);
            ModifyDrillGain_G(c, s, stat, tier, ref gain);
            LineageDrillGain(c, s, stat, tier, ref gain);
        }

        static partial void ModifyDrillGain_F(ContentDb c, RunState s, string stat, DrillTier tier, ref double gain);
        static partial void ModifyDrillGain_G(ContentDb c, RunState s, string stat, DrillTier tier, ref double gain);

        /// <summary>ModifyDrillCost: DrillCost, the energy the next drill costs (after Momentum and talents).</summary>
        static void HookDrillCost(ContentDb c, RunState s, ref double cost)
        {
            ModifyDrillCost_F(c, s, ref cost);
            ModifyDrillCost_G(c, s, ref cost);
        }

        static partial void ModifyDrillCost_F(ContentDb c, RunState s, ref double cost);
        static partial void ModifyDrillCost_G(ContentDb c, RunState s, ref double cost);

        /// <summary>ModifySuperChance: RunSuperChance, the super chance at energy fraction e (the result is kept in [0, 1 - fail]).</summary>
        static void HookSuperChance(ContentDb c, RunState s, double e, ref double super)
        {
            ModifySuperChance_F(c, s, e, ref super);
            ModifySuperChance_G(c, s, e, ref super);
        }

        static partial void ModifySuperChance_F(ContentDb c, RunState s, double e, ref double super);
        static partial void ModifySuperChance_G(ContentDb c, RunState s, double e, ref double super);

        /// <summary>ModifyInjuryChance: RunInjuryChance, a drill's injury chance at energy fraction e (0 = no injury roll is drawn).</summary>
        static void HookInjuryChance(ContentDb c, RunState s, double e, ref double chance)
        {
            ModifyInjuryChance_F(c, s, e, ref chance);
            ModifyInjuryChance_G(c, s, e, ref chance);
            LineageInjury(c, s, e, ref chance);
        }

        static partial void ModifyInjuryChance_F(ContentDb c, RunState s, double e, ref double chance);
        static partial void ModifyInjuryChance_G(ContentDb c, RunState s, double e, ref double chance);

        /// <summary>
        /// OnDrillDone: Drill, after the gain, the energy cost and Echo, before an injury or the AP is spent. <paramref name="energyBefore"/>
        /// is the energy before the drill; a hook that changes s.Energy also sets result.EnergyAfter.
        /// </summary>
        static void HookDrillDone(ContentDb c, RunState s, DrillResult result, double energyBefore)
        {
            OnDrillDone_F(c, s, result, energyBefore);
            OnDrillDone_G(c, s, result, energyBefore);
            LineageDrillDone(c, s, result, energyBefore);
        }

        static partial void OnDrillDone_F(ContentDb c, RunState s, DrillResult result, double energyBefore);
        static partial void OnDrillDone_G(ContentDb c, RunState s, DrillResult result, double energyBefore);

        /// <summary>RestAllowed: CanRest: clear allowed to forbid Rest (Rest then throws, the UI locks the button).</summary>
        static void HookRestAllowed(ContentDb c, RunState s, ref bool allowed)
        {
            RestAllowed_F(c, s, ref allowed);
            RestAllowed_G(c, s, ref allowed);
        }

        static partial void RestAllowed_F(ContentDb c, RunState s, ref bool allowed);
        static partial void RestAllowed_G(ContentDb c, RunState s, ref bool allowed);

        /// <summary>ModifyRestEnergy: RestEnergy, the energy a rest restores before the cap.</summary>
        static void HookRestEnergy(ContentDb c, RunState s, ref double energy)
        {
            ModifyRestEnergy_F(c, s, ref energy);
            ModifyRestEnergy_G(c, s, ref energy);
        }

        static partial void ModifyRestEnergy_F(ContentDb c, RunState s, ref double energy);
        static partial void ModifyRestEnergy_G(ContentDb c, RunState s, ref double energy);

        /// <summary>OnCycleStart: NextCycle, once the new cycle's AP and SP are in.</summary>
        static void HookCycleStart(ContentDb c, RunState s)
        {
            OnCycleStart_F(c, s);
            OnCycleStart_G(c, s);
            LineageCycleStart(c, s);
        }

        static partial void OnCycleStart_F(ContentDb c, RunState s);
        static partial void OnCycleStart_G(ContentDb c, RunState s);

        /// <summary>OnRankFightStart: Fight, the two fresh fighters before the fight (both are new objects: change them freely).</summary>
        static void HookRankFightStart(ContentDb c, RunState s, Fighter monster, Fighter opponent)
        {
            OnRankFightStart_F(c, s, monster, opponent);
            OnRankFightStart_G(c, s, monster, opponent);
            LineageRankStart(c, s, monster, opponent);
        }

        static partial void OnRankFightStart_F(ContentDb c, RunState s, Fighter monster, Fighter opponent);
        static partial void OnRankFightStart_G(ContentDb c, RunState s, Fighter monster, Fighter opponent);

        /// <summary>OnRankFightResult: Fight, after rank and SP are settled, before the next cycle.</summary>
        static void HookRankFightResult(ContentDb c, RunState s, RankFightResult result)
        {
            OnRankFightResult_F(c, s, result);
            OnRankFightResult_G(c, s, result);
            LineageRankResult(c, s, result);
        }

        static partial void OnRankFightResult_F(ContentDb c, RunState s, RankFightResult result);
        static partial void OnRankFightResult_G(ContentDb c, RunState s, RankFightResult result);

        /// <summary>
        /// OnGauntletFloor: GauntletSim.Run, each floor's fresh enemy before its fight (change only <paramref name="enemy"/>: the
        /// monster's fighter is shared by every floor). The run is over, so only the fighter's rules tell what it holds.
        /// </summary>
        public static void HookGauntletFloor(ContentDb c, Fighter monster, Fighter enemy)
        {
            OnGauntletFloor_F(c, monster, enemy);
            OnGauntletFloor_G(c, monster, enemy);
        }

        static partial void OnGauntletFloor_F(ContentDb c, Fighter monster, Fighter enemy);
        static partial void OnGauntletFloor_G(ContentDb c, Fighter monster, Fighter enemy);

        /// <summary>ModifyStatCap: StatCap, the most the stat can reach (STAT_MAX).</summary>
        static void HookStatCap(ContentDb c, RunState s, string stat, ref double cap)
        {
            ModifyStatCap_F(c, s, stat, ref cap);
            ModifyStatCap_G(c, s, stat, ref cap);
        }

        static partial void ModifyStatCap_F(ContentDb c, RunState s, string stat, ref double cap);
        static partial void ModifyStatCap_G(ContentDb c, RunState s, string stat, ref double cap);

        /// <summary>
        /// RollTree: the rolled tree's own stat, before any filler node is rolled. A lane may change it (drawing on
        /// <paramref name="r"/>, the draft stream, only for its own trees); the filler nodes on the tree's content own stat then
        /// move to the new one.
        /// </summary>
        static void HookRollOwnStat(ContentDb c, RngStream r, TreeDef t, ref string own)
        {
            RollOwnStat_F(c, r, t, ref own);
            RollOwnStat_G(c, r, t, ref own);
        }

        static partial void RollOwnStat_F(ContentDb c, RngStream r, TreeDef t, ref string own);
        static partial void RollOwnStat_G(ContentDb c, RngStream r, TreeDef t, ref string own);

        /// <summary>TreeRemoved: RemoveSlot, once a tree (overwritten, or a chase tree locking again) has left its slot and its refund and flat stats are settled.</summary>
        static void HookTreeRemoved(ContentDb c, RunState s)
        {
            OnTreeRemoved_F(c, s);
            OnTreeRemoved_G(c, s);
        }

        static partial void OnTreeRemoved_F(ContentDb c, RunState s);
        static partial void OnTreeRemoved_G(ContentDb c, RunState s);

        /// <summary>
        /// KitExtra: MonsterFighter and Vault.Seal, the tree nodes (ids, in kit order) the run adds to the kit beyond the bought
        /// nodes of its trees. BuildFighter adds them after those, so a sealed Vault entry keeps them without the run state.
        /// </summary>
        static List<string> HookKitExtra(ContentDb c, RunState s)
        {
            var extra = new List<string>();
            KitExtra_F(c, s, extra);
            KitExtra_G(c, s, extra);
            LineageKitExtra(c, s, extra);
            return extra;
        }

        static partial void KitExtra_F(ContentDb c, RunState s, List<string> extra);
        static partial void KitExtra_G(ContentDb c, RunState s, List<string> extra);

        /// <summary>HashRun: the run hash, at the end: the lane's run state, only when the run has it (so every other run keeps its hash).</summary>
        static void HookHashRun(CanonicalWriter w, RunState s)
        {
            HashRun_F(w, s);
            HashRun_G(w, s);
            LineageHash(w, s);
        }

        static partial void HashRun_F(CanonicalWriter w, RunState s);
        static partial void HashRun_G(CanonicalWriter w, RunState s);
    }
}
