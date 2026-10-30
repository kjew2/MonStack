using System;

namespace IdleRng.Bots.Report
{
    /// <summary>Output helpers shared by the golden files: how a content version and hash are printed.</summary>
    public static class TableWriter
    {
        public static string ShortHash(string hash) => string.IsNullOrEmpty(hash) ? "-" : hash.Substring(0, Math.Min(8, hash.Length));

        public static string ContentTag(string version, string hash) => $"{version}/{ShortHash(hash)}";
    }
}
