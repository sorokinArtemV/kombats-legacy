using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Kombats.Matchmaking.Infrastructure.Redis;

/// <summary>
/// Infrastructure implementation of IMatchQueueStore using Redis.
/// Uses Lua scripts for atomic operations.
/// </summary>
internal sealed class RedisMatchQueueStore : IMatchQueueStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisMatchQueueStore> _logger;
    private readonly MatchmakingRedisOptions _options;

    public RedisMatchQueueStore(
        IConnectionMultiplexer redis,
        ILogger<RedisMatchQueueStore> logger,
        IOptions<MatchmakingRedisOptions> options)
    {
        _redis = redis;
        _logger = logger;
        _options = options.Value;
    }

    private IDatabase GetDatabase() => _redis.GetDatabase(_options.DatabaseIndex);

    private string GetQueueKey(string variant) => $"mm:queue:{variant}";
    private string GetQueuedSetKey(string variant) => $"mm:queued:{variant}";
    private string GetCanceledSetKey(string variant) => $"mm:canceled:{variant}";

    public async Task<bool> TryJoinQueueAsync(string variant, Guid playerId, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var queueKey = GetQueueKey(variant);
        var queuedKey = GetQueuedSetKey(variant);
        var canceledKey = GetCanceledSetKey(variant);
        var playerIdStr = playerId.ToString();
        var nowEpochSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var cancelTtlSeconds = _options.CancelTtlSeconds;

        try
        {
            var result = await db.ScriptEvaluateAsync(
                RedisScripts.JoinQueueScript,
                [queueKey, queuedKey, canceledKey],
                [playerIdStr, nowEpochSeconds, cancelTtlSeconds]);

            var added = (int)result == 1;

            if (added)
            {
                _logger.LogInformation(
                    "Player joined queue: PlayerId={PlayerId}, Variant={Variant}",
                    playerId, variant);
            }

            return added;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in TryJoinQueueAsync for PlayerId: {PlayerId}, Variant: {Variant}, DatabaseIndex={DatabaseIndex}",
                playerId, variant, _options.DatabaseIndex);
            throw;
        }
    }

    public async Task<bool> TryLeaveQueueAsync(string variant, Guid playerId, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var queueKey = GetQueueKey(variant);
        var queuedKey = GetQueuedSetKey(variant);
        var canceledKey = GetCanceledSetKey(variant);
        var playerIdStr = playerId.ToString();
        var nowEpochSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var cancelTtlSeconds = _options.CancelTtlSeconds;

        try
        {
            var result = await db.ScriptEvaluateAsync(
                RedisScripts.LeaveQueueScript,
                new RedisKey[] { queueKey, queuedKey, canceledKey },
                new RedisValue[] { playerIdStr, nowEpochSeconds, cancelTtlSeconds });

            var removed = (int)result == 1;
            
            if (removed)
            {
                _logger.LogInformation(
                    "Player {PlayerId} left queue for variant {Variant}",
                    playerId, variant);
            }
            
            return removed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in TryLeaveQueueAsync for PlayerId: {PlayerId}, Variant: {Variant}",
                playerId, variant);
            throw;
        }
    }

    public async Task<(Guid PlayerAId, Guid PlayerBId)?> TryPopPairAsync(string variant, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var queueKey = GetQueueKey(variant);
        var queuedKey = GetQueuedSetKey(variant);
        var canceledKey = GetCanceledSetKey(variant);
        var nowEpochSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var cancelTtlSeconds = _options.CancelTtlSeconds;

        try
        {
            var result = await db.ScriptEvaluateAsync(
                RedisScripts.TryPopPairScript,
                [queueKey, queuedKey, canceledKey],
                [nowEpochSeconds, cancelTtlSeconds]);

            if (result.IsNull)
            {
                return null;
            }

            var results = (RedisValue[]?)result;
            if (results == null || results.Length < 2)
            {
                return null;
            }

            var playerAIdStr = results[0].ToString();
            var playerBIdStr = results[1].ToString();

            if (!Guid.TryParse(playerAIdStr, out var playerAId) || !Guid.TryParse(playerBIdStr, out var playerBId))
            {
                _logger.LogError(
                    "Failed to parse player IDs from Redis: PlayerA={PlayerA}, PlayerB={PlayerB}",
                    playerAIdStr, playerBIdStr);
                return null;
            }

            return (playerAId, playerBId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in TryPopPairAsync for Variant: {Variant}",
                variant);
            throw;
        }
    }

    public async Task TryRequeueAsync(string variant, Guid playerId, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var queueKey = GetQueueKey(variant);
        var queuedKey = GetQueuedSetKey(variant);
        var playerIdStr = playerId.ToString();

        try
        {
            await db.ScriptEvaluateAsync(
                RedisScripts.RequeueScript,
                [queueKey, queuedKey],
                [playerIdStr]);

            _logger.LogInformation(
                "Player re-queued after failed match: PlayerId={PlayerId}, Variant={Variant}",
                playerId, variant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in TryRequeueAsync for PlayerId: {PlayerId}, Variant: {Variant}",
                playerId, variant);
            throw;
        }
    }
}

