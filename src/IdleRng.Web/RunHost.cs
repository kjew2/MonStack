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
using IdleRng.Sim.Gauntlet;
using IdleRng.Sim.Tower;
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
                .P("spPerCycle", k.SpPerCycle).P("spPerWin", k.SpPerWin).P("treeRefund", k.TreeRefund).P("superP", k.DrillSuperP)
                .P("shardsPerCycle", k.ShardsPerCycle).P("shardsPerRank", k.ShardsPerRank).P("vaultCap", k.VaultCap).P("vendorOffers", k.VendorOffers)
                .P("injurySkipCycles", k.InjurySkipCycles).P("injuryRecoverEnergy", k.InjuryRecoverEnergy)
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
                w.Key("chase").Arr().Str(sp.ChaseTreeRun).Str(sp.ChaseTreeBook).EndArr();   // in-run, then book-gated
                w.P("book", SpeciesBook(sp));
                w.P("starter", sp.Id == _db.StarterSpecies.Id);
                var unlock = _db.AchievementList.FirstOrDefault(x => x.UnlocksSpecies == sp.Id);   // how a locked species is unlocked
                if (unlock == null) w.Key("unlock").Null();
                else w.Key("unlock").Obj().P("achievement", unlock.Id).P("name", _db.Text(unlock.NameKey)).P("text", AchievementText(unlock)).EndObj();
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
                    .P("rarity", t.Rarity).P("maxRank", t.MaxRank).EndObj();
            w.EndArr();
            w.Key("achievements").Arr();
            foreach (var d in _db.AchievementList)
                w.Obj().P("id", d.Id).P("name", _db.Text(d.NameKey)).P("text", AchievementText(d)).P("species", d.UnlocksSpecies).EndObj();
            w.EndArr();
            w.Key("rarities").Arr();
            foreach (var r in _db.VendorRarities) w.Obj().P("id", r.Id).P("name", _db.Text(r.NameKey)).EndObj();
            w.EndArr();
            w.Key("gauntlet").Obj().P("floors", k.GauntletFloors).P("bossEvery", k.GauntletBossEvery);
            w.Key("enemies").Obj();
            foreach (var g in _db.GauntletEnemies.Concat(_db.GauntletBosses))
                w.Key(g.Id).Obj().P("name", _db.Text(g.NameKey)).P("element", g.Element).P("boss", g.Boss).EndObj();
            w.EndObj().EndObj();
            TipJson.StatInfo(w, _db);
            TipJson.NodeTips(w, _db, NodeText);
            TipJson.TowerTips(w, _db);
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
            w.P("source", TreeSource(t));
            if (t.Kind == TreeKind.Chase) w.P("species", t.Species).P("bookGated", t.BookGated);
            w.Key("conditions").Arr();
            if (t.Conditions != null) foreach (var cd in t.Conditions) w.Str(ConditionText(cd));
            w.EndArr();
            w.Key("conditionTrees").Arr();   // trees the conditions name, so the UI can say where each comes from
            if (t.Conditions != null)
                foreach (var cd in t.Conditions)
                {
                    if (cd.Type == ConditionType.TreeMaxed) w.Str(cd.Ref);
                    else if (cd.Type == ConditionType.NodeOwned) w.Str(_db.TreeNodes[cd.Ref].TreeId);
                }
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
                    return (_db.Text("mskill." + n.Skill.Id + ".name"), BattleText.SkillDesc(_db, n.Skill));
                case NodeKind.Trigger:
                    return (_db.Text("mskill." + n.Skill.Id + ".name") + " (" + BattleText.TriggerName(_db, n.Trigger.Value) + ")", BattleText.SkillDesc(_db, n.Skill));
                default:
                    return (_db.Text("rule." + n.RuleId + ".name"), BattleText.RuleDesc(_db, n.RuleId));
            }
        }

        /// <summary>Where a tree comes from, in words: tells the player how to get a tree a chase condition asks for.</summary>
        private string TreeSource(TreeDef t)
        {
            switch (t.Kind)
            {
                case TreeKind.Native:
                {
                    var owners = _db.SpeciesList.Where(sp => sp.NativeTree1 == t.Id || sp.NativeTree2 == t.Id).Select(sp => _db.Text(sp.NameKey)).ToList();
                    return owners.Count == 0 ? "A native tree" : "Native tree of " + string.Join(" and ", owners) + " (the monster starts with it)";
                }
                case TreeKind.Generic:
                    return "Offered as a draft when " + (t.OfferStat == null ? "any stat" : t.OfferStat.ToUpperInvariant()) + " first reaches " + t.OfferThreshold;
                case TreeKind.Book:
                {
                    var b = _db.SkillBooks.Values.FirstOrDefault(x => x.TreeId == t.Id);
                    return b == null ? "From a skill book" : "From the skill book " + _db.Text(b.NameKey) + " (bring it into a run)";
                }
                default:
                {
                    string owner = t.Species != null && _db.Species.TryGetValue(t.Species, out var sp) ? _db.Text(sp.NameKey) : "its species";
                    return t.BookGated ? "Book-gated chase tree of " + owner + ": unlocks when its conditions hold, one of which needs a skill book tree"
                        : "Chase tree of " + owner + ": unlocks when its conditions hold";
                }
            }
        }

        /// <summary>The skill book whose tree the species' book-gated chase tree needs (the first one), or null.</summary>
        private string SpeciesBook(SpeciesDef sp)
        {
            if (!_db.Trees.TryGetValue(sp.ChaseTreeBook, out var t)) return null;
            foreach (var tree in _db.BookTreesNeeded(t))
            {
                var b = _db.SkillBooks.Values.Where(x => x.TreeId == tree).OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
                if (b != null) return b.Id;
            }
            return null;
        }

        /// <summary>An achievement's condition in words (specs/18 section 8).</summary>
        private string AchievementText(AchievementDef d)
        {
            switch (d.Kind)
            {
                case AchievementKind.RunsFinished: return "Finish " + Num(d.Amount) + (d.Amount == 1 ? " run" : " runs");
                case AchievementKind.RankReached: return "Reach rank " + (d.Ref ?? "?").ToUpperInvariant() + " in a run";
                case AchievementKind.StatReached: return "Finish a run with " + (d.Ref == null ? "any stat" : d.Ref.ToUpperInvariant()) + " at " + Num(d.Amount) + " or more";
                default: return "Clear " + Num(d.Amount) + " gauntlet floors";
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

        /// <summary>
        /// Rebuild the account from its save. Throws (returned as {"error"}) when the save is damaged or from a newer schema.
        /// A run in progress that no longer replays is dropped instead; the view then carries a <c>notice</c> sentence (specs/06 section 5).
        /// </summary>
        public string LoadAccount(string saveText)
        {
            _acct = AccountSave.Decode(_db, Encoding.UTF8.GetBytes(saveText), out var notice);
            return AccountView(notice);
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
                        w.P("ok", true).P("shards", r.Shards).P("ticketPieces", r.TicketPieces).P("ticketsEarned", r.TicketsEarned);
                        w.Key("entry"); Entry(w, r.Entry);
                        w.Key("booksReturned").Arr();
                        foreach (var b in r.BooksReturned) w.Str(b);
                        w.EndArr();
                        w.Key("achievements").Arr();   // earned by this run, for the unlock notice
                        foreach (var id in r.Achievements)
                        {
                            var d = _db.Achievements[id];
                            bool unlocked = d.UnlocksSpecies != null && r.SpeciesUnlocked.Contains(d.UnlocksSpecies);
                            w.Obj().P("id", id).P("name", _db.Text(d.NameKey)).P("text", AchievementText(d))
                                .P("species", unlocked ? d.UnlocksSpecies : null)
                                .P("speciesName", unlocked ? _db.Text(_db.Species[d.UnlocksSpecies].NameKey) : null).EndObj();
                        }
                        w.EndArr();
                        break;
                    }
                    case "gauntlet":
                        Climb(w, a.Int(0));
                        break;
                    case "towerInfo":
                        TowerInfo(w, a.Int(0));
                        break;
                    case "runTower":
                        RunTower(w, a.Int(0), a.El(1));
                        break;
                    case "towerFloor":
                        TowerFloorFight(w, a.Int(0), a.El(1), a.Int(2));
                        break;
                    case "buyOffer":
                    {
                        int i = a.Int(0);
                        var offer = _acct.Vendor != null && i >= 0 && i < _acct.Vendor.Offers.Count ? _acct.Vendor.Offers[i] : null;
                        w.P("ok", true).P("cost", AccountOps.BuyOffer(_db, _acct, i)).P("talent", offer.Talent);
                        break;
                    }
                    case "abandon":
                        AccountOps.AbandonRun(_db, _acct);
                        w.P("ok", true);
                        break;
                    case "dismantle":
                        w.P("ok", true).P("refund", AccountOps.Dismantle(_db, _acct, a.Int(0)));
                        break;
                    case "lock":
                        AccountOps.SetLocked(_db, _acct, a.Int(0), a.Bool(1));
                        w.P("ok", true);
                        break;
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

        /// <summary>
        /// The floors a Vault entry fought in its gauntlet, for the wheel (specs/19 section 7): GauntletSim.Run replayed with the
        /// entry's fighter and seed. Read only. The hash equals the entry's stored gauntlet hash when the replay matches.
        /// </summary>
        private void Climb(JsonOut w, int index)
        {
            if (index < 0 || index >= _acct.Vault.Count) throw new ArgumentException("no Vault entry " + index);
            var e = _acct.Vault[index];
            var g = GauntletSim.Run(_db, e.Fighter(_db), e.Seed);
            w.P("ok", true).P("floorReached", g.FloorReached).P("cleared", g.Cleared).P("killer", g.KillerId).P("hash", g.Hash);
            w.Key("floors").Arr();
            foreach (var f in g.Floors) w.Obj().P("floor", f.Floor).P("enemy", f.EnemyId).P("boss", f.Boss).P("won", f.Won).P("ticks", f.Ticks).EndObj();
            w.EndArr();
        }

        /// <summary>
        /// The Dungeon on a day (specs/21 section 8), read only: tickets (held plus the free ones due), ticket pieces, today's and
        /// the best floors cleared, and the day's lineup with each boss's name, element, level and phases.
        /// </summary>
        private void TowerInfo(JsonOut w, int day)
        {
            var t = _acct.Tower ?? new TowerProgress();
            w.P("ok", true).P("day", day).P("tickets", AccountOps.TowerTickets(_db, _acct, day))
                .P("ticketsPerDay", _db.Constants.TowerAttemptsPerDay).P("ticketCap", _db.Constants.TowerTicketCap)
                .P("ticketPieces", t.TicketPieces).P("piecesPerTicket", _db.Constants.TowerTicketPieces)
                .P("clearedToday", day == t.Day ? t.ClearedToday : 0)
                .P("clearedEver", t.ClearedEver).P("lastDay", t.Day).P("teamMax", _db.Constants.MonSideSize);
            w.Key("floors").Arr();
            foreach (var p in TowerSim.Lineup(_db, day))
            {
                var d = _db.TowerBosses[p.BossId];
                w.Obj().P("floor", p.Floor).P("boss", p.BossId).P("name", _db.Text(d.NameKey)).P("element", d.Element)
                    .P("marquee", p.Marquee).P("level", Math.Round(p.Level));
                w.Key("phases").Arr();
                foreach (var ph in d.Phases)
                    w.Obj().P("kind", ph.Kind).P("name", _db.Text("phase." + ph.Kind + ".name")).P("desc", _db.Text("phase." + ph.Kind + ".desc"))
                        .P("enterHp", ph.EnterHp).EndObj();
                w.EndArr().EndObj();
            }
            w.EndArr();
        }

        /// <summary>
        /// One Dungeon attempt (AccountOps.RunTower): the rewards, each floor fought (with its fight hash), and the last floor's
        /// fight for playback (TowerFight). The fight log comes from replaying the attempt with observers, which gives the identical
        /// result (the hash is checked). The other floors' fights come from towerFloor, on demand.
        /// </summary>
        private void RunTower(JsonOut w, int day, JsonElement indices)
        {
            var idx = Indices(indices);
            var r = AccountOps.RunTower(_db, _acct, idx, day);
            var res = r.Result;
            var last = res.Floors[res.Floors.Count - 1];
            var (again, log, final) = ReplayTower(idx, day, last.Floor);
            if (again.Hash != res.Hash) throw new InvalidOperationException("the tower replay differs from the attempt");

            w.P("ok", true).P("shards", r.Shards).P("ticketsLeft", r.TicketsLeft).P("floorReached", res.FloorReached)
                .P("cleared", res.Cleared).P("floorsCleared", res.FloorsCleared).P("hash", res.Hash);
            w.Key("books").Arr();
            foreach (var (floor, book) in r.Books) w.Obj().P("floor", floor).P("book", book).P("name", _db.Text(_db.SkillBooks[book].NameKey)).EndObj();
            w.EndArr();
            w.Key("firstClears").Arr();
            foreach (var f in r.FirstClears) w.Int(f);
            w.EndArr();
            w.Key("floors").Arr();
            foreach (var f in res.Floors)
            {
                w.Obj().P("floor", f.Floor).P("boss", f.BossId).P("name", TowerSim.BossName(_db, f.BossId)).P("marquee", f.Marquee)
                    .P("won", f.Won).P("ticks", f.Ticks).P("vents", f.Vents).P("corruption", f.PeakCorruption).P("hash", f.FightHash);
                w.Key("phases").Arr();
                foreach (var p in f.Phases) w.Str(_db.Text("phase." + p + ".name"));
                w.EndArr().EndObj();
            }
            w.EndArr();
            w.Key("final").Obj();
            TowerFight(w, last, idx, log, final);
            w.EndObj();
        }

        /// <summary>
        /// One floor's fight of a Dungeon attempt for playback, read only (specs/21 section 8). The same team (Vault indices in
        /// slot order) on the same day replays the same fights, since a result is a pure function of them (specs/21 section 2), so
        /// this pays and counts nothing. Refused when the team does not reach that floor.
        /// </summary>
        private void TowerFloorFight(JsonOut w, int day, JsonElement indices, int floor)
        {
            if (day < 0) throw new ArgumentException("the day must not be negative");
            var idx = Indices(indices);
            var (res, log, final) = ReplayTower(idx, day, floor);
            var f = res.Floors.FirstOrDefault(x => x.Floor == floor) ?? throw new ArgumentException("the team does not reach floor " + floor);
            w.P("ok", true);
            TowerFight(w, f, idx, log, final);
        }

        private List<int> Indices(JsonElement indices)
        {
            var idx = new List<int>();
            foreach (var e in indices.EnumerateArray()) idx.Add(e.GetInt32());
            foreach (var i in idx)
                if (i < 0 || i >= _acct.Vault.Count) throw new ArgumentException("no Vault entry " + i);
            return idx;
        }

        /// <summary>Replays a tower attempt with observers, logging only one floor's fight (a null log when it is not reached).</summary>
        private (TowerResult res, FightLog log, FightState final) ReplayTower(List<int> idx, int day, int floor)
        {
            FightLog log = null, cur = null;
            FightState final = null;
            var rules = FightRules.FromContent(_db, tower: true);
            var team = idx.Select(i => _acct.Vault[i].Fighter(_db)).ToList();
            var res = TowerSim.Run(_db, team, day, x => cur?.Record(x), (plan, s) =>
            {
                cur = null;
                if (plan.Floor != floor) return;
                cur = log = new FightLog(rules);
                cur.Watch(s);
                final = s;
            });
            return (res, log, final);
        }

        /// <summary>
        /// A tower floor's fight in the rank fight's shape (FightUnitOut, Action) plus the floor, boss, won and fight hash, each
        /// unit's name, add flag, and entropy and next vent at the end, the entropy cap, and the corruption level at the end and
        /// at the time limit (corruptMax). Its actions carry the tower events and the entropy and corruption snapshots (FightLog).
        /// </summary>
        private void TowerFight(JsonOut w, TowerFloor f, List<int> idx, FightLog log, FightState final)
        {
            var rules = FightRules.FromContent(_db, tower: true);
            w.P("floor", f.Floor).P("boss", f.BossId).P("name", TowerSim.BossName(_db, f.BossId)).P("marquee", f.Marquee).P("won", f.Won)
                .P("ticks", f.Ticks).P("hash", f.FightHash).P("entropyCap", rules.Tower.EntropyCap)
                .P("corruptMax", BattleSim.CorruptionLevel(rules, rules.TimeLimitTicks)).P("corruption", BattleSim.CorruptionLevel(rules, final.Ticks));
            w.Key("units").Arr();
            foreach (var u in final.Units)
                FightUnitOut(w, u, o => o.P("name", TowerUnitName(u.Id, idx)).P("add", u.IsAdd).P("entropy", u.Entropy).P("vent", BattleSim.NextVent(u)));
            w.EndArr();
            w.P("actionCount", log.Count);
            w.Key("actions").Arr();
            foreach (var x in log.Entries) Action(w, x);
            w.EndArr();
        }

        /// <summary>
        /// A tower fighter's display name: "t0_monster" is the first team slot's species, "boss_x" the boss row x, and "boss_x#2"
        /// its second add, named after the add row of x's split phase plus "#2" (the first split phase's row, if x has several).
        /// </summary>
        private string TowerUnitName(string id, List<int> idx)
        {
            if (id.StartsWith("t", StringComparison.Ordinal) && id.Length > 1 && char.IsDigit(id[1]))
            {
                int slot = id[1] - '0';
                if (slot < idx.Count) return _db.Text(_db.Species[_acct.Vault[idx[slot]].SpeciesId].NameKey);
            }
            if (id.StartsWith("boss_", StringComparison.Ordinal))
            {
                string row = id.Substring(5);
                int hash = row.IndexOf('#');
                if (hash < 0) return TowerSim.BossName(_db, row);
                string boss = row.Substring(0, hash);
                string add = _db.TowerBosses.TryGetValue(boss, out var d)
                    ? d.Phases.FirstOrDefault(p => p.Kind == PhaseKinds.Split && !string.IsNullOrEmpty(p.Adds))?.Adds : null;
                return TowerSim.BossName(_db, add ?? boss) + " " + row.Substring(hash);
            }
            return id;
        }

        private static List<string> BookList(JsonElement arr)
        {
            var list = new List<string>();
            foreach (var e in arr.EnumerateArray()) list.Add(e.GetString());
            return list;
        }

        /// <summary>
        /// The account without the active run: shards, owned species, achievements with progress, books, talent ranks, the post-run
        /// vendor's offers with their current price, the Vault, the bonuses runs start with.
        /// </summary>
        public string AccountView(string notice = null)
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
            w.Key("achievements").Arr();
            foreach (var d in _db.AchievementList)
            {
                var (cur, target) = AccountOps.AchievementProgress(_db, ac, d);
                w.Obj().P("id", d.Id).P("earned", ac.Achievements.Contains(d.Id)).P("current", cur).P("target", target).EndObj();
            }
            w.EndArr();
            w.Key("talents").Arr();
            foreach (var t in _db.TalentList) w.Obj().P("id", t.Id).P("rank", ac.TalentRank(t.Id)).EndObj();
            w.EndArr();
            w.Key("vendor");
            if (ac.Vendor == null) w.Null();
            else
            {
                w.Obj().P("grade", ac.Vendor.Grade);
                w.Key("offers").Arr();
                foreach (var o in ac.Vendor.Offers)
                {
                    // The talent's own tier; an offer rolled before #102 (or whose talent left content) keeps its stored rarity.
                    string rarity = o.Talent != null && _db.Talents.TryGetValue(o.Talent, out var td) ? td.Rarity : o.Rarity;
                    w.Obj().P("talent", o.Talent).P("rarity", rarity).P("bought", o.Bought).P("rank", ac.TalentRank(o.Talent))
                        .P("price", AccountOps.OfferPrice(_db, ac, o)).P("maxed", !AccountOps.OfferOpen(_db, ac, o)).EndObj();
                }
                w.EndArr().EndObj();
            }
            w.Key("mods").Obj().P("drillCostMult", m.DrillCostMult).P("statFloorMult", m.StatFloorMult).P("draftSizeBonus", m.DraftSizeBonus).P("startSp", m.StartSp).EndObj();
            w.P("vaultCap", _db.Constants.VaultCap).P("vaultOver", AccountOps.VaultOver(_db, ac));
            w.Key("vault").Arr();
            foreach (var e in ac.Vault) Entry(w, e);
            w.EndArr();
            if (notice != null) w.P("notice", notice);
            return w.EndObj().ToString();
        }

        private void Entry(JsonOut w, VaultEntry e)
        {
            w.Obj().P("species", e.SpeciesId).P("seed", e.Seed.ToString("x16", CultureInfo.InvariantCulture))
                .P("grade", e.Grade).P("rank", e.RankIndex).P("end", e.End.ToString().ToLowerInvariant()).P("cycles", e.Cycles)
                .P("fightsFought", e.FightsFought).P("fightsWon", e.FightsWon).P("hash", e.RunHash).P("sealedUtcMs", e.SealedUtcMs)
                .P("locked", e.Locked).P("refund", AccountOps.DismantleRefund(_db, e));
            w.Key("stats"); Stats(w, e.Stats);
            w.Key("trees").Arr();
            foreach (var t in e.Trees) w.Obj().P("id", t.TreeId).P("bought", t.Bought).EndObj();
            w.EndArr();
            w.Key("gauntlet");
            if (e.LegacyTitle == null) w.Null();   // sealed before the gauntlet existed
            else
            {
                string title = GauntletSim.TitleText(_db, e.LegacyTitle, e.GauntletKiller);
                // "Floor 42: Professional Punching Bag" (specs/12 section 6); a cleared tower has no losing floor to name.
                string label = (e.GauntletCleared ? "All " + (e.GauntletFloor - 1) + " floors" : "Floor " + e.GauntletFloor) + ": " + title;
                w.Obj().P("floor", e.GauntletFloor).P("cleared", e.GauntletCleared).P("killer", e.GauntletKiller)
                    .P("killerName", e.GauntletKiller == null ? null : GauntletSim.EnemyName(_db, e.GauntletKiller))
                    .P("title", title).P("label", label).P("hash", e.GauntletHash).EndObj();
            }
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
                        w.Key("injury");
                        if (r.Injury == null) w.Null(); else Injury(w, r.Injury);
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
                        var log = new FightLog();
                        var r = TrainingRun.Fight(_db, s, log.Record, log.Watch);
                        w.P("ok", true).P("kind", "fight").P("won", r.Won).P("rankBefore", r.RankBefore).P("rankAfter", r.RankAfter).P("sp", r.SpGained);
                        w.P("outcome", r.Fight.Outcome.ToString()).P("ticks", r.Fight.Ticks);
                        w.Key("units").Arr();
                        foreach (var u in r.Fight.Final.Units) FightUnitOut(w, u);
                        w.EndArr();
                        w.P("actionCount", log.Count);
                        w.Key("actions").Arr();
                        foreach (var x in log.Entries) Action(w, x);
                        w.EndArr();
                        break;
                    }
                    case "take":
                    {
                        var r = TrainingRun.TakeTree(_db, s, a.Int(0), a.Int(1));
                        w.P("ok", true).P("kind", "take").P("placed", r.Placed).P("refunded", r.Refunded);
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

        /// <summary>An injury (specs/14 section 4.1): the cycle it happened in, whole cycles sat out, and the cycle the monster is back (0 = the run ran out).</summary>
        private static void Injury(JsonOut w, InjuryHit i) =>
            w.Obj().P("cycle", i.Cycle).P("lost", i.CyclesLost).P("back", i.BackCycle).EndObj();

        private void Milestones(JsonOut w, List<MilestoneHit> hits)
        {
            w.Key("milestones").Arr();
            foreach (var m in hits) w.Obj().P("stat", m.Stat).P("threshold", m.Threshold).EndObj();
            w.EndArr();
        }

        /// <summary>
        /// The fight's action log for playback (specs/16 section 2): each action with a snapshot of the live fight taken as the
        /// sim reports it (every unit's HP, the actor's cooldowns; in a tower fight also every unit's entropy and next vent, and
        /// the corruption level), so the UI shows exact state without computing any rule. Reads only; caps the stored entries at
        /// MaxLoggedActions and counts the rest.
        /// </summary>
        private sealed class FightLog
        {
            public sealed class Entry
            {
                public FightAction X;
                public double[] Hp;
                public int[] Cd;
                public int Si;
                public double[] Entropy;   // tower fights only, else null
                public string[] NextVent;
                public int Corruption;
            }

            private readonly FightRules _tower;   // the fight's rules when it has tower rules, else null
            private FightState _live;
            public readonly List<Entry> Entries = new List<Entry>();
            public int Count;

            public FightLog(FightRules rules = null) { _tower = rules?.Tower != null ? rules : null; }

            public void Watch(FightState s) => _live = s;

            public void Record(FightAction x)
            {
                Count++;
                if (Entries.Count >= MaxLoggedActions || _live == null) return;
                int n = _live.Units.Count;
                var e = new Entry { X = x, Hp = new double[n], Si = -1 };
                FightUnit actor = null;
                for (int i = 0; i < n; i++)
                {
                    var u = _live.Units[i];
                    e.Hp[i] = Math.Max(0, u.Hp);
                    if (u.Id == x.Actor) actor = u;
                }
                // the actor's skill (by id, so an echo counts) this action used; -1 for basic attacks, counters and triggers not in its list
                if (actor != null && x.Skill != null)
                    for (int i = 0; i < actor.Source.Skills.Count && e.Si < 0; i++)
                        if (actor.Source.Skills[i].Id == x.Skill.Id && actor.Source.Skills[i].Kind != SkillKind.Passive) e.Si = i;
                e.Cd = actor == null ? new int[0] : (int[])actor.Cooldowns.Clone();
                if (_tower != null)
                {
                    e.Entropy = new double[n];
                    e.NextVent = new string[n];
                    for (int i = 0; i < n; i++)
                    {
                        e.Entropy[i] = _live.Units[i].Entropy;
                        e.NextVent[i] = BattleSim.NextVent(_live.Units[i]);
                    }
                    e.Corruption = BattleSim.CorruptionLevel(_tower, _live.Ticks);
                }
                Entries.Add(e);
            }
        }

        private void FightUnitOut(JsonOut w, FightUnit u, Action<JsonOut> extra = null)
        {
            w.Obj().P("id", u.Id).P("side", u.Side).P("hp", Math.Max(0, u.Hp)).P("maxHp", u.MaxHp)
                .P("damage", u.Tally.DamageDealt).P("crits", u.Tally.Crits).P("evades", u.Tally.Evades);
            w.Key("skills").Arr();   // in the fighter's skill order, the index the actions' "cd" and "si" use
            foreach (var sk in u.Source.Skills)
                w.Obj().P("name", _db.Text("mskill." + sk.Id + ".name")).P("kind", sk.Kind.ToString().ToLowerInvariant()).P("cooldown", sk.Cooldown).EndObj();
            w.EndArr();
            w.Key("cd").Arr();       // cooldowns left when the fight ended
            foreach (var c in u.Cooldowns) w.Int(c);
            w.EndArr();
            extra?.Invoke(w);
            w.EndObj();
        }

        private void Action(JsonOut w, FightLog.Entry e)
        {
            var x = e.X;
            w.Obj().P("actor", x.Actor).P("target", x.Target).P("amount", x.Amount).P("crit", x.Crit).P("evaded", x.Evaded).P("aoe", x.Aoe).P("counter", x.Counter);
            if (x.Skill != null) w.P("skill", _db.Text("mskill." + x.Skill.Id + ".name")).P("skillId", x.Skill.Id).P("kind", x.Skill.Kind.ToString().ToLowerInvariant());
            if (x.Event != null)
            {
                w.P("event", x.Event);   // a Dungeon event (specs/21 section 6) or a rule modifier's (specs/16 section 6.2)
                if (x.Event.StartsWith(FightAction.RulePrefix, StringComparison.Ordinal))
                    w.P("skill", _db.Text("rule." + x.Event.Substring(FightAction.RulePrefix.Length) + ".name"));
            }
            w.P("si", e.Si);
            w.Key("hp").Arr();
            foreach (var v in e.Hp) w.Num(v);
            w.EndArr();
            w.Key("cd").Arr();
            foreach (var c in e.Cd) w.Int(c);
            w.EndArr();
            if (e.Entropy != null)   // tower fights only: every unit's entropy and next vent, and the corruption level
            {
                w.Key("en").Arr();
                foreach (var v in e.Entropy) w.Num(v);
                w.EndArr();
                w.Key("vent").Arr();
                foreach (var v in e.NextVent) w.Str(v);
                w.EndArr();
                w.P("corruption", e.Corruption);
            }
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
            var opp = _db.FightRanks[s.Rank];
            TipJson.StatNow(w, _db, s.Stats, opp.Stats, _db.Text(opp.NameKey));
            w.Key("growth").Obj();
            foreach (var id in StatIds.Trained) w.P(id, TrainingRun.Growth(_db, s, id));
            w.EndObj();
            w.Key("opponent").Obj().P("name", _db.Text(opp.NameKey)).P("element", opp.Element);
            w.Key("stats"); Stats(w, opp.Stats);
            TipJson.StatNow(w, _db, opp.Stats, s.Stats, _db.Text(sp.NameKey));
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
            w.Key("chase").Arr();   // the species' own chase trees (in-run, then book-gated) and which conditions hold
            foreach (var t in new[] { sp.ChaseTreeRun, sp.ChaseTreeBook }.Select(id => _db.Trees[id]))
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
            w.Key("injuries").Arr();
            foreach (var i in s.Injuries) Injury(w, i);
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
