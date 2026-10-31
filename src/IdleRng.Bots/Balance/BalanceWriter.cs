using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using IdleRng.Bots.Report;

namespace IdleRng.Bots.Balance
{
    /// <summary>The balance report as a text table (for people), JSON or long-form CSV (for scripts). specs/09 section 7.</summary>
    public static class BalanceWriter
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Write(BalanceReport r, string format)
        {
            switch (format)
            {
                case "text": return Text(r);
                case "json": return Json(r);
                case "csv": return Csv(r);
                default: throw new ArgumentException($"unknown --format '{format}' (one of text, json, csv)");
            }
        }

        // ------------------------------------------------------------------ text

        public static string Text(BalanceReport r)
        {
            var o = r.Options;
            var sb = new StringBuilder();
            sb.Append("# balance report (specs/09 section 7). A report, not a gate: no targets are agreed yet.\n");
            sb.Append($"# content {TableWriter.ContentTag(r.ContentVersion, r.ContentHash)}, seeds {o.SeedBase}..{o.SeedBase + (ulong)o.Seeds - 1} ")
              .Append($"({o.Seeds} runs per species x policy), books {(o.Books == null || o.Books.Count == 0 ? "none" : string.Join(",", o.Books))}, ")
              .Append("fresh account (no talents)\n");

            sb.Append("\n## Grades: % of runs ending at each rank (mean is the rank index, E = 0)\n");
            var t = new Table(2);
            t.Row(new[] { "species", "policy", "runs" }.Concat(r.RankIds.Select(id => id.ToUpperInvariant())).Concat(new[] { "mean", "p10/p50/p90" }));
            foreach (var c in r.Cells)
                t.Row(new[] { c.Species, c.Policy, c.N.ToString(Inv) }
                    .Concat(c.Grades.Select(g => Pct(c.N == 0 ? 0 : (double)g / c.N)))
                    .Concat(new[] { Num(c.Grade.Mean), Pcts(c.Grade, x => r.RankIds[(int)x].ToUpperInvariant()) }));
            t.AppendTo(sb);

            sb.Append("\n## Runs: cycles played, rank fights, injuries (an injury ends the run), shard payout\n");
            t = new Table(2);
            t.Row("species", "policy", "injured%", "cycles mean", "p10/p50/p90", "won mean", "p10/p50/p90", "lost mean", "p10/p50/p90",
                  "win%", "shards mean", "sd", "p10/p50/p90");
            foreach (var c in r.Cells)
                t.Row(c.Species, c.Policy, Pct(c.InjuryRate), Num(c.Cycles.Mean), Pcts(c.Cycles), Num(c.FightsWon.Mean), Pcts(c.FightsWon),
                      Num(c.FightsLost.Mean), Pcts(c.FightsLost), Pct(c.FightWinRate), Num(c.Shards.Mean), Num(c.Shards.Sd), Pcts(c.Shards));
            t.AppendTo(sb);

            sb.Append($"\n## Gauntlet: floor reached of {r.GauntletFloors} ({r.GauntletFloors + 1} = cleared)\n");
            t = new Table(2, textLast: true);
            t.Row("species", "policy", "mean", "sd", "min", "p10/p50/p90", "max", "cleared%", "boss-floor%", "top killers");
            foreach (var c in r.Cells)
                t.Row(c.Species, c.Policy, Num(c.GauntletFloor.Mean), Num(c.GauntletFloor.Sd), Num(c.GauntletFloor.Min), Pcts(c.GauntletFloor),
                      Num(c.GauntletFloor.Max), Pct(c.GauntletClearRate), Pct(c.GauntletBossStallRate),
                      c.Killers.Count == 0 ? "-" : string.Join(" ", c.Killers.Take(3).Select(k => $"{k.Key} {Pct(c.N == 0 ? 0 : (double)k.Count / c.N)}%")));
            t.AppendTo(sb);

            if (o.Tower)
            {
                sb.Append($"\n## Daily Tower: floors cleared of {r.TowerFloors}, first attempt, day {o.TowerDayBase} + seed index\n");
                t = new Table(2);
                t.Row("policy", "team", "mean", "sd", "p10/p50/p90", "max", "cleared%", "shards mean", "books mean");
                foreach (var w in r.Tower)
                    t.Row(w.Policy, w.Team, Num(w.FloorsCleared.Mean), Num(w.FloorsCleared.Sd), Pcts(w.FloorsCleared), Num(w.FloorsCleared.Max),
                          Pct(w.ClearRate), Num(w.Shards.Mean), Num(w.Books.Mean));
                t.AppendTo(sb);
            }
            return sb.ToString();
        }

        static string Pct(double fraction) => (fraction * 100).ToString("0.0", Inv);

        static string Num(double x) => x.ToString("0.##", Inv);

