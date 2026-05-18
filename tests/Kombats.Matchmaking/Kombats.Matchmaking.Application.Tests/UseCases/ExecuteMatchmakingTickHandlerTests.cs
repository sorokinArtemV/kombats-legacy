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

    /// <summary>
    /// Configures _queueStore to return the supplied pairs in order, then null forever.
    /// Also stubs profile lookups for every GUID that appears in any pair.
    /// </summary>
    private void EnqueuePairs(IReadOnlyList<(Guid, Guid)> pairs)
    {
        var queue = new Queue<(Guid, Guid)>(pairs);
        _queueStore
            .TryPopPairAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => queue.Count > 0 ? queue.Dequeue() : ((Guid, Guid)?)null);

        foreach (var (a, b) in pairs)
        {
            _profileRepo.GetByIdentityIdAsync(a, Arg.Any<CancellationToken>()).Returns(MakeProfile(a));
            _profileRepo.GetByIdentityIdAsync(b, Arg.Any<CancellationToken>()).Returns(MakeProfile(b));
        }
    }

    [Fact]
    public async Task Handle_NoPair_ReturnsZeroPairs()
    {
        _queueStore.TryPopPairAsync("default", Arg.Any<CancellationToken>())
            .Returns((ValueTuple<Guid, Guid>?)null);

        var result = await _handler.HandleAsync(new ExecuteMatchmakingTickCommand("default"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PairsCreated.Should().Be(0);
        _matchRepo.DidNotReceiveWithAnyArgs().Add(Arg.Any<Match>());
    }

    [Fact]
    public async Task Handle_PairPopped_CreatesMatchAndPublishes()
    {
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        EnqueuePairs([(playerA, playerB)]);

        var result = await _handler.HandleAsync(new ExecuteMatchmakingTickCommand("default"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PairsCreated.Should().Be(1);

        _matchRepo.Received(1).Add(Arg.Is<Match>(m =>
            m.PlayerAId == playerA &&
            m.PlayerBId == playerB &&
            m.State == MatchState.BattleCreateRequested));

        await _battlePublisher.Received(1).PublishAsync(
            Arg.Is<CreateBattleRequest>(r =>
                r.PlayerA.IdentityId == playerA &&
                r.PlayerB.IdentityId == playerB),
            Arg.Any<CancellationToken>());

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        await _statusStore.Received(1).SetMatchedAsync(playerA, Arg.Any<Guid>(), Arg.Any<Guid>(), "default", Arg.Any<CancellationToken>());
        await _statusStore.Received(1).SetMatchedAsync(playerB, Arg.Any<Guid>(), Arg.Any<Guid>(), "default", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ProfileMissing_ContinuesLoopAndRequeuesBothPlayers()
    {
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        _queueStore.TryPopPairAsync("default", Arg.Any<CancellationToken>())
            .Returns((playerA, playerB), ((Guid, Guid)?)null);
        _profileRepo.GetByIdentityIdAsync(playerA, Arg.Any<CancellationToken>()).Returns(MakeProfile(playerA));
        _profileRepo.GetByIdentityIdAsync(playerB, Arg.Any<CancellationToken>()).Returns((PlayerCombatProfile?)null);

        var result = await _handler.HandleAsync(new ExecuteMatchmakingTickCommand("default"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PairsCreated.Should().Be(0);
        _matchRepo.DidNotReceiveWithAnyArgs().Add(Arg.Any<Match>());

        // Both players must be re-queued to prevent silent loss (EI-014).
        await _queueStore.Received(1).TryRequeueAsync("default", playerA, Arg.Any<CancellationToken>());
        await _queueStore.Received(1).TryRequeueAsync("default", playerB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PairPopped_MatchStateIsBattleCreateRequested()
    {
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        EnqueuePairs([(playerA, playerB)]);

        await _handler.HandleAsync(new ExecuteMatchmakingTickCommand("default"), CancellationToken.None);

        _matchRepo.Received(1).Add(Arg.Is<Match>(m => m.State == MatchState.BattleCreateRequested));
    }

    // --- Chapter 5: bounded inner pairing loop ---

    [Fact]
    public async Task Handle_EvenQueue_DrainsToDistinctMatches()
    {
        // 8 players → 4 pairs, all GUIDs distinct, all matches distinct.
        var players = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        var pairs = new[]
        {
            (players[0], players[1]),
            (players[2], players[3]),
            (players[4], players[5]),
            (players[6], players[7]),
        };
        EnqueuePairs(pairs);

        var capturedMatches = new List<Match>();
        _matchRepo.Add(Arg.Do<Match>(capturedMatches.Add));

        var result = await _handler.HandleAsync(
            new ExecuteMatchmakingTickCommand("default", MaxPairsPerTick: 64),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PairsCreated.Should().Be(4);
        _matchRepo.Received(4).Add(Arg.Any<Match>());
        await _unitOfWork.Received(4).SaveChangesAsync(Arg.Any<CancellationToken>());

        // No player GUID appears in more than one pair.
        var playerIds = capturedMatches
            .SelectMany(m => new[] { m.PlayerAId, m.PlayerBId })
            .ToList();
        playerIds.Should().HaveCount(8).And.OnlyHaveUniqueItems();

        // No match GUID is reused.
        capturedMatches
            .Select(m => m.MatchId)
            .Should().OnlyHaveUniqueItems();
        capturedMatches
            .Select(m => m.BattleId)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Handle_QueueLargerThanCap_StopsAtCapLeavingRestQueued()
    {
        // 10 players (5 pairs) available, cap = 3. Loop should pair 3, leaving 2 pairs popped-but... no:
        // The Lua pop removes from the queue; from the handler side we model that as
        // the mock returning pairs in order. With cap=3, only 3 pops occur — the rest stay
        // in the queue store (mock's queue still has remaining pairs).
        var players = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
        var pairs = Enumerable.Range(0, 5).Select(i => (players[i * 2], players[i * 2 + 1])).ToArray();

        var queue = new Queue<(Guid, Guid)>(pairs);
        _queueStore
            .TryPopPairAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => queue.Count > 0 ? queue.Dequeue() : ((Guid, Guid)?)null);
        foreach (var (a, b) in pairs)
        {
            _profileRepo.GetByIdentityIdAsync(a, Arg.Any<CancellationToken>()).Returns(MakeProfile(a));
            _profileRepo.GetByIdentityIdAsync(b, Arg.Any<CancellationToken>()).Returns(MakeProfile(b));
        }

        var result = await _handler.HandleAsync(
            new ExecuteMatchmakingTickCommand("default", MaxPairsPerTick: 3),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PairsCreated.Should().Be(3);
        _matchRepo.Received(3).Add(Arg.Any<Match>());

        // Exactly 3 pops occurred; 2 pairs remain in the mock queue (not popped).
        await _queueStore.Received(3).TryPopPairAsync("default", Arg.Any<CancellationToken>());
        queue.Count.Should().Be(2);
    }

    [Fact]
    public async Task Handle_LonePlayer_ReturnsZeroPairsAndDoesNotSelfPair()
    {
        // Lua's TryPopPair returns null when only one player remains (it requeues that
        // player to the tail itself — recon Q1). The handler must NOT attempt requeue
        // on the no-pair path, and must NOT pair a player with itself.
        _queueStore.TryPopPairAsync("default", Arg.Any<CancellationToken>())
            .Returns((ValueTuple<Guid, Guid>?)null);

        var result = await _handler.HandleAsync(
            new ExecuteMatchmakingTickCommand("default"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PairsCreated.Should().Be(0);

        _matchRepo.DidNotReceiveWithAnyArgs().Add(Arg.Any<Match>());
        await _queueStore.DidNotReceiveWithAnyArgs().TryRequeueAsync(default!, default, default);
        await _battlePublisher.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
    }

    [Theory]
    [InlineData(0)]  // truly empty queue
    [InlineData(1)]  // lone player — Lua returns null, requeues internally
    public async Task Handle_IdleCases_ReturnZeroPairs_TriggersWorkerSleepPath(int simulatedQueueDepth)
    {
        // Whether the queue is empty (depth 0) or has a single lone player (depth 1),
        // the Lua pop returns null. The handler returns PairsCreated = 0, which is the
        // signal the worker uses to take the idle backoff (sleep) path.
        _queueStore.TryPopPairAsync("default", Arg.Any<CancellationToken>())
            .Returns((ValueTuple<Guid, Guid>?)null);

        var result = await _handler.HandleAsync(
            new ExecuteMatchmakingTickCommand("default"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PairsCreated.Should().Be(0);
        _ = simulatedQueueDepth; // documents the two distinct idle conditions under test
    }

    [Fact]
    public async Task Handle_CancellationMidLoop_StopsAtNextIterationWithoutThrowing()
    {
        // Pair 1 succeeds; the SetMatchedAsync stub cancels the CTS just after pair 1 is
        // recorded. The loop must observe the cancelled token at the top of iteration 2
        // and exit cleanly, without an OperationCanceledException propagating out of
        // HandleAsync. PairsCreated is the count actually committed (1).
        using var cts = new CancellationTokenSource();
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        var playerC = Guid.NewGuid();
        var playerD = Guid.NewGuid();

        EnqueuePairs([(playerA, playerB), (playerC, playerD)]);

        _statusStore
            .SetMatchedAsync(playerB, Arg.Any<Guid>(), Arg.Any<Guid>(), "default", Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => cts.Cancel());

        // If the loop let cancellation escape, this await would throw OperationCanceledException
        // and the test would fail with that exception. We assert no throw by reaching the next line.
        var result = await _handler.HandleAsync(
            new ExecuteMatchmakingTickCommand("default", MaxPairsPerTick: 64),
            cts.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.PairsCreated.Should().Be(1);

        // Pair 2 never popped: cancellation observed at the top of iteration 2.
        await _queueStore.Received(1).TryPopPairAsync("default", Arg.Any<CancellationToken>());
        _matchRepo.Received(1).Add(Arg.Any<Match>());
    }
}
