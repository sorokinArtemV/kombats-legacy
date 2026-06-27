using Testcontainers.Redis;
using Xunit;

namespace Kombats.Chat.Infrastructure.Tests.Fixtures;

/// <summary>
/// Shared Redis Testcontainers fixture for Chat infrastructure tests.
/// Redis operations (presence, rate limiting) will be added in later batches.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private RedisContainer _redis = null!;

    public string ConnectionString => _redis.GetConnectionString();

    public string AdminConnectionString => _redis.GetConnectionString() + ",allowAdmin=true";

    public async Task InitializeAsync()
    {
        _redis = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await _redis.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _redis.DisposeAsync();
    }

    public Task PauseAsync() => _redis.PauseAsync();

    public Task UnpauseAsync() => _redis.UnpauseAsync();
}

[CollectionDefinition(Name)]
public class RedisCollection : ICollectionFixture<RedisFixture>
{
    public const string Name = "Redis";
}