        static string Pcts(Distribution d, Func<double, string> f = null)
        {
            f = f ?? Num;
            return d.N == 0 ? "-" : $"{f(d.P10)}/{f(d.P50)}/{f(d.P90)}";
        }

        /// <summary>
        /// Columns padded to their widest cell: the first <c>left</c> left-aligned, the rest right-aligned, except a free-text last
        /// column (<c>textLast</c>), which is left-aligned.
        /// </summary>
        sealed class Table
        {
            readonly int _left;
            readonly bool _textLast;
            readonly List<string[]> _rows = new List<string[]>();

            public Table(int left, bool textLast = false) { _left = left; _textLast = textLast; }

            public void Row(params string[] cells) => _rows.Add(cells);

            public void Row(IEnumerable<string> cells) => _rows.Add(cells.ToArray());

            public void AppendTo(StringBuilder sb)
            {
                int cols = _rows.Max(x => x.Length);
                var w = new int[cols];
                foreach (var row in _rows)
                    for (int i = 0; i < row.Length; i++) w[i] = Math.Max(w[i], row[i].Length);
                foreach (var row in _rows)
                {
                    var line = new StringBuilder();
                    for (int i = 0; i < row.Length; i++)
                    {
                        if (i > 0) line.Append("  ");
                        bool left = i < _left || _textLast && i == cols - 1;
                        line.Append(left ? row[i].PadRight(w[i]) : row[i].PadLeft(w[i]));
                    }
                    sb.Append(line.ToString().TrimEnd()).Append('\n');
                }
            }
        }

        // ------------------------------------------------------------------ json

        public static string Json(BalanceReport r)
        {
            var o = r.Options;
            var j = new JsonOut();
            j.Open('{');
            j.Key("report").Str("balance");
            j.Key("content").Open('{').Key("version").Str(r.ContentVersion).Key("hash").Str(r.ContentHash).Close('}');
            j.Key("options").Open('{')
             .Key("seeds").Num(o.Seeds).Key("seedBase").Num(o.SeedBase)
             .Key("books").Strs(o.Books ?? new string[0]).Key("tower").Bool(o.Tower).Key("towerDayBase").Num(o.TowerDayBase)
             .Close('}');
            j.Key("ranks").Strs(r.RankIds);
            j.Key("gauntletFloors").Num(r.GauntletFloors).Key("towerFloors").Num(r.TowerFloors);
            j.Key("cells").Open('[');
            foreach (var c in r.Cells)
            {
                j.Open('{').Key("species").Str(c.Species).Key("policy").Str(c.Policy).Key("n").Num(c.N);
                j.Key("grades").Open('{');
                for (int i = 0; i < r.RankIds.Count; i++) j.Key(r.RankIds[i]).Num(c.Grades[i]);
                j.Close('}');
                j.Key("grade"); Dist(j, c.Grade);
                j.Key("completed").Num(c.Completed).Key("injured").Num(c.Injured).Key("injuryRate").Num(c.InjuryRate);
                j.Key("cycles"); Dist(j, c.Cycles);
                j.Key("fightsWon"); Dist(j, c.FightsWon);
                j.Key("fightsLost"); Dist(j, c.FightsLost);
                j.Key("fightWinRate").Num(c.FightWinRate);
                j.Key("shards"); Dist(j, c.Shards);
                j.Key("gauntletFloor"); Dist(j, c.GauntletFloor);
                j.Key("gauntletCleared").Num(c.GauntletCleared).Key("gauntletClearRate").Num(c.GauntletClearRate);
                j.Key("gauntletBossStalls").Num(c.GauntletBossStalls).Key("gauntletBossStallRate").Num(c.GauntletBossStallRate);
                j.Key("killers").Open('{');
                foreach (var k in c.Killers) j.Key(k.Key).Num(k.Count);
                j.Close('}');
                j.Close('}');
            }
            j.Close(']');
            j.Key("tower").Open('[');
            foreach (var w in r.Tower)
            {
                j.Open('{').Key("policy").Str(w.Policy).Key("team").Str(w.Team).Key("n").Num(w.N);
                j.Key("floorsCleared"); Dist(j, w.FloorsCleared);
                j.Key("cleared").Num(w.Cleared).Key("clearRate").Num(w.ClearRate);
                j.Key("shards"); Dist(j, w.Shards);
                j.Key("books"); Dist(j, w.Books);
                j.Close('}');
            }
            j.Close(']');
            j.Close('}');
            return j.ToString() + "\n";
        }

        static void Dist(JsonOut j, Distribution d) =>
            j.Open('{').Key("n").Num(d.N).Key("mean").Num(d.Mean).Key("sd").Num(d.Sd).Key("min").Num(d.Min)
             .Key("p10").Num(d.P10).Key("p50").Num(d.P50).Key("p90").Num(d.P90).Key("max").Num(d.Max).Close('}');

