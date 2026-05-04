using Testcontainers.PostgreSql;
using Xunit;

namespace Kombats.Chat.Infrastructure.Tests.Fixtures;

/// <summary>
/// Shared PostgreSQL Testcontainers fixture for Chat infrastructure tests.
/// ChatDbContext will be added in Batch 1 when persistence is implemented.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();

        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
