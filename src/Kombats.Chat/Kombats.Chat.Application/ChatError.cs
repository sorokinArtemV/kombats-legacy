using Kombats.Abstractions;

namespace Kombats.Chat.Application;

/// <summary>
/// Chat-domain error that carries the optional <c>RetryAfterMs</c> hint used by
/// the SignalR <c>ChatError</c> event for rate-limit responses. Inherits from
/// <see cref="Error"/> so it flows through the existing <see cref="Result"/> pipeline.
/// </summary>
internal sealed record ChatError(string Code, string Description, long? RetryAfterMs)
    : Error(Code, Description, ErrorType.Validation)
{
    public static ChatError NotEligible() =>
        new(ChatErrorCodes.NotEligible, "Sender's onboarding is not Ready.", null);

    public static ChatError RecipientNotFound() =>
        new(ChatErrorCodes.RecipientNotFound, "DM recipient does not exist or is not Ready.", null);

    public static ChatError RateLimited(long? retryAfterMs) =>
        new(ChatErrorCodes.RateLimited, "Message rate exceeded.", retryAfterMs);

    public static ChatError MessageTooLong() =>
        new(ChatErrorCodes.MessageTooLong, "Content exceeds 500 characters.", null);

    public static ChatError MessageEmpty() =>
        new(ChatErrorCodes.MessageEmpty, "Content is empty after sanitization.", null);

    public static ChatError ServiceUnavailable(string description) =>
        new(ChatErrorCodes.ServiceUnavailable, description, null);
}
