using Kombats.Chat.Domain.Conversations;
using Kombats.Chat.Domain.Messages;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Kombats.Chat.Infrastructure.Data;

internal sealed class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
    public const string Schema = "chat";

    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChatDbContext).Assembly);

        // MassTransit EF Core transactional outbox/inbox entities (AD-01)
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
