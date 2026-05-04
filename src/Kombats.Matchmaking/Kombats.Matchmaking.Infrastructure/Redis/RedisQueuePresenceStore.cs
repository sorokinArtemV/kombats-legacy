using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Kombats.Matchmaking.Infrastructure.Redis;

/// <summary>
/// Redis implementation of IQueuePresenceStore. Mirrors RedisPresenceStore from
/// the Chat service (db 2, chat:presence:*); this lives in the matchmaking DB
/// (configured index, default 1) under mm:queue:presence:*. Multi-tab safety:
/// the per-identity refs SET stores each tab's connectionRef as a member; the
/// SET TTL is refreshed on every heartbeat from any tab. The online ZSET is
/// keyed by identityId so sweep operates at the identity level.
/// </summary>
internal sealed class RedisQueuePresenceStore : IQueuePresenceStore
{
    private const string OnlineSetKey = "mm:queue:presence:online";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisQueuePresenceStore> _logger;
    private readonly MatchmakingRedisOptions _redisOptions;
    private readonly QueuePresenceOptions _presenceOptions;

    public RedisQueuePresenceStore(
        IConnectionMultiplexer redis,
        ILogger<RedisQueuePresenceStore> logger,
        IOptions<MatchmakingRedisOptions> redisOptions,
        IOptions<QueuePresenceOptions> presenceOptions)
    {
        _redis = redis;
        _logger = logger;
        _redisOptions = redisOptions.Value;
        _presenceOptions = presenceOptions.Value;
    }

    private static readonly LuaScript RegisterScript = LuaScript.Prepare(
        """
        local added = redis.call('SADD', @refsKey, @connectionRef)
        redis.call('EXPIRE', @refsKey, @ttl)
        redis.call('ZADD', @onlineKey, @nowMs, @memberId)
        return added
        """);

    private static readonly LuaScript RefreshScript = LuaScript.Prepare(
        """
        redis.call('SADD', @refsKey, @connectionRef)
        redis.call('EXPIRE', @refsKey, @ttl)
        redis.call('ZADD', @onlineKey, @nowMs, @memberId)
        return 1
        """);

    private static readonly LuaScript UnregisterScript = LuaScript.Prepare(
        """
        redis.call('SREM', @refsKey, @connectionRef)
        local remaining = redis.call('SCARD', @refsKey)
        if remaining == 0 then
          redis.call('DEL', @refsKey)
          redis.call('ZREM', @onlineKey, @memberId)
          return 1
        end
        return 0
        """);

    private IDatabase GetDatabase() => _redis.GetDatabase(_redisOptions.DatabaseIndex);

    private static string RefsKey(Guid identityId) => $"mm:queue:presence:refs:{identityId}";

    public async Task<bool> RegisterAsync(Guid identityId, string connectionRef, CancellationToken ct)
    {
        var db = GetDatabase();
        string id = identityId.ToString();
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var result = (int)await db.ScriptEvaluateAsync(RegisterScript, new
        {
            refsKey = (RedisKey)RefsKey(identityId),
            onlineKey = (RedisKey)OnlineSetKey,
            ttl = _presenceOptions.PresenceTtlSeconds,
            nowMs = nowMs,
            memberId = id,
            connectionRef = connectionRef,
        });

        bool added = result == 1;
        if (added)
        {
            _logger.LogInformation(
                "Queue presence registered: IdentityId={IdentityId}, ConnectionRef={ConnectionRef}",
                identityId, connectionRef);
        }

        return added;
    }

    public async Task RefreshAsync(Guid identityId, string connectionRef, CancellationToken ct)
    {
        var db = GetDatabase();
        string id = identityId.ToString();
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await db.ScriptEvaluateAsync(RefreshScript, new
        {
            refsKey = (RedisKey)RefsKey(identityId),
            onlineKey = (RedisKey)OnlineSetKey,
            ttl = _presenceOptions.PresenceTtlSeconds,
            nowMs = nowMs,
            memberId = id,
            connectionRef = connectionRef,
        });
    }

    public async Task<bool> UnregisterAsync(Guid identityId, string connectionRef, CancellationToken ct)
    {
        var db = GetDatabase();
        string id = identityId.ToString();

        var result = (int)await db.ScriptEvaluateAsync(UnregisterScript, new
        {
            refsKey = (RedisKey)RefsKey(identityId),
            onlineKey = (RedisKey)OnlineSetKey,
            memberId = id,
            connectionRef = connectionRef,
        });

        bool last = result == 1;
        if (last)
        {
            _logger.LogInformation(
                "Queue presence cleared (last ref): IdentityId={IdentityId}, ConnectionRef={ConnectionRef}",
                identityId, connectionRef);
        }

        return last;
    }

    public async Task<bool> IsAliveAsync(Guid identityId, CancellationToken ct)
    {
        var db = GetDatabase();
        return await db.KeyExistsAsync(RefsKey(identityId));
    }

    public async Task<IReadOnlyList<Guid>> SweepStaleAsync(TimeSpan staleAfter, CancellationToken ct)
    {
        var db = GetDatabase();
        long cutoffMs = DateTimeOffset.UtcNow.Subtract(staleAfter).ToUnixTimeMilliseconds();

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
            if (!Guid.TryParse(memberStr, out Guid identityId))
                continue;

            // Atomic ZREM — true only if THIS call removed the entry. Gates the
            // downstream queue/status cleanup so duplicate sweepers (future
            // multi-instance) cannot double-act on the same identity.
            bool didRemove = await db.SortedSetRemoveAsync(OnlineSetKey, memberStr);
            if (!didRemove)
                continue;

            // Defensive: refs SET should already be gone via TTL, but if a
            // race left it dangling, drop it.
            await db.KeyDeleteAsync(RefsKey(identityId));

            removed.Add(identityId);
        }

        return removed;
    }
}
