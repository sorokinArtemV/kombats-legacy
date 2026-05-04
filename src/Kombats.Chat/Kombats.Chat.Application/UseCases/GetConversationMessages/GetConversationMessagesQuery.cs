using Kombats.Abstractions;

namespace Kombats.Chat.Application.UseCases.GetConversationMessages;

internal sealed record GetConversationMessagesQuery(
    Guid ConversationId,
    Guid CallerIdentityId,
    DateTimeOffset? Before,
    int Limit) : IQuery<GetConversationMessagesResponse>;
