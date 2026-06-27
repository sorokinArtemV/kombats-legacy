namespace Kombats.Players.Infrastructure.Messaging.Inbox;

public sealed class InboxMessage
{
    public Guid MessageId { get; init; }
    public DateTimeOffset ProcessedAt { get; init; }
}