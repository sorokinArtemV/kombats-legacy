using System.Collections.Concurrent;
using Kombats.Chat.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kombats.Chat.Api.Hubs;

/// <summary>
/// Per-connection 30-second heartbeat timer that drives <see cref="IPresenceStore.HeartbeatAsync"/>.
/// Application-level (not SignalR keepalive) per the approved Batch 3 design.
/// Heartbeat failures are logged but never crash the process or corrupt state.
/// </summary>
internal sealed class HeartbeatScheduler(
    IServiceScopeFactory scopeFactory,
    ILogger<HeartbeatScheduler> logger,
    TimeProvider timeProvider) : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, ITimer> _timers = new();

    public void Start(string connectionId, Guid identityId)
    {
        ITimer timer = timeProvider.CreateTimer(
            callback: _ => _ = TickAsync(identityId),
            state: null,
            dueTime: Interval,
            period: Interval);

        if (!_timers.TryAdd(connectionId, timer))
        {
            timer.Dispose();
        }
    }

    public void Stop(string connectionId)
    {
        if (_timers.TryRemove(connectionId, out var timer))
        {
            timer.Dispose();
        }
    }

    /// <summary>
    /// Internal seam exposed for testing. Performs a single heartbeat tick using
    /// a freshly-resolved <see cref="IPresenceStore"/> from a new DI scope.
    /// Defensive: any exception is logged and swallowed — never crashes the timer.
    /// </summary>
    internal async Task TickAsync(Guid identityId)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var presence = scope.ServiceProvider.GetRequiredService<IPresenceStore>();
            await presence.HeartbeatAsync(identityId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Heartbeat failed for {IdentityId}", identityId);
        }
    }

    public void Dispose()
    {
        foreach (var kvp in _timers)
        {
            kvp.Value.Dispose();
        }
        _timers.Clear();
    }
}
