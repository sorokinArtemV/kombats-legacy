using FluentAssertions;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Infrastructure.Redis;
using Kombats.Chat.Infrastructure.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Kombats.Chat.Infrastructure.Tests.Redis;

[Collection(RedisCollection.Name)]
public sealed class RedisPlayerInfoCacheTests(RedisFixture redisFixture)
{
    private RedisPlayerInfoCache CreateCache()
    {
        var mux = ConnectionMultiplexer.Connect(redisFixture.ConnectionString);
        return new RedisPlayerInfoCache(mux, NullLogger<RedisPlayerInfoCache>.Instance);
    }

    private async Task FlushDb()
    {
        var mux = ConnectionMultiplexer.Connect(redisFixture.AdminConnectionString);
        var server = mux.GetServers()[0];
        await server.FlushDatabaseAsync(2);
    }

    [Fact]
    public async Task SetAndGet_RoundTrip()
    {
        await FlushDb();
        var cache = CreateCache();
        var id = Guid.NewGuid();

        await cache.SetAsync(id, new CachedPlayerInfo("Alice", "Ready"), CancellationToken.None);

        var result = await cache.GetAsync(id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Alice");
        result.OnboardingState.Should().Be("Ready");
        result.IsEligible.Should().BeTrue();
    }

    [Fact]
    public async Task Get_NotFound_ReturnsNull()
    {
        await FlushDb();
        var cache = CreateCache();

        var result = await cache.GetAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Remove_DeletesEntry()
    {
        await FlushDb();
        var cache = CreateCache();
        var id = Guid.NewGuid();

        await cache.SetAsync(id, new CachedPlayerInfo("Bob", "PickingName"), CancellationToken.None);
        await cache.RemoveAsync(id, CancellationToken.None);

        var result = await cache.GetAsync(id, CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task Set_StoresOnboardingState()
    {
        await FlushDb();
        var cache = CreateCache();
        var id = Guid.NewGuid();

        await cache.SetAsync(id, new CachedPlayerInfo("Charlie", "PickingName"), CancellationToken.None);

        var result = await cache.GetAsync(id, CancellationToken.None);
        result.Should().NotBeNull();
        result!.OnboardingState.Should().Be("PickingName");
        result.IsEligible.Should().BeFalse();
    }

    [Fact]
    public async Task Get_RenewsTtl()
    {
        await FlushDb();
        var cache = CreateCache();
        var id = Guid.NewGuid();

        await cache.SetAsync(id, new CachedPlayerInfo("Dave", "Ready"), CancellationToken.None);

        // First get — should renew TTL
        var result1 = await cache.GetAsync(id, CancellationToken.None);
        result1.Should().NotBeNull();

        // Verify key still has TTL (7 days)
        var mux = ConnectionMultiplexer.Connect(redisFixture.ConnectionString);
        var db = mux.GetDatabase(2);
        var ttl = await db.KeyTimeToLiveAsync($"chat:playerinfo:{id}");
        ttl.Should().NotBeNull();
        ttl!.Value.TotalDays.Should().BeGreaterThan(6); // close to 7 days
    }
}
