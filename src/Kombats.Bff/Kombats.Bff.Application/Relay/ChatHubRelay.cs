using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Kombats.Bff.Application.Clients;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kombats.Bff.Application.Relay;

/// <summary>
/// Per-frontend-connection relay between the BFF client-facing chat hub
/// (<c>/chathub</c>) and the Chat service's internal hub (<c>/chathub-internal</c>).
/// Forwards frontend invocations to the downstream hub and blindly relays the
/// frozen Batch 3 server-to-client events back to the frontend.
/// </summary>
public sealed class ChatHubRelay : IChatHubRelay, IAsyncDisposable
{
    /// <summary>Default per-invocation timeout. EQ-5 option (b) — bound stalls so the frontend gets a deterministic <c>ChatConnectionLost</c>.</summary>
    public static readonly TimeSpan DefaultInvocationTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Effective per-invocation timeout. Tests may override via the internal constructor.</summary>
    public TimeSpan InvocationTimeout { get; }

    /// <summary>SignalR event name relayed to the frontend when the downstream connection drops or hangs.</summary>
    public const string ChatConnectionLostEvent = "ChatConnectionLost";

    /// <summary>Frozen Batch 3 server-to-client event names — relayed verbatim, no remapping.</summary>
    internal static readonly string[] RelayedEventNames =
    [
        "GlobalMessageReceived",
        "DirectMessageReceived",
        "PlayerOnline",
        "PlayerOffline",
        "ChatError"
    ];

    private readonly ConcurrentDictionary<string, ChatConnection> _connections = new();
    private readonly ServicesOptions _servicesOptions;
    private readonly IFrontendChatSender _sender;
    private readonly ILogger<ChatHubRelay> _logger;

    public ChatHubRelay(
        IOptions<ServicesOptions> servicesOptions,
        IFrontendChatSender sender,
        ILogger<ChatHubRelay> logger)
        : this(servicesOptions, sender, logger, DefaultInvocationTimeout)
    {
    }

    internal ChatHubRelay(
        IOptions<ServicesOptions> servicesOptions,
        IFrontendChatSender sender,
        ILogger<ChatHubRelay> logger,
        TimeSpan invocationTimeout)
    {
        _servicesOptions = servicesOptions.Value;
        _sender = sender;
        _logger = logger;
        InvocationTimeout = invocationTimeout;
    }

    public async Task ConnectAsync(string frontendConnectionId, string accessToken, CancellationToken cancellationToken = default)
    {
        // If a previous downstream exists (e.g. forced reconnect), tear it down first.
        await DisconnectAsync(frontendConnectionId);

        if (_servicesOptions.Chat is null)
        {
            throw new InvalidOperationException("Services:Chat:BaseUrl is not configured.");
        }

        string chatHubUrl = $"{_servicesOptions.Chat.BaseUrl.TrimEnd('/')}/chathub-internal";

        Activity? activity = Activity.Current;
        string? traceparent = activity?.Id;
        string? tracestate = activity?.TraceStateString;

        var hubBuilder = new HubConnectionBuilder()
            .WithUrl(chatHubUrl, options =>
            {
                // The frontend's access token is captured at connect time and reused
                // for downstream auth. SignalR re-invokes this provider on
                // reconnection, but the captured value cannot refresh — when the JWT
                // expires the downstream will reject and the Closed handler will
                // surface ChatConnectionLost. Future hardening: subscribe to the
                // frontend's token-refresh signal and update the provider's source.
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);

                if (!string.IsNullOrEmpty(traceparent))
                {
                    options.Headers["traceparent"] = traceparent;
                    if (!string.IsNullOrEmpty(tracestate))
                        options.Headers["tracestate"] = tracestate;
                }
            });

        // Match Chat hub's JSON config: enums-as-strings. Keeps blind relay deserialization stable.
        hubBuilder.Services.Configure<JsonHubProtocolOptions>(opt =>
        {
            opt.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        HubConnection connection = hubBuilder.Build();

        // Blind relay handlers — forward Chat events as-is to the frontend, no remapping, no buffering.
        foreach (string eventName in RelayedEventNames)
        {
            string captured = eventName;
            connection.On<object>(captured, async (payload) =>
            {
                try
                {
                    await _sender.SendAsync(frontendConnectionId, captured, [payload]);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to relay {EventName} to frontend chat connection {ConnectionId}",
                        captured, frontendConnectionId);
                }
            });
        }

        var chatConnection = new ChatConnection(frontendConnectionId, connection);
        _connections[frontendConnectionId] = chatConnection;

        // Downstream drop → notify the frontend (only on real, unintentional loss)
        // and clean up tracking. Intentional close paths (DisconnectAsync,
        // DisposeAsync, forced reconnect, timeout teardown) flip
        // chatConnection.IntentionalClose first so this handler stays silent.
        connection.Closed += async (exception) =>
        {
            bool intentional = chatConnection.IntentionalClose;

            _logger.LogInformation(
                "Downstream Chat connection closed for frontend {ConnectionId}. Intentional={Intentional}. Exception: {Error}",
                frontendConnectionId, intentional, exception?.Message);

            if (!intentional)
            {
                try
                {
                    await _sender.SendAsync(frontendConnectionId, ChatConnectionLostEvent,
                        [new { Reason = "connection_lost" }]);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to notify frontend chat connection {ConnectionId} about downstream loss",
                        frontendConnectionId);
                }

                // Remove the entry so subsequent invokes fail fast instead of using a dead connection.
                _connections.TryRemove(frontendConnectionId, out _);
            }
        };

