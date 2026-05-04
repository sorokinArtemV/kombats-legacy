using System.Text.Json;
using Kombats.Chat.Application.Ports;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Kombats.Chat.Infrastructure.Redis;

internal sealed class RedisPlayerInfoCache(IConnectionMultiplexer redis, ILogger<RedisPlayerInfoCache> logger)
    : IPlayerInfoCache
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(7);

    public async Task<CachedPlayerInfo?> GetAsync(Guid identityId, CancellationToken ct)
    {
        var db = redis.GetDatabase(2);
        string key = $"chat:playerinfo:{identityId}";

        var value = await db.StringGetAsync(key);
        if (!value.HasValue)
            return null;

        try
        {
            var info = JsonSerializer.Deserialize<CachedPlayerInfoDto>(value.ToString());
            if (info is null)
                return null;

            // Renew TTL on hit
            await db.KeyExpireAsync(key, DefaultTtl);

            return new CachedPlayerInfo(info.Name, info.OnboardingState);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize cached player info for {IdentityId}", identityId);
            return null;
        }
    }

    public async Task SetAsync(Guid identityId, CachedPlayerInfo info, CancellationToken ct)
    {
        var db = redis.GetDatabase(2);
        string key = $"chat:playerinfo:{identityId}";

        string json = JsonSerializer.Serialize(new CachedPlayerInfoDto { Name = info.Name, OnboardingState = info.OnboardingState });
        await db.StringSetAsync(key, json, DefaultTtl);
    }

    public async Task RemoveAsync(Guid identityId, CancellationToken ct)
    {
        var db = redis.GetDatabase(2);
        await db.KeyDeleteAsync($"chat:playerinfo:{identityId}");
    }

    private sealed class CachedPlayerInfoDto
    {
        public string Name { get; set; } = string.Empty;
        public string OnboardingState { get; set; } = string.Empty;
    }
}
