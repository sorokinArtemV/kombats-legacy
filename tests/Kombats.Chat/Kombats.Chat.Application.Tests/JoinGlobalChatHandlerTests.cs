using FluentAssertions;
using Kombats.Chat.Application;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Application.Repositories;
using Kombats.Chat.Application.UseCases.JoinGlobalChat;
using Kombats.Chat.Domain.Conversations;
using Kombats.Chat.Domain.Messages;
using NSubstitute;
using Xunit;

namespace Kombats.Chat.Application.Tests;

public sealed class JoinGlobalChatHandlerTests
{
    private readonly IEligibilityChecker _eligibility = Substitute.For<IEligibilityChecker>();
    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();
    private readonly IPresenceStore _presence = Substitute.For<IPresenceStore>();
    private readonly JoinGlobalChatHandler _handler;

    public JoinGlobalChatHandlerTests()
    {
        _handler = new JoinGlobalChatHandler(_eligibility, _messages, _presence);
    }

    [Fact]
    public async Task EligiblePlayer_ReturnsRecentMessagesAndOnlinePlayers()
    {
        var id = Guid.NewGuid();
        _eligibility.CheckEligibilityAsync(id, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(true, "Alice"));

        var msg = Message.Create(Conversation.GlobalConversationId, Guid.NewGuid(), "Bob", "hi", DateTimeOffset.UtcNow);
        _messages.GetByConversationAsync(Conversation.GlobalConversationId, null, JoinGlobalChatHandler.RecentMessagesLimit, Arg.Any<CancellationToken>())
            .Returns(new List<Message> { msg });

        _presence.GetOnlinePlayersAsync(JoinGlobalChatHandler.OnlinePlayersInitialLimit, 0, Arg.Any<CancellationToken>())
            .Returns(new List<OnlinePlayer> { new(Guid.NewGuid(), "Bob") });
        _presence.GetOnlineCountAsync(Arg.Any<CancellationToken>()).Returns(7);

        var result = await _handler.HandleAsync(new JoinGlobalChatCommand(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ConversationId.Should().Be(Conversation.GlobalConversationId);
        result.Value.RecentMessages.Should().HaveCount(1);
        result.Value.OnlinePlayers.Should().HaveCount(1);
        result.Value.TotalOnline.Should().Be(7);
    }

    [Fact]
    public async Task NamedButNotReady_IsRejected()
    {
        // Critical Batch 3 negative case: player has display name (cached) but
        // OnboardingState != Ready — must be rejected with not_eligible.
        var id = Guid.NewGuid();
        _eligibility.CheckEligibilityAsync(id, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(false));

        var result = await _handler.HandleAsync(new JoinGlobalChatCommand(id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ChatErrorCodes.NotEligible);
        await _messages.DidNotReceiveWithAnyArgs().GetByConversationAsync(default, default, default, default);
        await _presence.DidNotReceiveWithAnyArgs().GetOnlinePlayersAsync(default, default, default);
    }
}