        try
        {
            await connection.StartAsync(cancellationToken);

            _logger.LogInformation(
                "Connected to Chat hub for frontend {ConnectionId} (downstream count={Count})",
                frontendConnectionId, _connections.Count);
        }
        catch
        {
            _connections.TryRemove(frontendConnectionId, out _);
            await DisposeConnectionSafely(connection);
            throw;
        }
    }

    public Task<object?> JoinGlobalChatAsync(string frontendConnectionId, CancellationToken cancellationToken = default) =>
        InvokeWithTimeoutAsync<object?>(frontendConnectionId, "JoinGlobalChat", Array.Empty<object?>(), cancellationToken);

    public async Task LeaveGlobalChatAsync(string frontendConnectionId, CancellationToken cancellationToken = default)
    {
        await InvokeWithTimeoutAsync(frontendConnectionId, "LeaveGlobalChat", Array.Empty<object?>(), cancellationToken);
    }

    public async Task SendGlobalMessageAsync(string frontendConnectionId, string content, CancellationToken cancellationToken = default)
    {
        await InvokeWithTimeoutAsync(frontendConnectionId, "SendGlobalMessage", new object?[] { content }, cancellationToken);
    }

    public Task<object?> SendDirectMessageAsync(string frontendConnectionId, Guid recipientPlayerId, string content, CancellationToken cancellationToken = default) =>
        InvokeWithTimeoutAsync<object?>(frontendConnectionId, "SendDirectMessage", new object?[] { recipientPlayerId, content }, cancellationToken);

    public async Task DisconnectAsync(string frontendConnectionId)
    {
        if (_connections.TryRemove(frontendConnectionId, out var conn))
        {
            // Mark before disposing so the Closed handler (which fires during
            // StopAsync) suppresses the spurious ChatConnectionLost notification.
            conn.IntentionalClose = true;
            _logger.LogInformation(
                "Disposing downstream Chat connection for frontend {ConnectionId}",
                frontendConnectionId);
            await DisposeConnectionSafely(conn.Hub);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (string connectionId in _connections.Keys.ToArray())
        {
            await DisconnectAsync(connectionId);
        }
    }

    private async Task<T> InvokeWithTimeoutAsync<T>(
        string frontendConnectionId,
        string method,
        object?[] args,
        CancellationToken cancellationToken)
    {
        ChatConnection conn = GetConnectedOrThrow(frontendConnectionId);

        using var timeoutCts = new CancellationTokenSource(InvocationTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            return await conn.Hub.InvokeCoreAsync<T>(method, args, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await HandleInvocationTimeoutAsync(frontendConnectionId, method);
            throw new TimeoutException(
                $"Downstream Chat invocation '{method}' for frontend connection {frontendConnectionId} timed out after {InvocationTimeout.TotalSeconds}s.");
        }
    }

    private async Task InvokeWithTimeoutAsync(
        string frontendConnectionId,
        string method,
        object?[] args,
        CancellationToken cancellationToken)
    {
        ChatConnection conn = GetConnectedOrThrow(frontendConnectionId);

        using var timeoutCts = new CancellationTokenSource(InvocationTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await conn.Hub.InvokeCoreAsync(method, args, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await HandleInvocationTimeoutAsync(frontendConnectionId, method);
            throw new TimeoutException(
                $"Downstream Chat invocation '{method}' for frontend connection {frontendConnectionId} timed out after {InvocationTimeout.TotalSeconds}s.");
        }
    }

    private ChatConnection GetConnectedOrThrow(string frontendConnectionId)
    {
        if (!_connections.TryGetValue(frontendConnectionId, out var conn))
        {
            throw new InvalidOperationException(
                $"No active chat relay connection for frontend connection {frontendConnectionId}. Connect first.");
        }

        if (conn.Hub.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException(
                $"Chat relay connection for {frontendConnectionId} is in {conn.Hub.State} state, not Connected.");
        }

        return conn;
    }

    private async Task HandleInvocationTimeoutAsync(string frontendConnectionId, string method)
    {
        _logger.LogWarning(
            "Downstream Chat invocation '{Method}' timed out for frontend {ConnectionId}. Tearing down downstream connection.",
            method, frontendConnectionId);

        try
        {
            await _sender.SendAsync(frontendConnectionId, ChatConnectionLostEvent,
                [new { Reason = "downstream_timeout" }]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to notify frontend {ConnectionId} about downstream timeout",
                frontendConnectionId);
        }

        await DisconnectAsync(frontendConnectionId);
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
            // Best-effort cleanup — never throw on dispose.
        }
    }

    /// <summary>Test seam: number of currently tracked downstream connections.</summary>
    internal int ConnectionCount => _connections.Count;
}
