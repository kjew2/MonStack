using IdleRng.Sim.State;

namespace IdleRng.Bots.Policies
{
    /// <summary>Mirrors Bot.spend() in tools/power_curve_model.py (specs/09 section 5.2).</summary>
    public class GreedyF2PBot : IBotPolicy
    {
        public virtual string Name => "greedy-f2p";

        /// <summary>The Python model pulls singles only.</summary>
        protected virtual bool UseTenPulls => false;

        public virtual void OnDayStart(BotContext ctx)
        {
            int free = ctx.Content.HeroBanner.FreePullsPerDay;
            for (int i = 0; i < free && ctx.State.Currencies.FreePullsDaily > 0; i++) ctx.Sim.Pull();
            ctx.Sim.AutoEquipBestHeroes();
        }

        public void Decide(BotContext ctx)
        {
            var sim = ctx.Sim;

            PromoteBest(ctx);                                                         // 0. one promotion if affordable (specs/11 section 6)

            sim.BuyStatLevelsMax();                                                   // 1. stat levels while affordable

            int slot = WeakestEnhanceableSlot(ctx.State, ctx.Content.Constants.EnhMax);
            if (slot >= 0) sim.Enhance(slot);                                         // 2. one attempt (rejected for gold is fine)

            SpendGems(ctx);                                                           // 3. gems -> pulls, 4. equip best six

            if (ctx.State.Stage.Boss == BossPhase.Walled && sim.Query.BossClearable)   // 5. retry once clearable
                sim.AttemptBoss();
        }

        protected void SpendGems(BotContext ctx)
        {
            var sim = ctx.Sim;
            var w = ctx.State.Currencies;
            int cost = ctx.Content.HeroBanner.PullCostGems;
            int batch = ctx.Content.HeroBanner.GuaranteeBatchSize;
            bool pulled = false;
            if (UseTenPulls)
                while (w.Gems >= (long)batch * cost && sim.Pull10().Ok) pulled = true;
            while (w.Gems >= cost && sim.Pull().Ok) pulled = true;
            if (pulled) sim.AutoEquipBestHeroes();
        }

        /// <summary>Promote the party member with the best FarmDps gain per gold, if any promotion is affordable now.</summary>
        public static bool PromoteBest(BotContext ctx)
        {
            var q = ctx.Sim.Query;
            string best = null;
            double bestScore = 0;
            foreach (var id in ctx.State.Heroes.Equipped)
            {
                if (id == null || !q.CanPromote(id)) continue;
                double gain = q.HeroFarmDpsAfterPromotion(id) - q.HeroFarmDps(id), cost = q.NextPromotionCost(id);
                double score = cost > 0 ? gain / cost : gain > 0 ? double.MaxValue : 0;   // free promotions first, never NaN
                if (best == null || score > bestScore || (score == bestScore && string.CompareOrdinal(id, best) < 0))
                {
                    best = id;
                    bestScore = score;
                }
            }
            return best != null && ctx.Sim.Promote(best).Ok;
        }

        /// <summary>Lowest Enhance among occupied slots below ENH_MAX; lowest index on ties; -1 if none.</summary>
        public static int WeakestEnhanceableSlot(GameState s, int enhMax)
        {
            int best = -1;
            var slots = s.Gear.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                var p = slots[i];
                if (p == null || p.Enhance >= enhMax) continue;
                if (best < 0 || p.Enhance < slots[best].Enhance) best = i;
            }
            return best;
        }
    }

    /// <summary>Buys 100 pulls on day 1 as ten 10-pulls (specs/02 section 4.3), then plays like the F2P bot.</summary>
    public sealed class WhaleBot : GreedyF2PBot
    {
        public const int Day1ExtraPulls = 100;
        public override string Name => "whale";
        protected override bool UseTenPulls => true;

        public override void OnDayStart(BotContext ctx)
        {
            if (ctx.Day == 1) ctx.Sim.Debug.GrantGems((long)Day1ExtraPulls * ctx.Content.HeroBanner.PullCostGems);
            base.OnDayStart(ctx);
            SpendGems(ctx);
        }
    }
}
