using System;
using System.Collections.Generic;
using System.Text;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;

namespace IdleRng.Sim.Training
{
    /// <summary>
    /// The training run (specs/14): cycles of RUN_AP taps, each a Drill or a Rest with instant feedback, then a 1v1 rank fight
    /// every FIGHT_EVERY cycles. Pure functions over a RunState: no clock, IO or Math.Pow; every roll comes from the run's
    /// training stream and every fight seed is derived from the run seed, so the command log replays exactly.
    /// </summary>
    public static class TrainingRun
    {
        public const string TrainingStream = "training", FightStream = "fight";
        public const string MonsterId = "monster";
        public const int HashVersion = 1;
        static readonly byte[] HashMagic = Encoding.ASCII.GetBytes("IDLERNG-RUNHASH-v1");

        // ------------------------------------------------------------------ start

        /// <summary>A fresh run of <paramref name="speciesId"/>: species base stats, full start energy, cycle 1, lowest rank.</summary>
        public static RunState Start(ContentDb c, string speciesId, ulong seed)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (speciesId == null || !c.Species.TryGetValue(speciesId, out var sp))
                throw new ArgumentException($"unknown species '{speciesId}'", nameof(speciesId));
            var k = c.Constants;
            return new RunState
            {
                SpeciesId = speciesId, Seed = seed, Stats = sp.Base.Clone(),
                Energy = k.RunEnergyStart, Cycle = 1, ApLeft = k.RunAp, Rank = 0, Phase = RunPhase.Training,
                Training = new RngStream(TrainingStream, StreamNames.StreamSeed(seed, TrainingStream)),
            };
        }

        // ------------------------------------------------------------------ commands

        /// <summary>
        /// Drill one trained stat (specs/14 section 4). Rolls the tier (fail/success/super, odds from current energy), then the
        /// gain inside the tier's range, then injury when energy is low. Costs 1 AP and DRILL_ENERGY_COST energy.
        /// </summary>
        public static DrillResult Drill(ContentDb c, RunState s, string stat)
        {
            RequirePhase(s, RunPhase.Training, "Drill");
            if (!StatIds.IsTrained(stat)) throw new ArgumentException($"'{stat}' is not a trained stat", nameof(stat));
            var k = c.Constants;
            double e = EnergyFraction(k, s.Energy);

            var tier = RollTier(k, e, s.Training.NextDouble());
            var def = c.DrillTiers[(int)tier];
            int span = def.GainMax - def.GainMin + 1;
            int raw = def.GainMin + Math.Min(span - 1, (int)(s.Training.NextDouble() * span));
            double growth = c.Species[s.SpeciesId].Growth.Get(stat);
            double before = s.Stats.Get(stat);
            double after = Math.Min(k.StatMax, before + Math.Floor(raw * growth));
            Set(s.Stats, stat, after);

            double injuryP = InjuryChance(k, e);
            bool injured = injuryP > 0 && s.Training.NextDouble() < injuryP;

            s.Energy = Math.Max(0, s.Energy - k.DrillEnergyCost);
            s.Drills++;
            s.Log.Add(new RunCommand(RunCommandKind.Drill, stat));
            var result = new DrillResult
            {
                Stat = stat, Tier = tier, Gain = after - before, NewValue = after, EnergyAfter = s.Energy, Injured = injured,
            };
            foreach (int t in c.StatMilestones)
                if (before < t && after >= t)
                {
                    var hit = new MilestoneHit { Stat = stat, Threshold = t, Cycle = s.Cycle };
                    s.Milestones.Add(hit);
                    result.Milestones.Add(hit);
                }

            if (injured) { s.Phase = RunPhase.Finished; s.End = RunEnd.Injured; s.ApLeft = 0; }
            else SpendAp(c, s);
            return result;
        }

        /// <summary>Rest: restore REST_ENERGY (capped at max). Costs 1 AP. No roll.</summary>
        public static RestResult Rest(ContentDb c, RunState s)
        {
            RequirePhase(s, RunPhase.Training, "Rest");
            var k = c.Constants;
            double before = s.Energy;
            s.Energy = Math.Min(k.RunEnergyMax, s.Energy + k.RestEnergy);
            s.Rests++;
            s.Log.Add(new RunCommand(RunCommandKind.Rest));
            var result = new RestResult { Restored = s.Energy - before, EnergyAfter = s.Energy };
            SpendAp(c, s);
            return result;
        }

        /// <summary>
        /// The due rank fight (specs/14 section 5): the monster, with its current stats and no skills until T3, against the
        /// current rank's opponent, 1v1. A win ranks up (the top rank stays). Then the next cycle starts, or the run completes.
        /// </summary>
        public static RankFightResult Fight(ContentDb c, RunState s, Action<FightAction> onAction = null)
        {
            RequirePhase(s, RunPhase.AwaitFight, "Fight");
            var rank = c.FightRanks[s.Rank];
            var monster = MonsterFighter(c, s);
            var opponent = new Fighter { Id = "rank_" + rank.Id, Element = rank.Element, Stats = rank.Stats.Clone() };
            var fight = BattleSim.Resolve(FightRules.FromContent(c), new[] { monster }, new[] { opponent }, FightSeed(s.Seed, s.FightsFought), onAction);

            var result = new RankFightResult { RankBefore = s.Rank, Won = fight.Outcome == FightOutcome.SideAWon, Fight = fight };
            s.FightsFought++;
            if (result.Won)
            {
                s.FightsWon++;
                if (s.Rank < c.FightRanks.Count - 1) s.Rank++;
            }
            result.RankAfter = s.Rank;
            s.Log.Add(new RunCommand(RunCommandKind.Fight));
            NextCycle(c, s);
            return result;
        }

