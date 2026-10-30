using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Newtonsoft.Json.Linq;

namespace IdleRng.Runtime.Save
{
    /// <summary>
    /// Forward-only JSON migrations (specs/06 section 8). Steps operate on the body JObject before it is
    /// materialised, so the C# types only ever describe the current schema.
    ///
    /// Rules for a step: a pure JObject -> JObject function; reads no ContentDb, clock, file or current C# type
    /// (any value it needs is a literal frozen inside the step); tolerates absent optional fields; does not set
    /// SchemaVersion (the driver does). To add v1 -> v2: bump SaveSchema.Current to 2, append the step to
    /// <see cref="Steps"/>, add a fixture test.
    /// </summary>
    public static class SaveMigrations
    {
        /// <summary>Steps[i] migrates version (SaveSchema.MinSupported + i) to the next. Count == Current - MinSupported.</summary>
        public static readonly IReadOnlyList<Func<JObject, JObject>> Steps = new Func<JObject, JObject>[]
        {
            V0ToV1,
            V1ToV2,
        };

        /// <summary>Lift <paramref name="json"/> from <paramref name="version"/> to SaveSchema.Current.</summary>
        /// <param name="onStep">Optional (from, to) callback per applied step, for analytics (migration_applied).</param>
        public static JObject Migrate(int version, JObject json, Action<int, int> onStep = null) =>
            Migrate(version, json, Steps, SaveSchema.MinSupported, SaveSchema.Current, onStep);

        /// <summary>The driver, parameterised so tests can exercise it with their own chain.</summary>
        public static JObject Migrate(int version, JObject json, IReadOnlyList<Func<JObject, JObject>> steps,
                                      int minSupported, int current, Action<int, int> onStep = null)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            if (version > current) throw new SaveTooNewException(version, current);
            if (version < minSupported) throw new SaveInvalidException($"schema v{version} is older than the oldest supported (v{minSupported})");
            if (steps.Count != current - minSupported)
                throw new InvalidOperationException($"migration chain has {steps.Count} steps, expected {current - minSupported}");
            for (int v = version; v < current; v++)
            {
                json = steps[v - minSupported](json) ?? throw new InvalidOperationException($"migration step v{v} returned null");
                json["SchemaVersion"] = v + 1;
                onStep?.Invoke(v, v + 1);
            }
            return json;
        }

        // ================================================================== steps

        /// <summary>
        /// v0 -> v1. v0 was the pre-envelope dev format: the bare GameState at the root ("SchemaVersion": 0), seeds
        /// as JSON integers, and an optional root "LastSavedAtServerSec". v1 wraps it as { Meta, State } and writes
        /// seeds as 16-char hex strings. Without a saved anchor the sim's last wall-clock sync is the best guess.
        /// </summary>
        static JObject V0ToV1(JObject v0)
        {
            var state = (JObject)v0.DeepClone();
            state.Remove("SchemaVersion");
            long lastSaved;
            var anchor = state["LastSavedAtServerSec"];
            if (anchor != null && anchor.Type == JTokenType.Integer) lastSaved = (long)anchor;
            else if (state["Clock"]?["LastSyncServerMs"] is JValue ms && ms.Type == JTokenType.Integer) lastSaved = (long)ms / 1000;
            else lastSaved = 0;
            state.Remove("LastSavedAtServerSec");

            if (state["Rng"] is JObject rng)
            {
                HexSeed(rng, "ProfileSeed");
                if (rng["Streams"] is JObject streams)
                    foreach (var p in streams.Properties())
                        if (p.Value is JObject stream) HexSeed(stream, "Seed");
            }

            return new JObject
            {
                ["SchemaVersion"] = 0,   // the driver overwrites this
                ["Meta"] = new JObject
                {
                    ["LastSavedAtServerSec"] = lastSaved,
                    ["SaveSequence"] = 0,
                    ["DeviceInstallId"] = Guid.Empty.ToString(),
                    ["DebugTainted"] = false,
                },
                ["State"] = state,
            };
        }

        // ---- v1 -> v2: party and builds overhaul (specs/11 section 8). Literals frozen from content 1.0 / 2.0.

        /// <summary>Hero ids kept by content 2.0. Every other v1 id is converted to shards.</summary>
        static readonly HashSet<string> V2KeptHeroes = new HashSet<string>(StringComparer.Ordinal)
        {
            "hero_c_01", "hero_c_02", "hero_c_03", "hero_c_04", "hero_c_05",
            "hero_r_01", "hero_r_02", "hero_r_03", "hero_r_04", "hero_r_05",
            "hero_e_01", "hero_e_02", "hero_e_03", "hero_e_04",
            "hero_l_01", "hero_l_02", "hero_l_03", "hero_l_04",
            "hero_m_01", "hero_m_02",
        };

        /// <summary>Content 1.0 hero_tiers.shard_value, keyed by the tier letter in the id (hero_&lt;tier&gt;_NN).</summary>
        static readonly Dictionary<char, long> V1ShardValue = new Dictionary<char, long> { ['c'] = 1, ['r'] = 5, ['e'] = 25, ['l'] = 100, ['m'] = 500 };

