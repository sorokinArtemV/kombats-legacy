namespace Kombats.Chat.Application.Ports;

internal interface IRateLimiter
{
    /// <summary>
    /// Checks the rate limit and increments the counter.
    /// Returns (allowed, retryAfterMs) — retryAfterMs is set when rejected.
    /// </summary>
    Task<RateLimitResult> CheckAndIncrementAsync(Guid identityId, string surface, CancellationToken ct);
}

internal sealed record RateLimitResult(bool Allowed, long? RetryAfterMs = null);
