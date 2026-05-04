using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kombats.Battle.Realtime.Contracts;
using Kombats.Bff.Application.Clients;
using Kombats.Bff.Application.Narration;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kombats.Bff.Application.Relay;

public sealed class BattleHubRelay : IBattleHubRelay, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, BattleConnection> _connections = new();
    private readonly ServicesOptions _servicesOptions;
    private readonly IFrontendBattleSender _sender;
    private readonly INarrationPipeline _narrationPipeline;
    private readonly ILogger<BattleHubRelay> _logger;

    /// <summary>Event names relayed blindly (no narration, no deserialization).</summary>
    private static readonly string[] BlindEventNames = ["BattleReady", "TurnOpened", "PlayerDamaged"];

    /// <summary>The SignalR event name used for narration feed entries.</summary>
    public const string BattleFeedUpdatedEvent = "BattleFeedUpdated";

    public BattleHubRelay(
        IOptions<ServicesOptions> servicesOptions,
        IFrontendBattleSender sender,
        INarrationPipeline narrationPipeline,
        ILogger<BattleHubRelay> logger)
    {
        _servicesOptions = servicesOptions.Value;
        _sender = sender;
        _narrationPipeline = narrationPipeline;
        _logger = logger;
    }

    public async Task<object> JoinBattleAsync(
        Guid battleId,
        string frontendConnectionId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        // If there's already a connection for this frontend connection, dispose it first (reconnect)
        await DisconnectAsync(frontendConnectionId);

        string battleHubUrl = $"{_servicesOptions.Battle.BaseUrl.TrimEnd('/')}/battlehub";

        Activity? activity = Activity.Current;
        string? traceparent = activity?.Id;
        string? tracestate = activity?.TraceStateString;

        var hubBuilder = new HubConnectionBuilder()
            .WithUrl(battleHubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);

                if (!string.IsNullOrEmpty(traceparent))
                {
                    options.Headers["traceparent"] = traceparent;
                    if (!string.IsNullOrEmpty(tracestate))
                        options.Headers["tracestate"] = tracestate;
                }
            });

        // Match the Battle service's SignalR JSON config: enums are serialized as strings.
        // Without this, typed On<T> handlers fail to deserialize enum fields (Outcome, Phase, Reason)
        // and silently drop TurnResolved / BattleStateUpdated / BattleEnded events.
        hubBuilder.Services.Configure<JsonHubProtocolOptions>(opt =>
        {
            opt.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        HubConnection connection = hubBuilder.Build();

        // Blind relay handlers — forward as-is, no deserialization
        foreach (string eventName in BlindEventNames)
        {
            connection.On<object>(eventName, async (payload) =>
            {
                try
                {
                    await _sender.SendAsync(frontendConnectionId, eventName, [payload]);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to relay {EventName} to frontend connection {ConnectionId}",
                        eventName, frontendConnectionId);
                }
            });
        }

        // Typed handler: TurnResolved — relay raw, then generate + send BattleFeedUpdated
        connection.On<TurnResolvedRealtime>(RealtimeEventNames.TurnResolved, async (turnResolved) =>
        {
            // Always relay raw event first
            try
            {
                await _sender.SendAsync(frontendConnectionId, RealtimeEventNames.TurnResolved, [turnResolved]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to relay TurnResolved to frontend {ConnectionId}", frontendConnectionId);
            }

            // Then generate narration (best-effort — never block raw relay)
            try
            {
                if (_connections.TryGetValue(frontendConnectionId, out var bc) && bc.State is not null)
                {
                    var state = bc.State;
                    var feed = _narrationPipeline.GenerateTurnFeed(
                        state.BattleId,
                        turnResolved,
                        state.Participants,
                        state.Commentator,
                        state.PlayerAHp,
                        state.PlayerBHp,
                        state.PlayerAMaxHp,
                        state.PlayerBMaxHp);

                    if (feed.Entries.Length > 0)
                    {
                        await _sender.SendAsync(frontendConnectionId, BattleFeedUpdatedEvent, [feed]);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to generate/send BattleFeedUpdated for TurnResolved on {ConnectionId}",
                    frontendConnectionId);
            }
        });

        // Typed handler: BattleEnded — relay raw, then generate + send BattleFeedUpdated
        connection.On<BattleEndedRealtime>(RealtimeEventNames.BattleEnded, async (ended) =>
        {
            // Always relay raw event first
            try
            {
                await _sender.SendAsync(frontendConnectionId, RealtimeEventNames.BattleEnded, [ended]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to relay BattleEnded to frontend {ConnectionId}", frontendConnectionId);
            }

            // Generate end-of-battle narration (best-effort)
            try
            {
                if (_connections.TryGetValue(frontendConnectionId, out var bc) && bc.State is not null)
                {
                    var state = bc.State;
                    var feed = _narrationPipeline.GenerateBattleEndFeed(
                        state.BattleId,
                        ended,
                        state.Participants,
                        state.Commentator);

                    if (feed.Entries.Length > 0)
                    {
                        await _sender.SendAsync(frontendConnectionId, BattleFeedUpdatedEvent, [feed]);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to generate/send BattleFeedUpdated for BattleEnded on {ConnectionId}",
                    frontendConnectionId);
            }

            // Auto-cleanup downstream connection
            _logger.LogInformation(
                "Battle ended for frontend {ConnectionId}, cleaning up downstream connection",
                frontendConnectionId);
            await DisconnectAsync(frontendConnectionId);
        });

        // Typed handler: BattleStateUpdated — capture HP into connection state, relay raw
        connection.On<BattleStateUpdatedRealtime>(RealtimeEventNames.BattleStateUpdated, async (stateUpdate) =>
        {
            // Update HP tracking in connection state
            try
            {
                if (_connections.TryGetValue(frontendConnectionId, out var bc) && bc.State is not null)
                {
                    bc.State.PlayerAHp = stateUpdate.PlayerAHp;
                    bc.State.PlayerBHp = stateUpdate.PlayerBHp;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to update HP state from BattleStateUpdated on {ConnectionId}",
                    frontendConnectionId);
            }

            // Relay raw
            try
            {
                await _sender.SendAsync(frontendConnectionId, RealtimeEventNames.BattleStateUpdated, [stateUpdate]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to relay BattleStateUpdated to frontend {ConnectionId}", frontendConnectionId);
            }
        });

        // Handle downstream connection closure
        connection.Closed += async (exception) =>
        {
            _logger.LogInformation(
                "Downstream Battle connection closed for frontend {ConnectionId}. Exception: {Error}",
                frontendConnectionId, exception?.Message);

            try
            {
                await _sender.SendAsync(frontendConnectionId, "BattleConnectionLost",
                    [new { Reason = exception?.Message ?? "Connection closed" }]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to notify frontend {ConnectionId} about connection loss",
                    frontendConnectionId);
            }
        };

        // Store connection before connecting
        var battleConnection = new BattleConnection(connection, null);
        _connections[frontendConnectionId] = battleConnection;

        try
        {
            await connection.StartAsync(cancellationToken);

            _logger.LogInformation(
                "Connected to Battle hub for frontend {ConnectionId}, joining battle {BattleId}",
                frontendConnectionId, battleId);

            // Call JoinBattle on the downstream Battle hub
            object snapshot = await connection.InvokeAsync<object>(
                "JoinBattle",
                battleId,
                cancellationToken);

            // Deserialize snapshot to capture participant names and max HP
            BattleConnectionState? connectionState = null;
            try
            {
                var snapshotRealtime = DeserializeSnapshot(snapshot);
                if (snapshotRealtime is not null)
                {
                    var participants = new BattleParticipantSnapshot(
                        snapshotRealtime.PlayerAId,
                        snapshotRealtime.PlayerBId,
                        snapshotRealtime.PlayerAName,
                        snapshotRealtime.PlayerBName);

                    connectionState = new BattleConnectionState
                    {
                        BattleId = battleId,
                        Participants = participants,
                        Commentator = new CommentatorState(),
                        PlayerAHp = snapshotRealtime.PlayerAHp,
                        PlayerBHp = snapshotRealtime.PlayerBHp,
                        PlayerAMaxHp = snapshotRealtime.PlayerAMaxHp,
                        PlayerBMaxHp = snapshotRealtime.PlayerBMaxHp
                    };

                    // Update connection with state
                    _connections[frontendConnectionId] = new BattleConnection(connection, connectionState);

                    // Send battle start feed entry
                    var startFeed = _narrationPipeline.GenerateBattleStartFeed(battleId, participants);
                    if (startFeed.Entries.Length > 0)
                    {
                        await _sender.SendAsync(frontendConnectionId, BattleFeedUpdatedEvent, [startFeed]);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to deserialize snapshot or send battle start feed for {ConnectionId}. Raw relay continues.",
                    frontendConnectionId);
            }

            return snapshot;
        }
        catch
        {
            _connections.TryRemove(frontendConnectionId, out _);
            await DisposeConnectionSafely(connection);
            throw;
        }
    }

    public async Task SubmitTurnActionAsync(
        string frontendConnectionId,
        Guid battleId,
        int turnIndex,
        string actionPayload,
        CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(frontendConnectionId, out var bc))
        {
            throw new InvalidOperationException(
                $"No active battle connection for frontend connection {frontendConnectionId}. Call JoinBattle first.");
        }

        if (bc.Hub.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException(
                $"Battle connection is in {bc.Hub.State} state, not Connected.");
        }

        try
        {
            // Don't propagate caller's CancellationToken to the downstream invoke.
            // Once the action reaches Battle, it's committed to Redis before resolution starts.
            // Cancelling the invoke doesn't undo the action — it just loses the acknowledgement.
            // The downstream HubConnection has its own ServerTimeout (default 30s) for stalls.
            await bc.Hub.InvokeAsync(
                "SubmitTurnAction",
                battleId,
                turnIndex,
                actionPayload,
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
        {
            // On battle-ending turns, the BattleEnded handler fires before the Completion frame
            // arrives (SignalR message ordering: notification sent before hub method returns).
            // The handler calls DisconnectAsync which kills this connection, cancelling the
            // in-flight InvokeAsync. The action was already processed — this is expected.
            if (!_connections.ContainsKey(frontendConnectionId))
            {
                _logger.LogInformation(
                    "Downstream SubmitTurnAction cancelled for {ConnectionId} battle {BattleId} turn {TurnIndex} — "
                    + "battle ended during resolution (action was processed successfully)",
                    frontendConnectionId, battleId, turnIndex);
                return;
            }

            // Connection still in dictionary but invoke cancelled — genuine failure
            _logger.LogWarning(ex,
                "Downstream SubmitTurnAction cancelled unexpectedly for {ConnectionId} battle {BattleId} turn {TurnIndex}",
                frontendConnectionId, battleId, turnIndex);
            throw;
        }
    }

    public async Task DisconnectAsync(string frontendConnectionId)
    {
        if (_connections.TryRemove(frontendConnectionId, out var bc))
        {
            _logger.LogInformation(
                "Disposing downstream Battle connection for frontend {ConnectionId}",
                frontendConnectionId);
            await DisposeConnectionSafely(bc.Hub);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (string connectionId in _connections.Keys.ToArray())
        {
            await DisconnectAsync(connectionId);
        }
    }

    private static readonly JsonSerializerOptions SnapshotSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static BattleSnapshotRealtime? DeserializeSnapshot(object snapshot)
    {
        if (snapshot is JsonElement jsonElement)
        {
            return JsonSerializer.Deserialize<BattleSnapshotRealtime>(
                jsonElement.GetRawText(),
                SnapshotSerializerOptions);
        }

        return null;
    }

    private static async Task DisposeConnectionSafely(HubConnection connection)
    {
        try
        {
            if (connection.State != HubConnectionState.Disconnected)
            {
                await connection.StopAsync();
            }
            await connection.DisposeAsync();
        }
        catch
        {
            // Best-effort cleanup — don't throw on dispose
        }
    }

    /// <summary>Wraps a downstream HubConnection with its per-connection narration state.</summary>
    internal sealed record BattleConnection(HubConnection Hub, BattleConnectionState? State);
}
