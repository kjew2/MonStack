using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;

namespace IdleRng.Web
{
    /// <summary>
    /// The adapter for the monster-trainer run (specs/14, 15, 13 T4/T6): one run at a time, driven by the same TrainingRun
    /// commands the bots and tests use. It adds no rules; it maps RunState and ContentDb to JSON for web/run.js. A run saves as
    /// species + seed + books + command log and loads by replay, so saves cannot drift from the engine.
    /// </summary>
    internal sealed class RunHost
    {
        private const int MaxLoggedActions = 400;

        private readonly ContentDb _db;
        private RunState _run;
        private VaultEntry _sealed;

        public RunHost(string contentJson)
        {
            if (!PackSource.LooksLikePack(contentJson)) throw new InvalidOperationException("content pack missing: pass the bundled Content/*.json pack (web/wasm/pack-content.js)");
            _db = ContentDb.Load(new PackSource(contentJson), verifyHashes: true);
        }

        public string ContentVersion => _db.Version;

        // ------------------------------------------------------------------ content (labels, never changes in a session)

        public string Content()
        {
            var k = _db.Constants;
            var w = new JsonOut().Obj();
            w.P("version", _db.Version);
            w.Key("consts").Obj()
                .P("runCycles", k.RunCycles).P("runAp", k.RunAp).P("energyMax", k.RunEnergyMax).P("fightEvery", k.FightEvery)
                .P("statMax", k.StatMax).P("treeSlots", k.TreeSlots).P("drillEnergyCost", k.DrillEnergyCost).P("restEnergy", k.RestEnergy)
                .P("spPerCycle", k.SpPerCycle).P("spPerWin", k.SpPerWin).P("superP", k.DrillSuperP)
                .EndObj();
            w.Key("stats").Arr();
            foreach (var id in StatIds.Trained) w.Str(id);
            w.EndArr();
            w.Key("milestones").Arr();
            foreach (var m in _db.StatMilestones) w.Int(m);
            w.EndArr();
            w.Key("elements").Obj();
            foreach (var e in _db.ElementList) w.Key(e.Id).Obj().P("name", _db.Text(e.NameKey)).P("color", e.DisplayColor).EndObj();
            w.EndObj();
            w.Key("species").Arr();
            foreach (var sp in _db.SpeciesList)
            {
                w.Obj().P("id", sp.Id).P("name", _db.Text(sp.NameKey)).P("element", sp.Element);
                w.Key("base"); Stats(w, sp.Base);
                w.Key("growth"); Stats(w, sp.Growth);
                w.Key("trees").Arr().Str(sp.NativeTree1).Str(sp.NativeTree2).EndArr();
                w.EndObj();
            }
            w.EndArr();
            w.Key("ranks").Arr();
            foreach (var r in _db.FightRanks)
            {
                w.Obj().P("id", r.Id).P("name", _db.Text(r.NameKey)).P("letter", r.Id.ToUpperInvariant()).P("element", r.Element);
                w.Key("stats"); Stats(w, r.Stats);
                w.EndObj();
            }
            w.EndArr();
            w.Key("trees").Obj();
            foreach (var t in _db.Trees.Values.OrderBy(t => t.Id, StringComparer.Ordinal)) Tree(w, t);
            w.EndObj();
            w.Key("books").Obj();
            foreach (var b in _db.SkillBooks.Values) w.Key(b.Id).Obj().P("name", _db.Text(b.NameKey)).P("tree", b.TreeId).EndObj();
            w.EndObj();
            return w.EndObj().ToString();
        }

        private void Tree(JsonOut w, TreeDef t)
        {
            w.Key(t.Id).Obj().P("id", t.Id).P("name", _db.Text(t.NameKey)).P("kind", t.Kind.ToString().ToLowerInvariant());
            w.Key("nodes").Arr();
            for (int i = 0; i < t.Nodes.Count; i++)
            {
                var n = t.Nodes[i];
                var (name, desc) = NodeText(n);
                w.Obj().P("id", n.Id).P("cost", n.Cost).P("kind", n.Kind.ToString().ToLowerInvariant()).P("name", name).P("desc", desc)
                    .P("last", i == t.Nodes.Count - 1).EndObj();
            }
            w.EndArr();
            w.Key("conditions").Arr();
            if (t.Conditions != null) foreach (var cd in t.Conditions) w.Str(ConditionText(cd));
            w.EndArr();
            w.EndObj();
        }

