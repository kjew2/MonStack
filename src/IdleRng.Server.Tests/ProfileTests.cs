using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Account;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Server.Tests
{
    /// <summary>The profile projection (specs/27 section 3.3): written by accepted commits, built from the real parts, read by others.</summary>
    public class ProfileTests
    {
        static Sim.Content.ContentDb Db => Content.Db;
        const string P = Rig.Player;

        static PlayerProfile Stored(Rig rig) => rig.Profiles.ReadAsync(P).Result;

        /// <summary>The server's profile equals the one built from the client's account at the same time.</summary>
        static void AssertMatchesClient(Rig rig, Device a, string because)
        {
            Assert.That(Stored(rig)?.ToJson(), Is.EqualTo(Profiles.Of(Db, a.Acct, rig.Now).ToJson()), because);
        }

        [Test]
        public void AProfile_OfANewAccount_ShowsNothingYet()
        {
            var p = Profiles.Of(Db, AccountOps.New(Db, 1), 5_000);
            Assert.That((p.Name, p.BestGrade, p.BestRank, p.BestDungeonFloor, p.Achievements, p.Showcase, p.AtSec), Is.EqualTo(((string)null, (string)null, -1, 0, 0, (VaultEntry)null, 5L)));
            Assert.That(PlayerProfile.FromJson(p.ToJson()).ToJson(), Is.EqualTo(p.ToJson()), "round trip");
        }

        [Test]
        public void TheShowcase_IsTheBestGrade_ThenTheGauntletFloor_ThenTheEarliestSealed()
        {
            var a = AccountOps.New(Db, 1);
            Assert.That(Profiles.ShowcaseOf(a), Is.Null);
            VaultEntry E(int id, int rank, int floor) => new VaultEntry { Id = id, RankIndex = rank, GauntletFloor = floor };
            a.Vault.AddRange(new[] { E(4, 2, 3), E(1, 3, 1), E(7, 3, 5), E(2, 3, 5), E(9, 1, 9) });
            Assert.That(Profiles.ShowcaseOf(a).Id, Is.EqualTo(2));
        }

        [Test]
        public void AcceptedCommits_WriteTheProfile_FromTheRealParts_NeverAStandIn()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            Assert.That(Stored(rig), Is.Null, "an open writes no profile");
            a.Do("setLoadout", new List<string>());
            Assert.That(a.Commit().Accepted, Is.True);
            Assert.That(Stored(rig), Is.Null, "a commit that changes nothing it shows writes none");

            a.Do("setName", "Misty");
            Assert.That(a.Commit().Accepted, Is.True);
            AssertMatchesClient(rig, a, "setName");
            Assert.That(Stored(rig).Name, Is.EqualTo("Misty"));

            a.SealOne(rig.Now);
            AssertMatchesClient(rig, a, "finishRun");
            var p = Stored(rig);
            Assert.That(p.BestGrade, Is.EqualTo(a.Acct.Vault[0].Grade));
            Assert.That(p.Showcase.Id, Is.EqualTo(a.Acct.Vault[0].Id));
            Assert.That(p.Name, Is.EqualTo("Misty"), "the name is read from the progress part, not a new account's");

            // runTower writes the dungeon part but does not declare progress: the profile still has the name and achievements.
            rig.Now += 1000;
            a.Do("runTower", AccountServer.DayOf(rig.Now), new List<int> { 0 });
            Assert.That(a.Commit().Accepted, Is.True);
            AssertMatchesClient(rig, a, "runTower");
            Assert.That(Stored(rig).BestDungeonFloor, Is.EqualTo(a.Acct.Tower.ClearedEver));
            Assert.That(Stored(rig).Name, Is.EqualTo("Misty"));

            rig.Now += 1000;
            a.Do("lock", 0, true);
            Assert.That(a.Commit().Accepted, Is.True);
            Assert.That(Stored(rig).Showcase.Locked, Is.False, "the lock is the player's, not the monster's");
            Assert.That(Stored(rig).ToJson(), Does.Not.Contain("Locked"));

            // A refused commit writes nothing.
            long at = Stored(rig).AtSec;
            rig.Now += 60_000;
            a.Send("dismantle", 0);   // locked
            Assert.That(a.Commit().Code, Is.EqualTo(Rejection.Refused));
            Assert.That(Stored(rig).AtSec, Is.EqualTo(at));

            a.Open();
            a.Do("lock", 0, false);
            a.Do("dismantle", 0);
            Assert.That(a.Commit().Accepted, Is.True);
            AssertMatchesClient(rig, a, "dismantle");
            Assert.That(Stored(rig).Showcase, Is.Null, "the Vault is empty");
            Assert.That(Stored(rig).BestGrade, Is.Not.Null, "the best grade is a record, kept");
        }

        [Test]
        public void ARead_FillsInTheGeneratedName_ForAPlayerWithNone()
        {
            var rig = new Rig();
            var a = rig.Device("a");
            a.Open();
            Assert.That(rig.Server.Profiles.ReadAsync(P).Result, Is.Null);
            a.SealOne(rig.Now);
            Assert.That(Stored(rig).Name, Is.Null, "stored as the account holds it");
            Assert.That(rig.Server.Profiles.ReadAsync(P).Result.Name, Is.EqualTo(EndlessBoard.FallbackName(P)));
            Assert.ThrowsAsync<System.ArgumentException>(() => rig.Server.Profiles.ReadAsync(""));
        }

        [Test]
        public void AServerWithoutProfiles_LoadsOnlyTheDeclaredParts_AndOneWithThemLoadsWhatTheProfileShows()
        {
            foreach (bool profiles in new[] { false, true })
            {
                var rig = new Rig(profiles);
                Assert.That(rig.Server.Profiles == null, Is.EqualTo(!profiles));
                var a = rig.Device("a");
                a.Open();
                a.Do("setName", "Gary");
                var h = rig.Store.Head(P);   // the keys the commit loads from
                rig.Store.ClearLog();
                Assert.That(a.Commit().Accepted, Is.True);
                var expected = profiles ? new[] { h.Parts["dungeon"], h.Parts["progress"], h.Parts["vault"] } : new[] { h.Parts["progress"] };
                Assert.That(rig.Store.Read.OrderBy(k => k), Is.EqualTo(expected.OrderBy(k => k)), "profiles: " + profiles);
                Assert.That(Stored(rig), profiles ? Is.Not.Null : Is.Null);
            }
        }
    }
}
