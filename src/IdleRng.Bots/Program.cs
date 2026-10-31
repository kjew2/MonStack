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
            "usage: IdleRng.Bots <train|golden|help> [options]\n" +
            "  train      --species ID --policy balanced|focus|reckless|hoarder --seed N --books a,b --content dir   (one training run, specs/14-15)\n" +
            "  gauntlet   --seeds N --policies a,b --content dir   (where bot monsters stall in the gauntlet, specs/19 section 6)\n" +
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
                    case "gauntlet": return Gauntlet(a);
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

        static int Gauntlet(CliArgs a)
        {
            var db = LoadContent(a);
            var policies = a.Str("policies", string.Join(",", IdleRng.Bots.Training.RunPolicies.Names))
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            int seeds = a.Int("seeds", 10);
            if (seeds < 1) throw new ArgumentException("--seeds must be at least 1");
            var r = IdleRng.Bots.Training.GauntletSpread.Collect(db, policies, seeds);
            Emit(a, IdleRng.Bots.Training.GauntletSpread.Report(r));
            return 0;
        }

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
