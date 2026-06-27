using FluentAssertions;
using Kombats.Chat.Application.Repositories;
using Kombats.Chat.Application.UseCases.GetConversationMessages;
using Kombats.Chat.Domain.Conversations;
using Kombats.Chat.Domain.Messages;
using NSubstitute;
using Xunit;

namespace Kombats.Chat.Application.Tests;

public sealed class GetConversationMessagesHandlerTests
{
    private readonly IConversationRepository _conversations = Substitute.For<IConversationRepository>();
    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();
    private readonly GetConversationMessagesHandler _handler;

    public GetConversationMessagesHandlerTests()
    {
        _handler = new GetConversationMessagesHandler(_conversations, _messages);
    }

    [Fact]
    public async Task Handle_ConversationNotFound_ReturnsNotFound()
    {
        _conversations.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Conversation?)null);

        var query = new GetConversationMessagesQuery(Guid.NewGuid(), Guid.NewGuid(), null, 50);
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("GetConversationMessages.NotFound");
    }

    [Fact]
    public async Task Handle_CallerNotParticipant_ReturnsNotFound()
    {
        var participantA = Guid.NewGuid();
        var participantB = Guid.NewGuid();
        var nonParticipant = Guid.NewGuid();
        var conversation = Conversation.CreateDirect(participantA, participantB);

        _conversations.GetByIdAsync(conversation.Id, Arg.Any<CancellationToken>())
            .Returns(conversation);

        var query = new GetConversationMessagesQuery(conversation.Id, nonParticipant, null, 50);
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("GetConversationMessages.NotFound");
    }

    [Fact]
    public async Task Handle_GlobalConversation_ReturnsMessages()
    {
        var global = Conversation.CreateGlobal(Conversation.GlobalConversationId);
        var callerId = Guid.NewGuid();

        _conversations.GetByIdAsync(global.Id, Arg.Any<CancellationToken>())
            .Returns(global);

        var msg = Message.Create(global.Id, Guid.NewGuid(), "Player1", "Hello", DateTimeOffset.UtcNow);
        _messages.GetByConversationAsync(global.Id, null, 51, Arg.Any<CancellationToken>())
            .Returns(new List<Message> { msg });

        var query = new GetConversationMessagesQuery(global.Id, callerId, null, 50);
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Messages.Should().HaveCount(1);
        result.Value.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_HasMore_ReturnsTrue_WhenExtraRow()
    {
        var global = Conversation.CreateGlobal(Conversation.GlobalConversationId);
        var callerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        _conversations.GetByIdAsync(global.Id, Arg.Any<CancellationToken>())
            .Returns(global);

        // Return 3 messages when limit is 2 (handler requests limit+1=3)
        var msgs = Enumerable.Range(0, 3)
            .Select(i => Message.Create(global.Id, Guid.NewGuid(), "P", $"msg{i}", now.AddMinutes(-i)))
            .ToList();

        _messages.GetByConversationAsync(global.Id, null, 3, Arg.Any<CancellationToken>())
            .Returns(msgs);

        var query = new GetConversationMessagesQuery(global.Id, callerId, null, 2);
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Messages.Should().HaveCount(2);
        result.Value.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_LimitClampedTo50()
    {
        var global = Conversation.CreateGlobal(Conversation.GlobalConversationId);
        var callerId = Guid.NewGuid();

        _conversations.GetByIdAsync(global.Id, Arg.Any<CancellationToken>())
            .Returns(global);

        _messages.GetByConversationAsync(global.Id, null, 51, Arg.Any<CancellationToken>())
            .Returns(new List<Message>());

        var query = new GetConversationMessagesQuery(global.Id, callerId, null, 100);
        await _handler.HandleAsync(query, CancellationToken.None);

        // Verify we clamped to 50 + 1 = 51
        await _messages.Received(1).GetByConversationAsync(global.Id, null, 51, Arg.Any<CancellationToken>());
    }
}
