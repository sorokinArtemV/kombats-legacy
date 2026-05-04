using System.Diagnostics;
using FluentAssertions;
using Kombats.Bff.Application.Clients;
using Kombats.Bff.Application.Composition;
using Kombats.Bff.Application.Errors;
using Kombats.Bff.Application.Models.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Kombats.Bff.Application.Tests;

public sealed class GameStateComposerTests
{
    private readonly IPlayersClient _playersClient = Substitute.For<IPlayersClient>();
    private readonly IMatchmakingClient _matchmakingClient = Substitute.For<IMatchmakingClient>();
    private readonly GameStateComposer _composer;

    public GameStateComposerTests()
    {
        _composer = new GameStateComposer(
            _playersClient,
            _matchmakingClient,
            NullLogger<GameStateComposer>.Instance);
    }

    [Fact]
    public async Task ComposeAsync_BothServicesSucceed_ReturnsFullResponse()
    {
        var character = CreateTestCharacter();
        var queueStatus = new InternalQueueStatusResponse("Searching");

        _playersClient.GetCharacterAsync(Arg.Any<CancellationToken>())
            .Returns(character);
        _matchmakingClient.GetQueueStatusAsync(Arg.Any<CancellationToken>())
            .Returns(queueStatus);

        GameStateResult result = await _composer.ComposeAsync(CancellationToken.None);

        result.IsBothUnavailable.Should().BeFalse();
        result.Character.Should().NotBeNull();
        result.Character!.Name.Should().Be("TestHero");
        result.QueueStatus.Should().NotBeNull();
        result.QueueStatus!.Status.Should().Be("Searching");
        result.IsCharacterCreated.Should().BeTrue();
        result.DegradedServices.Should().BeNull();
    }

    [Fact]
    public async Task ComposeAsync_PlayersReturnsNull_IsCharacterCreatedFalse()
    {
        _playersClient.GetCharacterAsync(Arg.Any<CancellationToken>())
            .Returns((InternalCharacterResponse?)null);
        _matchmakingClient.GetQueueStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new InternalQueueStatusResponse("NotQueued"));

        GameStateResult result = await _composer.ComposeAsync(CancellationToken.None);

