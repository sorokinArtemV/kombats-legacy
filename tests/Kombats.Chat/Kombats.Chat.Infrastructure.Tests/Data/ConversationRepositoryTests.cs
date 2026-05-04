using FluentAssertions;
using Kombats.Chat.Domain.Conversations;
using Kombats.Chat.Infrastructure.Data;
using Kombats.Chat.Infrastructure.Data.Repositories;
using Kombats.Chat.Infrastructure.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Kombats.Chat.Infrastructure.Tests.Data;

[Collection(PostgresCollection.Name)]
public sealed class ConversationRepositoryTests(PostgresFixture postgres)
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
    public async Task GetOrCreateDirect_FirstCall_CreatesConversation()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var repo = new ConversationRepository(context);

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var (sortedA, sortedB) = Conversation.SortParticipants(a, b);

        var conversation = await repo.GetOrCreateDirectAsync(sortedA, sortedB, CancellationToken.None);

        conversation.Should().NotBeNull();
        conversation!.Type.Should().Be(ConversationType.Direct);
        conversation.ParticipantAIdentityId.Should().Be(sortedA);
        conversation.ParticipantBIdentityId.Should().Be(sortedB);
    }

    [Fact]
    public async Task GetOrCreateDirect_SecondCall_ReturnsSameConversation()
    {
        await using var context1 = CreateContext();
        await context1.Database.MigrateAsync();
        var repo1 = new ConversationRepository(context1);

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var (sortedA, sortedB) = Conversation.SortParticipants(a, b);

        var first = await repo1.GetOrCreateDirectAsync(sortedA, sortedB, CancellationToken.None);

        await using var context2 = CreateContext();
        var repo2 = new ConversationRepository(context2);
        var second = await repo2.GetOrCreateDirectAsync(sortedA, sortedB, CancellationToken.None);

        first!.Id.Should().Be(second!.Id);
    }

    [Fact]
    public async Task GetOrCreateDirect_Concurrent_OnlyOneCreated()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.MigrateAsync();

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var (sortedA, sortedB) = Conversation.SortParticipants(a, b);

        // Run two concurrent GetOrCreateDirect calls
        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var ctx = CreateContext();
            var repo = new ConversationRepository(ctx);
            return await repo.GetOrCreateDirectAsync(sortedA, sortedB, CancellationToken.None);
        }).ToList();

        var results = await Task.WhenAll(tasks);

        // All should return the same conversation ID
        var ids = results.Select(r => r!.Id).Distinct().ToList();
        ids.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListByParticipant_ReturnsGlobalAndDirectConversations()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var repo = new ConversationRepository(context);

        var callerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var (a, b) = Conversation.SortParticipants(callerId, otherId);

        await repo.GetOrCreateDirectAsync(a, b, CancellationToken.None);

        var conversations = await repo.ListByParticipantAsync(callerId, CancellationToken.None);

        // Should include global + DM
        conversations.Should().HaveCountGreaterThanOrEqualTo(2);
        conversations.Should().Contain(c => c.Type == ConversationType.Global);
        conversations.Should().Contain(c => c.Type == ConversationType.Direct);
    }

    [Fact]
    public async Task UpdateLastMessageAt_UpdatesField()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var global = await context.Conversations
            .FirstAsync(c => c.Id == Conversation.GlobalConversationId);

        var repo = new ConversationRepository(context);
        var now = DateTimeOffset.UtcNow;
        await repo.UpdateLastMessageAtAsync(global.Id, now, CancellationToken.None);

        await using var readContext = CreateContext();
        var updated = await readContext.Conversations
            .FirstAsync(c => c.Id == Conversation.GlobalConversationId);

        updated.LastMessageAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }
}
