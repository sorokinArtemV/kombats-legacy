namespace Kombats.Matchmaking.Infrastructure.Options;

/// <summary>
/// Configuration options for MatchTimeoutWorker.
/// </summary>
internal sealed class MatchTimeoutWorkerOptions
{
    public const string SectionName = "Matchmaking:TimeoutWorker";

    /// <summary>
    /// Interval between timeout scans in milliseconds.
    /// </summary>
    public int ScanIntervalMs { get; set; } = 5000;

    /// <summary>
    /// Timeout threshold in seconds. Matches in BattleCreateRequested state older than this will be marked TimedOut.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Timeout threshold in seconds for matches stuck in BattleCreated state (battle started but never completed).
    /// Default: 600 (10 minutes). See EI-015.
    /// </summary>
    public int BattleCreatedTimeoutSeconds { get; set; } = 600;
}





