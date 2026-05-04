using FluentAssertions;
using Kombats.Chat.Application.Repositories;
using Kombats.Chat.Domain.Conversations;
using Kombats.Chat.Domain.Messages;
using Kombats.Chat.Infrastructure.Data;
using Kombats.Chat.Infrastructure.Data.Repositories;
using Kombats.Chat.Infrastructure.Options;
using Kombats.Chat.Infrastructure.Tests.Fixtures;
using Kombats.Chat.Infrastructure.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Kombats.Chat.Infrastructure.Tests.Workers;

[Collection(PostgresCollection.Name)]
public sealed class MessageRetentionWorkerTests(PostgresFixture postgres)
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

    private async Task<Guid> EnsureMigrated()
    {
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
        return Conversation.GlobalConversationId;
    }

    private (MessageRetentionWorker worker, ServiceProvider sp) BuildWorker(MessageRetentionOptions opts)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ChatDbContext>(o =>
            o.UseNpgsql(postgres.ConnectionString, n =>
                n.MigrationsHistoryTable("__ef_migrations_history", ChatDbContext.Schema))
             .UseSnakeCaseNamingConvention()
             .ReplaceService<IHistoryRepository, SnakeCaseHistoryRepository>());

        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();

        var sp = services.BuildServiceProvider();

        var worker = new MessageRetentionWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MessageRetentionWorker>.Instance,
            new FakeOptionsMonitor<MessageRetentionOptions>(opts));

        return (worker, sp);
    }

    private async Task ClearMessagesAndDirectConversations()
    {
        await using var ctx = CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM chat.messages");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM chat.conversations WHERE type = 1");
    }

    [Fact]
    public async Task RunOnce_OldMessages_AreDeleted_NewKept()
    {
        await EnsureMigrated();
        await ClearMessagesAndDirectConversations();

        var globalId = Conversation.GlobalConversationId;
        var oldTime = DateTimeOffset.UtcNow.AddHours(-48);
        var newTime = DateTimeOffset.UtcNow.AddHours(-1);

        await using (var ctx = CreateContext())
        {
            var repo = new MessageRepository(ctx);
            await repo.SaveAsync(Message.Create(globalId, Guid.NewGuid(), "P", "old-1", oldTime), CancellationToken.None);
            await repo.SaveAsync(Message.Create(globalId, Guid.NewGuid(), "P", "old-2", oldTime), CancellationToken.None);
            await repo.SaveAsync(Message.Create(globalId, Guid.NewGuid(), "P", "new-1", newTime), CancellationToken.None);
        }

        var (worker, sp) = BuildWorker(new MessageRetentionOptions
        {
            MessageTtlHours = 24,
            BatchSize = 1000,
            MaxBatchesPerPass = 100,
        });

        await worker.RunOnceAsync(CancellationToken.None);

        await using (var ctx = CreateContext())
        {
            var remaining = await ctx.Messages.Where(m => m.ConversationId == globalId).ToListAsync();
            remaining.Should().HaveCount(1);
            remaining[0].Content.Should().Be("new-1");
        }

        await sp.DisposeAsync();
    }

    [Fact]
    public async Task RunOnce_GlobalConversation_IsNeverDeleted()
    {
        await EnsureMigrated();
        await ClearMessagesAndDirectConversations();

        var (worker, sp) = BuildWorker(new MessageRetentionOptions { MessageTtlHours = 0, BatchSize = 1000, MaxBatchesPerPass = 10 });

        await worker.RunOnceAsync(CancellationToken.None);

        await using (var ctx = CreateContext())
        {
            bool globalPresent = await ctx.Conversations.AnyAsync(c => c.Id == Conversation.GlobalConversationId);
            globalPresent.Should().BeTrue();
        }

        await sp.DisposeAsync();
    }

    [Fact]
    public async Task RunOnce_EmptyDirectConversation_IsDeleted()
    {
        await EnsureMigrated();
        await ClearMessagesAndDirectConversations();

        var participantA = Guid.NewGuid();
        var participantB = Guid.NewGuid();

        await using (var ctx = CreateContext())
        {
            var convRepo = new ConversationRepository(ctx);
            var conv = await convRepo.GetOrCreateDirectAsync(participantA, participantB, CancellationToken.None);
            conv.Should().NotBeNull();

            // Insert a message and then age it out so the worker will delete it.
            var old = DateTimeOffset.UtcNow.AddHours(-48);
            var msgRepo = new MessageRepository(ctx);
            await msgRepo.SaveAsync(
                Message.Create(conv!.Id, participantA, "A", "aged-dm", old),
                CancellationToken.None);
        }

        var (worker, sp) = BuildWorker(new MessageRetentionOptions { MessageTtlHours = 24, BatchSize = 1000, MaxBatchesPerPass = 10 });

        await worker.RunOnceAsync(CancellationToken.None);

        await using (var ctx = CreateContext())
        {
            bool stillPresent = await ctx.Conversations.AnyAsync(c =>
                c.Type == ConversationType.Direct
                && c.ParticipantAIdentityId != null
                && (c.ParticipantAIdentityId == participantA || c.ParticipantAIdentityId == participantB));
            stillPresent.Should().BeFalse();
        }

        await sp.DisposeAsync();
    }

    [Fact]
    public async Task RunOnce_BatchSize_BoundsWorkPerStatement()
    {
        await EnsureMigrated();
        await ClearMessagesAndDirectConversations();

        var globalId = Conversation.GlobalConversationId;
        var oldTime = DateTimeOffset.UtcNow.AddHours(-48);

        await using (var ctx = CreateContext())
        {
            var repo = new MessageRepository(ctx);
            for (int i = 0; i < 50; i++)
            {
                await repo.SaveAsync(
                    Message.Create(globalId, Guid.NewGuid(), "P", $"old-{i}", oldTime),
                    CancellationToken.None);
            }
        }

        // Batch size 10, max batches 2 → at most 20 rows per pass.
        var (worker, sp) = BuildWorker(new MessageRetentionOptions
        {
            MessageTtlHours = 24,
            BatchSize = 10,
            MaxBatchesPerPass = 2,
        });

        await worker.RunOnceAsync(CancellationToken.None);

        await using (var ctx = CreateContext())
        {
            int remaining = await ctx.Messages.CountAsync(m => m.ConversationId == globalId);
            remaining.Should().Be(30);
        }

        // Next pass continues to drain (with the default safety cap).
        var (worker2, sp2) = BuildWorker(new MessageRetentionOptions
        {
            MessageTtlHours = 24,
            BatchSize = 1000,
            MaxBatchesPerPass = 10,
        });
        await worker2.RunOnceAsync(CancellationToken.None);

        await using (var ctx = CreateContext())
        {
            int remaining = await ctx.Messages.CountAsync(m => m.ConversationId == globalId);
            remaining.Should().Be(0);
        }

        await sp.DisposeAsync();
        await sp2.DisposeAsync();
    }
}
