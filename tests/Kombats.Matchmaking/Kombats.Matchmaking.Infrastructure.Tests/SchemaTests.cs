using FluentAssertions;
using Kombats.Matchmaking.Infrastructure.Data;
using Kombats.Matchmaking.Infrastructure.Entities;
using Kombats.Matchmaking.Infrastructure.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kombats.Matchmaking.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SchemaTests
{
    private readonly PostgresFixture _fixture;

    public SchemaTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Schema_UsesMatchmakingSchema()
    {
        await using var db = _fixture.CreateDbContext();
        var schemas = db.Model.GetEntityTypes()
            .Select(e => e.GetSchema())
            .Distinct()
            .ToList();
        schemas.Should().AllBe(MatchmakingDbContext.Schema);
    }

    [Fact]
    public async Task Schema_MatchesTable_UsesSnakeCaseColumns()
    {
        await using var db = _fixture.CreateDbContext();
        var matchEntity = db.Model.FindEntityType(typeof(MatchEntity));
        matchEntity.Should().NotBeNull();
        var columnNames = matchEntity!.GetProperties()
            .Select(p => p.GetColumnName())
            .ToList();
        columnNames.Should().Contain("match_id");
        columnNames.Should().Contain("battle_id");
        columnNames.Should().Contain("player_a_id");
        columnNames.Should().Contain("player_b_id");
        columnNames.Should().Contain("variant");
        columnNames.Should().Contain("state");
        columnNames.Should().Contain("created_at_utc");
        columnNames.Should().Contain("updated_at_utc");
    }

    [Fact]
    public async Task Schema_PlayerCombatProfilesTable_UsesSnakeCaseColumns()
    {
        await using var db = _fixture.CreateDbContext();
        var entity = db.Model.FindEntityType(typeof(PlayerCombatProfileEntity));
        entity.Should().NotBeNull();
        var columnNames = entity!.GetProperties()
            .Select(p => p.GetColumnName())
            .ToList();
        columnNames.Should().Contain("identity_id");
        columnNames.Should().Contain("character_id");
        columnNames.Should().Contain("level");
        columnNames.Should().Contain("strength");
        columnNames.Should().Contain("is_ready");
        columnNames.Should().Contain("revision");
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
