namespace Kombats.Chat.Application.UseCases.GetOnlinePlayers;

public sealed record GetOnlinePlayersResponse(
    List<OnlinePlayerDto> Players,
    long TotalOnline);

public sealed record OnlinePlayerDto(
    Guid PlayerId,
    string DisplayName);
