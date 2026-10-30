using System;
using System.Collections.Generic;
using IdleRng.Sim;
using IdleRng.Sim.Content;
using IdleRng.Sim.Gacha;
using IdleRng.Sim.Rng;
using IdleRng.Sim.State;

namespace IdleRng.Server
{
    /// <summary>
    /// Server gacha handlers (specs/08 section 4.3). Pure: (content, profile, request, now) in, (new profile, response)
    /// out; the input profile is never mutated. The backend supplies storage, the per-profile mutex (rule 1) and the
    /// single transaction that persists the returned profile (rule 3).
    /// </summary>
    public static class GachaHandlers
    {
        /// <summary>IDEMPOTENCY_TTL_H = 48.</summary>
        public const long IdempotencyTtlSec = 48 * 3600;

        public static PullHandled Pull(ContentDb content, GachaProfile profile, PullRequest req, long nowSec)
        {
            // Rule 2: a live key returns the stored response, with no roll and no debit.
            if (!string.IsNullOrEmpty(req.IdempotencyKey) &&
                profile.Idempotency.TryGetValue(req.IdempotencyKey, out var hit) && nowSec - hit.CreatedAtSec < IdempotencyTtlSec)
                return new PullHandled(profile, hit.Response, Replayed: true);

            if (string.IsNullOrEmpty(req.IdempotencyKey)) return Reject(profile, PullError.BadKey, nowSec);
            if (req.BannerId == null || !content.Banners.TryGetValue(req.BannerId, out var banner) || !banner.Enabled)
                return Reject(profile, PullError.BadBanner, nowSec);
            if (req.Count != 1 && req.Count != banner.GuaranteeBatchSize) return Reject(profile, PullError.BadCount, nowSec);

            // Rule 3: PayForPulls against the server ledger, then Pull/Pull10 on the server's (seed, counter).
            var cur = profile.Ledger.ToCurrencies();
            var quote = PullPayment.Quote(banner, cur, req.Count);
            if (quote == PaidWith.None) return Reject(profile, PullError.CannotAfford, nowSec);
            if (quote != req.Payment) return Reject(profile, PullError.PaymentMismatch, nowSec);
            PullPayment.PayForPulls(banner, cur, req.Count, out _);

            var st = new BannerState { PityCounter = profile.Banner.PityCounter, TotalPulls = profile.Banner.TotalPulls };
            var heroes = new HeroState { Copies = new Dictionary<string, int>(profile.Copies, StringComparer.Ordinal) };
            var rng = new RngStream(banner.RngStream, profile.Rng.Seed, profile.Rng.Counter);
            var results = GachaAlgorithm.PullBatch(content, banner, st, heroes, cur, rng, req.Count);

            var response = new PullResponse(PullError.None, results, new BannerRecord(st.PityCounter, st.TotalPulls),
                                            ProfileLedger.From(cur), nowSec);
            var keys = new Dictionary<string, IdempotencyRecord>(StringComparer.Ordinal);
            foreach (var kv in profile.Idempotency)
                if (nowSec - kv.Value.CreatedAtSec < IdempotencyTtlSec) keys[kv.Key] = kv.Value;   // expired keys drop out
            keys[req.IdempotencyKey] = new IdempotencyRecord(req.IdempotencyKey, nowSec, response);

            var next = new GachaProfile(response.Balances, response.Banner, new GachaRngRecord(rng.Seed, rng.Counter), heroes.Copies, keys);
            return new PullHandled(next, response, Replayed: false);
        }

        /// <summary>Rejections change nothing and are not stored: nothing was charged, so the key may be retried.</summary>
        static PullHandled Reject(GachaProfile p, PullError error, long nowSec) =>
            new PullHandled(p, new PullResponse(error, Array.Empty<PullResult>(), p.Banner, p.Ledger, nowSec), Replayed: false);
    }
}
