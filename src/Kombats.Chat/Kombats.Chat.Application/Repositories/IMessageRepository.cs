using Kombats.Chat.Domain.Messages;

namespace Kombats.Chat.Application.Repositories;

internal interface IMessageRepository
{
    Task SaveAsync(Message message, CancellationToken ct);
    Task<List<Message>> GetByConversationAsync(
        Guid conversationId,
        DateTimeOffset? before,
        int limit,
        CancellationToken ct);
    Task<int> DeleteExpiredAsync(DateTimeOffset cutoff, int batchSize, CancellationToken ct);
}
