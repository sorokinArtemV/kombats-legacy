using FluentAssertions;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Application.UseCases.GetQueueStatus;
using Kombats.Matchmaking.Domain;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Kombats.Matchmaking.Application.Tests.UseCases;

public sealed class GetQueueStatusHandlerTests
{
    private readonly IMatchRepository _matchRepo = Substitute.For<IMatchRepository>();
    private readonly IPlayerMatchStatusStore _statusStore = Substitute.For<IPlayerMatchStatusStore>();
    private readonly GetQueueStatusHandler _handler;

    public GetQueueStatusHandlerTests()
    {
        _handler = new GetQueueStatusHandler(
            _matchRepo, _statusStore,
            Substitute.For<ILogger<GetQueueStatusHandler>>());
    }

    [Fact]
    public async Task Handle_ActiveMatch_ReturnsMatched()
    {
        var playerId = Guid.NewGuid();
        var match = Match.Create(Guid.NewGuid(), Guid.NewGuid(), playerId, Guid.NewGuid(), "default", DateTimeOffset.UtcNow);
        match.MarkBattleCreateRequested(DateTimeOffset.UtcNow);
        _matchRepo.GetActiveForPlayerAsync(playerId, Arg.Any<CancellationToken>()).Returns(match);

        var result = await _handler.HandleAsync(new GetQueueStatusQuery(playerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(QueueStatusType.Matched);
        result.Value.MatchId.Should().Be(match.MatchId);
        result.Value.MatchState.Should().Be(MatchState.BattleCreateRequested);
    }

    [Fact]
    public async Task Handle_Searching_ReturnsSearching()
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

        var result = await _handler.HandleAsync(new GetQueueStatusQuery(playerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(QueueStatusType.Searching);
    }

    [Fact]
    public async Task Handle_NoMatchNoQueue_ReturnsNotQueued()
    {
        var playerId = Guid.NewGuid();
        _matchRepo.GetActiveForPlayerAsync(playerId, Arg.Any<CancellationToken>()).Returns((Match?)null);
        _statusStore.GetStatusAsync(playerId, Arg.Any<CancellationToken>()).Returns((PlayerMatchStatus?)null);

        var result = await _handler.HandleAsync(new GetQueueStatusQuery(playerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(QueueStatusType.NotQueued);
    }
}
