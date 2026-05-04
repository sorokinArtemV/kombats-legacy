using Microsoft.Extensions.Logging;

namespace Kombats.Bff.Application.Clients;

public sealed class BattleClient(HttpClient httpClient, ILogger<BattleClient> logger) : IBattleClient
{
    private const string ServiceName = "Battle";

    public async Task<BattleHistoryResponse?> GetHistoryAsync(Guid battleId, CancellationToken cancellationToken = default)
    {
        return await HttpClientHelper.SendAsync<BattleHistoryResponse>(
            httpClient, HttpMethod.Get, $"/api/internal/battles/{battleId}/history",
            null, ServiceName, logger, cancellationToken);
    }
}
