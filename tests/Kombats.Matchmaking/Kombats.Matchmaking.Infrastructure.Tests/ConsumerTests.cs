using FluentAssertions;
using Kombats.Battle.Contracts.Battle;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Domain;
using Kombats.Matchmaking.Infrastructure.Data;
using Kombats.Matchmaking.Infrastructure.Messaging.Consumers;
using Kombats.Matchmaking.Infrastructure.Repositories;
using Kombats.Matchmaking.Infrastructure.Tests.Fixtures;
using Kombats.Players.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Kombats.Matchmaking.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ConsumerTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgresFixture _fixture;

    public ConsumerTests(PostgresFixture fixture) => _fixture = fixture;

    // --- PlayerCombatProfileChangedConsumer ---

    [Fact]
    public async Task ProfileChanged_NewProfile_InsertsProjection()
    {
        var identityId = Guid.NewGuid();
        var message = CreateProfileChanged(identityId, revision: 1, isReady: true, avatarId: "warrior-01");

        await using (var db = _fixture.CreateDbContext())
        {
            var consumer = CreateProfileConsumer(db);
            await consumer.Consume(CreateContext(message));
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);
            var profile = await repo.GetByIdentityIdAsync(identityId);
            profile.Should().NotBeNull();
            profile!.Level.Should().Be(5);
            profile.IsReady.Should().BeTrue();
            profile.Revision.Should().Be(1);
            profile.AvatarId.Should().Be("warrior-01");
        }
    }

    [Fact]
    public async Task ProfileChanged_NullAvatar_PopulatesDefault()
    {
        var identityId = Guid.NewGuid();
        var message = CreateProfileChanged(identityId, revision: 1, isReady: true, avatarId: null);

        await using (var db = _fixture.CreateDbContext())
        {
            var consumer = CreateProfileConsumer(db);
            await consumer.Consume(CreateContext(message));
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);
            var profile = await repo.GetByIdentityIdAsync(identityId);
            profile!.AvatarId.Should().Be("default");
        }
    }

    [Fact]
    public async Task ProfileChanged_HigherRevision_UpdatesProjection()
    {
        var identityId = Guid.NewGuid();

        // Insert initial
        await using (var db = _fixture.CreateDbContext())
        {
            var consumer = CreateProfileConsumer(db);
            await consumer.Consume(CreateContext(CreateProfileChanged(identityId, revision: 1, isReady: false)));
        }

        // Update with higher revision
        await using (var db = _fixture.CreateDbContext())
        {
            var consumer = CreateProfileConsumer(db);
            await consumer.Consume(CreateContext(CreateProfileChanged(identityId, revision: 2, isReady: true, level: 10)));
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);
            var profile = await repo.GetByIdentityIdAsync(identityId);
            profile!.Revision.Should().Be(2);
            profile.IsReady.Should().BeTrue();
            profile.Level.Should().Be(10);
        }
    }

    [Fact]
    public async Task ProfileChanged_StaleRevision_IsIgnored()
    {
        var identityId = Guid.NewGuid();

        // Insert with revision 5
        await using (var db = _fixture.CreateDbContext())
        {
            var consumer = CreateProfileConsumer(db);
            await consumer.Consume(CreateContext(CreateProfileChanged(identityId, revision: 5, isReady: true, level: 10)));
        }

        // Try to update with revision 3 (stale)
        await using (var db = _fixture.CreateDbContext())
        {
            var consumer = CreateProfileConsumer(db);
            await consumer.Consume(CreateContext(CreateProfileChanged(identityId, revision: 3, isReady: false, level: 1)));
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);
            var profile = await repo.GetByIdentityIdAsync(identityId);
            profile!.Revision.Should().Be(5, "stale revision should not overwrite");
            profile.IsReady.Should().BeTrue();
            profile.Level.Should().Be(10);
        }
    }

    // --- BattleCreatedConsumer ---

    [Fact]
    public async Task BattleCreated_AdvancesMatchToBattleCreatedState()
    {
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();

        await SeedMatch(matchId, battleId, playerA, playerB, MatchState.BattleCreateRequested);

        var message = new BattleCreated
        {
            BattleId = battleId,
            MatchId = matchId,
            PlayerAId = playerA,
            PlayerBId = playerB,
            OccurredAt = Now.AddSeconds(10)
        };

        await using (var db = _fixture.CreateDbContext())
        {
            var consumer = CreateBattleCreatedConsumer(db);
            await consumer.Consume(CreateContext(message));
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var match = await repo.GetByMatchIdAsync(matchId);
            match!.State.Should().Be(MatchState.BattleCreated);
        }
    }

    [Fact]
    public async Task BattleCreated_AlreadyAdvanced_IsIdempotent()
    {
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();

        await SeedMatch(matchId, battleId, playerA, playerB, MatchState.BattleCreateRequested);

        var message = new BattleCreated
        {
            BattleId = battleId,
            MatchId = matchId,
            PlayerAId = playerA,
            PlayerBId = playerB,
            OccurredAt = Now.AddSeconds(10)
        };

        // First consume
        await using (var db = _fixture.CreateDbContext())
        {
            var consumer = CreateBattleCreatedConsumer(db);
            await consumer.Consume(CreateContext(message));
        }

        // Second consume — should not throw
        await using (var db = _fixture.CreateDbContext())
        {
            var consumer = CreateBattleCreatedConsumer(db);
            await consumer.Consume(CreateContext(message));
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var match = await repo.GetByMatchIdAsync(matchId);
            match!.State.Should().Be(MatchState.BattleCreated);
        }
    }

    // --- BattleCompletedConsumer ---

    [Fact]
    public async Task BattleCompleted_AdvancesMatchToCompleted()
    {
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();

        await SeedMatch(matchId, battleId, playerA, playerB, MatchState.BattleCreated);

        var statusStore = Substitute.For<IPlayerMatchStatusStore>();

        var message = CreateBattleCompleted(matchId, battleId, playerA, playerB, BattleEndReason.Normal);

        await using (var db = _fixture.CreateDbContext())
        {
            var consumer = CreateBattleCompletedConsumer(db, statusStore);
            await consumer.Consume(CreateContext(message));
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var match = await repo.GetByMatchIdAsync(matchId);
            match!.State.Should().Be(MatchState.Completed);
        }

        // Verify status cleared for both players
        await statusStore.Received(1).RemoveStatusAsync(playerA, Arg.Any<CancellationToken>());
        await statusStore.Received(1).RemoveStatusAsync(playerB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BattleCompleted_Timeout_AdvancesToTimedOut()
    {
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();

        await SeedMatch(matchId, battleId, playerA, playerB, MatchState.BattleCreated);

        var statusStore = Substitute.For<IPlayerMatchStatusStore>();
        var message = CreateBattleCompleted(matchId, battleId, playerA, playerB, BattleEndReason.Timeout);

        await using (var db = _fixture.CreateDbContext())
        {
            var consumer = CreateBattleCompletedConsumer(db, statusStore);
            await consumer.Consume(CreateContext(message));
        }

        await using (var db = _fixture.CreateDbContext())
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
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();

        await SeedMatch(matchId, battleId, playerA, playerB, MatchState.BattleCreated);

        var statusStore = Substitute.For<IPlayerMatchStatusStore>();
        var message = CreateBattleCompleted(matchId, battleId, playerA, playerB, BattleEndReason.Normal);

        // First consume
        await using (var db = _fixture.CreateDbContext())
        {
            var consumer = CreateBattleCompletedConsumer(db, statusStore);
            await consumer.Consume(CreateContext(message));
        }

        // Second consume — should not throw, status still cleared
        await using (var db = _fixture.CreateDbContext())
        {
            var consumer = CreateBattleCompletedConsumer(db, statusStore);
            await consumer.Consume(CreateContext(message));
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
            var match = await repo.GetByMatchIdAsync(matchId);
            match!.State.Should().Be(MatchState.Completed);
        }
    }

    // --- Helpers ---

    private async Task SeedMatch(Guid matchId, Guid battleId, Guid playerA, Guid playerB, MatchState targetState)
    {
        await using var db = _fixture.CreateDbContext();
        var match = Match.Create(matchId, battleId, playerA, playerB, "default", Now);
        match.MarkBattleCreateRequested(Now);
        if (targetState == MatchState.BattleCreated)
            match.TryMarkBattleCreated(Now.AddSeconds(5));

        var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
        repo.Add(match);
        await db.SaveChangesAsync();
    }

    private PlayerCombatProfileChangedConsumer CreateProfileConsumer(MatchmakingDbContext db)
    {
        var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);
        return new PlayerCombatProfileChangedConsumer(repo, NullLogger<PlayerCombatProfileChangedConsumer>.Instance);
    }

    private BattleCreatedConsumer CreateBattleCreatedConsumer(MatchmakingDbContext db)
    {
        var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
        return new BattleCreatedConsumer(repo, NullLogger<BattleCreatedConsumer>.Instance);
    }

    private BattleCompletedConsumer CreateBattleCompletedConsumer(MatchmakingDbContext db, IPlayerMatchStatusStore statusStore)
    {
        var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);
        return new BattleCompletedConsumer(repo, statusStore, NullLogger<BattleCompletedConsumer>.Instance);
    }

    private static ConsumeContext<T> CreateContext<T>(T message) where T : class
    {
        var context = Substitute.For<ConsumeContext<T>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }

    private static PlayerCombatProfileChanged CreateProfileChanged(
        Guid identityId, int revision, bool isReady, int level = 5, string? avatarId = "default") => new()
    {
        MessageId = Guid.NewGuid(),
        IdentityId = identityId,
        CharacterId = Guid.NewGuid(),
        Name = "Hero",
        Level = level,
        Strength = 10,
        Agility = 8,
        Intuition = 6,
        Vitality = 12,
        IsReady = isReady,
        Revision = revision,
        AvatarId = avatarId,
        OccurredAt = Now
    };

    private static BattleCompleted CreateBattleCompleted(
        Guid matchId, Guid battleId, Guid playerA, Guid playerB, BattleEndReason reason) => new()
    {
        MessageId = Guid.NewGuid(),
        BattleId = battleId,
        MatchId = matchId,
        PlayerAIdentityId = playerA,
        PlayerBIdentityId = playerB,
        WinnerIdentityId = reason == BattleEndReason.Normal ? playerA : null,
        LoserIdentityId = reason == BattleEndReason.Normal ? playerB : null,
        Reason = reason,
        TurnCount = 5,
        DurationMs = 30000,
        RulesetVersion = 1,
        OccurredAt = Now.AddSeconds(30)
    };
}
