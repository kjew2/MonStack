using System;
using IdleRng.Bots.Measure;
using IdleRng.Sim;
using IdleRng.Sim.Content;
using IdleRng.Sim.State;
using IdleRng.Sim.Time;

namespace IdleRng.Bots.Policies
{
    public sealed class BotContext
    {
        public BotContext(Simulation sim, FakeClock clock, BotEventSink stats)
        {
            Sim = sim;
            Clock = clock;
            Stats = stats;
        }

        public Simulation Sim { get; }
        public GameState State => Sim.State;
        public ContentDb Content => Sim.Content;
        public FakeClock Clock { get; }
        public BotEventSink Stats { get; }
        public int Day { get; set; }
    }

    public interface IBotPolicy
    {
        /// <summary>CLI id and golden-file component: "greedy-f2p", "whale".</summary>
        string Name { get; }

        void OnDayStart(BotContext ctx);

        /// <summary>Called once per DecisionIntervalSeconds of active play.</summary>
        void Decide(BotContext ctx);
    }
}
