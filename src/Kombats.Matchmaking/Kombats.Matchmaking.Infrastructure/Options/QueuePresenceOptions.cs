namespace Kombats.Matchmaking.Infrastructure.Options;

/// <summary>
/// Options for the queue-presence heartbeat / sweep machinery.
/// Numbers default to: 10s heartbeat (client), 15s TTL (server), 20s sweep —
/// worst-case ~35s cleanup after a tab dies without a graceful leave.
/// </summary>
internal sealed class QueuePresenceOptions
{
    public const string SectionName = "Matchmaking:QueuePresence";

    /// <summary>TTL applied to the per-identity refs SET on every register/refresh.</summary>
    public int PresenceTtlSeconds { get; set; } = 15;

    /// <summary>Identities with last heartbeat older than this are considered stale.</summary>
    public int StaleAfterSeconds { get; set; } = 15;

    /// <summary>Delay between sweep passes.</summary>
    public int SweepIntervalSeconds { get; set; } = 20;
}
