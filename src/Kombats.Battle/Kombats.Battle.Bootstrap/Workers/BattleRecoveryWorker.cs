using Kombats.Battle.Application.UseCases.Recovery;
using Microsoft.Extensions.Options;

namespace Kombats.Battle.Bootstrap.Workers;

/// <summary>
/// Thin background scheduler that periodically triggers battle recovery.
/// All orchestration logic lives in <see cref="BattleRecoveryService"/> (Application layer).
/// </summary>
public sealed class BattleRecoveryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<BattleRecoveryWorker> logger,
    IOptions<BattleRecoveryWorkerOptions> options) : BackgroundService
{
    private readonly BattleRecoveryWorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "BattleRecoveryWorker started. ScanInterval={ScanIntervalMs}ms, StaleBattleThreshold={StaleBattleThresholdSeconds}s",
            _options.ScanIntervalMs, _options.StaleBattleThresholdSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var recoveryService = scope.ServiceProvider.GetRequiredService<BattleRecoveryService>();

                var cutoff = DateTimeOffset.UtcNow.AddSeconds(-_options.StaleBattleThresholdSeconds);
                await recoveryService.ScanAndRecoverAsync(cutoff, _options.BatchSize, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error in BattleRecoveryWorker scan");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(_options.ScanIntervalMs), stoppingToken);
        }
    }
}

public sealed class BattleRecoveryWorkerOptions
{
    public int ScanIntervalMs { get; set; } = 30000;
    public int StaleBattleThresholdSeconds { get; set; } = 600;
    public int BatchSize { get; set; } = 50;
}
