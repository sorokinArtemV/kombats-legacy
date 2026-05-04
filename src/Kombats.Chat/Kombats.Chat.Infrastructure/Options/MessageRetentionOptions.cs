namespace Kombats.Chat.Infrastructure.Options;

/// <summary>
/// Configuration for <c>MessageRetentionWorker</c>. Binds from <c>Chat:Retention</c>.
/// </summary>
internal sealed class MessageRetentionOptions
{
    public const string SectionName = "Chat:Retention";

    /// <summary>Delay between retention passes.</summary>
    public int ScanIntervalSeconds { get; set; } = 3600;

    /// <summary>Message TTL in hours (messages older than this are deleted).</summary>
    public int MessageTtlHours { get; set; } = 24;

    /// <summary>Max rows deleted per batch statement (holds locks shorter).</summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>Safety cap on batches per pass to keep the worker responsive.</summary>
    public int MaxBatchesPerPass { get; set; } = 100;
}
