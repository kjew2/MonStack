using System.Linq;
using System;
using System.Collections.Generic;
using IdleRng.Runtime.Save;
using IdleRng.Runtime.Services;
using IdleRng.Sim.Rng;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace IdleRng.Runtime.Tests
{
    public class MigrationTests
    {
        /// <summary>
        /// A fake v0 save: the pre-envelope dev format. Bare GameState at the root, seeds as plain JSON integers
        /// (some above 2^63), "SchemaVersion": 0 and a root LastSavedAtServerSec.
        /// </summary>
        static (byte[] file, string expectedState) FakeV0(long lastSaved)
        {
            var sim = TestContent.PlayedSim(seed: 0xFEDCBA9876543210UL);
            var json = JObject.Parse(JsonConvert.SerializeObject(sim.State, new StringEnumConverter()));
            json["SchemaVersion"] = 0;
            json["LastSavedAtServerSec"] = lastSaved;
            Assert.That(json["Rng"]["ProfileSeed"].Type, Is.EqualTo(JTokenType.Integer), "v0 seeds are numbers");
            return (SaveSerializer.EncodeBody(json.ToString()), TestContent.Dump(sim.State));
        }

        [Test]
        public void StepCount_MatchesSchemaRange() =>
            Assert.That(SaveMigrations.Steps.Count, Is.EqualTo(SaveSchema.Current - SaveSchema.MinSupported));

        [Test]
        public void V0Blob_LoadsThroughMigration_IntoIdenticalState()
        {
            var (file, expected) = FakeV0(lastSaved: TestContent.T0 + 1234);
            var store = new MemorySaveStore { Primary = file };
            var analytics = new MemoryAnalytics();

            var r = SaveSystem.Load(store, TestContent.Db, TestContent.T0 + 5000, analytics);

            Assert.That(r.Status, Is.EqualTo(LoadStatus.Loaded), string.Join("; ", r.Problems));
            Assert.That(r.FromSchemaVersion, Is.EqualTo(0));
            Assert.That(r.Meta.LastSavedAtServerSec, Is.EqualTo(TestContent.T0 + 1234));
            Assert.That(r.State.SchemaVersion, Is.EqualTo(SaveSchema.Current));
            Assert.That(TestContent.Dump(r.State), Is.EqualTo(expected));
            Assert.That(r.State.Rng.ProfileSeed, Is.EqualTo(0xFEDCBA9876543210UL));
            Assert.That(analytics.Count("migration_applied"), Is.EqualTo(SaveSchema.Current - 0));   // one per step from v0
        }

        /// <summary>A hand-built v1 body: content 1.0 hero ids, a 6-slot party and two class advances.</summary>
        static JObject FakeV1()
        {
            var sim = TestContent.PlayedSim(seed: 99);
            var state = JObject.Parse(JsonConvert.SerializeObject(sim.State, new StringEnumConverter()));
            state["SchemaVersion"] = 1;
            state["ContentVersion"] = "1.0";
            var heroes = (JObject)state["Heroes"];
            heroes.Remove("Promotions");
            heroes["Copies"] = new JObject { ["hero_c_01"] = 3, ["hero_c_17"] = 4, ["hero_r_02"] = 2, ["hero_l_06"] = 1, ["hero_e_03"] = 1 };
            heroes["Equipped"] = new JArray("hero_l_06", "hero_r_02", null, "hero_c_17", "hero_e_03", "hero_c_01");
            ((JObject)state["Progression"])["ClassAdvances"] = 2;
            state["Currencies"]["Shards"] = 10;
            return new JObject { ["SchemaVersion"] = 1, ["Meta"] = new JObject { ["LastSavedAtServerSec"] = 0 }, ["State"] = state };
        }

        [Test]
        public void V1ToV2_RemovedHeroesBecomeShards_PartyCutToFour_ClassBecomesPromotions()
        {
            var v2 = SaveMigrations.Migrate(1, FakeV1());
            var st = (JObject)v2["State"];
            Assert.That((int)v2["SchemaVersion"], Is.EqualTo(2));
            Assert.That((string)st["ContentVersion"], Is.EqualTo("2.0"));
            var copies = (JObject)st["Heroes"]["Copies"];
            Assert.That(copies.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "hero_c_01", "hero_r_02", "hero_e_03" }));
            Assert.That((long)st["Currencies"]["Shards"], Is.EqualTo(10 + 4 * 1 + 1 * 100));
            Assert.That(st["Heroes"]["Equipped"].Select(t => (string)t), Is.EqualTo(new[] { "hero_r_02", "hero_e_03", "hero_c_01", null }));
            var promos = (JObject)st["Heroes"]["Promotions"];
            Assert.That((int)promos["hero_r_02"], Is.EqualTo(1));   // capped at Rare's 1
            Assert.That((int)promos["hero_e_03"], Is.EqualTo(2));
            Assert.That(promos["hero_c_01"], Is.Null);               // Common cannot promote
            Assert.That(st["Progression"]["ClassAdvances"], Is.Null);

            // and it materialises into a valid sim
            var state = st.ToObject<IdleRng.Sim.State.GameState>(JsonSerializer.Create(new JsonSerializerSettings { Converters = { new StringEnumConverter() } }));
            var sim = new IdleRng.Sim.Simulation(TestContent.Db, state);
            sim.Validate();
            Assert.That(sim.Query.PromoMult, Is.GreaterThan(1.0));
        }

        [Test]
        public void V1ToV2_NothingKept_GrantsTheStarter()
        {
            var v1 = FakeV1();
            var heroes = (JObject)v1["State"]["Heroes"];
            heroes["Copies"] = new JObject { ["hero_c_17"] = 1 };
            heroes["Equipped"] = new JArray("hero_c_17", null, null, null, null, null);
            var st = SaveMigrations.Migrate(1, v1)["State"];
            Assert.That((int)st["Heroes"]["Copies"]["hero_c_01"], Is.EqualTo(1));
            Assert.That(st["Heroes"]["Equipped"].Select(t => (string)t), Is.EqualTo(new[] { "hero_c_01", null, null, null }));
        }

        [Test]
        public void V2SaveWithoutBattleStream_LoadsAndGetsTheStream()
        {
            // saves written before the battle stream existed (#61) have only the five original streams
            var sim = TestContent.PlayedSim(seed: 5);
            sim.State.Rng.Streams.Remove(IdleRng.Sim.Rng.StreamNames.Battle);
            var store = new MemorySaveStore { Primary = SaveSerializer.Encode(sim.State, new SaveMeta { LastSavedAtServerSec = TestContent.T0 }) };
            var r = SaveSystem.Load(store, TestContent.Db, TestContent.T0 + 10, new MemoryAnalytics());
            Assert.That(r.Status, Is.EqualTo(LoadStatus.Loaded), string.Join("; ", r.Problems));
            var loaded = new IdleRng.Sim.Simulation(TestContent.Db, r.State);
            Assert.That(r.State.Rng.Streams.ContainsKey(IdleRng.Sim.Rng.StreamNames.Battle), Is.True);
            loaded.Validate();
        }

        [Test]
        public void V0ToV1_ProducesV1Envelope_WithHexSeeds()
        {
            var (file, _) = FakeV0(lastSaved: 42);
            var (v, body) = SaveSerializer.Decode(file);
            Assert.That(v, Is.EqualTo(0));
            var v1 = SaveMigrations.Migrate(v, body, SaveMigrations.Steps.Take(1).ToList(), 0, 1);   // just the v0 -> v1 step
            Assert.That((int)v1["SchemaVersion"], Is.EqualTo(1));
            Assert.That((long)v1["Meta"]["LastSavedAtServerSec"], Is.EqualTo(42));
            Assert.That((string)v1["State"]["Rng"]["ProfileSeed"], Is.EqualTo("fedcba9876543210"));
            Assert.That((string)v1["State"]["Rng"]["Streams"][StreamNames.GachaHero]["Seed"], Has.Length.EqualTo(16));
            Assert.That(v1["State"]["LastSavedAtServerSec"], Is.Null);
        }

        [Test]
        public void V0WithoutAnchor_FallsBackToLastWallClockSync()
        {
            var sim = TestContent.PlayedSim();
            var json = JObject.Parse(JsonConvert.SerializeObject(sim.State, new StringEnumConverter()));
            json["SchemaVersion"] = 0;
            var v1 = SaveMigrations.Migrate(0, json);
            Assert.That((long)v1["Meta"]["LastSavedAtServerSec"], Is.EqualTo(sim.State.Clock.LastSyncServerMs / 1000));
        }

        [Test]
        public void TooNew_Throws_AndTouchesNothing()
        {
            var good = SaveSerializer.Encode(TestContent.NewSim().State, new SaveMeta());
            var body = SaveSerializer.Decode(good).body;
            body["SchemaVersion"] = SaveSchema.Current + 1;
            var tooNew = SaveSerializer.EncodeBody(body.ToString());
            var store = new MemorySaveStore { Primary = tooNew, Backup = good };

            Assert.Throws<SaveTooNewException>(() => SaveSystem.Load(store, TestContent.Db, TestContent.T0));
            Assert.That(store.Primary, Is.EqualTo(tooNew));
            Assert.That(store.Backup, Is.EqualTo(good));
            Assert.That(store.Quarantined, Is.Empty);
        }

        [Test]
        public void Driver_RunsStepsInOrder_AndStampsVersions()
        {
            var seen = new List<string>();
            var steps = new Func<JObject, JObject>[]
            {
                j => { j["a"] = 1; return j; },
                j => { j["b"] = 2; return j; },
                j => { j["c"] = (int)j["b"] + 1; return j; },
            };
            var outJson = SaveMigrations.Migrate(1, new JObject { ["SchemaVersion"] = 1 }, steps, minSupported: 0, current: 3,
                                                 onStep: (f, t) => seen.Add($"{f}->{t}"));
            Assert.That((int)outJson["SchemaVersion"], Is.EqualTo(3));
            Assert.That(outJson["a"], Is.Null, "step 0 (v0->v1) must not run for a v1 input");
            Assert.That((int)outJson["c"], Is.EqualTo(3));
            Assert.That(seen, Is.EqualTo(new[] { "1->2", "2->3" }));
        }

        [Test]
        public void Driver_RejectsBrokenChainAndTooOld()
        {
            var steps = new Func<JObject, JObject>[] { j => j };
            Assert.Throws<InvalidOperationException>(() => SaveMigrations.Migrate(0, new JObject(), steps, 0, 2));
            Assert.Throws<SaveInvalidException>(() => SaveMigrations.Migrate(-1, new JObject(), steps, 0, 1));
        }
    }
}
