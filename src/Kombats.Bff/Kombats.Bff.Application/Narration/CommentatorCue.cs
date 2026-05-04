using Kombats.Bff.Application.Narration.Feed;

namespace Kombats.Bff.Application.Narration;

/// <summary>
/// Output of a commentator trigger: a template category to render as a commentary entry.
/// </summary>
public sealed record CommentatorCue
{
    public required string Category { get; init; }
    public required FeedEntryKind Kind { get; init; }
    public required NarrationContext Context { get; init; }
}