        /// <summary>Content 2.0 hero_tiers.max_promotions by tier letter.</summary>
        static readonly Dictionary<char, int> V2MaxPromotions = new Dictionary<char, int> { ['c'] = 0, ['r'] = 1, ['e'] = 2, ['l'] = 3, ['m'] = 3 };

        const int V2PartySize = 4;
        const string V2Starter = "hero_c_01";

        /// <summary>The object at o[name], created (and attached) if missing or not an object.</summary>
        static JObject Child(JObject o, string name)
        {
            if (o[name] is JObject existing) return existing;
            var created = new JObject();
            o[name] = created;
            return created;
        }

        static char TierLetter(string heroId) => heroId.Length > 5 ? heroId[5] : '?';

        /// <summary>
        /// v1 -> v2. Removed heroes become shards; the party is compacted and cut to 4; class advances become
        /// per-companion promotions (min(advances, rarity cap)); a profile left with no companion gets the starter.
        /// Idempotent on a body that already has the v2 shape.
        /// </summary>
        static JObject V1ToV2(JObject v1)
        {
            var body = (JObject)v1.DeepClone();
            if (!(body["State"] is JObject state)) return body;

            // (Assigning a token that already has a parent attaches a clone, so only assign newly created objects.)
            var heroes = Child(state, "Heroes");
            var copies = Child(heroes, "Copies");

            // 1. removed companions -> shards
            long shards = 0;
            foreach (var p in new List<JProperty>(copies.Properties()))
            {
                if (V2KeptHeroes.Contains(p.Name)) continue;
                long n = p.Value.Type == JTokenType.Integer ? (long)p.Value : 0;
                if (n > 0 && V1ShardValue.TryGetValue(TierLetter(p.Name), out long v)) shards += n * v;
                p.Remove();
            }
            if (shards > 0)
            {
                var cur = Child(state, "Currencies");
                long have = cur["Shards"] != null && cur["Shards"].Type == JTokenType.Integer ? (long)cur["Shards"] : 0;
                cur["Shards"] = have + shards;
            }

            // 2. party: drop removed ids, compact, cut to 4
            var party = new List<string>();
            if (heroes["Equipped"] is JArray eq)
                foreach (var t in eq)
                    if (t.Type == JTokenType.String && V2KeptHeroes.Contains((string)t) && !party.Contains((string)t)) party.Add((string)t);

            // 3. class advances -> promotions
            var promos = Child(heroes, "Promotions");
            var prog = state["Progression"] as JObject;
            int adv = prog?["ClassAdvances"] != null && prog["ClassAdvances"].Type == JTokenType.Integer ? (int)prog["ClassAdvances"] : 0;
            prog?.Remove("ClassAdvances");
            if (adv > 0)
                foreach (var p in copies.Properties())
                {
                    if (promos[p.Name] != null || p.Value.Type != JTokenType.Integer || (long)p.Value <= 0) continue;
                    int cap = V2MaxPromotions.TryGetValue(TierLetter(p.Name), out int c) ? c : 0;
                    int n = Math.Min(adv, cap);
                    if (n > 0) promos[p.Name] = n;
                }

            // 4. starter if nothing is owned
            bool ownsAny = false;
            foreach (var p in copies.Properties())
                if (p.Value.Type == JTokenType.Integer && (long)p.Value > 0) ownsAny = true;
            if (!ownsAny)
            {
                copies[V2Starter] = 1;
                party.Remove(V2Starter);
                party.Insert(0, V2Starter);
            }

            // fill empty slots with other owned companions, rarest first then id ordinal, so the party is never
            // emptier than the roster allows (the sim re-ranks by FarmDps on the next pull or promotion)
            const string order = "mlerc";
            var bench = new List<string>();
            foreach (var p in copies.Properties())
                if (p.Value.Type == JTokenType.Integer && (long)p.Value > 0 && !party.Contains(p.Name)) bench.Add(p.Name);
            bench.Sort((a, b) =>
            {
                int ta = order.IndexOf(TierLetter(a)), tb = order.IndexOf(TierLetter(b));
                return ta != tb ? ta.CompareTo(tb) : string.CompareOrdinal(a, b);
            });
            foreach (var id in bench) if (party.Count < V2PartySize) party.Add(id);

            var outParty = new JArray();
            for (int i = 0; i < V2PartySize; i++) outParty.Add(i < party.Count ? (JToken)party[i] : JValue.CreateNull());
            heroes["Equipped"] = outParty;

            // 5. the save now describes content 2.0
            if (state["ContentVersion"] is JValue cv && cv.Type == JTokenType.String && ((string)cv).StartsWith("1.", StringComparison.Ordinal))
                state["ContentVersion"] = "2.0";
            return body;
        }

        static void HexSeed(JObject o, string name)
        {
            if (!(o[name] is JValue v) || v.Type != JTokenType.Integer) return;
            ulong seed = v.Value is BigInteger b ? (ulong)b : Convert.ToUInt64(v.Value, CultureInfo.InvariantCulture);
            o[name] = seed.ToString("x16", CultureInfo.InvariantCulture);
        }
    }
}
