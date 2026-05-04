using System.Globalization;
using Kombats.Bff.Application.Models.Internal;
using Microsoft.Extensions.Logging;

namespace Kombats.Bff.Application.Clients;

public sealed class ChatClient(HttpClient httpClient, ILogger<ChatClient> logger) : IChatClient
{
    private const string ServiceName = "Chat";

    public Task<InternalConversationListResponse?> GetConversationsAsync(CancellationToken cancellationToken = default) =>
        HttpClientHelper.SendAsync<InternalConversationListResponse>(
            httpClient, HttpMethod.Get, "/api/internal/conversations",
            null, ServiceName, logger, cancellationToken);

    public Task<InternalMessageListResponse?> GetMessagesAsync(
        Guid conversationId,
        DateTimeOffset? before,
        int limit,
        CancellationToken cancellationToken = default)
    {
        string path = $"/api/internal/conversations/{conversationId}/messages?{BuildPagingQuery(before, limit)}";
        return HttpClientHelper.SendAsync<InternalMessageListResponse>(
            httpClient, HttpMethod.Get, path, null, ServiceName, logger, cancellationToken);
    }

    public Task<InternalMessageListResponse?> GetDirectMessagesAsync(
        Guid otherIdentityId,
        DateTimeOffset? before,
        int limit,
        CancellationToken cancellationToken = default)
    {
        string path = $"/api/internal/direct/{otherIdentityId}/messages?{BuildPagingQuery(before, limit)}";
        return HttpClientHelper.SendAsync<InternalMessageListResponse>(
            httpClient, HttpMethod.Get, path, null, ServiceName, logger, cancellationToken);
    }

    public Task<InternalOnlinePlayersResponse?> GetOnlinePlayersAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        string path = $"/api/internal/presence/online?limit={limit}&offset={offset}";
        return HttpClientHelper.SendAsync<InternalOnlinePlayersResponse>(
            httpClient, HttpMethod.Get, path, null, ServiceName, logger, cancellationToken);
    }

    private static string BuildPagingQuery(DateTimeOffset? before, int limit)
    {
        if (before is null)
        {
            return $"limit={limit}";
        }

        // Round-trippable ISO-8601 with offset; matches the [FromQuery] DateTimeOffset binder on the Chat side.
        string encoded = Uri.EscapeDataString(before.Value.ToString("O", CultureInfo.InvariantCulture));
        return $"before={encoded}&limit={limit}";
    }
}
