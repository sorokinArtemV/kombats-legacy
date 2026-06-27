using System.Net;
using System.Net.Http.Json;
using Kombats.Bff.Application.Errors;
using Kombats.Bff.Application.Models.Internal;
using Microsoft.Extensions.Logging;

namespace Kombats.Bff.Application.Clients;

public sealed class MatchmakingClient(HttpClient httpClient, ILogger<MatchmakingClient> logger) : IMatchmakingClient
{
    private const string ServiceName = "Matchmaking";

    public async Task<InternalQueueStatusResponse> JoinQueueAsync(string? connectionRef = null, CancellationToken cancellationToken = default)
    {
        const string path = "/api/v1/matchmaking/queue/join";
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = JsonContent.Create(new { Variant = (string?)null, ConnectionRef = connectionRef });

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to reach {Service} at {Path}", ServiceName, path);
            throw new ServiceUnavailableException(ServiceName);
        }

        // 200 = joined queue (Searching), 409 = already matched (returns QueueStatusDto with match info)
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict)
        {
            var result = await response.Content.ReadFromJsonAsync<InternalQueueStatusResponse>(cancellationToken);
            return result ?? new InternalQueueStatusResponse("Searching");
        }

        BffError error = await ErrorMapper.MapFromResponseAsync(response, ServiceName, logger, cancellationToken);
        throw new BffServiceException(response.StatusCode, error);
    }

    public async Task<InternalLeaveQueueResponse> LeaveQueueAsync(string? connectionRef = null, CancellationToken cancellationToken = default)
    {
        const string path = "/api/v1/matchmaking/queue/leave";
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = JsonContent.Create(new { Variant = (string?)null, ConnectionRef = connectionRef });

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to reach {Service} at {Path}", ServiceName, path);
            throw new ServiceUnavailableException(ServiceName);
        }

        // Both 200 (left queue) and 409 (already matched) are valid outcomes
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict)
        {
            var result = await response.Content.ReadFromJsonAsync<InternalLeaveQueueResponse>(cancellationToken);
            return result ?? new InternalLeaveQueueResponse(false);
        }

        BffError error = await ErrorMapper.MapFromResponseAsync(response, ServiceName, logger, cancellationToken);
        throw new BffServiceException(response.StatusCode, error);
    }

    public async Task<InternalQueueStatusResponse?> GetQueueStatusAsync(CancellationToken cancellationToken = default)
    {
        return await HttpClientHelper.SendAsync<InternalQueueStatusResponse>(
            httpClient, HttpMethod.Get, "/api/v1/matchmaking/queue/status", null, ServiceName, logger, cancellationToken);
    }

    public async Task HeartbeatAsync(string connectionRef, CancellationToken cancellationToken = default)
    {
        const string path = "/api/v1/matchmaking/queue/heartbeat";
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = JsonContent.Create(new { ConnectionRef = connectionRef });

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to reach {Service} at {Path}", ServiceName, path);
            throw new ServiceUnavailableException(ServiceName);
        }

        if (response.IsSuccessStatusCode) return;

        BffError error = await ErrorMapper.MapFromResponseAsync(response, ServiceName, logger, cancellationToken);
        throw new BffServiceException(response.StatusCode, error);
    }
}
