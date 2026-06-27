using System.Text.Json;
using Kombats.Chat.Application.Ports;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Kombats.Chat.Infrastructure.Redis;

internal sealed class RedisPresenceStore(IConnectionMultiplexer redis, ILogger<RedisPresenceStore> logger) : IPresenceStore
{
    private const int PresenceTtlSeconds = 90;
    private const string OnlineSetKey = "chat:presence:online";

    private static readonly LuaScript ConnectScript = LuaScript.Prepare(
        """
        local refs = redis.call('INCR', @refsKey)
        redis.call('EXPIRE', @refsKey, @ttl)
        if refs == 1 then
          redis.call('ZADD', @onlineKey, @nowMs, @memberId)
          redis.call('SET', @presenceKey, @presenceJson, 'EX', @ttl)
          return 1
        end
        return 0
        """);

    private static readonly LuaScript HeartbeatScript = LuaScript.Prepare(
        """
        local refs = redis.call('EXISTS', @refsKey)
        if refs == 0 then
          return 0
        end
        redis.call('EXPIRE', @refsKey, @ttl)
        redis.call('EXPIRE', @presenceKey, @ttl)
        redis.call('ZADD', @onlineKey, @nowMs, @memberId)
        return 1
        """);

    private static readonly LuaScript DisconnectScript = LuaScript.Prepare(
        """
        local refs = tonumber(redis.call('GET', @refsKey))
        if refs == nil then
          -- Refs already gone (TTL expired or never existed). Clean up any
          -- dangling online/presence entries defensively, but do not signal
          -- "last connection" — the player is already offline from Redis'
          -- perspective, so callers must not emit a fresh offline broadcast.
          redis.call('ZREM', @onlineKey, @memberId)
          redis.call('DEL', @presenceKey)
          return 0
        end
        if refs <= 1 then
          redis.call('DEL', @refsKey)
          redis.call('ZREM', @onlineKey, @memberId)
          redis.call('DEL', @presenceKey)
          return 1
        end
        redis.call('DECR', @refsKey)
        return 0
        """);

    public async Task<bool> ConnectAsync(Guid identityId, string displayName, CancellationToken ct)
    {
        var db = redis.GetDatabase(2);
        string id = identityId.ToString();
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string presenceJson = JsonSerializer.Serialize(new { name = displayName, connectedAtUnixMs = nowMs });

        var result = (int)await db.ScriptEvaluateAsync(ConnectScript, new
        {
            refsKey = (RedisKey)$"chat:presence:refs:{id}",
            onlineKey = (RedisKey)OnlineSetKey,
            presenceKey = (RedisKey)$"chat:presence:{id}",
            ttl = PresenceTtlSeconds,
            nowMs = nowMs,
            memberId = id,
            presenceJson = presenceJson,
        });

        if (result == 1)
            logger.LogInformation("Player {IdentityId} came online", identityId);

        return result == 1;
    }

    public async Task<bool> DisconnectAsync(Guid identityId, CancellationToken ct)
    {
        var db = redis.GetDatabase(2);
        string id = identityId.ToString();

        var result = (int)await db.ScriptEvaluateAsync(DisconnectScript, new
        {
            refsKey = (RedisKey)$"chat:presence:refs:{id}",
            onlineKey = (RedisKey)OnlineSetKey,
            presenceKey = (RedisKey)$"chat:presence:{id}",
            memberId = id,
        });

        if (result == 1)
            logger.LogInformation("Player {IdentityId} went offline", identityId);

        return result == 1;
    }

    public async Task HeartbeatAsync(Guid identityId, CancellationToken ct)
    {
        var db = redis.GetDatabase(2);
        string id = identityId.ToString();
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await db.ScriptEvaluateAsync(HeartbeatScript, new
        {
            refsKey = (RedisKey)$"chat:presence:refs:{id}",
            onlineKey = (RedisKey)OnlineSetKey,
            presenceKey = (RedisKey)$"chat:presence:{id}",
            ttl = PresenceTtlSeconds,
            nowMs = nowMs,
            memberId = id,
        });
    }

