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
            "MatchmakingPairingWorker started. InstanceId={InstanceId}, TickDelay={TickDelayMs}ms",
            _instanceIdService.InstanceId, _options.TickDelayMs);

        const string variant = "default";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider
                    .GetRequiredService<ICommandHandler<ExecuteMatchmakingTickCommand, MatchmakingTickResult>>();

                var result = await _leaseService.TryExecuteUnderLeaseAsync(
                    variant,
                    async ct =>
                    {
                        var r = await handler.HandleAsync(new ExecuteMatchmakingTickCommand(variant), ct);
                        return r.IsSuccess ? r.Value : new MatchmakingTickResult(false);
                    },
                    stoppingToken);

                if (result is { MatchCreated: true })
                {
                    _logger.LogInformation(
                        "Match created: MatchId={MatchId}, BattleId={BattleId}",
                        result.MatchId, result.BattleId);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in MatchmakingPairingWorker tick");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(_options.TickDelayMs), stoppingToken);
        }
    }
}
