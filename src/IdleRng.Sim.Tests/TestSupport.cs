using System;
using System.Linq;
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

    /// <summary>Idea-pool trees (#132) that a species lane may turn native-only (#160).</summary>
    internal static class IdeaPool
    {
        /// <summary>
        /// The tree is a generic Awakening tree for <paramref name="stat"/> (null = any stat) at 400, or, once its species lane has
        /// landed, a native-only tree (kj, 2026-11-03): no offer stat or threshold, a native tree of exactly one species.
        /// </summary>
        public static void AssertOfferedOrNative(ContentDb db, TreeDef t, string stat)
        {
            if (t.Kind == TreeKind.Generic)
            {
                Assert.That((t.OfferStat, t.OfferThreshold), Is.EqualTo((stat, 400)), t.Id);
                return;
            }
            Assert.That(t.Kind, Is.EqualTo(TreeKind.Native), t.Id + ": generic, or native-only once its species lands");
            Assert.That(IdleRng.TestFixtures.ContentFixtures.SpeciesTrees, Does.Contain(t.Id), t.Id + ": only the 17 species trees of #160 turn native");
            Assert.That((t.OfferStat, t.OfferThreshold), Is.EqualTo(((string)null, 0)), t.Id);
            Assert.That(db.SpeciesList.Count(sp => sp.NativeTree1 == t.Id || sp.NativeTree2 == t.Id), Is.EqualTo(1), t.Id + ": the native tree of one species");
        }
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
