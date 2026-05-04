using FluentAssertions;
using Kombats.Battle.Infrastructure.Data.Entities;
using Kombats.Battle.Infrastructure.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kombats.Battle.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public sealed class BattlePersistenceTests
{
    private readonly PostgresFixture _fixture;

    public BattlePersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreateAndReload_RoundTrip()
    {
        var battleId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            db.Battles.Add(new BattleEntity
            {
                BattleId = battleId,
                MatchId = matchId,
                PlayerAId = playerAId,
                PlayerBId = playerBId,
                State = "ArenaOpen",
                CreatedAt = now
            });
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var loaded = await db.Battles.FirstOrDefaultAsync(b => b.BattleId == battleId);
            loaded.Should().NotBeNull();
            loaded!.MatchId.Should().Be(matchId);
            loaded.PlayerAId.Should().Be(playerAId);
            loaded.PlayerBId.Should().Be(playerBId);
            loaded.State.Should().Be("ArenaOpen");
            loaded.EndedAt.Should().BeNull();
            loaded.EndReason.Should().BeNull();
            loaded.WinnerPlayerId.Should().BeNull();
        }
    }

    [Fact]
    public async Task UpdateToEnded_PersistsAllFields()
    {
        var battleId = Guid.NewGuid();
        var winnerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            db.Battles.Add(new BattleEntity
            {
                BattleId = battleId,
                MatchId = Guid.NewGuid(),
                PlayerAId = winnerId,
                PlayerBId = Guid.NewGuid(),
                State = "ArenaOpen",
                CreatedAt = now
            });
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var battle = await db.Battles.FirstAsync(b => b.BattleId == battleId);
            battle.State = "Ended";
            battle.EndedAt = now.AddMinutes(5);
            battle.EndReason = "Normal";
            battle.WinnerPlayerId = winnerId;
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var loaded = await db.Battles.FirstAsync(b => b.BattleId == battleId);
            loaded.State.Should().Be("Ended");
            loaded.EndedAt.Should().NotBeNull();
            loaded.EndReason.Should().Be("Normal");
            loaded.WinnerPlayerId.Should().Be(winnerId);
        }
    }

    [Fact]
    public async Task DuplicateBattleId_ThrowsUniqueViolation()
    {
        var battleId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            db.Battles.Add(new BattleEntity
            {
                BattleId = battleId,
                MatchId = Guid.NewGuid(),
                PlayerAId = Guid.NewGuid(),
                PlayerBId = Guid.NewGuid(),
                State = "ArenaOpen",
                CreatedAt = now
            });
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            db.Battles.Add(new BattleEntity
            {
                BattleId = battleId,
                MatchId = Guid.NewGuid(),
                PlayerAId = Guid.NewGuid(),
                PlayerBId = Guid.NewGuid(),
                State = "ArenaOpen",
                CreatedAt = now
            });

            var act = () => db.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>();
        }
    }
}
