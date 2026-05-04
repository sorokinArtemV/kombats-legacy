using FluentAssertions;
using Kombats.Players.Domain.Entities;
using Kombats.Players.Infrastructure.Persistence.Repository;
using Kombats.Players.Infrastructure.Tests.Fixtures;
using Xunit;

namespace Kombats.Players.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public sealed class CharacterPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgresFixture _fixture;

    public CharacterPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task RoundTrip_DraftCharacter_AllFieldsPersisted()
    {
        var identityId = Guid.NewGuid();
        var character = Character.CreateDraft(identityId, Now);
        var characterId = character.Id;

        await using (var db = _fixture.CreateDbContext())
        {
            db.Characters.Add(character);
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var loaded = await new CharacterRepository(db).GetByIdAsync(characterId, CancellationToken.None);

            loaded.Should().NotBeNull();
            loaded!.Id.Should().Be(characterId);
            loaded.IdentityId.Should().Be(identityId);
            loaded.Name.Should().BeNull();
            loaded.Strength.Should().Be(3);
            loaded.Agility.Should().Be(3);
            loaded.Intuition.Should().Be(3);
            loaded.Vitality.Should().Be(3);
            loaded.UnspentPoints.Should().Be(3);
            loaded.TotalXp.Should().Be(0);
            loaded.Level.Should().Be(0);
            loaded.LevelingVersion.Should().Be(1);
            loaded.Wins.Should().Be(0);
            loaded.Losses.Should().Be(0);
            loaded.Revision.Should().Be(1);
            loaded.OnboardingState.Should().Be(OnboardingState.Draft);
            loaded.AvatarId.Should().Be(AvatarCatalog.Default);
            loaded.Created.Should().Be(Now);
            loaded.Updated.Should().Be(Now);
        }
    }

    [Fact]
    public async Task RoundTrip_ReadyCharacter_AllFieldsPersisted()
    {
        var identityId = Guid.NewGuid();
        var character = Character.CreateDraft(identityId, Now);
        character.SetNameOnce("TestHero", Now);
        character.AllocatePoints(1, 1, 1, 0, Now);
        var characterId = character.Id;

        await using (var db = _fixture.CreateDbContext())
        {
            db.Characters.Add(character);
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var loaded = await new CharacterRepository(db).GetByIdAsync(characterId, CancellationToken.None);

            loaded.Should().NotBeNull();
            loaded!.Name.Should().Be("TestHero");
            loaded.Strength.Should().Be(4);
            loaded.Agility.Should().Be(4);
            loaded.Intuition.Should().Be(4);
            loaded.Vitality.Should().Be(3);
            loaded.UnspentPoints.Should().Be(0);
            loaded.OnboardingState.Should().Be(OnboardingState.Ready);
        }
    }

    [Fact]
    public async Task GetByIdentityIdAsync_ReturnsCorrectCharacter()
    {
        var identityId = Guid.NewGuid();
        var character = Character.CreateDraft(identityId, Now);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Characters.Add(character);
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var loaded = await new CharacterRepository(db).GetByIdentityIdAsync(identityId, CancellationToken.None);
            loaded.Should().NotBeNull();
            loaded!.IdentityId.Should().Be(identityId);
        }
    }

    [Fact]
    public async Task GetByIdentityIdAsync_ReturnsNull_WhenNotFound()
    {
        await using var db = _fixture.CreateDbContext();
        var loaded = await new CharacterRepository(db).GetByIdentityIdAsync(Guid.NewGuid(), CancellationToken.None);
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_ThenSave_PersistsCharacter()
    {
        var identityId = Guid.NewGuid();
        var character = Character.CreateDraft(identityId, Now);

        await using (var db = _fixture.CreateDbContext())
        {
            var repo = new CharacterRepository(db);
            await repo.AddAsync(character, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var loaded = await new CharacterRepository(db).GetByIdAsync(character.Id, CancellationToken.None);
            loaded.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task UpdateCharacter_PersistsChanges()
    {
        var identityId = Guid.NewGuid();
        var character = Character.CreateDraft(identityId, Now);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Characters.Add(character);
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var repo = new CharacterRepository(db);
            var loaded = await repo.GetByIdentityIdAsync(identityId, CancellationToken.None);
            loaded!.SetNameOnce("UpdatedHero", Now);
            loaded.AllocatePoints(1, 1, 1, 0, Now);
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var loaded = await new CharacterRepository(db).GetByIdentityIdAsync(identityId, CancellationToken.None);
            loaded.Should().NotBeNull();
            loaded!.Name.Should().Be("UpdatedHero");
            loaded.Strength.Should().Be(4);
            loaded.Agility.Should().Be(4);
            loaded.Intuition.Should().Be(4);
            loaded.Vitality.Should().Be(3);
            loaded.UnspentPoints.Should().Be(0);
            loaded.OnboardingState.Should().Be(OnboardingState.Ready);
            loaded.Revision.Should().Be(3);
        }
    }

    [Fact]
    public async Task IsNameTakenAsync_ReturnsFalse_WhenNoMatchingName()
    {
        await using var db = _fixture.CreateDbContext();
        var repo = new CharacterRepository(db);
        var taken = await repo.IsNameTakenAsync("nonexistent", null, CancellationToken.None);
        taken.Should().BeFalse();
    }

    [Fact]
    public async Task IsNameTakenAsync_ReturnsTrue_WhenNameExists()
    {
        var character = Character.CreateDraft(Guid.NewGuid(), Now);
        character.SetNameOnce("TakenName", Now);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Characters.Add(character);
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var repo = new CharacterRepository(db);
            var taken = await repo.IsNameTakenAsync("takenname", null, CancellationToken.None);
            taken.Should().BeTrue();
        }
    }

    [Fact]
    public async Task IsNameTakenAsync_ExcludesOwnCharacter()
    {
        var character = Character.CreateDraft(Guid.NewGuid(), Now);
        character.SetNameOnce("MyOwnName", Now);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Characters.Add(character);
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var repo = new CharacterRepository(db);
            var taken = await repo.IsNameTakenAsync("myownname", character.Id, CancellationToken.None);
            taken.Should().BeFalse();
        }
    }
}
