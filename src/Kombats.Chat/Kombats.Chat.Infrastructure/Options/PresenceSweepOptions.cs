namespace Kombats.Chat.Infrastructure.Options;

/// <summary>
/// Configuration for <c>PresenceSweepWorker</c>. Binds from <c>Chat:PresenceSweep</c>.
/// </summary>
internal sealed class PresenceSweepOptions
{
    public const string SectionName = "Chat:PresenceSweep";

    /// <summary>Delay between sweep passes.</summary>
    public int ScanIntervalSeconds { get; set; } = 60;

    /// <summary>Entries older than this are considered stale and removed.</summary>
    public int StaleAfterSeconds { get; set; } = 90;
}
