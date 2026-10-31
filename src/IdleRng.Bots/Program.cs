using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using IdleRng.Bots.Golden;
using IdleRng.Sim.Content;

namespace IdleRng.Bots
{
    /// <summary>Hand-rolled "--key value" / "--flag" parser.</summary>
    public sealed class CliArgs
    {
        readonly Dictionary<string, string> _opts = new Dictionary<string, string>(StringComparer.Ordinal);

        public string Verb { get; }

        public CliArgs(string[] args)
        {
            int i = 0;
            Verb = args.Length > 0 && !args[0].StartsWith("--") ? args[i++] : "run";
            for (; i < args.Length; i++)
            {
                string a = args[i];
                if (!a.StartsWith("--")) throw new ArgumentException($"unexpected argument '{a}'");
                string key = a.Substring(2);
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) _opts[key] = args[++i];
                else _opts[key] = "";
            }
        }

        public bool Has(string k) => _opts.ContainsKey(k);
        public string Str(string k, string def) => _opts.TryGetValue(k, out var v) && v != "" ? v : def;
        public int Int(string k, int def) => _opts.TryGetValue(k, out var v) && v != "" ? int.Parse(v, CultureInfo.InvariantCulture) : def;
        public long Long(string k, long def) => _opts.TryGetValue(k, out var v) && v != "" ? long.Parse(v, CultureInfo.InvariantCulture) : def;
    }

    public static class Program
    {
        const string Usage =
            "usage: IdleRng.Bots <train|balance|golden|help> [options]\n" +
            "  train      --species ID --policy balanced|focus|reckless|hoarder|chaser --seed N --books a,b --content dir   (one training run, specs/14-15)\n" +
            "  balance    --seeds N --seed-base N --species a,b --policies a,b --books none|all|a,b --no-tower --tower-day N\n" +
            "             --no-book-canary --strict --format text|json|csv --jobs N --out path\n" +
            "             (run-balance report and loose sanity checks over many seeds, specs/09 section 7; --strict exits 1 on a WARN)\n" +
            "  golden     --check|--update --golden-dir path --content dir   (specs/09 section 11)\n";

        public static int Main(string[] argv)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                var a = new CliArgs(argv);
                switch (a.Verb)
                {
                    case "golden": return GoldenCmd(a);
                    case "train": return Train(a);
                    case "balance": return BalanceCmd(a);
                    case "help": case "--help": Console.Out.Write(Usage); return 0;
                    default: Console.Error.Write($"unknown verb '{a.Verb}'\n" + Usage); return 2;
                }
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException)
            {
                Console.Error.Write(e.Message + "\n" + Usage);
                return 2;
            }
        }

        public static string ContentDir(CliArgs a) =>
            a.Str("content", null) ?? FileContentSource.FindContentDirectory(AppContext.BaseDirectory)
            ?? FileContentSource.FindContentDirectory(Directory.GetCurrentDirectory())
            ?? throw new ArgumentException("Content/manifest.json not found; pass --content <dir>");

        static ContentDb LoadContent(CliArgs a)
        {
            var db = ContentDb.LoadFromDirectory(ContentDir(a));
            Console.Error.Write($"# content {db.Version} hash {db.ContentHash}\n");
            return db;
        }

        static void Emit(CliArgs a, string text)
        {
            string path = a.Str("out", null);
            if (path == null) { Console.Out.Write(text); return; }
            File.WriteAllText(path, text, new UTF8Encoding(false));
            Console.Error.Write($"# wrote {path}\n");
        }

        static int Train(CliArgs a)
        {
            var db = LoadContent(a);
            string species = a.Str("species", db.SpeciesList[0].Id);
            var policy = IdleRng.Bots.Training.RunPolicies.Create(a.Str("policy", "balanced"));
            var sb = new StringBuilder();
            string books = a.Str("books", null);
            var s = IdleRng.Bots.Training.RunBot.Play(db, policy, species, (ulong)a.Long("seed", 7), line => sb.Append(line).Append('\n'),
                                                     books?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            sb.Append(IdleRng.Bots.Training.RunBot.Summary(db, s)).Append('\n');
            sb.Append("hash ").Append(IdleRng.Sim.Training.TrainingRun.StateHash(s)).Append('\n');
            Emit(a, sb.ToString());
            return 0;
        }

        /// <summary>
        /// The run-balance report (specs/09 section 7). A report, not a gate: it exits 0 whatever the numbers say, unless --strict
        /// (opt-in, not used by check.sh), which exits 1 when a sanity check WARNs.
        /// </summary>
        static int BalanceCmd(CliArgs a)
        {
            var db = LoadContent(a);
            string format = a.Str("format", "text");
            if (format != "text" && format != "json" && format != "csv") throw new ArgumentException($"unknown --format '{format}' (one of text, json, csv)");
            string books = a.Str("books", "none");
            if (a.Long("seed-base", 1) < 0) throw new ArgumentException("--seed-base must be >= 0");
            var o = new IdleRng.Bots.Balance.BalanceOptions
            {
                Seeds = a.Int("seeds", 200),
                SeedBase = (ulong)a.Long("seed-base", 1),
                Species = List(a.Str("species", null)),
                Policies = List(a.Str("policies", null)),
                Books = books == "none" ? null : books == "all" ? db.SkillBooks.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList() : List(books),
                Tower = !a.Has("no-tower"),
                TowerDayBase = a.Int("tower-day", 0),
                Jobs = a.Int("jobs", Environment.ProcessorCount),
                BookCanary = !a.Has("no-book-canary"),
            };
            var sw = Stopwatch.StartNew();
            var report = IdleRng.Bots.Balance.BalanceHarness.Run(db, o);
            Console.Error.Write($"# balance: {report.Cells.Count} cells x {o.Seeds} seeds in {sw.Elapsed.TotalSeconds:0.0}s\n");
            Emit(a, IdleRng.Bots.Balance.BalanceWriter.Write(report, format));
            var warns = report.Checks.Where(k => k.Status == IdleRng.Bots.Balance.CheckStatus.Warn).Select(k => k.Id).ToList();
            Console.Error.Write($"# sanity checks: {(warns.Count == 0 ? "no WARN" : "WARN " + string.Join(", ", warns))}\n");
            return IdleRng.Bots.Balance.BalanceChecks.ExitCode(report.Checks, a.Has("strict"));
        }

        static List<string> List(string csv) => csv?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();

        /// <summary>--update rewrites golden/, otherwise every file is compared byte for byte.</summary>
        static int GoldenCmd(CliArgs a)
        {
            var db = LoadContent(a);
            string dir = a.Str("golden-dir", GoldenFiles.DefaultDir(ContentDir(a)));
            bool update = a.Has("update");
            int bad = 0;
            foreach (var e in GoldenFiles.All)
            {
                string text = e.Generate(db);
                if (update) { GoldenFiles.Write(dir, e, text); Console.Out.Write($"wrote {e.FileName}\n"); continue; }
                string diff = GoldenFiles.Compare(dir, e, text);
                if (diff == null) Console.Out.Write($"OK   {e.FileName}\n");
                else { Console.Out.Write("FAIL " + diff + "\n"); bad++; }
            }
            return bad == 0 ? 0 : 1;
        }
    }
}
