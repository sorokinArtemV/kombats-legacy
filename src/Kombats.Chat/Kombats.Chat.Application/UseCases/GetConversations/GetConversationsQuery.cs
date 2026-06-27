using Kombats.Abstractions;

namespace Kombats.Chat.Application.UseCases.GetConversations;

internal sealed record GetConversationsQuery(
    Guid CallerIdentityId) : IQuery<GetConversationsResponse>;
