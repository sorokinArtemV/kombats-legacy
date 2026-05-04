using Kombats.Abstractions;
using Kombats.Chat.Application.UseCases.GetConversationMessages;

namespace Kombats.Chat.Application.UseCases.GetDirectMessages;

internal sealed record GetDirectMessagesQuery(
    Guid CallerIdentityId,
    Guid OtherIdentityId,
    DateTimeOffset? Before,
    int Limit) : IQuery<GetConversationMessagesResponse>;
