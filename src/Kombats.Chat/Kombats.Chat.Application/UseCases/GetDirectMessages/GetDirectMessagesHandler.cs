using Kombats.Abstractions;
using Kombats.Chat.Application.Repositories;
using Kombats.Chat.Application.UseCases.GetConversationMessages;
using Kombats.Chat.Domain.Conversations;

namespace Kombats.Chat.Application.UseCases.GetDirectMessages;

internal sealed class GetDirectMessagesHandler(
    IConversationRepository conversations,
    IMessageRepository messages)
    : IQueryHandler<GetDirectMessagesQuery, GetConversationMessagesResponse>
{
    public async Task<Result<GetConversationMessagesResponse>> HandleAsync(
        GetDirectMessagesQuery query,
        CancellationToken cancellationToken)
    {
        if (query.CallerIdentityId == query.OtherIdentityId)
        {
            return Result.Failure<GetConversationMessagesResponse>(
                Error.Validation("GetDirectMessages.SameUser", "Cannot query direct messages with yourself."));
        }

        // Deterministic participant-pair resolution (read-only — no conversation creation)
        var (a, b) = Conversation.SortParticipants(query.CallerIdentityId, query.OtherIdentityId);

        Conversation? conversation = await conversations.GetDirectByParticipantsAsync(a, b, cancellationToken);

        // No conversation exists yet — return empty result without creating one
        if (conversation is null)
            return Result.Success(new GetConversationMessagesResponse([], false));

        int fetchLimit = Math.Clamp(query.Limit, 1, 50);

        var messageList = await messages.GetByConversationAsync(
            conversation.Id,
            query.Before,
            fetchLimit + 1,
            cancellationToken);

        bool hasMore = messageList.Count > fetchLimit;
        if (hasMore)
            messageList.RemoveAt(messageList.Count - 1);

        var dtos = messageList.Select(m => new MessageDto(
            m.Id,
            m.ConversationId,
            new SenderDto(m.SenderIdentityId, m.SenderDisplayName),
            m.Content,
            m.SentAt)).ToList();

        return Result.Success(new GetConversationMessagesResponse(dtos, hasMore));
    }
}
