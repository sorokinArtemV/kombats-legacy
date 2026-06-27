using Kombats.Bff.Application.Clients;
using Kombats.Bff.Application.Errors;
using Kombats.Bff.Application.Models.Internal;
using Microsoft.Extensions.Logging;

namespace Kombats.Bff.Application.Composition;

public sealed class GameStateComposer(
    IPlayersClient playersClient,
    IMatchmakingClient matchmakingClient,
    ILogger<GameStateComposer> logger)
{
    public async Task<GameStateResult> ComposeAsync(CancellationToken cancellationToken)
    {
        InternalCharacterResponse? character = null;
        InternalQueueStatusResponse? queueStatus = null;
        bool playersAvailable = true;
        bool matchmakingAvailable = true;
        bool characterFound = false;

        var playersTask = GetCharacterSafeAsync(cancellationToken);
        var matchmakingTask = GetQueueStatusSafeAsync(cancellationToken);

        await Task.WhenAll(playersTask, matchmakingTask);

        (character, playersAvailable, characterFound) = playersTask.Result;
        (queueStatus, matchmakingAvailable) = matchmakingTask.Result;

        if (!playersAvailable && !matchmakingAvailable)
        {
            return GameStateResult.BothUnavailable();
        }

        var degradedServices = new List<string>();
        if (!playersAvailable)
        {
            degradedServices.Add("Players");
        }
        if (!matchmakingAvailable)
        {
            degradedServices.Add("Matchmaking");
        }

        return GameStateResult.Success(
            character,
            queueStatus,
            characterFound,
            degradedServices.Count > 0 ? degradedServices : null);
    }

    private async Task<(InternalCharacterResponse? Character, bool Available, bool Found)> GetCharacterSafeAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            InternalCharacterResponse? character = await playersClient.GetCharacterAsync(cancellationToken);
            return (character, Available: true, Found: character is not null);
        }
        catch (ServiceUnavailableException ex)
        {
            logger.LogWarning(ex, "Players service unavailable during game state composition");
            return (null, Available: false, Found: false);
        }
    }

    private async Task<(InternalQueueStatusResponse? QueueStatus, bool Available)> GetQueueStatusSafeAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            InternalQueueStatusResponse? queueStatus = await matchmakingClient.GetQueueStatusAsync(cancellationToken);
            return (queueStatus, Available: true);
        }
        catch (ServiceUnavailableException ex)
        {
            logger.LogWarning(ex, "Matchmaking service unavailable during game state composition");
            return (null, Available: false);
        }
    }
}

public sealed record GameStateResult(
    InternalCharacterResponse? Character,
    InternalQueueStatusResponse? QueueStatus,
    bool IsCharacterCreated,
    IReadOnlyList<string>? DegradedServices,
    bool IsBothUnavailable)
{
    public static GameStateResult Success(
        InternalCharacterResponse? character,
        InternalQueueStatusResponse? queueStatus,
        bool isCharacterCreated,
        IReadOnlyList<string>? degradedServices) =>
        new(character, queueStatus, isCharacterCreated, degradedServices, IsBothUnavailable: false);

    public static GameStateResult BothUnavailable() =>
        new(null, null, false, null, IsBothUnavailable: true);
}
