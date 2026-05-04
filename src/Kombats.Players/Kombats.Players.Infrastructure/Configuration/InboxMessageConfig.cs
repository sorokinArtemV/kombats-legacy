using Kombats.Players.Infrastructure.Messaging.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kombats.Players.Infrastructure.Configuration;

internal sealed class InboxMessageConfig : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> b)
    {
        b.ToTable("inbox_messages");
        b.HasKey(x => x.MessageId);
        b.Property(x => x.ProcessedAt).IsRequired();
    }
}