        /// <summary>Minimal JSON emitter: commas are placed automatically, numbers in invariant culture (up to 6 decimals).</summary>
        sealed class JsonOut
        {
            readonly StringBuilder _sb = new StringBuilder();
            bool _needComma;

            void Sep() { if (_needComma) _sb.Append(','); _needComma = false; }

            public JsonOut Open(char ch) { Sep(); _sb.Append(ch); return this; }
            public JsonOut Close(char ch) { _sb.Append(ch); _needComma = true; return this; }
            public JsonOut Key(string k) { Sep(); Quote(k); _sb.Append(':'); return this; }
            public JsonOut Str(string s) { Sep(); if (s == null) _sb.Append("null"); else Quote(s); _needComma = true; return this; }
            public JsonOut Bool(bool b) { Sep(); _sb.Append(b ? "true" : "false"); _needComma = true; return this; }
            public JsonOut Num(double x) { Sep(); _sb.Append(x.ToString("0.######", Inv)); _needComma = true; return this; }
            public JsonOut Num(ulong x) { Sep(); _sb.Append(x.ToString(Inv)); _needComma = true; return this; }

            public JsonOut Strs(IEnumerable<string> items)
            {
                Open('[');
                foreach (var s in items) Str(s);
                return Close(']');
            }

            void Quote(string s)
            {
                _sb.Append('"');
                foreach (char ch in s)
                {
                    if (ch == '"' || ch == '\\') _sb.Append('\\').Append(ch);
                    else if (ch < ' ') _sb.Append("\\u").Append(((int)ch).ToString("x4", Inv));
                    else _sb.Append(ch);
                }
                _sb.Append('"');
            }

            public override string ToString() => _sb.ToString();
        }

        // ------------------------------------------------------------------ csv

        /// <summary>
        /// Long form, one metric per row: section,species,policy,team,metric,n,mean,sd,min,p10,p50,p90,max. Rates and grade shares
        /// put the fraction in mean and leave the spread columns empty.
        /// </summary>
        public static string Csv(BalanceReport r)
        {
            var sb = new StringBuilder("section,species,policy,team,metric,n,mean,sd,min,p10,p50,p90,max\n");
            foreach (var c in r.Cells)
            {
                for (int i = 0; i < r.RankIds.Count; i++) Rate(sb, "run", c.Species, c.Policy, "", "grade_" + r.RankIds[i], c.N, c.N == 0 ? 0 : (double)c.Grades[i] / c.N);
                Row(sb, "run", c.Species, c.Policy, "", "grade", c.Grade);
                Rate(sb, "run", c.Species, c.Policy, "", "injury_rate", c.N, c.InjuryRate);
                Row(sb, "run", c.Species, c.Policy, "", "cycles", c.Cycles);
                Row(sb, "run", c.Species, c.Policy, "", "fights_won", c.FightsWon);
                Row(sb, "run", c.Species, c.Policy, "", "fights_lost", c.FightsLost);
                Rate(sb, "run", c.Species, c.Policy, "", "fight_win_rate", c.N, c.FightWinRate);
                Row(sb, "run", c.Species, c.Policy, "", "shards", c.Shards);
                Row(sb, "gauntlet", c.Species, c.Policy, "", "floor", c.GauntletFloor);
                Rate(sb, "gauntlet", c.Species, c.Policy, "", "clear_rate", c.N, c.GauntletClearRate);
                Rate(sb, "gauntlet", c.Species, c.Policy, "", "boss_stall_rate", c.N - c.GauntletCleared, c.GauntletBossStallRate);
                foreach (var k in c.Killers) Rate(sb, "gauntlet", c.Species, c.Policy, "", "killer_" + k.Key, c.N, c.N == 0 ? 0 : (double)k.Count / c.N);
            }
            foreach (var w in r.Tower)
            {
                Row(sb, "tower", "", w.Policy, w.Team, "floors_cleared", w.FloorsCleared);
                Rate(sb, "tower", "", w.Policy, w.Team, "clear_rate", w.N, w.ClearRate);
                Row(sb, "tower", "", w.Policy, w.Team, "shards", w.Shards);
                Row(sb, "tower", "", w.Policy, w.Team, "books", w.Books);
            }
            return sb.ToString();
        }

        static void Row(StringBuilder sb, string section, string species, string policy, string team, string metric, Distribution d) =>
            sb.Append(string.Join(",", section, species, policy, team, metric, d.N.ToString(Inv), F(d.Mean), F(d.Sd), F(d.Min),
                F(d.P10), F(d.P50), F(d.P90), F(d.Max))).Append('\n');

        static void Rate(StringBuilder sb, string section, string species, string policy, string team, string metric, int n, double rate) =>
            sb.Append(string.Join(",", section, species, policy, team, metric, n.ToString(Inv), F(rate), "", "", "", "", "", "")).Append('\n');

        static string F(double x) => x.ToString("0.######", Inv);
    }
}
