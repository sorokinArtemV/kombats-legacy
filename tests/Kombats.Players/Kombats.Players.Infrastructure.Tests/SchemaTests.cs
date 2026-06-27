using FluentAssertions;
using Kombats.Players.Infrastructure.Data;
using Kombats.Players.Infrastructure.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kombats.Players.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SchemaTests
{
    private readonly PostgresFixture _fixture;

    public SchemaTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Schema_UsesPlayersSchema()
    {
        await using var db = _fixture.CreateDbContext();

        var entityTypes = db.Model.GetEntityTypes()
            .Select(e => e.GetSchema())
            .Distinct()
            .ToList();

        entityTypes.Should().AllBe(PlayersDbContext.Schema);
    }

    [Fact]
    public async Task Schema_CharactersTable_UsesSnakeCaseColumns()
    {
        await using var db = _fixture.CreateDbContext();

        var characterEntity = db.Model.FindEntityType(typeof(Kombats.Players.Domain.Entities.Character));
        characterEntity.Should().NotBeNull();

        var columnNames = characterEntity!.GetProperties()
            .Select(p => p.GetColumnName())
            .ToList();

        columnNames.Should().Contain("identity_id");
        columnNames.Should().Contain("onboarding_state");
        columnNames.Should().Contain("unspent_points");
        columnNames.Should().Contain("total_xp");
        columnNames.Should().Contain("leveling_version");
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
        // Verify that all migrations have been applied (done in fixture setup)
        await using var db = _fixture.CreateDbContext();
        var pending = await db.Database.GetPendingMigrationsAsync();
        pending.Should().BeEmpty();
    }
}
