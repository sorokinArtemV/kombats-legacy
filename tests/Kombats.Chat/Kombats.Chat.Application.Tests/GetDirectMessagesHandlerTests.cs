using FluentAssertions;
using Kombats.Chat.Application.Repositories;
using Kombats.Chat.Application.UseCases.GetConversationMessages;
using Kombats.Chat.Application.UseCases.GetDirectMessages;
using Kombats.Chat.Domain.Conversations;
using Kombats.Chat.Domain.Messages;
using NSubstitute;
using Xunit;

namespace Kombats.Chat.Application.Tests;

public sealed class GetDirectMessagesHandlerTests
{
    private readonly IConversationRepository _conversations = Substitute.For<IConversationRepository>();
    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();
    private readonly GetDirectMessagesHandler _handler;

    public GetDirectMessagesHandlerTests()
    {
        _handler = new GetDirectMessagesHandler(_conversations, _messages);
    }

    [Fact]
    public async Task Handle_SameUser_ReturnsValidationError()
    {
        var userId = Guid.NewGuid();
        var query = new GetDirectMessagesQuery(userId, userId, null, 50);

        var result = await _handler.HandleAsync(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("GetDirectMessages.SameUser");
    }

    [Fact]
    public async Task Handle_ResolvesConversationAndReturnsMessages()
    {
        var callerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var (a, b) = Conversation.SortParticipants(callerId, otherId);

        var conversation = Conversation.CreateDirect(callerId, otherId);
        _conversations.GetDirectByParticipantsAsync(a, b, Arg.Any<CancellationToken>())
            .Returns(conversation);

        var msg = Message.Create(conversation.Id, callerId, "Me", "Hi", DateTimeOffset.UtcNow);
        _messages.GetByConversationAsync(conversation.Id, null, 51, Arg.Any<CancellationToken>())
            .Returns(new List<Message> { msg });

        var query = new GetDirectMessagesQuery(callerId, otherId, null, 50);
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Messages.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_NoConversation_ReturnsEmptyList()
    {
        var callerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var (a, b) = Conversation.SortParticipants(callerId, otherId);

        _conversations.GetDirectByParticipantsAsync(a, b, Arg.Any<CancellationToken>())
            .Returns((Conversation?)null);

        var query = new GetDirectMessagesQuery(callerId, otherId, null, 50);
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Messages.Should().BeEmpty();
        result.Value.HasMore.Should().BeFalse();

        // Should NOT have called GetOrCreateDirectAsync
        await _conversations.DidNotReceive()
            .GetOrCreateDirectAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ConversationExists_NoMessages_ReturnsEmptyList()
    {
        var callerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var (a, b) = Conversation.SortParticipants(callerId, otherId);

        var conversation = Conversation.CreateDirect(callerId, otherId);
        _conversations.GetDirectByParticipantsAsync(a, b, Arg.Any<CancellationToken>())
            .Returns(conversation);

        _messages.GetByConversationAsync(conversation.Id, null, 51, Arg.Any<CancellationToken>())
            .Returns(new List<Message>());

        var query = new GetDirectMessagesQuery(callerId, otherId, null, 50);
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Messages.Should().BeEmpty();
        result.Value.HasMore.Should().BeFalse();
    }
}
