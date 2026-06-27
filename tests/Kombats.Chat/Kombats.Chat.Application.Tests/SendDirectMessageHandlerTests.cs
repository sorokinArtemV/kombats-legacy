using FluentAssertions;
using Kombats.Chat.Application;
using Kombats.Chat.Application.Notifications;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Application.Repositories;
using Kombats.Chat.Application.UseCases.SendDirectMessage;
using Kombats.Chat.Domain.Conversations;
using Kombats.Chat.Domain.Messages;
using NSubstitute;
using Xunit;

namespace Kombats.Chat.Application.Tests;

public sealed class SendDirectMessageHandlerTests
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

    private SendDirectMessageHandler Build() => new(
        _eligibility, _restriction, _rate, _filter, _names,
        _conversations, _messages, _notifier, _time);

    private void DefaultSetup(Guid sender, Guid recipient, Conversation conversation)
    {
        _eligibility.CheckEligibilityAsync(sender, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(true, "Alice"));
        _eligibility.CheckEligibilityAsync(recipient, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(true, "Bob"));
        _restriction.CanSendAsync(sender, Arg.Any<CancellationToken>()).Returns(true);
        _rate.CheckAndIncrementAsync(sender, "dm", Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult(true));
        _filter.Filter(Arg.Any<string>())
            .Returns(c => new MessageFilterResult(true, ((string)c[0]).Trim(), null));
        _conversations.GetOrCreateDirectAsync(sender, recipient, Arg.Any<CancellationToken>())
            .Returns(conversation);
    }

    [Fact]
    public async Task HappyPath_PersistsAndDelivers()
    {
        var sender = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        var conversation = Conversation.CreateDirect(sender, recipient);
        DefaultSetup(sender, recipient, conversation);

        var result = await Build().HandleAsync(
            new SendDirectMessageCommand(sender, recipient, "hello"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ConversationId.Should().Be(conversation.Id);

        await _messages.Received(1).SaveAsync(
            Arg.Is<Message>(m => m.ConversationId == conversation.Id && m.SenderIdentityId == sender && m.Content == "hello"),
            Arg.Any<CancellationToken>());
        await _conversations.Received(1).UpdateLastMessageAtAsync(
            conversation.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _notifier.Received(1).SendDirectMessageAsync(
            recipient,
            Arg.Is<DirectMessageEvent>(e => e.ConversationId == conversation.Id && e.Content == "hello"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecipientNotEligible_ReturnsRecipientNotFound()
    {
        var sender = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        _eligibility.CheckEligibilityAsync(sender, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(true, "Alice"));
        _restriction.CanSendAsync(sender, Arg.Any<CancellationToken>()).Returns(true);
        _eligibility.CheckEligibilityAsync(recipient, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(false));

        var result = await Build().HandleAsync(
            new SendDirectMessageCommand(sender, recipient, "hi"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ChatErrorCodes.RecipientNotFound);
    }

    [Fact]
    public async Task SenderNotEligible_ReturnsNotEligible()
    {
        var sender = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        _eligibility.CheckEligibilityAsync(sender, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(false));

        var result = await Build().HandleAsync(
            new SendDirectMessageCommand(sender, recipient, "hi"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ChatErrorCodes.NotEligible);
    }

    [Fact]
    public async Task NamedButNotReady_IsRejected()
    {
        // Critical Batch 3 negative case for sender.
        var sender = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        _eligibility.CheckEligibilityAsync(sender, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(false)); // displayName cached but not Ready

        var result = await Build().HandleAsync(
            new SendDirectMessageCommand(sender, recipient, "hi"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ChatErrorCodes.NotEligible);
        await _messages.DidNotReceiveWithAnyArgs().SaveAsync(default!, default);
    }

    [Fact]
    public async Task SendingToSelf_Rejected()
    {
        var id = Guid.NewGuid();
        var result = await Build().HandleAsync(
            new SendDirectMessageCommand(id, id, "hi"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ChatErrorCodes.RecipientNotFound);
    }

    [Fact]
    public async Task RateLimited_ReturnsRetryAfter()
    {
        var sender = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        var conversation = Conversation.CreateDirect(sender, recipient);
        DefaultSetup(sender, recipient, conversation);
        _rate.CheckAndIncrementAsync(sender, "dm", Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult(false, 1234));

        var result = await Build().HandleAsync(
            new SendDirectMessageCommand(sender, recipient, "hi"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ChatErrorCodes.RateLimited);
        ((ChatError)result.Error).RetryAfterMs.Should().Be(1234);
    }

    [Fact]
    public async Task InvalidContent_Rejected()
    {
        var sender = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        var conversation = Conversation.CreateDirect(sender, recipient);
        DefaultSetup(sender, recipient, conversation);
        _filter.Filter(Arg.Any<string>())
            .Returns(new MessageFilterResult(false, null, ChatErrorCodes.MessageEmpty));

        var result = await Build().HandleAsync(
            new SendDirectMessageCommand(sender, recipient, ""), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ChatErrorCodes.MessageEmpty);
    }
}
