using FluentAssertions;
using Kombats.Players.Contracts;
using Kombats.Players.Domain.Entities;
using Kombats.Players.Infrastructure.Data;
using Kombats.Players.Infrastructure.Tests.Fixtures;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kombats.Players.Infrastructure.Tests;

/// <summary>
/// Verifies that MassTransit outbox entries and domain entities are committed
/// atomically through a single SaveChangesAsync call (AD-01).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OutboxAtomicityTests
{
    private readonly PostgresFixture _fixture;

    public OutboxAtomicityTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task PublishBeforeSave_CommitsDomainEntityAndOutboxAtomically()
    {
        // Build a minimal DI container with MassTransit InMemory transport + EF Core outbox.
        // This mirrors the real AddMessaging<PlayersDbContext> config but without RabbitMQ.
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        services.AddDbContext<PlayersDbContext>(options =>
        {
            options
                .UseNpgsql(_fixture.ConnectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", PlayersDbContext.Schema))
                .UseSnakeCaseNamingConvention()
                .ReplaceService<IHistoryRepository, SnakeCaseHistoryRepository>();
        });

        services.AddMassTransit(x =>
        {
            x.AddEntityFrameworkOutbox<PlayersDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
            });

            x.UsingInMemory((context, cfg) =>
            {
                cfg.ConfigureEndpoints(context);
            });
        });

        await using var provider = services.BuildServiceProvider();

        // Start MassTransit bus (required for bus outbox to function)
        var busControl = provider.GetRequiredService<IBusControl>();
        await busControl.StartAsync(CancellationToken.None);

        try
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlayersDbContext>();
            var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

            // Create a domain entity
            var identityId = Guid.NewGuid();
            var character = Character.CreateDraft(identityId, DateTimeOffset.UtcNow);
            db.Characters.Add(character);

            // Publish via outbox — this adds outbox entities to the DbContext
            await publishEndpoint.Publish(new PlayerCombatProfileChanged
            {
                MessageId = Guid.NewGuid(),
                IdentityId = identityId,
                CharacterId = character.Id,
                Name = null,
                Level = 0,
                Strength = 3,
                Agility = 3,
                Intuition = 3,
                Vitality = 3,
                IsReady = false,
                Revision = 1,
                OccurredAt = DateTimeOffset.UtcNow,
                Version = 1
            });

            // Single SaveChanges commits both domain entity and outbox entry atomically
            await db.SaveChangesAsync();

            // Verify domain entity persisted
            await using var verifyDb = _fixture.CreateDbContext();
            var saved = await verifyDb.Characters.AnyAsync(c => c.Id == character.Id);
            saved.Should().BeTrue("domain entity should be persisted");

            // Verify outbox entry persisted (query outbox table via raw SQL)
            var connection = verifyDb.Database.GetDbConnection();
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {PlayersDbContext.Schema}.outbox_message";
            var outboxCount = (long)(await cmd.ExecuteScalarAsync())!;
            outboxCount.Should().BeGreaterThan(0, "outbox should contain the published message");
        }
        finally
        {
            await busControl.StopAsync(CancellationToken.None);
        }
    }
}
