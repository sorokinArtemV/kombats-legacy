using FluentAssertions;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Infrastructure.Repositories;
using Kombats.Matchmaking.Infrastructure.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kombats.Matchmaking.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PlayerCombatProfileRepositoryTests
{
    private readonly PostgresFixture _fixture;

    public PlayerCombatProfileRepositoryTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Upsert_Insert_And_GetByIdentityId_RoundTrip()
    {
        await using var db = _fixture.CreateDbContext();
        var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);

        var identityId = Guid.NewGuid();
        var profile = new PlayerCombatProfile
        {
            IdentityId = identityId,
            CharacterId = Guid.NewGuid(),
            Name = "TestPlayer",
            Level = 5,
            Strength = 10,
            Agility = 8,
            Intuition = 6,
            Vitality = 12,
            IsReady = true,
            Revision = 1,
            OccurredAt = DateTimeOffset.UtcNow,
            AvatarId = "warrior-01"
        };

        var result = await repo.UpsertAsync(profile);
        result.Should().BeTrue();

        await using var db2 = _fixture.CreateDbContext();
        var repo2 = new PlayerCombatProfileRepository(db2, NullLogger<PlayerCombatProfileRepository>.Instance);
        var loaded = await repo2.GetByIdentityIdAsync(identityId);

        loaded.Should().NotBeNull();
        loaded!.IdentityId.Should().Be(identityId);
        loaded.Name.Should().Be("TestPlayer");
        loaded.Level.Should().Be(5);
        loaded.Strength.Should().Be(10);
        loaded.IsReady.Should().BeTrue();
        loaded.Revision.Should().Be(1);
        loaded.AvatarId.Should().Be("warrior-01");
    }

    [Fact]
    public async Task Upsert_Update_WithHigherRevision_Succeeds()
    {
        await using var db = _fixture.CreateDbContext();
        var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);

        var identityId = Guid.NewGuid();
        var profile1 = new PlayerCombatProfile
        {
            IdentityId = identityId, CharacterId = Guid.NewGuid(), Name = "V1",
            Level = 1, Strength = 5, Agility = 5, Intuition = 5, Vitality = 5,
            IsReady = false, Revision = 1, OccurredAt = DateTimeOffset.UtcNow
        };
        await repo.UpsertAsync(profile1);

        await using var db2 = _fixture.CreateDbContext();
        var repo2 = new PlayerCombatProfileRepository(db2, NullLogger<PlayerCombatProfileRepository>.Instance);
        var profile2 = new PlayerCombatProfile
        {
            IdentityId = identityId, CharacterId = profile1.CharacterId, Name = "V2",
            Level = 2, Strength = 10, Agility = 10, Intuition = 10, Vitality = 10,
            IsReady = true, Revision = 2, OccurredAt = DateTimeOffset.UtcNow
        };
        var result = await repo2.UpsertAsync(profile2);
        result.Should().BeTrue();

        await using var db3 = _fixture.CreateDbContext();
        var repo3 = new PlayerCombatProfileRepository(db3, NullLogger<PlayerCombatProfileRepository>.Instance);
        var loaded = await repo3.GetByIdentityIdAsync(identityId);
        loaded!.Name.Should().Be("V2");
        loaded.Level.Should().Be(2);
        loaded.Revision.Should().Be(2);
        loaded.IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task Upsert_Update_WithSameOrLowerRevision_IsRejected()
    {
        await using var db = _fixture.CreateDbContext();
        var repo = new PlayerCombatProfileRepository(db, NullLogger<PlayerCombatProfileRepository>.Instance);

        var identityId = Guid.NewGuid();
        var profile1 = new PlayerCombatProfile
        {
            IdentityId = identityId, CharacterId = Guid.NewGuid(), Name = "Latest",
            Level = 5, Strength = 10, Agility = 10, Intuition = 10, Vitality = 10,
            IsReady = true, Revision = 5, OccurredAt = DateTimeOffset.UtcNow
        };
        await repo.UpsertAsync(profile1);

        await using var db2 = _fixture.CreateDbContext();
        var repo2 = new PlayerCombatProfileRepository(db2, NullLogger<PlayerCombatProfileRepository>.Instance);
        var staleProfile = new PlayerCombatProfile
        {
            IdentityId = identityId, CharacterId = profile1.CharacterId, Name = "Stale",
            Level = 3, Strength = 6, Agility = 6, Intuition = 6, Vitality = 6,
            IsReady = false, Revision = 3, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        var result = await repo2.UpsertAsync(staleProfile);
        result.Should().BeFalse();

        await using var db3 = _fixture.CreateDbContext();
        var repo3 = new PlayerCombatProfileRepository(db3, NullLogger<PlayerCombatProfileRepository>.Instance);
        var loaded = await repo3.GetByIdentityIdAsync(identityId);
        loaded!.Name.Should().Be("Latest");
        loaded.Revision.Should().Be(5);
    }
}
