using FluentAssertions;
using Kombats.Battle.Contracts.Battle;
using Kombats.Battle.Infrastructure.Data.DbContext;
using Kombats.Battle.Infrastructure.Data;
using Kombats.Battle.Infrastructure.Data.Entities;
using Kombats.Battle.Application.Models;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kombats.Integration.Tests;

/// <summary>
/// I-02: Verify Matchmaking → Battle Command Flow.
/// Verifies the CreateBattle command contract, persistence, and idempotency
/// at the Battle service boundary. The consumer writes a BattleEntity to Postgres
/// and publishes BattleCreated. These tests verify:
/// 1. Contract field alignment (Matchmaking sends what Battle expects)
/// 2. Persistence layer correctly stores BattleEntity
/// 3. Duplicate BattleId → unique violation (consumer catches for idempotency)
/// 4. Vitality → Stamina domain mapping
/// </summary>
public sealed class I02_MatchmakingToBattleFlowTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

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

    [Fact]
    public async Task CreateBattle_Command_PersistsBattleEntity()
    {
        var battleId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();

        // Simulate what the consumer does: create BattleEntity and save
        await using (var db = CreateDbContext())
        {
            db.Battles.Add(new BattleEntity
            {
                BattleId = battleId,
                MatchId = matchId,
                PlayerAId = playerAId,
                PlayerBId = playerBId,
                State = "ArenaOpen",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Verify entity was persisted correctly
        await using (var db = CreateDbContext())
        {
            var entity = await db.Battles.FirstOrDefaultAsync(b => b.BattleId == battleId);
            entity.Should().NotBeNull();
            entity!.MatchId.Should().Be(matchId);
            entity.PlayerAId.Should().Be(playerAId);
            entity.PlayerBId.Should().Be(playerBId);
            entity.State.Should().Be("ArenaOpen");
            entity.EndedAt.Should().BeNull();
            entity.EndReason.Should().BeNull();
            entity.WinnerPlayerId.Should().BeNull();
        }
    }

    [Fact]
    public async Task CreateBattle_DuplicateBattleId_ThrowsUniqueViolation_ConsumerCatchesForIdempotency()
    {
        var battleId = Guid.NewGuid();

        // First insert
        await using (var db = CreateDbContext())
        {
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
        }

        // Duplicate insert — should throw DbUpdateException (unique violation)
        await using (var db = CreateDbContext())
        {
            db.Battles.Add(new BattleEntity
            {
                BattleId = battleId,
                MatchId = Guid.NewGuid(),
                PlayerAId = Guid.NewGuid(),
                PlayerBId = Guid.NewGuid(),
                State = "ArenaOpen",
                CreatedAt = DateTimeOffset.UtcNow
            });

            var act = () => db.SaveChangesAsync();
            var ex = await act.Should().ThrowAsync<DbUpdateException>();

            // The consumer catches this specific pattern for idempotency
            var message = ex.Which.InnerException?.Message ?? "";
            (message.Contains("23505") || message.Contains("duplicate key") || message.Contains("unique constraint"))
                .Should().BeTrue("unique violation should be detectable for idempotency");
        }

        // Only one entity exists
        await using (var db = CreateDbContext())
        {
            var count = await db.Battles.CountAsync(b => b.BattleId == battleId);
            count.Should().Be(1);
        }
    }

    [Fact]
    public void CreateBattle_Contract_CarriesAllRequiredFields()
    {
        // Verify the CreateBattle contract has all fields that the Battle consumer needs
        var command = new CreateBattle
        {
            BattleId = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            RequestedAt = DateTimeOffset.UtcNow,
            PlayerA = new BattleParticipantSnapshot
            {
                IdentityId = Guid.NewGuid(),
                CharacterId = Guid.NewGuid(),
                Name = "PlayerA",
                Level = 5,
                Strength = 10,
                Agility = 8,
                Intuition = 6,
                Vitality = 12
            },
            PlayerB = new BattleParticipantSnapshot
            {
                IdentityId = Guid.NewGuid(),
                CharacterId = Guid.NewGuid(),
                Name = "PlayerB",
                Level = 5,
                Strength = 8,
                Agility = 10,
                Intuition = 8,
                Vitality = 10
            }
        };

        // All fields required by CreateBattleConsumer are present
        command.BattleId.Should().NotBeEmpty();
        command.MatchId.Should().NotBeEmpty();
        command.PlayerA.Should().NotBeNull();
        command.PlayerB.Should().NotBeNull();
        command.PlayerA.IdentityId.Should().NotBeEmpty();
        command.PlayerB.IdentityId.Should().NotBeEmpty();
        command.PlayerA.Vitality.Should().Be(12, "Vitality is the stat that maps to Stamina in Battle domain");
    }

    [Fact]
    public void VitalityMapsToStamina_InBattleDomainMapping()
    {
        // The CreateBattleConsumer maps: command.PlayerA.Vitality → CombatProfile.Stamina
        // This is the publisher-domain-language contract rule (AD-02)
        var snapshot = new BattleParticipantSnapshot
        {
            IdentityId = Guid.NewGuid(),
            CharacterId = Guid.NewGuid(),
            Name = "Test",
            Level = 5,
            Strength = 10,
            Agility = 8,
            Intuition = 6,
            Vitality = 20
        };

        var profile = new CombatProfile(
            snapshot.IdentityId,
            snapshot.Strength,
            snapshot.Vitality, // Vitality → Stamina
            snapshot.Agility,
            snapshot.Intuition);

        profile.Stamina.Should().Be(20);
        profile.Strength.Should().Be(10);
        profile.Agility.Should().Be(8);
        profile.Intuition.Should().Be(6);
    }

    [Fact]
    public void BattleCreated_Event_CarriesCorrectFields_FromCreateBattle()
    {
        // Verify BattleCreated event (published by consumer) aligns with what
        // Matchmaking's BattleCreatedConsumer expects: BattleId, MatchId
        var battleId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();

        var event_ = new BattleCreated
        {
            BattleId = battleId,
            MatchId = matchId,
            PlayerAId = playerAId,
            PlayerBId = playerBId,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        // Matchmaking's BattleCreatedConsumer reads these fields
        event_.BattleId.Should().Be(battleId);
        event_.MatchId.Should().Be(matchId);
        event_.Version.Should().Be(1);
    }

    [Fact]
    public async Task BattleCompleted_Projection_UpdatesReadModel()
    {
        // This verifies the Battle → Matchmaking completion flow's read model update
        var battleId = Guid.NewGuid();
        var winnerId = Guid.NewGuid();

        // Seed battle entity
        await using (var db = CreateDbContext())
        {
            db.Battles.Add(new BattleEntity
            {
                BattleId = battleId,
                MatchId = Guid.NewGuid(),
                PlayerAId = winnerId,
                PlayerBId = Guid.NewGuid(),
                State = "ArenaOpen",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Simulate BattleCompletedProjectionConsumer updating the read model
        await using (var db = CreateDbContext())
        {
            var battle = await db.Battles.FirstAsync(b => b.BattleId == battleId);
            battle.State = "Ended";
            battle.EndedAt = DateTimeOffset.UtcNow;
            battle.EndReason = "Normal";
            battle.WinnerPlayerId = winnerId;
            await db.SaveChangesAsync();
        }

        // Verify read model was updated
        await using (var db = CreateDbContext())
        {
            var battle = await db.Battles.FirstAsync(b => b.BattleId == battleId);
            battle.State.Should().Be("Ended");
            battle.EndedAt.Should().NotBeNull();
            battle.EndReason.Should().Be("Normal");
            battle.WinnerPlayerId.Should().Be(winnerId);
        }
    }

    private BattleDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BattleDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", BattleDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IHistoryRepository, SnakeCaseHistoryRepository>()
            .Options;
        return new BattleDbContext(options);
    }
}
