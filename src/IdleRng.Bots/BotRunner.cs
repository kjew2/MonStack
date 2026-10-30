using System;
using System.Linq;
using IdleRng.Bots.Measure;
using IdleRng.Bots.Policies;
using IdleRng.Sim;
using IdleRng.Sim.Content;
using IdleRng.Sim.State;
using IdleRng.Sim.Time;

namespace IdleRng.Bots
{
    public sealed class RunResult
    {
        public string Bot { get; set; }
        public ulong Seed { get; set; }
        public int Days { get; set; }
        public string ContentVersion { get; set; }
        public string ContentHash { get; set; }
        public DayRow[] Rows { get; set; }
        public WallRecord[] Walls { get; set; }
        /// <summary>Clock (ms) at the end of each day, after its sync; index = day - 1. For wall checks at a given day.</summary>
        public long[] DayEndUtcMs { get; set; }
        public double BaseAtk { get; set; }
    }

    /// <summary>Plays N bot days (specs/09 section 4): 120 active minutes in 60 s decisions, then one night sync.</summary>
    public sealed class BotRunner
    {
        readonly ContentDb _content;

        public BotRunner(ContentDb content) { _content = content; }

        /// <summary>Optional hook called after each day's snapshot (tests).</summary>
        public Action<int, BotContext> AfterDay { get; set; }

        public static Guid ProfileIdFor(ulong seed) => new Guid((int)seed, 0, 0, new byte[8]);

        public RunResult Run(IBotPolicy policy, ulong seed, int days)
        {
            var c = _content;
            var k = c.Constants;
            long epochSec = HarnessConstants.Epoch.ToUnixTimeSeconds();
            var clock = new FakeClock(epochSec);
            var state = GameState.NewProfile(c, ProfileIdFor(seed), seed, epochSec);
            var sink = new BotEventSink(state, k.TickHz);
            var sim = new Simulation(c, state, sink);
            var ctx = new BotContext(sim, clock, sink);
            var rows = new DayRow[days];
            var dayEnd = new long[days];

            sim.SyncWallClock(clock);   // anchors at Epoch and rolls day 1 (NewProfile starts at DayKey -1): free pulls available

            int ticksPerDecision = HarnessConstants.DecisionIntervalSeconds * k.TickHz;
            int decisionsPerDay = HarnessConstants.ActiveMinutesPerDay * 60 / HarnessConstants.DecisionIntervalSeconds;

            for (int day = 1; day <= days; day++)
            {
                ctx.Day = day;
                sink.Day = day;
                long dayStart = epochSec + (long)(day - 1) * k.DaySeconds;

                sim.Debug.GrantGems(k.GemsPerDayF2p);   // flat F2P income (specs/09 section 5.3)
                policy.OnDayStart(ctx);

                for (int d = 0; d < decisionsPerDay; d++)
                {
                    sim.Advance(ticksPerDecision);
                    clock.Advance(HarnessConstants.DecisionIntervalSeconds);
                    policy.Decide(ctx);
                }

                clock.Set(dayStart + k.DaySeconds);
                sim.SyncWallClock(clock);   // the one catch-up: 22 h gap, credited up to OFFLINE_CAP_H; rolls the day

                long nowMs = clock.NowServerSeconds * 1000L;
                dayEnd[day - 1] = nowMs;
                rows[day - 1] = Snapshot(day, sim, sink, nowMs);
                AfterDay?.Invoke(day, ctx);
            }

            return new RunResult
            {
                Bot = policy.Name, Seed = seed, Days = days, ContentVersion = c.Version, ContentHash = c.ContentHash,
                Rows = rows, Walls = sink.Walls.ToArray(), DayEndUtcMs = dayEnd, BaseAtk = k.BaseAtk,
            };
        }

        static DayRow Snapshot(int day, Simulation sim, BotEventSink sink, long nowUtcMs)
        {
            var q = sim.Query;
            var s = sim.State;
            double longest = sink.Walls.Count == 0 ? 0 : sink.Walls.Max(w => w.WallClockHours(nowUtcMs));
            return new DayRow(day, s.Stage.Stage, q.Dps, s.Progression.StatLevel, q.StatMult, q.GearMult, q.HeroMult, q.ClassMult,
                              s.HeroBanner.TotalPulls, sink.LPlus, s.Lifetime.FirstWallStage, longest);
        }
    }
}
