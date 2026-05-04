using FluentAssertions;
using Kombats.Players.Infrastructure.Persistence.Repository;
using Kombats.Players.Infrastructure.Tests.Fixtures;
using Xunit;

namespace Kombats.Players.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public sealed class InboxPersistenceTests
{
    private readonly PostgresFixture _fixture;

    public InboxPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task IsProcessedAsync_ReturnsFalse_WhenNotProcessed()
    {
        await using var db = _fixture.CreateDbContext();
        var repo = new InboxRepository(db);

        var result = await repo.IsProcessedAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task AddProcessedAsync_ThenIsProcessed_ReturnsTrue()
    {
        var messageId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var repo = new InboxRepository(db);
            await repo.AddProcessedAsync(messageId, DateTimeOffset.UtcNow, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var repo = new InboxRepository(db);
            var result = await repo.IsProcessedAsync(messageId, CancellationToken.None);
            result.Should().BeTrue();
        }
    }

    [Fact]
    public async Task AddProcessedAsync_DuplicateMessageId_ThrowsOnSave()
    {
        var messageId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            var repo = new InboxRepository(db);
            await repo.AddProcessedAsync(messageId, DateTimeOffset.UtcNow, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var repo = new InboxRepository(db);
            await repo.AddProcessedAsync(messageId, DateTimeOffset.UtcNow, CancellationToken.None);

            var act = () => db.SaveChangesAsync();
            await act.Should().ThrowAsync<Exception>();
        }
    }
}
