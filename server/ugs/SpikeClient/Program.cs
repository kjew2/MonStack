using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using IdleRng.Bots.Golden;
using IdleRng.Bots.Training;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Tower;
using IdleRng.Sim.Training;
using IdleRngBattle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpikeClient
{
    /// <summary>
    /// The UGS spike's test script (issue #17).
    ///   local   every fight, in process: client JSON -> Cloud Code-style parameter binding -> BattleModule -> hash vs IdleRng.Sim.
    ///   remote  the same fights through the deployed module as an anonymous player, plus cold and warm latency.
    /// Fights: golden/battle-sim-vectors.txt (BattleVectors) and every floor of the golden/tower-vectors.txt team runs.
    /// </summary>
    static class Program
    {
        const string Module = "IdleRngBattle", Function = "ResolveFight";

        sealed class Case
        {
            public string Name;
            public FightRules Rules;
            public Fighter[] A, B;
            public ulong Seed;
            public FightResult Local;
            public string ParamsJson;
        }

        static async Task<int> Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : "local";
            string contentDir = FileContentSource.FindContentDirectory(AppContext.BaseDirectory)
                                ?? FileContentSource.FindContentDirectory(Environment.CurrentDirectory)
                                ?? throw new InvalidOperationException("Content/manifest.json not found");
            var db = ContentDb.LoadFromDirectory(contentDir);
            Console.WriteLine($"# content {db.Version} hash {db.ContentHash}; client {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            if (mode == "local")
            {
                // Cross-runtime check: every golden file was written by the .NET 8 client build; regenerate them on this runtime.
                string goldenDir = GoldenFiles.DefaultDir(contentDir);
                int drift = 0;
                foreach (var e in GoldenFiles.All)
                {
                    string diff = GoldenFiles.Compare(goldenDir, e, e.Generate(db));
                    Console.WriteLine(diff ?? $"ok   golden/{e.FileName} identical on this runtime");
                    if (diff != null) drift++;
                }
                if (drift > 0) { Console.WriteLine($"FAIL local: {drift} golden files differ on this runtime"); return 1; }
            }
            var cases = Cases(db);
            Console.WriteLine($"# {cases.Count} fights; params JSON {cases.Min(c => c.ParamsJson.Length)}..{cases.Max(c => c.ParamsJson.Length)} bytes");
            switch (mode)
            {
                case "local": return Local(cases);
                case "remote":
                    try { return await Remote(cases); }
                    catch (Exception e) when (e is HttpRequestException || e is InvalidOperationException || e is TaskCanceledException
                                               || e is JsonException)
                    {
                        Console.WriteLine("FAIL remote: " + e.Message);
                        return 1;
                    }
                default: Console.Error.WriteLine("usage: SpikeClient local|remote"); return 2;
            }
        }

        static List<Case> Cases(ContentDb db)
        {
            var list = new List<Case>();
            var rules = FightRules.FromContent(db);
            foreach (var v in BattleVectors.Vectors()) list.Add(Make("battle: " + v.Name, rules, v.A, v.B, v.Seed));

            // The tower-vectors team run (every species after a focus run at seed 7, up to the side size), floor by floor, on both
            // golden days. Each floor is one BattleSim.Resolve; its local hash must equal the golden run's floor hash.
            var towerRules = FightRules.FromContent(db, tower: true);
            var team = db.SpeciesList.Take(db.Constants.MonSideSize)
                .Select(sp => TrainingRun.MonsterFighter(db, RunBot.Play(db, RunPolicies.Create("focus"), sp.Id, 7))).ToArray();
            var side = TowerSim.Team(team, towerRules.SideSize);
            foreach (int day in new[] { 0, 20391 })
            {
                var run = TowerSim.Run(db, team, day);
                foreach (var f in run.Floors)
                {
                    var plan = TowerSim.Lineup(db, day).Single(p => p.Floor == f.Floor);
                    var c = Make($"tower: day {day} floor {f.Floor} {f.BossId}{(f.Phases.Count > 0 ? " phases " + string.Join(",", f.Phases) : "")}{(f.Vents > 0 ? $" vents {f.Vents}" : "")}",
                        towerRules, side, new[] { TowerSim.Boss(db, plan.BossId, plan.Level) }, TowerSim.FloorSeed(db, day, f.Floor));
                    if (c.Local.Hash != f.FightHash) throw new InvalidOperationException($"{c.Name}: rebuilt floor hash differs from TowerSim.Run");
                    list.Add(c);
                }
            }
            return list;
        }

        static Case Make(string name, FightRules rules, Fighter[] a, Fighter[] b, ulong seed)
        {
            var p = new JObject
            {
                ["rules"] = JToken.FromObject(rules),
                ["sideA"] = JToken.FromObject(a),
                ["sideB"] = JToken.FromObject(b),
                ["seed"] = seed.ToString(CultureInfo.InvariantCulture),
            };
            return new Case { Name = name, Rules = rules, A = a, B = b, Seed = seed, Local = BattleSim.Resolve(rules, a, b, seed),
                              ParamsJson = p.ToString(Formatting.None) };
        }

        /// <summary>Bind the params the way Cloud Code does (by parameter name, Newtonsoft.Json) and call the module in process.</summary>
        static int Local(List<Case> cases)
        {
            int bad = 0;
            var m = new BattleModule();
            foreach (var c in cases)
            {
                var p = JObject.Parse(c.ParamsJson);
                var r = m.ResolveFight(p["rules"].ToObject<FightRules>(), p["sideA"].ToObject<Fighter[]>(), p["sideB"].ToObject<Fighter[]>(),
                                       p["seed"].ToObject<string>());
                bad += Report(c, r.Hash, r.Outcome, r.Ticks, $"{r.ResolveMicros} us");
            }
            Console.WriteLine(bad == 0 ? $"PASS local: {cases.Count}/{cases.Count} hashes match" : $"FAIL local: {bad} mismatches");
            return bad == 0 ? 0 : 1;
        }

        static int Report(Case c, string hash, string outcome, int ticks, string extra)
        {
            bool ok = hash == c.Local.Hash && outcome == c.Local.Outcome.ToString() && ticks == c.Local.Ticks;
            Console.WriteLine($"{(ok ? "ok  " : "BAD ")} {c.Name}: {outcome} ticks={ticks} hash={hash}{(ok ? "" : $" (local {c.Local.Outcome} {c.Local.Ticks} {c.Local.Hash})")} [{extra}]");
            return ok ? 0 : 1;
        }

        static async Task<int> Remote(List<Case> cases)
        {
            string project = Need("UGS_CLI_PROJECT_ID"), env = Need("UGS_CLI_ENVIRONMENT_NAME");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var sign = new HttpRequestMessage(HttpMethod.Post, "https://player-auth.services.api.unity.com/v1/authentication/anonymous")
                { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            sign.Headers.Add("ProjectId", project);
            sign.Headers.Add("UnityEnvironment", env);
            var sw = Stopwatch.StartNew();
            var signResp = await Send(http, sign);
            string signBody = await signResp.Content.ReadAsStringAsync();
            if (!signResp.IsSuccessStatusCode) { Console.WriteLine($"FAIL sign-in HTTP {(int)signResp.StatusCode}: {signBody}"); return 1; }
            string token = (string)JObject.Parse(signBody)["idToken"] ?? throw new InvalidOperationException("sign-in reply has no idToken: " + signBody);
            Console.WriteLine($"# anonymous sign-in {sw.ElapsedMilliseconds} ms");

            string url = $"https://cloud-code.services.api.unity.com/v1/projects/{project}/modules/{Module}/{Function}";
            async Task<(JObject reply, double ms)> Call(Case c)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, url)
                    { Content = new StringContent("{\"params\":" + c.ParamsJson + "}", Encoding.UTF8, "application/json") };
                req.Headers.Add("Authorization", "Bearer " + token);
                var t = Stopwatch.StartNew();
                var resp = await Send(http, req);
                string body = await resp.Content.ReadAsStringAsync();
                double ms = t.Elapsed.TotalMilliseconds;
                if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"{c.Name}: HTTP {(int)resp.StatusCode}: {body}");
                var j = JObject.Parse(body);
                return ((j["output"] as JObject) ?? j, ms);
            }

            int bad = 0;
            var first = await Call(cases[0]);
            Console.WriteLine($"# first call (cold if the module had no traffic for 15 min or was just deployed): {first.ms:0} ms; runtime {Field(first.reply, "runtime")}");
            foreach (var c in cases)
            {
                var (r, ms) = await Call(c);
                bad += Report(c, (string)Field(r, "hash"), (string)Field(r, "outcome"), (int?)Field(r, "ticks") ?? -1,
                              $"{ms:0} ms, resolve {Field(r, "resolveMicros")} us");
            }

            foreach (var c in new[] { cases[0], cases.OrderByDescending(x => x.ParamsJson.Length).First() })
            {
                var ms = new List<double>();
                for (int i = 0; i < 20; i++) ms.Add((await Call(c)).ms);
                ms.Sort();
                Console.WriteLine($"# warm x20 '{c.Name}' ({c.ParamsJson.Length} B): median {(ms[9] + ms[10]) / 2:0} ms, worst {ms[19]:0} ms, best {ms[0]:0} ms");
            }
            Console.WriteLine(bad == 0 ? $"PASS remote: {cases.Count}/{cases.Count} hashes match" : $"FAIL remote: {bad} mismatches");
            return bad == 0 ? 0 : 1;
        }

        /// <summary>Cloud Code serializes the reply with Newtonsoft; accept either camelCase or PascalCase keys.</summary>
        static JToken Field(JObject o, string name) => o.GetValue(name, StringComparison.OrdinalIgnoreCase);

        static async Task<HttpResponseMessage> Send(HttpClient http, HttpRequestMessage req)
        {
            try { return await http.SendAsync(req); }
            catch (HttpRequestException e)
            {
                throw new HttpRequestException($"cannot reach {req.RequestUri.Host}: {e.Message} (is it in the environment's Allowed domains?)", e);
            }
        }

        static string Need(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException(name + " is not set");
    }
}
