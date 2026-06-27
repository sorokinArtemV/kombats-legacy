using FluentAssertions;
using Kombats.Battle.Contracts.Battle;
using Kombats.Battle.Infrastructure.Data.DbContext;
using Kombats.Battle.Infrastructure.Data.Entities;
using Kombats.Battle.Infrastructure.Messaging.Projections;
using Kombats.Battle.Infrastructure.Tests.Fixtures;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Kombats.Battle.Infrastructure.Tests.Consumers;

[Collection(PostgresCollection.Name)]
public class BattleCompletedProjectionConsumerTests
{
    private readonly PostgresFixture _fixture;

    public BattleCompletedProjectionConsumerTests(PostgresFixture fixture) => _fixture = fixture;

    private async Task<BattleEntity> SeedBattle(BattleDbContext db, Guid? battleId = null)
    {
        var battle = new BattleEntity
        {
            BattleId = battleId ?? Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            PlayerAId = Guid.NewGuid(),
            PlayerBId = Guid.NewGuid(),
            State = "TurnOpen",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        db.Battles.Add(battle);
        await db.SaveChangesAsync();
        return battle;
    }

    [Fact]
    public async Task Consume_NormalCompletion_UpdatesReadModel()
    {
        await using var seedDb = _fixture.CreateDbContext();
        var battle = await SeedBattle(seedDb);
        var now = DateTimeOffset.UtcNow;
        var winnerId = battle.PlayerAId;

        var message = new BattleCompleted
        {
            MessageId = Guid.NewGuid(),
            BattleId = battle.BattleId,
            MatchId = battle.MatchId,
            PlayerAIdentityId = battle.PlayerAId,
            PlayerBIdentityId = battle.PlayerBId,
            WinnerIdentityId = winnerId,
            LoserIdentityId = battle.PlayerBId,
            Reason = BattleEndReason.Normal,
            TurnCount = 5,
            DurationMs = 30000,
            RulesetVersion = 1,
            OccurredAt = now,
            Version = 1
        };

        await using var consumeDb = _fixture.CreateDbContext();
        var consumer = new BattleCompletedProjectionConsumer(consumeDb, NullLogger<BattleCompletedProjectionConsumer>.Instance);
        var context = Substitute.For<ConsumeContext<BattleCompleted>>();
        context.Message.Returns(message);

        await consumer.Consume(context);

        // Verify
        await using var verifyDb = _fixture.CreateDbContext();
        var updated = await verifyDb.Battles.FirstAsync(b => b.BattleId == battle.BattleId);
        updated.State.Should().Be("Ended");
        updated.EndedAt.Should().BeCloseTo(now, TimeSpan.FromMilliseconds(1));
        updated.EndReason.Should().Be("Normal");
        updated.WinnerPlayerId.Should().Be(winnerId);
    }

    [Fact]
    public async Task Consume_DuplicateWithMatchingData_IdempotentNoThrow()
    {
        await using var seedDb = _fixture.CreateDbContext();
        var battle = await SeedBattle(seedDb);
        var now = DateTimeOffset.UtcNow;

        var message = new BattleCompleted
        {
            MessageId = Guid.NewGuid(),
            BattleId = battle.BattleId,
            MatchId = battle.MatchId,
            PlayerAIdentityId = battle.PlayerAId,
            PlayerBIdentityId = battle.PlayerBId,
            WinnerIdentityId = null,
            Reason = BattleEndReason.DoubleForfeit,
            OccurredAt = now,
            Version = 1
        };

        // First consume
        await using var db1 = _fixture.CreateDbContext();
        var consumer1 = new BattleCompletedProjectionConsumer(db1, NullLogger<BattleCompletedProjectionConsumer>.Instance);
        var ctx1 = Substitute.For<ConsumeContext<BattleCompleted>>();
        ctx1.Message.Returns(message);
        await consumer1.Consume(ctx1);

        // Second consume — same message
        await using var db2 = _fixture.CreateDbContext();
        var consumer2 = new BattleCompletedProjectionConsumer(db2, NullLogger<BattleCompletedProjectionConsumer>.Instance);
        var ctx2 = Substitute.For<ConsumeContext<BattleCompleted>>();
        ctx2.Message.Returns(message);

        var act = () => consumer2.Consume(ctx2);
        await act.Should().NotThrowAsync();

        // State unchanged
        await using var verifyDb = _fixture.CreateDbContext();
        var result = await verifyDb.Battles.FirstAsync(b => b.BattleId == battle.BattleId);
        result.State.Should().Be("Ended");
        result.EndReason.Should().Be("DoubleForfeit");
    }

    [Fact]
    public async Task Consume_DuplicateWithDifferentData_FirstWriteWins()
    {
        await using var seedDb = _fixture.CreateDbContext();
        var battle = await SeedBattle(seedDb);
        var firstTime = DateTimeOffset.UtcNow.AddSeconds(-10);
        var secondTime = DateTimeOffset.UtcNow;

        var firstMessage = new BattleCompleted
        {
            MessageId = Guid.NewGuid(),
            BattleId = battle.BattleId,
            MatchId = battle.MatchId,
            PlayerAIdentityId = battle.PlayerAId,
            PlayerBIdentityId = battle.PlayerBId,
            WinnerIdentityId = battle.PlayerAId,
            Reason = BattleEndReason.Normal,
            OccurredAt = firstTime,
            Version = 1
        };

        var secondMessage = new BattleCompleted
        {
            MessageId = Guid.NewGuid(),
            BattleId = battle.BattleId,
            MatchId = battle.MatchId,
            PlayerAIdentityId = battle.PlayerAId,
            PlayerBIdentityId = battle.PlayerBId,
            WinnerIdentityId = null,
            Reason = BattleEndReason.SystemError,
            OccurredAt = secondTime,
            Version = 1
        };

        // First consume
        await using var db1 = _fixture.CreateDbContext();
        var consumer1 = new BattleCompletedProjectionConsumer(db1, NullLogger<BattleCompletedProjectionConsumer>.Instance);
        var ctx1 = Substitute.For<ConsumeContext<BattleCompleted>>();
        ctx1.Message.Returns(firstMessage);
        await consumer1.Consume(ctx1);

        // Second consume — different data
        await using var db2 = _fixture.CreateDbContext();
        var consumer2 = new BattleCompletedProjectionConsumer(db2, NullLogger<BattleCompletedProjectionConsumer>.Instance);
        var ctx2 = Substitute.For<ConsumeContext<BattleCompleted>>();
        ctx2.Message.Returns(secondMessage);
        await consumer2.Consume(ctx2);

        // First write wins
        await using var verifyDb = _fixture.CreateDbContext();
        var result = await verifyDb.Battles.FirstAsync(b => b.BattleId == battle.BattleId);
        result.WinnerPlayerId.Should().Be(battle.PlayerAId);
        result.EndReason.Should().Be("Normal");
        result.EndedAt.Should().BeCloseTo(firstTime, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Consume_BattleNotFound_NoOpNoThrow()
    {
        var message = new BattleCompleted
        {
            MessageId = Guid.NewGuid(),
            BattleId = Guid.NewGuid(), // Does not exist in DB
            MatchId = Guid.NewGuid(),
            PlayerAIdentityId = Guid.NewGuid(),
            PlayerBIdentityId = Guid.NewGuid(),
            Reason = BattleEndReason.Normal,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        await using var db = _fixture.CreateDbContext();
        var consumer = new BattleCompletedProjectionConsumer(db, NullLogger<BattleCompletedProjectionConsumer>.Instance);
        var context = Substitute.For<ConsumeContext<BattleCompleted>>();
        context.Message.Returns(message);

        var act = () => consumer.Consume(context);
        await act.Should().NotThrowAsync();
    }
}
