namespace Kombats.Chat.Application.Ports;

internal interface IMessageFilter
{
    /// <summary>
    /// Validates and sanitizes message content.
    /// Returns (valid, sanitizedContent, errorCode). When valid is false, errorCode is one of
    /// <see cref="ChatErrorCodes.MessageEmpty"/> or <see cref="ChatErrorCodes.MessageTooLong"/>.
    /// </summary>
    MessageFilterResult Filter(string content);
}

internal sealed record MessageFilterResult(bool Valid, string? SanitizedContent, string? ErrorCode);
