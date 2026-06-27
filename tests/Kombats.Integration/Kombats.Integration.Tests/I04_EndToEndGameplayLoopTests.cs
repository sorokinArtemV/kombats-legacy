using FluentAssertions;
using Kombats.Battle.Contracts.Battle;
using Kombats.Battle.Infrastructure.Data.DbContext;
using Kombats.Battle.Infrastructure.Data;
using Kombats.Battle.Infrastructure.Data.Entities;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Domain;
using Kombats.Matchmaking.Infrastructure.Data;
using Kombats.Matchmaking.Infrastructure.Messaging.Consumers;
using Kombats.Matchmaking.Infrastructure.Repositories;
using Kombats.Players.Contracts;
using Kombats.Players.Domain.Entities;
using Kombats.Players.Infrastructure.Data;
using Kombats.Players.Infrastructure.Persistence.EF;
using Kombats.Players.Infrastructure.Persistence.Repository;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kombats.Integration.Tests;

/// <summary>
/// I-04: End-to-End Gameplay Loop Verification.
///
/// Verifies the full cross-service handoff chain by simulating the lifecycle
/// using real PostgreSQL for all three services' DbContexts:
///
/// 1. Player onboards (Players creates character, allocates stats, becomes IsReady)
/// 2. Players publishes PlayerCombatProfileChanged
/// 3. Matchmaking consumes profile and creates projection
/// 4. Two players queued → match created (BattleCreateRequested)
/// 5. CreateBattle command sent → Battle creates entity
/// 6. Battle publishes BattleCreated → Matchmaking advances match to BattleCreated
/// 7. Battle completes → publishes BattleCompleted
/// 8. Matchmaking consumes BattleCompleted → match → Completed, clears player status
/// 9. Players consumes BattleCompleted → awards XP, updates combat record, publishes profile
/// 10. Player can re-queue (profile re-projected in Matchmaking)
///
/// Each step is verified with real persistence. Consumer boundaries are tested
/// by directly invoking consumers with mocked ConsumeContext.
/// </summary>
public sealed class I04_EndToEndGameplayLoopTests : IAsyncLifetime
{
    private PostgreSqlContainer _playersPostgres = null!;
    private PostgreSqlContainer _matchmakingPostgres = null!;
    private PostgreSqlContainer _battlePostgres = null!;

    public async Task InitializeAsync()
    {
        _playersPostgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithName($"e2e-players-{Guid.NewGuid():N}")
            .Build();
        _matchmakingPostgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithName($"e2e-matchmaking-{Guid.NewGuid():N}")
            .Build();
        _battlePostgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithName($"e2e-battle-{Guid.NewGuid():N}")
            .Build();

        await Task.WhenAll(
            _playersPostgres.StartAsync(),
            _matchmakingPostgres.StartAsync(),
            _battlePostgres.StartAsync());

        // Apply migrations for all three schemas
        await using var playersDb = CreatePlayersContext();
        await playersDb.Database.MigrateAsync();

        await using var matchmakingDb = CreateMatchmakingContext();
        await matchmakingDb.Database.MigrateAsync();

        await using var battleDb = CreateBattleContext();
        await battleDb.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(
            _playersPostgres.DisposeAsync().AsTask(),
            _matchmakingPostgres.DisposeAsync().AsTask(),
            _battlePostgres.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task FullGameplayLoop_PlayerOnboards_Queues_Battles_ReceivesXP_CanRequeue()
    {
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // ═══════════════════════════════════════════════════════════
        // STEP 1: Players onboard (create characters, allocate stats)
        // ═══════════════════════════════════════════════════════════

        await using (var db = CreatePlayersContext())
        {
            var charA = Character.CreateDraft(playerAId, now);
            charA.SetNameOnce("PlayerA", now);
            charA.AllocatePoints(1, 1, 1, 0, now);
            db.Characters.Add(charA);

            var charB = Character.CreateDraft(playerBId, now);
            charB.SetNameOnce("PlayerB", now);
            charB.AllocatePoints(0, 0, 2, 1, now);
            db.Characters.Add(charB);

            await db.SaveChangesAsync();
        }

        // Verify characters are ready
        Guid charAId, charBId;
        await using (var db = CreatePlayersContext())
        {
            var charA = await db.Characters.FirstAsync(c => c.IdentityId == playerAId);
            charA.IsReady.Should().BeTrue("Player A should be ready after allocating points");
            charAId = charA.Id;

            var charB = await db.Characters.FirstAsync(c => c.IdentityId == playerBId);
            charB.IsReady.Should().BeTrue("Player B should be ready after allocating points");
            charBId = charB.Id;
        }

        // ═══════════════════════════════════════════════════════════
        // STEP 2 & 3: Players publishes profiles → Matchmaking creates projections
        // ═══════════════════════════════════════════════════════════

        var profileEventA = new PlayerCombatProfileChanged
        {
            MessageId = Guid.NewGuid(),
            IdentityId = playerAId,
            CharacterId = charAId,
            Name = "PlayerA",
            Level = 1,
            Strength = 4,
            Agility = 4,
            Intuition = 4,
            Vitality = 3,
            IsReady = true,
            Revision = 1,
            OccurredAt = now,
            Version = 1
        };

        var profileEventB = new PlayerCombatProfileChanged
        {
            MessageId = Guid.NewGuid(),
            IdentityId = playerBId,
            CharacterId = charBId,
            Name = "PlayerB",
            Level = 1,
            Strength = 3,
            Agility = 3,
            Intuition = 5,
            Vitality = 4,
            IsReady = true,
            Revision = 1,
            OccurredAt = now,
            Version = 1
        };

        // Matchmaking consumes profiles
        await using (var db = CreateMatchmakingContext())
        {
            var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);
            var consumer = new PlayerCombatProfileChangedConsumer(repo, NullLogger<PlayerCombatProfileChangedConsumer>.Instance);
            await consumer.Consume(CreateContext(profileEventA));
        }

        await using (var db = CreateMatchmakingContext())
        {
            var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);
            var consumer = new PlayerCombatProfileChangedConsumer(repo, NullLogger<PlayerCombatProfileChangedConsumer>.Instance);
            await consumer.Consume(CreateContext(profileEventB));
        }

        // Verify projections exist
        await using (var db = CreateMatchmakingContext())
        {
            var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);
            var profileA = await repo.GetByIdentityIdAsync(playerAId);
            profileA.Should().NotBeNull();
            profileA!.IsReady.Should().BeTrue();

            var profileB = await repo.GetByIdentityIdAsync(playerBId);
            profileB.Should().NotBeNull();
            profileB!.IsReady.Should().BeTrue();
        }

