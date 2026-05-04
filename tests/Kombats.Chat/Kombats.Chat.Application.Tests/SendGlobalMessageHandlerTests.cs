using FluentAssertions;
using Kombats.Chat.Application;
using Kombats.Chat.Application.Notifications;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Application.Repositories;
using Kombats.Chat.Application.UseCases.SendGlobalMessage;
using Kombats.Chat.Domain.Conversations;
using Kombats.Chat.Domain.Messages;
using NSubstitute;
using Xunit;

namespace Kombats.Chat.Application.Tests;

public sealed class SendGlobalMessageHandlerTests
{
    private readonly IEligibilityChecker _eligibility = Substitute.For<IEligibilityChecker>();
    private readonly IUserRestriction _restriction = Substitute.For<IUserRestriction>();
    private readonly IRateLimiter _rate = Substitute.For<IRateLimiter>();
    private readonly IMessageFilter _filter = Substitute.For<IMessageFilter>();
    private readonly IDisplayNameResolver _names = Substitute.For<IDisplayNameResolver>();
    private readonly IConversationRepository _conversations = Substitute.For<IConversationRepository>();
    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();
    private readonly IChatNotifier _notifier = Substitute.For<IChatNotifier>();
    private readonly TimeProvider _time = TimeProvider.System;

    private SendGlobalMessageHandler Build() => new(
        _eligibility, _restriction, _rate, _filter, _names,
        _conversations, _messages, _notifier, _time);

    private void DefaultSetup(Guid id)
    {
        _eligibility.CheckEligibilityAsync(id, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(true, "Alice"));
        _restriction.CanSendAsync(id, Arg.Any<CancellationToken>()).Returns(true);
        _rate.CheckAndIncrementAsync(id, "global", Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult(true));
        _filter.Filter(Arg.Any<string>())
            .Returns(c => new MessageFilterResult(true, ((string)c[0]).Trim(), null));
    }

    [Fact]
    public async Task HappyPath_PersistsMessageAndBroadcasts()
    {
        var id = Guid.NewGuid();
        DefaultSetup(id);

        var result = await Build().HandleAsync(new SendGlobalMessageCommand(id, "hello"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _messages.Received(1).SaveAsync(
            Arg.Is<Message>(m => m.Content == "hello" && m.SenderIdentityId == id && m.SenderDisplayName == "Alice"),
            Arg.Any<CancellationToken>());
        await _conversations.Received(1).UpdateLastMessageAtAsync(
            Conversation.GlobalConversationId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _notifier.Received(1).BroadcastGlobalMessageAsync(
            Arg.Is<GlobalMessageEvent>(e => e.Content == "hello"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotEligible_Rejected()
    {
        var id = Guid.NewGuid();
        _eligibility.CheckEligibilityAsync(id, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(false));

        var result = await Build().HandleAsync(new SendGlobalMessageCommand(id, "hi"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ChatErrorCodes.NotEligible);
        await _messages.DidNotReceiveWithAnyArgs().SaveAsync(default!, default);
    }

    [Fact]
    public async Task RateLimited_ReturnsRetryAfter()
    {
        var id = Guid.NewGuid();
        DefaultSetup(id);
        _rate.CheckAndIncrementAsync(id, "global", Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult(false, 4200));

        var result = await Build().HandleAsync(new SendGlobalMessageCommand(id, "hi"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ChatErrorCodes.RateLimited);
        ((ChatError)result.Error).RetryAfterMs.Should().Be(4200);
    }

    [Theory]
    [InlineData(ChatErrorCodes.MessageEmpty)]
    [InlineData(ChatErrorCodes.MessageTooLong)]
    public async Task InvalidContent_Rejected(string code)
    {
        var id = Guid.NewGuid();
        DefaultSetup(id);
        _filter.Filter(Arg.Any<string>())
            .Returns(new MessageFilterResult(false, null, code));

        var result = await Build().HandleAsync(new SendGlobalMessageCommand(id, ""), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(code);
    }
}
