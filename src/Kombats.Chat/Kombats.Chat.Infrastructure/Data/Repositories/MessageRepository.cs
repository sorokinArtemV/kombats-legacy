using Kombats.Chat.Application.Repositories;
using Kombats.Chat.Domain.Messages;
using Microsoft.EntityFrameworkCore;

namespace Kombats.Chat.Infrastructure.Data.Repositories;

internal sealed class MessageRepository(ChatDbContext db) : IMessageRepository
{
    public async Task SaveAsync(Message message, CancellationToken ct)
    {
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<Message>> GetByConversationAsync(
        Guid conversationId,
        DateTimeOffset? before,
        int limit,
        CancellationToken ct)
    {
        IQueryable<Message> query = db.Messages
            .Where(m => m.ConversationId == conversationId);

        if (before.HasValue)
            query = query.Where(m => m.SentAt < before.Value);

        return await query
            .OrderByDescending(m => m.SentAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset cutoff, int batchSize, CancellationToken ct)
    {
        return await db.Messages
            .Where(m => m.SentAt < cutoff)
            .OrderBy(m => m.SentAt)
            .Take(batchSize)
            .ExecuteDeleteAsync(ct);
    }
}
