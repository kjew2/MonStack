using System;
using System.Collections.Generic;
using System.Linq;
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
        public const string TrainingStream = "training", DraftStream = "draft", FightStream = "fight", ChaseStream = "chase", LadderStream = "ladder";
        public const string MonsterId = "monster";

        /// <summary>
        /// Run hash version 7 (the talent catalog v2, specs/18 section 3; specs/14 section 7). Only a run with one of those talents
        /// uses it: the talent fields and counters are appended after the ladder. Every other run hashes exactly as before, version
        /// 6 (the rank ladder) or, for a legacy run with an empty Ladder, version 5, so saved runs and Vault entries keep their hashes.
        /// </summary>
        public const int HashVersion = 7;
        static readonly byte[] HashMagic = Encoding.ASCII.GetBytes("IDLERNG-RUNHASH-v7");
        static readonly byte[] LadderHashMagic = Encoding.ASCII.GetBytes("IDLERNG-RUNHASH-v6");
        static readonly byte[] LegacyHashMagic = Encoding.ASCII.GetBytes("IDLERNG-RUNHASH-v5");

        // ------------------------------------------------------------------ start

        /// <summary>
        /// A fresh run of <paramref name="speciesId"/>: species base stats, full start energy, cycle 1, lowest rank, the two
        /// native trees in slots 0 and 1, SP_PER_CYCLE skill points, and <paramref name="books"/> to use during the run.
        /// <paramref name="mods"/> carries the account's talent bonuses (specs/18); null means none. <paramref name="ladder"/>
        /// null rolls the rank ladder from the seed (<see cref="RollLadder"/>); a saved ladder rebuilds that run (an empty one
        /// is a legacy run: flat fight_ranks opponents); see <see cref="Replay"/>.
        /// </summary>
        public static RunState Start(ContentDb c, string speciesId, ulong seed, IEnumerable<string> books = null, RunModifiers mods = null,
                                     IReadOnlyList<string> ladder = null)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (speciesId == null || !c.Species.TryGetValue(speciesId, out var sp))
                throw new ArgumentException($"unknown species '{speciesId}'", nameof(speciesId));
            var k = c.Constants;
            mods = mods?.Clone() ?? new RunModifiers();
            if (!(mods.DrillCostMult >= 0) || !(mods.StatFloorMult >= 0) || mods.StartSp < 0
                || !(mods.RestEnergyBonus >= 0) || !(mods.FailChanceDelta <= 0) || !(mods.SuperChanceBonus >= 0) || !(mods.ShardMult >= 0)
                || mods.MomentumDrills < 0 || mods.LossSp < 0 || !(mods.SpecialistBonus >= 0 && mods.SpecialistBonus <= 2)
                || mods.TreeSlotBonus < 0 || mods.InjuryShields < 0 || mods.LastCycleAp < 0 || !(mods.MilestoneMult > 0) || mods.DraftRerolls < 0)
                throw new ArgumentException("run modifiers out of range (specs/18 section 3)", nameof(mods));
            if (!(mods.ChaseChance >= 0 && mods.ChaseChance <= 1)) throw new ArgumentException("ChaseChance must be in [0, 1]", nameof(mods));
            var stats = sp.Base.Clone();
            if (mods.StatFloorMult != 1)
                foreach (var id in StatIds.Trained) Set(stats, id, Math.Min(k.StatMax, Math.Floor(stats.Get(id) * mods.StatFloorMult)));
            var s = new RunState
            {
                SpeciesId = speciesId, Seed = seed, Stats = stats, Mods = mods,
                Energy = k.RunEnergyStart, Cycle = 1, Rank = 0, Phase = RunPhase.Training,
                Training = new RngStream(TrainingStream, StreamNames.StreamSeed(seed, TrainingStream)),
                Draft = new RngStream(DraftStream, StreamNames.StreamSeed(seed, DraftStream)),
                Sp = k.SpPerCycle + mods.StartSp, Slots = new TreeSlot[k.TreeSlots + mods.TreeSlotBonus],
                ChaseAttuned = mods.ChaseChance >= 1 || new RngStream(ChaseStream, StreamNames.StreamSeed(seed, ChaseStream)).NextDouble() < mods.ChaseChance,
                Ladder = ladder == null ? RollLadder(c, seed) : CheckLadder(c, ladder),
            };
            s.ApLeft = CycleAp(c, s);
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
        /// species' plus the bought growth nodes of the slotted trees. An injury keeps the gain, then costs the rest of the cycle
        /// and its fight plus INJURY_SKIP_CYCLES whole cycles (<see cref="Injure"/>); the run goes on after that. Talents (specs/18
        /// section 3) shift the fail and super odds, make drills after a Super free (Momentum) and can prevent an injury (Second Wind).
        /// </summary>
        public static DrillResult Drill(ContentDb c, RunState s, string stat)
        {
            RequirePhase(s, RunPhase.Training, "Drill");
            if (!StatIds.IsTrained(stat)) throw new ArgumentException($"'{stat}' is not a trained stat", nameof(stat));
            var k = c.Constants;
            double e = EnergyFraction(k, s.Energy);

            var tier = RollTier(RunFailChance(c, s, e), RunSuperChance(c, s, e), s.Training.NextDouble());
            var def = c.DrillTiers[(int)tier];
            int span = def.GainMax - def.GainMin + 1;
            int raw = def.GainMin + Math.Min(span - 1, (int)(s.Training.NextDouble() * span));
            double before = s.Stats.Get(stat);
            double after = Math.Min(k.StatMax, before + Math.Floor(raw * Growth(c, s, stat)));
            Set(s.Stats, stat, after);

            double injuryP = InjuryChance(k, e);
            bool injured = injuryP > 0 && s.Training.NextDouble() < injuryP;
            bool shielded = injured && s.ShieldsUsed < s.Mods.InjuryShields;
            if (shielded) { injured = false; s.ShieldsUsed++; }

            bool free = s.FreeDrills > 0;
            s.Energy = shielded ? 0 : Math.Max(0, s.Energy - DrillCost(c, s));
            if (free) s.FreeDrills--;
            if (tier == DrillTier.Super && s.Mods.MomentumDrills > 0) s.FreeDrills = Math.Max(s.FreeDrills, s.Mods.MomentumDrills);
            s.Drills++;
            s.Log.Add(new RunCommand(RunCommandKind.Drill, stat));
            var result = new DrillResult
            {
                Stat = stat, Tier = tier, Gain = after - before, NewValue = after, EnergyAfter = s.Energy, Injured = injured, Shielded = shielded, Free = free,
            };
            CrossMilestones(c, s, stat, before, after, result.Milestones);

            if (injured) result.Injury = Injure(c, s);
            else SpendAp(c, s);
            Reconcile(c, s, null);
            return result;
        }

        /// <summary>Rest: restore REST_ENERGY plus the Deep Rest bonus (capped at max). Costs 1 AP. No roll.</summary>
        public static RestResult Rest(ContentDb c, RunState s)
        {
            RequirePhase(s, RunPhase.Training, "Rest");
            var k = c.Constants;
            double before = s.Energy;
            s.Energy = Math.Min(k.RunEnergyMax, s.Energy + RestEnergy(c, s));
            s.Rests++;
            s.Log.Add(new RunCommand(RunCommandKind.Rest));
            var result = new RestResult { Restored = s.Energy - before, EnergyAfter = s.Energy };
            SpendAp(c, s);
            Reconcile(c, s, null);
            return result;
        }

        /// <summary>
        /// The due rank fight (specs/14 section 5): the monster with its current stats and bought tree skills against the
        /// current rank's opponent on the run's ladder, 1v1. A win ranks up (the top rank stays) and pays SP_PER_WIN; a loss is
        /// a rematch with the same opponent next time and pays the Grudge Match talent's SP (specs/18 section 3). The fight's
        /// tallies add to the run tally. Then the next cycle starts, or the run completes. The observers are passed to BattleSim.Resolve.
        /// </summary>
        public static RankFightResult Fight(ContentDb c, RunState s, Action<FightAction> onAction = null, Action<FightState> onStart = null)
        {
            RequirePhase(s, RunPhase.AwaitFight, "Fight");
            var monster = MonsterFighter(c, s);
            var opponent = OpponentFighter(c, s, s.Rank);
            var fight = BattleSim.Resolve(FightRules.FromContent(c), new[] { monster }, new[] { opponent }, FightSeed(s.Seed, s.FightsFought), onAction, onStart);

            var result = new RankFightResult
            {
                RankBefore = s.Rank, OpponentId = LadderEnemy(c, s, s.Rank)?.Id, Won = fight.Outcome == FightOutcome.SideAWon, Fight = fight,
            };
            s.FightsFought++;
            AddTally(s.Tally, fight.Final.Units[0].Tally);
            if (result.Won)
            {
                s.FightsWon++;
                s.Sp += c.Constants.SpPerWin;
                result.SpGained = c.Constants.SpPerWin;
                if (s.Rank < c.FightRanks.Count - 1) s.Rank++;
            }
            else if (s.Mods.LossSp > 0)
            {
                s.Sp += s.Mods.LossSp;
                result.SpGained = s.Mods.LossSp;
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
        /// TREE_REFUND of its spent points comes back (rounded down) and its flat stat boosts are removed (specs/15 section 4).
        /// Afterwards any slotted chase tree whose conditions no longer hold locks again and leaves its slot the same way.
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
            if (offer.Source == OfferSource.Fusion)
            {
                // The fused tree takes one parent's slot and absorbs both; the other parent's slot comes free (specs/15 section 5.6).
                var def = c.Trees[tree];
                int a = Slotted(s, def.FuseA), b = Slotted(s, def.FuseB);
                if (a < 0 || b < 0) throw new InvalidOperationException($"{tree} needs {def.FuseA} and {def.FuseB} in slots");
                if (slot != a && slot != b) throw new InvalidOperationException($"{tree} fuses into the slot of {def.FuseA} or {def.FuseB}");
                var fused = new TreeSlot { TreeId = tree, Absorbed = { s.Slots[a], s.Slots[b] } };
                s.Slots[a] = null;
                s.Slots[b] = null;
                s.Slots[slot] = fused;
            }
            else
            {
                if (s.Slots[slot] != null) RemoveSlot(c, s, slot, result);
                s.Slots[slot] = new TreeSlot { TreeId = tree };
            }
            s.Offers.RemoveAt(0);
            s.Log.Add(new RunCommand(RunCommandKind.TakeTree) { Choice = choice, Slot = slot });
            Reconcile(c, s, result);
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

        /// <summary>
        /// Second Look (specs/18 section 3): redraw the oldest offer, a milestone draft, once per Mods.DraftRerolls. The new trees
        /// are generic trees at or below the draft's threshold for <b>any</b> stat (agent: a stat's own pool is rarely bigger than
        /// one draft), not slotted and not in a pending offer (so none it offered), by the same partial shuffle on the draft
        /// stream. Refused when no such tree is left (<see cref="CanRerollDraft"/>).
        /// </summary>
        public static void RerollDraft(ContentDb c, RunState s)
        {
            RequireOffer(s);
            var offer = s.Offers[0];
            if (offer.Source != OfferSource.Milestone) throw new InvalidOperationException("only an Awakening (a milestone tree offer) can be rerolled");
            if (s.RerollsUsed >= s.Mods.DraftRerolls) throw new InvalidOperationException("no draft rerolls left this run");
            var trees = DraftPicks(c, s, null, offer.Threshold);
            if (trees.Count == 0) throw new InvalidOperationException("no other tree can be drawn for this Awakening");
            offer.Trees = trees;
            s.RerollsUsed++;
            s.Log.Add(new RunCommand(RunCommandKind.RerollDraft));
            Reconcile(c, s, null);
        }

        /// <summary>Whether <see cref="RerollDraft"/> would be accepted now (a reroll left, the oldest offer a milestone draft, a tree to draw).</summary>
        public static bool CanRerollDraft(ContentDb c, RunState s)
        {
            if (s.Offers.Count == 0 || s.Offers[0].Source != OfferSource.Milestone || s.RerollsUsed >= s.Mods.DraftRerolls) return false;
            foreach (var t in c.TreeList)
                if (DraftEligible(s, t, null, s.Offers[0].Threshold)) return true;
            return false;
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
            if (Held(s, tree) || Pending(s, tree)) throw new InvalidOperationException($"{tree} is already slotted or offered");
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
            foreach (var ts in AllTrees(s))
            {
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

        // ------------------------------------------------------------------ the rank ladder (specs/14 section 5)

        /// <summary>
        /// Roll a ladder for a run seed: for each rank in order, one draw on the run's "ladder" stream picks a pool monster by
        /// weight (cumulative weights in CSV order; the draw times the pool's total weight). The training, draft and fight
        /// streams are untouched. The same seed and content always give the same ladder.
        /// </summary>
        public static List<string> RollLadder(ContentDb c, ulong seed)
        {
            var rng = new RngStream(LadderStream, StreamNames.StreamSeed(seed, LadderStream));
            var ladder = new List<string>(c.FightRanks.Count);
            foreach (var rank in c.FightRanks)
            {
                double total = 0;
                foreach (var e in rank.Pool) total += e.Weight;
                double u = rng.NextDouble() * total;
                var pick = rank.Pool[rank.Pool.Count - 1];   // rounding can leave u at the very top: the last one
                foreach (var e in rank.Pool)
                {
                    if (u < e.Weight) { pick = e; break; }
                    u -= e.Weight;
                }
                ladder.Add(pick.Id);
            }
            return ladder;
        }

        /// <summary>A saved ladder: empty (a legacy run), or one id per rank, each from that rank's pool. Throws otherwise.</summary>
        static List<string> CheckLadder(ContentDb c, IReadOnlyList<string> ladder)
        {
            if (ladder.Count == 0) return new List<string>();
            if (ladder.Count != c.FightRanks.Count)
                throw new ArgumentException($"the ladder has {ladder.Count} ranks, content has {c.FightRanks.Count}", nameof(ladder));
            for (int i = 0; i < ladder.Count; i++)
                if (ladder[i] == null || !c.RankEnemies.TryGetValue(ladder[i], out var e) || e.RankIndex != i)
                    throw new ArgumentException($"'{ladder[i]}' is not in the pool of rank {c.FightRanks[i].Id}", nameof(ladder));
            return new List<string>(ladder);
        }

        /// <summary>The pool monster the run fights at rank <paramref name="rank"/>, or null in a legacy run (flat fight_ranks opponent).</summary>
        public static RankEnemyDef LadderEnemy(ContentDb c, RunState s, int rank) =>
            s.Ladder.Count == 0 ? null : c.RankEnemies[s.Ladder[rank]];

        /// <summary>
        /// The opponent at rank <paramref name="rank"/> as a battle fighter: the ladder's pool monster (its element, stats and
        /// skills in content order, which is the AI's priority), or in a legacy run the rank's flat fight_ranks row with no skills.
        /// </summary>
        public static Fighter OpponentFighter(ContentDb c, RunState s, int rank)
        {
            var e = LadderEnemy(c, s, rank);
            if (e == null)
            {
                var r = c.FightRanks[rank];
                return new Fighter { Id = "rank_" + r.Id, Element = r.Element, Stats = r.Stats.Clone() };
            }
            var f = new Fighter { Id = "rank_" + e.Id, Element = e.Element, Stats = e.Stats.Clone() };
            foreach (var sk in e.Skills) f.Skills.Add(c.MonsterSkills[sk]);
            return f;
        }

        /// <summary>
        /// Drill growth for <paramref name="stat"/>: species growth plus the bought growth nodes of the slotted trees, times the
        /// Specialist talent's factor when it is on (x(1 + b) on the highest trained stat, ties included, else x(1 - b / 2)).
        /// </summary>
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
            double b = s.Mods.SpecialistBonus;
            if (b == 0) return g;
            double top = 0;
            foreach (var id in StatIds.Trained) top = Math.Max(top, s.Stats.Get(id));
            return g * (s.Stats.Get(stat) >= top ? 1 + b : 1 - b / 2);
        }

        /// <summary>Whether chase tree <paramref name="tree"/> can ever be offered to this run: an unattuned run never sees its in-run chase tree (specs/15 section 5.4).</summary>
        public static bool ChaseOpen(ContentDb c, RunState s, TreeDef tree) =>
            tree.Kind == TreeKind.Chase && tree.Species == s.SpeciesId && (s.ChaseAttuned || tree.Id != c.Species[s.SpeciesId].ChaseTreeRun);

        /// <summary>Whether every condition of chase tree <paramref name="tree"/> holds now (specs/15 section 5).</summary>
        public static bool ChaseConditionsHold(ContentDb c, RunState s, TreeDef tree)
        {
            foreach (var cd in tree.Conditions)
                if (!ConditionHolds(c, s, cd)) return false;
            return true;
        }

        /// <summary>Whether one chase condition holds now (the UI shows which are met).</summary>
        public static bool ConditionHolds(ContentDb c, RunState s, ChaseConditionDef cd)
        {
            switch (cd.Type)
            {
                case ConditionType.TreeMaxed:
                {
                    var ts = Owned(s, cd.Ref);
                    return ts != null && ts.Bought >= c.Trees[cd.Ref].Nodes.Count;
                }
                case ConditionType.NodeOwned:
                {
                    var n = c.TreeNodes[cd.Ref];
                    var ts = Owned(s, n.TreeId);
                    return ts != null && ts.Bought > n.Index;
                }
                case ConditionType.StatAtLeast: return s.Stats.Get(cd.Ref) >= cd.Amount;
                case ConditionType.TallyAtLeast: return TallyValue(s, cd.Ref) >= cd.Amount;
                default: return true;
            }
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

        /// <summary>Energy the next drill costs: DRILL_ENERGY_COST times the run's talent modifier, or 0 while Momentum has a free drill.</summary>
        public static double DrillCost(ContentDb c, RunState s) => s.FreeDrills > 0 ? 0 : c.Constants.DrillEnergyCost * s.Mods.DrillCostMult;

        /// <summary>Energy a rest restores this run before the cap: REST_ENERGY plus the Deep Rest bonus.</summary>
        public static double RestEnergy(ContentDb c, RunState s) => c.Constants.RestEnergy + s.Mods.RestEnergyBonus;

        /// <summary>The run's fail chance at energy fraction e: <see cref="FailChance"/> plus the Steady Hands delta, not below 0.</summary>
        public static double RunFailChance(ContentDb c, RunState s, double e) => Math.Max(0, FailChance(c.Constants, e) + s.Mods.FailChanceDelta);

        /// <summary>The run's super chance at energy fraction e: DRILL_SUPER_P plus Super Set, at most what the fail chance leaves.</summary>
        public static double RunSuperChance(ContentDb c, RunState s, double e) =>
            Math.Max(0, Math.Min(c.Constants.DrillSuperP + s.Mods.SuperChanceBonus, 1 - RunFailChance(c, s, e)));

        /// <summary>Action points cycle <c>s.Cycle</c> starts with: RUN_AP, plus Overtime's in the last cycle.</summary>
        public static int CycleAp(ContentDb c, RunState s) => c.Constants.RunAp + (s.Cycle == c.Constants.RunCycles ? s.Mods.LastCycleAp : 0);

        /// <summary>The value a stat must reach to cross milestone <paramref name="threshold"/> this run (Early Bloom scales it, rounded).</summary>
        public static double MilestoneAt(RunState s, int threshold) =>
            s.Mods.MilestoneMult == 1 ? threshold : Math.Round(threshold * s.Mods.MilestoneMult, MidpointRounding.AwayFromZero);

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
        public static DrillTier RollTier(Constants k, double e, double u) => RollTier(FailChance(k, e), k.DrillSuperP, u);

        /// <summary>Tier from one draw u with these chances: [0, fail) fail, [fail, 1 - super) success, [1 - super, 1) super.</summary>
        public static DrillTier RollTier(double fail, double super, double u)
        {
            if (u < fail) return DrillTier.Fail;
            if (u < 1 - super) return DrillTier.Success;
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

        /// <summary>
        /// An injury (specs/14 section 4.1, kj 2026-10-31 on issue #89): the rest of the current cycle is lost and its rank fight
        /// is skipped; the next INJURY_SKIP_CYCLES cycles are sat out (no drills, no fight, no SP_PER_CYCLE); then the monster
        /// comes back at INJURY_RECOVER_ENERGY and trains the following cycle as usual. Lost cycles past the last one finish the
        /// run as Completed. No roll: the injury itself was the roll.
        /// </summary>
        static InjuryHit Injure(ContentDb c, RunState s)
        {
            var k = c.Constants;
            var hit = new InjuryHit { Cycle = s.Cycle };
            s.Injuries.Add(hit);
            s.ApLeft = 0;
            for (int i = 0; i < k.InjurySkipCycles && s.Cycle < k.RunCycles; i++) { s.Cycle++; hit.CyclesLost++; }
            NextCycle(c, s);
            if (s.Phase == RunPhase.Finished) return hit;
            s.Energy = k.InjuryRecoverEnergy;
            hit.BackCycle = s.Cycle;
            return hit;
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
            s.ApLeft = CycleAp(c, s);
            s.Sp += c.Constants.SpPerCycle;
            s.Phase = RunPhase.Training;
        }

        /// <summary>
        /// Record each threshold crossed upward (once per stat and threshold) and open a milestone draft for it. A stat crosses
        /// threshold t at <see cref="MilestoneAt"/> (t unless Early Bloom); the hit and the draft still name t.
        /// </summary>
        static void CrossMilestones(ContentDb c, RunState s, string stat, double before, double after, List<MilestoneHit> into)
        {
            foreach (int t in c.StatMilestones)
            {
                double at = MilestoneAt(s, t);
                if (!(before < at && after >= at) || s.Milestones.Exists(m => m.Stat == stat && m.Threshold == t)) continue;
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
            var trees = DraftPicks(c, s, stat, threshold);
            if (trees.Count == 0) return;
            s.Offers.Add(new TreeOffer { Source = OfferSource.Milestone, Stat = stat, Threshold = threshold, Trees = trees });
        }

        /// <summary>
        /// A draft's picks: eligible generic trees (<see cref="DraftEligible"/>; <paramref name="stat"/> null = any stat), partial
        /// shuffle on the draft stream.
        /// </summary>
        static List<string> DraftPicks(ContentDb c, RunState s, string stat, int threshold)
        {
            var pool = new List<string>();
            foreach (var t in c.TreeList)
                if (DraftEligible(s, t, stat, threshold)) pool.Add(t.Id);
            int n = Math.Min(c.Constants.DraftSize, pool.Count);
            for (int i = 0; i < n; i++)
            {
                int j = i + Math.Min(pool.Count - i - 1, (int)(s.Draft.NextDouble() * (pool.Count - i)));
                var tmp = pool[i]; pool[i] = pool[j]; pool[j] = tmp;
            }
            return pool.GetRange(0, n);
        }

        /// <summary>A generic tree for <paramref name="stat"/> (or any stat; null = every stat) at or below the threshold, not slotted, not offered.</summary>
        static bool DraftEligible(RunState s, TreeDef t, string stat, int threshold) =>
            t.Kind == TreeKind.Generic && (stat == null || t.OfferStat == null || t.OfferStat == stat) && t.OfferThreshold <= threshold
            && !Held(s, t.Id) && !Pending(s, t.Id);

        /// <summary>
        /// Keep chase trees consistent with their conditions (specs/15 section 5): a slotted chase tree whose conditions fail
        /// locks again and leaves its slot (repeated until stable, since one removal can break another); a pending chase offer
        /// whose conditions fail is withdrawn; one of the run species' own chase trees (specs/15 section 5.1) that holds and is
        /// neither slotted nor already offered is offered.
        /// </summary>
        static void Reconcile(ContentDb c, RunState s, TakeResult result)
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
                        RemoveSlot(c, s, i, result);
                        changed = true;
                    }
                }
            }
            s.Offers.RemoveAll(o => o.Source == OfferSource.Chase && !ChaseConditionsHold(c, s, c.Trees[o.Trees[0]]));
            s.ChaseOffered.RemoveAll(id => !ChaseConditionsHold(c, s, c.Trees[id]));
            foreach (var t in c.TreeList)
            {
                if (t.Kind != TreeKind.Chase || t.Species != s.SpeciesId || s.ChaseOffered.Contains(t.Id) || Slotted(s, t.Id) >= 0) continue;
                if (!ChaseOpen(c, s, t)) continue;
                if (!ChaseConditionsHold(c, s, t)) continue;
                s.Offers.Add(new TreeOffer { Source = OfferSource.Chase, Trees = { t.Id } });
                s.ChaseOffered.Add(t.Id);
            }
            // Fusion (specs/15 section 5.6): offered once while both parents sit in slots, maxed; withdrawn when that breaks.
            s.Offers.RemoveAll(o => o.Source == OfferSource.Fusion && !FusionReady(c, s, c.Trees[o.Trees[0]]));
            s.FusionOffered.RemoveAll(id => !FusionReady(c, s, c.Trees[id]) && Slotted(s, id) < 0);
            foreach (var t in c.TreeList)
            {
                if (t.Kind != TreeKind.Fusion || s.FusionOffered.Contains(t.Id) || Slotted(s, t.Id) >= 0 || !FusionReady(c, s, t)) continue;
                s.Offers.Add(new TreeOffer { Source = OfferSource.Fusion, Trees = { t.Id } });
                s.FusionOffered.Add(t.Id);
            }
        }

        /// <summary>Whether fusion tree <paramref name="t"/>'s two parents both sit in slots with every node bought.</summary>
        public static bool FusionReady(ContentDb c, RunState s, TreeDef t)
        {
            if (t.Kind != TreeKind.Fusion) return false;
            int a = Slotted(s, t.FuseA), b = Slotted(s, t.FuseB);
            return a >= 0 && b >= 0 && s.Slots[a].Bought >= c.Trees[t.FuseA].Nodes.Count && s.Slots[b].Bought >= c.Trees[t.FuseB].Nodes.Count;
        }

        /// <summary>
        /// Empty a slot: TREE_REFUND of the SP spent in it comes back (rounded down) and its flat stat boosts come off (not below 0).
        /// The tree and the refund are added to <paramref name="result"/> when there is one.
        /// </summary>
        static void RemoveSlot(ContentDb c, RunState s, int slot, TakeResult result)
        {
            var ts = s.Slots[slot];
            int refund = 0;
            foreach (var part in new[] { ts }.Concat(ts.Absorbed))   // a fused tree takes its absorbed parents with it
            {
                var nodes = c.Trees[part.TreeId].Nodes;
                int spent = 0;
                for (int i = 0; i < part.Bought; i++) spent += nodes[i].Cost;
                refund += (int)Math.Floor(spent * c.Constants.TreeRefund);
                for (int i = 0; i < StatIds.Trained.Length; i++)
                {
                    if (part.FlatAdded[i] == 0) continue;
                    string id = StatIds.Trained[i];
                    Set(s.Stats, id, Math.Max(0, s.Stats.Get(id) - part.FlatAdded[i]));
                }
            }
            s.Sp += refund;
            s.Slots[slot] = null;
            if (result == null) return;
            result.Removed.Add(ts.TreeId);
            result.Refunded += refund;
        }

        /// <summary>Every tree the monster holds: each slotted tree, a fused tree's absorbed parents first.</summary>
        public static IEnumerable<TreeSlot> AllTrees(RunState s)
        {
            foreach (var ts in s.Slots)
            {
                if (ts == null) continue;
                foreach (var a in ts.Absorbed) yield return a;
                yield return ts;
            }
        }

        /// <summary>The slotted or absorbed tree <paramref name="treeId"/>, or null.</summary>
        static TreeSlot Owned(RunState s, string treeId)
        {
            foreach (var ts in AllTrees(s)) if (ts.TreeId == treeId) return ts;
            return null;
        }

        static bool Held(RunState s, string treeId) => Owned(s, treeId) != null;

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

        /// <summary>
        /// Rebuild a run from its species, seed, books, modifiers, ladder and command log. Throws if a command is not valid at its
        /// point. A saved run passes its stored ladder (an empty one for a legacy run, which fights the flat fight_ranks
        /// opponents as it did); null rolls it from the seed again, which gives the same ladder while the pools are unchanged.
        /// </summary>
        public static RunState Replay(ContentDb c, string speciesId, ulong seed, IEnumerable<RunCommand> log, IEnumerable<string> books = null,
                                      RunModifiers mods = null, IReadOnlyList<string> ladder = null)
        {
            var s = Start(c, speciesId, seed, books, mods, ladder);
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
                case RunCommandKind.RerollDraft: RerollDraft(c, s); break;
                default: throw new ArgumentOutOfRangeException(nameof(cmd), cmd.Kind, "unknown command");
            }
        }

        /// <summary>
        /// Lowercase hex SHA-256 over a canonical encoding of the run (specs/03 section 1). Bump HashVersion on change. Version 6
        /// is version 5 plus the ladder at the end; a legacy run (empty ladder) keeps the exact version 5 bytes.
        /// </summary>
        public static string StateHash(RunState s)
        {
            bool legacy = s.Ladder.Count == 0, talents = UsesCatalogV2(s.Mods);
            var w = new CanonicalWriter(512);
            w.Bytes(talents ? HashMagic : legacy ? LegacyHashMagic : LadderHashMagic);
            w.Str(s.SpeciesId);
            w.U64(s.Seed);
            w.F64(s.Mods.DrillCostMult); w.F64(s.Mods.StatFloorMult); w.F64(s.Mods.ChaseChance); w.I32(s.Mods.StartSp);
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
            w.I32(s.Injuries.Count);
            foreach (var i in s.Injuries) { w.I32(i.Cycle); w.I32(i.BackCycle); w.I32(i.CyclesLost); }
            w.I32(s.Log.Count);
            foreach (var l in s.Log) { w.I32((int)l.Kind); w.Str(l.Stat); w.I32(l.Choice); w.I32(l.Slot); w.Str(l.Book); }
            if (!legacy)
            {
                w.I32(s.Ladder.Count);
                foreach (var id in s.Ladder) w.Str(id);
            }
            if (s.FusionOffered.Count > 0 || s.Slots.Any(x => x != null && x.Absorbed.Count > 0))
            {
                // Fusion state (specs/15 section 5.6), hashed only when a run has any, so every other run keeps its hash.
                w.Str("fusion");
                w.I32(s.FusionOffered.Count);
                foreach (var id in s.FusionOffered) w.Str(id);
                foreach (var ts in s.Slots)
                {
                    w.I32(ts == null ? 0 : ts.Absorbed.Count);
                    if (ts == null) continue;
                    foreach (var a in ts.Absorbed) { w.Str(a.TreeId); w.I32(a.Bought); foreach (var v in a.FlatAdded) w.F64(v); }
                }
            }
            if (talents)
            {
                var m = s.Mods;
                w.F64(m.RestEnergyBonus); w.F64(m.FailChanceDelta); w.F64(m.SuperChanceBonus); w.F64(m.ShardMult); w.I32(m.MomentumDrills);
                w.I32(m.LossSp); w.F64(m.SpecialistBonus); w.I32(m.TreeSlotBonus); w.I32(m.InjuryShields); w.I32(m.LastCycleAp);
                w.F64(m.MilestoneMult); w.I32(m.DraftRerolls);
                w.I32(s.FreeDrills); w.I32(s.ShieldsUsed); w.I32(s.RerollsUsed);
            }
            return Sha256.Hex(Sha256.Compute(w.ToArray()));
        }

        /// <summary>
        /// Whether a run uses any talent of the catalog v2 (specs/18 section 3): those fields are hashed (version 7) only then. The
        /// counters and the RerollDraft command need one of them, so a run without them hashes exactly as before.
        /// </summary>
        static bool UsesCatalogV2(RunModifiers m) =>
            m.RestEnergyBonus != 0 || m.FailChanceDelta != 0 || m.SuperChanceBonus != 0 || m.ShardMult != 1 || m.MomentumDrills != 0
            || m.LossSp != 0 || m.SpecialistBonus != 0 || m.TreeSlotBonus != 0 || m.InjuryShields != 0 || m.LastCycleAp != 0
            || m.MilestoneMult != 1 || m.DraftRerolls != 0;

        static void Stream(CanonicalWriter w, RngStream r) { w.Str(r.Name); w.U64(r.Seed); w.U64(r.Counter); }
    }
}
