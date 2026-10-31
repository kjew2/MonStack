using System;
using System.Globalization;
using System.IO;
using System.Linq;
using IdleRng.Sim.Content;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    public class ContentDbTests
    {
        [Test]
        public void ShippedContent_Loads_AndHasEveryTable()
        {
            var db = TestContent.Db;
            Assert.That(db.Version, Is.EqualTo("3.17"));
            Assert.That(db.ContentHash, Has.Length.EqualTo(64));
            Assert.That(db.ElementList, Has.Count.EqualTo(5));
            Assert.That(db.SpeciesList, Is.Not.Empty);
            Assert.That(db.FightRanks, Is.Not.Empty);
            Assert.That(db.DrillTiers.Select(t => t.Tier), Is.EqualTo(new[] { DrillTier.Fail, DrillTier.Success, DrillTier.Super }));
            Assert.That(db.StatMilestones, Is.EqualTo(new[] { 300, 500, 700, 900 }));
            Assert.That(db.TreeList, Is.Not.Empty);
            Assert.That(db.TalentList, Is.Not.Empty);
            Assert.That(db.AchievementList, Is.Not.Empty);
            Assert.That(db.VendorRarities, Is.Not.Empty);
            Assert.That(db.GauntletEnemies, Is.Not.Empty);
            Assert.That(db.GauntletBosses, Is.Not.Empty);
            Assert.That(db.LegacyTitles[0].MinFloor, Is.EqualTo(1));
            Assert.That(db.TowerRegulars, Is.Not.Empty);
            Assert.That(db.TowerMarquees, Is.Not.Empty);
            Assert.That(db.BookDrops, Is.Not.Empty);
            Assert.That(db.Constants.TurnTicks, Is.GreaterThan(0));
        }

        [Test]
        public void ElementWheel_StrongWeakNeutral()
        {
            var db = TestContent.Db;
            Assert.That(db.ElementMult("fire", "nature"), Is.EqualTo(db.Constants.ElementStrongMult));
            Assert.That(db.ElementMult("nature", "fire"), Is.EqualTo(db.Constants.ElementWeakMult));
            Assert.That(db.ElementMult("light", "dark"), Is.EqualTo(db.Constants.ElementStrongMult));
            Assert.That(db.ElementMult("dark", "light"), Is.EqualTo(db.Constants.ElementStrongMult));
            Assert.That(db.ElementMult("fire", "light"), Is.EqualTo(1.0));
            Assert.That(db.ElementMult(null, "fire"), Is.EqualTo(1.0));
        }

        [Test]
        public void EveryNameKey_HasText()
        {
            var db = TestContent.Db;
            var keys = db.SpeciesList.Select(s => s.NameKey)
                .Concat(db.FightRanks.Select(r => r.NameKey)).Concat(db.TreeList.Select(t => t.NameKey))
                .Concat(db.TalentList.SelectMany(t => new[] { t.NameKey, t.DescKey }))
                .Concat(db.GauntletEnemies.Concat(db.GauntletBosses).Select(e => e.NameKey))
                .Concat(db.LegacyTitles.Select(t => t.TitleKey)).Concat(db.SkillBooks.Values.Select(b => b.NameKey))
                .Concat(db.AchievementList.Select(d => d.NameKey)).Concat(db.VendorRarities.Select(r => r.NameKey));
            foreach (var k in keys) Assert.That(db.Strings.ContainsKey(k), Is.True, k);
        }

        [Test]
        public void HashMismatch_IsRejected()
        {
            string dir = FileContentSource.FindContentDirectory(TestContext.CurrentContext.TestDirectory);
            var files = Directory.GetFiles(dir, "*.json").ToDictionary(Path.GetFileName, File.ReadAllText);
            files["species.json"] = files["species.json"].Replace("\"base_hp\": 140.0", "\"base_hp\": 141.0");
            Assert.That(files["species.json"], Is.Not.EqualTo(File.ReadAllText(Path.Combine(dir, "species.json"))), "the test edit applied");
            var ex = Assert.Throws<ContentLoadException>(() => ContentDb.Load(new DictionaryContentSource(files)));
            Assert.That(ex.Errors[0].Code, Is.EqualTo("E900"));
        }

        [Test]
        public void ChaseTreeOwnership_IsValidated()
        {
            string dir = FileContentSource.FindContentDirectory(TestContext.CurrentContext.TestDirectory);
            var files = Directory.GetFiles(dir, "*.json").ToDictionary(Path.GetFileName, File.ReadAllText);
            // Drake's in-run chase tree becomes sprite's book-gated one: owned twice, needs a book, and Unbreakable Will is orphaned.
            files["species.json"] = files["species.json"].Replace("\"chase_tree_run\": \"unbreakable_will\"", "\"chase_tree_run\": \"arcane_overflow\"");
            var ex = Assert.Throws<ContentLoadException>(() => ContentDb.Load(new DictionaryContentSource(files), verifyHashes: false));
            var msgs = ex.Errors.Where(e => e.Code == "E043").Select(e => e.Location + ": " + e.Message).ToList();
            Assert.That(msgs, Has.Some.Contains("already belongs to species"));
            Assert.That(msgs, Has.Some.Contains("in-run chase tree 'arcane_overflow' must not need a book tree"));
            Assert.That(msgs, Has.Some.Contains("trees[unbreakable_will]: a chase tree must belong to a species"));
        }

        [Test]
        public void SpeciesUnlocks_AreValidated()
        {
            string dir = FileContentSource.FindContentDirectory(TestContext.CurrentContext.TestDirectory);
            var files = Directory.GetFiles(dir, "*.json").ToDictionary(Path.GetFileName, File.ReadAllText);
            // The achievement that unlocked drake now names the starter: drake has no way in, and the starter cannot be unlocked.
            files["achievements.json"] = files["achievements.json"].Replace("\"unlocks_species\": \"drake\"", "\"unlocks_species\": \"slime\"");
            var ex = Assert.Throws<ContentLoadException>(() => ContentDb.Load(new DictionaryContentSource(files), verifyHashes: false));
            var msgs = ex.Errors.Select(e => e.Code + " " + e.Location + ": " + e.Message).ToList();
            Assert.That(msgs, Has.Some.Contains("E043 achievements[rank_c]: 'slime' is the starter species"));
            Assert.That(msgs, Has.Some.Contains("E044 species[drake]: only the first species is owned from the start"));
        }

        [Test]
        public void TalentRarity_MustBeAVendorTier()
        {
            string dir = FileContentSource.FindContentDirectory(TestContext.CurrentContext.TestDirectory);
            var files = Directory.GetFiles(dir, "*.json").ToDictionary(Path.GetFileName, File.ReadAllText);
            files["talents.json"] = files["talents.json"].Replace("\"rarity\": \"epic\"", "\"rarity\": \"mythic\"");
            var ex = Assert.Throws<ContentLoadException>(() => ContentDb.Load(new DictionaryContentSource(files), verifyHashes: false));
            var msgs = ex.Errors.Select(e => e.Code + " " + e.Location + ": " + e.Message).ToList();
            Assert.That(msgs, Has.Some.Contains("E023 talents[wide_scouting]: rarity 'mythic' is not a vendor_rarities id"));
        }

        [Test]
        public void CultureInvariant()
        {
            var prev = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var db = ContentDb.LoadFromDirectory(FileContentSource.FindContentDirectory(TestContext.CurrentContext.TestDirectory));
                Assert.That(db.Constants.ElementStrongMult, Is.EqualTo(1.3));
            }
            finally { CultureInfo.CurrentCulture = prev; }
        }

        [Test]
        public void IsCompatibleWith()
        {
            var db = TestContent.Db;   // 3.4
            Assert.That(db.IsCompatibleWith("3.0"), Is.True);
            Assert.That(db.IsCompatibleWith("3.1"), Is.True);
            Assert.That(db.IsCompatibleWith("2.7"), Is.False);
            Assert.That(db.IsCompatibleWith(""), Is.False);
            Assert.That(db.IsCompatibleWith("a.b"), Is.False);
        }
    }
}
