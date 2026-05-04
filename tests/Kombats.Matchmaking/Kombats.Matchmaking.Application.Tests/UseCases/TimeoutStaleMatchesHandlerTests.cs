using FluentAssertions;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Application.UseCases.TimeoutStaleMatches;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Kombats.Matchmaking.Application.Tests.UseCases;

public sealed class TimeoutStaleMatchesHandlerTests
{
    private readonly IMatchRepository _matchRepo = Substitute.For<IMatchRepository>();
    private readonly IPlayerMatchStatusStore _statusStore = Substitute.For<IPlayerMatchStatusStore>();
    private readonly TimeoutStaleMatchesHandler _handler;

    public TimeoutStaleMatchesHandlerTests()
    {
        _handler = new TimeoutStaleMatchesHandler(
            _matchRepo,
            _statusStore,
            Substitute.For<ILogger<TimeoutStaleMatchesHandler>>());
    }

    [Fact]
    public async Task Handle_NoStaleMatches_ReturnsZero()
    {
        _matchRepo.TimeoutStaleMatchesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<(Guid, Guid)>());
        _matchRepo.TimeoutStaleBattleCreatedMatchesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<(Guid, Guid)>());

        var result = await _handler.HandleAsync(new TimeoutStaleMatchesCommand(60, 600), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
        await _statusStore.DidNotReceiveWithAnyArgs().RemoveStatusAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StaleMatchesExist_ReturnsCombinedCount()
    {
        var players1 = new List<(Guid, Guid)>
        {
            (Guid.NewGuid(), Guid.NewGuid()),
            (Guid.NewGuid(), Guid.NewGuid()),
            (Guid.NewGuid(), Guid.NewGuid())
        };
        var players2 = new List<(Guid, Guid)>
        {
            (Guid.NewGuid(), Guid.NewGuid()),
            (Guid.NewGuid(), Guid.NewGuid())
        };

        _matchRepo.TimeoutStaleMatchesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(players1);
        _matchRepo.TimeoutStaleBattleCreatedMatchesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(players2);

        var result = await _handler.HandleAsync(new TimeoutStaleMatchesCommand(60, 600), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(5);
    }

    [Fact]
    public async Task Handle_PassesCorrectCutoff()
    {
        _matchRepo.TimeoutStaleMatchesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<(Guid, Guid)>());
        _matchRepo.TimeoutStaleBattleCreatedMatchesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<(Guid, Guid)>());

        await _handler.HandleAsync(new TimeoutStaleMatchesCommand(120, 600), CancellationToken.None);

        await _matchRepo.Received(1).TimeoutStaleMatchesAsync(
            Arg.Is<DateTimeOffset>(d => d < DateTimeOffset.UtcNow.AddSeconds(-100)),
            Arg.Is<DateTimeOffset>(d => d <= DateTimeOffset.UtcNow),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_BattleCreatedTimeout_CallsRepository()
    {
        _matchRepo.TimeoutStaleMatchesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<(Guid, Guid)>());
        var players = new List<(Guid, Guid)> { (Guid.NewGuid(), Guid.NewGuid()) };
        _matchRepo.TimeoutStaleBattleCreatedMatchesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(players);

        var result = await _handler.HandleAsync(new TimeoutStaleMatchesCommand(60, 600), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        await _matchRepo.Received(1).TimeoutStaleBattleCreatedMatchesAsync(
            Arg.Is<DateTimeOffset>(d => d < DateTimeOffset.UtcNow.AddSeconds(-580)),
            Arg.Is<DateTimeOffset>(d => d <= DateTimeOffset.UtcNow),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_BothTimeoutsHaveMatches_ReturnsCombinedCount()
    {
        var players1 = new List<(Guid, Guid)>
        {
            (Guid.NewGuid(), Guid.NewGuid()),
            (Guid.NewGuid(), Guid.NewGuid())
        };
        var players2 = new List<(Guid, Guid)>
        {
            (Guid.NewGuid(), Guid.NewGuid()),
            (Guid.NewGuid(), Guid.NewGuid()),
            (Guid.NewGuid(), Guid.NewGuid())
        };

        _matchRepo.TimeoutStaleMatchesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(players1);
        _matchRepo.TimeoutStaleBattleCreatedMatchesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(players2);

        var result = await _handler.HandleAsync(new TimeoutStaleMatchesCommand(60, 600), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(5);
    }

    [Fact]
    public async Task Handle_StaleMatches_ClearsRedisStatusForAllPlayers()
    {
        var playerA1 = Guid.NewGuid();
        var playerB1 = Guid.NewGuid();
        var playerA2 = Guid.NewGuid();
        var playerB2 = Guid.NewGuid();

        _matchRepo.TimeoutStaleMatchesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<(Guid, Guid)> { (playerA1, playerB1) });
        _matchRepo.TimeoutStaleBattleCreatedMatchesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<(Guid, Guid)> { (playerA2, playerB2) });

        await _handler.HandleAsync(new TimeoutStaleMatchesCommand(60, 600), CancellationToken.None);

        // All 4 players should have Redis status cleared
        await _statusStore.Received(1).RemoveStatusAsync(playerA1, Arg.Any<CancellationToken>());
        await _statusStore.Received(1).RemoveStatusAsync(playerB1, Arg.Any<CancellationToken>());
        await _statusStore.Received(1).RemoveStatusAsync(playerA2, Arg.Any<CancellationToken>());
        await _statusStore.Received(1).RemoveStatusAsync(playerB2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoStaleMatches_DoesNotClearRedisStatus()
    {
        _matchRepo.TimeoutStaleMatchesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<(Guid, Guid)>());
        _matchRepo.TimeoutStaleBattleCreatedMatchesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<(Guid, Guid)>());

        await _handler.HandleAsync(new TimeoutStaleMatchesCommand(60, 600), CancellationToken.None);

        await _statusStore.DidNotReceiveWithAnyArgs().RemoveStatusAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
