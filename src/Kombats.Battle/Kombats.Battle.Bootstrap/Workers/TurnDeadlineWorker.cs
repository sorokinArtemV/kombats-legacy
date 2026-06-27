using Kombats.Battle.Application.Models;
using Kombats.Battle.Application.Ports;
using Kombats.Battle.Application.UseCases.Turns;
using Microsoft.Extensions.Options;

namespace Kombats.Battle.Bootstrap.Workers;

public sealed class TurnDeadlineWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TurnDeadlineWorker> _logger;
    private readonly TurnDeadlineWorkerOptions _options;

    private int _consecutiveEmptyIterations;

    public TurnDeadlineWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<TurnDeadlineWorker> logger,
        IOptions<TurnDeadlineWorkerOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Turn deadline worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessClaimBasedTickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in turn deadline worker iteration");
                await Task.Delay(TimeSpan.FromMilliseconds(_options.ErrorDelayMs), stoppingToken);
                _consecutiveEmptyIterations = 0;
            }
        }

        _logger.LogInformation("Turn deadline worker stopped");
    }

    private async Task ProcessClaimBasedTickAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var stateStore = scope.ServiceProvider.GetRequiredService<IBattleStateStore>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var turnAppService = scope.ServiceProvider.GetRequiredService<BattleTurnAppService>();

        var now = clock.UtcNow;
        var claimedBattles = await stateStore.ClaimDueBattlesAsync(now, _options.BatchSize, _options.ClaimLeaseTtl, cancellationToken);

        int delayMs;
        if (claimedBattles.Count == 0)
        {
            _consecutiveEmptyIterations++;
            delayMs = Math.Min(
                (int)(_options.IdleDelayMinMs * Math.Pow(2, Math.Min(_consecutiveEmptyIterations - 1, _options.MaxBackoffSteps))),
                _options.IdleDelayMaxMs);
        }
        else
        {
            _consecutiveEmptyIterations = 0;
            delayMs = _options.BacklogDelayMs;

            foreach (var claimed in claimedBattles)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var resolved = await turnAppService.ResolveTurnAsync(claimed.BattleId, cancellationToken);
                    if (resolved)
                    {
                        _logger.LogInformation("Resolved battle {BattleId} via deadline", claimed.BattleId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Transient error processing battle {BattleId}", claimed.BattleId);
                }
            }
        }

        await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
    }
}

public class TurnDeadlineWorkerOptions
{
    public int BatchSize { get; set; } = 50;
    public TimeSpan ClaimLeaseTtl { get; set; } = TimeSpan.FromSeconds(12);
    public int IdleDelayMinMs { get; set; } = 200;
    public int IdleDelayMaxMs { get; set; } = 1000;
    public int BacklogDelayMs { get; set; } = 30;
    public int MaxBackoffSteps { get; set; } = 3;
    public int ErrorDelayMs { get; set; } = 200;
}
