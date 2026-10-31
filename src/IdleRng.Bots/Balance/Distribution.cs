using System;
using System.Collections.Generic;
using System.Linq;

namespace IdleRng.Bots.Balance
{
    /// <summary>
    /// Summary of one metric over many runs (specs/09 section 7): count, mean, population standard deviation, min, max and the
    /// nearest-rank 10th, 50th and 90th percentiles (the value at sorted index ceil(p x n) - 1). Empty input gives all zeros.
    /// </summary>
    public sealed class Distribution
    {
        public int N { get; private set; }
        public double Mean { get; private set; }
        public double Sd { get; private set; }
        public double Min { get; private set; }
        public double P10 { get; private set; }
        public double P50 { get; private set; }
        public double P90 { get; private set; }
        public double Max { get; private set; }

        public static Distribution Of(IEnumerable<double> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var v = values.ToArray();
            var d = new Distribution { N = v.Length };
            if (v.Length == 0) return d;
            Array.Sort(v);
            double sum = 0;
            foreach (var x in v) sum += x;
            d.Mean = sum / v.Length;
            double sq = 0;
            foreach (var x in v) sq += (x - d.Mean) * (x - d.Mean);
            d.Sd = Math.Sqrt(sq / v.Length);
            d.Min = v[0];
            d.Max = v[v.Length - 1];
            d.P10 = Percentile(v, 10);
            d.P50 = Percentile(v, 50);
            d.P90 = Percentile(v, 90);
            return d;
        }

        public static Distribution Of(IEnumerable<int> values) => Of(values.Select(x => (double)x));

        /// <summary>
        /// Nearest-rank percentile (<paramref name="percent"/> 0..100) of an ascending, non-empty array, in integer arithmetic so
        /// no rounding of p x n can pick a neighbouring rank.
        /// </summary>
        public static double Percentile(double[] sorted, int percent)
        {
            if (sorted == null || sorted.Length == 0) throw new ArgumentException("need at least one value", nameof(sorted));
            if (percent < 0 || percent > 100) throw new ArgumentOutOfRangeException(nameof(percent));
            long rank = ((long)percent * sorted.Length + 99) / 100;   // ceil(percent x n / 100)
            return sorted[(int)Math.Max(0, rank - 1)];
        }
    }
}
