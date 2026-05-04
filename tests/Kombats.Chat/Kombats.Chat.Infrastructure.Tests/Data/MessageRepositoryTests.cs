using FluentAssertions;
using Kombats.Chat.Domain.Conversations;
using Kombats.Chat.Domain.Messages;
using Kombats.Chat.Infrastructure.Data;
using Kombats.Chat.Infrastructure.Data.Repositories;
using Kombats.Chat.Infrastructure.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Kombats.Chat.Infrastructure.Tests.Data;

[Collection(PostgresCollection.Name)]
public sealed class MessageRepositoryTests(PostgresFixture postgres)
{
    private ChatDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseNpgsql(postgres.ConnectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", ChatDbContext.Schema);
            })
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IHistoryRepository, SnakeCaseHistoryRepository>()
            .Options;

        return new ChatDbContext(options);
    }

    [Fact]
    public async Task Save_And_GetByConversation_RoundTrip()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var global = await context.Conversations
            .FirstAsync(c => c.Id == Conversation.GlobalConversationId);

        var repo = new MessageRepository(context);
        var msg = Message.Create(global.Id, Guid.NewGuid(), "Player1", "Hello", DateTimeOffset.UtcNow);

        await repo.SaveAsync(msg, CancellationToken.None);

        await using var readContext = CreateContext();
        var readRepo = new MessageRepository(readContext);
        var messages = await readRepo.GetByConversationAsync(global.Id, null, 10, CancellationToken.None);

        messages.Should().Contain(m => m.Id == msg.Id);
    }

    [Fact]
    public async Task GetByConversation_CursorPagination_WorksCorrectly()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var global = await context.Conversations
            .FirstAsync(c => c.Id == Conversation.GlobalConversationId);

        var repo = new MessageRepository(context);
        var now = DateTimeOffset.UtcNow;

        // Create 5 messages at different times
        for (int i = 0; i < 5; i++)
        {
            var msg = Message.Create(global.Id, Guid.NewGuid(), "P", $"msg{i}", now.AddSeconds(i));
            await repo.SaveAsync(msg, CancellationToken.None);
        }

        await using var readContext = CreateContext();
        var readRepo = new MessageRepository(readContext);

        // Get first page of 3
        var page1 = await readRepo.GetByConversationAsync(global.Id, null, 3, CancellationToken.None);
        page1.Should().HaveCount(3);

        // Messages should be ordered by SentAt DESC (newest first)
        page1[0].SentAt.Should().BeOnOrAfter(page1[1].SentAt);

        // Get next page using cursor
        var oldest = page1[^1].SentAt;
        var page2 = await readRepo.GetByConversationAsync(global.Id, oldest, 10, CancellationToken.None);

        // Should get remaining messages before the cursor
        page2.Should().NotBeEmpty();
        page2.Should().OnlyContain(m => m.SentAt < oldest);
    }

    [Fact]
    public async Task DeleteExpired_RemovesOldMessages()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var global = await context.Conversations
            .FirstAsync(c => c.Id == Conversation.GlobalConversationId);

        var repo = new MessageRepository(context);
        var now = DateTimeOffset.UtcNow;

        // Create an old message and a new message
        var oldMsg = Message.Create(global.Id, Guid.NewGuid(), "P", "old", now.AddDays(-2));
        var newMsg = Message.Create(global.Id, Guid.NewGuid(), "P", "new", now);

        await repo.SaveAsync(oldMsg, CancellationToken.None);
        await repo.SaveAsync(newMsg, CancellationToken.None);

        // Delete messages older than 1 day
        int deleted = await repo.DeleteExpiredAsync(now.AddDays(-1), 100, CancellationToken.None);

        deleted.Should().BeGreaterThanOrEqualTo(1);

        // New message should still exist
        await using var readContext = CreateContext();
        var remaining = await readContext.Messages.FirstOrDefaultAsync(m => m.Id == newMsg.Id);
        remaining.Should().NotBeNull();
    }
}
