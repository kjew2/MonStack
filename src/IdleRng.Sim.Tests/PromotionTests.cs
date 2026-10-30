using System.Linq;
using IdleRng.Sim.Content;
using IdleRng.Sim.Events;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Per-companion promotion (specs/11 section 6) and the party DPS formula (section 7).</summary>
    public class PromotionTests
    {
        static ContentDb Db => TestContent.Db;
        static string FirstOf(Rarity tier) => Db.HeroList.First(h => h.Tier == tier && !h.Starter).Id;

        [Test]
        public void NewProfile_StartsWithTheStarterInSlot0()
        {
            var sim = TestContent.NewSim();
            Assert.That(sim.State.Heroes.Equipped, Has.Length.EqualTo(4));
            Assert.That(sim.State.Heroes.Equipped[0], Is.EqualTo(Db.StarterHeroId));
            Assert.That(sim.Query.HeroCopies(Db.StarterHeroId), Is.EqualTo(1));
            Assert.That(sim.Query.HeroMult, Is.EqualTo(1.0).Within(1e-12));
            Assert.That(sim.Query.PromoMult, Is.EqualTo(1.0));
            sim.Validate();
        }

        [Test]
        public void Unequip_CannotEmptyTheParty()
        {
            var sim = TestContent.NewSim();
            Assert.That(sim.UnequipHero(0).Reason, Is.EqualTo(Reject.LastCompanion));
            Assert.That(sim.State.Heroes.Equipped[0], Is.EqualTo(Db.StarterHeroId));
            Assert.That(sim.State.Heroes.AutoEquipBest, Is.True, "a rejected unequip changes nothing");
            sim.Debug.GrantHeroCopies(FirstOf(Rarity.R), 1);
            int other = System.Array.FindIndex(sim.State.Heroes.Equipped, id => id != null && id != Db.StarterHeroId);
            Assert.That(sim.UnequipHero(other).Ok, Is.True);
            Assert.That(sim.UnequipHero(System.Array.IndexOf(sim.State.Heroes.Equipped, Db.StarterHeroId)).Reason, Is.EqualTo(Reject.LastCompanion));
            Assert.That(sim.Query.Dps, Is.GreaterThan(0));
            sim.Validate();
        }

        [Test]
        public void Promoted_IsPublishedAfterAutoEquipRePicks()
        {
            var box = new EventOutbox(EventKinds.All);
            var sim = TestContent.NewSim(sink: box);
            foreach (var h in Db.HeroList.Where(h => h.Tier == Rarity.C)) sim.Debug.GrantHeroCopies(h.Id, 1);   // a full party of Commons
            string leg = FirstOf(Rarity.L);
            sim.Debug.GrantHeroCopies(leg, 1);
            sim.SetAutoEquipHeroes(false);
            int slot = System.Array.IndexOf(sim.State.Heroes.Equipped, leg);
            if (slot >= 0) Assert.That(sim.UnequipHero(slot).Ok, Is.True);                                         // bench the Legendary
            sim.SetAutoEquipHeroes(true);
            Assert.That(sim.State.Heroes.Equipped, Does.Contain(leg), "auto-equip puts the strongest back");
            sim.Debug.SetStage(Db.Promotions[0].UnlockStage);
            sim.Debug.GrantGold(Db.PromotionCost(1));
            box.Drain();
            Assert.That(sim.Promote(leg).Ok, Is.True);
            var ev = box.Drain();
            Assert.That(ev.Last(), Is.InstanceOf<CompanionPromoted>(), "promotion event comes after any re-pick events");
        }

        [Test]
        public void FarmDps_IsAtkTimesCritTimesSpeed()
        {
            var h = Db.Heroes[FirstOf(Rarity.L)];
            double crit = 1 + h.CritRate * (h.CritDmg - 1);
            double expected = h.Atk * (1 + Db.Constants.HeroLvlBonus * 4) * 1.5 * 1.5 * crit * h.Spd / Db.Constants.SpdRef;
            Assert.That(Db.FarmDps(h, 5, 2), Is.EqualTo(expected).Within(1e-9));
            Assert.That(Db.FarmDps(h, 0, 0), Is.EqualTo(0));
        }

        [Test]
        public void Dps_IsPartyDpsTimesStatAndGear()
        {
            var sim = TestContent.NewSim();
            sim.Debug.GrantHeroCopies(FirstOf(Rarity.E), 1);
            sim.Debug.SetStatLevel(40);
            var q = sim.Query;
            double party = sim.State.Heroes.Equipped.Where(id => id != null).Sum(id => q.HeroFarmDps(id));
            Assert.That(q.PartyDps, Is.EqualTo(party).Within(1e-9));
            Assert.That(q.Dps, Is.EqualTo(party * q.StatMult * q.GearMult).Within(1e-9));
            Assert.That(q.BaseDps * q.HeroMult * q.PromoMult * q.StatMult * q.GearMult, Is.EqualTo(q.Dps).Within(1e-9));
        }

        [Test]
        public void Promote_Rejections()
        {
            var sim = TestContent.NewSim();
            string rare = FirstOf(Rarity.R);
            Assert.That(sim.Promote(rare).Reason, Is.EqualTo(Reject.HeroNotOwned));
            Assert.That(sim.Promote(Db.StarterHeroId).Reason, Is.EqualTo(Reject.PromotionMaxed));   // Common: cap 0
            sim.Debug.GrantHeroCopies(rare, 1);
            sim.Debug.GrantGold(1e30);
            sim.Debug.SetStage(Db.Promotions[0].UnlockStage - 1);
            Assert.That(sim.Promote(rare).Reason, Is.EqualTo(Reject.PromotionLocked));
            sim.Debug.SetStage(Db.Promotions[0].UnlockStage);
            sim.State.Currencies.Gold = Db.PromotionCost(1) * 0.999;
            Assert.That(sim.Promote(rare).Reason, Is.EqualTo(Reject.NotEnoughGold));
            Assert.That(sim.Query.Promotions(rare), Is.EqualTo(0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => sim.Promote("no_such_hero"));
        }

        [Test]
        public void Promote_ChargesGold_MultipliesStats_AndCaps()
        {
            var box = new EventOutbox(EventKinds.All);
            var sim = TestContent.NewSim(sink: box);
            string rare = FirstOf(Rarity.R);
            sim.Debug.GrantHeroCopies(rare, 1);
            sim.Debug.SetStage(Db.Promotions[0].UnlockStage);
            double cost = Db.PromotionCost(1);
            Assert.That(cost, Is.EqualTo(Db.Promotions[0].CostKills * Db.GoldPerKill(Db.Promotions[0].UnlockStage)).Within(1e-9));
            sim.Debug.GrantGold(cost);
            double before = sim.Query.HeroFarmDps(rare), atk = sim.Query.HeroAtk(rare);
            Assert.That(sim.Query.CanPromote(rare), Is.True);
            box.Drain();

            Assert.That(sim.Promote(rare).Ok, Is.True);
            Assert.That(sim.State.Currencies.Gold, Is.EqualTo(0).Within(1e-9));
            Assert.That(sim.Query.Promotions(rare), Is.EqualTo(1));
            Assert.That(sim.Query.HeroFarmDps(rare), Is.EqualTo(before * Db.Promotions[0].Mult).Within(1e-9));
            Assert.That(sim.Query.HeroAtk(rare), Is.EqualTo(atk * Db.Promotions[0].Mult).Within(1e-9));
            Assert.That(box.Drain().OfType<CompanionPromoted>().Single(), Is.EqualTo(new CompanionPromoted(sim.State.Tick, rare, 1, cost)));

            sim.Debug.GrantGold(1e30);
            sim.Debug.SetStage(Db.Promotions[2].UnlockStage);
            Assert.That(sim.Promote(rare).Reason, Is.EqualTo(Reject.PromotionMaxed));   // Rare: cap 1
            Assert.That(sim.Query.NextPromotionCost(rare), Is.EqualTo(0));
            sim.Validate();
        }

        [Test]
        public void Promote_Legendary_UpToThree_AndAutoEquipRanksByFarmDps()
        {
            var sim = TestContent.NewSim();
            string leg = FirstOf(Rarity.L);
            sim.Debug.GrantHeroCopies(leg, 1);
            sim.Debug.GrantGold(1e30);
            sim.Debug.SetStage(Db.Promotions[2].UnlockStage);
            for (int i = 0; i < 3; i++) Assert.That(sim.Promote(leg).Ok, Is.True, $"promotion {i + 1}");
            Assert.That(sim.Promote(leg).Reason, Is.EqualTo(Reject.PromotionMaxed));
            Assert.That(sim.Query.PromoMult, Is.GreaterThan(1.0));
            Assert.That(sim.State.Heroes.Equipped[0], Is.EqualTo(leg));   // strongest companion first
            sim.Validate();
        }
    }
}
