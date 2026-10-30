using System;
using System.Collections.Generic;
using System.Text;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;

namespace IdleRng.Sim.Training
{
    /// <summary>
    /// The training run (specs/14) with its skill trees (specs/15): cycles of RUN_AP taps, each a Drill or a Rest with instant
    /// feedback, then a 1v1 rank fight every FIGHT_EVERY cycles; skill points buy tree nodes in order; milestones, books and
    /// chase conditions offer new trees for the 4 slots. Pure functions over a RunState: no clock, IO or Math.Pow; rolls come
    /// from the run's training and draft streams and fight seeds derive from the run seed, so the command log replays exactly.
    /// </summary>
    public static class TrainingRun
    {
        public const string TrainingStream = "training", DraftStream = "draft", FightStream = "fight";
        public const string MonsterId = "monster";
        public const int HashVersion = 2;
        static readonly byte[] HashMagic = Encoding.ASCII.GetBytes("IDLERNG-RUNHASH-v2");

        // ------------------------------------------------------------------ start

        /// <summary>
        /// A fresh run of <paramref name="speciesId"/>: species base stats, full start energy, cycle 1, lowest rank, the two
        /// native trees in slots 0 and 1, SP_PER_CYCLE skill points, and <paramref name="books"/> to use during the run.
        /// </summary>
        public static RunState Start(ContentDb c, string speciesId, ulong seed, IEnumerable<string> books = null)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (speciesId == null || !c.Species.TryGetValue(speciesId, out var sp))
                throw new ArgumentException($"unknown species '{speciesId}'", nameof(speciesId));
            var k = c.Constants;
            var s = new RunState
            {
                SpeciesId = speciesId, Seed = seed, Stats = sp.Base.Clone(),
                Energy = k.RunEnergyStart, Cycle = 1, ApLeft = k.RunAp, Rank = 0, Phase = RunPhase.Training,
                Training = new RngStream(TrainingStream, StreamNames.StreamSeed(seed, TrainingStream)),
                Draft = new RngStream(DraftStream, StreamNames.StreamSeed(seed, DraftStream)),
                Sp = k.SpPerCycle, Slots = new TreeSlot[k.TreeSlots],
            };
            s.Slots[0] = new TreeSlot { TreeId = sp.NativeTree1 };
            s.Slots[1] = new TreeSlot { TreeId = sp.NativeTree2 };
            if (books != null)
                foreach (var b in books)
                {
                    if (b == null || !c.SkillBooks.ContainsKey(b)) throw new ArgumentException($"unknown skill book '{b}'", nameof(books));
                    s.Books.Add(b);
                }
            Reconcile(c, s, null);
            return s;
        }

        // ------------------------------------------------------------------ training commands

        /// <summary>
        /// Drill one trained stat (specs/14 section 4). Rolls the tier (fail/success/super, odds from current energy), then the
        /// gain inside the tier's range, then injury when energy is low. Costs 1 AP and DRILL_ENERGY_COST energy. Growth is the
        /// species' plus the bought growth nodes of the slotted trees.
        /// </summary>
        public static DrillResult Drill(ContentDb c, RunState s, string stat)
        {
            RequirePhase(s, RunPhase.Training, "Drill");
            if (!StatIds.IsTrained(stat)) throw new ArgumentException($"'{stat}' is not a trained stat", nameof(stat));
            var k = c.Constants;
            double e = EnergyFraction(k, s.Energy);

            var tier = RollTier(k, e, s.Training.NextDouble());
            var def = c.DrillTiers[(int)tier];
            int span = def.GainMax - def.GainMin + 1;
            int raw = def.GainMin + Math.Min(span - 1, (int)(s.Training.NextDouble() * span));
            double before = s.Stats.Get(stat);
            double after = Math.Min(k.StatMax, before + Math.Floor(raw * Growth(c, s, stat)));
            Set(s.Stats, stat, after);

            double injuryP = InjuryChance(k, e);
            bool injured = injuryP > 0 && s.Training.NextDouble() < injuryP;

            s.Energy = Math.Max(0, s.Energy - k.DrillEnergyCost);
            s.Drills++;
            s.Log.Add(new RunCommand(RunCommandKind.Drill, stat));
            var result = new DrillResult { Stat = stat, Tier = tier, Gain = after - before, NewValue = after, EnergyAfter = s.Energy, Injured = injured };
            CrossMilestones(c, s, stat, before, after, result.Milestones);

            if (injured) { s.Phase = RunPhase.Finished; s.End = RunEnd.Injured; s.ApLeft = 0; }
            else SpendAp(c, s);
            Reconcile(c, s, null);
            return result;
        }

