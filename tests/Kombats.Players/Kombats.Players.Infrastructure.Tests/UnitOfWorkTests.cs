using FluentAssertions;
using Kombats.Players.Application.Abstractions;
using Kombats.Players.Domain.Entities;
using Kombats.Players.Infrastructure.Persistence.EF;
using Kombats.Players.Infrastructure.Tests.Fixtures;
using Xunit;

namespace Kombats.Players.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public sealed class UnitOfWorkTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgresFixture _fixture;

    public UnitOfWorkTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SaveChangesAsync_DuplicateIdentityId_ThrowsUniqueConstraintConflict()
    {
        var identityId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            db.Characters.Add(Character.CreateDraft(identityId, Now));
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            db.Characters.Add(Character.CreateDraft(identityId, Now));
            var uow = new EfUnitOfWork(db);

            var act = () => uow.SaveChangesAsync(CancellationToken.None);

            var ex = await act.Should().ThrowAsync<UniqueConstraintConflictException>();
            ex.Which.ConflictKind.Should().Be(UniqueConflictKind.IdentityId);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_DuplicateNormalizedName_ThrowsUniqueConstraintConflict()
    {
        var c1 = Character.CreateDraft(Guid.NewGuid(), Now);
        c1.SetNameOnce("DupeName", Now);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Characters.Add(c1);
            await db.SaveChangesAsync();
        }

        var c2 = Character.CreateDraft(Guid.NewGuid(), Now);
        c2.SetNameOnce("dupename", Now);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Characters.Add(c2);
            var uow = new EfUnitOfWork(db);

            var act = () => uow.SaveChangesAsync(CancellationToken.None);

            var ex = await act.Should().ThrowAsync<UniqueConstraintConflictException>();
            ex.Which.ConflictKind.Should().Be(UniqueConflictKind.CharacterName);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_ConcurrentModification_ThrowsConcurrencyConflict()
    {
        var identityId = Guid.NewGuid();
        var character = Character.CreateDraft(identityId, Now);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Characters.Add(character);
            await db.SaveChangesAsync();
        }

        await using var db1 = _fixture.CreateDbContext();
        await using var db2 = _fixture.CreateDbContext();

        var char1 = await db1.Characters.FindAsync(character.Id);
        var char2 = await db2.Characters.FindAsync(character.Id);

        char1!.SetNameOnce("First", Now);
        char2!.SetNameOnce("Second", Now);

        await db1.SaveChangesAsync();

        var uow2 = new EfUnitOfWork(db2);
        var act = () => uow2.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }
}
