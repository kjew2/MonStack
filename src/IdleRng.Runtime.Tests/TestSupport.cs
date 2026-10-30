using System;
using IdleRng.Sim.Content;
using NUnit.Framework;

namespace IdleRng.Runtime.Tests
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
}
