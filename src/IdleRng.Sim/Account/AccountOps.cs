using System;
using System.Collections.Generic;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;
using IdleRng.Sim.Training;

namespace IdleRng.Sim.Account
{
    /// <summary>
    /// Account commands (specs/18): start and finish runs, pay Training Shards, buy talents, hold skill books. Pure functions
    /// over an AccountState: no clock (the caller passes the time a run is sealed), no IO.
    /// </summary>
    public static class AccountOps
    {
        public const string RunStream = "run";

        /// <summary>A new account: every species owned (until the gacha), no shards, books or talents.</summary>
        public static AccountState New(ContentDb c, ulong seed)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            var a = new AccountState { Seed = seed, ContentVersion = c.Version };
            foreach (var sp in c.SpeciesList) a.OwnedSpecies.Add(sp.Id);
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
            return run;
        }

        /// <summary>
        /// Finish the active run once it is over (completed or injured): pay shards (<see cref="Payout"/>), return unused books,
        /// seal the monster into the Vault (Vault.Seal, specs/13 T4) and clear the active run.
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
            return result;
        }

        /// <summary>Training Shards for a finished run: SHARDS_PER_CYCLE per cycle played plus SHARDS_PER_RANK per rank reached.</summary>
        public static long Payout(ContentDb c, RunState run) =>
            (long)c.Constants.ShardsPerCycle * run.Cycle + (long)c.Constants.ShardsPerRank * run.Rank;

        // ------------------------------------------------------------------ talents

        /// <summary>Shards to buy the next rank of a talent from <paramref name="rank"/>.</summary>
        public static long TalentCost(TalentDef t, int rank) => t.CostBase + (long)t.CostStep * rank;

        /// <summary>Buy one rank of a talent. Talents apply from the next run on (a run keeps the bonuses it started with).</summary>
        public static long BuyTalent(ContentDb c, AccountState a, string talentId)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (talentId == null || !c.Talents.TryGetValue(talentId, out var t)) throw new ArgumentException($"unknown talent '{talentId}'", nameof(talentId));
            int rank = a.TalentRank(talentId);
            if (rank >= t.MaxRank) throw new InvalidOperationException($"{talentId} is maxed");
            long cost = TalentCost(t, rank);
            if (a.Shards < cost) throw new InvalidOperationException($"{talentId} rank {rank + 1} costs {cost} shards, have {a.Shards}");
            a.Shards -= cost;
            a.Talents[talentId] = rank + 1;
            return cost;
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
