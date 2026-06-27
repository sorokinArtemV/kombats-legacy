using FluentAssertions;
using Kombats.Abstractions;
using Kombats.Battle.Contracts.Battle;
using Kombats.Players.Application.Abstractions;
using Kombats.Players.Application.Battles;
using Kombats.Players.Contracts;
using Kombats.Players.Domain.Entities;
using Kombats.Players.Infrastructure.Configuration;
using Kombats.Players.Infrastructure.Messaging.Consumers;
using Kombats.Players.Infrastructure.Persistence.EF;
using Kombats.Players.Infrastructure.Persistence.Repository;
using Kombats.Players.Infrastructure.Tests.Fixtures;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Kombats.Players.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public sealed class BattleCompletedConsumerTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgresFixture _fixture;

    public BattleCompletedConsumerTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task BattleCompleted_WithWinner_AwardsXpAndRecordsWinLoss()
    {
        var messageId = Guid.NewGuid();
        var winnerId = Guid.NewGuid();
        var loserId = Guid.NewGuid();

        await SeedReadyCharacters(winnerId, loserId);

        var message = CreateBattleCompleted(messageId, winnerId, loserId);
        CapturingProfilePublisher publisher;

        await using (var db = _fixture.CreateDbContext())
        {
            var (consumer, pub) = CreateConsumer(db);
            publisher = pub;
            await consumer.Consume(CreateConsumeContext(message));
        }

        publisher.Published.Should().HaveCount(2);
        publisher.Published.Should().Contain(e => e.IdentityId == winnerId);
        publisher.Published.Should().Contain(e => e.IdentityId == loserId);

        await using (var db = _fixture.CreateDbContext())
        {
            var winner = await db.Characters.FirstAsync(c => c.IdentityId == winnerId);
            winner.TotalXp.Should().Be(10);
            winner.Wins.Should().Be(1);
            winner.Losses.Should().Be(0);

            var loser = await db.Characters.FirstAsync(c => c.IdentityId == loserId);
            loser.TotalXp.Should().Be(5);
            loser.Losses.Should().Be(1);
            loser.Wins.Should().Be(0);
        }
    }

    [Fact]
    public async Task BattleCompleted_Draw_NoXpChangesAndNoProfilePublished()
    {
        var messageId = Guid.NewGuid();
        var message = CreateBattleCompleted(messageId, winnerIdentityId: null, loserIdentityId: null);

        CapturingProfilePublisher publisher;

        await using (var db = _fixture.CreateDbContext())
        {
            var (consumer, pub) = CreateConsumer(db);
            publisher = pub;
            await consumer.Consume(CreateConsumeContext(message));
        }

        publisher.Published.Should().BeEmpty();

        // Inbox entry should still exist
        await using (var db = _fixture.CreateDbContext())
        {
            var inboxRepo = new InboxRepository(db);
            var processed = await inboxRepo.IsProcessedAsync(messageId, CancellationToken.None);
            processed.Should().BeTrue();
        }
    }

    [Fact]
    public async Task BattleCompleted_SameMessageTwice_SecondIsNoOp()
    {
        var messageId = Guid.NewGuid();
        var winnerId = Guid.NewGuid();
        var loserId = Guid.NewGuid();

        await SeedReadyCharacters(winnerId, loserId);

        var message = CreateBattleCompleted(messageId, winnerId, loserId);

        // First consume
        await using (var db = _fixture.CreateDbContext())
        {
            var (consumer, _) = CreateConsumer(db);
            await consumer.Consume(CreateConsumeContext(message));
        }

        // Second consume — same MessageId
        await using (var db = _fixture.CreateDbContext())
        {
            var (consumer, publisher) = CreateConsumer(db);
            await consumer.Consume(CreateConsumeContext(message));
            publisher.Published.Should().BeEmpty("second consume should be a no-op");
        }

        // Verify XP was only awarded once
        await using (var db = _fixture.CreateDbContext())
        {
            var winner = await db.Characters.FirstAsync(c => c.IdentityId == winnerId);
            winner.TotalXp.Should().Be(10, "XP should only be awarded once");
            winner.Wins.Should().Be(1);

            var loser = await db.Characters.FirstAsync(c => c.IdentityId == loserId);
            loser.TotalXp.Should().Be(5, "XP should only be awarded once");
            loser.Losses.Should().Be(1);
        }
    }

    [Fact]
    public async Task BattleCompleted_WinnerNotFound_Throws()
    {
        var messageId = Guid.NewGuid();
        var winnerId = Guid.NewGuid(); // not seeded
        var loserId = Guid.NewGuid();

        await SeedReadyCharacters(loserId); // only seed one

        var message = CreateBattleCompleted(messageId, winnerId, loserId);

        await using var db = _fixture.CreateDbContext();
        var (consumer, _) = CreateConsumer(db);

        var act = () => consumer.Consume(CreateConsumeContext(message));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*WinnerNotFound*");
    }

    private async Task SeedReadyCharacters(params Guid[] identityIds)
    {
        await using var db = _fixture.CreateDbContext();
        foreach (var identityId in identityIds)
        {
            var character = Character.CreateDraft(identityId, Now);
            character.SetNameOnce("Hero" + identityId.ToString()[..4], Now);
            character.AllocatePoints(1, 1, 1, 0, Now);
            db.Characters.Add(character);
        }
        await db.SaveChangesAsync();
    }

    private (BattleCompletedConsumer consumer, CapturingProfilePublisher publisher) CreateConsumer(
        Data.PlayersDbContext db)
    {
        var characterRepo = new CharacterRepository(db);
        var inboxRepo = new InboxRepository(db);
        var uow = new EfUnitOfWork(db);
        var levelingProvider = new LevelingConfigProvider(
            Options.Create(new LevelingOptions()));
        var publisher = new CapturingProfilePublisher();
        ICommandHandler<HandleBattleCompletedCommand> handler =
            new HandleBattleCompletedHandler(
                inboxRepo, characterRepo, levelingProvider, uow, publisher,
                NullLogger<HandleBattleCompletedHandler>.Instance);
        var consumer = new BattleCompletedConsumer(handler);
        return (consumer, publisher);
    }

    private static ConsumeContext<BattleCompleted> CreateConsumeContext(BattleCompleted message)
    {
        var context = Substitute.For<ConsumeContext<BattleCompleted>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }

    private static BattleCompleted CreateBattleCompleted(
        Guid messageId,
        Guid? winnerIdentityId,
        Guid? loserIdentityId)
    {
        return new BattleCompleted
        {
            MessageId = messageId,
            BattleId = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            PlayerAIdentityId = winnerIdentityId ?? Guid.NewGuid(),
            PlayerBIdentityId = loserIdentityId ?? Guid.NewGuid(),
            WinnerIdentityId = winnerIdentityId,
            LoserIdentityId = loserIdentityId,
            Reason = winnerIdentityId.HasValue ? BattleEndReason.Normal : BattleEndReason.DoubleForfeit,
            TurnCount = 5,
            DurationMs = 30000,
            RulesetVersion = 1,
            OccurredAt = DateTimeOffset.UtcNow
        };
    }

    private sealed class CapturingProfilePublisher : ICombatProfilePublisher
    {
        public List<PlayerCombatProfileChanged> Published { get; } = [];

        public Task PublishAsync(PlayerCombatProfileChanged profile, CancellationToken ct)
        {
            Published.Add(profile);
            return Task.CompletedTask;
        }
    }
}
