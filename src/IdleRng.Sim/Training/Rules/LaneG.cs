using System;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Training
{
    // Idea-pool lane G: run rules (#140, specs/14 section 4.3). Each rule takes effect once its capstone is bought and ends when
    // the tree leaves its slot (overwritten); an absorbed (fused) tree still counts. None of them draws.
    //   iron_vow       Rest is refused; every drill's gain is x(1 + VOW_GAIN), floored
    //   cursed_mark    DEF growth is 0 (drills give no DEF); every rank-fight and gauntlet enemy has each stat x(1 - CURSE_FOE)
    //   devourer       a rank-fight win adds DEVOUR_FRAC of the opponent's highest stat to that stat; each cycle start costs DEVOUR_ENERGY
    //   second_breath  a Super drill refunds its energy cost; a drill made below BREATH_SAFE energy cannot injure or Super
    //   ascetic        +ASCETIC_AP AP at each cycle start; a rank-fight win pays no SP_PER_WIN
    //   hungry_core    each cycle start eats HUNGRY_ENERGY energy (devourer's cost first); fed, every trained stat is +HUNGRY_STAT in
    //                  that cycle's rank fight; short of energy it is dormant for the cycle (RunState.CoreDormant, hashed when set)
    public static partial class TrainingRun
    {
        /// <summary>Lane G's rules whose capstone the UI warns about before the purchase (they take something away).</summary>
        public static readonly string[] BuyWarnRules = { RuleModifiers.IronVow, RuleModifiers.CursedMark };

        /// <summary>Whether the run holds a bought rule node <paramref name="ruleId"/> (slotted or absorbed tree).</summary>
        public static bool HasRunRule(ContentDb c, RunState s, string ruleId)
        {
            foreach (var ts in AllTrees(s))
            {
                var nodes = Nodes(c, ts);
                for (int i = 0; i < ts.Bought && i < nodes.Count; i++)
                    if (nodes[i].Kind == NodeKind.Rule && nodes[i].RuleId == ruleId) return true;
            }
            return false;
        }

        /// <summary>hungry_core: the run holds the core and it is fed this cycle (its bonus applies to the cycle's rank fight).</summary>
        public static bool CoreFed(ContentDb c, RunState s) => HasRunRule(c, s, RuleModifiers.HungryCore) && !s.CoreDormant;

        static double GConst(ContentDb c, string key) => c.ConstantValues[key];

        /// <summary>cursed_mark: lower each trained stat of an enemy by CURSE_FOE.</summary>
        static void Curse(ContentDb c, Fighter foe)
        {
            double keep = 1 - GConst(c, "CURSE_FOE");
            foreach (var id in StatIds.Trained) Set(foe.Stats, id, foe.Stats.Get(id) * keep);
        }

        /// <summary>second_breath: whether a drill at energy fraction e is made below BREATH_SAFE energy.</summary>
        static bool BelowBreath(ContentDb c, double e) => e * c.Constants.RunEnergyMax < GConst(c, "BREATH_SAFE");

        static partial void ModifyGrowth_G(ContentDb c, RunState s, string stat, ref double growth)
        {
            // cursed_mark: DEF growth is 0, so a DEF drill gains nothing (and the UI and the bots see it).
            if (stat == StatIds.Def && HasRunRule(c, s, RuleModifiers.CursedMark)) growth = 0;
        }

        static partial void ModifyDrillGain_G(ContentDb c, RunState s, string stat, DrillTier tier, ref double gain)
        {
            if (HasRunRule(c, s, RuleModifiers.IronVow)) gain = Math.Floor(gain * (1 + GConst(c, "VOW_GAIN")));
        }

        static partial void ModifySuperChance_G(ContentDb c, RunState s, double e, ref double super)
        {
            if (BelowBreath(c, e) && HasRunRule(c, s, RuleModifiers.SecondBreath)) super = 0;
        }

        static partial void ModifyInjuryChance_G(ContentDb c, RunState s, double e, ref double chance)
        {
            if (BelowBreath(c, e) && HasRunRule(c, s, RuleModifiers.SecondBreath)) chance = 0;
        }

        static partial void OnDrillDone_G(ContentDb c, RunState s, DrillResult result, double energyBefore)
        {
            // second_breath: a Super gives back what the drill cost (not Second Wind's drop to 0, which is the shield's price).
            if (result.Tier != DrillTier.Super || result.Shielded || !HasRunRule(c, s, RuleModifiers.SecondBreath)) return;
            double paid = energyBefore - s.Energy;
            if (paid <= 0) return;
            s.Energy = Math.Min(c.Constants.RunEnergyMax, s.Energy + paid);
            result.Refunded = paid;
            result.EnergyAfter = s.Energy;
        }

        static partial void RestAllowed_G(ContentDb c, RunState s, ref bool allowed)
        {
            if (allowed && HasRunRule(c, s, RuleModifiers.IronVow)) allowed = false;
        }

        static partial void OnCycleStart_G(ContentDb c, RunState s)
        {
            if (HasRunRule(c, s, RuleModifiers.Ascetic)) s.ApLeft += (int)GConst(c, "ASCETIC_AP");
            if (HasRunRule(c, s, RuleModifiers.Devourer)) s.Energy = Math.Max(0, s.Energy - GConst(c, "DEVOUR_ENERGY"));
            s.CoreDormant = false;
            if (!HasRunRule(c, s, RuleModifiers.HungryCore)) return;
            double eat = GConst(c, "HUNGRY_ENERGY");
            if (s.Energy >= eat) s.Energy -= eat;
            else s.CoreDormant = true;
        }

        static partial void OnRankFightStart_G(ContentDb c, RunState s, Fighter monster, Fighter opponent)
        {
            if (HasRunRule(c, s, RuleModifiers.CursedMark)) Curse(c, opponent);
            if (CoreFed(c, s))
            {
                double add = GConst(c, "HUNGRY_STAT");
                foreach (var id in StatIds.Trained) Set(monster.Stats, id, monster.Stats.Get(id) + add);
            }
        }

        static partial void OnRankFightResult_G(ContentDb c, RunState s, RankFightResult result)
        {
            if (!result.Won) return;
            if (HasRunRule(c, s, RuleModifiers.Ascetic))
            {
                int sp = c.Constants.SpPerWin;
                s.Sp -= sp;
                result.SpGained -= sp;
            }
            if (HasRunRule(c, s, RuleModifiers.Devourer))
            {
                // The opponent as it is on the ladder (before cursed_mark); its highest trained stat, the first in sheet order on a tie.
                var foe = OpponentFighter(c, s, result.RankBefore).Stats;
                string top = StatIds.Trained[0];
                foreach (var id in StatIds.Trained) if (foe.Get(id) > foe.Get(top)) top = id;
                double before = s.Stats.Get(top);
                double after = Math.Max(before, Math.Min(StatCap(c, s, top), before + Math.Floor(foe.Get(top) * GConst(c, "DEVOUR_FRAC"))));
                Set(s.Stats, top, after);
                result.DevouredStat = top;
                result.DevouredGain = after - before;
                CrossMilestones(c, s, top, before, after, result.Milestones);
            }
        }

        static partial void OnGauntletFloor_G(ContentDb c, Fighter monster, Fighter enemy)
        {
            if (monster.Rules.Contains(RuleModifiers.CursedMark)) Curse(c, enemy);
        }

        static partial void HashRun_G(CanonicalWriter w, RunState s)
        {
            if (!s.CoreDormant) return;
            w.Str("lane_g");
            w.Bool(true);
        }
    }
}
