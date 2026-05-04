using FluentAssertions;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Application.Repositories;
using Kombats.Chat.Application.UseCases.GetConversations;
using Kombats.Chat.Domain.Conversations;
using NSubstitute;
using Xunit;

namespace Kombats.Chat.Application.Tests;

public sealed class GetConversationsHandlerTests
{
    private readonly IConversationRepository _conversations = Substitute.For<IConversationRepository>();
    private readonly IDisplayNameResolver _displayNameResolver = Substitute.For<IDisplayNameResolver>();
    private readonly GetConversationsHandler _handler;

    public GetConversationsHandlerTests()
    {
        _handler = new GetConversationsHandler(_conversations, _displayNameResolver);

        // Default: resolver returns the display name "ResolvedName"
        _displayNameResolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns("ResolvedName");
    }

    [Fact]
    public async Task Handle_ReturnsConversationsOrderedByLastMessageAt()
    {
        var callerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var global = Conversation.CreateGlobal(Conversation.GlobalConversationId);
        global.UpdateLastMessageAt(now.AddMinutes(-10));

        var other = Guid.NewGuid();
        var dm = Conversation.CreateDirect(callerId, other);
        dm.UpdateLastMessageAt(now);

        _conversations.ListByParticipantAsync(callerId, Arg.Any<CancellationToken>())
            .Returns(new List<Conversation> { global, dm });

        var query = new GetConversationsQuery(callerId);
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Conversations.Should().HaveCount(2);
        // DM has more recent LastMessageAt, should be first
        result.Value.Conversations[0].Type.Should().Be("Direct");
        result.Value.Conversations[1].Type.Should().Be("Global");
    }

    [Fact]
    public async Task Handle_DirectConversation_ResolvesDisplayName()
    {
        var callerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var dm = Conversation.CreateDirect(callerId, otherId);

        _conversations.ListByParticipantAsync(callerId, Arg.Any<CancellationToken>())
            .Returns(new List<Conversation> { dm });

        _displayNameResolver.ResolveAsync(otherId, Arg.Any<CancellationToken>())
            .Returns("Alice");

        var result = await _handler.HandleAsync(new GetConversationsQuery(callerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value.Conversations[0];
        dto.OtherPlayer.Should().NotBeNull();
        dto.OtherPlayer!.PlayerId.Should().Be(otherId);
        dto.OtherPlayer.DisplayName.Should().Be("Alice");
    }

    [Fact]
    public async Task Handle_GlobalConversation_OtherPlayerIsNull()
    {
        var callerId = Guid.NewGuid();
        var global = Conversation.CreateGlobal(Conversation.GlobalConversationId);

        _conversations.ListByParticipantAsync(callerId, Arg.Any<CancellationToken>())
            .Returns(new List<Conversation> { global });

        var result = await _handler.HandleAsync(new GetConversationsQuery(callerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Conversations[0].OtherPlayer.Should().BeNull();
    }

    [Fact]
    public async Task Handle_EmptyList_ReturnsEmptyResponse()
    {
        var callerId = Guid.NewGuid();

        _conversations.ListByParticipantAsync(callerId, Arg.Any<CancellationToken>())
            .Returns(new List<Conversation>());

        var result = await _handler.HandleAsync(new GetConversationsQuery(callerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Conversations.Should().BeEmpty();
    }
}
