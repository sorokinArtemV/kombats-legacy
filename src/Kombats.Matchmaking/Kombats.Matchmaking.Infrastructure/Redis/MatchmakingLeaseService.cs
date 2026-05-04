using Microsoft.Extensions.Logging;

namespace Kombats.Matchmaking.Infrastructure.Redis;

/// <summary>
/// Infrastructure service that executes a callback under lease protection.
/// Owns: lease acquisition, renewal loop, release, and cancellation coordination.
/// </summary>
internal sealed class MatchmakingLeaseService
{
    private readonly RedisLeaseLock _leaseLock;
    private readonly InstanceIdService _instanceIdService;
    private readonly ILogger<MatchmakingLeaseService> _logger;

    private const int LockTtlMs = 5000; // Lock expires after 5 seconds (must be renewed)
    private static readonly int RenewalIntervalMs = LockTtlMs / 3; // Renew every 1/3 of TTL

    public MatchmakingLeaseService(
        RedisLeaseLock leaseLock,
        InstanceIdService instanceIdService,
        ILogger<MatchmakingLeaseService> logger)
    {
        _leaseLock = leaseLock;
        _instanceIdService = instanceIdService;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to acquire the lease for the given variant, and if acquired,
    /// executes the provided callback under lease protection with renewal.
    /// Returns default(T) if the lease was not acquired, otherwise returns the callback result.
    /// </summary>
    public async Task<T?> TryExecuteUnderLeaseAsync<T>(
        string variant,
        Func<CancellationToken, Task<T>> action,
        CancellationToken stoppingToken)
    {
        var instanceId = _instanceIdService.InstanceId;
        var lockKey = RedisLeaseLock.GetLockKey(variant);

        // Try to acquire lease lock for this variant
        var lockAcquired = await _leaseLock.TryAcquireLockAsync(
            lockKey,
            LockTtlMs,
            instanceId,
            stoppingToken);

        if (!lockAcquired)
        {
            _logger.LogDebug(
                "Lease lock not acquired for variant {Variant}, sleeping. InstanceId={InstanceId}",
                variant, instanceId);
            return default;
        }

        // We have the lock - start renewal loop and execute action
        using var leaseLostSource = new CancellationTokenSource();
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            leaseLostSource.Token);

        var renewalTask = StartRenewalLoopAsync(
            lockKey,
            instanceId,
            leaseLostSource,
            stoppingToken);

        try
        {
            return await action(linkedCancellation.Token);
        }
        catch (OperationCanceledException) when (leaseLostSource.Token.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            // Lease was lost - abort action
            _logger.LogWarning(
                "Action aborted due to lost lease. InstanceId={InstanceId}, Variant={Variant}",
                instanceId, variant);
            return default;
        }
        finally
        {
            // Stop renewal loop
            leaseLostSource.Cancel();
            try
            {
                await renewalTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }

            // Release lock (optional - it will expire anyway, but good practice)
            await _leaseLock.ReleaseLockAsync(lockKey, instanceId, stoppingToken);
        }
    }

    /// <summary>
    /// Starts a background renewal loop that renews the lease every renewalIntervalMs.
    /// If renewal fails (returns 0), cancels the leaseLostSource to abort the action.
    /// </summary>
    private async Task StartRenewalLoopAsync(
        string lockKey,
        string instanceId,
        CancellationTokenSource leaseLostSource,
        CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested && !leaseLostSource.Token.IsCancellationRequested)
            {
                await Task.Delay(RenewalIntervalMs, stoppingToken);

                if (stoppingToken.IsCancellationRequested || leaseLostSource.Token.IsCancellationRequested)
                    break;

                var renewalResult = await _leaseLock.RenewLeaseAsync(
                    lockKey,
                    instanceId,
                    LockTtlMs,
                    stoppingToken);

                if (renewalResult == 0)
                {
                    // Lease lost - cancel the action
                    _logger.LogWarning(
                        "Lease renewal failed (lease lost). Aborting tick. InstanceId={InstanceId}, LockKey={LockKey}",
                        instanceId, lockKey);
                    leaseLostSource.Cancel();
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
    }
}
