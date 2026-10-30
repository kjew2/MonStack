using System;
using System.Collections.Generic;
using IdleRng.Sim;
using IdleRng.Sim.Gacha;
using IdleRng.Sim.State;

namespace IdleRng.Server
{
    /// <summary>The server ledger for one profile (specs/10 section 8). Authoritative; the client's Currencies mirror it.</summary>
    public sealed record ProfileLedger(long Gems, int FreePullsDaily, int FreePullsBanked, long Shards)
    {
        public static readonly ProfileLedger Empty = new ProfileLedger(0, 0, 0, 0);

        internal Currencies ToCurrencies() =>
            new Currencies { Gems = Gems, FreePullsDaily = FreePullsDaily, FreePullsBanked = FreePullsBanked, Shards = Shards };

        internal static ProfileLedger From(Currencies c) => new ProfileLedger(c.Gems, c.FreePullsDaily, c.FreePullsBanked, c.Shards);
    }

    /// <summary>Pity and lifetime pulls on the hero banner (specs/01 section 3).</summary>
    public sealed record BannerRecord(int PityCounter, long TotalPulls)
    {
        public static readonly BannerRecord Fresh = new BannerRecord(0, 0);
    }

    /// <summary>The server-held gacha_hero stream (specs/08 section 4.3). The seed never leaves the server.</summary>
    public sealed record GachaRngRecord(ulong Seed, ulong Counter);

    /// <summary>Response stored under an Idempotency-Key; a replay returns it unchanged (specs/08 section 4.3 rule 2).</summary>
    public sealed record IdempotencyRecord(string Key, long CreatedAtSec, PullResponse Response);

    /// <summary>Everything the pull handler reads and writes for one profile. Immutable; handlers return a new one.</summary>
    public sealed record GachaProfile(
        ProfileLedger Ledger,
        BannerRecord Banner,
        GachaRngRecord Rng,
        IReadOnlyDictionary<string, int> Copies,
        IReadOnlyDictionary<string, IdempotencyRecord> Idempotency)
    {
        public static GachaProfile New(ulong gachaSeed, ProfileLedger ledger) =>
            new GachaProfile(ledger, BannerRecord.Fresh, new GachaRngRecord(gachaSeed, 0),
                new Dictionary<string, int>(StringComparer.Ordinal),
                new Dictionary<string, IdempotencyRecord>(StringComparer.Ordinal));
    }

    /// <summary>POST /v1/gacha/pull body (specs/08 section 4.3). Payment is what the client's QuotePayment said.</summary>
    public sealed record PullRequest(string BannerId, int Count, PaidWith Payment, string IdempotencyKey);

    public enum PullError { None, BadBanner, BadCount, BadKey, PaymentMismatch, CannotAfford }

    public sealed record PullResponse(PullError Error, PullResult[] Results, BannerRecord Banner, ProfileLedger Balances, long ServerTimeSec)
    {
        public bool Ok => Error == PullError.None;

        /// <summary>The HTTP status of specs/08 section 4.3.</summary>
        public int HttpStatus => Error switch
        {
            PullError.None => 200,
            PullError.CannotAfford => 402,
            PullError.PaymentMismatch => 409,
            _ => 400,
        };
    }

    /// <summary>Replayed: the key was already stored, so nothing was rolled or charged.</summary>
    public sealed record PullHandled(GachaProfile State, PullResponse Response, bool Replayed);
}
