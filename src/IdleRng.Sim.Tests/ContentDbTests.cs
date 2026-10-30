using System.Collections.Generic;
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
        public void ShippedContent_Loads_AndMatchesSpec00()
        {
            var db = TestContent.Db;
            Assert.That(db.Version, Is.EqualTo("2.2"));
            Assert.That(db.ContentHash, Has.Length.EqualTo(64));
            var b = db.HeroBanner;
            Assert.That(Enumerable.Range(0, 5).Select(i => b.Pool[i].Count), Is.EqualTo(new[] { 5, 5, 4, 4, 2 }));
            Assert.That(db.Promotions.Select(p => p.UnlockStage), Is.EqualTo(new[] { 50, 150, 300 }));
            Assert.That(db.HeroTiers.Select(t => t.MaxPromotions), Is.EqualTo(new[] { 0, 1, 2, 3, 3 }));
            Assert.That(db.Constants.HeroSlots, Is.EqualTo(4));
            Assert.That(db.StarterHeroId, Is.EqualTo("hero_c_01"));
            // skeleton kits: C 1, R 2, E 3, L/M 6 skills; one skill per promotion (specs/11 section 4.4)
            var kitSize = new[] { 1, 2, 3, 6, 6 };
            foreach (var h in db.HeroList)
            {
                var kit = db.Kits[h.Id];
                Assert.That(kit.Count, Is.EqualTo(kitSize[(int)h.Tier]), h.Id);
                Assert.That(kit.Where(k => k.UnlockPromotion > 0).Select(k => k.UnlockPromotion),
                            Is.EqualTo(Enumerable.Range(1, db.MaxPromotions(h))), h.Id);
            }
            Assert.That(db.RoleList.Select(r => r.Id), Is.EqualTo(new[] { "tank", "striker", "caster", "healer", "support" }));
            Assert.That(db.ElementList.Select(e => e.Id + ">" + e.StrongVs),
                        Is.EqualTo(new[] { "fire>nature", "water>fire", "nature>water", "light>dark", "dark>light" }));
            Assert.That(db.HeroLevelThresholds, Is.EqualTo(new[] { 1, 3, 6, 10, 16, 25, 40, 64, 100, 160 }));
            Assert.That(db.GearSlotCount, Is.EqualTo(6));
            Assert.That(db.GearRarity[(int)Rarity.M].Mult, Is.EqualTo(3.0));
            Assert.That(db.Constants.TickHz, Is.EqualTo(10));
            Assert.That(db.Curves[CurveId.EnemyHp].Growth, Is.EqualTo(1.07));
            Assert.That(db.BossRuleFor(550).Id, Is.EqualTo("default"));
            Assert.That(b.BaseLplus, Is.EqualTo(0.03).Within(1e-15));
            Assert.That(b.Pool[0], Is.Ordered.Using((IComparer<string>)System.StringComparer.Ordinal));
            Assert.That(db.Text("hero.c_01.name"), Is.EqualTo("Pell the Farmhand"));
            Assert.That(db.Text("no.such.key"), Is.EqualTo("no.such.key"));
        }

        [Test]
        public void Curves_AgreeWithPow()
        {
            var db = TestContent.Db;
            for (int s = 0; s <= 1000; s += 37)
            {
                Assert.That(db.EnemyHp(s), Is.EqualTo(20 * System.Math.Pow(1.07, s)).Within(1e-9).Percent);
                Assert.That(db.GearSlotPct(s) + 1, Is.EqualTo(System.Math.Pow(1.0074, s)).Within(1e-9).Percent);
            }
            Assert.That(double.IsInfinity(db.EnemyHp(2500)), Is.False);
        }

        [Test]
        public void HashMismatch_IsRejected()
        {
            string dir = FileContentSource.FindContentDirectory(TestContext.CurrentContext.TestDirectory);
            var files = Directory.GetFiles(dir, "*.json").ToDictionary(Path.GetFileName, File.ReadAllText);
            files["heroes.json"] = files["heroes.json"].Replace("\"atk\": 10.0", "\"atk\": 11.0");
            var ex = Assert.Throws<ContentLoadException>(() => ContentDb.Load(new DictionaryContentSource(files)));
            Assert.That(ex.Errors[0].Code, Is.EqualTo("E900"));
        }

        [Test]
        public void CultureInvariant()
        {
            var prev = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var db = ContentDb.LoadFromDirectory(FileContentSource.FindContentDirectory(TestContext.CurrentContext.TestDirectory));
                Assert.That(db.Curves[CurveId.EnemyHp].Growth, Is.EqualTo(1.07));
            }
            finally { CultureInfo.CurrentCulture = prev; }
        }

        [Test]
        public void IsCompatibleWith()
        {
            var db = TestContent.Db;   // 2.2
            Assert.That(db.IsCompatibleWith("2.7"), Is.True);
            Assert.That(db.IsCompatibleWith("1.0"), Is.False);
            Assert.That(db.IsCompatibleWith(""), Is.False);
            Assert.That(db.IsCompatibleWith("a.b"), Is.False);
        }
    }
}
