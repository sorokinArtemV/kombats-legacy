using Kombats.Chat.Domain.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kombats.Chat.Infrastructure.Data.Configurations;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.ConversationId)
            .IsRequired();

        builder.Property(m => m.SenderIdentityId)
            .IsRequired();

        builder.Property(m => m.SenderDisplayName)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(m => m.Content)
            .IsRequired()
            .HasMaxLength(Message.MaxContentLength);

        builder.Property(m => m.SentAt)
            .IsRequired();

        // Index for cursor pagination: (conversation_id, sent_at DESC)
        builder.HasIndex(m => new { m.ConversationId, m.SentAt })
            .IsDescending(false, true);

        // Foreign key to conversations
        builder.HasOne<Domain.Conversations.Conversation>()
            .WithMany()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
