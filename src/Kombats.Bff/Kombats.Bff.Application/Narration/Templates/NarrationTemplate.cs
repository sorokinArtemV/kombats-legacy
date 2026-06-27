using Kombats.Bff.Application.Narration.Feed;

namespace Kombats.Bff.Application.Narration.Templates;

/// <summary>
/// A single narration template with its metadata.
/// </summary>
public sealed record NarrationTemplate
{
    public required string Category { get; init; }
    public required string Template { get; init; }
    public required FeedEntryTone Tone { get; init; }
    public required FeedEntrySeverity Severity { get; init; }
}