        private (string name, string desc) NodeText(TreeNodeDef n)
        {
            switch (n.Kind)
            {
                case NodeKind.Growth:
                    return ("+" + Num(n.Amount) + " " + n.Stat.ToUpperInvariant() + " growth", "Drills raise " + n.Stat.ToUpperInvariant() + " faster.");
                case NodeKind.Stat:
                    return ("+" + Num(n.Amount) + " " + n.Stat.ToUpperInvariant(), "A flat boost, lost if this tree is overwritten.");
                case NodeKind.Skill:
                    return (_db.Text("mskill." + n.Skill.Id + ".name"), _db.Text("mskill." + n.Skill.Id + ".desc"));
                case NodeKind.Trigger:
                    return (_db.Text("mskill." + n.Skill.Id + ".name") + " (" + TriggerText(n.Trigger) + ")", _db.Text("mskill." + n.Skill.Id + ".desc"));
                default:
                    return (_db.Text("rule." + n.RuleId + ".name"), _db.Text("rule." + n.RuleId + ".desc"));
            }
        }

        private static string TriggerText(FightTrigger? t)
        {
            switch (t)
            {
                case FightTrigger.BattleStart: return "battle start";
                case FightTrigger.OnAction: return "each action";
                case FightTrigger.OnBasicAttack: return "on basic attack";
                case FightTrigger.OnCrit: return "on crit";
                case FightTrigger.OnHitTaken: return "when hit";
                case FightTrigger.OnEvade: return "on evade";
                default: return "trigger";
            }
        }

        private string ConditionText(ChaseConditionDef c)
        {
            switch (c.Type)
            {
                case ConditionType.TreeMaxed: return "Max out " + _db.Text(_db.Trees[c.Ref].NameKey);
                case ConditionType.NodeOwned:
                {
                    var n = _db.TreeNodes[c.Ref];
                    return "Buy node " + (n.Index + 1) + " of " + _db.Text(_db.Trees[n.TreeId].NameKey);
                }
                case ConditionType.StatAtLeast: return c.Ref.ToUpperInvariant() + " at least " + Num(c.Amount);
                default: return "Reach " + Num(c.Amount) + " " + c.Ref.Replace('_', ' ') + " across rank fights";
            }
        }

        // ------------------------------------------------------------------ run lifecycle

        public string Start(string speciesId, string seedHex, string booksJson)
        {
            ulong seed = ulong.Parse(string.IsNullOrEmpty(seedHex) ? "0" : seedHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var books = ReadStrings(booksJson);
            _run = TrainingRun.Start(_db, speciesId, seed, books);
            _sealed = null;
            return State();
        }

        /// <summary>Saved form of the run: species, seed, books and command log. Null when no run.</summary>
        public string Save()
        {
            if (_run == null) return "null";
            var books = new List<string>(_run.Books);
            foreach (var l in _run.Log) if (l.Kind == RunCommandKind.UseBook) books.Add(l.Book);
            var w = new JsonOut().Obj().P("v", 1).P("species", _run.SpeciesId).P("seed", _run.Seed.ToString("x16", CultureInfo.InvariantCulture));
            w.Key("books").Arr();
            foreach (var b in books) w.Str(b);
            w.EndArr();
            w.Key("log"); Log(w, _run.Log);
            return w.EndObj().ToString();
        }

        public string Load(string saveJson)
        {
            using var doc = JsonDocument.Parse(saveJson);
            var r = doc.RootElement;
            ulong seed = ulong.Parse(r.GetProperty("seed").GetString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var books = r.GetProperty("books").EnumerateArray().Select(e => e.GetString()).ToList();
            _run = TrainingRun.Replay(_db, r.GetProperty("species").GetString(), seed, ReadLog(r.GetProperty("log")), books);
            _sealed = null;
            return State();
        }

        public void Abandon() { _run = null; _sealed = null; }

        private static List<string> ReadStrings(string json)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.EnumerateArray()) list.Add(e.GetString());
            return list;
        }

        private static void Log(JsonOut w, List<RunCommand> log)
        {
            w.Arr();
            foreach (var l in log)
            {
                w.Obj().P("k", l.Kind.ToString());
                if (l.Stat != null) w.P("stat", l.Stat);
                if (l.Kind == RunCommandKind.TakeTree) w.P("choice", l.Choice);
                if (l.Kind == RunCommandKind.TakeTree || l.Kind == RunCommandKind.SpendPoint) w.P("slot", l.Slot);
                if (l.Book != null) w.P("book", l.Book);
                w.EndObj();
            }
            w.EndArr();
        }

        private static List<RunCommand> ReadLog(JsonElement arr)
        {
            var log = new List<RunCommand>();
            foreach (var e in arr.EnumerateArray())
            {
                var c = new RunCommand { Kind = (RunCommandKind)Enum.Parse(typeof(RunCommandKind), e.GetProperty("k").GetString()) };
                if (e.TryGetProperty("stat", out var st)) c.Stat = st.GetString();
                if (e.TryGetProperty("choice", out var ch)) c.Choice = ch.GetInt32();
                if (e.TryGetProperty("slot", out var sl)) c.Slot = sl.GetInt32();
                if (e.TryGetProperty("book", out var bk)) c.Book = bk.GetString();
                log.Add(c);
            }
            return log;
        }

