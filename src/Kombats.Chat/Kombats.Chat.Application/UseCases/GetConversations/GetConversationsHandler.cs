using Kombats.Abstractions;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Application.Repositories;
using Kombats.Chat.Domain.Conversations;

namespace Kombats.Chat.Application.UseCases.GetConversations;

internal sealed class GetConversationsHandler(
    IConversationRepository conversations,
    IDisplayNameResolver displayNameResolver)
    : IQueryHandler<GetConversationsQuery, GetConversationsResponse>
{
    public async Task<Result<GetConversationsResponse>> HandleAsync(
        GetConversationsQuery query,
        CancellationToken cancellationToken)
    {
        List<Conversation> list = await conversations.ListByParticipantAsync(
            query.CallerIdentityId, cancellationToken);

        var ordered = list.OrderByDescending(c => c.LastMessageAt).ToList();

        var dtos = new List<ConversationDto>(ordered.Count);
        foreach (var c in ordered)
        {
            dtos.Add(await MapConversationAsync(c, query.CallerIdentityId, cancellationToken));
        }

        return Result.Success(new GetConversationsResponse(dtos));
    }

    private async Task<ConversationDto> MapConversationAsync(
        Conversation c, Guid callerIdentityId, CancellationToken ct)
    {
        OtherPlayerDto? otherPlayer = null;

        if (c.Type == ConversationType.Direct)
        {
            Guid otherPlayerId = c.ParticipantAIdentityId == callerIdentityId
                ? c.ParticipantBIdentityId!.Value
                : c.ParticipantAIdentityId!.Value;

            string displayName = await displayNameResolver.ResolveAsync(otherPlayerId, ct);
            otherPlayer = new OtherPlayerDto(otherPlayerId, displayName);
        }

        return new ConversationDto(
            c.Id,
            c.Type.ToString(),
            otherPlayer,
            c.LastMessageAt);
    }
}
