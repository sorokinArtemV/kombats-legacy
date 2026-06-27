using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Kombats.Matchmaking.Bootstrap.Workers;

/// <summary>
/// Reaps queue entries whose heartbeat presence has lapsed. Mirrors
/// PresenceSweepWorker in the chat service: periodic pass, atomic ZREM-per-
/// member gating duplicate cleanup, never-throws-out execution loop.
///
/// CRITICAL race protection: before removing a player from the queue / status
/// store, this worker checks Postgres for an active match. If one exists
/// (Queued / BattleCreateRequested / BattleCreated), the queue cleanup is
/// SKIPPED — the matched lifecycle is owned by MatchTimeoutWorker (60s for
/// BattleCreateRequested, 600s for BattleCreated). Removing here would race
/// with active match flow.
/// </summary>
internal sealed class QueuePresenceSweepWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QueuePresenceSweepWorker> _logger;
    private readonly IOptionsMonitor<QueuePresenceOptions> _options;

    public QueuePresenceSweepWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<QueuePresenceSweepWorker> logger,
        IOptionsMonitor<QueuePresenceOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue;
        _logger.LogInformation(
            "QueuePresenceSweepWorker started. Interval={IntervalSeconds}s, StaleAfter={StaleAfterSeconds}s",
            opts.SweepIntervalSeconds, opts.StaleAfterSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "QueuePresenceSweepWorker pass failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.SweepIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        var opts = _options.CurrentValue;

        using var scope = _scopeFactory.CreateScope();
        var presence = scope.ServiceProvider.GetRequiredService<IQueuePresenceStore>();
        var matchRepo = scope.ServiceProvider.GetRequiredService<IMatchRepository>();
        var queueStore = scope.ServiceProvider.GetRequiredService<IMatchQueueStore>();
        var statusStore = scope.ServiceProvider.GetRequiredService<IPlayerMatchStatusStore>();

        var staleAfter = TimeSpan.FromSeconds(opts.StaleAfterSeconds);
        var expiredIdentities = await presence.SweepStaleAsync(staleAfter, ct);

        if (expiredIdentities.Count == 0)
            return;

        int cleaned = 0;
        int skippedDueToActiveMatch = 0;

        foreach (var identityId in expiredIdentities)
        {
            // Race protection — never touch a player who is in an active match.
            // Their cleanup is owned by MatchTimeoutWorker.
            var activeMatch = await matchRepo.GetActiveForPlayerAsync(identityId, ct);
            if (activeMatch is not null)
            {
                _logger.LogInformation(
                    "QueuePresenceSweep: skipping cleanup for {IdentityId} — active match {MatchId} in state {State}",
                    identityId, activeMatch.MatchId, activeMatch.State);
                skippedDueToActiveMatch++;
                continue;
            }

            // Look up variant from the status key. The queue list/dedup set
            // are partitioned by variant — without this we can't target the
            // right keys. If the status key is already gone we fall back to
            // "default", which is the only variant in use today.
            var status = await statusStore.GetStatusAsync(identityId, ct);
            var variant = status?.Variant ?? "default";

            // Best-effort cleanup. All three operations are idempotent.
            await queueStore.TryLeaveQueueAsync(variant, identityId, ct);
            await statusStore.RemoveStatusAsync(identityId, ct);

            cleaned++;
            _logger.LogInformation(
                "QueuePresenceSweep: cleaned stale queue entry for {IdentityId}, variant={Variant}",
                identityId, variant);
        }

        _logger.LogInformation(
            "QueuePresenceSweep pass complete: expired={Expired}, cleaned={Cleaned}, skippedActiveMatch={Skipped}",
            expiredIdentities.Count, cleaned, skippedDueToActiveMatch);
    }
}
