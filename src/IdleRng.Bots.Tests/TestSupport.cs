using System;
using IdleRng.Sim.Content;
using NUnit.Framework;

namespace IdleRng.Bots.Tests
{
    internal static class Content
    {
        static readonly Lazy<ContentDb> _db = new Lazy<ContentDb>(() =>
            ContentDb.LoadFromDirectory(FileContentSource.FindContentDirectory(TestContext.CurrentContext.TestDirectory)
                ?? throw new InvalidOperationException("Content/manifest.json not found above the test directory")));

        public static ContentDb Db => _db.Value;
    }
}