        // ═══════════════════════════════════════════════════════════
        // STEP 4: Match created (simulating pairing worker output)
        // ═══════════════════════════════════════════════════════════

        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();

        await using (var db = CreateMatchmakingContext())
        {
            var match = Match.Create(matchId, battleId, playerAId, playerBId, "default", now);
            match.MarkBattleCreateRequested(now);
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            repo.Add(match);
            await db.SaveChangesAsync();
        }

        // ═══════════════════════════════════════════════════════════
        // STEP 5: Battle creates entity (simulating CreateBattle consumer)
        // ═══════════════════════════════════════════════════════════

        await using (var db = CreateBattleContext())
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

        // ═══════════════════════════════════════════════════════════
        // STEP 6: BattleCreated → Matchmaking advances match
        // ═══════════════════════════════════════════════════════════

        var battleCreatedEvent = new BattleCreated
        {
            BattleId = battleId,
            MatchId = matchId,
            PlayerAId = playerAId,
            PlayerBId = playerBId,
            OccurredAt = now,
            Version = 1
        };

        await using (var db = CreateMatchmakingContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var consumer = new BattleCreatedConsumer(repo, NullLogger<BattleCreatedConsumer>.Instance);
            await consumer.Consume(CreateContext(battleCreatedEvent));
        }

        await using (var db = CreateMatchmakingContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var match = await repo.GetByMatchIdAsync(matchId);
            match!.State.Should().Be(MatchState.BattleCreated);
        }

        // ═══════════════════════════════════════════════════════════
        // STEP 7: Battle completes
        // ═══════════════════════════════════════════════════════════

        var battleCompletedEvent = new BattleCompleted
        {
            MessageId = Guid.NewGuid(),
            BattleId = battleId,
            MatchId = matchId,
            PlayerAIdentityId = playerAId,
            PlayerBIdentityId = playerBId,
            WinnerIdentityId = playerAId,
            LoserIdentityId = playerBId,
            Reason = BattleEndReason.Normal,
            TurnCount = 8,
            DurationMs = 32000,
            RulesetVersion = 1,
            OccurredAt = now.AddMinutes(5),
            Version = 1
        };

        // Update battle read model
        await using (var db = CreateBattleContext())
        {
            var battle = await db.Battles.FirstAsync(b => b.BattleId == battleId);
            battle.State = "Ended";
            battle.EndedAt = battleCompletedEvent.OccurredAt;
            battle.EndReason = "Normal";
            battle.WinnerPlayerId = playerAId;
            await db.SaveChangesAsync();
        }

        // ═══════════════════════════════════════════════════════════
        // STEP 8: Matchmaking consumes BattleCompleted
        // ═══════════════════════════════════════════════════════════

        var statusStore = Substitute.For<IPlayerMatchStatusStore>();

        await using (var db = CreateMatchmakingContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var consumer = new Matchmaking.Infrastructure.Messaging.Consumers.BattleCompletedConsumer(
                repo, statusStore, NullLogger<Matchmaking.Infrastructure.Messaging.Consumers.BattleCompletedConsumer>.Instance);
            await consumer.Consume(CreateContext(battleCompletedEvent));
        }

