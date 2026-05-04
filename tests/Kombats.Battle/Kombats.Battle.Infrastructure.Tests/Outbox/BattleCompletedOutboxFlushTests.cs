using FluentAssertions;
using Kombats.Battle.Application.Models;
using Kombats.Battle.Application.Ports;
using Kombats.Battle.Application.ReadModels;
using Kombats.Battle.Application.UseCases.Turns;
using Kombats.Battle.Domain.Engine;
using Kombats.Battle.Domain.Events;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Results;
using Kombats.Battle.Domain.Rules;
using Kombats.Battle.Infrastructure.Data;
using Kombats.Battle.Infrastructure.Data.DbContext;
using Kombats.Battle.Infrastructure.Messaging.Publisher;
using Kombats.Battle.Infrastructure.Tests.Fixtures;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Kombats.Battle.Infrastructure.Tests.Outbox;

/// <summary>
/// Integration regression tests for the BattleCompleted bus-outbox flush in the normal
/// battle-completion path. These tests drive the real BattleTurnAppService through the
/// EndedNow branch using a real Postgres-backed BattleDbContext and a real MassTransit
/// bus configured with UseBusOutbox (identical to production configuration via
/// Kombats.Messaging). Infrastructure ports that would otherwise require Redis/engine
/// are stubbed — the surface under test is specifically "does IPublishEndpoint.Publish
/// followed by the unit-of-work flush produce a row in battle.outbox_message?".
///
/// Without the SaveChangesAsync flush added to CommitAndNotifyBattleEnded, the positive
/// test's outbox query returns zero rows (see NegativeControl test), which is the exact
/// regression that shipped and escaped existing coverage.
/// </summary>
[Collection(PostgresCollection.Name)]
public class BattleCompletedOutboxFlushTests
{
    private readonly PostgresFixture _fixture;

    public BattleCompletedOutboxFlushTests(PostgresFixture fixture) => _fixture = fixture;

    private static readonly PlayerStats DefaultStats = new(10, 10, 10, 10);

