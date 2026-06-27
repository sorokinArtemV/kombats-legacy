using FluentAssertions;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Application.UseCases.JoinQueue;
using Kombats.Matchmaking.Domain;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Kombats.Matchmaking.Application.Tests.UseCases;

public sealed class JoinQueueHandlerTests
{
    private readonly IMatchRepository _matchRepo = Substitute.For<IMatchRepository>();
    private readonly IPlayerCombatProfileRepository _profileRepo = Substitute.For<IPlayerCombatProfileRepository>();
    private readonly IMatchQueueStore _queueStore = Substitute.For<IMatchQueueStore>();
    private readonly IPlayerMatchStatusStore _statusStore = Substitute.For<IPlayerMatchStatusStore>();
    private readonly IQueuePresenceStore _presenceStore = Substitute.For<IQueuePresenceStore>();
    private readonly JoinQueueHandler _handler;

    public JoinQueueHandlerTests()
    {
        _handler = new JoinQueueHandler(
            _matchRepo, _profileRepo, _queueStore, _statusStore, _presenceStore,
            Substitute.For<ILogger<JoinQueueHandler>>());
    }

    private static PlayerCombatProfile ReadyProfile(Guid playerId) => new()
    {
        IdentityId = playerId,
        CharacterId = Guid.NewGuid(),
        Name = "Test",
        Level = 1,
        Strength = 5,
        Agility = 5,
        Intuition = 5,
        Vitality = 5,
        IsReady = true,
        Revision = 1,
        OccurredAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_NoActiveMatch_ReadyProfile_JoinsQueue()
    {
        var playerId = Guid.NewGuid();
        _matchRepo.GetActiveForPlayerAsync(playerId, Arg.Any<CancellationToken>()).Returns((Match?)null);
        _profileRepo.GetByIdentityIdAsync(playerId, Arg.Any<CancellationToken>()).Returns(ReadyProfile(playerId));

        var result = await _handler.HandleAsync(new JoinQueueCommand(playerId, "default", "test-conn-ref"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(QueuePlayerStatus.Searching);
        await _queueStore.Received(1).TryJoinQueueAsync("default", playerId, Arg.Any<CancellationToken>());
        await _statusStore.Received(1).SetSearchingAsync("default", playerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ActiveMatch_ReturnsAlreadyMatched()
    {
        var playerId = Guid.NewGuid();
        var match = Match.Create(Guid.NewGuid(), Guid.NewGuid(), playerId, Guid.NewGuid(), "default", DateTimeOffset.UtcNow);
        match.MarkBattleCreateRequested(DateTimeOffset.UtcNow);
        _matchRepo.GetActiveForPlayerAsync(playerId, Arg.Any<CancellationToken>()).Returns(match);

        var result = await _handler.HandleAsync(new JoinQueueCommand(playerId, "default", "test-conn-ref"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(QueuePlayerStatus.AlreadyMatched);
        result.Value.MatchId.Should().Be(match.MatchId);
        await _queueStore.DidNotReceiveWithAnyArgs().TryJoinQueueAsync(default!, default, default);
    }

    [Fact]
    public async Task Handle_NoCombatProfile_ReturnsValidationError()
    {
        var playerId = Guid.NewGuid();
        _matchRepo.GetActiveForPlayerAsync(playerId, Arg.Any<CancellationToken>()).Returns((Match?)null);
        _profileRepo.GetByIdentityIdAsync(playerId, Arg.Any<CancellationToken>()).Returns((PlayerCombatProfile?)null);

        var result = await _handler.HandleAsync(new JoinQueueCommand(playerId, "default", "test-conn-ref"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Queue.NoCombatProfile");
    }

    [Fact]
    public async Task Handle_ProfileNotReady_ReturnsValidationError()
    {
        var playerId = Guid.NewGuid();
        var profile = new PlayerCombatProfile
        {
            IdentityId = playerId,
            CharacterId = Guid.NewGuid(),
            Name = "Test",
            Level = 1,
            Strength = 5,
            Agility = 5,
            Intuition = 5,
            Vitality = 5,
            IsReady = false,
            Revision = 1,
            OccurredAt = DateTimeOffset.UtcNow
        };
        _matchRepo.GetActiveForPlayerAsync(playerId, Arg.Any<CancellationToken>()).Returns((Match?)null);
        _profileRepo.GetByIdentityIdAsync(playerId, Arg.Any<CancellationToken>()).Returns(profile);

        var result = await _handler.HandleAsync(new JoinQueueCommand(playerId, "default", "test-conn-ref"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Queue.NotReady");
    }
}
