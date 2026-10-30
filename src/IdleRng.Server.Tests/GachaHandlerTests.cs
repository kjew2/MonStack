using System;
using System.Linq;
using IdleRng.Server;
using IdleRng.Sim;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using IdleRng.Sim.Tests;
using NUnit.Framework;

namespace IdleRng.Server.Tests
{
    public class GachaHandlerTests
    {
        static readonly Lazy<ContentDb> _db = new Lazy<ContentDb>(() =>
            ContentDb.LoadFromDirectory(FileContentSource.FindContentDirectory(TestContext.CurrentContext.TestDirectory)
                                        ?? throw new InvalidOperationException("Content/manifest.json not found")));

        static ContentDb Db => _db.Value;
        static BannerDef Banner => Db.HeroBanner;
        const long Now = 1_800_000_000;

        static GachaProfile Profile(long gems = 0, int daily = 0, int banked = 0) =>
            GachaProfile.New(StreamNames.StreamSeed(7, StreamNames.GachaHero), new ProfileLedger(gems, daily, banked, 0));

        static PullRequest Req(int count, PaidWith pay, string key = "k1") => new PullRequest(Banner.Id, count, pay, key);

        [Test]
        public void Handlers_MatchVectors()
        {
            var f = GachaVectorFile.LoadFromRepo(TestContext.CurrentContext.TestDirectory);
            Assert.That(f.ContentHash, Is.EqualTo(Db.ContentHash), "content changed: " + GachaVectorFile.Regenerate);
            long cost = Db.Banners[f.BannerId].PullCostGems;
            foreach (var v in f.Vectors)
            {
                var p = GachaProfile.New(v.StreamSeed, new ProfileLedger(cost * f.PullsPerSeed, 0, 0, 0));
                int pulls = 0;
                for (int b = 0; b < v.Batches.Length; b++)
                {
                    var exp = v.Batches[b];
                    var h = GachaHandlers.Pull(Db, p, new PullRequest(f.BannerId, exp.Length, PaidWith.Gems, $"{v.ProfileSeed}/{b}"), Now);
                    Assert.That(h.Response.Ok, Is.True, h.Response.Error.ToString());
                    for (int i = 0; i < exp.Length; i++)
                    {
                        string at = $"profileSeed {v.ProfileSeed}, pull {pulls + i + 1}";
                        Assert.That(h.Response.Results[i].Tier, Is.EqualTo(exp[i].Tier), at);
                        Assert.That(h.Response.Results[i].Hero, Is.EqualTo(exp[i].HeroId), at);
                    }
                    pulls += exp.Length;
                    Assert.That(h.Response.Banner.PityCounter, Is.EqualTo(exp[exp.Length - 1].PityAfter), $"profileSeed {v.ProfileSeed}, batch {b}");
                    p = h.State;
                }
                Assert.That(p.Ledger.Gems, Is.EqualTo(0));
                Assert.That(p.Banner.TotalPulls, Is.EqualTo(f.PullsPerSeed));
                Assert.That(p.Rng.Counter, Is.EqualTo((ulong)(3 * f.PullsPerSeed)));
            }
        }

        [Test]
        public void ReplayedKey_ReturnsStoredResponse_NoDebitNoRoll()
        {
            var first = GachaHandlers.Pull(Db, Profile(gems: 5000), Req(10, PaidWith.Gems), Now);
            Assert.That(first.Response.Ok && !first.Replayed);
            Assert.That(first.State.Ledger.Gems, Is.EqualTo(5000 - 10 * Banner.PullCostGems));

            var again = GachaHandlers.Pull(Db, first.State, Req(10, PaidWith.Gems), Now + 60);
            Assert.That(again.Replayed, Is.True);
            Assert.That(again.Response, Is.SameAs(first.Response));
            Assert.That(again.State, Is.SameAs(first.State));

            // Even a different body under the same key replays (the key names the intent).
            var other = GachaHandlers.Pull(Db, first.State, Req(1, PaidWith.Gems), Now + 120);
            Assert.That(other.Replayed && ReferenceEquals(other.Response, first.Response));
        }

