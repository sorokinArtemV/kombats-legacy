using Kombats.Abstractions;
using Kombats.Matchmaking.Application.UseCases.ExecuteMatchmakingTick;
using Kombats.Matchmaking.Infrastructure;
using Kombats.Matchmaking.Infrastructure.Options;
using Kombats.Matchmaking.Infrastructure.Redis;
using Microsoft.Extensions.Options;

namespace Kombats.Matchmaking.Bootstrap.Workers;

/// <summary>
/// Background service that runs matchmaking ticks under lease protection.
/// </summary>
internal sealed class MatchmakingPairingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MatchmakingLeaseService _leaseService;
    private readonly ILogger<MatchmakingPairingWorker> _logger;
    private readonly MatchmakingWorkerOptions _options;
    private readonly InstanceIdService _instanceIdService;

    public MatchmakingPairingWorker(
        IServiceScopeFactory scopeFactory,
        MatchmakingLeaseService leaseService,
        ILogger<MatchmakingPairingWorker> logger,
        IOptions<MatchmakingWorkerOptions> options,
        InstanceIdService instanceIdService)
    {
        _scopeFactory = scopeFactory;
        _leaseService = leaseService;
        _logger = logger;
        _options = options.Value;
        _instanceIdService = instanceIdService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MatchmakingPairingWorker started. InstanceId={InstanceId}, TickDelay={TickDelayMs}ms, MaxPairsPerTick={MaxPairsPerTick}",
            _instanceIdService.InstanceId, _options.TickDelayMs, _options.MaxPairsPerTick);

        const string variant = "default";

        while (!stoppingToken.IsCancellationRequested)
        {
            var pairsCreated = 0;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider
                    .GetRequiredService<ICommandHandler<ExecuteMatchmakingTickCommand, MatchmakingTickResult>>();

                var maxPairsPerTick = _options.MaxPairsPerTick;

                var result = await _leaseService.TryExecuteUnderLeaseAsync(
                    variant,
                    async ct =>
                    {
                        var r = await handler.HandleAsync(
                            new ExecuteMatchmakingTickCommand(variant, maxPairsPerTick), ct);
                        return r.IsSuccess ? r.Value : new MatchmakingTickResult(0);
                    },
                    stoppingToken);

                pairsCreated = result?.PairsCreated ?? 0;

                if (pairsCreated > 0)
                {
                    _logger.LogInformation(
                        "Matchmaking tick paired {PairsCreated} matches", pairsCreated);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in MatchmakingPairingWorker tick");
            }

            // Idle backoff: only sleep when the tick was idle. If we drained at least one
            // pair this tick, immediately try again — the queue may still be hot.
            if (pairsCreated == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(_options.TickDelayMs), stoppingToken);
            }
        }
    }
}
