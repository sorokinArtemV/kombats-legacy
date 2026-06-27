using FluentAssertions;
using Kombats.Chat.Domain.Messages;
using Xunit;

namespace Kombats.Chat.Domain.Tests;

public sealed class MessageTests
{
    [Fact]
    public void Create_ValidContent_Succeeds()
    {
        var conversationId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var message = Message.Create(conversationId, senderId, "Player1", "Hello world", now);

        message.ConversationId.Should().Be(conversationId);
        message.SenderIdentityId.Should().Be(senderId);
        message.SenderDisplayName.Should().Be("Player1");
        message.Content.Should().Be("Hello world");
        message.SentAt.Should().Be(now);
        message.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_EmptyContent_Throws()
    {
        var act = () => Message.Create(Guid.NewGuid(), Guid.NewGuid(), "P", "", DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithMessage("*empty*");
    }

    [Fact]
    public void Create_WhitespaceOnlyContent_Throws()
    {
        var act = () => Message.Create(Guid.NewGuid(), Guid.NewGuid(), "P", "   ", DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithMessage("*empty*");
    }

    [Fact]
    public void Create_ContentExceedsMaxLength_Throws()
    {
        string longContent = new('a', 501);

        var act = () => Message.Create(Guid.NewGuid(), Guid.NewGuid(), "P", longContent, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithMessage("*500*");
    }

    [Fact]
    public void Create_ContentAtMaxLength_Succeeds()
    {
        string content = new('a', 500);

        var message = Message.Create(Guid.NewGuid(), Guid.NewGuid(), "P", content, DateTimeOffset.UtcNow);

        message.Content.Should().HaveLength(500);
    }

    [Fact]
    public void Sanitize_TrimsWhitespace()
    {
        string result = Message.Sanitize("  hello  ");

        result.Should().Be("hello");
    }

    [Fact]
    public void Sanitize_CollapsesWhitespace()
    {
        string result = Message.Sanitize("hello   world");

        result.Should().Be("hello world");
    }

    [Fact]
    public void Sanitize_StripsControlCharacters()
    {
        string result = Message.Sanitize("hello\x00\x01\x02world");

        result.Should().Be("helloworld");
    }

    [Fact]
    public void Sanitize_PreservesNewlines()
    {
        // Newlines are allowed but collapsed with other whitespace
        string result = Message.Sanitize("hello\nworld");

        result.Should().Be("hello world");
    }

    [Fact]
    public void Sanitize_NullOrWhitespace_ReturnsEmpty()
    {
        Message.Sanitize("").Should().BeEmpty();
        Message.Sanitize("   ").Should().BeEmpty();
    }

    [Fact]
    public void Create_SanitizesContent()
    {
        var message = Message.Create(
            Guid.NewGuid(), Guid.NewGuid(), "P",
            "  hello   world  ",
            DateTimeOffset.UtcNow);

        message.Content.Should().Be("hello world");
    }
}
