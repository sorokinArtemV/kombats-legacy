using FluentAssertions;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Application.UseCases.GetOnlinePlayers;
using NSubstitute;
using Xunit;

namespace Kombats.Chat.Application.Tests;

public sealed class GetOnlinePlayersHandlerTests
{
    private readonly IPresenceStore _presenceStore = Substitute.For<IPresenceStore>();
    private readonly GetOnlinePlayersHandler _handler;

    public GetOnlinePlayersHandlerTests()
    {
        _handler = new GetOnlinePlayersHandler(_presenceStore);
    }

    [Fact]
    public async Task Handle_ReturnsPlayersAndCount()
    {
        var players = new List<OnlinePlayer>
        {
            new(Guid.NewGuid(), "Alice"),
            new(Guid.NewGuid(), "Bob"),
        };

        _presenceStore.GetOnlinePlayersAsync(100, 0, Arg.Any<CancellationToken>())
            .Returns(players);
        _presenceStore.GetOnlineCountAsync(Arg.Any<CancellationToken>())
            .Returns(2);

        var query = new GetOnlinePlayersQuery(100, 0);
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Players.Should().HaveCount(2);
        result.Value.TotalOnline.Should().Be(2);
    }

    [Fact]
    public async Task Handle_ClampsLimit()
    {
        _presenceStore.GetOnlinePlayersAsync(100, 0, Arg.Any<CancellationToken>())
            .Returns(new List<OnlinePlayer>());
        _presenceStore.GetOnlineCountAsync(Arg.Any<CancellationToken>())
            .Returns(0);

        var query = new GetOnlinePlayersQuery(500, 0); // exceeds 100
        await _handler.HandleAsync(query, CancellationToken.None);

        await _presenceStore.Received(1).GetOnlinePlayersAsync(100, 0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyResult()
    {
        _presenceStore.GetOnlinePlayersAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<OnlinePlayer>());
        _presenceStore.GetOnlineCountAsync(Arg.Any<CancellationToken>())
            .Returns(0);

        var query = new GetOnlinePlayersQuery(100, 0);
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Players.Should().BeEmpty();
        result.Value.TotalOnline.Should().Be(0);
    }
}
