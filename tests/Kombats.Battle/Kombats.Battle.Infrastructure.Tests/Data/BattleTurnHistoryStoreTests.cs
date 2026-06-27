using FluentAssertions;
using Kombats.Battle.Domain.Results;
using Kombats.Battle.Domain.Rules;
using Kombats.Battle.Infrastructure.Data;
using Kombats.Battle.Infrastructure.Data.Entities;
using Kombats.Battle.Infrastructure.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kombats.Battle.Infrastructure.Tests.Data;

[Collection(PostgresCollection.Name)]
public sealed class BattleTurnHistoryStoreTests
{
    private readonly PostgresFixture _fixture;

    public BattleTurnHistoryStoreTests(PostgresFixture fixture) => _fixture = fixture;

    private static TurnResolutionLog CreateLog(Guid battleId, int turnIndex, Guid playerAId, Guid playerBId)
    {
        return new TurnResolutionLog
        {
            BattleId = battleId,
            TurnIndex = turnIndex,
            AtoB = new AttackResolution
            {
                AttackerId = playerAId,
                DefenderId = playerBId,
                TurnIndex = turnIndex,
                AttackZone = BattleZone.Head,
                DefenderBlockPrimary = BattleZone.Chest,
                DefenderBlockSecondary = BattleZone.Belly,
                WasBlocked = false,
                WasCrit = true,
                Outcome = AttackOutcome.CriticalHit,
                Damage = 45
            },
            BtoA = new AttackResolution
            {
                AttackerId = playerBId,
                DefenderId = playerAId,
                TurnIndex = turnIndex,
                AttackZone = BattleZone.Legs,
                DefenderBlockPrimary = BattleZone.Waist,
                DefenderBlockSecondary = BattleZone.Legs,
                WasBlocked = true,
                WasCrit = false,
                Outcome = AttackOutcome.Blocked,
                Damage = 0
            }
        };
    }

    private async Task<Guid> SeedBattleAsync()
    {
        var battleId = Guid.NewGuid();
        await using var db = _fixture.CreateDbContext();
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
        return battleId;
    }

    [Fact]
    public async Task PersistTurnAsync_RoundTrip_AllFieldsCorrect()
    {
        var battleId = await SeedBattleAsync();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var log = CreateLog(battleId, 1, playerAId, playerBId);

        await using (var db = _fixture.CreateDbContext())
        {
            var store = new BattleTurnHistoryStore(db, NullLogger<BattleTurnHistoryStore>.Instance);
            await store.PersistTurnAsync(battleId, 1, log, 80, 100);
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var turn = await db.BattleTurns.FirstOrDefaultAsync(
                t => t.BattleId == battleId && t.TurnIndex == 1);

            turn.Should().NotBeNull();
            turn!.BattleId.Should().Be(battleId);
            turn.TurnIndex.Should().Be(1);

            // A→B
            turn.AtoBAttackZone.Should().Be("Head");
            turn.AtoBDefenderBlockPrimary.Should().Be("Chest");
            turn.AtoBDefenderBlockSecondary.Should().Be("Belly");
            turn.AtoBWasBlocked.Should().BeFalse();
            turn.AtoBWasCrit.Should().BeTrue();
            turn.AtoBOutcome.Should().Be("CriticalHit");
            turn.AtoBDamage.Should().Be(45);

            // B→A
            turn.BtoAAttackZone.Should().Be("Legs");
            turn.BtoADefenderBlockPrimary.Should().Be("Waist");
            turn.BtoADefenderBlockSecondary.Should().Be("Legs");
            turn.BtoAWasBlocked.Should().BeTrue();
            turn.BtoAWasCrit.Should().BeFalse();
            turn.BtoAOutcome.Should().Be("Blocked");
            turn.BtoADamage.Should().Be(0);

            // Post-turn
            turn.PlayerAHpAfter.Should().Be(80);
            turn.PlayerBHpAfter.Should().Be(100);
            turn.ResolvedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task PersistTurnAsync_DuplicateKey_IdempotentNoException()
    {
        var battleId = await SeedBattleAsync();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var log = CreateLog(battleId, 1, playerAId, playerBId);

        // First persist
        await using (var db = _fixture.CreateDbContext())
        {
            var store = new BattleTurnHistoryStore(db, NullLogger<BattleTurnHistoryStore>.Instance);
            await store.PersistTurnAsync(battleId, 1, log, 80, 100);
        }

        // Second persist — same (battleId, turnIndex) — should not throw
        await using (var db = _fixture.CreateDbContext())
        {
            var store = new BattleTurnHistoryStore(db, NullLogger<BattleTurnHistoryStore>.Instance);
            var act = () => store.PersistTurnAsync(battleId, 1, log, 80, 100);
            await act.Should().NotThrowAsync();
        }

        // Verify exactly one row
        await using (var db = _fixture.CreateDbContext())
        {
            var count = await db.BattleTurns.CountAsync(
                t => t.BattleId == battleId && t.TurnIndex == 1);
            count.Should().Be(1);
        }
    }
}
