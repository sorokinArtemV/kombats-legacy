using Kombats.Matchmaking.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kombats.Matchmaking.Infrastructure.Tests.Fixtures;

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
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    public MatchmakingDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MatchmakingDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", MatchmakingDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IHistoryRepository, SnakeCaseHistoryRepository>()
            .Options;
        return new MatchmakingDbContext(options);
    }
}

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
