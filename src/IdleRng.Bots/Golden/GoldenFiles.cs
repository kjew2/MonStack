using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IdleRng.Bots.Report;
using IdleRng.Sim.Content;

namespace IdleRng.Bots.Golden
{
    /// <summary>Golden files: expected output of fixed commands (specs/09 section 11).</summary>
    public static class GoldenFiles
    {
        public sealed record Entry(string FileName, string Command, Func<ContentDb, string> Generate);

        public static readonly IReadOnlyList<Entry> All = new[]
        {
            new Entry("battle-sim-vectors.txt", "golden (BattleVectors, specs/16 section 8)", BattleVectors.Generate),
            new Entry("training-run-vectors.txt", "golden (RunBot.Vectors, specs/14 section 9)", IdleRng.Bots.Training.RunBot.Vectors),
            new Entry("gauntlet-vectors.txt", "golden (GauntletVectors, specs/19 section 6)", GauntletVectors.Generate),
            new Entry("tower-vectors.txt", "golden (TowerVectors, specs/21 section 9)", TowerVectors.Generate),
            new Entry("endless-vectors.txt", "golden (EndlessVectors, specs/28 section 8)", EndlessVectors.Generate),
        };

        /// <summary>The repo's golden/ folder: a sibling of the Content folder.</summary>
        public static string DefaultDir(string contentDir) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(contentDir).TrimEnd('/', '\\')), "golden");

        /// <summary>Null when <paramref name="actual"/> equals the file; else a report (verdict + first differing lines).</summary>
        public static string Compare(string dir, Entry e, string actual)
        {
            string path = Path.Combine(dir, e.FileName);
            if (!File.Exists(path)) return $"{e.FileName}: missing (run `golden --update`)";
            string expected = File.ReadAllText(path).Replace("\r\n", "\n");
            if (expected == actual) return null;

            var exp = expected.Split('\n');
            var act = actual.Split('\n');
            var sb = new StringBuilder();
            sb.Append($"{e.FileName}: MISMATCH (regenerate: dotnet run -c Release --project src/IdleRng.Bots -- golden --update)\n");
            bool contentChanged = HeaderContent(exp) != HeaderContent(act);
            sb.Append(contentChanged
                ? "  content changed; if the acceptance checks pass and the change is intended, run `golden --update`\n"
                : "  sim or bot behaviour changed with identical content; this is a determinism or logic regression unless a sim change explains it\n");
            int shown = 0;
            for (int i = 0; i < Math.Max(exp.Length, act.Length) && shown < 20; i++)
            {
                string a = i < exp.Length ? exp[i] : null, b = i < act.Length ? act[i] : null;
                if (a == b) continue;
                if (a != null) sb.Append("  -").Append(a).Append('\n');
                if (b != null) sb.Append("  +").Append(b).Append('\n');
                shown++;
            }
            return sb.ToString().TrimEnd('\n');
        }

        static string HeaderContent(string[] lines)
        {
            foreach (var l in lines)
            {
                int i = l.IndexOf("content=", StringComparison.Ordinal);
                if (l.StartsWith("#") && i >= 0) return l.Substring(i);
            }
            return "";
        }

        public static void Write(string dir, Entry e, string actual)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, e.FileName), actual, new UTF8Encoding(false));
        }
    }
}
