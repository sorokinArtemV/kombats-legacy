using Kombats.Players.Domain.Entities;
using Kombats.Players.Infrastructure.Messaging.Inbox;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Kombats.Players.Infrastructure.Data;

public sealed class PlayersDbContext : DbContext
{
    public const string Schema = "players";

    public DbSet<Character> Characters => Set<Character>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public PlayersDbContext(DbContextOptions<PlayersDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlayersDbContext).Assembly);

        // MassTransit EF Core transactional outbox/inbox entities (AD-01)
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
