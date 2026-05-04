using FluentAssertions;
using Kombats.Chat.Application.Notifications;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Application.UseCases.DisconnectUser;
using NSubstitute;
using Xunit;

namespace Kombats.Chat.Application.Tests;

public sealed class DisconnectUserHandlerTests
{
    private readonly IPresenceStore _presence = Substitute.For<IPresenceStore>();
    private readonly IChatNotifier _notifier = Substitute.For<IChatNotifier>();
    private readonly DisconnectUserHandler _handler;

    public DisconnectUserHandlerTests()
    {
        _handler = new DisconnectUserHandler(_presence, _notifier);
    }

    [Fact]
    public async Task LastDisconnect_BroadcastsPlayerOffline()
    {
        var id = Guid.NewGuid();
        _presence.DisconnectAsync(id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.HandleAsync(new DisconnectUserCommand(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _notifier.Received(1).BroadcastPlayerOfflineAsync(
            Arg.Is<PlayerOfflineEvent>(e => e.PlayerId == id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonLastDisconnect_DoesNotBroadcast()
    {
        var id = Guid.NewGuid();
        _presence.DisconnectAsync(id, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(new DisconnectUserCommand(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _notifier.DidNotReceiveWithAnyArgs().BroadcastPlayerOfflineAsync(default!, default);
    }
}
