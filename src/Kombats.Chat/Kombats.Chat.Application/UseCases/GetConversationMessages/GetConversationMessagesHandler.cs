using Kombats.Abstractions;
using Kombats.Chat.Application.Repositories;
using Kombats.Chat.Domain.Conversations;

namespace Kombats.Chat.Application.UseCases.GetConversationMessages;

internal sealed class GetConversationMessagesHandler(
    IConversationRepository conversations,
    IMessageRepository messages)
    : IQueryHandler<GetConversationMessagesQuery, GetConversationMessagesResponse>
{
    public async Task<Result<GetConversationMessagesResponse>> HandleAsync(
        GetConversationMessagesQuery query,
        CancellationToken cancellationToken)
    {
        Conversation? conversation = await conversations.GetByIdAsync(query.ConversationId, cancellationToken);

        if (conversation is null)
        {
            return Result.Failure<GetConversationMessagesResponse>(
                Error.NotFound("GetConversationMessages.NotFound", "Conversation not found."));
        }

        if (!conversation.IsParticipant(query.CallerIdentityId))
        {
            return Result.Failure<GetConversationMessagesResponse>(
                Error.NotFound("GetConversationMessages.NotFound", "Conversation not found."));
        }

        int fetchLimit = Math.Clamp(query.Limit, 1, 50);

        var messageList = await messages.GetByConversationAsync(
            query.ConversationId,
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