        /// <summary>The monster as a battle fighter: species element, current stats, no skills or triggers yet (T3 adds trees).</summary>
        public static Fighter MonsterFighter(ContentDb c, RunState s) =>
            new Fighter { Id = MonsterId, Element = c.Species[s.SpeciesId].Element, Stats = s.Stats.Clone() };

        /// <summary>Seed of the run's <paramref name="index"/>-th fight (0-based). Fights never draw from the training stream.</summary>
        public static ulong FightSeed(ulong runSeed, int index) =>
            SplitMix64.Derive(StreamNames.StreamSeed(runSeed, FightStream), (ulong)index);

        // ------------------------------------------------------------------ odds (public for the UI and tests)

        public static double EnergyFraction(Constants k, double energy) => Clamp01(energy / k.RunEnergyMax);

        /// <summary>Fail chance at energy fraction e: DRILL_FAIL_MIN at full energy up to DRILL_FAIL_MAX at zero.</summary>
        public static double FailChance(Constants k, double e) => k.DrillFailMin + (k.DrillFailMax - k.DrillFailMin) * (1 - Clamp01(e));

        /// <summary>Injury chance of a drill at energy fraction e: 0 at or above INJURY_BELOW, rising to INJURY_MAX_P at zero.</summary>
        public static double InjuryChance(Constants k, double e)
        {
            if (k.InjuryBelow <= 0 || e >= k.InjuryBelow) return 0;
            return k.InjuryMaxP * (k.InjuryBelow - Clamp01(e)) / k.InjuryBelow;
        }

        /// <summary>Tier from one draw u: [0, fail) fail, [fail, 1 - super) success, [1 - super, 1) super.</summary>
        public static DrillTier RollTier(Constants k, double e, double u)
        {
            if (u < FailChance(k, e)) return DrillTier.Fail;
            if (u < 1 - k.DrillSuperP) return DrillTier.Success;
            return DrillTier.Super;
        }

        // ------------------------------------------------------------------ flow

        static void SpendAp(ContentDb c, RunState s)
        {
            s.ApLeft--;
            if (s.ApLeft > 0) return;
            if (s.Cycle % c.Constants.FightEvery == 0) s.Phase = RunPhase.AwaitFight;
            else NextCycle(c, s);
        }

        static void NextCycle(ContentDb c, RunState s)
        {
            if (s.Cycle >= c.Constants.RunCycles)
            {
                s.Phase = RunPhase.Finished;
                s.End = RunEnd.Completed;
                s.ApLeft = 0;
                return;
            }
            s.Cycle++;
            s.ApLeft = c.Constants.RunAp;
            s.Phase = RunPhase.Training;
        }

        static void RequirePhase(RunState s, RunPhase phase, string command)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (s.Phase != phase) throw new InvalidOperationException($"{command} needs phase {phase}, run is in {s.Phase}");
        }

        static void Set(MonsterStats st, string id, double v)
        {
            switch (id)
            {
                case StatIds.Hp: st.Hp = v; break;
                case StatIds.Str: st.Str = v; break;
                case StatIds.Int: st.Int = v; break;
                case StatIds.Wis: st.Wis = v; break;
                case StatIds.Dex: st.Dex = v; break;
                case StatIds.Def: st.Def = v; break;
                case StatIds.Spd: st.Spd = v; break;
                case StatIds.Luk: st.Luk = v; break;
                default: throw new ArgumentOutOfRangeException(nameof(id), id, "not a trained stat");
            }
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        // ------------------------------------------------------------------ replay and hash

        /// <summary>Rebuild a run from its species, seed and command log. Throws if a command is not valid at its point.</summary>
        public static RunState Replay(ContentDb c, string speciesId, ulong seed, IEnumerable<RunCommand> log)
        {
            var s = Start(c, speciesId, seed);
            foreach (var cmd in log)
            {
                switch (cmd.Kind)
                {
                    case RunCommandKind.Drill: Drill(c, s, cmd.Stat); break;
                    case RunCommandKind.Rest: Rest(c, s); break;
                    case RunCommandKind.Fight: Fight(c, s); break;
                    default: throw new ArgumentOutOfRangeException(nameof(log), cmd.Kind, "unknown command");
                }
            }
            return s;
        }

        /// <summary>Lowercase hex SHA-256 over a canonical encoding of the run (StateHasher conventions). Bump HashVersion on change.</summary>
        public static string StateHash(RunState s)
        {
            var w = new CanonicalWriter(256);
            w.Bytes(HashMagic);
            w.Str(s.SpeciesId);
            w.U64(s.Seed);
            foreach (var id in StatIds.Trained) w.F64(s.Stats.Get(id));
            w.F64(s.Energy);
            w.I32(s.Cycle); w.I32(s.ApLeft); w.I32(s.Rank);
            w.I32((int)s.Phase); w.I32((int)s.End);
            w.Str(s.Training.Name); w.U64(s.Training.Seed); w.U64(s.Training.Counter);
            w.I32(s.FightsFought); w.I32(s.FightsWon); w.I32(s.Drills); w.I32(s.Rests);
            w.I32(s.Milestones.Count);
            foreach (var m in s.Milestones) { w.Str(m.Stat); w.I32(m.Threshold); w.I32(m.Cycle); }
            w.I32(s.Log.Count);
            foreach (var l in s.Log) { w.I32((int)l.Kind); w.Str(l.Stat); }
            return Sha256.Hex(Sha256.Compute(w.ToArray()));
        }
    }
}
