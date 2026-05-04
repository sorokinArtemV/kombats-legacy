using FluentAssertions;
using Kombats.Chat.Application.Notifications;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Application.UseCases.ConnectUser;
using NSubstitute;
using Xunit;

namespace Kombats.Chat.Application.Tests;

public sealed class ConnectUserHandlerTests
{
    private readonly IPresenceStore _presence = Substitute.For<IPresenceStore>();
    private readonly IDisplayNameResolver _names = Substitute.For<IDisplayNameResolver>();
    private readonly IChatNotifier _notifier = Substitute.For<IChatNotifier>();
    private readonly ConnectUserHandler _handler;

    public ConnectUserHandlerTests()
    {
        _handler = new ConnectUserHandler(_presence, _names, _notifier);
    }

    [Fact]
    public async Task FirstConnection_BroadcastsPlayerOnline()
    {
        var id = Guid.NewGuid();
        _names.ResolveAsync(id, Arg.Any<CancellationToken>()).Returns("Alice");
        _presence.ConnectAsync(id, "Alice", Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.HandleAsync(new ConnectUserCommand(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _notifier.Received(1).BroadcastPlayerOnlineAsync(
            Arg.Is<PlayerOnlineEvent>(e => e.PlayerId == id && e.DisplayName == "Alice"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonFirstConnection_DoesNotBroadcast()
    {
        var id = Guid.NewGuid();
        _names.ResolveAsync(id, Arg.Any<CancellationToken>()).Returns("Bob");
        _presence.ConnectAsync(id, "Bob", Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(new ConnectUserCommand(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _notifier.DidNotReceiveWithAnyArgs().BroadcastPlayerOnlineAsync(default!, default);
    }
}
