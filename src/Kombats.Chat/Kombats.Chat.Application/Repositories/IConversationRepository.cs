using Kombats.Chat.Domain.Conversations;

namespace Kombats.Chat.Application.Repositories;

internal interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Conversation?> GetGlobalAsync(CancellationToken ct);
    Task<Conversation?> GetDirectByParticipantsAsync(Guid participantA, Guid participantB, CancellationToken ct);
    Task<Conversation?> GetOrCreateDirectAsync(Guid participantA, Guid participantB, CancellationToken ct);
    Task<List<Conversation>> ListByParticipantAsync(Guid identityId, CancellationToken ct);
    Task UpdateLastMessageAtAsync(Guid conversationId, DateTimeOffset sentAt, CancellationToken ct);
    Task DeleteEmptyDirectConversationsAsync(CancellationToken ct);
}
