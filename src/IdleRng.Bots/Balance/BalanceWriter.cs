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
            sb.Append("# balance report (specs/09 section 7). A report, not a gate: the sanity checks are loose and a WARN fails nothing.\n");
            sb.Append($"# content {TableWriter.ContentTag(r.ContentVersion, r.ContentHash)}, seeds {o.SeedBase}..{o.SeedBase + (ulong)o.Seeds - 1} ")
              .Append($"({o.Seeds} runs per species x policy), books {(o.Books == null || o.Books.Count == 0 ? "none" : string.Join(",", o.Books))}, ")
              .Append("fresh account (no talents)\n");

            sb.Append("\n## Sanity checks: \"absolutely unplayable\" guards only, not balance targets (issue #89)\n");
            var t = new Table(2, textLast: true);
            t.Row("status", "check", "value", "limit", "n", "rule; worst");
            foreach (var k in r.Checks)
                t.Row(BalanceChecks.StatusName(k.Status), k.Id, double.IsNaN(k.Value) ? "-" : Num(k.Value), Num(k.Limit), k.N.ToString(Inv),
                      $"{k.Rule}; {k.Detail}");
            t.AppendTo(sb);
            sb.Append($"{r.Checks.Count(k => k.Status == CheckStatus.Pass)} pass, {r.Checks.Count(k => k.Status == CheckStatus.Warn)} warn, ")
              .Append($"{r.Checks.Count(k => k.Status == CheckStatus.Skip)} skip\n");

            sb.Append("\n## Grades: % of runs ending at each rank (mean is the rank index, E = 0)\n");
            t = new Table(2);
            t.Row(new[] { "species", "policy", "runs" }.Concat(r.RankIds.Select(id => id.ToUpperInvariant())).Concat(new[] { "mean", "p10/p50/p90" }));
            foreach (var c in r.Cells)
                t.Row(new[] { c.Species, c.Policy, c.N.ToString(Inv) }
                    .Concat(c.Grades.Select(g => Pct(c.N == 0 ? 0 : (double)g / c.N)))
                    .Concat(new[] { Num(c.Grade.Mean), Pcts(c.Grade, x => r.RankIds[(int)x].ToUpperInvariant()) }));
            t.AppendTo(sb);

            sb.Append("\n## Runs: injuries (each costs the rest of its cycle and fight, then INJURY_SKIP_CYCLES whole cycles sat out), rank fights, shard payout\n");
            t = new Table(2);
            t.Row("species", "policy", "injured%", "injuries mean", "p10/p50/p90", "sat-out mean", "p10/p50/p90", "won mean", "p10/p50/p90",
                  "lost mean", "p10/p50/p90", "win%", "shards mean", "sd", "p10/p50/p90");
            foreach (var c in r.Cells)
                t.Row(c.Species, c.Policy, Pct(c.InjuryRate), Num(c.Injuries.Mean), Pcts(c.Injuries), Num(c.CyclesLost.Mean), Pcts(c.CyclesLost),
                      Num(c.FightsWon.Mean), Pcts(c.FightsWon), Num(c.FightsLost.Mean), Pcts(c.FightsLost), Pct(c.FightWinRate), Num(c.Shards.Mean), Num(c.Shards.Sd), Pcts(c.Shards));
            t.AppendTo(sb);

            OpponentTable(sb, r);

            sb.Append($"\n## Gauntlet: floor reached of {r.GauntletFloors} ({r.GauntletFloors + 1} = cleared)\n");
            t = new Table(2, textLast: true);
            t.Row("species", "policy", "mean", "sd", "min", "p10/p50/p90", "max", "cleared%", "boss-floor%", "top killers");
            foreach (var c in r.Cells)
                t.Row(c.Species, c.Policy, Num(c.GauntletFloor.Mean), Num(c.GauntletFloor.Sd), Num(c.GauntletFloor.Min), Pcts(c.GauntletFloor),
                      Num(c.GauntletFloor.Max), Pct(c.GauntletClearRate), Pct(c.GauntletBossStallRate),
                      c.Killers.Count == 0 ? "-" : string.Join(" ", c.Killers.Take(3).Select(k => $"{k.Key} {Pct(c.N == 0 ? 0 : (double)k.Count / c.N)}%")));
            t.AppendTo(sb);

            if (r.GauntletBossEvery > 0)
            {
                sb.Append($"\n## Gauntlet stall floors: % of the runs that did not clear, by floor reached mod {r.GauntletBossEvery} (x0 = boss floor; issue #99)\n");
                t = new Table(2);
                t.Row(new[] { "species", "policy", "stalls" }.Concat(Enumerable.Range(0, r.GauntletBossEvery).Select(i => "x" + i.ToString(Inv))));
                foreach (var c in r.Cells)
                {
                    int stalls = c.N - c.GauntletCleared;
                    t.Row(new[] { c.Species, c.Policy, stalls.ToString(Inv) }
                        .Concat(c.GauntletStallMod.Select(k => Pct(stalls == 0 ? 0 : (double)k / stalls))));
                }
                t.AppendTo(sb);
            }

            ChaseTable(sb, "\n## Chase capstones: % of runs ending with the capstone bought and its cycle; % of runs where the tree unlocked and where each condition held at some point (\"any\": either tree)\n", r.Cells);

            if (r.BookCells.Count > 0)
            {
                sb.Append("\n## Book canary: mean rank and capstone% with every skill book vs none (no tower)\n");
                t = new Table(2);
                t.Row("species", "policy", "none", "all books", "change", "capstone% none", "all books");
                foreach (var b in r.BookCells)
                {
                    var n = r.Cells.First(c => c.Species == b.Species && c.Policy == b.Policy);
                    t.Row(b.Species, b.Policy, Num(n.Grade.Mean), Num(b.Grade.Mean), (b.Grade.Mean - n.Grade.Mean).ToString("+0.##;-0.##;0", Inv),
                          Pct(n.CapstoneRate), Pct(b.CapstoneRate));
                }
                t.AppendTo(sb);
                ChaseTable(sb, "\n## Book canary chase capstones (every skill book brought)\n", r.BookCells);
            }

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

        /// <summary>Each pool monster over every policy: how often ladders rolled it and, per species, the rank fights won against it.</summary>
        static void OpponentTable(StringBuilder sb, BalanceReport r)
        {
            var species = r.Cells.Select(c => c.Species).Distinct().ToList();
            var ids = r.Cells.SelectMany(c => c.Opponents).GroupBy(o => o.Id, StringComparer.Ordinal)
                .Select(g => g.First()).OrderBy(o => o.Rank).ThenBy(o => o.Id, StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return;
            int runs = r.Cells.Sum(c => c.N);
            sb.Append("\n## Rank fights by opponent: % of ladders that rolled each pool monster; per species (every policy), % of the rank fights against it won\n");
            var t = new Table(2);
            t.Row(new[] { "rank", "opponent", "rolled%" }.Concat(species));
            foreach (var id in ids)
            {
                var all = r.Cells.SelectMany(c => c.Opponents).Where(o => o.Id == id.Id).ToList();
                var row = new List<string> { r.RankIds[id.Rank].ToUpperInvariant(), id.Id, Pct(runs == 0 ? 0 : (double)all.Sum(o => o.Rolled) / runs) };
                foreach (var sp in species)
                {
                    var mine = r.Cells.Where(c => c.Species == sp).SelectMany(c => c.Opponents).Where(o => o.Id == id.Id).ToList();
                    int fought = mine.Sum(o => o.Fought), won = mine.Sum(o => o.Won);
                    row.Add(fought == 0 ? "-" : $"{Pct((double)won / fought)}% of {fought.ToString(Inv)}");
                }
                t.Row(row);
            }
            t.AppendTo(sb);
        }

        static void ChaseTable(StringBuilder sb, string title, List<CellSummary> cells)
        {
            sb.Append(title);
            var t = new Table(3, textLast: true);
            t.Row("species", "policy", "tree", "unlocked%", "capstone%", "cycle mean", "p10/p50/p90", "conditions held%");
            foreach (var c in cells)
            {
                t.Row(c.Species, c.Policy, "any", "-", Pct(c.CapstoneRate), CycleMean(c.CapstoneCycle), Pcts(c.CapstoneCycle), "-");
                foreach (var x in c.Chase)
                    t.Row(c.Species, c.Policy, x.TreeId, Pct(x.UnlockRate), Pct(x.CapstoneRate), CycleMean(x.CapstoneCycle), Pcts(x.CapstoneCycle),
                          string.Join(", ", x.Conditions.Select(k => $"{k.Label} {Pct(k.HeldRate)}")));
            }
            t.AppendTo(sb);
        }

        static string CycleMean(Distribution d) => d.N == 0 ? "-" : Num(d.Mean);

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
             .Key("bookCanary").Bool(r.BookCells.Count > 0)
             .Close('}');
            j.Key("ranks").Strs(r.RankIds);
            j.Key("gauntletFloors").Num(r.GauntletFloors).Key("towerFloors").Num(r.TowerFloors).Key("gauntletBossEvery").Num(r.GauntletBossEvery);
            j.Key("checks").Open('[');
            foreach (var k in r.Checks)
            {
                j.Open('{').Key("id").Str(k.Id).Key("status").Str(BalanceChecks.StatusName(k.Status)).Key("value");
                if (double.IsNaN(k.Value)) j.Null(); else j.Num(k.Value);
                j.Key("limit").Num(k.Limit).Key("n").Num(k.N).Key("rule").Str(k.Rule).Key("detail").Str(k.Detail).Close('}');
            }
            j.Close(']');
            j.Key("cells").Open('[');
            foreach (var c in r.Cells)
            {
                j.Open('{').Key("species").Str(c.Species).Key("policy").Str(c.Policy).Key("n").Num(c.N);
                j.Key("grades").Open('{');
                for (int i = 0; i < r.RankIds.Count; i++) j.Key(r.RankIds[i]).Num(c.Grades[i]);
                j.Close('}');
                j.Key("grade"); Dist(j, c.Grade);
                j.Key("completed").Num(c.Completed).Key("injured").Num(c.Injured).Key("injuryRate").Num(c.InjuryRate);
                j.Key("injuries"); Dist(j, c.Injuries);
                j.Key("cyclesLost"); Dist(j, c.CyclesLost);
                j.Key("cycles"); Dist(j, c.Cycles);
                j.Key("fightsWon"); Dist(j, c.FightsWon);
                j.Key("fightsLost"); Dist(j, c.FightsLost);
                j.Key("rests"); Dist(j, c.Rests);
                j.Key("injuryLoss"); Dist(j, c.InjuryLoss);
                j.Key("fightWinRate").Num(c.FightWinRate);
                j.Key("shards"); Dist(j, c.Shards);
                j.Key("gauntletFloor"); Dist(j, c.GauntletFloor);
                j.Key("gauntletCleared").Num(c.GauntletCleared).Key("gauntletClearRate").Num(c.GauntletClearRate);
                j.Key("gauntletBossStalls").Num(c.GauntletBossStalls).Key("gauntletBossStallRate").Num(c.GauntletBossStallRate);
                j.Key("gauntletStallMod").Open('[');
                foreach (var k in c.GauntletStallMod) j.Num(k);
                j.Close(']');
                j.Key("killers").Open('{');
                foreach (var k in c.Killers) j.Key(k.Key).Num(k.Count);
                j.Close('}');
                ChaseJson(j, c);
                j.Key("opponents").Open('[');
                foreach (var x in c.Opponents)
                    j.Open('{').Key("id").Str(x.Id).Key("rank").Str(r.RankIds[x.Rank]).Key("rolled").Num(x.Rolled)
                     .Key("fought").Num(x.Fought).Key("won").Num(x.Won).Key("winRate").Num(x.WinRate).Close('}');
                j.Close(']');
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
            j.Key("bookCells").Open('[');
            foreach (var b in r.BookCells)
            {
                j.Open('{').Key("species").Str(b.Species).Key("policy").Str(b.Policy).Key("n").Num(b.N);
                j.Key("grade"); Dist(j, b.Grade);
                ChaseJson(j, b);
                j.Close('}');
            }
            j.Close(']');
            j.Close('}');
            return j.ToString() + "\n";
        }

        static void ChaseJson(JsonOut j, CellSummary c)
        {
            j.Key("capstoned").Num(c.Capstoned).Key("capstoneRate").Num(c.CapstoneRate);
            j.Key("capstoneCycle"); Dist(j, c.CapstoneCycle);
            j.Key("chase").Open('[');
            foreach (var x in c.Chase)
            {
                j.Open('{').Key("tree").Str(x.TreeId).Key("unlocked").Num(x.Unlocked).Key("unlockRate").Num(x.UnlockRate)
                 .Key("capstones").Num(x.Capstones).Key("capstoneRate").Num(x.CapstoneRate);
                j.Key("capstoneCycle"); Dist(j, x.CapstoneCycle);
                j.Key("conditions").Open('[');
                foreach (var k in x.Conditions)
                    j.Open('{').Key("id").Str(k.Id).Key("label").Str(k.Label).Key("held").Num(k.Held).Key("heldRate").Num(k.HeldRate).Close('}');
                j.Close(']');
                j.Close('}');
            }
            j.Close(']');
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
            public JsonOut Null() { Sep(); _sb.Append("null"); _needComma = true; return this; }
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
        /// put the fraction in mean and leave the spread columns empty. Each sanity check gives two rows in section "check":
        /// metric = its id (mean = the measured value, empty when skipped) and id + "_warn" (mean 1 on WARN, 0 on PASS, empty
        /// when skipped); n is what the check looked at. The book canary's grades are section "books". The gauntlet's stall_mod_i
        /// rows give the share of the runs that did not clear (n) whose floor reached mod GAUNTLET_BOSS_EVERY is i. Section "chase" (and
        /// "books_chase" for the canary) holds capstone_rate and capstone_cycle, then per chase tree &lt;tree&gt;_unlock_rate,
        /// &lt;tree&gt;_capstone_rate, &lt;tree&gt;_capstone_cycle and per condition &lt;condition id&gt;_held_rate. Section "opponent" has
        /// per rank pool monster &lt;id&gt;_rolled_rate (share of the runs whose ladder had it) and &lt;id&gt;_win_rate (n = rank fights
        /// against it).
        /// </summary>
        public static string Csv(BalanceReport r)
        {
            var sb = new StringBuilder("section,species,policy,team,metric,n,mean,sd,min,p10,p50,p90,max\n");
            foreach (var c in r.Cells)
            {
                for (int i = 0; i < r.RankIds.Count; i++) Rate(sb, "run", c.Species, c.Policy, "", "grade_" + r.RankIds[i], c.N, c.N == 0 ? 0 : (double)c.Grades[i] / c.N);
                Row(sb, "run", c.Species, c.Policy, "", "grade", c.Grade);
                Rate(sb, "run", c.Species, c.Policy, "", "injury_rate", c.N, c.InjuryRate);
                Row(sb, "run", c.Species, c.Policy, "", "injuries", c.Injuries);
                Row(sb, "run", c.Species, c.Policy, "", "cycles_lost", c.CyclesLost);
                Row(sb, "run", c.Species, c.Policy, "", "cycles", c.Cycles);
                Row(sb, "run", c.Species, c.Policy, "", "fights_won", c.FightsWon);
                Row(sb, "run", c.Species, c.Policy, "", "fights_lost", c.FightsLost);
                Rate(sb, "run", c.Species, c.Policy, "", "fight_win_rate", c.N, c.FightWinRate);
                Row(sb, "run", c.Species, c.Policy, "", "shards", c.Shards);
                Row(sb, "gauntlet", c.Species, c.Policy, "", "floor", c.GauntletFloor);
                Rate(sb, "gauntlet", c.Species, c.Policy, "", "clear_rate", c.N, c.GauntletClearRate);
                Rate(sb, "gauntlet", c.Species, c.Policy, "", "boss_stall_rate", c.N - c.GauntletCleared, c.GauntletBossStallRate);
                for (int i = 0; i < c.GauntletStallMod.Length; i++)
                    Rate(sb, "gauntlet", c.Species, c.Policy, "", "stall_mod_" + i.ToString(Inv), c.N - c.GauntletCleared,
                         c.N == c.GauntletCleared ? 0 : (double)c.GauntletStallMod[i] / (c.N - c.GauntletCleared));
                foreach (var k in c.Killers) Rate(sb, "gauntlet", c.Species, c.Policy, "", "killer_" + k.Key, c.N, c.N == 0 ? 0 : (double)k.Count / c.N);
                ChaseCsv(sb, "chase", c);
                foreach (var x in c.Opponents)
                {
                    Rate(sb, "opponent", c.Species, c.Policy, "", x.Id + "_rolled_rate", c.N, c.N == 0 ? 0 : (double)x.Rolled / c.N);
                    Rate(sb, "opponent", c.Species, c.Policy, "", x.Id + "_win_rate", x.Fought, x.WinRate);
                }
            }
            foreach (var b in r.BookCells)
            {
                Row(sb, "books", b.Species, b.Policy, "", "grade", b.Grade);
                ChaseCsv(sb, "books_chase", b);
            }
            foreach (var w in r.Tower)
            {
                Row(sb, "tower", "", w.Policy, w.Team, "floors_cleared", w.FloorsCleared);
                Rate(sb, "tower", "", w.Policy, w.Team, "clear_rate", w.N, w.ClearRate);
                Row(sb, "tower", "", w.Policy, w.Team, "shards", w.Shards);
                Row(sb, "tower", "", w.Policy, w.Team, "books", w.Books);
            }
            foreach (var k in r.Checks)
            {
                bool skip = k.Status == CheckStatus.Skip;
                Check(sb, k.Id, k.N, skip ? "" : F(k.Value));
                Check(sb, k.Id + "_warn", k.N, skip ? "" : k.Status == CheckStatus.Warn ? "1" : "0");
            }
            return sb.ToString();
        }

        static void ChaseCsv(StringBuilder sb, string section, CellSummary c)
        {
            Rate(sb, section, c.Species, c.Policy, "", "capstone_rate", c.N, c.CapstoneRate);
            Row(sb, section, c.Species, c.Policy, "", "capstone_cycle", c.CapstoneCycle);
            foreach (var x in c.Chase)
            {
                Rate(sb, section, c.Species, c.Policy, "", x.TreeId + "_unlock_rate", x.N, x.UnlockRate);
                Rate(sb, section, c.Species, c.Policy, "", x.TreeId + "_capstone_rate", x.N, x.CapstoneRate);
                Row(sb, section, c.Species, c.Policy, "", x.TreeId + "_capstone_cycle", x.CapstoneCycle);
                foreach (var k in x.Conditions) Rate(sb, section, c.Species, c.Policy, "", k.Id + "_held_rate", c.N, k.HeldRate);
            }
        }

        static void Row(StringBuilder sb, string section, string species, string policy, string team, string metric, Distribution d) =>
            sb.Append(string.Join(",", section, species, policy, team, metric, d.N.ToString(Inv), F(d.Mean), F(d.Sd), F(d.Min),
                F(d.P10), F(d.P50), F(d.P90), F(d.Max))).Append('\n');

        static void Rate(StringBuilder sb, string section, string species, string policy, string team, string metric, int n, double rate) =>
            sb.Append(string.Join(",", section, species, policy, team, metric, n.ToString(Inv), F(rate), "", "", "", "", "", "")).Append('\n');

        static void Check(StringBuilder sb, string metric, int n, string mean) =>
            sb.Append(string.Join(",", "check", "", "", "", metric, n.ToString(Inv), mean, "", "", "", "", "", "")).Append('\n');

        static string F(double x) => x.ToString("0.######", Inv);
    }
}