        /// <summary>Rest: restore REST_ENERGY (capped at max). Costs 1 AP. No roll.</summary>
        public static RestResult Rest(ContentDb c, RunState s)
        {
            RequirePhase(s, RunPhase.Training, "Rest");
            var k = c.Constants;
            double before = s.Energy;
            s.Energy = Math.Min(k.RunEnergyMax, s.Energy + k.RestEnergy);
            s.Rests++;
            s.Log.Add(new RunCommand(RunCommandKind.Rest));
            var result = new RestResult { Restored = s.Energy - before, EnergyAfter = s.Energy };
            SpendAp(c, s);
            Reconcile(c, s, null);
            return result;
        }

        /// <summary>
        /// The due rank fight (specs/14 section 5): the monster with its current stats and bought tree skills against the
        /// current rank's opponent, 1v1. A win ranks up (the top rank stays) and pays SP_PER_WIN. The fight's tallies add to the
        /// run tally. Then the next cycle starts, or the run completes.
        /// </summary>
        public static RankFightResult Fight(ContentDb c, RunState s, Action<FightAction> onAction = null)
        {
            RequirePhase(s, RunPhase.AwaitFight, "Fight");
            var rank = c.FightRanks[s.Rank];
            var monster = MonsterFighter(c, s);
            var opponent = new Fighter { Id = "rank_" + rank.Id, Element = rank.Element, Stats = rank.Stats.Clone() };
            var fight = BattleSim.Resolve(FightRules.FromContent(c), new[] { monster }, new[] { opponent }, FightSeed(s.Seed, s.FightsFought), onAction);

            var result = new RankFightResult { RankBefore = s.Rank, Won = fight.Outcome == FightOutcome.SideAWon, Fight = fight };
            s.FightsFought++;
            AddTally(s.Tally, fight.Final.Units[0].Tally);
            if (result.Won)
            {
                s.FightsWon++;
                s.Sp += c.Constants.SpPerWin;
                result.SpGained = c.Constants.SpPerWin;
                if (s.Rank < c.FightRanks.Count - 1) s.Rank++;
            }
            result.RankAfter = s.Rank;
            s.Log.Add(new RunCommand(RunCommandKind.Fight));
            NextCycle(c, s);
            Reconcile(c, s, null);
            return result;
        }

        // ------------------------------------------------------------------ tree commands (any phase)

        /// <summary>
        /// Take tree <paramref name="choice"/> of the oldest offer into <paramref name="slot"/>. An occupied slot is overwritten:
        /// its points are lost and its flat stat boosts removed (specs/15 section 4). Afterwards any slotted chase tree whose
        /// conditions no longer hold locks again and leaves its slot.
        /// </summary>
        public static TakeResult TakeTree(ContentDb c, RunState s, int choice, int slot)
        {
            RequireOffer(s);
            var offer = s.Offers[0];
            if (choice < 0 || choice >= offer.Trees.Count) throw new ArgumentOutOfRangeException(nameof(choice), choice, "not an offered tree");
            if (slot < 0 || slot >= s.Slots.Length) throw new ArgumentOutOfRangeException(nameof(slot), slot, "not a tree slot");
            string tree = offer.Trees[choice];
            int already = Slotted(s, tree);
            if (already >= 0 && already != slot) throw new InvalidOperationException($"{tree} is already in slot {already}");
            var result = new TakeResult { Placed = tree };
            if (s.Slots[slot] != null) result.Removed.Add(RemoveSlot(s, slot));
            s.Slots[slot] = new TreeSlot { TreeId = tree };
            s.Offers.RemoveAt(0);
            s.Log.Add(new RunCommand(RunCommandKind.TakeTree) { Choice = choice, Slot = slot });
            Reconcile(c, s, result.Removed);
            return result;
        }

        /// <summary>Decline the oldest offer.</summary>
        public static void DiscardOffer(ContentDb c, RunState s)
        {
            RequireOffer(s);
            s.Offers.RemoveAt(0);
            s.Log.Add(new RunCommand(RunCommandKind.DiscardOffer));
            Reconcile(c, s, null);
        }

