using System;
using System.Collections.Generic;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using IdleRng.Sim.Training;

namespace IdleRng.Sim.Account
{
    /// <summary>
    /// Account commands (specs/18): start and finish runs, pay Training Shards, achievements and species unlocks, the post-run
    /// vendor that sells talent ranks, skill books. Pure functions
    /// over an AccountState: no clock (the caller passes the time a run is sealed), no IO.
    /// </summary>
    public static class AccountOps
    {
        public const string RunStream = "run";
        public const string VendorStream = "vendor";

        /// <summary>
        /// A new account: owns only the starter species (the first in content; the others unlock from achievements, section 8),
        /// no shards, books, talents or achievements.
        /// </summary>
        public static AccountState New(ContentDb c, ulong seed)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            var a = new AccountState { Seed = seed, ContentVersion = c.Version };
            a.OwnedSpecies.Add(c.StarterSpecies.Id);
            return a;
        }

        // ------------------------------------------------------------------ runs

        /// <summary>Seed of the account's <paramref name="index"/>-th run (0-based).</summary>
        public static ulong RunSeed(ulong accountSeed, int index) =>
            SplitMix64.Derive(StreamNames.StreamSeed(accountSeed, RunStream), (ulong)index);

        /// <summary>
        /// Start a run of an owned species with books taken from the inventory, under the account's talent bonuses
        /// (<see cref="Modifiers"/>). Only one run at a time.
        /// </summary>
        /// <remarks>
        /// <paramref name="talentsOff"/> is the run's talent loadout (issue #106): owned talents switched off for this run. Null
        /// uses the account's last loadout (<see cref="DefaultLoadout"/>). An unknown talent id, or more legendary-slot talents
        /// left on than LEGENDARY_SLOTS, is refused. The loadout used is saved on the account for the next run.
        /// </remarks>
        public static RunState StartRun(ContentDb c, AccountState a, string speciesId, IList<string> books = null, IEnumerable<string> talentsOff = null)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (a.Active != null) throw new InvalidOperationException("a run is already in progress");
            var off = talentsOff == null ? DefaultLoadout(c, a) : CheckLoadout(c, a, talentsOff);
            int over = VaultOver(c, a);
            if (over > 0)
                throw new InvalidOperationException($"the Vault holds {a.Vault.Count} of {c.Constants.VaultCap}: dismantle {over} to start a run");
            if (speciesId == null || !a.OwnedSpecies.Contains(speciesId)) throw new InvalidOperationException($"species '{speciesId}' is not owned");
            books = books ?? new List<string>();
            var need = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var b in books)
            {
                if (b == null) throw new ArgumentException("null book id", nameof(books));
                need.TryGetValue(b, out var n);
                need[b] = n + 1;
            }
            foreach (var kv in need)
                if (a.BookCount(kv.Key) < kv.Value) throw new InvalidOperationException($"not enough '{kv.Key}' books ({a.BookCount(kv.Key)} < {kv.Value})");

            var run = TrainingRun.Start(c, speciesId, RunSeed(a.Seed, a.RunsStarted), books, Modifiers(c, a, off));
            foreach (var kv in need) AddBooks(a, kv.Key, -kv.Value);
            a.TalentsOff = off;
            a.RunsStarted++;
            a.Active = run;
            a.Vendor = null;   // unbought offers from the last run are gone (specs/18 section 4)
            return run;
        }

        /// <summary>
        /// Finish the active run once it is over (after its last cycle): pay shards (<see cref="Payout"/>), return unused books,
        /// seal the monster into the Vault (Vault.Seal, specs/13 T4), record it and check achievements (which may unlock species),
        /// add the grade's Dungeon ticket pieces (<see cref="AddTicketPieces"/>), roll the post-run vendor's offers
        /// (<see cref="RollVendor"/>) and clear the active run. It seals even when the Vault is at or over VAULT_CAP (no monster
        /// is lost); the next StartRun waits until the player dismantles down to the cap.
        /// </summary>
        public static FinishResult FinishRun(ContentDb c, AccountState a, long sealedUtcMs = 0)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            var run = a.Active ?? throw new InvalidOperationException("no run in progress");
            if (run.Phase != RunPhase.Finished) throw new InvalidOperationException("the run is not over yet");

            var entry = Vault.Seal(c, run, sealedUtcMs, Math.Max(0, a.RunsStarted - 1), out var climb);
            var result = new FinishResult { Shards = Payout(c, run), Entry = entry, Gauntlet = climb };
            a.Shards += result.Shards;
            foreach (var b in run.Books) { AddBooks(a, b, 1); result.BooksReturned.Add(b); }
            a.Vault.Add(result.Entry);
            TreeLineages.Award(c, a, run);
            a.Active = null;
            Record(a, result.Entry);
            var ownedBefore = new HashSet<string>(a.OwnedSpecies, StringComparer.Ordinal);
            foreach (var d in CheckAchievements(c, a))
            {
                result.Achievements.Add(d.Id);
                if (d.UnlocksSpecies != null && ownedBefore.Add(d.UnlocksSpecies)) result.SpeciesUnlocked.Add(d.UnlocksSpecies);
            }
            AddTicketPieces(c, a, result.Entry, result);
            a.Vendor = RollVendor(c, a, result.Entry.RankIndex, Math.Max(0, a.RunsStarted - 1));
            return result;
        }

        /// <summary>
        /// Abandon the active run at any phase: no shards, no Vault entry; books brought into the run but not used go back to
        /// the inventory (a misclick costs nothing). RunsStarted stays counted, so the next run gets a new seed. Returns the
        /// books returned.
        /// </summary>
        public static List<string> AbandonRun(ContentDb c, AccountState a)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            var run = a.Active ?? throw new InvalidOperationException("no run in progress");
            var returned = new List<string>(run.Books);
            foreach (var b in returned) AddBooks(a, b, 1);
            a.Active = null;
            return returned;
        }

        /// <summary>
        /// Training Shards for a finished run: SHARDS_PER_CYCLE per cycle played plus SHARDS_PER_RANK per rank reached, times the
        /// run's Scavenger multiplier (rounded down).
        /// </summary>
        public static long Payout(ContentDb c, RunState run)
        {
            long basePay = (long)c.Constants.ShardsPerCycle * run.Cycle + (long)c.Constants.ShardsPerRank * run.Rank;
            double mult = run.Mods.ShardMult;
            return mult == 1 ? basePay : (long)Math.Floor(basePay * mult);
        }

        // ------------------------------------------------------------------ Vault (specs/18 section 7)

        /// <summary>How many entries the Vault holds above VAULT_CAP (0 when at or under it). StartRun is refused while it is above 0.</summary>
        public static int VaultOver(ContentDb c, AccountState a) => Math.Max(0, a.Vault.Count - c.Constants.VaultCap);

        /// <summary>Training Shards dismantling this entry pays: its grade's dismantle_shards (0 for a grade no longer in content).</summary>
        public static long DismantleRefund(ContentDb c, VaultEntry e)
        {
            foreach (var r in c.FightRanks) if (r.Id == e.Grade) return r.DismantleShards;
            return 0;
        }

        /// <summary>Lock or unlock Vault entry <paramref name="index"/> (oldest first). A locked entry cannot be dismantled.</summary>
        public static void SetLocked(ContentDb c, AccountState a, int index, bool locked)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            VaultAt(a, index).Locked = locked;
        }

        /// <summary>
        /// Dismantle Vault entry <paramref name="index"/> (oldest first): the entry is removed for good and pays its grade's
        /// refund (<see cref="DismantleRefund"/>). Its rulebook goes with it. Locked entries are refused. Later entries move down
        /// one index. Returns the refund.
        /// </summary>
        public static long Dismantle(ContentDb c, AccountState a, int index)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            var e = VaultAt(a, index);
            if (e.Locked) throw new InvalidOperationException("that monster is locked: unlock it to dismantle it");
            long refund = DismantleRefund(c, e);
            a.Vault.RemoveAt(index);
            a.Rulebooks?.Remove(e.Id);
            a.Shards += refund;
            return refund;
        }

        static VaultEntry VaultAt(AccountState a, int index)
        {
            if (index < 0 || index >= a.Vault.Count) throw new ArgumentException($"no Vault entry {index}", nameof(index));
            return a.Vault[index];
        }

        // ------------------------------------------------------------------ achievements and species (specs/18 section 8)

        /// <summary>Add a sealed monster to the account's records (runs finished, best rank, floors cleared, best stats).</summary>
        public static void Record(AccountState a, VaultEntry e)
        {
            var r = a.Records ?? (a.Records = new AccountRecords());
            r.RunsFinished++;
            r.BestRank = Math.Max(r.BestRank, e.RankIndex);
            if (e.LegacyTitle != null) r.BestFloorsCleared = Math.Max(r.BestFloorsCleared, Math.Max(0, e.GauntletFloor - 1));
            foreach (var id in StatIds.Trained)
            {
                double v = e.Stats.Get(id);
                if (v > r.BestStat(id)) r.BestStats[id] = v;
            }
        }

        /// <summary>
        /// Records rebuilt from the Vault (each entry one finished run). For saves from before records existed (schema v2):
        /// dismantled monsters are not counted, so the result is a lower bound.
        /// </summary>
        public static void RebuildRecords(AccountState a)
        {
            a.Records = new AccountRecords();
            foreach (var e in a.Vault) Record(a, e);
        }

        /// <summary>Progress towards an achievement: (current, target), current capped at target. Holds when current >= target.</summary>
        public static (double current, double target) AchievementProgress(ContentDb c, AccountState a, AchievementDef d)
        {
            var r = a.Records ?? new AccountRecords();
            double cur, target = d.Amount;
            switch (d.Kind)
            {
                case AchievementKind.RunsFinished: cur = r.RunsFinished; break;
                case AchievementKind.GauntletFloors: cur = r.BestFloorsCleared; break;
                case AchievementKind.EndlessFloor: cur = a.Endless?.ClearedEver ?? 0; break;
                case AchievementKind.StatReached:
                    cur = 0;
                    foreach (var id in StatIds.Trained) if (d.Ref == null || d.Ref == id) cur = Math.Max(cur, r.BestStat(id));
                    break;
                default:   // RankReached: counted in ranks from 1 (E = 1), so a run that reached E counts and none yet is 0
                    target = double.MaxValue;   // a rank no longer in content is never reached
                    for (int i = 0; i < c.FightRanks.Count; i++) if (c.FightRanks[i].Id == d.Ref) target = i + 1;
                    cur = r.BestRank + 1;
                    break;
            }
            return (Math.Min(cur, target), target);
        }

        public static bool AchievementHolds(ContentDb c, AccountState a, AchievementDef d)
        {
            var (cur, target) = AchievementProgress(c, a, d);
            return cur >= target;
        }

        /// <summary>
        /// Earn every achievement whose condition now holds (content order) and unlock the species they name. Returns the newly
        /// earned ones. Called when a run is sealed and when an account loads (so an achievement added to content later is
        /// granted to accounts that already meet it).
        /// </summary>
        public static List<AchievementDef> CheckAchievements(ContentDb c, AccountState a)
        {
            var earned = new List<AchievementDef>();
            if (a.Achievements == null) a.Achievements = new List<string>();
            foreach (var d in c.AchievementList)
            {
                if (a.Achievements.Contains(d.Id) || !AchievementHolds(c, a, d)) continue;
                a.Achievements.Add(d.Id);
                earned.Add(d);
                if (d.UnlocksSpecies != null) UnlockSpecies(c, a, d.UnlocksSpecies);
            }
            return earned;
        }

        /// <summary>Add a species to the owned list, kept in content order. False when it was already owned.</summary>
        public static bool UnlockSpecies(ContentDb c, AccountState a, string speciesId)
        {
            if (speciesId == null || !c.Species.ContainsKey(speciesId)) throw new ArgumentException($"unknown species '{speciesId}'", nameof(speciesId));
            if (a.OwnedSpecies.Contains(speciesId)) return false;
            a.OwnedSpecies.Add(speciesId);
            var order = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < c.SpeciesList.Count; i++) order[c.SpeciesList[i].Id] = i;
            a.OwnedSpecies.Sort((x, y) => (order.TryGetValue(x, out var i) ? i : int.MaxValue).CompareTo(order.TryGetValue(y, out var j) ? j : int.MaxValue));
            return true;
        }

        // ------------------------------------------------------------------ talents and the post-run vendor (specs/18 section 4)

        /// <summary>Shards one rank of a talent costs from <paramref name="rank"/>: rank r + 1 costs cost_base + cost_step x r.</summary>
        public static long TalentCost(TalentDef t, int rank) => t.CostBase + (long)t.CostStep * rank;

        /// <summary>Seed of the vendor rolled after the account's <paramref name="runIndex"/>-th run (0-based).</summary>
        public static ulong VendorSeed(ulong accountSeed, int runIndex) =>
            SplitMix64.Derive(StreamNames.StreamSeed(accountSeed, VendorStream), (ulong)runIndex);

        /// <summary>
        /// Seed of the vendor's <paramref name="reroll"/>-th roll after run <paramref name="runIndex"/>: the roll at seal time
        /// (reroll 0) is <see cref="VendorSeed(ulong, int)"/>; reroll n &gt; 0 derives n from it (issue #104).
        /// </summary>
        public static ulong VendorSeed(ulong accountSeed, int runIndex, int reroll) =>
            reroll == 0 ? VendorSeed(accountSeed, runIndex) : SplitMix64.Derive(VendorSeed(accountSeed, runIndex), (ulong)reroll);

        /// <summary>
        /// Roll VENDOR_OFFERS offers for a run that reached grade index <paramref name="gradeIndex"/>. A rarity is a talent's own
        /// tier. Each offer draws a tier by weight (<see cref="VendorRarityDef.Weight"/> at that grade) among the tiers that still
        /// have a talent not maxed and weigh more than 0 at that grade (<see cref="OpenTiers"/>), then a talent of that tier
        /// uniformly; the same talent may be offered twice. No offers when no such tier is left (every talent maxed, or only tiers
        /// that weigh 0 at that grade, such as Legendary below A). Two draws per offer, always.
        /// Deterministic from the account seed, the run index and the reroll count (0 for the roll at seal time).
        /// </summary>
        public static VendorState RollVendor(ContentDb c, AccountState a, int gradeIndex, int runIndex, int reroll = 0)
        {
            int g = Math.Max(0, Math.Min(gradeIndex, c.FightRanks.Count - 1));
            var v = new VendorState { Grade = c.FightRanks[g].Id };
            var tiers = OpenTiers(c, a, g);
            if (tiers.Count == 0) return v;
            double total = 0;
            foreach (var x in tiers) total += x.Rarity.Weight(g);
            var rng = new RngStream(VendorStream, VendorSeed(a.Seed, runIndex, reroll));
            for (int i = 0; i < c.Constants.VendorOffers; i++)
            {
                double u = rng.NextDouble() * total;
                var tier = tiers[0];
                foreach (var x in tiers)
                {
                    double w = x.Rarity.Weight(g);
                    tier = x;              // the last tier absorbs any rounding residue
                    if (u < w) break;
                    u -= w;
                }
                var talent = tier.Talents[rng.NextIndex(tier.Talents.Count)];
                v.Offers.Add(new VendorOffer { Talent = talent.Id, Rarity = tier.Rarity.Id });
            }
            return v;
        }

        /// <summary>
        /// The tiers the vendor can sell at grade index <paramref name="g"/>, with their talents not maxed: a tier needs a talent
        /// left and a weight above 0 at that grade (kj, issue #103: a tier weighing 0 there, Legendary below A, is never sold).
        /// </summary>
        static List<(VendorRarityDef Rarity, List<TalentDef> Talents)> OpenTiers(ContentDb c, AccountState a, int g)
        {
            var tiers = new List<(VendorRarityDef Rarity, List<TalentDef> Talents)>();
            foreach (var r in c.VendorRarities)
            {
                if (!(r.Weight(g) > 0)) continue;
                var open = new List<TalentDef>();
                foreach (var t in c.TalentList) if (t.Rarity == r.Id && a.TalentRank(t.Id) < t.MaxRank) open.Add(t);
                if (open.Count > 0) tiers.Add((r, open));
            }
            return tiers;
        }

        static int GradeIndex(ContentDb c, string grade)
        {
            for (int i = 0; i < c.FightRanks.Count; i++) if (c.FightRanks[i].Id == grade) return i;
            return 0;
        }

        /// <summary>True when buying this offer now would add a rank: its talent is in content and not maxed.</summary>
        public static bool OfferOpen(ContentDb c, AccountState a, VendorOffer o) =>
            o.Talent != null && c.Talents.TryGetValue(o.Talent, out var t) && a.TalentRank(o.Talent) < t.MaxRank;

        /// <summary>Shards the offer costs now: the talent's normal cost for its next rank. 0 when it cannot be bought.</summary>
        public static long OfferPrice(ContentDb c, AccountState a, VendorOffer o) =>
            OfferOpen(c, a, o) ? TalentCost(c.Talents[o.Talent], a.TalentRank(o.Talent)) : 0;

        /// <summary>
        /// Buy vendor offer <paramref name="index"/>: costs its price (<see cref="OfferPrice"/>) and adds exactly one rank of its
        /// talent (kj, issue #102: never several ranks in one purchase). Each offer sells once. Talents apply from the next run on.
        /// Returns the shards spent.
        /// </summary>
        public static long BuyOffer(ContentDb c, AccountState a, int index)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            var v = a.Vendor ?? throw new InvalidOperationException("the vendor has no offers: finish a run first");
            if (index < 0 || index >= v.Offers.Count) throw new ArgumentException($"no vendor offer {index}", nameof(index));
            var o = v.Offers[index];
            if (o.Bought) throw new InvalidOperationException("that offer is sold");
            if (!OfferOpen(c, a, o)) throw new InvalidOperationException($"{o.Talent} is maxed");
            long price = OfferPrice(c, a, o);
            if (a.Shards < price) throw new InvalidOperationException($"that offer costs {price} shards, have {a.Shards}");
            a.Shards -= price;
            a.Talents[o.Talent] = a.TalentRank(o.Talent) + 1;
            o.Bought = true;
            return price;
        }

        /// <summary>Shards the vendor's next reroll costs: REROLL_BASE + REROLL_STEP x rerolls already made this visit (issue #104).</summary>
        public static long RerollPrice(ContentDb c, VendorState v) =>
            c.Constants.RerollBase + (long)c.Constants.RerollStep * (v?.Rerolls ?? 0);

        /// <summary>
        /// True when <see cref="RerollVendor"/> would change something (the price aside): an offer is unbought and some talent is
        /// not maxed, so the fresh roll has offers.
        /// </summary>
        public static bool CanRerollVendor(ContentDb c, AccountState a)
        {
            var v = a?.Vendor;
            if (v == null) return false;
            bool unbought = false;
            foreach (var o in v.Offers) if (!o.Bought) unbought = true;
            return unbought && OpenTiers(c, a, GradeIndex(c, v.Grade)).Count > 0;
        }

        /// <summary>
        /// Reroll the post-run vendor (kj, 2026-10-31, issue #104): costs <see cref="RerollPrice"/> shards and replaces every
        /// unbought offer with a fresh roll at the same grade odds; bought offers stay. The fresh roll is <see cref="RollVendor"/>
        /// keyed by the sealed run's index and the new reroll count, and unbought offer i takes the roll's offer i, so it is
        /// deterministic. Refused (nothing changes) with no vendor, every offer bought, every talent maxed, or too few shards.
        /// Returns the shards spent.
        /// </summary>
        public static long RerollVendor(ContentDb c, AccountState a)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            var v = a.Vendor ?? throw new InvalidOperationException("the vendor has no offers: finish a run first");
            if (!CanRerollVendor(c, a)) throw new InvalidOperationException("nothing to reroll: every offer is sold, or nothing is left to sell at this grade");
            long price = RerollPrice(c, v);
            if (a.Shards < price) throw new InvalidOperationException($"a reroll costs {price} shards, have {a.Shards}");
            var fresh = RollVendor(c, a, GradeIndex(c, v.Grade), Math.Max(0, a.RunsStarted - 1), v.Rerolls + 1);
            a.Shards -= price;
            v.Rerolls++;
            // Both lists hold VENDOR_OFFERS offers; the bound only matters for a vendor saved under a larger VENDOR_OFFERS.
            for (int i = 0; i < v.Offers.Count && i < fresh.Offers.Count; i++)
                if (!v.Offers[i].Bought) v.Offers[i] = fresh.Offers[i];
            return price;
        }

        /// <summary>
        /// Close the post-run vendor (kj, 2026-11-01): the player left the vendor screen, so its unbought offers are gone. The
        /// vendor exists only between sealing a run and leaving that screen (specs/18 section 4). No vendor: nothing changes.
        /// </summary>
        public static void CloseVendor(ContentDb c, AccountState a)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            a.Vendor = null;
        }

        // ------------------------------------------------------------------ talent loadout (specs/18 section 4, issue #106)

        /// <summary>Whether the talent's tier takes a legendary slot (vendor_rarities.legendary_slot).</summary>
        public static bool TakesLegendarySlot(ContentDb c, TalentDef t)
        {
            foreach (var r in c.VendorRarities) if (r.Id == t.Rarity) return r.LegendarySlot;
            return false;
        }

        /// <summary>
        /// The loadout the next run starts with unless the player changes it: the account's last loadout (<see
        /// cref="AccountState.TalentsOff"/>; a talent bought since is on), with any owned legendary-slot talents beyond
        /// LEGENDARY_SLOTS switched off, the first in content order keeping the slots (agent). Talent ids in content order.
        /// </summary>
        public static List<string> DefaultLoadout(ContentDb c, AccountState a)
        {
            var saved = a.TalentsOff ?? new List<string>();
            var off = new List<string>();
            int used = 0;
            foreach (var t in c.TalentList)
            {
                bool isOff = saved.Contains(t.Id);
                if (!isOff && a.TalentRank(t.Id) > 0 && TakesLegendarySlot(c, t))
                {
                    if (used < c.Constants.LegendarySlots) used++;
                    else isOff = true;
                }
                if (isOff) off.Add(t.Id);
            }
            return off;
        }

        /// <summary>
        /// A loadout the player asked for, in content order without duplicates: refused (throws) for an unknown talent id or when
        /// more owned legendary-slot talents are left on than LEGENDARY_SLOTS. Switching off a talent not owned is allowed and
        /// changes nothing.
        /// </summary>
        public static List<string> CheckLoadout(ContentDb c, AccountState a, IEnumerable<string> talentsOff)
        {
            var asked = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in talentsOff)
            {
                if (id == null || !c.Talents.ContainsKey(id)) throw new ArgumentException($"unknown talent '{id}'", nameof(talentsOff));
                asked.Add(id);
            }
            var off = new List<string>();
            int legendaries = 0;
            foreach (var t in c.TalentList)
            {
                if (asked.Contains(t.Id)) off.Add(t.Id);
                else if (a.TalentRank(t.Id) > 0 && TakesLegendarySlot(c, t)) legendaries++;
            }
            if (legendaries > c.Constants.LegendarySlots)
                throw new InvalidOperationException($"{legendaries} legendary talents are on, but a run has {c.Constants.LegendarySlots} legendary slot{(c.Constants.LegendarySlots == 1 ? "" : "s")}");
            return off;
        }

        /// <summary>
        /// Save the loadout the next run starts with (the species pick's talent toggles): checked as <see cref="CheckLoadout"/>
        /// does. Free, allowed at any time; a run in progress keeps the bonuses it started with.
        /// </summary>
        public static void SetLoadout(ContentDb c, AccountState a, IEnumerable<string> talentsOff)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (talentsOff == null) throw new ArgumentNullException(nameof(talentsOff));
            a.TalentsOff = CheckLoadout(c, a, talentsOff);
        }

        /// <summary>Lowest milestone threshold multiplier talents can sum to (agent: keeps a stack of Early Bloom talents sane).</summary>
        public const double MinMilestoneMult = 0.1;

        /// <summary>
        /// The run bonuses the account's talents give (specs/18 sections 3 and 4): each talent adds per_rank x rank to its effect's
        /// field. Sums of several talents are clamped to what a run accepts (multipliers at or above 0, Specialist at most 2,
        /// Early Bloom's threshold multiplier at least MinMilestoneMult).
        /// </summary>

        public static RunModifiers Modifiers(ContentDb c, AccountState a) => Modifiers(c, a, DefaultLoadout(c, a));

        /// <summary>The run bonuses of the talents left on by the loadout <paramref name="talentsOff"/> (issue #106).</summary>
        public static RunModifiers Modifiers(ContentDb c, AccountState a, ICollection<string> talentsOff)
        {
            var m = new RunModifiers();
            double costMult = 1;
            foreach (var t in c.TalentList)
            {
                int rank = a.TalentRank(t.Id);
                if (rank == 0 || (talentsOff != null && talentsOff.Contains(t.Id))) continue;
                double v = t.PerRank * rank;
                switch (t.Effect)
                {
                    case TalentEffect.DrillCost: m.DrillCostMult = Math.Max(0, m.DrillCostMult + v); break;
                    case TalentEffect.StatFloor: m.StatFloorMult += v; break;
                    case TalentEffect.StartSp: m.StartSp += (int)v; break;
                    case TalentEffect.RestEnergy: m.RestEnergyBonus += v; break;
                    case TalentEffect.FailChance: m.FailChanceDelta += v; break;
                    case TalentEffect.SuperChance: m.SuperChanceBonus += v; break;
                    case TalentEffect.ShardMult: m.ShardMult += v; break;
                    case TalentEffect.Momentum: m.MomentumDrills += (int)v; break;
                    case TalentEffect.LossSp: m.LossSp += (int)v; break;
                    case TalentEffect.Specialist: m.SpecialistBonus = Math.Min(2, m.SpecialistBonus + v); break;
                    case TalentEffect.TreeSlots: m.TreeSlotBonus += (int)v; break;
                    case TalentEffect.InjuryShield: m.InjuryShields += (int)v; break;
                    case TalentEffect.LastCycleAp: m.LastCycleAp += (int)v; break;
                    case TalentEffect.MilestoneMult: m.MilestoneMult = Math.Max(MinMilestoneMult, m.MilestoneMult + v); break;
                    case TalentEffect.DraftRerolls: m.DraftRerolls += (int)v; break;
                    // Legendary rule changes (issue #103): one rank each, so v is per_rank.
                    case TalentEffect.IronConstitution: m.InjuryImmune = true; m.RestEnergyMult = Math.Min(m.RestEnergyMult, v); break;
                    case TalentEffect.GamblersBlood: m.NeverFail = true; costMult *= v; break;
                    case TalentEffect.GlassCannon: m.GlassCannonBonus += v; break;
                    case TalentEffect.EchoTraining: m.EchoFrac = Math.Min(1, m.EchoFrac + v); break;
                }
            }
            m.DrillCostMult *= costMult;   // Gambler's Blood multiplies the summed Frugal Drills multiplier
            return m;
        }

        // ------------------------------------------------------------------ Dungeon (specs/21 section 7; "tower" in code)

        /// <summary>
        /// Free tickets due on <paramref name="day"/> and not yet granted: TOWER_ATTEMPTS_PER_DAY for each day since the last
        /// grant (one day's worth on the first), filling the stock only up to TOWER_TICKET_CAP. Tickets from runs count toward
        /// the cap but are never cut by it. 0 on a day at or before the last grant.
        /// </summary>
        static int TicketsDue(ContentDb c, TowerProgress t, int day)
        {
            if (day <= t.GrantDay) return 0;
            long days = t.GrantDay < 0 ? 1 : (long)day - t.GrantDay;
            long due = Math.Min(days * c.Constants.TowerAttemptsPerDay, (long)c.Constants.TowerTicketCap - t.Tickets);
            return (int)Math.Max(0, due);
        }

        /// <summary>Tickets the player can spend on <paramref name="day"/>: those held plus the free ones due (read only).</summary>
        public static int TowerTickets(ContentDb c, AccountState a, int day)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            var t = a.Tower ?? new TowerProgress();
            return t.Tickets + TicketsDue(c, t, day);
        }

        /// <summary>
        /// One Dungeon attempt on <paramref name="day"/> (the caller's UTC day number) with the Vault entries at
        /// <paramref name="vaultIndices"/> (oldest-first indices, in slot order, 1 to MON_SIDE_SIZE distinct). Grants the free
        /// tickets due, then spends one. A run in progress does not block it. Pays every floor cleared, on every attempt:
        /// TOWER_SHARDS_PER_FLOOR shards and the floor's Skill Book roll (TowerSim.BookDrop, fixed by day, the day's attempt
        /// number and floor, so each ticket rolls afresh). Rejected (throws, changes nothing) with no ticket, a bad team, or a
        /// day before the last attempt (the clock went back).
        /// </summary>
        public static TowerRunResult RunTower(ContentDb c, AccountState a, IList<int> vaultIndices, int day)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (vaultIndices == null) throw new ArgumentNullException(nameof(vaultIndices));
            if (day < 0) throw new ArgumentOutOfRangeException(nameof(day), "day must be >= 0");
            var t = a.Tower ?? new TowerProgress();
            if (day < t.Day) throw new InvalidOperationException($"day {day} is before the last Dungeon attempt (day {t.Day})");
            if (TowerTickets(c, a, day) <= 0) throw new InvalidOperationException("no Dungeon tickets left");
            if (vaultIndices.Count < 1 || vaultIndices.Count > c.Constants.MonSideSize)
                throw new ArgumentException($"a Dungeon team needs 1..{c.Constants.MonSideSize} monsters", nameof(vaultIndices));
            var seen = new HashSet<int>();
            var team = new List<Fighter>(vaultIndices.Count);
            foreach (int i in vaultIndices)
            {
                if (i < 0 || i >= a.Vault.Count) throw new ArgumentOutOfRangeException(nameof(vaultIndices), $"no Vault entry {i}");
                if (!seen.Add(i)) throw new ArgumentException($"Vault entry {i} is on the team twice", nameof(vaultIndices));
                team.Add(TeamFighter(c, a, i));
            }

            var result = IdleRng.Sim.Tower.TowerSim.Run(c, team, day);
            bool newDay = day > t.Day;
            int attempt = newDay ? 0 : t.AttemptsUsed;
            var r = new TowerRunResult { Result = result };
            for (int f = 1; f <= result.FloorsCleared; f++)
            {
                r.Shards += c.Constants.TowerShardsPerFloor;
                var book = IdleRng.Sim.Tower.TowerSim.BookDrop(c, day, f, attempt);
                if (book != null) r.Books.Add((f, book));
                if (f > t.ClearedEver) r.FirstClears.Add(f);
            }

            // commit
            a.Tower = t;
            t.Tickets += TicketsDue(c, t, day);
            t.GrantDay = Math.Max(t.GrantDay, day);
            t.Tickets--;
            if (newDay) { t.Day = day; t.AttemptsUsed = 0; t.ClearedToday = 0; }
            t.AttemptsUsed++;
            t.ClearedToday = Math.Max(t.ClearedToday, result.FloorsCleared);
            t.ClearedEver = Math.Max(t.ClearedEver, result.FloorsCleared);
            a.Shards += r.Shards;
            foreach (var (_, book) in r.Books) AddBooks(a, book, 1);
            r.TicketsLeft = TowerTickets(c, a, day);
            return r;
        }

        // ------------------------------------------------------------------ rulebooks (specs/28 section 4.4)

        /// <summary>
        /// Vault entry <paramref name="index"/> (oldest first) as a team fighter: the entry's fighter with its rulebook. Every team
        /// mode (the Dungeon, the Endless Tower, later the PvP defense) builds its team through this. A rulebook stored under
        /// older content is used as far as it still fits (agent): text that no longer parses is ignored, a rule whose action is
        /// no longer one of the monster's active skills is skipped, and only the first GAMBIT_SLOTS rules count, so a content
        /// change never stops a monster from fighting. Editing the rulebook stores it clean again.
        /// </summary>
        public static Fighter TeamFighter(ContentDb c, AccountState a, int index)
        {
            var e = VaultAt(a, index);
            var f = e.Fighter(c);
            string book = a.Rulebook(e.Id);
            if (book.Length == 0) return f;
            List<GambitRule> rules;
            try { rules = GambitText.Parse(book); }
            catch (FormatException) { return f; }
            foreach (var g in rules)
                if (f.Gambits.Count < c.Constants.GambitSlots && (g.Skill == GambitRule.Basic || HasActiveSkill(f, g.Skill))) f.Gambits.Add(g);
            return f;
        }

        static bool HasActiveSkill(Fighter f, string id)
        {
            foreach (var sk in f.Skills) if (sk.Id == id && sk.Kind != SkillKind.Passive) return true;
            return false;
        }

        /// <summary>
        /// The rulebook <paramref name="text"/> checked for Vault entry <paramref name="e"/>, in its canonical text (GambitText.Format):
        /// it parses, holds at most GAMBIT_SLOTS rules, and every action is "basic" or one of the monster's active skills. Throws
        /// ArgumentException naming the problem. Null or empty text is an empty rulebook ("").
        /// </summary>
        public static string CheckRulebook(ContentDb c, VaultEntry e, string text)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            List<GambitRule> rules;
            try { rules = GambitText.Parse(text); }
            catch (FormatException x) { throw new ArgumentException(x.Message, nameof(text), x); }
            if (rules.Count > c.Constants.GambitSlots)
                throw new ArgumentException($"a rulebook holds at most {c.Constants.GambitSlots} rules, got {rules.Count}", nameof(text));
            var fighter = e.Fighter(c);
            foreach (var g in rules)
                if (g.Skill != GambitRule.Basic && !HasActiveSkill(fighter, g.Skill))
                    throw new ArgumentException($"'{g.Skill}' is not one of this monster's active skills", nameof(text));
            return GambitText.Format(rules);
        }

        /// <summary>
        /// Set (or with empty text clear) the rulebook of Vault entry <paramref name="index"/> (oldest first), checked as
        /// <see cref="CheckRulebook"/> does. Free, at any time, also during a run; the entry itself does not change.
        /// </summary>
        public static void SetRulebook(ContentDb c, AccountState a, int index, string text)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            var e = VaultAt(a, index);
            if (e.Id < 0) throw new InvalidOperationException("this monster has no id: it cannot hold a rulebook");
            string book = CheckRulebook(c, e, text);
            if (a.Rulebooks == null) a.Rulebooks = new SortedDictionary<int, string>();
            if (book.Length == 0) a.Rulebooks.Remove(e.Id);
            else a.Rulebooks[e.Id] = book;
        }

        /// <summary>
        /// Set (or with empty text clear) the player's display name, checked and normalised by <see cref="DisplayName.Check"/>
        /// (throws ArgumentException with the reason). Free, at any time.
        /// </summary>
        public static void SetName(AccountState a, string name)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            a.Name = DisplayName.Check(name);
        }

        // ------------------------------------------------------------------ Endless Tower (specs/28 sections 5 and 6)

        /// <summary>
        /// Training Shards for clearing Endless floor <paramref name="floor"/> for the first time in a season:
        /// floor(ENDLESS_SHARDS_BASE + floor x ENDLESS_SHARDS_PER_FLOOR), times ENDLESS_ELITE_SHARD_MULT on an elite floor.
        /// </summary>
        public static long EndlessFloorShards(ContentDb c, int floor)
        {
            var k = c.Constants;
            long pay = (long)Math.Floor(k.EndlessShardsBase + floor * k.EndlessShardsPerFloor);
            return IdleRng.Sim.Tower.EndlessSim.IsElite(c, floor) ? pay * k.EndlessEliteShardMult : pay;
        }

        /// <summary>
        /// One Endless Tower climb in <paramref name="season"/> (the caller's UTC week, EndlessSim.SeasonOf) with the Vault
        /// entries at <paramref name="vaultIndices"/> (oldest-first indices, in slot order, exactly MON_SIDE_SIZE distinct), each
        /// with its rulebook. Free and unlimited (kj, 2026-11-02). Pays <see cref="EndlessFloorShards"/> for each floor above the
        /// season's best (each floor once a season), records the bests and checks the achievements. A run in progress does not
        /// block it. Rejected (throws, changes nothing) for a bad team or a season before the last climb's (the clock went back).
        /// </summary>
        public static EndlessRunResult RunEndless(ContentDb c, AccountState a, IList<int> vaultIndices, int season)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (vaultIndices == null) throw new ArgumentNullException(nameof(vaultIndices));
            if (season < 0) throw new ArgumentOutOfRangeException(nameof(season), "season must be >= 0");
            var p = a.Endless ?? new EndlessProgress();
            if (season < p.Season) throw new InvalidOperationException($"season {season} is before the last Endless Tower climb (season {p.Season})");
            int size = c.Constants.MonSideSize;
            if (vaultIndices.Count != size) throw new ArgumentException($"an Endless Tower team needs exactly {size} monsters", nameof(vaultIndices));
            var seen = new HashSet<int>();
            var team = new List<Fighter>(size);
            foreach (int i in vaultIndices)
            {
                if (i < 0 || i >= a.Vault.Count) throw new ArgumentOutOfRangeException(nameof(vaultIndices), $"no Vault entry {i}");
                if (!seen.Add(i)) throw new ArgumentException($"Vault entry {i} is on the team twice", nameof(vaultIndices));
                team.Add(TeamFighter(c, a, i));
            }

            var result = IdleRng.Sim.Tower.EndlessSim.Run(c, team, season);
            bool newSeason = season > p.Season;
            int paid = newSeason ? 0 : p.ClearedSeason;
            var r = new EndlessRunResult { Result = result };
            for (int f = paid + 1; f <= result.FloorsCleared; f++)
            {
                r.NewFloors.Add(f);
                r.Shards += EndlessFloorShards(c, f);
                if (f > p.ClearedEver) r.FirstClears.Add(f);
            }

            // commit
            a.Endless = p;
            if (newSeason) { p.Season = season; p.Climbs = 0; p.ClearedSeason = 0; }
            p.Climbs++;
            p.ClearedSeason = Math.Max(p.ClearedSeason, result.FloorsCleared);
            p.ClearedEver = Math.Max(p.ClearedEver, result.FloorsCleared);
            a.Shards += r.Shards;
            var ownedBefore = new HashSet<string>(a.OwnedSpecies, StringComparer.Ordinal);
            foreach (var d in CheckAchievements(c, a))
            {
                r.Achievements.Add(d.Id);
                if (d.UnlocksSpecies != null && ownedBefore.Add(d.UnlocksSpecies)) r.SpeciesUnlocked.Add(d.UnlocksSpecies);
            }
            return r;
        }

        /// <summary>
        /// Add a sealed run's ticket pieces (its grade's fight_ranks.ticket_pieces; 0 for a grade no longer in content) and turn
        /// every TOWER_TICKET_PIECES of them into a ticket. Tickets from runs are not capped.
        /// </summary>
        static void AddTicketPieces(ContentDb c, AccountState a, VaultEntry e, FinishResult result)
        {
            int pieces = 0;
            foreach (var r in c.FightRanks) if (r.Id == e.Grade) pieces = r.TicketPieces;
            var t = a.Tower ?? (a.Tower = new TowerProgress());
            int per = c.Constants.TowerTicketPieces;
            int total = t.TicketPieces + pieces;
            result.TicketPieces = pieces;
            result.TicketsEarned = total / per;
            t.Tickets += total / per;
            t.TicketPieces = total % per;
        }

        // ------------------------------------------------------------------ books

        /// <summary>Add (or with a negative count remove) skill books. The Dungeon grants its drops through AddBooks.</summary>
        public static void GrantBooks(ContentDb c, AccountState a, string bookId, int count)
        {
            if (bookId == null || !c.SkillBooks.ContainsKey(bookId)) throw new ArgumentException($"unknown skill book '{bookId}'", nameof(bookId));
            if (a.BookCount(bookId) + count < 0) throw new InvalidOperationException($"not enough '{bookId}' books");
            AddBooks(a, bookId, count);
        }

        static void AddBooks(AccountState a, string id, int delta)
        {
            int n = a.BookCount(id) + delta;
            if (n == 0) a.Books.Remove(id);
            else a.Books[id] = n;
        }
    }
}