        [Test]
        public void ExpiredKey_IsANewPull()
        {
            var first = GachaHandlers.Pull(Db, Profile(gems: 5000), Req(1, PaidWith.Gems), Now);
            var late = GachaHandlers.Pull(Db, first.State, Req(1, PaidWith.Gems), Now + GachaHandlers.IdempotencyTtlSec);
            Assert.That(late.Replayed, Is.False);
            Assert.That(late.State.Banner.TotalPulls, Is.EqualTo(2));
            Assert.That(late.State.Ledger.Gems, Is.EqualTo(5000 - 2 * Banner.PullCostGems));
            Assert.That(late.State.Idempotency.Count, Is.EqualTo(1), "expired key pruned, new one stored");
        }

        [Test]
        public void Handler_DoesNotMutateInput()
        {
            var p = Profile(gems: 5000);
            var h = GachaHandlers.Pull(Db, p, Req(10, PaidWith.Gems), Now);
            Assert.That(h.State.Copies.Values.Sum(), Is.EqualTo(10));
            Assert.That(p.Copies.Count, Is.Zero);
            Assert.That(p.Idempotency.Count, Is.Zero);
            Assert.That(p.Rng.Counter, Is.Zero);
            Assert.That(p.Ledger.Gems, Is.EqualTo(5000));
        }

        [Test]
        public void PaymentPriority_DailyThenBankedThenGems()
        {
            var p = Profile(gems: 1000, daily: 1, banked: 1);
            var mismatch = GachaHandlers.Pull(Db, p, Req(1, PaidWith.Gems), Now);
            Assert.That(mismatch.Response.HttpStatus, Is.EqualTo(409));
            Assert.That(mismatch.State, Is.SameAs(p));

            p = GachaHandlers.Pull(Db, p, Req(1, PaidWith.FreeDaily, "a"), Now).State;
            p = GachaHandlers.Pull(Db, p, Req(1, PaidWith.FreeBanked, "b"), Now).State;
            p = GachaHandlers.Pull(Db, p, Req(1, PaidWith.Gems, "c"), Now).State;
            Assert.That(p.Ledger, Is.EqualTo(new ProfileLedger(1000 - Banner.PullCostGems, 0, 0, p.Ledger.Shards)));
            Assert.That(p.Banner.TotalPulls, Is.EqualTo(3));
        }

        [Test]
        public void Rejections_ChangeNothing_AndAreNotStored()
        {
            var p = Profile(gems: 50);
            var broke = GachaHandlers.Pull(Db, p, Req(1, PaidWith.Gems), Now);
            Assert.That(broke.Response.HttpStatus, Is.EqualTo(402));
            Assert.That(broke.Response.Error, Is.EqualTo(PullError.CannotAfford));
            Assert.That(broke.State, Is.SameAs(p));
            Assert.That(broke.State.Idempotency, Is.Empty);

            Assert.That(GachaHandlers.Pull(Db, p, new PullRequest("nope", 1, PaidWith.Gems, "k"), Now).Response.Error, Is.EqualTo(PullError.BadBanner));
            Assert.That(GachaHandlers.Pull(Db, p, Req(3, PaidWith.Gems), Now).Response.Error, Is.EqualTo(PullError.BadCount));
            Assert.That(GachaHandlers.Pull(Db, p, Req(1, PaidWith.Gems, ""), Now).Response.HttpStatus, Is.EqualTo(400));

            // The same key succeeds once the ledger can pay.
            var paid = GachaHandlers.Pull(Db, p with { Ledger = p.Ledger with { Gems = 100 } }, Req(1, PaidWith.Gems), Now);
            Assert.That(paid.Response.Ok && !paid.Replayed);
        }

        [Test]
        public void NoBaasDependency()
        {
            var refs = typeof(GachaHandlers).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            Assert.That(refs.Where(n => !(n.StartsWith("System") || n == "netstandard" || n == "mscorlib")), Is.Empty,
                "IdleRng.Server references: " + string.Join(", ", refs));
        }
    }
}
