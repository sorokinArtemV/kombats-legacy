using Kombats.Abstractions;
using Kombats.Chat.Application.Ports;

namespace Kombats.Chat.Application.UseCases.GetOnlinePlayers;

internal sealed class GetOnlinePlayersHandler(IPresenceStore presenceStore)
    : IQueryHandler<GetOnlinePlayersQuery, GetOnlinePlayersResponse>
{
    public async Task<Result<GetOnlinePlayersResponse>> HandleAsync(
        GetOnlinePlayersQuery query,
        CancellationToken cancellationToken)
    {
        int limit = Math.Clamp(query.Limit, 1, 100);
        int offset = Math.Max(0, query.Offset);

        var players = await presenceStore.GetOnlinePlayersAsync(limit, offset, cancellationToken);
        long totalOnline = await presenceStore.GetOnlineCountAsync(cancellationToken);

        var dtos = players.Select(p => new OnlinePlayerDto(p.PlayerId, p.DisplayName)).ToList();

        return Result.Success(new GetOnlinePlayersResponse(dtos, totalOnline));
    }
}
