using FluentAssertions;
using Kombats.Chat.Domain.Conversations;
using Kombats.Chat.Domain.Messages;
using Kombats.Chat.Infrastructure.Data;
using Kombats.Chat.Infrastructure.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Kombats.Chat.Infrastructure.Tests.Data;

[Collection(PostgresCollection.Name)]
public sealed class ChatDbContextTests(PostgresFixture postgres)
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
    public async Task Migration_AppliesCleanly()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        bool canConnect = await context.Database.CanConnectAsync();
        canConnect.Should().BeTrue();
    }

    [Fact]
    public async Task GlobalConversation_IsSeeded()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var global = await context.Conversations
            .FirstOrDefaultAsync(c => c.Id == Conversation.GlobalConversationId);

        global.Should().NotBeNull();
        global!.Type.Should().Be(ConversationType.Global);
        global.ParticipantAIdentityId.Should().BeNull();
        global.ParticipantBIdentityId.Should().BeNull();
    }

    [Fact]
    public async Task SchemaIsolation_AllTablesInChatSchema()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var tables = await context.Database.SqlQueryRaw<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'chat'")
            .ToListAsync();

        tables.Should().Contain("conversations");
        tables.Should().Contain("messages");
        tables.Should().Contain("inbox_state");
        tables.Should().Contain("outbox_message");
        tables.Should().Contain("outbox_state");
    }

    [Fact]
    public async Task SnakeCaseNaming_ColumnsAreSnakeCase()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var columns = await context.Database.SqlQueryRaw<string>(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'chat' AND table_name = 'conversations'")
            .ToListAsync();

        columns.Should().Contain("participant_a_identity_id");
        columns.Should().Contain("participant_b_identity_id");
        columns.Should().Contain("last_message_at");
        columns.Should().Contain("created_at");
    }

    [Fact]
    public async Task Conversation_RoundTrip()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var dm = Conversation.CreateDirect(a, b);

        context.Conversations.Add(dm);
        await context.SaveChangesAsync();

        await using var readContext = CreateContext();
        var loaded = await readContext.Conversations.FirstOrDefaultAsync(c => c.Id == dm.Id);

        loaded.Should().NotBeNull();
        loaded!.Type.Should().Be(ConversationType.Direct);
        loaded.ParticipantAIdentityId.Should().NotBeNull();
        loaded.ParticipantBIdentityId.Should().NotBeNull();
    }

    [Fact]
    public async Task Message_RoundTrip()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var global = await context.Conversations
            .FirstAsync(c => c.Id == Conversation.GlobalConversationId);

        var message = Message.Create(
            global.Id,
            Guid.NewGuid(),
            "TestPlayer",
            "Hello world",
            DateTimeOffset.UtcNow);

        context.Messages.Add(message);
        await context.SaveChangesAsync();

        await using var readContext = CreateContext();
        var loaded = await readContext.Messages.FirstOrDefaultAsync(m => m.Id == message.Id);

        loaded.Should().NotBeNull();
        loaded!.Content.Should().Be("Hello world");
        loaded.SenderDisplayName.Should().Be("TestPlayer");
        loaded.ConversationId.Should().Be(global.Id);
    }
}
