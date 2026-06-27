using Kombats.Abstractions;
using Kombats.Matchmaking.Application.UseCases.TimeoutStaleMatches;
using Kombats.Matchmaking.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Kombats.Matchmaking.Bootstrap.Workers;

/// <summary>
/// Background service that scans for stale matches and marks them as timed out.
/// </summary>
internal sealed class MatchTimeoutWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MatchTimeoutWorker> _logger;
    private readonly MatchTimeoutWorkerOptions _options;

    public MatchTimeoutWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<MatchTimeoutWorker> logger,
        IOptions<MatchTimeoutWorkerOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MatchTimeoutWorker started. ScanInterval={ScanIntervalMs}ms, Timeout={TimeoutSeconds}s",
            _options.ScanIntervalMs, _options.TimeoutSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider
                    .GetRequiredService<ICommandHandler<TimeoutStaleMatchesCommand, int>>();

                await handler.HandleAsync(
                    new TimeoutStaleMatchesCommand(_options.TimeoutSeconds, _options.BattleCreatedTimeoutSeconds),
                    stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in MatchTimeoutWorker scan");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(_options.ScanIntervalMs), stoppingToken);
        }
    }
}