        result.IsBothUnavailable.Should().BeFalse();
        result.Character.Should().BeNull();
        result.IsCharacterCreated.Should().BeFalse();
        result.QueueStatus.Should().NotBeNull();
        result.DegradedServices.Should().BeNull();
    }

    [Fact]
    public async Task ComposeAsync_PlayersUnavailable_ReturnsPartialWithDegradation()
    {
        _playersClient.GetCharacterAsync(Arg.Any<CancellationToken>())
            .Throws(new ServiceUnavailableException("Players"));
        _matchmakingClient.GetQueueStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new InternalQueueStatusResponse("Searching"));

        GameStateResult result = await _composer.ComposeAsync(CancellationToken.None);

        result.IsBothUnavailable.Should().BeFalse();
        result.Character.Should().BeNull();
        result.IsCharacterCreated.Should().BeFalse();
        result.QueueStatus.Should().NotBeNull();
        result.QueueStatus!.Status.Should().Be("Searching");
        result.DegradedServices.Should().NotBeNull();
        result.DegradedServices.Should().Contain("Players");
        result.DegradedServices.Should().NotContain("Matchmaking");
    }

    [Fact]
    public async Task ComposeAsync_MatchmakingUnavailable_ReturnsPartialWithDegradation()
    {
        var character = CreateTestCharacter();
        _playersClient.GetCharacterAsync(Arg.Any<CancellationToken>())
            .Returns(character);
        _matchmakingClient.GetQueueStatusAsync(Arg.Any<CancellationToken>())
            .Throws(new ServiceUnavailableException("Matchmaking"));

        GameStateResult result = await _composer.ComposeAsync(CancellationToken.None);

        result.IsBothUnavailable.Should().BeFalse();
        result.Character.Should().NotBeNull();
        result.Character!.Name.Should().Be("TestHero");
        result.IsCharacterCreated.Should().BeTrue();
        result.QueueStatus.Should().BeNull();
        result.DegradedServices.Should().NotBeNull();
        result.DegradedServices.Should().Contain("Matchmaking");
        result.DegradedServices.Should().NotContain("Players");
    }

    [Fact]
    public async Task ComposeAsync_BothUnavailable_ReturnsBothUnavailableResult()
    {
        _playersClient.GetCharacterAsync(Arg.Any<CancellationToken>())
            .Throws(new ServiceUnavailableException("Players"));
        _matchmakingClient.GetQueueStatusAsync(Arg.Any<CancellationToken>())
            .Throws(new ServiceUnavailableException("Matchmaking"));

        GameStateResult result = await _composer.ComposeAsync(CancellationToken.None);

        result.IsBothUnavailable.Should().BeTrue();
        result.Character.Should().BeNull();
        result.QueueStatus.Should().BeNull();
    }

    [Fact]
    public async Task ComposeAsync_CallsClientsInParallel()
    {
        var playersDelay = TimeSpan.FromMilliseconds(200);
        var matchmakingDelay = TimeSpan.FromMilliseconds(200);

        _playersClient.GetCharacterAsync(Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await Task.Delay(playersDelay, callInfo.Arg<CancellationToken>());
                return (InternalCharacterResponse?)CreateTestCharacter();
            });

        _matchmakingClient.GetQueueStatusAsync(Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await Task.Delay(matchmakingDelay, callInfo.Arg<CancellationToken>());
                return (InternalQueueStatusResponse?)new InternalQueueStatusResponse("NotQueued");
            });

        var stopwatch = Stopwatch.StartNew();
        GameStateResult result = await _composer.ComposeAsync(CancellationToken.None);
        stopwatch.Stop();

        result.IsBothUnavailable.Should().BeFalse();
        result.Character.Should().NotBeNull();
        result.QueueStatus.Should().NotBeNull();

        // If calls were sequential, elapsed would be >= 400ms.
        // Parallel execution should complete in ~200ms (+ overhead).
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(350,
            "calls should execute in parallel, not sequentially");
    }

    [Fact]
    public async Task ComposeAsync_MatchedWithBattleId_IncludesBattleIdInQueueStatus()
    {
        var character = CreateTestCharacter();
        var battleId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var queueStatus = new InternalQueueStatusResponse(
            "Matched", matchId, battleId, "BattleCreated");

        _playersClient.GetCharacterAsync(Arg.Any<CancellationToken>())
            .Returns(character);
        _matchmakingClient.GetQueueStatusAsync(Arg.Any<CancellationToken>())
            .Returns(queueStatus);

        GameStateResult result = await _composer.ComposeAsync(CancellationToken.None);

        result.QueueStatus.Should().NotBeNull();
        result.QueueStatus!.BattleId.Should().Be(battleId);
        result.QueueStatus.MatchId.Should().Be(matchId);
        result.QueueStatus.MatchState.Should().Be("BattleCreated");
    }

    [Fact]
    public async Task ComposeAsync_DoesNotCallAnyBattleService()
    {
        // GameStateComposer should only call Players + Matchmaking.
        // No battle service interaction — live battle state is SignalR-only (BFF-3).
        _playersClient.GetCharacterAsync(Arg.Any<CancellationToken>())
            .Returns(CreateTestCharacter());
        _matchmakingClient.GetQueueStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new InternalQueueStatusResponse("NotQueued"));

        await _composer.ComposeAsync(CancellationToken.None);

        // Verify only Players and Matchmaking clients were called
        await _playersClient.Received(1).GetCharacterAsync(Arg.Any<CancellationToken>());
        await _matchmakingClient.Received(1).GetQueueStatusAsync(Arg.Any<CancellationToken>());
    }

    private static InternalCharacterResponse CreateTestCharacter() => new(
        CharacterId: Guid.NewGuid(),
        IdentityId: Guid.NewGuid(),
        OnboardingState: 2,
        Name: "TestHero",
        Strength: 5,
        Agility: 3,
        Intuition: 2,
        Vitality: 4,
        UnspentPoints: 0,
        Revision: 1,
        TotalXp: 100,
        Level: 2,
        LevelingVersion: 1,
        AvatarId: "default");
}
