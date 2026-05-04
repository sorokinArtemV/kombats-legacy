using FluentAssertions;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Application.UseCases.ExecuteMatchmakingTick;
using Kombats.Matchmaking.Domain;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Kombats.Matchmaking.Application.Tests.UseCases;

public sealed class ExecuteMatchmakingTickHandlerTests
{
    private readonly IMatchQueueStore _queueStore = Substitute.For<IMatchQueueStore>();
    private readonly IMatchRepository _matchRepo = Substitute.For<IMatchRepository>();
    private readonly IPlayerCombatProfileRepository _profileRepo = Substitute.For<IPlayerCombatProfileRepository>();
    private readonly ICreateBattlePublisher _battlePublisher = Substitute.For<ICreateBattlePublisher>();
    private readonly IPlayerMatchStatusStore _statusStore = Substitute.For<IPlayerMatchStatusStore>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ExecuteMatchmakingTickHandler _handler;

    public ExecuteMatchmakingTickHandlerTests()
    {
        _handler = new ExecuteMatchmakingTickHandler(
            _queueStore, _matchRepo, _profileRepo, _battlePublisher, _statusStore, _unitOfWork,
            Substitute.For<ILogger<ExecuteMatchmakingTickHandler>>());
    }

    private static PlayerCombatProfile MakeProfile(Guid id) => new()
    {
        IdentityId = id,
        CharacterId = Guid.NewGuid(),
        Name = "Test",
        Level = 5,
        Strength = 10,
        Agility = 8,
        Intuition = 6,
        Vitality = 12,
        IsReady = true,
        Revision = 1,
        OccurredAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_NoPair_ReturnsNoMatch()
    {
        _queueStore.TryPopPairAsync("default", Arg.Any<CancellationToken>())
            .Returns((ValueTuple<Guid, Guid>?)null);

        var result = await _handler.HandleAsync(new ExecuteMatchmakingTickCommand("default"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.MatchCreated.Should().BeFalse();
        _matchRepo.DidNotReceiveWithAnyArgs().Add(Arg.Any<Match>());
    }

    [Fact]
    public async Task Handle_PairPopped_CreatesMatchAndPublishes()
    {
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        _queueStore.TryPopPairAsync("default", Arg.Any<CancellationToken>())
            .Returns((playerA, playerB));
        _profileRepo.GetByIdentityIdAsync(playerA, Arg.Any<CancellationToken>()).Returns(MakeProfile(playerA));
        _profileRepo.GetByIdentityIdAsync(playerB, Arg.Any<CancellationToken>()).Returns(MakeProfile(playerB));

        var result = await _handler.HandleAsync(new ExecuteMatchmakingTickCommand("default"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.MatchCreated.Should().BeTrue();
        result.Value.MatchId.Should().NotBeNull();
        result.Value.BattleId.Should().NotBeNull();

        // Verify match was added
        _matchRepo.Received(1).Add(Arg.Is<Match>(m =>
            m.PlayerAId == playerA &&
            m.PlayerBId == playerB &&
            m.State == MatchState.BattleCreateRequested));

        // Verify CreateBattle was published
        await _battlePublisher.Received(1).PublishAsync(
            Arg.Is<CreateBattleRequest>(r =>
                r.PlayerA.IdentityId == playerA &&
                r.PlayerB.IdentityId == playerB),
            Arg.Any<CancellationToken>());

        // Verify UoW saved
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // Verify Redis status updated for both players
        await _statusStore.Received(1).SetMatchedAsync(playerA, Arg.Any<Guid>(), Arg.Any<Guid>(), "default", Arg.Any<CancellationToken>());
        await _statusStore.Received(1).SetMatchedAsync(playerB, Arg.Any<Guid>(), Arg.Any<Guid>(), "default", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ProfileMissing_ReturnsNoMatchAndRequeuesBothPlayers()
    {
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        _queueStore.TryPopPairAsync("default", Arg.Any<CancellationToken>())
            .Returns((playerA, playerB));
        _profileRepo.GetByIdentityIdAsync(playerA, Arg.Any<CancellationToken>()).Returns(MakeProfile(playerA));
        _profileRepo.GetByIdentityIdAsync(playerB, Arg.Any<CancellationToken>()).Returns((PlayerCombatProfile?)null);

        var result = await _handler.HandleAsync(new ExecuteMatchmakingTickCommand("default"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.MatchCreated.Should().BeFalse();
        _matchRepo.DidNotReceiveWithAnyArgs().Add(Arg.Any<Match>());

        // Both players must be re-queued to prevent silent loss (EI-014)
        await _queueStore.Received(1).TryRequeueAsync("default", playerA, Arg.Any<CancellationToken>());
        await _queueStore.Received(1).TryRequeueAsync("default", playerB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PairPopped_MatchStateIsBattleCreateRequested()
    {
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        _queueStore.TryPopPairAsync("default", Arg.Any<CancellationToken>())
            .Returns((playerA, playerB));
        _profileRepo.GetByIdentityIdAsync(playerA, Arg.Any<CancellationToken>()).Returns(MakeProfile(playerA));
        _profileRepo.GetByIdentityIdAsync(playerB, Arg.Any<CancellationToken>()).Returns(MakeProfile(playerB));

        await _handler.HandleAsync(new ExecuteMatchmakingTickCommand("default"), CancellationToken.None);

        // The match should be in BattleCreateRequested state (Queued -> BattleCreateRequested in handler)
        _matchRepo.Received(1).Add(Arg.Is<Match>(m => m.State == MatchState.BattleCreateRequested));
    }
}
