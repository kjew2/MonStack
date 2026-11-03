using System.Collections.Generic;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;

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
        }

        static partial void ModifyGrowth_F(ContentDb c, RunState s, string stat, ref double growth);
        static partial void ModifyGrowth_G(ContentDb c, RunState s, string stat, ref double growth);

        /// <summary>ModifyDrillGain: Drill, the floored gain before the stat cap.</summary>
        static void HookDrillGain(ContentDb c, RunState s, string stat, DrillTier tier, ref double gain)
        {
            ModifyDrillGain_F(c, s, stat, tier, ref gain);
            ModifyDrillGain_G(c, s, stat, tier, ref gain);
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
        }

        static partial void OnCycleStart_F(ContentDb c, RunState s);
        static partial void OnCycleStart_G(ContentDb c, RunState s);

        /// <summary>OnRankFightStart: Fight, the two fresh fighters before the fight (both are new objects: change them freely).</summary>
        static void HookRankFightStart(ContentDb c, RunState s, Fighter monster, Fighter opponent)
        {
            OnRankFightStart_F(c, s, monster, opponent);
            OnRankFightStart_G(c, s, monster, opponent);
        }

        static partial void OnRankFightStart_F(ContentDb c, RunState s, Fighter monster, Fighter opponent);
        static partial void OnRankFightStart_G(ContentDb c, RunState s, Fighter monster, Fighter opponent);

        /// <summary>OnRankFightResult: Fight, after rank and SP are settled, before the next cycle.</summary>
        static void HookRankFightResult(ContentDb c, RunState s, RankFightResult result)
        {
            OnRankFightResult_F(c, s, result);
            OnRankFightResult_G(c, s, result);
        }

        static partial void OnRankFightResult_F(ContentDb c, RunState s, RankFightResult result);
        static partial void OnRankFightResult_G(ContentDb c, RunState s, RankFightResult result);

        /// <summary>ModifyStatCap: StatCap, the most the stat can reach (STAT_MAX).</summary>
        static void HookStatCap(ContentDb c, RunState s, string stat, ref double cap)
        {
            ModifyStatCap_F(c, s, stat, ref cap);
            ModifyStatCap_G(c, s, stat, ref cap);
        }

        static partial void ModifyStatCap_F(ContentDb c, RunState s, string stat, ref double cap);
        static partial void ModifyStatCap_G(ContentDb c, RunState s, string stat, ref double cap);

        /// <summary>HashRun: the run hash, at the end: the lane's run state, only when the run has it (so every other run keeps its hash).</summary>
        static void HookHashRun(CanonicalWriter w, RunState s)
        {
            HashRun_F(w, s);
            HashRun_G(w, s);
        }

        static partial void HashRun_F(CanonicalWriter w, RunState s);
        static partial void HashRun_G(CanonicalWriter w, RunState s);
    }
}
