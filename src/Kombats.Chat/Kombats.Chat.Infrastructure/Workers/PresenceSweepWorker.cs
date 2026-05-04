using Kombats.Chat.Application.Notifications;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kombats.Chat.Infrastructure.Workers;

/// <summary>
/// Hosted worker. Every <see cref="PresenceSweepOptions.ScanIntervalSeconds"/> it calls
/// <see cref="IPresenceStore.SweepStaleAsync"/>, which atomically ZREMs stale entries.
/// Only identities that THIS call actually removed are broadcast as offline (the ZREM
/// return-value gating prevents duplicate broadcasts if multiple sweepers race).
/// This is a reconciliation path, not a replacement for the graceful-disconnect handler.
/// </summary>
internal sealed class PresenceSweepWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PresenceSweepWorker> _logger;
    private readonly IOptionsMonitor<PresenceSweepOptions> _options;

    public PresenceSweepWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PresenceSweepWorker> logger,
        IOptionsMonitor<PresenceSweepOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue;
        _logger.LogInformation(
            "PresenceSweepWorker started. Interval={IntervalSeconds}s, StaleAfter={StaleAfterSeconds}s",
            opts.ScanIntervalSeconds, opts.StaleAfterSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "PresenceSweepWorker pass failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.ScanIntervalSeconds), stoppingToken);
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
        var presence = scope.ServiceProvider.GetRequiredService<IPresenceStore>();
        var notifier = scope.ServiceProvider.GetRequiredService<IChatNotifier>();

        var removed = await presence.SweepStaleAsync(TimeSpan.FromSeconds(opts.StaleAfterSeconds), ct);

        foreach (var identityId in removed)
        {
            await notifier.BroadcastPlayerOfflineAsync(new PlayerOfflineEvent(identityId), ct);
        }

        if (removed.Count > 0)
        {
            _logger.LogInformation(
                "PresenceSweepWorker removed {Count} stale presence entries", removed.Count);
        }
    }
}
