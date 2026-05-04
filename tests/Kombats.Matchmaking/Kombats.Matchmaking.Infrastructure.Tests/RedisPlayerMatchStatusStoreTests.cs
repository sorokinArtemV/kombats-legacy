using FluentAssertions;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Infrastructure.Options;
using Kombats.Matchmaking.Infrastructure.Redis;
using Kombats.Matchmaking.Infrastructure.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;
using Xunit;

namespace Kombats.Matchmaking.Infrastructure.Tests;

[Collection(RedisCollection.Name)]
public sealed class RedisPlayerMatchStatusStoreTests : IAsyncLifetime
{
    private readonly RedisFixture _fixture;
    private readonly RedisPlayerMatchStatusStore _store;

    public RedisPlayerMatchStatusStoreTests(RedisFixture fixture)
    {
        _fixture = fixture;
        var options = MsOptions.Create(new MatchmakingRedisOptions
        {
            DatabaseIndex = 1,
            StatusTtlSeconds = 1800,
            CancelTtlSeconds = 600
        });
        _store = new RedisPlayerMatchStatusStore(
            fixture.Connection,
            NullLogger<RedisPlayerMatchStatusStore>.Instance,
            options);
    }

    public Task InitializeAsync() => _fixture.FlushDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetStatus_NoEntry_ReturnsNull()
    {
        var result = await _store.GetStatusAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetSearching_ThenGet_ReturnsSearchingStatus()
    {
        var playerId = Guid.NewGuid();
        await _store.SetSearchingAsync("default", playerId);

        var status = await _store.GetStatusAsync(playerId);

        status.Should().NotBeNull();
        status!.State.Should().Be(PlayerMatchState.Searching);
        status.Variant.Should().Be("default");
        status.MatchId.Should().BeNull();
        status.BattleId.Should().BeNull();
    }

    [Fact]
    public async Task SetMatched_ThenGet_ReturnsMatchedStatusWithIds()
    {
        var playerId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();

        await _store.SetMatchedAsync(playerId, matchId, battleId, "ranked");

        var status = await _store.GetStatusAsync(playerId);

        status.Should().NotBeNull();
        status!.State.Should().Be(PlayerMatchState.Matched);
        status.MatchId.Should().Be(matchId);
        status.BattleId.Should().Be(battleId);
        status.Variant.Should().Be("ranked");
    }

    [Fact]
    public async Task RemoveStatus_ClearsEntry()
    {
        var playerId = Guid.NewGuid();
        await _store.SetSearchingAsync("default", playerId);

        await _store.RemoveStatusAsync(playerId);

        var status = await _store.GetStatusAsync(playerId);
        status.Should().BeNull();
    }

    [Fact]
    public async Task RemoveStatus_NonExistentPlayer_DoesNotThrow()
    {
        var act = () => _store.RemoveStatusAsync(Guid.NewGuid());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetMatched_OverwritesSearching()
    {
        var playerId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();

        await _store.SetSearchingAsync("default", playerId);
        await _store.SetMatchedAsync(playerId, matchId, battleId, "default");

        var status = await _store.GetStatusAsync(playerId);
        status.Should().NotBeNull();
        status!.State.Should().Be(PlayerMatchState.Matched);
        status.MatchId.Should().Be(matchId);
    }
}
