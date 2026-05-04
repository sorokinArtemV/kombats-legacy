namespace Kombats.Matchmaking.Infrastructure.Options;

/// <summary>
/// Configuration options for Redis matchmaking stores.
/// </summary>
internal sealed class MatchmakingRedisOptions
{
    public const string SectionName = "Matchmaking:Redis";

    /// <summary>
    /// Redis database index to use for matchmaking keys.
    /// Default: 1 (as per requirements).
    /// </summary>
    public int DatabaseIndex { get; set; } = 1;

    /// <summary>
    /// Time-to-live for player status and match records in Redis.
    /// Default: 1800 seconds (30 minutes).
    /// </summary>
    public int StatusTtlSeconds { get; set; } = 1800;

    /// <summary>
    /// Time-to-live for canceled player entries in Redis ZSET.
    /// Default: 600 seconds (10 minutes).
    /// </summary>
    public int CancelTtlSeconds { get; set; } = 600;
}

/// <summary>
/// Configuration options for matchmaking worker.
/// </summary>
internal sealed class MatchmakingWorkerOptions
{
    public const string SectionName = "Matchmaking:Worker";

    /// <summary>
    /// Delay between matchmaking ticks in milliseconds.
    /// Default: 100ms.
    /// </summary>
    public int TickDelayMs { get; set; } = 100;
}





