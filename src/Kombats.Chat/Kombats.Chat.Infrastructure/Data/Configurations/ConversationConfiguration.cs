using Kombats.Chat.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kombats.Chat.Infrastructure.Data.Configurations;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Type)
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.LastMessageAt);

        builder.Property(c => c.ParticipantAIdentityId);

        builder.Property(c => c.ParticipantBIdentityId);

        // Unique index on sorted participant pair for direct conversations
        builder.HasIndex(c => new { c.ParticipantAIdentityId, c.ParticipantBIdentityId })
            .IsUnique()
            .HasFilter("type = 1"); // Direct = 1

        builder.HasIndex(c => c.Type);

        // Seed global conversation
        builder.HasData(new
        {
            Id = Conversation.GlobalConversationId,
            Type = ConversationType.Global,
            CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            LastMessageAt = (DateTimeOffset?)null,
            ParticipantAIdentityId = (Guid?)null,
            ParticipantBIdentityId = (Guid?)null,
        });
    }
}