        // ------------------------------------------------------------------ commands

        /// <summary>One command; the result carries what happened for instant feedback. Invalid commands return {ok:false,reason}.</summary>
        public string Cmd(string name, string argsJson)
        {
            if (_run == null) return Fail("no run");
            var a = new Args(argsJson);
            var s = _run;
            var w = new JsonOut().Obj();
            try
            {
                switch (name)
                {
                    case "drill":
                    {
                        var r = TrainingRun.Drill(_db, s, a.Str(0));
                        w.P("ok", true).P("kind", "drill").P("stat", r.Stat).P("tier", r.Tier.ToString().ToLowerInvariant())
                            .P("tierName", _db.Text("drill." + r.Tier.ToString().ToLowerInvariant() + ".name"))
                            .P("gain", r.Gain).P("value", r.NewValue).P("energy", r.EnergyAfter).P("injured", r.Injured);
                        Milestones(w, r.Milestones);
                        break;
                    }
                    case "rest":
                    {
                        var r = TrainingRun.Rest(_db, s);
                        w.P("ok", true).P("kind", "rest").P("restored", r.Restored).P("energy", r.EnergyAfter);
                        break;
                    }
                    case "fight":
                    {
                        var actions = new List<FightAction>();
                        var r = TrainingRun.Fight(_db, s, actions.Add);
                        w.P("ok", true).P("kind", "fight").P("won", r.Won).P("rankBefore", r.RankBefore).P("rankAfter", r.RankAfter).P("sp", r.SpGained);
                        w.P("outcome", r.Fight.Outcome.ToString()).P("ticks", r.Fight.Ticks);
                        w.Key("units").Arr();
                        foreach (var u in r.Fight.Final.Units)
                            w.Obj().P("id", u.Id).P("side", u.Side).P("hp", Math.Max(0, u.Hp)).P("maxHp", u.MaxHp)
                                .P("damage", u.Tally.DamageDealt).P("crits", u.Tally.Crits).P("evades", u.Tally.Evades).EndObj();
                        w.EndArr();
                        w.P("actionCount", actions.Count);
                        w.Key("actions").Arr();
                        foreach (var x in actions.Take(MaxLoggedActions)) Action(w, x);
                        w.EndArr();
                        break;
                    }
                    case "take":
                    {
                        var r = TrainingRun.TakeTree(_db, s, a.Int(0), a.Int(1));
                        w.P("ok", true).P("kind", "take").P("placed", r.Placed);
                        w.Key("removed").Arr();
                        foreach (var id in r.Removed) w.Str(id);
                        w.EndArr();
                        break;
                    }
                    case "discard": TrainingRun.DiscardOffer(_db, s); w.P("ok", true).P("kind", "discard"); break;
                    case "spend":
                    {
                        var r = TrainingRun.SpendPoint(_db, s, a.Int(0));
                        var (nn, nd) = NodeText(r.Node);
                        w.P("ok", true).P("kind", "spend").P("node", nn).P("desc", nd).P("statGain", r.StatGain);
                        Milestones(w, r.Milestones);
                        break;
                    }
                    case "book": TrainingRun.UseBook(_db, s, a.Str(0)); w.P("ok", true).P("kind", "book"); break;
                    default: return Fail("unknown command");
                }
            }
            catch (Exception e) when (e is InvalidOperationException || e is ArgumentException)
            {
                return Fail(e.Message);   // the engine refused the command; state is unchanged
            }
            return w.EndObj().ToString();
        }

        private static string Fail(string reason) => new JsonOut().Obj().P("ok", false).P("reason", reason).EndObj().ToString();

        private void Milestones(JsonOut w, List<MilestoneHit> hits)
        {
            w.Key("milestones").Arr();
            foreach (var m in hits) w.Obj().P("stat", m.Stat).P("threshold", m.Threshold).EndObj();
            w.EndArr();
        }

        private void Action(JsonOut w, FightAction x)
        {
            w.Obj().P("actor", x.Actor).P("target", x.Target).P("amount", x.Amount).P("crit", x.Crit).P("evaded", x.Evaded).P("aoe", x.Aoe).P("counter", x.Counter);
            if (x.Skill != null) w.P("skill", _db.Text("mskill." + x.Skill.Id + ".name")).P("kind", x.Skill.Kind.ToString().ToLowerInvariant());
            w.EndObj();
        }

        // ------------------------------------------------------------------ vault

