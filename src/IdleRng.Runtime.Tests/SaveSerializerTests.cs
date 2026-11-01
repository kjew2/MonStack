using System;
using System.Text;
using IdleRng.Runtime.Save;
using IdleRng.Sim.Account;
using NUnit.Framework;

namespace IdleRng.Runtime.Tests
{
    /// <summary>The save file format (specs/06 section 4): header line, CRC, length, and the JSON conventions.</summary>
    public class SaveSerializerTests
    {
        static byte[] Good() => AccountSave.Encode(AccountOps.New(TestContent.Db, 5));

        static void Corrupt(Func<byte[], byte[]> mutate) =>
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Decode(mutate(Good())));

        static int HeaderEnd(byte[] f) => Array.IndexOf(f, (byte)'\n');

        [Test] public void Header_FlippedBodyByte_Corrupt() => Corrupt(f => { f[HeaderEnd(f) + 10] ^= 0x20; return f; });
        [Test] public void Header_TruncatedByOne_Corrupt() => Corrupt(f => f.AsSpan(0, f.Length - 1).ToArray());
        [Test] public void Header_TrailingByte_Corrupt() => Corrupt(f => { var g = new byte[f.Length + 1]; f.CopyTo(g, 0); g[^1] = (byte)' '; return g; });
        [Test] public void Header_NoNewline_Corrupt() => Corrupt(f => Encoding.ASCII.GetBytes("IDLERNG-SAVE fmt=json crc32=00000000 len=2"));
        [Test] public void Header_Garbage_Corrupt() => Corrupt(f => new byte[] { 0x00, 0xFF, 0x13, 0x37 });
        [Test] public void Header_Empty_Corrupt() => Corrupt(f => Array.Empty<byte>());

        [Test]
        public void Header_ValidCrcButNotJson_Corrupt() =>
            Corrupt(f => SaveSerializer.EncodeBody("{ \"SchemaVersion\": 1, \"Account\": "));

        [Test]
        public void Header_ReadsBackTheVersion()
        {
            var (version, body) = SaveSerializer.Decode(Good());
            Assert.That(version, Is.EqualTo(AccountState.CurrentSchemaVersion));
            Assert.That((string)body["Kind"], Is.EqualTo(AccountSave.Kind));
        }

        [TestCase(0UL)]
        [TestCase(ulong.MaxValue)]
        [TestCase(0x9f86d081884c7d65UL)]
        public void UlongSeeds_AreHexStrings_AndRoundTripExactly(ulong value)
        {
            var a = AccountOps.New(TestContent.Db, value);
            var (_, body) = SaveSerializer.Decode(AccountSave.Encode(a));
            if (value == 0) Assert.That(body["Account"]["Seed"], Is.Null, "a seed of 0 is the starting value, so it is left out");
            else Assert.That((string)body["Account"]["Seed"], Is.EqualTo(value.ToString("x16")));
            Assert.That(AccountSave.Decode(TestContent.Db, AccountSave.Encode(a)).Seed, Is.EqualTo(value));
        }

        [Test]
        public void EnumsAreStrings_AndDoublesExact()
        {
            var a = AccountOps.New(TestContent.Db, 3);
            a.Shards = 1234567890123;
            var json = Encoding.UTF8.GetString(AccountSave.Encode(a));
            StringAssert.Contains("\"Kind\": \"monster_account\"", json);
            Assert.That(AccountSave.Decode(TestContent.Db, AccountSave.Encode(a)).Shards, Is.EqualTo(1234567890123));
        }
    }
}