        /// <summary>Buy the next node of the tree in <paramref name="slot"/> (nodes are bought in order) for its cost in SP.</summary>
        public static SpendResult SpendPoint(ContentDb c, RunState s, int slot)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (slot < 0 || slot >= s.Slots.Length || s.Slots[slot] == null) throw new InvalidOperationException($"slot {slot} has no tree");
            var ts = s.Slots[slot];
            var tree = c.Trees[ts.TreeId];
            if (ts.Bought >= tree.Nodes.Count) throw new InvalidOperationException($"{tree.Id} is maxed");
            var node = tree.Nodes[ts.Bought];
            if (s.Sp < node.Cost) throw new InvalidOperationException($"{node.Id} costs {node.Cost} SP, have {s.Sp}");

            s.Sp -= node.Cost;
            ts.Bought++;
            var result = new SpendResult { Node = node };
            if (node.Kind == NodeKind.Stat)
            {
                double before = s.Stats.Get(node.Stat);
                double add = Math.Max(0, Math.Min(node.Amount, c.Constants.StatMax - before));
                Set(s.Stats, node.Stat, before + add);
                ts.FlatAdded[Array.IndexOf(StatIds.Trained, node.Stat)] += add;
                result.StatGain = add;
                CrossMilestones(c, s, node.Stat, before, before + add, result.Milestones);
            }
            s.Log.Add(new RunCommand(RunCommandKind.SpendPoint) { Slot = slot });
            Reconcile(c, s, null);
            return result;
        }

        /// <summary>Use a skill book brought into the run: offers its book tree (specs/15 section 4). The book is consumed.</summary>
        public static void UseBook(ContentDb c, RunState s, string bookId)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            int i = s.Books.IndexOf(bookId);
            if (i < 0) throw new InvalidOperationException($"no skill book '{bookId}' in this run");
            string tree = c.SkillBooks[bookId].TreeId;
            if (Slotted(s, tree) >= 0 || Pending(s, tree)) throw new InvalidOperationException($"{tree} is already slotted or offered");
            s.Books.RemoveAt(i);
            s.Offers.Add(new TreeOffer { Source = OfferSource.Book, Trees = { tree } });
            s.Log.Add(new RunCommand(RunCommandKind.UseBook) { Book = bookId });
            Reconcile(c, s, null);
        }

        // ------------------------------------------------------------------ the monster as a fighter

        /// <summary>
        /// The monster as a battle fighter: species element, current stats, and the bought skill, trigger and rule nodes of its
        /// slotted trees in slot order then node order (that order is the AI's skill priority).
        /// </summary>
        public static Fighter MonsterFighter(ContentDb c, RunState s)
        {
            var f = new Fighter { Id = MonsterId, Element = c.Species[s.SpeciesId].Element, Stats = s.Stats.Clone() };
            foreach (var ts in s.Slots)
            {
                if (ts == null) continue;
                var nodes = c.Trees[ts.TreeId].Nodes;
                for (int i = 0; i < ts.Bought; i++)
                {
                    var n = nodes[i];
                    switch (n.Kind)
                    {
                        case NodeKind.Skill: f.Skills.Add(n.Skill); break;
                        case NodeKind.Trigger: f.Triggers.Add(new TriggeredSkill(n.Trigger.Value, n.Skill)); break;
                        case NodeKind.Rule: if (!f.Rules.Contains(n.RuleId)) f.Rules.Add(n.RuleId); break;
                    }
                }
            }
            return f;
        }

        /// <summary>Drill growth for <paramref name="stat"/>: species growth plus the bought growth nodes of the slotted trees.</summary>
        public static double Growth(ContentDb c, RunState s, string stat)
        {
            double g = c.Species[s.SpeciesId].Growth.Get(stat);
            foreach (var ts in s.Slots)
            {
                if (ts == null) continue;
                var nodes = c.Trees[ts.TreeId].Nodes;
                for (int i = 0; i < ts.Bought; i++)
                    if (nodes[i].Kind == NodeKind.Growth && nodes[i].Stat == stat) g += nodes[i].Amount;
            }
            return g;
        }

        /// <summary>Whether every condition of chase tree <paramref name="tree"/> holds now (specs/15 section 5).</summary>
        public static bool ChaseConditionsHold(ContentDb c, RunState s, TreeDef tree)
        {
            foreach (var cd in tree.Conditions)
            {
                switch (cd.Type)
                {
                    case ConditionType.TreeMaxed:
                    {
                        int i = Slotted(s, cd.Ref);
                        if (i < 0 || s.Slots[i].Bought < c.Trees[cd.Ref].Nodes.Count) return false;
                        break;
                    }
                    case ConditionType.NodeOwned:
                    {
                        var n = c.TreeNodes[cd.Ref];
                        int i = Slotted(s, n.TreeId);
                        if (i < 0 || s.Slots[i].Bought <= n.Index) return false;
                        break;
                    }
                    case ConditionType.StatAtLeast:
                        if (s.Stats.Get(cd.Ref) < cd.Amount) return false;
                        break;
                    case ConditionType.TallyAtLeast:
                        if (TallyValue(s, cd.Ref) < cd.Amount) return false;
                        break;
                }
            }
            return true;
        }

        public static double TallyValue(RunState s, string name)
        {
            switch (name)
            {
                case "damage_dealt": return s.Tally.DamageDealt;
                case "evades": return s.Tally.Evades;
                case "crits": return s.Tally.Crits;
                case "kills": return s.Tally.Kills;
                case "wins": return s.FightsWon;
                default:
                    if (name.StartsWith("damage_", StringComparison.Ordinal) && s.Tally.DamageByElement.TryGetValue(name.Substring(7), out var v)) return v;
                    return 0;
            }
        }

        /// <summary>Seed of the run's <paramref name="index"/>-th fight (0-based). Fights never draw from the training stream.</summary>
        public static ulong FightSeed(ulong runSeed, int index) =>
            SplitMix64.Derive(StreamNames.StreamSeed(runSeed, FightStream), (ulong)index);

        // ------------------------------------------------------------------ odds (public for the UI and tests)

        public static double EnergyFraction(Constants k, double energy) => Clamp01(energy / k.RunEnergyMax);

        /// <summary>Fail chance at energy fraction e: DRILL_FAIL_MIN at full energy up to DRILL_FAIL_MAX at zero.</summary>
        public static double FailChance(Constants k, double e) => k.DrillFailMin + (k.DrillFailMax - k.DrillFailMin) * (1 - Clamp01(e));

        /// <summary>Injury chance of a drill at energy fraction e: 0 at or above INJURY_BELOW, rising to INJURY_MAX_P at zero.</summary>
        public static double InjuryChance(Constants k, double e)
        {
            if (k.InjuryBelow <= 0 || e >= k.InjuryBelow) return 0;
            return k.InjuryMaxP * (k.InjuryBelow - Clamp01(e)) / k.InjuryBelow;
        }

        /// <summary>Tier from one draw u: [0, fail) fail, [fail, 1 - super) success, [1 - super, 1) super.</summary>
        public static DrillTier RollTier(Constants k, double e, double u)
        {
            if (u < FailChance(k, e)) return DrillTier.Fail;
            if (u < 1 - k.DrillSuperP) return DrillTier.Success;
            return DrillTier.Super;
        }

        // ------------------------------------------------------------------ flow

        static void SpendAp(ContentDb c, RunState s)
        {
            s.ApLeft--;
            if (s.ApLeft > 0) return;
            if (s.Cycle % c.Constants.FightEvery == 0) s.Phase = RunPhase.AwaitFight;
            else NextCycle(c, s);
        }

        static void NextCycle(ContentDb c, RunState s)
        {
            if (s.Cycle >= c.Constants.RunCycles)
            {
                s.Phase = RunPhase.Finished;
                s.End = RunEnd.Completed;
                s.ApLeft = 0;
                return;
            }
            s.Cycle++;
            s.ApLeft = c.Constants.RunAp;
            s.Sp += c.Constants.SpPerCycle;
            s.Phase = RunPhase.Training;
        }

        /// <summary>Record each threshold crossed upward (once per stat and threshold) and open a milestone draft for it.</summary>
        static void CrossMilestones(ContentDb c, RunState s, string stat, double before, double after, List<MilestoneHit> into)
        {
            foreach (int t in c.StatMilestones)
            {
                if (!(before < t && after >= t) || s.Milestones.Exists(m => m.Stat == stat && m.Threshold == t)) continue;
                var hit = new MilestoneHit { Stat = stat, Threshold = t, Cycle = s.Cycle };
                s.Milestones.Add(hit);
                into.Add(hit);
                Draft(c, s, stat, t);
            }
        }

        /// <summary>
        /// A milestone draft (specs/15 section 4): up to DRAFT_SIZE distinct generic trees for this stat (or any stat) at or below
        /// this threshold that are not slotted or already offered, picked by partial shuffle on the draft stream (one draw per
        /// pick). No eligible tree, no offer.
        /// </summary>
        static void Draft(ContentDb c, RunState s, string stat, int threshold)
        {
            var pool = new List<string>();
            foreach (var t in c.TreeList)
                if (t.Kind == TreeKind.Generic && (t.OfferStat == null || t.OfferStat == stat) && t.OfferThreshold <= threshold
                    && Slotted(s, t.Id) < 0 && !Pending(s, t.Id))
                    pool.Add(t.Id);
            int n = Math.Min(c.Constants.DraftSize, pool.Count);
            if (n == 0) return;
            for (int i = 0; i < n; i++)
            {
                int j = i + Math.Min(pool.Count - i - 1, (int)(s.Draft.NextDouble() * (pool.Count - i)));
                var tmp = pool[i]; pool[i] = pool[j]; pool[j] = tmp;
            }
            s.Offers.Add(new TreeOffer { Source = OfferSource.Milestone, Stat = stat, Threshold = threshold, Trees = pool.GetRange(0, n) });
        }

        /// <summary>
        /// Keep chase trees consistent with their conditions (specs/15 section 5): a slotted chase tree whose conditions fail
        /// locks again and leaves its slot (repeated until stable, since one removal can break another); a pending chase offer
        /// whose conditions fail is withdrawn; a chase tree that holds and is neither slotted nor already offered is offered.
        /// </summary>
        static void Reconcile(ContentDb c, RunState s, List<string> removed)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < s.Slots.Length; i++)
                {
                    var ts = s.Slots[i];
                    if (ts == null) continue;
                    var tree = c.Trees[ts.TreeId];
                    if (tree.Kind == TreeKind.Chase && !ChaseConditionsHold(c, s, tree))
                    {
                        string id = RemoveSlot(s, i);
                        removed?.Add(id);
                        changed = true;
                    }
                }
            }
            s.Offers.RemoveAll(o => o.Source == OfferSource.Chase && !ChaseConditionsHold(c, s, c.Trees[o.Trees[0]]));
            s.ChaseOffered.RemoveAll(id => !ChaseConditionsHold(c, s, c.Trees[id]));
            foreach (var t in c.TreeList)
            {
                if (t.Kind != TreeKind.Chase || s.ChaseOffered.Contains(t.Id) || Slotted(s, t.Id) >= 0) continue;
                if (!ChaseConditionsHold(c, s, t)) continue;
                s.Offers.Add(new TreeOffer { Source = OfferSource.Chase, Trees = { t.Id } });
                s.ChaseOffered.Add(t.Id);
            }
        }

        /// <summary>Empty a slot: its points are lost and its flat stat boosts come off (not below 0). Returns the tree id.</summary>
        static string RemoveSlot(RunState s, int slot)
        {
            var ts = s.Slots[slot];
            for (int i = 0; i < StatIds.Trained.Length; i++)
            {
                if (ts.FlatAdded[i] == 0) continue;
                string id = StatIds.Trained[i];
                Set(s.Stats, id, Math.Max(0, s.Stats.Get(id) - ts.FlatAdded[i]));
            }
            s.Slots[slot] = null;
            return ts.TreeId;
        }

        static int Slotted(RunState s, string treeId)
        {
            for (int i = 0; i < s.Slots.Length; i++) if (s.Slots[i] != null && s.Slots[i].TreeId == treeId) return i;
            return -1;
        }

        static bool Pending(RunState s, string treeId)
        {
            foreach (var o in s.Offers) if (o.Trees.Contains(treeId)) return true;
            return false;
        }

        static void AddTally(RunTally run, UnitTally fight)
        {
            run.DamageDealt += fight.DamageDealt;
            run.Evades += fight.Evades;
            run.Crits += fight.Crits;
            run.Kills += fight.Kills;
            foreach (var kv in fight.DamageByElement)
            {
                run.DamageByElement.TryGetValue(kv.Key, out var prev);
                run.DamageByElement[kv.Key] = prev + kv.Value;
            }
        }

        static void RequirePhase(RunState s, RunPhase phase, string command)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (s.Phase != phase) throw new InvalidOperationException($"{command} needs phase {phase}, run is in {s.Phase}");
            if (s.Offers.Count > 0) throw new InvalidOperationException($"{command} waits until the pending tree offer is taken or discarded");
        }

        static void RequireOffer(RunState s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (s.Offers.Count == 0) throw new InvalidOperationException("no tree offer is pending");
        }

        static void Set(MonsterStats st, string id, double v)
        {
            switch (id)
            {
                case StatIds.Hp: st.Hp = v; break;
                case StatIds.Str: st.Str = v; break;
                case StatIds.Int: st.Int = v; break;
                case StatIds.Wis: st.Wis = v; break;
                case StatIds.Dex: st.Dex = v; break;
                case StatIds.Def: st.Def = v; break;
                case StatIds.Spd: st.Spd = v; break;
                case StatIds.Luk: st.Luk = v; break;
                default: throw new ArgumentOutOfRangeException(nameof(id), id, "not a trained stat");
            }
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        // ------------------------------------------------------------------ replay and hash

        /// <summary>Rebuild a run from its species, seed, books and command log. Throws if a command is not valid at its point.</summary>
        public static RunState Replay(ContentDb c, string speciesId, ulong seed, IEnumerable<RunCommand> log, IEnumerable<string> books = null)
        {
            var s = Start(c, speciesId, seed, books);
            foreach (var cmd in log) Apply(c, s, cmd);
            return s;
        }

        /// <summary>Run one command (bots and replay).</summary>
        public static void Apply(ContentDb c, RunState s, RunCommand cmd)
        {
            switch (cmd.Kind)
            {
                case RunCommandKind.Drill: Drill(c, s, cmd.Stat); break;
                case RunCommandKind.Rest: Rest(c, s); break;
                case RunCommandKind.Fight: Fight(c, s); break;
                case RunCommandKind.TakeTree: TakeTree(c, s, cmd.Choice, cmd.Slot); break;
                case RunCommandKind.DiscardOffer: DiscardOffer(c, s); break;
                case RunCommandKind.SpendPoint: SpendPoint(c, s, cmd.Slot); break;
                case RunCommandKind.UseBook: UseBook(c, s, cmd.Book); break;
                default: throw new ArgumentOutOfRangeException(nameof(cmd), cmd.Kind, "unknown command");
            }
        }

        /// <summary>Lowercase hex SHA-256 over a canonical encoding of the run (StateHasher conventions). Bump HashVersion on change.</summary>
        public static string StateHash(RunState s)
        {
            var w = new CanonicalWriter(512);
            w.Bytes(HashMagic);
            w.Str(s.SpeciesId);
            w.U64(s.Seed);
            foreach (var id in StatIds.Trained) w.F64(s.Stats.Get(id));
            w.F64(s.Energy);
            w.I32(s.Cycle); w.I32(s.ApLeft); w.I32(s.Rank);
            w.I32((int)s.Phase); w.I32((int)s.End);
            Stream(w, s.Training);
            Stream(w, s.Draft);
            w.I32(s.FightsFought); w.I32(s.FightsWon); w.I32(s.Drills); w.I32(s.Rests);
            w.I32(s.Sp);
            w.I32(s.Slots.Length);
            foreach (var ts in s.Slots)
            {
                w.Bool(ts != null);
                if (ts == null) continue;
                w.Str(ts.TreeId); w.I32(ts.Bought);
                foreach (var v in ts.FlatAdded) w.F64(v);
            }
            w.I32(s.Offers.Count);
            foreach (var o in s.Offers)
            {
                w.I32((int)o.Source); w.Str(o.Stat); w.I32(o.Threshold);
                w.I32(o.Trees.Count);
                foreach (var t in o.Trees) w.Str(t);
            }
            w.I32(s.Books.Count);
            foreach (var b in s.Books) w.Str(b);
            w.I32(s.ChaseOffered.Count);
            foreach (var id in s.ChaseOffered) w.Str(id);
            w.F64(s.Tally.DamageDealt); w.I32(s.Tally.Evades); w.I32(s.Tally.Crits); w.I32(s.Tally.Kills);
            w.I32(s.Tally.DamageByElement.Count);
            foreach (var kv in s.Tally.DamageByElement) { w.Str(kv.Key); w.F64(kv.Value); }
            w.I32(s.Milestones.Count);
            foreach (var m in s.Milestones) { w.Str(m.Stat); w.I32(m.Threshold); w.I32(m.Cycle); }
            w.I32(s.Log.Count);
            foreach (var l in s.Log) { w.I32((int)l.Kind); w.Str(l.Stat); w.I32(l.Choice); w.I32(l.Slot); w.Str(l.Book); }
            return Sha256.Hex(Sha256.Compute(w.ToArray()));
        }

        static void Stream(CanonicalWriter w, RngStream r) { w.Str(r.Name); w.U64(r.Seed); w.U64(r.Counter); }
    }
}
