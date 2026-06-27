using FluentAssertions;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Application.UseCases.LeaveQueue;
using Kombats.Matchmaking.Domain;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Kombats.Matchmaking.Application.Tests.UseCases;

public sealed class LeaveQueueHandlerTests
{
    private readonly IMatchRepository _matchRepo = Substitute.For<IMatchRepository>();
    private readonly IMatchQueueStore _queueStore = Substitute.For<IMatchQueueStore>();
    private readonly IPlayerMatchStatusStore _statusStore = Substitute.For<IPlayerMatchStatusStore>();
    private readonly IQueuePresenceStore _presenceStore = Substitute.For<IQueuePresenceStore>();
    private readonly LeaveQueueHandler _handler;

    public LeaveQueueHandlerTests()
    {
        _handler = new LeaveQueueHandler(
            _matchRepo, _queueStore, _statusStore, _presenceStore,
            Substitute.For<ILogger<LeaveQueueHandler>>());
    }

    [Fact]
    public async Task Handle_ActiveMatch_ReturnsAlreadyMatched()
    {
        var playerId = Guid.NewGuid();
        var match = Match.Create(Guid.NewGuid(), Guid.NewGuid(), playerId, Guid.NewGuid(), "default", DateTimeOffset.UtcNow);
        match.MarkBattleCreateRequested(DateTimeOffset.UtcNow);
        _matchRepo.GetActiveForPlayerAsync(playerId, Arg.Any<CancellationToken>()).Returns(match);

        var result = await _handler.HandleAsync(new LeaveQueueCommand(playerId, "default", "test-conn-ref"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(LeaveQueueStatus.AlreadyMatched);
        result.Value.MatchId.Should().Be(match.MatchId);
    }

    [Fact]
    public async Task Handle_NotInQueue_ReturnsNotInQueue()
    {
        var playerId = Guid.NewGuid();
        _matchRepo.GetActiveForPlayerAsync(playerId, Arg.Any<CancellationToken>()).Returns((Match?)null);
        _statusStore.GetStatusAsync(playerId, Arg.Any<CancellationToken>()).Returns((PlayerMatchStatus?)null);

        var result = await _handler.HandleAsync(new LeaveQueueCommand(playerId, "default", "test-conn-ref"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(LeaveQueueStatus.NotInQueue);
    }

    [Fact]
    public async Task Handle_InQueue_LeavesSuccessfully()
    {
        var playerId = Guid.NewGuid();
        _matchRepo.GetActiveForPlayerAsync(playerId, Arg.Any<CancellationToken>()).Returns((Match?)null);
        _statusStore.GetStatusAsync(playerId, Arg.Any<CancellationToken>())
            .Returns(new PlayerMatchStatus
            {
                State = PlayerMatchState.Searching,
                Variant = "default",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

        var result = await _handler.HandleAsync(new LeaveQueueCommand(playerId, "default", "test-conn-ref"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(LeaveQueueStatus.Left);
        await _queueStore.Received(1).TryLeaveQueueAsync("default", playerId, Arg.Any<CancellationToken>());
        await _statusStore.Received(1).RemoveStatusAsync(playerId, Arg.Any<CancellationToken>());
    }
}
