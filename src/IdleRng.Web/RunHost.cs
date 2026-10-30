using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using IdleRng.Runtime.Save;
using IdleRng.Sim.Account;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;

namespace IdleRng.Web
{
    /// <summary>
    /// The adapter for the monster trainer (specs/13 T4-T6, 14, 15, 18): holds the player's AccountState and its active run,
    /// driven by the same AccountOps and TrainingRun commands the bots and tests use. It adds no rules; it maps AccountState,
    /// RunState and ContentDb to JSON for web/run.js. The save is AccountSave (the active run as its replay recipe), so
    /// saves cannot drift from the engine.
    /// </summary>
    internal sealed class RunHost
    {
        private const int MaxLoggedActions = 400;

        private readonly ContentDb _db;
        private AccountState _acct;
        private RunState _run => _acct?.Active;

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
                .P("shardsPerCycle", k.ShardsPerCycle).P("shardsPerRank", k.ShardsPerRank)
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
            w.Key("talents").Arr();
            foreach (var t in _db.TalentList)
                w.Obj().P("id", t.Id).P("name", _db.Text(t.NameKey)).P("desc", _db.Text(t.DescKey)).P("effect", t.Effect.ToString().ToLowerInvariant())
                    .P("maxRank", t.MaxRank).EndObj();
            w.EndArr();
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

        // ------------------------------------------------------------------ account lifecycle

        public string NewAccount(string seedHex)
        {
            ulong seed = ulong.Parse(string.IsNullOrEmpty(seedHex) ? "0" : seedHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            _acct = AccountOps.New(_db, seed);
            return AccountView();
        }

        /// <summary>Saved form (AccountSave text: a header line, then JSON). The active run is its replay recipe.</summary>
        public string SaveAccount() => _acct == null ? "null" : Encoding.UTF8.GetString(AccountSave.Encode(_acct));

        /// <summary>Rebuild the account from its save. Throws (returned as {"error"}) when the save is damaged or no longer replays.</summary>
        public string LoadAccount(string saveText)
        {
            _acct = AccountSave.Decode(_db, Encoding.UTF8.GetBytes(saveText));
            return AccountView();
        }

        /// <summary>Account commands. Invalid ones return {ok:false,reason} and leave the account unchanged.</summary>
        public string AccountCmd(string name, string argsJson)
        {
            if (_acct == null) return Fail("no account");
            var a = new Args(argsJson);
            var w = new JsonOut().Obj();
            try
            {
                switch (name)
                {
                    case "startRun":
                        AccountOps.StartRun(_db, _acct, a.Str(0), a.Has(1) ? BookList(a.El(1)) : null);
                        w.P("ok", true);
                        break;
                    case "finishRun":
                    {
                        var r = AccountOps.FinishRun(_db, _acct, (long)a.Num(0));
                        w.P("ok", true).P("shards", r.Shards);
                        w.Key("entry"); Entry(w, r.Entry);
                        w.Key("booksReturned").Arr();
                        foreach (var b in r.BooksReturned) w.Str(b);
                        w.EndArr();
                        break;
                    }
                    case "buyTalent":
                        w.P("ok", true).P("cost", AccountOps.BuyTalent(_db, _acct, a.Str(0)));
                        break;
                    case "abandon":
                    {
                        // Abandoning pays nothing and seals nothing; unused books go back to the inventory so a misclick costs none.
                        var run = _acct.Active ?? throw new InvalidOperationException("no run in progress");
                        foreach (var b in run.Books) AccountOps.GrantBooks(_db, _acct, b, 1);
                        _acct.Active = null;
                        w.P("ok", true);
                        break;
                    }
                    case "debugGrantBook":
                        AccountOps.GrantBooks(_db, _acct, a.Str(0), a.Has(1) ? a.Int(1) : 1);
                        w.P("ok", true);
                        break;
                    default: return Fail("unknown command");
                }
            }
            catch (Exception e) when (e is InvalidOperationException || e is ArgumentException)
            {
                return Fail(e.Message);
            }
            return w.EndObj().ToString();
        }

        private static List<string> BookList(JsonElement arr)
        {
            var list = new List<string>();
            foreach (var e in arr.EnumerateArray()) list.Add(e.GetString());
            return list;
        }

        /// <summary>The account without the active run: shards, books, talents (with the next rank's cost), the Vault, the bonuses runs start with.</summary>
        public string AccountView()
        {
            if (_acct == null) return "null";
            var ac = _acct;
            var m = AccountOps.Modifiers(_db, ac);
            var w = new JsonOut().Obj();
            w.P("seed", ac.Seed.ToString("x16", CultureInfo.InvariantCulture)).P("runsStarted", ac.RunsStarted).P("shards", ac.Shards).P("hasRun", ac.Active != null);
            w.Key("owned").Arr();
            foreach (var s in ac.OwnedSpecies) w.Str(s);
            w.EndArr();
            w.Key("books").Obj();
            foreach (var kv in ac.Books) w.P(kv.Key, kv.Value);
            w.EndObj();
            w.Key("talents").Arr();
            foreach (var t in _db.TalentList)
            {
                int rank = ac.TalentRank(t.Id);
                w.Obj().P("id", t.Id).P("rank", rank);
                if (rank < t.MaxRank) w.P("nextCost", AccountOps.TalentCost(t, rank)); else w.Key("nextCost").Null();
                w.EndObj();
            }
            w.EndArr();
            w.Key("mods").Obj().P("drillCostMult", m.DrillCostMult).P("statFloorMult", m.StatFloorMult).P("draftSizeBonus", m.DraftSizeBonus).P("startSp", m.StartSp).EndObj();
            w.Key("vault").Arr();
            foreach (var e in ac.Vault) Entry(w, e);
            w.EndArr();
            return w.EndObj().ToString();
        }

        private static void Entry(JsonOut w, VaultEntry e)
        {
            w.Obj().P("species", e.SpeciesId).P("seed", e.Seed.ToString("x16", CultureInfo.InvariantCulture))
                .P("grade", e.Grade).P("rank", e.RankIndex).P("end", e.End.ToString().ToLowerInvariant()).P("cycles", e.Cycles)
                .P("fightsFought", e.FightsFought).P("fightsWon", e.FightsWon).P("hash", e.RunHash).P("sealedUtcMs", e.SealedUtcMs);
            w.Key("stats"); Stats(w, e.Stats);
            w.Key("trees").Arr();
            foreach (var t in e.Trees) w.Obj().P("id", t.TreeId).P("bought", t.Bought).EndObj();
            w.EndArr();
            w.EndObj();
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
            w.P("drillCost", TrainingRun.DrillCost(_db, s));
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
