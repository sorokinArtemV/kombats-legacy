using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Kombats.Battle.Infrastructure.Tests.Fixtures;

public sealed class RedisFixture : IAsyncLifetime
{
    private RedisContainer _redis = null!;

    public string ConnectionString => _redis.GetConnectionString();
    public IConnectionMultiplexer Connection { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _redis = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
        await _redis.StartAsync();
        Connection = await ConnectionMultiplexer.ConnectAsync($"{ConnectionString},allowAdmin=true");
    }

    public async Task DisposeAsync()
    {
        Connection.Dispose();
        await _redis.DisposeAsync();
    }

    /// <summary>
    /// Flushes the current database to isolate tests.
    /// Call this in test constructor or setup for clean state.
    /// </summary>
    public async Task FlushAsync()
    {
        var server = Connection.GetServers()[0];
        await server.FlushDatabaseAsync();
    }
}

[CollectionDefinition(Name)]
public class RedisCollection : ICollectionFixture<RedisFixture>
{
    public const string Name = "Redis";
}