    public async Task<List<OnlinePlayer>> GetOnlinePlayersAsync(int limit, int offset, CancellationToken ct)
    {
        var db = redis.GetDatabase(2);

        // Get members from ZSET ordered by score descending (most recent heartbeat first)
        var entries = await db.SortedSetRangeByRankWithScoresAsync(
            OnlineSetKey,
            start: offset,
            stop: offset + limit - 1,
            order: Order.Descending);

        var players = new List<OnlinePlayer>(entries.Length);
        foreach (var entry in entries)
        {
            string memberId = entry.Element.ToString();
            if (!Guid.TryParse(memberId, out Guid playerId))
                continue;

            // Read display name from presence key
            var presenceJson = await db.StringGetAsync($"chat:presence:{memberId}");
            string displayName = "Unknown";
            if (presenceJson.HasValue)
            {
                try
                {
                    using var doc = JsonDocument.Parse(presenceJson.ToString());
                    displayName = doc.RootElement.GetProperty("name").GetString() ?? "Unknown";
                }
                catch (JsonException)
                {
                    // Ignore malformed JSON
                }
            }

            players.Add(new OnlinePlayer(playerId, displayName));
        }

        return players;
    }

    public async Task<long> GetOnlineCountAsync(CancellationToken ct)
    {
        var db = redis.GetDatabase(2);
        return await db.SortedSetLengthAsync(OnlineSetKey);
    }

    public async Task<bool> IsOnlineAsync(Guid identityId, CancellationToken ct)
    {
        var db = redis.GetDatabase(2);
        var score = await db.SortedSetScoreAsync(OnlineSetKey, identityId.ToString());
        return score.HasValue;
    }

    public async Task<IReadOnlyList<Guid>> SweepStaleAsync(TimeSpan staleAfter, CancellationToken ct)
    {
        var db = redis.GetDatabase(2);
        long cutoffMs = DateTimeOffset.UtcNow.Subtract(staleAfter).ToUnixTimeMilliseconds();

        // Snapshot candidate members (by rank/score) whose last heartbeat is older than cutoff.
        // ZRANGEBYSCORE -inf cutoff. ZRemoveRangeByScore cannot return the removed members,
        // so we must iterate ZRem-per-member to get the atomic "did I remove it?" signal.
        var staleMembers = await db.SortedSetRangeByScoreAsync(
            OnlineSetKey,
            start: double.NegativeInfinity,
            stop: cutoffMs,
            exclude: Exclude.None,
            order: Order.Ascending);

        if (staleMembers.Length == 0)
            return Array.Empty<Guid>();

        var removed = new List<Guid>(staleMembers.Length);
        foreach (var member in staleMembers)
        {
            string memberStr = member.ToString();
            if (!Guid.TryParse(memberStr, out Guid playerId))
                continue;

            // Atomic ZREM — true only if THIS call removed the entry.
            bool didRemove = await db.SortedSetRemoveAsync(OnlineSetKey, memberStr);
            if (!didRemove)
                continue;

            // Intentionally do NOT delete chat:presence:refs:{id} here: a concurrent
            // ConnectAsync may have just INCR'd it back from 0→1 and re-added the user to
            // the online ZSET. Clobbering refs in that window would desync refcount from
            // ZSET membership and cause the next DisconnectAsync to silently skip the
            // PlayerOffline broadcast. The 90s TTL on the refs key reaps it naturally.
            //
            // The display-name presence key is safe to delete: ConnectScript rewrites it
            // (SET EX) on reconnect, and readers treat a missing value as "Unknown".
            await db.KeyDeleteAsync($"chat:presence:{memberStr}");

            removed.Add(playerId);
            logger.LogInformation(
                "Presence sweep removed stale entry for Player {IdentityId}", playerId);
        }

        return removed;
    }
}
