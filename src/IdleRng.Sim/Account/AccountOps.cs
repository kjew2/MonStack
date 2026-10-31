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
        public static RunState StartRun(ContentDb c, AccountState a, string speciesId, IList<string> books = null)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (a.Active != null) throw new InvalidOperationException("a run is already in progress");
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

            var run = TrainingRun.Start(c, speciesId, RunSeed(a.Seed, a.RunsStarted), books, Modifiers(c, a));
            foreach (var kv in need) AddBooks(a, kv.Key, -kv.Value);
            a.RunsStarted++;
            a.Active = run;
            a.Vendor = null;   // unbought offers from the last run are gone (specs/18 section 4)
            return run;
        }

        /// <summary>
        /// Finish the active run once it is over (completed or injured): pay shards (<see cref="Payout"/>), return unused books,
        /// seal the monster into the Vault (Vault.Seal, specs/13 T4), record it and check achievements (which may unlock species),
        /// roll the post-run vendor's offers (<see cref="RollVendor"/>) and clear the active run. It seals even when the Vault is
        /// at or over VAULT_CAP (no monster is lost); the next StartRun waits until the player dismantles down to the cap.
        /// </summary>
        public static FinishResult FinishRun(ContentDb c, AccountState a, long sealedUtcMs = 0)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            var run = a.Active ?? throw new InvalidOperationException("no run in progress");
            if (run.Phase != RunPhase.Finished) throw new InvalidOperationException("the run is not over yet");

            var result = new FinishResult { Shards = Payout(c, run), Entry = Vault.Seal(c, run, sealedUtcMs) };
            a.Shards += result.Shards;
            foreach (var b in run.Books) { AddBooks(a, b, 1); result.BooksReturned.Add(b); }
            a.Vault.Add(result.Entry);
            a.Active = null;
            Record(a, result.Entry);
            var ownedBefore = new HashSet<string>(a.OwnedSpecies, StringComparer.Ordinal);
            foreach (var d in CheckAchievements(c, a))
            {
                result.Achievements.Add(d.Id);
                if (d.UnlocksSpecies != null && ownedBefore.Add(d.UnlocksSpecies)) result.SpeciesUnlocked.Add(d.UnlocksSpecies);
            }
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

        /// <summary>Training Shards for a finished run: SHARDS_PER_CYCLE per cycle played plus SHARDS_PER_RANK per rank reached.</summary>
        public static long Payout(ContentDb c, RunState run) =>
            (long)c.Constants.ShardsPerCycle * run.Cycle + (long)c.Constants.ShardsPerRank * run.Rank;

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
        /// refund (<see cref="DismantleRefund"/>). Locked entries are refused. Later entries move down one index. Returns the refund.
        /// </summary>
        public static long Dismantle(ContentDb c, AccountState a, int index)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            var e = VaultAt(a, index);
            if (e.Locked) throw new InvalidOperationException("that monster is locked: unlock it to dismantle it");
            long refund = DismantleRefund(c, e);
            a.Vault.RemoveAt(index);
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
        /// Roll VENDOR_OFFERS offers for a run that reached grade index <paramref name="gradeIndex"/>. A rarity is a talent's own
        /// tier. Each offer draws a tier by weight (<see cref="VendorRarityDef.Weight"/> at that grade) among the tiers that still
        /// have a talent not maxed, then a talent of that tier uniformly; the same talent may be offered twice. When those tiers all
        /// weigh 0 at that grade, the commonest of them is used. No offers once every talent is maxed. Two draws per offer, always.
        /// Deterministic from the account seed and the run index.
        /// </summary>
        public static VendorState RollVendor(ContentDb c, AccountState a, int gradeIndex, int runIndex)
        {
            int g = Math.Max(0, Math.Min(gradeIndex, c.FightRanks.Count - 1));
            var v = new VendorState { Grade = c.FightRanks[g].Id };
            var tiers = new List<(VendorRarityDef Rarity, List<TalentDef> Talents)>();
            foreach (var r in c.VendorRarities)
            {
                var open = new List<TalentDef>();
                foreach (var t in c.TalentList) if (t.Rarity == r.Id && a.TalentRank(t.Id) < t.MaxRank) open.Add(t);
                if (open.Count > 0) tiers.Add((r, open));
            }
            if (tiers.Count == 0) return v;
            double total = 0;
            foreach (var x in tiers) total += x.Rarity.Weight(g);
            var rng = new RngStream(VendorStream, VendorSeed(a.Seed, runIndex));
            for (int i = 0; i < c.Constants.VendorOffers; i++)
            {
                double u = rng.NextDouble() * total;
                var tier = tiers[0];
                foreach (var x in tiers)
                {
                    double w = x.Rarity.Weight(g);
                    if (w <= 0) continue;
                    tier = x;              // the last tier with weight absorbs any rounding residue
                    if (u < w) break;
                    u -= w;
                }
                var talent = tier.Talents[rng.NextIndex(tier.Talents.Count)];
                v.Offers.Add(new VendorOffer { Talent = talent.Id, Rarity = tier.Rarity.Id });
            }
            return v;
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

        /// <summary>The run bonuses the account's talents give (specs/18 section 4).</summary>
        public static RunModifiers Modifiers(ContentDb c, AccountState a)
        {
            var m = new RunModifiers();
            foreach (var t in c.TalentList)
            {
                int rank = a.TalentRank(t.Id);
                if (rank == 0) continue;
                double v = t.PerRank * rank;
                switch (t.Effect)
                {
                    case TalentEffect.DrillCost: m.DrillCostMult = Math.Max(0, m.DrillCostMult + v); break;
                    case TalentEffect.StatFloor: m.StatFloorMult += v; break;
                    case TalentEffect.DraftSize: m.DraftSizeBonus += (int)v; break;
                    case TalentEffect.StartSp: m.StartSp += (int)v; break;
                }
            }
            return m;
        }

        // ------------------------------------------------------------------ books

        /// <summary>Add (or with a negative count remove) skill books. The Daily Tower will grant books through this (specs/13 T10).</summary>
        // ------------------------------------------------------------------ Daily Tower (specs/21 section 7)

        /// <summary>Attempts left on <paramref name="day"/> (all of them on a day after the last attempt).</summary>
        public static int TowerAttemptsLeft(ContentDb c, AccountState a, int day)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            var t = a.Tower ?? new TowerProgress();
            int used = day > t.Day ? 0 : t.AttemptsUsed;
            return Math.Max(0, c.Constants.TowerAttemptsPerDay - used);
        }

        /// <summary>
        /// One Daily Tower attempt on <paramref name="day"/> (the caller's UTC day number) with the Vault entries at
        /// <paramref name="vaultIndices"/> (oldest-first indices, in slot order, 1 to MON_SIDE_SIZE distinct). A run in progress
        /// does not block it. Pays, for each floor cleared above today's best: TOWER_SHARDS_PER_FLOOR shards and that floor's
        /// Skill Book drop, if any (TowerSim.BookDrop, fixed by day and floor). Rejected (throws, changes nothing) with no
        /// attempts left, a bad team, or a day before the last attempt (the clock went back).
        /// </summary>
        public static TowerRunResult RunTower(ContentDb c, AccountState a, IList<int> vaultIndices, int day)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (vaultIndices == null) throw new ArgumentNullException(nameof(vaultIndices));
            if (day < 0) throw new ArgumentOutOfRangeException(nameof(day), "day must be >= 0");
            var t = a.Tower ?? new TowerProgress();
            if (day < t.Day) throw new InvalidOperationException($"day {day} is before the last tower attempt (day {t.Day})");
            if (TowerAttemptsLeft(c, a, day) <= 0) throw new InvalidOperationException("no tower attempts left today");
            if (vaultIndices.Count < 1 || vaultIndices.Count > c.Constants.MonSideSize)
                throw new ArgumentException($"a tower team needs 1..{c.Constants.MonSideSize} monsters", nameof(vaultIndices));
            var seen = new HashSet<int>();
            var team = new List<Fighter>(vaultIndices.Count);
            foreach (int i in vaultIndices)
            {
                if (i < 0 || i >= a.Vault.Count) throw new ArgumentOutOfRangeException(nameof(vaultIndices), $"no Vault entry {i}");
                if (!seen.Add(i)) throw new ArgumentException($"Vault entry {i} is on the team twice", nameof(vaultIndices));
                team.Add(a.Vault[i].Fighter(c));
            }

            var result = IdleRng.Sim.Tower.TowerSim.Run(c, team, day);
            bool newDay = day > t.Day;
            int paidUpTo = newDay ? 0 : t.ClearedToday;
            var r = new TowerRunResult { Result = result };
            for (int f = paidUpTo + 1; f <= result.FloorsCleared; f++)
            {
                r.Shards += c.Constants.TowerShardsPerFloor;
                var book = IdleRng.Sim.Tower.TowerSim.BookDrop(c, day, f);
                if (book != null) r.Books.Add((f, book));
                if (f > t.ClearedEver) r.FirstClears.Add(f);
            }

            // commit
            a.Tower = t;
            if (newDay) { t.Day = day; t.AttemptsUsed = 0; t.ClearedToday = 0; }
            t.AttemptsUsed++;
            t.ClearedToday = Math.Max(t.ClearedToday, result.FloorsCleared);
            t.ClearedEver = Math.Max(t.ClearedEver, result.FloorsCleared);
            a.Shards += r.Shards;
            foreach (var (_, book) in r.Books) AddBooks(a, book, 1);
            r.AttemptsLeft = TowerAttemptsLeft(c, a, day);
            return r;
        }

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
