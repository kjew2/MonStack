using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using IdleRng.Sim.Combat;
using Unity.Services.CloudCode.Core;

namespace IdleRngBattle
{
    /// <summary>What ResolveFight returns: enough to compare a server fight with the client's, bit for bit.</summary>
    public sealed class FightReply
    {
        public string Outcome { get; set; }
        public int Ticks { get; set; }

        /// <summary>BattleSim.StateHash of the final state.</summary>
        public string Hash { get; set; }
        public int HashVersion { get; set; }

        /// <summary>Time spent inside BattleSim.Resolve on the server, in microseconds (excludes JSON and the network).</summary>
        public long ResolveMicros { get; set; }

        /// <summary>The worker's runtime, OS, architecture and culture, so a hash mismatch can be traced.</summary>
        public string Runtime { get; set; }
    }

    /// <summary>
    /// The backend spike's one endpoint (issue #17): resolve a fight with the shared IdleRng.Sim and return its hash. Inputs are
    /// the plain-data FightRules and Fighter types, bound by name from the call's params by Cloud Code (Newtonsoft.Json). The seed
    /// is a decimal string: it is a full 64-bit value and a JSON number cannot carry it to or from a JavaScript client.
    /// </summary>
    public class BattleModule
    {
        [CloudCodeFunction("ResolveFight")]
        public FightReply ResolveFight(FightRules rules, Fighter[] sideA, Fighter[] sideB, string seed)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (sideA == null || sideA.Length == 0) throw new ArgumentException("sideA needs at least one fighter", nameof(sideA));
            if (sideB == null || sideB.Length == 0) throw new ArgumentException("sideB needs at least one fighter", nameof(sideB));
            if (!ulong.TryParse(seed, NumberStyles.None, CultureInfo.InvariantCulture, out ulong s))
                throw new ArgumentException("seed must be an unsigned 64-bit decimal string", nameof(seed));

            var sw = Stopwatch.StartNew();
            var r = BattleSim.Resolve(rules, sideA, sideB, s);
            sw.Stop();
            return new FightReply
            {
                Outcome = r.Outcome.ToString(),
                Ticks = r.Ticks,
                Hash = r.Hash,
                HashVersion = BattleSim.HashVersion,
                ResolveMicros = sw.ElapsedTicks * 1_000_000 / Stopwatch.Frequency,
                Runtime = RuntimeInformation.FrameworkDescription + "; " + RuntimeInformation.OSDescription + "; "
                          + RuntimeInformation.ProcessArchitecture + "; culture=" + CultureInfo.CurrentCulture.Name,
            };
        }
    }
}
