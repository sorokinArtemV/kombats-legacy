using Kombats.Chat.Application;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Domain.Messages;

namespace Kombats.Chat.Infrastructure.Services;

/// <summary>
/// v1 message filter: trims/sanitizes via <see cref="Message.Sanitize"/> and enforces
/// the 1–500 character envelope. No profanity/blocklist in v1 (placeholder for future moderation).
/// </summary>
internal sealed class MessageFilter : IMessageFilter
{
    public MessageFilterResult Filter(string content)
    {
        if (content is null)
        {
            return new MessageFilterResult(false, null, ChatErrorCodes.MessageEmpty);
        }

        if (content.Length > Message.MaxContentLength)
        {
            return new MessageFilterResult(false, null, ChatErrorCodes.MessageTooLong);
        }

        string sanitized = Message.Sanitize(content);

        if (sanitized.Length < Message.MinContentLength)
        {
            return new MessageFilterResult(false, null, ChatErrorCodes.MessageEmpty);
        }

        return new MessageFilterResult(true, sanitized, null);
    }
}
