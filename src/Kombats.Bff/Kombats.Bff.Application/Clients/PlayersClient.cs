using Kombats.Bff.Application.Models.Internal;
using Microsoft.Extensions.Logging;

namespace Kombats.Bff.Application.Clients;

public sealed class PlayersClient(HttpClient httpClient, ILogger<PlayersClient> logger) : IPlayersClient
{
    private const string ServiceName = "Players";

    public async Task<InternalCharacterResponse?> GetCharacterAsync(CancellationToken cancellationToken = default)
    {
        return await HttpClientHelper.SendAsync<InternalCharacterResponse>(
            httpClient, HttpMethod.Get, "/api/v1/me", null, ServiceName, logger, cancellationToken);
    }

    public async Task<InternalCharacterResponse?> EnsureCharacterAsync(CancellationToken cancellationToken = default)
    {
        return await HttpClientHelper.SendAsync<InternalCharacterResponse>(
            httpClient, HttpMethod.Post, "/api/v1/me/ensure", null, ServiceName, logger, cancellationToken);
    }

    public async Task<InternalCharacterResponse?> SetCharacterNameAsync(
        string name, CancellationToken cancellationToken = default)
    {
        return await HttpClientHelper.SendAsync<InternalCharacterResponse>(
            httpClient, HttpMethod.Post, "/api/v1/character/name", new { Name = name }, ServiceName, logger, cancellationToken);
    }

    public async Task<InternalPlayerProfileResponse?> GetProfileAsync(
        Guid playerId, CancellationToken cancellationToken = default)
    {
        return await HttpClientHelper.SendAsync<InternalPlayerProfileResponse>(
            httpClient, HttpMethod.Get, $"/api/v1/players/{playerId}/profile",
            null, ServiceName, logger, cancellationToken);
    }

    public async Task<InternalCharacterResponse?> AllocateStatsAsync(
        int expectedRevision, int strength, int agility, int intuition, int vitality,
        CancellationToken cancellationToken = default)
    {
        return await HttpClientHelper.SendAsync<InternalCharacterResponse>(
            httpClient, HttpMethod.Post, "/api/v1/players/me/stats/allocate",
            new { Str = strength, Agi = agility, Intuition = intuition, Vit = vitality, ExpectedRevision = expectedRevision },
            ServiceName, logger, cancellationToken);
    }

    public async Task<InternalChangeAvatarResponse?> ChangeAvatarAsync(
        int expectedRevision, string avatarId, CancellationToken cancellationToken = default)
    {
        return await HttpClientHelper.SendAsync<InternalChangeAvatarResponse>(
            httpClient, HttpMethod.Post, "/api/v1/character/avatar",
            new { ExpectedRevision = expectedRevision, AvatarId = avatarId },
            ServiceName, logger, cancellationToken);
    }
}
