using Kombats.Chat.Application.Repositories;
using Kombats.Chat.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kombats.Chat.Infrastructure.Workers;

/// <summary>
/// Hosted worker. Every <see cref="MessageRetentionOptions.ScanIntervalSeconds"/> deletes
/// messages older than <see cref="MessageRetentionOptions.MessageTtlHours"/> in batches of
/// <see cref="MessageRetentionOptions.BatchSize"/> rows (limits lock duration), then
/// removes empty direct conversations. The global conversation is never deleted because
/// <see cref="IConversationRepository.DeleteEmptyDirectConversationsAsync"/> targets only
/// conversations of type <c>Direct</c>.
/// </summary>
internal sealed class MessageRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MessageRetentionWorker> _logger;
    private readonly IOptionsMonitor<MessageRetentionOptions> _options;

    public MessageRetentionWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<MessageRetentionWorker> logger,
        IOptionsMonitor<MessageRetentionOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue;
        _logger.LogInformation(
            "MessageRetentionWorker started. Interval={IntervalSeconds}s, Ttl={TtlHours}h, BatchSize={BatchSize}",
            opts.ScanIntervalSeconds, opts.MessageTtlHours, opts.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "MessageRetentionWorker pass failed");
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
        var cutoff = DateTimeOffset.UtcNow.AddHours(-opts.MessageTtlHours);

        using var scope = _scopeFactory.CreateScope();
        var messages = scope.ServiceProvider.GetRequiredService<IMessageRepository>();
        var conversations = scope.ServiceProvider.GetRequiredService<IConversationRepository>();

        int totalDeleted = 0;
        for (int batch = 0; batch < opts.MaxBatchesPerPass; batch++)
        {
            int deleted = await messages.DeleteExpiredAsync(cutoff, opts.BatchSize, ct);
            totalDeleted += deleted;
            if (deleted < opts.BatchSize)
                break;
        }

        await conversations.DeleteEmptyDirectConversationsAsync(ct);

        if (totalDeleted > 0)
        {
            _logger.LogInformation(
                "MessageRetentionWorker deleted {Count} expired messages (cutoff={Cutoff:o})",
                totalDeleted, cutoff);
        }
    }
}
