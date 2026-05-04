using Kombats.Chat.Application.Repositories;
using Kombats.Chat.Domain.Conversations;
using Microsoft.EntityFrameworkCore;

namespace Kombats.Chat.Infrastructure.Data.Repositories;

internal sealed class ConversationRepository(ChatDbContext db) : IConversationRepository
{
    public Task<Conversation?> GetByIdAsync(Guid id, CancellationToken ct)
        => db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Conversation?> GetGlobalAsync(CancellationToken ct)
        => db.Conversations.FirstOrDefaultAsync(
            c => c.Id == Conversation.GlobalConversationId, ct);

    public async Task<Conversation?> GetDirectByParticipantsAsync(
        Guid participantA,
        Guid participantB,
        CancellationToken ct)
    {
        var (a, b) = Conversation.SortParticipants(participantA, participantB);

        return await db.Conversations.FirstOrDefaultAsync(
            c => c.Type == ConversationType.Direct
                 && c.ParticipantAIdentityId == a
                 && c.ParticipantBIdentityId == b,
            ct);
    }

    public async Task<Conversation?> GetOrCreateDirectAsync(
        Guid participantA,
        Guid participantB,
        CancellationToken ct)
    {
        // Ensure sorted order
        var (a, b) = Conversation.SortParticipants(participantA, participantB);

        // INSERT ... ON CONFLICT DO NOTHING pattern via raw SQL for atomicity
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO chat.conversations (id, type, created_at, last_message_at, participant_a_identity_id, participant_b_identity_id)
               VALUES ({Guid.CreateVersion7()}, {(int)ConversationType.Direct}, {DateTimeOffset.UtcNow}, {(DateTimeOffset?)null}, {a}, {b})
               ON CONFLICT (participant_a_identity_id, participant_b_identity_id) WHERE type = 1 DO NOTHING",
            ct);

        // Then SELECT to get the existing (or just-inserted) conversation
        return await db.Conversations.FirstOrDefaultAsync(
            c => c.Type == ConversationType.Direct
                 && c.ParticipantAIdentityId == a
                 && c.ParticipantBIdentityId == b,
            ct);
    }

    public async Task<List<Conversation>> ListByParticipantAsync(Guid identityId, CancellationToken ct)
    {
        // Return global conversation + direct conversations where the user is a participant
        return await db.Conversations
            .Where(c => c.Type == ConversationType.Global
                        || c.ParticipantAIdentityId == identityId
                        || c.ParticipantBIdentityId == identityId)
            .OrderByDescending(c => c.LastMessageAt)
            .ToListAsync(ct);
    }

    public async Task UpdateLastMessageAtAsync(Guid conversationId, DateTimeOffset sentAt, CancellationToken ct)
    {
        await db.Conversations
            .Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.LastMessageAt, sentAt), ct);
    }

    public async Task DeleteEmptyDirectConversationsAsync(CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM chat.conversations c
            WHERE c.type = 1
              AND NOT EXISTS (
                  SELECT 1 FROM chat.messages m WHERE m.conversation_id = c.id
              )
            """,
            ct);
    }
}
