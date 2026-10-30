using System;
using System.IO;
using IdleRng.Runtime.Save;
using NUnit.Framework;

namespace IdleRng.Runtime.Tests
{
    /// <summary>Two-slot save stores (specs/06 section 5): rotation, crash safety, quarantine.</summary>
    public class SaveStoreTests
    {
        static byte[] B(string s) => System.Text.Encoding.ASCII.GetBytes(s);

        static string Tmp()
        {
            var d = Path.Combine(Path.GetTempPath(), "idlerng-store-" + Guid.NewGuid().ToString("N"));
            return d;
        }

        [Test]
        public void MemoryStore_Rotates_AndQuarantines()
        {
            var s = new MemorySaveStore();
            Assert.That(s.Read(SaveSlot.Primary), Is.Null);
            s.Write(B("one")); s.Write(B("two"));
            Assert.That(s.Read(SaveSlot.Primary), Is.EqualTo(B("two")));
            Assert.That(s.Read(SaveSlot.Backup), Is.EqualTo(B("one")));
            s.Quarantine(SaveSlot.Primary, 10);
            Assert.That(s.Read(SaveSlot.Primary), Is.Null);
            Assert.That(s.Quarantined, Has.Count.EqualTo(1));
            s.FailWrites = true;
            Assert.Throws<SaveWriteException>(() => s.Write(B("three")));
            Assert.That(s.Read(SaveSlot.Backup), Is.EqualTo(B("one")), "a failed write changes nothing");
        }

        [Test]
        public void FileStore_Rotates_AndRemovesAPartialTmp()
        {
            var dir = Tmp();
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "account.tmp"), B("partial"));
                var s = new FileSaveStore(dir, AccountSave.SlotName);
                Assert.That(File.Exists(s.TempPath), Is.False, "a crash mid-write left a tmp: it is deleted");
                s.Write(B("one")); s.Write(B("two")); s.Write(B("three"));
                Assert.That(s.Read(SaveSlot.Primary), Is.EqualTo(B("three")));
                Assert.That(s.Read(SaveSlot.Backup), Is.EqualTo(B("two")));
                s.Quarantine(SaveSlot.Primary, 99);
                Assert.That(s.Read(SaveSlot.Primary), Is.Null);
                Assert.That(s.QuarantinedFiles(), Has.Length.EqualTo(1));
                s.DeleteAll();
                Assert.That(s.Read(SaveSlot.Backup), Is.Null);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public void FileStore_CrashBetweenTheRenames_KeepsThePreviousSave()
        {
            var dir = Tmp();
            try
            {
                var s = new FileSaveStore(dir, AccountSave.SlotName);
                s.Write(B("good"));
                s.AfterRotate = () => throw new IOException("power cut");
                Assert.Throws<SaveWriteException>(() => s.Write(B("new")));
                var reopened = new FileSaveStore(dir, AccountSave.SlotName);
                Assert.That(reopened.Read(SaveSlot.Backup), Is.EqualTo(B("good")), "the old save survives as the backup");
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
    }
}