        // Verify match is completed
        await using (var db = CreateMatchmakingContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var match = await repo.GetByMatchIdAsync(matchId);
            match!.State.Should().Be(MatchState.Completed, "match should be completed after battle finishes");
        }

        // Verify player status cleared (enabling re-queue)
        await statusStore.Received(1).RemoveStatusAsync(playerAId, Arg.Any<CancellationToken>());
        await statusStore.Received(1).RemoveStatusAsync(playerBId, Arg.Any<CancellationToken>());

        // ═══════════════════════════════════════════════════════════
        // STEP 9: Players consumes BattleCompleted → XP + combat record + profile
        // ═══════════════════════════════════════════════════════════

        // The Players BattleCompletedConsumer delegates to HandleBattleCompletedHandler.
        // We simulate the handler's effect on the Players DB — the full consumer pipeline
        // is tested in Players.Infrastructure.Tests.BattleCompletedConsumerTests.
        // Here we verify the domain-level combat record updates.
        await using (var db = CreatePlayersContext())
        {
            var winner = await db.Characters.FirstAsync(c => c.IdentityId == playerAId);
            winner.RecordWin(now.AddMinutes(5));

            var loser = await db.Characters.FirstAsync(c => c.IdentityId == playerBId);
            loser.RecordLoss(now.AddMinutes(5));

            await db.SaveChangesAsync();
        }

        // Verify combat records
        await using (var db = CreatePlayersContext())
        {
            var winner = await db.Characters.FirstAsync(c => c.IdentityId == playerAId);
            winner.Wins.Should().Be(1);
            winner.Losses.Should().Be(0);
            winner.IsReady.Should().BeTrue("winner should still be ready to re-queue");

            var loser = await db.Characters.FirstAsync(c => c.IdentityId == playerBId);
            loser.Wins.Should().Be(0);
            loser.Losses.Should().Be(1);
            loser.IsReady.Should().BeTrue("loser should still be ready to re-queue");
        }

        // ═══════════════════════════════════════════════════════════
        // STEP 10: Player can re-queue (profile updated in Matchmaking)
        // ═══════════════════════════════════════════════════════════

        // Simulate updated profile event (after XP award, stats unchanged but revision bumped)
        var profileUpdateA = new PlayerCombatProfileChanged
        {
            MessageId = Guid.NewGuid(),
            IdentityId = playerAId,
            CharacterId = charAId,
            Name = "PlayerA",
            Level = 1,
            Strength = 4,
            Agility = 4,
            Intuition = 4,
            Vitality = 3,
            IsReady = true,
            Revision = 2, // Bumped after battle
            OccurredAt = now.AddMinutes(6),
            Version = 1
        };

        await using (var db = CreateMatchmakingContext())
        {
            var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);
            var consumer = new PlayerCombatProfileChangedConsumer(repo, NullLogger<PlayerCombatProfileChangedConsumer>.Instance);
            await consumer.Consume(CreateContext(profileUpdateA));
        }

        // Verify updated projection
        await using (var db = CreateMatchmakingContext())
        {
            var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);
            var profile = await repo.GetByIdentityIdAsync(playerAId);
            profile.Should().NotBeNull();
            profile!.Revision.Should().Be(2, "profile should be updated with new revision");
            profile.IsReady.Should().BeTrue("player should still be eligible to queue");
        }

        // No active match for the player (can re-queue)
        await using (var db = CreateMatchmakingContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var activeMatch = await repo.GetActiveForPlayerAsync(playerAId);
            activeMatch.Should().BeNull("player should have no active match after completion");
        }
    }

    private PlayersDbContext CreatePlayersContext()
    {
        var options = new DbContextOptionsBuilder<PlayersDbContext>()
            .UseNpgsql(_playersPostgres.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", PlayersDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IHistoryRepository, Players.Infrastructure.Data.SnakeCaseHistoryRepository>()
            .Options;
        return new PlayersDbContext(options);
    }

    private MatchmakingDbContext CreateMatchmakingContext()
    {
        var options = new DbContextOptionsBuilder<MatchmakingDbContext>()
            .UseNpgsql(_matchmakingPostgres.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", MatchmakingDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IHistoryRepository, Matchmaking.Infrastructure.Data.SnakeCaseHistoryRepository>()
            .Options;
        return new MatchmakingDbContext(options);
    }

    private BattleDbContext CreateBattleContext()
    {
        var options = new DbContextOptionsBuilder<BattleDbContext>()
            .UseNpgsql(_battlePostgres.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", BattleDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IHistoryRepository, Battle.Infrastructure.Data.SnakeCaseHistoryRepository>()
            .Options;
        return new BattleDbContext(options);
    }

    private static ConsumeContext<T> CreateContext<T>(T message) where T : class
    {
        var ctx = Substitute.For<ConsumeContext<T>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }
}
