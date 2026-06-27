using FluentAssertions;
using Kombats.Battle.Infrastructure.Data.DbContext;
using Kombats.Battle.Infrastructure.Data.Entities;
using Kombats.Battle.Infrastructure.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kombats.Battle.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SchemaTests
{
    private readonly PostgresFixture _fixture;

    public SchemaTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Schema_UsesBattleSchema()
    {
        await using var db = _fixture.CreateDbContext();
        var schemas = db.Model.GetEntityTypes()
            .Select(e => e.GetSchema())
            .Distinct()
            .ToList();
        schemas.Should().AllBe(BattleDbContext.Schema);
    }

    [Fact]
    public async Task Schema_BattlesTable_UsesSnakeCaseColumns()
    {
        await using var db = _fixture.CreateDbContext();
        var battleEntity = db.Model.FindEntityType(typeof(BattleEntity));
        battleEntity.Should().NotBeNull();
        var columnNames = battleEntity!.GetProperties()
            .Select(p => p.GetColumnName())
            .ToList();
        columnNames.Should().Contain("battle_id");
        columnNames.Should().Contain("match_id");
        columnNames.Should().Contain("player_a_id");
        columnNames.Should().Contain("player_b_id");
        columnNames.Should().Contain("state");
        columnNames.Should().Contain("created_at");
        columnNames.Should().Contain("ended_at");
        columnNames.Should().Contain("end_reason");
        columnNames.Should().Contain("winner_player_id");
    }

    [Fact]
    public async Task Schema_OutboxTables_Exist()
    {
        await using var db = _fixture.CreateDbContext();
        var tables = db.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(t => t != null)
            .ToList();
        tables.Should().Contain("inbox_state");
        tables.Should().Contain("outbox_message");
        tables.Should().Contain("outbox_state");
    }

    [Fact]
    public async Task Migrations_ApplyCleanly()
    {
        await using var db = _fixture.CreateDbContext();
        var pending = await db.Database.GetPendingMigrationsAsync();
        pending.Should().BeEmpty();
    }
}