    private static readonly CombatBalance Balance = new(
        hp: new HpBalance(50, 10),
        damage: new DamageBalance(5, 1.0m, 0.3m, 0.2m, 0.8m, 1.2m),
        mf: new MfBalance(2, 2),
        dodgeChance: new ChanceBalance(0.10m, 0.01m, 0.40m, 0.30m, 50m),
        critChance: new ChanceBalance(0.10m, 0.01m, 0.40m, 0.30m, 50m),
        critEffect: new CritEffectBalance(CritEffectMode.Multiplier, 1.5m, 0.5m));

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddDbContext<BattleDbContext>(options =>
            options.UseNpgsql(_fixture.ConnectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", BattleDbContext.Schema))
                .UseSnakeCaseNamingConvention());

        services.AddMassTransit(x =>
        {
            x.AddEntityFrameworkOutbox<BattleDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
            });

            x.UsingInMemory((context, cfg) =>
            {
                cfg.ConfigureEndpoints(context);
            });
        });

        return services.BuildServiceProvider();
    }

    private static BattleSnapshot CreateSnapshot(Guid battleId, Guid playerAId, Guid playerBId) => new()
    {
        BattleId = battleId,
        MatchId = Guid.NewGuid(),
        PlayerAId = playerAId,
        PlayerBId = playerBId,
        Phase = BattlePhase.TurnOpen,
        TurnIndex = 1,
        LastResolvedTurnIndex = 0,
        DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(30),
        Ruleset = Ruleset.Create(1, 30, 10, 42, Balance),
        PlayerAHp = 100,
        PlayerBHp = 100,
        NoActionStreakBoth = 0,
        Version = 1
    };

    private static (IBattleStateStore stateStore, IBattleEngine engine, IBattleRealtimeNotifier notifier,
        IActionIntake actionIntake, IClock clock) BuildStubs(
            Guid battleId, Guid playerAId, Guid playerBId, BattleSnapshot snapshot)
    {
        var stateStore = Substitute.For<IBattleStateStore>();
        stateStore.GetStateAsync(battleId, Arg.Any<CancellationToken>()).Returns(snapshot);
        stateStore.TryMarkTurnResolvingAsync(battleId, 1, Arg.Any<CancellationToken>()).Returns(true);
        stateStore.GetActionsAsync(battleId, 1, playerAId, playerBId, Arg.Any<CancellationToken>())
            .Returns(((PlayerActionCommand?)null, (PlayerActionCommand?)null));
        stateStore.EndBattleAndMarkResolvedAsync(
                battleId, 1, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<BattleEndOutcome>(), Arg.Any<CancellationToken>())
            .Returns(EndBattleCommitResult.EndedNow);

        var engine = Substitute.For<IBattleEngine>();
        var battleEnded = new BattleEndedDomainEvent(
            battleId, playerAId, EndBattleReason.Normal, 1, DateTimeOffset.UtcNow);
        var newState = new BattleDomainState(
            battleId, snapshot.MatchId, playerAId, playerBId,
            snapshot.Ruleset, BattlePhase.Ended, 1, 0, 1,
            new PlayerState(playerAId, 100, 80, DefaultStats),
            new PlayerState(playerBId, 100, 0, DefaultStats));
        engine.ResolveTurn(Arg.Any<BattleDomainState>(), Arg.Any<PlayerAction>(), Arg.Any<PlayerAction>())
            .Returns(new BattleResolutionResult { NewState = newState, Events = [battleEnded] });

        var notifier = Substitute.For<IBattleRealtimeNotifier>();
        var actionIntake = Substitute.For<IActionIntake>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        return (stateStore, engine, notifier, actionIntake, clock);
    }

    [Fact]
    public async Task ResolveTurn_NormalCompletion_FlushesBattleCompletedToOutboxTable()
    {
        // Arrange — real DbContext, real MassTransit bus outbox, real publisher + unit of work.
        // Only the Redis-backed ports (state store / engine / notifier) are stubbed to reach
        // the EndedNow branch of CommitAndNotifyBattleEnded.
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();

        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<BattleDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var snapshot = CreateSnapshot(battleId, playerAId, playerBId);
        var (stateStore, engine, notifier, actionIntake, clock) =
            BuildStubs(battleId, playerAId, playerBId, snapshot);

        var publisher = new MassTransitBattleEventPublisher(
            publishEndpoint,
            NullLogger<MassTransitBattleEventPublisher>.Instance);
        var unitOfWork = new BattleUnitOfWork(dbContext);

        var service = new BattleTurnAppService(
            stateStore, engine, notifier, publisher, unitOfWork,
            actionIntake, Substitute.For<IBattleTurnHistoryStore>(), clock,
            NullLogger<BattleTurnAppService>.Instance);

        // Act
        var result = await service.ResolveTurnAsync(battleId, CancellationToken.None);

        // Assert — the app service reached the EndedNow branch and the flush persisted
        // the outbox row. Without SaveChangesAsync this count would be 0 (see NegativeControl).
        result.Should().BeTrue();

        await using var verifyDb = _fixture.CreateDbContext();
        var outboxRows = await verifyDb.Set<OutboxMessage>()
            .Where(m => m.MessageType.Contains("BattleCompleted"))
            .ToListAsync();

        outboxRows.Should().ContainSingle(
            "BattleTurnAppService.CommitAndNotifyBattleEnded must flush IPublishEndpoint.Publish " +
            "through the EF Core bus outbox in the normal Redis-only completion path — exactly " +
            "one BattleCompleted row is expected per ended battle");
    }

    [Fact]
    public async Task ResolveTurn_NormalCompletion_WithoutFlush_LeavesOutboxEmpty_PreFixRegressionGuard()
    {
        // Arrange — identical to the positive test but swaps BattleUnitOfWork for a no-op
        // implementation. This reproduces the exact pre-fix runtime behavior: PublishAsync
        // stages the BattleCompleted event into the DbContext change tracker via the bus
        // outbox interceptor, but SaveChangesAsync is never called, so nothing reaches
        // the outbox_message table. If this test ever starts failing (i.e. an outbox row
        // appears without the unit-of-work flush), the assumptions of this regression
        // suite are wrong and the positive test is no longer meaningful.
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();

        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<BattleDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var snapshot = CreateSnapshot(battleId, playerAId, playerBId);
        var (stateStore, engine, notifier, actionIntake, clock) =
            BuildStubs(battleId, playerAId, playerBId, snapshot);

        var publisher = new MassTransitBattleEventPublisher(
            publishEndpoint,
            NullLogger<MassTransitBattleEventPublisher>.Instance);
        var noOpUnitOfWork = Substitute.For<IBattleUnitOfWork>();

        var service = new BattleTurnAppService(
            stateStore, engine, notifier, publisher, noOpUnitOfWork,
            actionIntake, Substitute.For<IBattleTurnHistoryStore>(), clock,
            NullLogger<BattleTurnAppService>.Instance);

        // Act
        _ = await service.ResolveTurnAsync(battleId, CancellationToken.None);

        // Assert — no flush, no row in the outbox table. This is the bug that shipped.
        await using var verifyDb = _fixture.CreateDbContext();
        var outboxRows = await verifyDb.Set<OutboxMessage>()
            .Where(m => m.MessageType.Contains("BattleCompleted"))
            .ToListAsync();

        outboxRows.Should().BeEmpty(
            "without SaveChangesAsync on the DbContext, UseBusOutbox buffers the publish " +
            "on the change tracker and nothing is written to outbox_message — this was the " +
            "exact pre-fix runtime behavior");
    }
}
