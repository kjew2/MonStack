using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using IdleRng.Runtime.Save;
using IdleRng.Server;
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

        /// <summary>
        /// The player id of the page's local Endless Tower board (specs/28 section 6.1): the server core's EndlessBoard over memory,
        /// built on each read from this account only, until the page commits to the server (specs/27 step 3) and reads its board.
        /// </summary>
        private const string LocalPlayer = "local";
        private RunState _run => _acct?.Active;

        public RunHost(string contentJson)
        {
            if (!PackSource.LooksLikePack(contentJson)) throw new InvalidOperationException("content pack missing: pass the bundled Content/*.json pack (web/wasm/pack-content.js)");
            _db = ContentDb.Load(new PackSource(contentJson), verifyHashes: true);
        }

        public string ContentVersion => _db.Version;

        /// <summary>The loaded content, for Engine.GoldenVectors (the golden generators only read it).</summary>
        public ContentDb Db => _db;

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
                .P("shardsPerCycle", k.ShardsPerCycle).P("shardsPerRank", k.ShardsPerRank).P("vaultCap", k.VaultCap).P("vendorOffers", k.VendorOffers).P("legendarySlots", k.LegendarySlots)
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
                w.Key("pool").Arr();   // the rank's pool monsters (rankEnemies ids), in content order
                foreach (var e in r.Pool) w.Str(e.Id);
                w.EndArr();
                w.EndObj();
            }
            w.EndArr();
            w.Key("rankEnemies").Obj();
            foreach (var e in _db.FightRanks.SelectMany(r => r.Pool)) { w.Key(e.Id); RankEnemy(w, e); }
            w.EndObj();
            w.Key("trees").Obj();
            foreach (var t in _db.Trees.Values.OrderBy(t => t.Id, StringComparer.Ordinal)) Tree(w, t);
            w.EndObj();
            w.Key("books").Obj();
            foreach (var b in _db.SkillBooks.Values) w.Key(b.Id).Obj().P("name", _db.Text(b.NameKey)).P("tree", b.TreeId).EndObj();
            w.EndObj();
            w.Key("talents").Arr();
            foreach (var t in _db.TalentList)
                w.Obj().P("id", t.Id).P("name", _db.Text(t.NameKey)).P("desc", _db.Text(t.DescKey)).P("effect", t.Effect.ToString().ToLowerInvariant())
                    .P("rarity", t.Rarity).P("maxRank", t.MaxRank).P("legendary", AccountOps.TakesLegendarySlot(_db, t)).EndObj();
            w.EndArr();
            w.Key("achievements").Arr();
            foreach (var d in _db.AchievementList)
                w.Obj().P("id", d.Id).P("name", _db.Text(d.NameKey)).P("text", AchievementText(d)).P("species", d.UnlocksSpecies).EndObj();
            w.EndArr();
            w.Key("rarities").Arr();
            foreach (var r in _db.VendorRarities) w.Obj().P("id", r.Id).P("name", _db.Text(r.NameKey)).P("legendary", r.LegendarySlot).EndObj();
            w.EndArr();
            w.Key("gauntlet").Obj().P("floors", k.GauntletFloors).P("bossEvery", k.GauntletBossEvery);
            w.Key("enemies").Obj();
            foreach (var g in _db.GauntletEnemies.Concat(_db.GauntletBosses))
                w.Key(g.Id).Obj().P("name", _db.Text(g.NameKey)).P("element", g.Element).P("boss", g.Boss).EndObj();
            w.EndObj().EndObj();
            w.Key("gambits").Obj().P("slots", k.GambitSlots);   // the rulebook editor's vocabulary (specs/28 sections 4.1, 4.2 and 4.5)
            w.Key("conditions").Arr();
            foreach (var id in GambitConditions.Known)
                w.Obj().P("id", id).P("name", _db.Text("gambit.when." + id + ".name")).P("desc", _db.Text("gambit.when." + id + ".desc"))
                    .P("value", GambitConditions.TakesFraction(id) ? "fraction" : GambitConditions.TakesCount(id) ? "count" : null).EndObj();
            w.EndArr();
            w.Key("targets").Arr();
            foreach (var id in GambitTargets.Known)
                w.Obj().P("id", id).P("name", _db.Text("gambit.target." + id + ".name")).P("desc", _db.Text("gambit.target." + id + ".desc")).EndObj();
            w.EndArr();
            w.Key("basic").Obj().P("id", GambitRule.Basic).P("name", _db.Text("gambit.basic.name")).P("desc", _db.Text("gambit.basic.desc")).EndObj();
            w.EndObj();
            TipJson.StatInfo(w, _db);
            TipJson.NodeTips(w, _db, NodeText);
            TipJson.SkillTips(w, _db);
            TipJson.TowerTips(w, _db);
            return w.EndObj().ToString();
        }

        /// <summary>A rank pool monster (specs/14 section 5): {id, name, rank, letter, element, boss, stats, skills[{id, name, kind}]}. boss = the top rank's pool.</summary>
        private void RankEnemy(JsonOut w, RankEnemyDef e)
        {
            var r = _db.FightRanks[e.RankIndex];
            w.Obj().P("id", e.Id).P("name", _db.Text(e.NameKey)).P("rank", r.Id).P("letter", r.Id.ToUpperInvariant()).P("element", e.Element)
                .P("boss", e.RankIndex == _db.FightRanks.Count - 1);
            w.Key("stats"); Stats(w, e.Stats);
            w.Key("skills").Arr();
            foreach (var id in e.Skills)
                w.Obj().P("id", id).P("name", _db.Text("mskill." + id + ".name")).P("kind", _db.MonsterSkills[id].Kind.ToString().ToLowerInvariant()).EndObj();
            w.EndArr();
            w.EndObj();
        }

        /// <summary>
        /// The run's opponent at rank <paramref name="i"/> for the UI: {rank, letter, id (the rankEnemies id; null in a legacy run,
        /// which fights the flat rank), name, element, boss, skills[skill ids]}.
        /// </summary>
        private void LadderStep(JsonOut w, RunState s, int i)
        {
            var r = _db.FightRanks[i];
            var e = TrainingRun.LadderEnemy(_db, s, i);
            w.Obj().P("rank", r.Id).P("letter", r.Id.ToUpperInvariant()).P("id", e?.Id)
                .P("name", _db.Text(e != null ? e.NameKey : r.NameKey)).P("element", e != null ? e.Element : r.Element)
                .P("boss", i == _db.FightRanks.Count - 1);
            w.Key("skills").Arr();
            if (e != null) foreach (var id in e.Skills) w.Str(id);
            w.EndArr();
        }

        private void Tree(JsonOut w, TreeDef t)
        {
            w.Key(t.Id).Obj().P("id", t.Id).P("name", _db.Text(t.NameKey)).P("kind", t.Kind.ToString().ToLowerInvariant());
            NodesAndBoosts(w, t.Nodes, null);
            w.P("source", TreeSource(t));
            if (t.Kind == TreeKind.Chase) w.P("species", t.Species).P("bookGated", t.BookGated).P("apexName", _db.Text(t.ApexNameKey));
            if (t.Kind == TreeKind.Fusion) w.P("fuseA", t.FuseA).P("fuseB", t.FuseB).P("apexName", _db.Text(t.ApexNameKey));
            if (t.FusesInto != null)   // the fusing pair, so the UI can show it (kj: each fusing pair must be clear)
                w.P("fusesInto", t.FusesInto).P("fusesWith", _db.Trees[t.FusesInto].FuseA == t.Id ? _db.Trees[t.FusesInto].FuseB : _db.Trees[t.FusesInto].FuseA);
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

        /// <summary>
        /// A tree's "nodes" and "boosts" (its filler nodes summed per stat, in first-seen order, for the offer card's chips). With
        /// a rolled tree's <paramref name="roll"/> (specs/15 section 4.1), every node id gets a suffix that names its rolled content
        /// and carries its own tooltip ("tip"), since the content tooltip (nodeTips) shows the unrolled node; the capstone's text
        /// adds its growth bonus.
        /// </summary>
        private void NodesAndBoosts(JsonOut w, IReadOnlyList<TreeNodeDef> nodes, TreeRoll roll)
        {
            w.Key("nodes").Arr();
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                bool last = i == nodes.Count - 1;
                var (name, desc) = NodeText(n);
                string id = n.Id;
                if (roll != null && last)
                {
                    desc += " Also +" + Num(roll.CapGrowth) + " " + roll.Stat.ToUpperInvariant() + " growth.";
                    id += "~cap~" + roll.Stat + "~" + Num(roll.CapGrowth);
                }
                else if (roll != null && roll.NodeStats[i] != null) id += "~" + n.Stat + "~" + Num(n.Amount);
                w.Obj().P("id", id).P("cost", n.Cost).P("kind", n.Kind.ToString().ToLowerInvariant()).P("name", name).P("desc", desc).P("last", last);
                if (n.Kind == NodeKind.Growth || n.Kind == NodeKind.Stat) w.P("stat", n.Stat).P("amount", n.Amount);
                if (roll != null)
                {
                    var tip = BattleText.NodeTip(_db, n, name, desc, last);
                    if (last) tip.Fact("Rolled growth", "+" + Num(roll.CapGrowth) + " " + roll.Stat.ToUpperInvariant());
                    w.Key("tip"); TipJson.Write(w, tip);
                }
                w.EndObj();
            }
            w.EndArr();
            var boosts = new List<(string stat, double growth, double flat)>();
            foreach (var n in nodes)
            {
                if (n.Kind != NodeKind.Growth && n.Kind != NodeKind.Stat) continue;
                int k = boosts.FindIndex(b => b.stat == n.Stat);
                if (k < 0) { boosts.Add((n.Stat, 0, 0)); k = boosts.Count - 1; }
                var b0 = boosts[k];
                boosts[k] = n.Kind == NodeKind.Growth ? (b0.stat, b0.growth + n.Amount, b0.flat) : (b0.stat, b0.growth, b0.flat + n.Amount);
            }
            if (roll != null)
            {
                // The capstone's growth counts toward its stat's chip.
                int k = boosts.FindIndex(b => b.stat == roll.Stat);
                if (k < 0) boosts.Add((roll.Stat, roll.CapGrowth, 0)); else boosts[k] = (roll.Stat, boosts[k].growth + roll.CapGrowth, boosts[k].flat);
            }
            w.Key("boosts").Arr();
            foreach (var b in boosts) w.Obj().P("stat", b.stat).P("growth", Math.Round(b.growth, 4)).P("flat", Math.Round(b.flat, 4)).EndObj();
            w.EndArr();
        }

        /// <summary>A rolled tree as the client draws it (null when not rolled): its own stat, capstone growth, nodes and boosts.</summary>
        private void Rolled(JsonOut w, string treeId, TreeRoll roll)
        {
            if (roll == null) { w.Null(); return; }
            w.Obj().P("stat", roll.Stat).P("capGrowth", roll.CapGrowth);
            NodesAndBoosts(w, TrainingRun.Nodes(_db, treeId, roll), roll);
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
                    return "Rolled in an Awakening when any stat first reaches " + t.OfferThreshold + " or more";
                case TreeKind.Book:
                {
                    var b = _db.SkillBooks.Values.FirstOrDefault(x => x.TreeId == t.Id);
                    return b == null ? "From a skill book" : "From the skill book " + _db.Text(b.NameKey) + " (bring it into a run)";
                }
                case TreeKind.Fusion:
                    return "Fusion of " + _db.Text(_db.Trees[t.FuseA].NameKey) + " and " + _db.Text(_db.Trees[t.FuseB].NameKey)
                        + ": offered when both are slotted and maxed; it takes one of their slots, frees the other and keeps both";
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
                case AchievementKind.EndlessFloor: return "Clear " + Num(d.Amount) + " Endless Tower floors in one climb";
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

        /// <summary>{hash}: the account hash (AccountParts.AccountHash of its parts), as the server computes it after re-applying a commit.</summary>
        public string AccountHash() => _acct == null ? JsonOut.Error("no account")
            : new JsonOut().Obj().P("hash", AccountParts.AccountHash(AccountParts.Split(_acct))).EndObj().ToString();

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
                    // Reads: they change nothing, so they are not commands (specs/27 section 4).
                    case "towerInfo": TowerInfo(w, a.Int(0)); return w.EndObj().ToString();
                    case "towerFloor": TowerFloorFight(w, a.Int(0), a.El(1), a.Int(2)); return w.EndObj().ToString();
                    case "endlessInfo": EndlessInfo(w, a.Int(0)); return w.EndObj().ToString();
                    case "endlessFloor": EndlessFloorRead(w, a.Int(0), a.El(1), a.Int(2), null); return w.EndObj().ToString();
                    case "endlessPreview": EndlessFloorRead(w, a.Int(0), a.El(1), a.Int(2), a.El(3)); return w.EndObj().ToString();
                    case "endlessBoard": EndlessBoardRead(w, a.Int(0), a.Has(1) ? a.Int(1) : EndlessBoard.DefaultTop); return w.EndObj().ToString();
                }
                if (!AccountCommand.IsCommand(name)) return Fail("unknown command");
                var cmd = AccountCommand.Parse(name, a);
                if (AccountCommands.IsRunCommand(cmd.Kind)) return Fail("unknown command");
                var res = AccountCommands.Apply(_db, _acct, cmd);
                switch (cmd.Kind)
                {
                    case CommandKind.FinishRun:
                    {
                        var r = (FinishResult)res;
                        w.P("ok", true).P("shards", r.Shards).P("ticketPieces", r.TicketPieces).P("ticketsEarned", r.TicketsEarned);
                        w.Key("entry"); Entry(w, r.Entry);
                        w.Key("climb"); Climb(w, r.Gauntlet);
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
                    case CommandKind.RunTower: RunTower(w, cmd, (TowerRunResult)res); break;
                    case CommandKind.RunEndless: RunEndless(w, cmd, (EndlessRunResult)res); break;
                    case CommandKind.BuyOffer:
                    {
                        var r = (OfferBought)res;
                        w.P("ok", true).P("cost", r.Cost).P("talent", r.Talent);
                        break;
                    }
                    case CommandKind.RerollVendor: w.P("ok", true).P("cost", (long)res); break;
                    case CommandKind.Dismantle: w.P("ok", true).P("refund", (long)res); break;
                    case CommandKind.SetName: w.P("ok", true).P("name", _acct.Name); break;
                    default: w.P("ok", true); break;   // startRun, abandon, setLoadout, closeVendor, lock, debugGrantBook, setRulebook
                }
            }
            catch (Exception e) when (e is InvalidOperationException || e is ArgumentException)
            {
                return Fail(e.Message);
            }
            return w.EndObj().ToString();
        }

        /// <summary>
        /// The gauntlet the monster just climbed, floor by floor, returned with finishRun for the wheel shown once after sealing
        /// (specs/19 section 7). Nothing replays it later: the entry keeps only the result.
        /// </summary>
        private static void Climb(JsonOut w, GauntletResult g)
        {
            w.Obj().P("floorReached", g.FloorReached).P("cleared", g.Cleared).P("killer", g.KillerId).P("hash", g.Hash);
            w.Key("floors").Arr();
            foreach (var f in g.Floors) w.Obj().P("floor", f.Floor).P("enemy", f.EnemyId).P("boss", f.Boss).P("won", f.Won).P("ticks", f.Ticks).EndObj();
            w.EndArr().EndObj();
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
        /// The Endless Tower in a season (specs/28 sections 3 and 5), read only: the season records (zero for a season with no
        /// climb yet), the team size and rulebook size, the mutators the season has drawn up to the shown floors, and the next
        /// ENDLESS_BAND floors after the season's best with their squads, mutator count and the shards each pays.
        /// </summary>
        private void EndlessInfo(JsonOut w, int season)
        {
            if (season < 0) throw new ArgumentException("the season must not be negative");
            var p = _acct.Endless ?? new EndlessProgress();
            bool same = season == p.Season;
            int best = same ? p.ClearedSeason : 0;
            int band = _db.Constants.EndlessBand;
            int shown = Math.Min(_db.Constants.EndlessMaxFloors, best + band);
            w.P("ok", true).P("season", season).P("lastSeason", p.Season).P("climbs", same ? p.Climbs : 0)
                .P("clearedSeason", best).P("clearedEver", p.ClearedEver).P("teamSize", _db.Constants.MonSideSize)
                .P("gambitSlots", _db.Constants.GambitSlots).P("band", band).P("maxFloors", _db.Constants.EndlessMaxFloors);
            var mutators = EndlessSim.SeasonMutators(_db, season, shown / band);
            w.Key("mutators").Arr();   // in the order they join: mutator i is in force from floor (i + 1) x ENDLESS_BAND
            for (int i = 0; i < mutators.Count; i++)
                w.Obj().P("id", mutators[i].Id).P("name", _db.Text(mutators[i].NameKey)).P("kind", mutators[i].Kind)
                    .P("value", mutators[i].Value).P("element", mutators[i].Element).P("desc", MutatorText(mutators[i])).P("fromFloor", (i + 1) * band).EndObj();
            w.EndArr();
            w.Key("floors").Arr();
            foreach (var plan in EndlessSim.Lineup(_db, season, shown))
            {
                if (plan.Floor <= best) continue;   // already cleared (and paid) this season
                var shape = EndlessSim.Shape(_db, plan.ShapeId);
                w.Obj().P("floor", plan.Floor).P("shape", plan.ShapeId).P("shapeName", _db.Text(shape.NameKey)).P("check", shape.Check)
                    .P("elite", plan.Elite).P("level", Math.Round(plan.Level)).P("mutators", plan.Mutators)
                    .P("shards", AccountOps.EndlessFloorShards(_db, plan.Floor));
                w.Key("enemies").Arr();
                for (int slot = 0; slot < plan.EnemyIds.Count; slot++)
                {
                    string id = plan.EnemyIds[slot];
                    bool boss = plan.Elite && slot == 0;
                    string name = boss ? TowerSim.BossName(_db, id) : _db.Text(EndlessSim.Enemy(_db, id).NameKey);
                    string element = boss ? _db.TowerBosses[id].Element : EndlessSim.Enemy(_db, id).Element;
                    string role = boss ? "elite" : EndlessSim.Enemy(_db, id).Role;
                    w.Obj().P("id", id).P("name", name).P("element", element).P("role", role).EndObj();
                }
                w.EndArr().EndObj();
            }
            w.EndArr();
        }

        /// <summary>
        /// endlessBoard(season, top?), read only (specs/28 section 6.1): the season board as the server's EndlessBoard.ReadAsync
        /// gives it, from a local board holding this account only: its season best and name are put on it first, as the server
        /// does when it accepts a commit (EndlessBoard.RecordAsync; the time is 0, as the board holds one player). `local` tells
        /// the page the rows are this device's only.
        /// </summary>
        private void EndlessBoardRead(JsonOut w, int season, int top)
        {
            if (season < 0) throw new ArgumentException("the season must not be negative");
            var board = new EndlessBoard(new MemoryLeaderboardStore());   // fresh each read: another account loaded since never shows
            board.SetNameAsync(LocalPlayer, _acct.Name).GetAwaiter().GetResult();
            board.RecordAsync(LocalPlayer, _acct.Endless, 0).GetAwaiter().GetResult();
            var v = board.ReadAsync(LocalPlayer, season, top).GetAwaiter().GetResult();
            w.P("ok", true).P("season", v.Season).P("total", v.Total).P("local", true).P("name", _acct.Name)
                .P("fallbackName", EndlessBoard.FallbackName(LocalPlayer)).P("nameMin", DisplayName.Min).P("nameMax", DisplayName.Max);
            w.Key("top").Arr();
            foreach (var r in v.Top) BoardRowJson(w, r);
            w.EndArr();
            w.Key("you");
            if (v.You == null) w.Null();
            else BoardRowJson(w, v.You);
        }

        private static void BoardRowJson(JsonOut w, BoardRow r) =>
            w.Obj().P("rank", r.Rank).P("name", r.Name).P("floor", r.Floor).P("you", r.You).EndObj();

        /// <summary>
        /// One Endless Tower climb (AccountOps.RunEndless): the rewards, the records after it, and each floor fought with its fight
        /// hash. Floor playback comes with the climb screen (specs/28 section 9, step 5).
        /// </summary>
        private void RunEndless(JsonOut w, AccountCommand cmd, EndlessRunResult r)
        {
            var res = r.Result;
            var p = _acct.Endless;
            w.P("ok", true).P("season", res.Season).P("shards", r.Shards).P("floorReached", res.FloorReached).P("capped", res.Capped)
                .P("floorsCleared", res.FloorsCleared).P("clearedSeason", p.ClearedSeason).P("clearedEver", p.ClearedEver)
                .P("climbs", p.Climbs).P("hash", res.Hash);
            w.Key("newFloors").Arr();
            foreach (var f in r.NewFloors) w.Int(f);
            w.EndArr();
            w.Key("firstClears").Arr();
            foreach (var f in r.FirstClears) w.Int(f);
            w.EndArr();
            w.Key("achievements").Arr();
            foreach (var id in r.Achievements)
            {
                var d = _db.Achievements[id];
                bool unlocked = d.UnlocksSpecies != null && r.SpeciesUnlocked.Contains(d.UnlocksSpecies);
                w.Obj().P("id", id).P("name", _db.Text(d.NameKey)).P("text", AchievementText(d))
                    .P("species", unlocked ? d.UnlocksSpecies : null)
                    .P("speciesName", unlocked ? _db.Text(_db.Species[d.UnlocksSpecies].NameKey) : null).EndObj();
            }
            w.EndArr();
            w.Key("floors").Arr();
            foreach (var f in res.Floors)
            {
                w.Obj().P("floor", f.Floor).P("shape", f.ShapeId).P("shapeName", _db.Text(EndlessSim.Shape(_db, f.ShapeId).NameKey)).P("elite", f.Elite).P("won", f.Won).P("ticks", f.Ticks)
                    .P("vents", f.Vents).P("corruption", f.PeakCorruption).P("hash", f.FightHash);
                w.Key("phases").Arr();
                foreach (var ph in f.Phases) w.Str(_db.Text("phase." + ph + ".name"));
                w.EndArr().EndObj();
            }
            w.EndArr();
            // The deciding floor's fight for playback (the last floor fought); any other comes from endlessFloor.
            var last = res.Floors[res.Floors.Count - 1];
            var team = cmd.Team.Select(i => AccountOps.TeamFighter(_db, _acct, i)).ToList();
            w.Key("final").Obj();
            if (EndlessFight(w, res.Season, cmd.Team, team, last.Floor) != last.FightHash) throw new InvalidOperationException("the floor replay differs from the climb");
            w.EndObj();
        }

        /// <summary>
        /// endlessFloor(season, team, floor) and endlessPreview(season, team, floor, rulebooks), read only (specs/28 sections 4.5
        /// and 9): one floor's fight in the endless fight shape. A floor is fought on its own (EndlessSim.FightFloor, the same fight
        /// the climb fought there), so any floor can be watched or tested, reached or not. The preview fights it with the given
        /// rulebook text per team slot (null or absent = the monster's saved rulebook), checked as setRulebook checks it; it pays,
        /// counts and saves nothing.
        /// </summary>
        private void EndlessFloorRead(JsonOut w, int season, JsonElement indices, int floor, JsonElement? books)
        {
            if (season < 0) throw new ArgumentException("the season must not be negative");
            var idx = Indices(indices);
            if (idx.Count != _db.Constants.MonSideSize) throw new ArgumentException("an Endless Tower team needs exactly " + _db.Constants.MonSideSize + " monsters");
            if (idx.Distinct().Count() != idx.Count) throw new ArgumentException("a monster is on the team twice");
            if (floor < 1 || floor > _db.Constants.EndlessMaxFloors) throw new ArgumentException("no floor " + floor);
            var team = new List<Fighter>(idx.Count);
            var texts = books.HasValue && books.Value.ValueKind == JsonValueKind.Array ? books.Value.EnumerateArray().ToList() : new List<JsonElement>();
            for (int k = 0; k < idx.Count; k++)
            {
                if (k < texts.Count && texts[k].ValueKind == JsonValueKind.String)
                {
                    var f = _acct.Vault[idx[k]].Fighter(_db);
                    f.Gambits.AddRange(GambitText.Parse(AccountOps.CheckRulebook(_db, _acct.Vault[idx[k]], texts[k].GetString())));
                    team.Add(f);
                }
                else team.Add(AccountOps.TeamFighter(_db, _acct, idx[k]));
            }
            w.P("ok", true);
            EndlessFight(w, season, idx, team, floor);
        }

        /// <summary>
        /// One Endless floor's fight (EndlessSim.FightFloor) in the tower fight's shape (TowerFight): the floor, its shape and check,
        /// elite, the mutators in force (name, desc), won, ticks, hash, the entropy cap and corruption levels, the units with their
        /// names (and for the team, its slot's Vault index as <c>vault</c>), and the actions with their snapshots; a rule firing is
        /// an action with event "gambit" and amount the rule's index from 0. Returns the fight hash.
        /// </summary>
        private string EndlessFight(JsonOut w, int season, List<int> idx, List<Fighter> team, int floor)
        {
            // The floor's rules as FightFloor builds them (the season's first floor / ENDLESS_BAND mutators), for the log's tower snapshots.
            var rules = EndlessSim.Rules(_db, EndlessSim.SeasonMutators(_db, season, floor / _db.Constants.EndlessBand));
            var log = new FightLog(rules);
            FightState final = null;
            var (plan, mutators, fight) = EndlessSim.FightFloor(_db, team, season, floor, x => log.Record(x), st => { final = st; log.Watch(st); });
            var shape = EndlessSim.Shape(_db, plan.ShapeId);
            w.P("season", season).P("floor", plan.Floor).P("shape", plan.ShapeId).P("shapeName", _db.Text(shape.NameKey)).P("check", shape.Check)
                .P("elite", plan.Elite).P("eliteName", plan.Elite ? TowerSim.BossName(_db, plan.EliteId) : null)
                .P("name", plan.Elite ? TowerSim.BossName(_db, plan.EliteId) : _db.Text(shape.NameKey))
                .P("won", fight.Outcome == FightOutcome.SideAWon).P("ticks", fight.Ticks).P("hash", fight.Hash).P("entropyCap", rules.Tower.EntropyCap)
                .P("corruptMax", BattleSim.CorruptionLevel(rules, rules.TimeLimitTicks)).P("corruption", BattleSim.CorruptionLevel(rules, final.Ticks));
            w.Key("mutators").Arr();
            foreach (var m in mutators) w.Obj().P("id", m.Id).P("name", _db.Text(m.NameKey)).P("desc", MutatorText(m)).EndObj();
            w.EndArr();
            w.Key("units").Arr();
            foreach (var u in final.Units)
                FightUnitOut(w, u, o =>
                {
                    o.P("name", EndlessUnitName(u.Id, idx)).P("add", u.IsAdd).P("entropy", u.Entropy).P("vent", BattleSim.NextVent(u));
                    int slot = TeamSlot(u.Id);
                    if (slot >= 0 && slot < idx.Count && !u.IsAdd) o.P("vault", idx[slot]);
                });
            w.EndArr();
            w.P("actionCount", log.Count);
            w.Key("actions").Arr();
            foreach (var x in log.Entries) Action(w, x);
            w.EndArr();
            return fight.Hash;
        }

        /// <summary>The team slot of a unit id "t&lt;slot&gt;_...", else -1.</summary>
        private static int TeamSlot(string id) =>
            id != null && id.Length > 2 && id[0] == 't' && char.IsDigit(id[1]) && id[2] == '_' ? id[1] - '0' : -1;

        /// <summary>
        /// An Endless fighter's display name: the team's as in the Dungeon (TowerUnitName); "e&lt;slot&gt;_&lt;id&gt;" an enemy's or the elite's
        /// name, and "e0_&lt;boss&gt;#2" the elite's second add, named after the add row of its split phase.
        /// </summary>
        private string EndlessUnitName(string id, List<int> idx)
        {
            if (TeamSlot(id) >= 0) return TowerUnitName(id, idx);
            if (id.Length > 3 && id[0] == 'e' && char.IsDigit(id[1]) && id[2] == '_')
            {
                string row = id.Substring(3);
                int hash = row.IndexOf('#');
                string bare = hash < 0 ? row : row.Substring(0, hash);
                if (_db.TowerBosses.TryGetValue(bare, out var d))
                {
                    if (hash < 0) return TowerSim.BossName(_db, bare);
                    string add = d.Phases.FirstOrDefault(p => p.Kind == PhaseKinds.Split && !string.IsNullOrEmpty(p.Adds))?.Adds;
                    return TowerSim.BossName(_db, add ?? bare) + " " + row.Substring(hash);
                }
                foreach (var e in _db.EndlessEnemies) if (e.Id == bare) return _db.Text(e.NameKey) + (hash < 0 ? "" : " " + row.Substring(hash));
            }
            return id;
        }

        /// <summary>A mutator's effect in words (specs/28 section 3.2), from its kind and value.</summary>
        private string MutatorText(MutatorDef m)
        {
            string pct(double v) => Math.Round(v * 100).ToString(CultureInfo.InvariantCulture) + "%";
            string times(double v) => "x" + Math.Round(v, 2).ToString(CultureInfo.InvariantCulture);
            switch (m.Kind)
            {
                case "heal_mult": return "Heals and shields " + times(m.Value);
                case "aoe_mult": return "Hits on all enemies " + times(m.Value);
                case "single_mult": return "Single-target hits " + times(m.Value);
                case "element_mult": return (_db.Elements.TryGetValue(m.Element ?? "", out var el) ? _db.Text(el.NameKey) : m.Element) + " hits " + times(m.Value);
                case "speed_mult": return "Every unit's speed " + times(m.Value);
                case "crit_bonus": return "Crit chance " + (m.Value >= 0 ? "+" : "") + pct(m.Value);
                case "corruption_early": return "Corruption starts " + Math.Round(m.Value).ToString(CultureInfo.InvariantCulture) + " ticks earlier";
                case "start_shield": return "Every unit starts with a shield of " + pct(m.Value) + " of its max HP";
                default: return m.Kind + " " + m.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// One Dungeon attempt (AccountOps.RunTower): the rewards, each floor fought (with its fight hash), and the last floor's
        /// fight for playback (TowerFight). The fight log comes from replaying the attempt with observers, which gives the identical
        /// result (the hash is checked). The other floors' fights come from towerFloor, on demand.
        /// </summary>
        private void RunTower(JsonOut w, AccountCommand cmd, TowerRunResult r)
        {
            var idx = cmd.Team;
            int day = cmd.A;
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
            var team = idx.Select(i => AccountOps.TeamFighter(_db, _acct, i)).ToList();
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
        /// A tower fighter's display name: "t0_monster" is the first team slot's monster (its apex or species name, specs/15 section 5.3), "boss_x" the boss row x, and "boss_x#2"
        /// its second add, named after the add row of x's split phase plus "#2" (the first split phase's row, if x has several).
        /// </summary>
        private string TowerUnitName(string id, List<int> idx)
        {
            if (id.StartsWith("t", StringComparison.Ordinal) && id.Length > 1 && char.IsDigit(id[1]))
            {
                int slot = id[1] - '0';
                if (slot < idx.Count) { var e = _acct.Vault[idx[slot]]; return ApexForm.Name(_db, e.SpeciesId, ApexForm.TreeOf(_db, e)); }
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
            w.P("seed", ac.Seed.ToString("x16", CultureInfo.InvariantCulture)).P("name", ac.Name).P("runsStarted", ac.RunsStarted).P("shards", ac.Shards).P("hasRun", ac.Active != null);
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
            w.Key("talentsOff").Arr();   // the loadout the next run starts with unless changed (issue #106)
            foreach (var id in AccountOps.DefaultLoadout(_db, ac)) w.Str(id);
            w.EndArr();
            w.Key("vendor");
            if (ac.Vendor == null) w.Null();
            else
            {
                w.Obj().P("grade", ac.Vendor.Grade).P("rerolls", ac.Vendor.Rerolls)
                    .P("rerollPrice", AccountOps.RerollPrice(_db, ac.Vendor)).P("canReroll", AccountOps.CanRerollVendor(_db, ac));
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
            w.Key("mods"); Mods(w, m);
            w.P("vaultCap", _db.Constants.VaultCap).P("vaultOver", AccountOps.VaultOver(_db, ac));
            w.Key("vault").Arr();
            foreach (var e in ac.Vault) Entry(w, e);
            w.EndArr();
            if (notice != null) w.P("notice", notice);
            return w.EndObj().ToString();
        }

        /// <summary>The run bonuses talents give (specs/18 section 3), one field per RunModifiers property.</summary>
        private static void Mods(JsonOut w, RunModifiers m) =>
            w.Obj().P("drillCostMult", m.DrillCostMult).P("statFloorMult", m.StatFloorMult).P("startSp", m.StartSp)
                .P("restEnergyBonus", m.RestEnergyBonus).P("failChanceDelta", m.FailChanceDelta).P("superChanceBonus", m.SuperChanceBonus)
                .P("shardMult", m.ShardMult).P("momentumDrills", m.MomentumDrills).P("lossSp", m.LossSp).P("specialistBonus", m.SpecialistBonus)
                .P("treeSlotBonus", m.TreeSlotBonus).P("injuryShields", m.InjuryShields).P("lastCycleAp", m.LastCycleAp)
                .P("milestoneMult", m.MilestoneMult).P("draftRerolls", m.DraftRerolls)
                .P("injuryImmune", m.InjuryImmune).P("restEnergyMult", m.RestEnergyMult).P("neverFail", m.NeverFail)
                .P("glassCannonBonus", m.GlassCannonBonus).P("echoFrac", m.EchoFrac).EndObj();

        private void Entry(JsonOut w, VaultEntry e)
        {
            w.Obj().P("id", e.Id).P("species", e.SpeciesId)
                .P("grade", e.Grade).P("rank", e.RankIndex).P("end", e.End.ToString().ToLowerInvariant()).P("cycles", e.Cycles)
                .P("fightsFought", e.FightsFought).P("fightsWon", e.FightsWon).P("sealedUtcMs", e.SealedUtcMs)
                .P("locked", e.Locked).P("refund", AccountOps.DismantleRefund(_db, e)).P("rulebook", _acct?.Rulebook(e.Id) ?? "");
            w.Key("skills").Arr();   // the active skills a rulebook may name, in the fighter's order (mskill ids; tooltips under skillTips)
            foreach (var sk in e.Fighter(_db).Skills) if (sk.Kind != SkillKind.Passive) w.Str(sk.Id);
            w.EndArr();
            string apex = ApexForm.TreeOf(_db, e);
            w.P("apex", apex).P("name", ApexForm.Name(_db, e.SpeciesId, apex));
            w.Key("stats"); Stats(w, e.Stats);
            w.Key("trees").Arr();
            foreach (var t in e.Trees)
            {
                w.Obj().P("id", t.TreeId).P("bought", t.Bought).P("fusedInto", t.FusedInto);
                w.Key("rolled"); Rolled(w, t.TreeId, t.Roll);
                w.EndObj();
            }
            w.EndArr();
            w.Key("gauntlet");
            if (e.LegacyTitle == null) w.Null();   // sealed before the gauntlet existed
            else
            {
                string title = GauntletSim.TitleText(_db, e.LegacyTitle, e.GauntletKiller);
                // "Floor 42: Professional Punching Bag" (specs/12 section 6); a cleared tower has no losing floor to name.
                bool cleared = e.ClearedGauntlet();
                string label = (cleared ? "All " + (e.GauntletFloor - 1) + " floors" : "Floor " + e.GauntletFloor) + ": " + title;
                w.Obj().P("floor", e.GauntletFloor).P("cleared", cleared).P("killer", e.GauntletKiller)
                    .P("killerName", e.GauntletKiller == null ? null : GauntletSim.EnemyName(_db, e.GauntletKiller))
                    .P("title", title).P("label", label).EndObj();
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
                if (!AccountCommand.IsCommand(name)) return Fail("unknown command");
                var cmd = AccountCommand.Parse(name, a);
                if (!AccountCommands.IsRunCommand(cmd.Kind)) return Fail("unknown command");
                var log = cmd.Kind == CommandKind.Fight ? new FightLog() : null;
                var res = AccountCommands.Apply(_db, _acct, cmd, log == null ? null : (Action<FightAction>)log.Record, log == null ? null : (Action<FightState>)log.Watch);
                switch (cmd.Kind)
                {
                    case CommandKind.Drill:
                    {
                        var r = (DrillResult)res;
                        w.P("ok", true).P("kind", "drill").P("stat", r.Stat).P("tier", r.Tier.ToString().ToLowerInvariant())
                            .P("tierName", _db.Text("drill." + r.Tier.ToString().ToLowerInvariant() + ".name"))
                            .P("gain", r.Gain).P("value", r.NewValue).P("energy", r.EnergyAfter).P("injured", r.Injured)
                            .P("shielded", r.Shielded).P("free", r.Free).P("echoStat", r.EchoStat).P("echoGain", r.EchoGain);
                        w.Key("injury");
                        if (r.Injury == null) w.Null(); else Injury(w, r.Injury);
                        Milestones(w, r.Milestones);
                        break;
                    }
                    case CommandKind.Rest:
                    {
                        var r = (RestResult)res;
                        w.P("ok", true).P("kind", "rest").P("restored", r.Restored).P("energy", r.EnergyAfter);
                        break;
                    }
                    case CommandKind.Fight:
                    {
                        var r = (RankFightResult)res;
                        w.P("ok", true).P("kind", "fight").P("won", r.Won).P("rankBefore", r.RankBefore).P("rankAfter", r.RankAfter).P("sp", r.SpGained);
                        w.Key("opponent"); LadderStep(w, s, r.RankBefore); w.EndObj();   // who was fought (the ladder does not change)
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
                    case CommandKind.Take:
                    {
                        var r = (TakeResult)res;
                        w.P("ok", true).P("kind", "take").P("placed", r.Placed).P("refunded", r.Refunded);
                        w.Key("removed").Arr();
                        foreach (var id in r.Removed) w.Str(id);
                        w.EndArr();
                        break;
                    }
                    case CommandKind.Discard: w.P("ok", true).P("kind", "discard"); break;
                    case CommandKind.Reroll: w.P("ok", true).P("kind", "reroll"); break;
                    case CommandKind.Spend:
                    {
                        var r = (SpendResult)res;
                        var (nn, nd) = NodeText(r.Node);
                        w.P("ok", true).P("kind", "spend").P("node", nn).P("desc", nd).P("statGain", r.StatGain);
                        Milestones(w, r.Milestones);
                        break;
                    }
                    case CommandKind.Book: w.P("ok", true).P("kind", "book"); break;
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
                public double[] Sh;        // every unit's shield after the action
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
                var e = new Entry { X = x, Hp = new double[n], Sh = new double[n], Si = -1 };
                FightUnit actor = null;
                for (int i = 0; i < n; i++)
                {
                    var u = _live.Units[i];
                    e.Hp[i] = Math.Max(0, u.Hp);
                    e.Sh[i] = Math.Max(0, u.Shield);
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
            w.Obj().P("id", u.Id).P("side", u.Side).P("hp", Math.Max(0, u.Hp)).P("maxHp", u.MaxHp).P("shield", Math.Max(0, u.Shield))
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
            w.Obj().P("actor", x.Actor).P("target", x.Target).P("amount", x.Amount).P("crit", x.Crit).P("evaded", x.Evaded).P("aoe", x.Aoe).P("counter", x.Counter)
                .P("graze", x.Graze).P("lucky", x.Lucky);
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
            w.Key("sh").Arr();
            foreach (var v in e.Sh) w.Num(v);
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
            double fail = TrainingRun.RunFailChance(_db, s, e), super = TrainingRun.RunSuperChance(_db, s, e);
            var w = new JsonOut().Obj();
            w.P("species", s.SpeciesId).P("seed", s.Seed.ToString("x16", CultureInfo.InvariantCulture));
            string apex = ApexForm.TreeOf(_db, s);
            w.P("apex", apex).P("name", ApexForm.Name(_db, s.SpeciesId, apex));
            w.P("phase", s.Phase.ToString().ToLowerInvariant()).P("end", s.End.ToString().ToLowerInvariant());
            w.P("cycle", s.Cycle).P("ap", s.ApLeft).P("energy", s.Energy).P("sp", s.Sp).P("rank", s.Rank);
            w.P("fightsFought", s.FightsFought).P("fightsWon", s.FightsWon).P("drills", s.Drills).P("rests", s.Rests);
            w.P("fightDue", s.Phase == RunPhase.AwaitFight);
            w.P("drillCost", TrainingRun.DrillCost(_db, s)).P("restEnergy", TrainingRun.RestEnergy(_db, s));
            w.P("freeDrills", s.FreeDrills).P("shieldsLeft", s.Mods.InjuryShields - s.ShieldsUsed).P("rerollsLeft", s.Mods.DraftRerolls - s.RerollsUsed)
                .P("canReroll", TrainingRun.CanRerollDraft(_db, s));
            w.Key("mods"); Mods(w, s.Mods);
            w.Key("milestoneAt").Arr();   // the value each stat_milestones threshold is crossed at this run (Early Bloom lowers it)
            foreach (int t in _db.StatMilestones) w.Num(TrainingRun.MilestoneAt(s, t));
            w.EndArr();
            w.Key("odds").Obj().P("fail", fail).P("super", super).P("success", Math.Max(0, 1 - fail - super))
                .P("injury", TrainingRun.InjuryChance(k, e)).EndObj();
            w.Key("stats"); Stats(w, s.Stats);
            var opp = TrainingRun.OpponentFighter(_db, s, s.Rank);
            var oppEnemy = TrainingRun.LadderEnemy(_db, s, s.Rank);
            string oppName = _db.Text(oppEnemy != null ? oppEnemy.NameKey : _db.FightRanks[s.Rank].NameKey);
            TipJson.StatNow(w, _db, s.Stats, opp.Stats, oppName);
            w.Key("growth").Obj();
            foreach (var id in StatIds.Trained) w.P(id, TrainingRun.Growth(_db, s, id));
            w.EndObj();
            w.Key("opponent"); LadderStep(w, s, s.Rank);   // the current rank's opponent on the ladder
            w.Key("stats"); Stats(w, opp.Stats);
            TipJson.StatNow(w, _db, opp.Stats, s.Stats, _db.Text(sp.NameKey));
            // The element matchup of the units' own elements (a skill with an element of its own is not counted): the damage
            // multiplier on the monster's hits on the opponent, and on the opponent's hits on the monster (the fight intro, issue #126).
            w.P("elementDealt", _db.ElementMult(sp.Element, opp.Element)).P("elementTaken", _db.ElementMult(opp.Element, sp.Element));
            w.EndObj();
            // The whole ladder, lowest rank first (specs/14 section 5): beaten = the monster won a fight at that rank (below its
            // rank now, or a win at the top rank, where a win does not promote); current = the rank fought next.
            int top = _db.FightRanks.Count - 1;
            w.Key("ladder").Arr();
            for (int i = 0; i <= top; i++)
            {
                LadderStep(w, s, i);
                w.P("beaten", i < s.Rank || (i == top && s.Rank == top && s.FightsWon > s.Rank)).P("current", i == s.Rank).EndObj();
            }
            w.EndArr();
            w.Key("slots").Arr();
            foreach (var t in s.Slots)
            {
                if (t == null) { w.Null(); continue; }
                w.Obj().P("tree", t.TreeId).P("bought", t.Bought);
                w.Key("rolled"); Rolled(w, t.TreeId, t.Roll);
                w.Key("absorbed").Arr();   // a fusion tree's parents (specs/15 section 5.6)
                foreach (var a in t.Absorbed) { w.Obj().P("tree", a.TreeId).P("bought", a.Bought); w.Key("rolled"); Rolled(w, a.TreeId, a.Roll); w.EndObj(); }
                w.EndArr().EndObj();
            }
            w.EndArr();
            w.Key("offers").Arr();
            foreach (var o in s.Offers)
            {
                w.Obj().P("source", o.Source.ToString().ToLowerInvariant()).P("stat", o.Stat).P("threshold", o.Threshold);
                w.Key("trees").Arr();
                foreach (var t in o.Trees) w.Str(t);
                w.EndArr();
                w.Key("rolled").Arr();   // per tree: its rolls (specs/15 section 4.1), or null
                for (int i = 0; i < o.Trees.Count; i++) Rolled(w, o.Trees[i], o.RollOf(i));
                w.EndArr().EndObj();
            }
            w.EndArr();
            w.Key("books").Arr();
            foreach (var b in s.Books) w.Str(b);
            w.EndArr();
            w.Key("chase").Arr();   // the species' own chase trees (in-run, then book-gated) and which conditions hold
            foreach (var t in new[] { sp.ChaseTreeRun, sp.ChaseTreeBook }.Select(id => _db.Trees[id]))
            {
                w.Obj().P("tree", t.Id).P("open", TrainingRun.ChaseOpen(_db, s, t)).P("unlocked", TrainingRun.ChaseConditionsHold(_db, s, t));
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
