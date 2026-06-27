namespace Kombats.Chat.Application;

/// <summary>
/// Frozen Batch 3 chat error codes. These are the canonical strings emitted on the
/// SignalR <c>ChatError</c> event and used as <see cref="Kombats.Abstractions.Error"/>
/// codes returned from chat use-case handlers. Batch 5 (BFF) consumes these verbatim.
/// </summary>
internal static class ChatErrorCodes
{
    public const string RateLimited = "rate_limited";
    public const string MessageTooLong = "message_too_long";
    public const string MessageEmpty = "message_empty";
    public const string RecipientNotFound = "recipient_not_found";
    public const string NotEligible = "not_eligible";
    public const string ServiceUnavailable = "service_unavailable";
}
