using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Kombats.Matchmaking.Infrastructure.Tests.Fixtures;

public sealed class RedisFixture : IAsyncLifetime
{
    private RedisContainer _redis = null!;
    private IConnectionMultiplexer _connection = null!;

    public IConnectionMultiplexer Connection => _connection;

    public async Task InitializeAsync()
    {
        _redis = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
        await _redis.StartAsync();
        _connection = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString() + ",abortConnect=false,allowAdmin=true");
    }

    public async Task DisposeAsync()
    {
        _connection.Dispose();
        await _redis.DisposeAsync();
    }

    /// <summary>
    /// Flushes database 1 (Matchmaking's database index) between tests.
    /// </summary>
    public async Task FlushDatabaseAsync(int dbIndex = 1)
    {
        var server = _connection.GetServers()[0];
        await server.FlushDatabaseAsync(dbIndex);
    }
}

[CollectionDefinition(Name)]
public class RedisCollection : ICollectionFixture<RedisFixture>
{
    public const string Name = "Redis";
}
