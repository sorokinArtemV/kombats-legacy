using FluentAssertions;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Infrastructure.Data;
using Kombats.Matchmaking.Infrastructure.Messaging.Consumers;
using Kombats.Matchmaking.Infrastructure.Repositories;
using Kombats.Players.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kombats.Integration.Tests;

/// <summary>
/// I-01: Verify Players → Matchmaking Event Flow.
/// Verifies that PlayerCombatProfileChanged events published by Players
/// are correctly consumed by Matchmaking's PlayerCombatProfileChangedConsumer,
/// resulting in correct projection upsert with revision monotonicity.
/// Uses real PostgreSQL via Testcontainers for the Matchmaking DbContext.
/// </summary>
public sealed class I01_PlayersToMatchmakingFlowTests : IAsyncLifetime
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
    public async Task PlayerCombatProfileChanged_CreatesProjectionInMatchmaking()
    {
        var identityId = Guid.NewGuid();
        var characterId = Guid.NewGuid();

        var message = new PlayerCombatProfileChanged
        {
            MessageId = Guid.NewGuid(),
            IdentityId = identityId,
            CharacterId = characterId,
            Name = "TestHero",
            Level = 5,
            Strength = 10,
            Agility = 8,
            Intuition = 6,
            Vitality = 12,
            IsReady = true,
            Revision = 1,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        // Simulate consumer processing
        await using (var db = CreateDbContext())
        {
            var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);
            var consumer = new PlayerCombatProfileChangedConsumer(repo, NullLogger<PlayerCombatProfileChangedConsumer>.Instance);
            await consumer.Consume(CreateContext(message));
        }

        // Verify projection was created
        await using (var db = CreateDbContext())
        {
            var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);
            var profile = await repo.GetByIdentityIdAsync(identityId);

            profile.Should().NotBeNull();
            profile!.IdentityId.Should().Be(identityId);
            profile.CharacterId.Should().Be(characterId);
            profile.Name.Should().Be("TestHero");
            profile.Level.Should().Be(5);
            profile.Strength.Should().Be(10);
            profile.Agility.Should().Be(8);
            profile.Intuition.Should().Be(6);
            profile.Vitality.Should().Be(12);
            profile.IsReady.Should().BeTrue();
            profile.Revision.Should().Be(1);
        }
    }

    [Fact]
    public async Task PlayerCombatProfileChanged_UpdatesExistingProjection_WhenRevisionIsNewer()
    {
        var identityId = Guid.NewGuid();
        var characterId = Guid.NewGuid();

        // First message: revision 1
        var message1 = new PlayerCombatProfileChanged
        {
            MessageId = Guid.NewGuid(),
            IdentityId = identityId,
            CharacterId = characterId,
            Name = "V1",
            Level = 1,
            Strength = 5,
            Agility = 5,
            Intuition = 5,
            Vitality = 5,
            IsReady = false,
            Revision = 1,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        await using (var db = CreateDbContext())
        {
            var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);
            var consumer = new PlayerCombatProfileChangedConsumer(repo, NullLogger<PlayerCombatProfileChangedConsumer>.Instance);
            await consumer.Consume(CreateContext(message1));
        }

        // Second message: revision 2 (newer)
        var message2 = new PlayerCombatProfileChanged
        {
            MessageId = Guid.NewGuid(),
            IdentityId = identityId,
            CharacterId = characterId,
            Name = "V2",
            Level = 5,
            Strength = 10,
            Agility = 10,
            Intuition = 10,
            Vitality = 10,
            IsReady = true,
            Revision = 2,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        await using (var db = CreateDbContext())
        {
            var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);
            var consumer = new PlayerCombatProfileChangedConsumer(repo, NullLogger<PlayerCombatProfileChangedConsumer>.Instance);
            await consumer.Consume(CreateContext(message2));
        }

        // Verify updated
        await using (var db = CreateDbContext())
        {
            var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);
            var profile = await repo.GetByIdentityIdAsync(identityId);
            profile!.Name.Should().Be("V2");
            profile.Level.Should().Be(5);
            profile.IsReady.Should().BeTrue();
            profile.Revision.Should().Be(2);
        }
    }

    [Fact]
    public async Task PlayerCombatProfileChanged_StaleRevision_IsIgnored()
    {
        var identityId = Guid.NewGuid();
        var characterId = Guid.NewGuid();

        // First message: revision 5
        var message1 = new PlayerCombatProfileChanged
        {
            MessageId = Guid.NewGuid(),
            IdentityId = identityId,
            CharacterId = characterId,
            Name = "Latest",
            Level = 10,
            Strength = 20,
            Agility = 20,
            Intuition = 20,
            Vitality = 20,
            IsReady = true,
            Revision = 5,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        await using (var db = CreateDbContext())
        {
            var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);
            var consumer = new PlayerCombatProfileChangedConsumer(repo, NullLogger<PlayerCombatProfileChangedConsumer>.Instance);
            await consumer.Consume(CreateContext(message1));
        }

        // Second message: revision 3 (stale — should be ignored)
        var message2 = new PlayerCombatProfileChanged
        {
            MessageId = Guid.NewGuid(),
            IdentityId = identityId,
            CharacterId = characterId,
            Name = "Stale",
            Level = 3,
            Strength = 6,
            Agility = 6,
            Intuition = 6,
            Vitality = 6,
            IsReady = false,
            Revision = 3,
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            Version = 1
        };

        await using (var db = CreateDbContext())
        {
            var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);
            var consumer = new PlayerCombatProfileChangedConsumer(repo, NullLogger<PlayerCombatProfileChangedConsumer>.Instance);
            await consumer.Consume(CreateContext(message2));
        }

        // Verify original data preserved
        await using (var db = CreateDbContext())
        {
            var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);
            var profile = await repo.GetByIdentityIdAsync(identityId);
            profile!.Name.Should().Be("Latest");
            profile.Revision.Should().Be(5);
            profile.IsReady.Should().BeTrue();
        }
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

    private static ConsumeContext<PlayerCombatProfileChanged> CreateContext(PlayerCombatProfileChanged message)
    {
        var ctx = Substitute.For<ConsumeContext<PlayerCombatProfileChanged>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }
}
