using System;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    internal static class TestContent
    {
        static readonly Lazy<ContentDb> _db = new Lazy<ContentDb>(() =>
        {
            string dir = FileContentSource.FindContentDirectory(TestContext.CurrentContext.TestDirectory)
                         ?? throw new InvalidOperationException("Content/manifest.json not found above the test directory");
            return ContentDb.LoadFromDirectory(dir);
        });

        public static ContentDb Db => _db.Value;
    }

    /// <summary>Returns a scripted sequence of doubles, repeating the pattern.</summary>
    internal sealed class ScriptedRandom : IRandomSource
    {
        readonly double[] _pattern;
        public int Draws;

        public ScriptedRandom(params double[] pattern) { _pattern = pattern; }

        public Func<int, double> Override;   // draw index -> value, or NaN to fall back to the pattern

        public double NextDouble()
        {
            int i = Draws++;
            if (Override != null)
            {
                double v = Override(i);
                if (!double.IsNaN(v)) return v;
            }
            return _pattern[i % _pattern.Length];
        }
    }
}
