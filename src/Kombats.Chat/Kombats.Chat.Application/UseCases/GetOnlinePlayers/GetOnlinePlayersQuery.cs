using Kombats.Abstractions;

namespace Kombats.Chat.Application.UseCases.GetOnlinePlayers;

internal sealed record GetOnlinePlayersQuery(
    int Limit,
    int Offset) : IQuery<GetOnlinePlayersResponse>;
