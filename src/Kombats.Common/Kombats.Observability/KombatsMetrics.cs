using System.Diagnostics.Metrics;

namespace Kombats.Observability;

/// <summary>
/// Single per-process container for Kombats custom metrics. Instruments are created
/// once on the service's Meter (Kombats.{serviceName}) and held for the lifetime of
/// the process. Code that does not record a particular instrument simply ignores it
/// — instruments are cheap and unused ones emit no measurements.
/// </summary>
public sealed class KombatsMetrics : IDisposable
{
    public const string MeterPrefix = "Kombats.";

    private readonly Meter _meter;

    public string ServiceName { get; }
    public string MeterName => _meter.Name;

    // Battle
    public Histogram<double> TurnResolutionDurationMs { get; }
    public UpDownCounter<long> ActiveBattles { get; }

    // Battle and BFF both maintain inbound SignalR connections; each runs in its own
    // process with its own Meter, so the metric name collision is intentional and
    // segmented by the service.name resource attribute.
    public UpDownCounter<long> ActiveSignalRConnections { get; }

    // BFF only — counts the outbound HubConnection objects BattleHubRelay maintains
    // to the Battle service, one per frontend connection.
    public UpDownCounter<long> DownstreamHubConnections { get; }

    // Matchmaking
    public Histogram<double> PairingDurationMs { get; }
    public UpDownCounter<long> QueuedPlayers { get; }

    public KombatsMetrics(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name is required.", nameof(serviceName));

        ServiceName = serviceName;
        _meter = new Meter(MeterPrefix + serviceName, "1.0.0");

        TurnResolutionDurationMs = _meter.CreateHistogram<double>(
            name: "turn_resolution_duration",
            unit: "ms",
            description: "Battle turn resolution latency (engine + Redis commit + broadcast).");

        ActiveBattles = _meter.CreateUpDownCounter<long>(
            name: "active_battles",
            unit: "{battle}",
            description: "Battles currently in flight on this Battle replica.");

        ActiveSignalRConnections = _meter.CreateUpDownCounter<long>(
            name: "active_signalr_connections",
            unit: "{connection}",
            description: "SignalR connections currently attached to this process's hub.");

        DownstreamHubConnections = _meter.CreateUpDownCounter<long>(
            name: "downstream_hub_connections",
            unit: "{connection}",
            description: "Outbound SignalR client connections held open by this process.");

        PairingDurationMs = _meter.CreateHistogram<double>(
            name: "pairing_duration_ms",
            unit: "ms",
            description: "Time taken by a single matchmaking pairing tick.");

        QueuedPlayers = _meter.CreateUpDownCounter<long>(
            name: "queued_players",
            unit: "{player}",
            description: "Players currently sitting in the matchmaking queue.");
    }

    public void Dispose() => _meter.Dispose();
}
