namespace Kombats.Chat.Domain.Messages;

public sealed class Message
{
    public const int MaxContentLength = 500;
    public const int MinContentLength = 1;

    private Message() { }

    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid SenderIdentityId { get; private set; }
    public string SenderDisplayName { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public DateTimeOffset SentAt { get; private set; }

    public static Message Create(
        Guid conversationId,
        Guid senderIdentityId,
        string senderDisplayName,
        string content,
        DateTimeOffset sentAt)
    {
        string sanitized = Sanitize(content);

        if (sanitized.Length < MinContentLength)
            throw new ArgumentException("Message content must not be empty after sanitization.");

        if (sanitized.Length > MaxContentLength)
            throw new ArgumentException($"Message content must not exceed {MaxContentLength} characters.");

        return new Message
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversationId,
            SenderIdentityId = senderIdentityId,
            SenderDisplayName = senderDisplayName,
            Content = sanitized,
            SentAt = sentAt,
        };
    }

    /// <summary>
    /// Sanitizes message content: trim, collapse whitespace, strip control chars.
    /// </summary>
    public static string Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Strip control characters (except common whitespace)
        var chars = input
            .Where(c => !char.IsControl(c) || c == ' ' || c == '\n')
            .ToArray();

        string stripped = new(chars);

        // Trim leading/trailing whitespace
        stripped = stripped.Trim();

        // Collapse consecutive whitespace into single space
        return CollapseWhitespace(stripped);
    }

    private static string CollapseWhitespace(string input)
    {
        var result = new char[input.Length];
        int writeIndex = 0;
        bool lastWasSpace = false;

        foreach (char c in input)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    result[writeIndex++] = ' ';
                    lastWasSpace = true;
                }
            }
            else
            {
                result[writeIndex++] = c;
                lastWasSpace = false;
            }
        }

        return new string(result, 0, writeIndex);
    }
}
