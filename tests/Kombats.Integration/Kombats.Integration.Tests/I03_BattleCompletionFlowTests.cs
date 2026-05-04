using FluentAssertions;
using Kombats.Battle.Contracts.Battle;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Domain;
using Kombats.Matchmaking.Infrastructure.Data;
using Kombats.Matchmaking.Infrastructure.Messaging.Consumers;
using Kombats.Matchmaking.Infrastructure.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kombats.Integration.Tests;

/// <summary>
/// I-03: Verify Battle → Players + Matchmaking Completion Flow.
/// Verifies that BattleCompleted events published by Battle are correctly consumed by:
/// 1. Matchmaking's BattleCompletedConsumer (advances match to terminal state, clears player status)
/// 2. Matchmaking's BattleCreatedConsumer (advances match from BattleCreateRequested to BattleCreated)
///
/// Players' BattleCompletedConsumer is already tested in Kombats.Players.Infrastructure.Tests.
/// This test focuses on the Matchmaking side of the completion flow.
/// </summary>
public sealed class I03_BattleCompletionFlowTests : IAsyncLifetime
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
    public async Task BattleCreated_AdvancesMatch_FromBattleCreateRequested_ToBattleCreated()
    {
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();

        // Seed a match in BattleCreateRequested state
        await using (var db = CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            var match = Match.Create(matchId, battleId, playerAId, playerBId, "default", now);
            match.MarkBattleCreateRequested(now);
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            repo.Add(match);
            await db.SaveChangesAsync();
        }

        // Consume BattleCreated event
        var event_ = new BattleCreated
        {
            BattleId = battleId,
            MatchId = matchId,
            PlayerAId = playerAId,
            PlayerBId = playerBId,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        await using (var db = CreateDbContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var consumer = new BattleCreatedConsumer(repo, NullLogger<BattleCreatedConsumer>.Instance);
            await consumer.Consume(CreateContext(event_));
        }

        // Verify match advanced to BattleCreated
        await using (var db = CreateDbContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var match = await repo.GetByMatchIdAsync(matchId);
            match.Should().NotBeNull();
            match!.State.Should().Be(MatchState.BattleCreated);
        }
    }

    [Fact]
    public async Task BattleCreated_DuplicateEvent_IsIdempotent()
    {
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();

        // Seed match in BattleCreateRequested state
        await using (var db = CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            var match = Match.Create(matchId, battleId, playerAId, playerBId, "default", now);
            match.MarkBattleCreateRequested(now);
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            repo.Add(match);
            await db.SaveChangesAsync();
        }

        var event_ = new BattleCreated
        {
            BattleId = battleId,
            MatchId = matchId,
            PlayerAId = playerAId,
            PlayerBId = playerBId,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        // First consume
        await using (var db = CreateDbContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var consumer = new BattleCreatedConsumer(repo, NullLogger<BattleCreatedConsumer>.Instance);
            await consumer.Consume(CreateContext(event_));
        }

        // Second consume — should not throw, should be idempotent
        await using (var db = CreateDbContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var consumer = new BattleCreatedConsumer(repo, NullLogger<BattleCreatedConsumer>.Instance);
            await consumer.Consume(CreateContext(event_));
        }

        // Match should still be BattleCreated (not advanced further)
        await using (var db = CreateDbContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var match = await repo.GetByMatchIdAsync(matchId);
            match!.State.Should().Be(MatchState.BattleCreated);
        }
    }

    [Fact]
    public async Task BattleCompleted_Normal_AdvancesMatch_ToCompleted_AndClearsStatus()
    {
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();

        // Seed match in BattleCreated state (the correct precondition for completion)
        await SeedMatchInState(matchId, battleId, playerAId, playerBId, MatchState.BattleCreated);

        var statusStore = Substitute.For<IPlayerMatchStatusStore>();

        var event_ = new BattleCompleted
        {
            MessageId = Guid.NewGuid(),
            BattleId = battleId,
            MatchId = matchId,
            PlayerAIdentityId = playerAId,
            PlayerBIdentityId = playerBId,
            WinnerIdentityId = playerAId,
            LoserIdentityId = playerBId,
            Reason = BattleEndReason.Normal,
            TurnCount = 10,
            DurationMs = 45000,
            RulesetVersion = 1,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        await using (var db = CreateDbContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var consumer = new Matchmaking.Infrastructure.Messaging.Consumers.BattleCompletedConsumer(
                repo, statusStore, NullLogger<Matchmaking.Infrastructure.Messaging.Consumers.BattleCompletedConsumer>.Instance);
            await consumer.Consume(CreateContext(event_));
        }

        // Verify match advanced to Completed
        await using (var db = CreateDbContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var match = await repo.GetByMatchIdAsync(matchId);
            match!.State.Should().Be(MatchState.Completed);
        }

        // Verify player status was cleared for both players
        await statusStore.Received(1).RemoveStatusAsync(playerAId, Arg.Any<CancellationToken>());
        await statusStore.Received(1).RemoveStatusAsync(playerBId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BattleCompleted_Timeout_AdvancesMatch_ToTimedOut()
    {
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();

        await SeedMatchInState(matchId, battleId, playerAId, playerBId, MatchState.BattleCreated);

        var statusStore = Substitute.For<IPlayerMatchStatusStore>();

        var event_ = new BattleCompleted
        {
            MessageId = Guid.NewGuid(),
            BattleId = battleId,
            MatchId = matchId,
            PlayerAIdentityId = playerAId,
            PlayerBIdentityId = playerBId,
            WinnerIdentityId = null,
            LoserIdentityId = null,
            Reason = BattleEndReason.Timeout,
            TurnCount = 20,
            DurationMs = 60000,
            RulesetVersion = 1,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        await using (var db = CreateDbContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var consumer = new Matchmaking.Infrastructure.Messaging.Consumers.BattleCompletedConsumer(
                repo, statusStore, NullLogger<Matchmaking.Infrastructure.Messaging.Consumers.BattleCompletedConsumer>.Instance);
            await consumer.Consume(CreateContext(event_));
        }

        // Verify match advanced to TimedOut (Timeout reason maps to TimedOut state)
        await using (var db = CreateDbContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var match = await repo.GetByMatchIdAsync(matchId);
            match!.State.Should().Be(MatchState.TimedOut);
        }
    }

    [Fact]
    public async Task BattleCompleted_AlreadyTerminal_IsIdempotent()
    {
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();

        // Seed match already in Completed state
        await SeedMatchInState(matchId, battleId, playerAId, playerBId, MatchState.BattleCreated);

        var statusStore = Substitute.For<IPlayerMatchStatusStore>();

        var event_ = new BattleCompleted
        {
            MessageId = Guid.NewGuid(),
            BattleId = battleId,
            MatchId = matchId,
            PlayerAIdentityId = playerAId,
            PlayerBIdentityId = playerBId,
            WinnerIdentityId = playerAId,
            LoserIdentityId = playerBId,
            Reason = BattleEndReason.Normal,
            TurnCount = 10,
            DurationMs = 45000,
            RulesetVersion = 1,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        // First consume
        await using (var db = CreateDbContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var consumer = new Matchmaking.Infrastructure.Messaging.Consumers.BattleCompletedConsumer(
                repo, statusStore, NullLogger<Matchmaking.Infrastructure.Messaging.Consumers.BattleCompletedConsumer>.Instance);
            await consumer.Consume(CreateContext(event_));
        }

        // Second consume — should not throw
        await using (var db = CreateDbContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var consumer = new Matchmaking.Infrastructure.Messaging.Consumers.BattleCompletedConsumer(
                repo, statusStore, NullLogger<Matchmaking.Infrastructure.Messaging.Consumers.BattleCompletedConsumer>.Instance);
            await consumer.Consume(CreateContext(event_));
        }

        // State should still be Completed
        await using (var db = CreateDbContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var match = await repo.GetByMatchIdAsync(matchId);
            match!.State.Should().Be(MatchState.Completed);
        }
    }

    [Fact]
    public async Task BattleCompleted_Draw_ClearsStatusForBothPlayers()
    {
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();

        await SeedMatchInState(matchId, battleId, playerAId, playerBId, MatchState.BattleCreated);

        var statusStore = Substitute.For<IPlayerMatchStatusStore>();

        var event_ = new BattleCompleted
        {
            MessageId = Guid.NewGuid(),
            BattleId = battleId,
            MatchId = matchId,
            PlayerAIdentityId = playerAId,
            PlayerBIdentityId = playerBId,
            WinnerIdentityId = null,
            LoserIdentityId = null,
            Reason = BattleEndReason.DoubleForfeit,
            TurnCount = 20,
            DurationMs = 60000,
            RulesetVersion = 1,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        await using (var db = CreateDbContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var consumer = new Matchmaking.Infrastructure.Messaging.Consumers.BattleCompletedConsumer(
                repo, statusStore, NullLogger<Matchmaking.Infrastructure.Messaging.Consumers.BattleCompletedConsumer>.Instance);
            await consumer.Consume(CreateContext(event_));
        }

        // Both players' status should be cleared even on draw
        await statusStore.Received(1).RemoveStatusAsync(playerAId, Arg.Any<CancellationToken>());
        await statusStore.Received(1).RemoveStatusAsync(playerBId, Arg.Any<CancellationToken>());
    }

    private async Task SeedMatchInState(Guid matchId, Guid battleId, Guid playerAId, Guid playerBId, MatchState targetState)
    {
        await using var db = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var match = Match.Create(matchId, battleId, playerAId, playerBId, "default", now);
        match.MarkBattleCreateRequested(now);
        if (targetState >= MatchState.BattleCreated)
            match.TryMarkBattleCreated(now);

        var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
        repo.Add(match);
        await db.SaveChangesAsync();
    }

    private MatchmakingDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MatchmakingDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", MatchmakingDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IHistoryRepository, SnakeCaseHistoryRepository>()
            .Options;
        return new MatchmakingDbContext(options);
    }

    private static ConsumeContext<T> CreateContext<T>(T message) where T : class
    {
        var ctx = Substitute.For<ConsumeContext<T>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }
}
