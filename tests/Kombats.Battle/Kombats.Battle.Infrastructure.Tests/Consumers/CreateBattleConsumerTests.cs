using FluentAssertions;
using Kombats.Battle.Contracts.Battle;
using Kombats.Battle.Infrastructure.Data.DbContext;
using Kombats.Battle.Infrastructure.Data.Entities;
using Kombats.Battle.Infrastructure.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kombats.Battle.Infrastructure.Tests.Consumers;

/// <summary>
/// Tests the DB-level idempotency behavior that CreateBattleConsumer relies on.
/// The consumer catches DbUpdateException (unique violation on BattleId PK) to handle duplicates.
/// Testing this at the DB layer validates the actual constraint enforcement.
/// </summary>
[Collection(PostgresCollection.Name)]
public class CreateBattleConsumerTests
{
    private readonly PostgresFixture _fixture;

    public CreateBattleConsumerTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task InsertBattle_NewBattle_Succeeds()
    {
        await using var db = _fixture.CreateDbContext();
        var battleId = Guid.NewGuid();

        db.Battles.Add(new BattleEntity
        {
            BattleId = battleId,
            MatchId = Guid.NewGuid(),
            PlayerAId = Guid.NewGuid(),
            PlayerBId = Guid.NewGuid(),
            State = "ArenaOpen",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();

        await using var verifyDb = _fixture.CreateDbContext();
        var battle = await verifyDb.Battles.FirstOrDefaultAsync(b => b.BattleId == battleId);
        battle.Should().NotBeNull();
        battle!.State.Should().Be("ArenaOpen");
    }

    [Fact]
    public async Task InsertBattle_DuplicateBattleId_ThrowsDbUpdateException()
    {
        var battleId = Guid.NewGuid();
        var entity = new BattleEntity
        {
            BattleId = battleId,
            MatchId = Guid.NewGuid(),
            PlayerAId = Guid.NewGuid(),
            PlayerBId = Guid.NewGuid(),
            State = "ArenaOpen",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // First insert
        await using var db1 = _fixture.CreateDbContext();
        db1.Battles.Add(entity);
        await db1.SaveChangesAsync();

        // Duplicate insert — same BattleId
        await using var db2 = _fixture.CreateDbContext();
        db2.Battles.Add(new BattleEntity
        {
            BattleId = battleId,
            MatchId = Guid.NewGuid(),
            PlayerAId = Guid.NewGuid(),
            PlayerBId = Guid.NewGuid(),
            State = "ArenaOpen",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var act = () => db2.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DuplicateInsert_UniqueViolation_ContainsExpectedErrorCode()
    {
        var battleId = Guid.NewGuid();

        await using var db1 = _fixture.CreateDbContext();
        db1.Battles.Add(new BattleEntity
        {
            BattleId = battleId, MatchId = Guid.NewGuid(),
            PlayerAId = Guid.NewGuid(), PlayerBId = Guid.NewGuid(),
            State = "ArenaOpen", CreatedAt = DateTimeOffset.UtcNow
        });
        await db1.SaveChangesAsync();

        await using var db2 = _fixture.CreateDbContext();
        db2.Battles.Add(new BattleEntity
        {
            BattleId = battleId, MatchId = Guid.NewGuid(),
            PlayerAId = Guid.NewGuid(), PlayerBId = Guid.NewGuid(),
            State = "ArenaOpen", CreatedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await db2.SaveChangesAsync();
            Assert.Fail("Expected DbUpdateException");
        }
        catch (DbUpdateException ex)
        {
            // Consumer's IsUniqueViolation checks for "23505" or "duplicate key"
            var innerMsg = ex.InnerException?.Message ?? "";
            (innerMsg.Contains("23505") || innerMsg.Contains("duplicate key")).Should().BeTrue(
                "Consumer relies on unique violation detection via inner exception message");
        }
    }
}
