using FluentAssertions;
using Kombats.Chat.Infrastructure.Redis;
using Kombats.Chat.Infrastructure.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Kombats.Chat.Infrastructure.Tests.Redis;

[Collection(RedisCollection.Name)]
public sealed class RedisRateLimiterTests(RedisFixture redisFixture)
{
    private RedisRateLimiter CreateLimiter()
    {
        var mux = ConnectionMultiplexer.Connect(redisFixture.ConnectionString);
        return new RedisRateLimiter(mux, NullLogger<RedisRateLimiter>.Instance);
    }

    private async Task FlushDb()
    {
        var mux = ConnectionMultiplexer.Connect(redisFixture.AdminConnectionString);
        var server = mux.GetServers()[0];
        await server.FlushDatabaseAsync(2);
    }

    [Fact]
    public async Task UnderLimit_Allowed()
    {
        await FlushDb();
        var limiter = CreateLimiter();
        var id = Guid.NewGuid();

        var result = await limiter.CheckAndIncrementAsync(id, "global", CancellationToken.None);

        result.Allowed.Should().BeTrue();
        result.RetryAfterMs.Should().BeNull();
    }

    [Fact]
    public async Task AtLimit_Denied()
    {
        await FlushDb();
        var limiter = CreateLimiter();
        var id = Guid.NewGuid();

        // Global limit is 5 per 10s
        for (int i = 0; i < 5; i++)
        {
            var r = await limiter.CheckAndIncrementAsync(id, "global", CancellationToken.None);
            r.Allowed.Should().BeTrue();
        }

        var result = await limiter.CheckAndIncrementAsync(id, "global", CancellationToken.None);

        result.Allowed.Should().BeFalse();
        result.RetryAfterMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DifferentSurfaces_IndependentLimits()
    {
        await FlushDb();
        var limiter = CreateLimiter();
        var id = Guid.NewGuid();

        // Exhaust global limit
        for (int i = 0; i < 5; i++)
            await limiter.CheckAndIncrementAsync(id, "global", CancellationToken.None);

        var globalResult = await limiter.CheckAndIncrementAsync(id, "global", CancellationToken.None);
        var dmResult = await limiter.CheckAndIncrementAsync(id, "dm", CancellationToken.None);

        globalResult.Allowed.Should().BeFalse();
        dmResult.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task DifferentUsers_IndependentLimits()
    {
        await FlushDb();
        var limiter = CreateLimiter();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        // Exhaust limit for id1
        for (int i = 0; i < 5; i++)
            await limiter.CheckAndIncrementAsync(id1, "global", CancellationToken.None);

        var result1 = await limiter.CheckAndIncrementAsync(id1, "global", CancellationToken.None);
        var result2 = await limiter.CheckAndIncrementAsync(id2, "global", CancellationToken.None);

        result1.Allowed.Should().BeFalse();
        result2.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownSurface_AlwaysAllowed()
    {
        await FlushDb();
        var limiter = CreateLimiter();
        var id = Guid.NewGuid();

        var result = await limiter.CheckAndIncrementAsync(id, "nonexistent", CancellationToken.None);

        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Fallback_ActivatesOnRedisOutage_And_RecoversWhenRedisReturns()
    {
        await FlushDb();

        // Short timeouts so the outage window fails fast into the fallback branch.
        string cs = redisFixture.ConnectionString + ",connectTimeout=800,syncTimeout=800,abortConnect=false";
        var mux = await ConnectionMultiplexer.ConnectAsync(cs);
        try
        {
            var limiter = new RedisRateLimiter(mux, NullLogger<RedisRateLimiter>.Instance);
            var id = Guid.NewGuid();

            // Baseline: Redis up — allowed, distributed path.
            (await limiter.CheckAndIncrementAsync(id, "global", CancellationToken.None))
                .Allowed.Should().BeTrue();

            // Simulate Redis outage.
            await redisFixture.PauseAsync();
            try
            {
                // Fallback path must still serve requests. The global surface allows 5 per window;
                // the fallback keyspace is independent of Redis, so the first 5 calls succeed
                // even if the Redis counter already recorded one above.
                for (int i = 0; i < 5; i++)
                {
                    var r = await limiter.CheckAndIncrementAsync(id, "global", CancellationToken.None);
                    r.Allowed.Should().BeTrue($"fallback call #{i + 1} should be allowed");
                }

                // Sixth call exceeds the fallback window limit.
                var denied = await limiter.CheckAndIncrementAsync(id, "global", CancellationToken.None);
                denied.Allowed.Should().BeFalse();
                denied.RetryAfterMs.Should().BeGreaterThan(0);
            }
            finally
            {
                // Restore Redis before leaving the test even if an assertion fails.
                await redisFixture.UnpauseAsync();
            }

            // Give the multiplexer a moment to re-establish after unpause.
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline && !mux.IsConnected)
                await Task.Delay(100);

            // Recovery: next call must go through Redis again (usingFallback flag flips back).
            // Using a fresh identity ensures we are observing Redis state, not lingering fallback state.
            var freshId = Guid.NewGuid();
            var recovered = await limiter.CheckAndIncrementAsync(freshId, "global", CancellationToken.None);
            recovered.Allowed.Should().BeTrue();

            // Verify the distributed counter in Redis actually incremented — proves we hit Redis,
            // not the in-memory fallback, after recovery.
            var db = mux.GetDatabase(2);
            var counter = await db.StringGetAsync($"chat:ratelimit:{freshId}:global");
            counter.HasValue.Should().BeTrue("recovered calls must write to Redis, not the in-memory fallback");
            ((long)counter).Should().Be(1);
        }
        finally
        {
            await mux.CloseAsync();
            mux.Dispose();
        }
    }

    [Fact]
    public async Task PresenceLimit_OnePer5Seconds()
    {
        await FlushDb();
        var limiter = CreateLimiter();
        var id = Guid.NewGuid();

        var first = await limiter.CheckAndIncrementAsync(id, "presence", CancellationToken.None);
        var second = await limiter.CheckAndIncrementAsync(id, "presence", CancellationToken.None);

        first.Allowed.Should().BeTrue();
        second.Allowed.Should().BeFalse();
        second.RetryAfterMs.Should().BeGreaterThan(0);
    }
}
