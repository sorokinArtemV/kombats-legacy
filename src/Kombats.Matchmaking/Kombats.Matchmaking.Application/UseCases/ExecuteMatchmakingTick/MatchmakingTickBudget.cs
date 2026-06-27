namespace Kombats.Matchmaking.Application.UseCases.ExecuteMatchmakingTick;

internal static class MatchmakingTickBudget
{
    // Single source of truth for the matchmaking lease TTL; the infrastructure-side
    // MatchmakingLeaseService reads this so the handler's soft deadline stays tied to it.
    internal const int LeaseLockTtlMs = 5000;

    // Soft wall-time deadline for the inner pairing loop. Half the lease TTL leaves
    // headroom under the renewal interval (≈1.67 s) so we exit cleanly before a renewal
    // round-trip could collide with end-of-loop cleanup.
    internal const int SoftDeadlineMs = LeaseLockTtlMs / 2;
}
