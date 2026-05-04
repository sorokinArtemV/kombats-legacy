using FluentAssertions;
using Kombats.Matchmaking.Infrastructure.Options;
using Kombats.Matchmaking.Infrastructure.Redis;
using Kombats.Matchmaking.Infrastructure.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;
using Xunit;

namespace Kombats.Matchmaking.Infrastructure.Tests;

[Collection(RedisCollection.Name)]
public sealed class RedisMatchQueueStoreTests : IAsyncLifetime
{
    private readonly RedisFixture _fixture;
    private readonly RedisMatchQueueStore _store;

    public RedisMatchQueueStoreTests(RedisFixture fixture)
    {
        _fixture = fixture;
        var options = MsOptions.Create(new MatchmakingRedisOptions
        {
            DatabaseIndex = 1,
            StatusTtlSeconds = 1800,
            CancelTtlSeconds = 600
        });
        _store = new RedisMatchQueueStore(
            fixture.Connection,
            NullLogger<RedisMatchQueueStore>.Instance,
            options);
    }

    public Task InitializeAsync() => _fixture.FlushDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TryJoinQueue_NewPlayer_ReturnsTrue()
    {
        var playerId = Guid.NewGuid();
        var result = await _store.TryJoinQueueAsync("default", playerId);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryJoinQueue_SamePlayerTwice_ReturnsFalseSecondTime()
    {
        var playerId = Guid.NewGuid();
        await _store.TryJoinQueueAsync("default", playerId);

        var result = await _store.TryJoinQueueAsync("default", playerId);
        result.Should().BeFalse("player is already in queue");
    }

    [Fact]
    public async Task TryJoinQueue_DifferentPlayers_BothSucceed()
    {
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();

        (await _store.TryJoinQueueAsync("default", playerA)).Should().BeTrue();
        (await _store.TryJoinQueueAsync("default", playerB)).Should().BeTrue();
    }

    [Fact]
    public async Task TryLeaveQueue_PlayerInQueue_ReturnsTrue()
    {
        var playerId = Guid.NewGuid();
        await _store.TryJoinQueueAsync("default", playerId);

        var result = await _store.TryLeaveQueueAsync("default", playerId);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryLeaveQueue_PlayerNotInQueue_ReturnsFalse()
    {
        var result = await _store.TryLeaveQueueAsync("default", Guid.NewGuid());
        result.Should().BeFalse();
    }

    [Fact]
    public async Task TryLeaveQueue_ThenRejoin_Succeeds()
    {
        var playerId = Guid.NewGuid();
        await _store.TryJoinQueueAsync("default", playerId);
        await _store.TryLeaveQueueAsync("default", playerId);

        var result = await _store.TryJoinQueueAsync("default", playerId);
        result.Should().BeTrue("player should be able to rejoin after leaving");
    }

    [Fact]
    public async Task TryPopPair_TwoPlayersInQueue_ReturnsBoth()
    {
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        await _store.TryJoinQueueAsync("default", playerA);
        await _store.TryJoinQueueAsync("default", playerB);

        var pair = await _store.TryPopPairAsync("default");

        pair.Should().NotBeNull();
        var (poppedA, poppedB) = pair!.Value;
        // FIFO order: first joined is first popped
        poppedA.Should().Be(playerA);
        poppedB.Should().Be(playerB);
    }

    [Fact]
    public async Task TryPopPair_OnlyOnePlayer_ReturnsNull()
    {
        await _store.TryJoinQueueAsync("default", Guid.NewGuid());

        var pair = await _store.TryPopPairAsync("default");
        pair.Should().BeNull("need at least two players to form a pair");
    }

    [Fact]
    public async Task TryPopPair_EmptyQueue_ReturnsNull()
    {
        var pair = await _store.TryPopPairAsync("default");
        pair.Should().BeNull();
    }

    [Fact]
    public async Task TryPopPair_SkipsCanceledPlayers()
    {
        var canceled = Guid.NewGuid();
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();

        await _store.TryJoinQueueAsync("default", canceled);
        await _store.TryJoinQueueAsync("default", playerA);
        await _store.TryJoinQueueAsync("default", playerB);

        // Cancel the first player
        await _store.TryLeaveQueueAsync("default", canceled);

        var pair = await _store.TryPopPairAsync("default");

        pair.Should().NotBeNull();
        var (poppedA, poppedB) = pair!.Value;
        poppedA.Should().Be(playerA);
        poppedB.Should().Be(playerB);
    }

    [Fact]
    public async Task TryPopPair_RemovedPlayersCannotBePopped()
    {
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        await _store.TryJoinQueueAsync("default", playerA);
        await _store.TryJoinQueueAsync("default", playerB);

        // Pop them
        var pair = await _store.TryPopPairAsync("default");
        pair.Should().NotBeNull();

        // Queue should now be empty
        var secondPair = await _store.TryPopPairAsync("default");
        secondPair.Should().BeNull();
    }

    [Fact]
    public async Task TryRequeue_RestoresPlayerToQueueHead()
    {
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        var playerC = Guid.NewGuid();

        await _store.TryJoinQueueAsync("default", playerB);
        await _store.TryJoinQueueAsync("default", playerC);

        // Requeue playerA to head
        await _store.TryRequeueAsync("default", playerA);

        var pair = await _store.TryPopPairAsync("default");
        pair.Should().NotBeNull();
        var (first, second) = pair!.Value;
        // playerA was LPUSH'd so should be first (head of queue)
        first.Should().Be(playerA);
        second.Should().Be(playerB);
    }

    [Fact]
    public async Task TryRequeue_Idempotent_DoesNotDuplicate()
    {
        var playerId = Guid.NewGuid();
        var other = Guid.NewGuid();

        await _store.TryRequeueAsync("default", playerId);
        await _store.TryRequeueAsync("default", playerId); // second call should be no-op
        await _store.TryJoinQueueAsync("default", other);

        var pair = await _store.TryPopPairAsync("default");
        pair.Should().NotBeNull();
        var (first, second) = pair!.Value;
        first.Should().Be(playerId);
        second.Should().Be(other);

        // Queue should be empty now (no duplicate of playerId)
        var nextPair = await _store.TryPopPairAsync("default");
        nextPair.Should().BeNull();
    }
}