        /// <summary>Freeze the finished run. Idempotent: the same entry comes back until a new run starts.</summary>
        public string Seal(double utcMs)
        {
            if (_run == null) return Fail("no run");
            if (_run.Phase != RunPhase.Finished) return Fail("the run is not finished");
            _sealed ??= Vault.Seal(_db, _run, (long)utcMs);
            var e = _sealed;
            var w = new JsonOut().Obj().P("ok", true);
            w.Key("entry").Obj().P("v", 1).P("species", e.SpeciesId).P("seed", e.Seed.ToString("x16", CultureInfo.InvariantCulture))
                .P("grade", e.Grade).P("rank", e.RankIndex).P("end", e.End.ToString().ToLowerInvariant()).P("cycles", e.Cycles)
                .P("fightsFought", e.FightsFought).P("fightsWon", e.FightsWon).P("hash", e.RunHash).P("sealedUtcMs", e.SealedUtcMs);
            w.Key("stats"); Stats(w, e.Stats);
            w.Key("trees").Arr();
            foreach (var t in e.Trees) w.Obj().P("id", t.TreeId).P("bought", t.Bought).EndObj();
            w.EndArr();
            w.Key("books").Arr();
            foreach (var b in e.Books) w.Str(b);
            w.EndArr();
            w.Key("log"); Log(w, e.Log);
            w.EndObj();
            return w.EndObj().ToString();
        }

        // ------------------------------------------------------------------ state

        public string State()
        {
            if (_run == null) return "null";
            var s = _run;
            var k = _db.Constants;
            var sp = _db.Species[s.SpeciesId];
            double e = TrainingRun.EnergyFraction(k, s.Energy);
            double fail = TrainingRun.FailChance(k, e);
            var w = new JsonOut().Obj();
            w.P("species", s.SpeciesId).P("seed", s.Seed.ToString("x16", CultureInfo.InvariantCulture));
            w.P("phase", s.Phase.ToString().ToLowerInvariant()).P("end", s.End.ToString().ToLowerInvariant());
            w.P("cycle", s.Cycle).P("ap", s.ApLeft).P("energy", s.Energy).P("sp", s.Sp).P("rank", s.Rank);
            w.P("fightsFought", s.FightsFought).P("fightsWon", s.FightsWon).P("drills", s.Drills).P("rests", s.Rests);
            w.P("fightDue", s.Phase == RunPhase.AwaitFight);
            w.Key("odds").Obj().P("fail", fail).P("super", k.DrillSuperP).P("success", Math.Max(0, 1 - fail - k.DrillSuperP))
                .P("injury", TrainingRun.InjuryChance(k, e)).EndObj();
            w.Key("stats"); Stats(w, s.Stats);
            w.Key("growth").Obj();
            foreach (var id in StatIds.Trained) w.P(id, TrainingRun.Growth(_db, s, id));
            w.EndObj();
            var opp = _db.FightRanks[s.Rank];
            w.Key("opponent").Obj().P("name", _db.Text(opp.NameKey)).P("element", opp.Element);
            w.Key("stats"); Stats(w, opp.Stats);
            w.EndObj();
            w.Key("slots").Arr();
            foreach (var t in s.Slots)
            {
                if (t == null) { w.Null(); continue; }
                w.Obj().P("tree", t.TreeId).P("bought", t.Bought).EndObj();
            }
            w.EndArr();
            w.Key("offers").Arr();
            foreach (var o in s.Offers)
            {
                w.Obj().P("source", o.Source.ToString().ToLowerInvariant()).P("stat", o.Stat).P("threshold", o.Threshold);
                w.Key("trees").Arr();
                foreach (var t in o.Trees) w.Str(t);
                w.EndArr().EndObj();
            }
            w.EndArr();
            w.Key("books").Arr();
            foreach (var b in s.Books) w.Str(b);
            w.EndArr();
            w.Key("chase").Arr();   // chase trees not yet unlocked: what is still missing
            foreach (var t in _db.Trees.Values.Where(t => t.Kind == TreeKind.Chase).OrderBy(t => t.Id, StringComparer.Ordinal))
            {
                w.Obj().P("tree", t.Id).P("unlocked", TrainingRun.ChaseConditionsHold(_db, s, t));
                w.Key("met").Arr();
                foreach (var cd in t.Conditions) w.Bool(TrainingRun.ConditionHolds(_db, s, cd));
                w.EndArr().EndObj();
            }
            w.EndArr();
            w.Key("tally").Obj().P("damage", s.Tally.DamageDealt).P("crits", s.Tally.Crits).P("evades", s.Tally.Evades).P("kills", s.Tally.Kills).EndObj();
            w.Key("milestonesHit").Arr();
            foreach (var m in s.Milestones) w.Obj().P("stat", m.Stat).P("threshold", m.Threshold).EndObj();
            w.EndArr();
            w.P("hash", TrainingRun.StateHash(s));
            return w.EndObj().ToString();
        }

        private static void Stats(JsonOut w, MonsterStats m)
        {
            w.Obj();
            foreach (var id in StatIds.Trained) w.P(id, m.Get(id));
            w.EndObj();
        }

        private static string Num(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
