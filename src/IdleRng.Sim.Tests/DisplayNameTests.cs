using System;
using IdleRng.Sim.Account;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>The display name rule (specs/28 section 6) and the setName command.</summary>
    public class DisplayNameTests
    {
        [TestCase("Ash", "Ash")]
        [TestCase("  Misty   Water  ", "Misty Water")]
        [TestCase("Red_2-Blue", "Red_2-Blue")]
        [TestCase("x\tY\nz", "x Y z")]
        [TestCase("abcdefghijklmnop", "abcdefghijklmnop")]
        public void Check_AcceptsAndNormalises(string name, string stored) => Assert.That(DisplayName.Check(name), Is.EqualTo(stored));

        [TestCase(null)]
        [TestCase("")]
        [TestCase("    ")]
        public void Check_EmptyMeansNoName(string name) => Assert.That(DisplayName.Check(name), Is.Null);

        [TestCase("Al", "3 to 16")]
        [TestCase("abcdefghijklmnopq", "3 to 16")]
        [TestCase("Ash!", "only letters")]
        [TestCase("Åsa", "only letters")]
        [TestCase("Аsh", "only letters")]   // a Cyrillic A
        [TestCase("1234", "one letter")]
        [TestCase("--_", "one letter")]
        [TestCase("Big Fuck", "not allowed")]
        [TestCase("sh1t head", "not allowed")]
        [TestCase("F-U-C-K", "not allowed")]
        [TestCase("MonStack Admin", "not allowed")]
        public void Check_RefusesWithAReason(string name, string reason)
        {
            var e = Assert.Throws<ArgumentException>(() => DisplayName.Check(name));
            Assert.That(e.Message, Does.Contain(reason));
        }

        [Test]
        public void SetName_ThroughTheDispatcher_StoresAndClears_AndARefusalChangesNothing()
        {
            var a = AccountOps.New(TestContent.Db, 1);
            AccountCommands.Apply(TestContent.Db, a, AccountCommand.Parse("setName", new AccountCommandsTests.ObjArgs(" Gary  Oak ")));
            Assert.That(a.Name, Is.EqualTo("Gary Oak"));
            Assert.Throws<ArgumentException>(() => AccountCommands.Apply(TestContent.Db, a, AccountCommand.Parse("setName", new AccountCommandsTests.ObjArgs("??"))));
            Assert.That(a.Name, Is.EqualTo("Gary Oak"));
            AccountCommands.Apply(TestContent.Db, a, AccountCommand.Parse("setName", new AccountCommandsTests.ObjArgs("")));
            Assert.That(a.Name, Is.Null);
            AccountCommands.Apply(TestContent.Db, a, AccountCommand.Parse("setName", new AccountCommandsTests.ObjArgs()));
            Assert.That(a.Name, Is.Null, "no argument clears it too");
            Assert.That(AccountCommands.Writes(CommandKind.SetName), Is.EqualTo(AccountPart.Progress));
            Assert.That(AccountCommands.Reads(CommandKind.SetName), Is.EqualTo(AccountPart.None));
        }
    }
}
