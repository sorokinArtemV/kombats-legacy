using System.Text.Json;
using System.Text.Json.Serialization;
using Kombats.Battle.Realtime.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kombats.LoadTests.SignalR;

/// <summary>
/// Wraps a SignalR HubConnection to the BFF /battlehub endpoint. Mirrors
/// the frontend client at src/Kombats.Client/src/transport/signalr/battle-hub.ts:94-100
/// — the 8-step JoinBattle retry loop included.
/// </summary>
internal sealed class BattleHubClient : IAsyncDisposable
{
    // Mirrors src/Kombats.Client/src/transport/signalr/battle-hub.ts:27
    private static readonly int[] JoinBattleRetryDelaysMs = { 250, 500, 750, 1000, 1500, 2000, 2000 };
    // Same regex shape as battle-hub.ts:32
    private const string TransientBattleNotFoundFragment = "not found";

    private readonly string _hubUrl;
    private readonly Func<CancellationToken, Task<string>> _tokenFactory;
    private readonly ILogger _logger;
    private HubConnection? _connection;

    public event Action<BattleReadyRealtime>? BattleReady;
    public event Action<TurnOpenedRealtime>? TurnOpened;
    public event Action<PlayerDamagedRealtime>? PlayerDamaged;
    public event Action<TurnResolvedRealtime>? TurnResolved;
    public event Action<BattleStateUpdatedRealtime>? BattleStateUpdated;
    public event Action<BattleEndedRealtime>? BattleEnded;
    public event Action<JsonElement>? BattleFeedUpdated;
    public event Action? BattleConnectionLost;

    public BattleHubClient(string bffBaseUrl, string hubPath, Func<CancellationToken, Task<string>> tokenFactory, ILogger logger)
    {
        _hubUrl = $"{bffBaseUrl.TrimEnd('/')}{hubPath}";
        _tokenFactory = tokenFactory;
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        if (_connection is not null)
        {
            throw new InvalidOperationException("ConnectAsync called twice.");
        }

        var builder = new HubConnectionBuilder()
            .WithUrl(_hubUrl, options =>
            {
                options.AccessTokenProvider = async () => await _tokenFactory(ct);
            })
            .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning));

        // The Battle service serializes enums (Phase, Reason, Outcome) as strings.
        // Without the converter, typed On<T> handlers silently drop events whose
        // payload contains an enum — same fix as src/Kombats.Bff/.../BattleHubRelay.cs:77-80.
        builder.Services.Configure<Microsoft.AspNetCore.SignalR.JsonHubProtocolOptions>(opt =>
        {
            opt.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        var conn = builder.Build();
        RegisterHandlers(conn);
        await conn.StartAsync(ct);
        _connection = conn;
    }

    public async Task<BattleSnapshotRealtime> JoinBattleAsync(Guid battleId, CancellationToken ct)
    {
        if (_connection is null || _connection.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("BattleHubClient: not connected.");
        }

        Exception? lastErr = null;
        for (int attempt = 0; attempt <= JoinBattleRetryDelaysMs.Length; attempt++)
        {
            try
            {
                return await _connection.InvokeAsync<BattleSnapshotRealtime>("JoinBattle", battleId, ct);
            }
            catch (Exception ex)
            {
                lastErr = ex;
                if (!IsTransientBattleNotFound(ex))
                {
                    throw;
                }
                if (attempt == JoinBattleRetryDelaysMs.Length)
                {
                    break;
                }
                _logger.LogDebug("JoinBattle transient (attempt {Attempt}): {Message}", attempt + 1, ex.Message);
                await Task.Delay(JoinBattleRetryDelaysMs[attempt], ct);
            }
        }
        throw lastErr ?? new InvalidOperationException("JoinBattle exhausted retries.");
    }

    public Task SubmitTurnActionAsync(Guid battleId, int turnIndex, string actionPayload, CancellationToken ct)
    {
        if (_connection is null || _connection.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("BattleHubClient: not connected.");
        }
        return _connection.InvokeAsync("SubmitTurnAction", battleId, turnIndex, actionPayload, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is null) return;
        try
        {
            if (_connection.State != HubConnectionState.Disconnected)
            {
                await _connection.StopAsync();
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
        await _connection.DisposeAsync();
        _connection = null;
    }

    private void RegisterHandlers(HubConnection conn)
    {
        conn.On<BattleReadyRealtime>("BattleReady", data => BattleReady?.Invoke(data));
        conn.On<TurnOpenedRealtime>("TurnOpened", data => TurnOpened?.Invoke(data));
        conn.On<PlayerDamagedRealtime>("PlayerDamaged", data => PlayerDamaged?.Invoke(data));
        conn.On<TurnResolvedRealtime>("TurnResolved", data => TurnResolved?.Invoke(data));
        conn.On<BattleStateUpdatedRealtime>("BattleStateUpdated", data => BattleStateUpdated?.Invoke(data));
        conn.On<BattleEndedRealtime>("BattleEnded", data => BattleEnded?.Invoke(data));
        // BattleFeedUpdated is a BFF-synthesized event with a feed-entry array; we
        // record receipt without deserializing the shape into a domain type.
        conn.On<JsonElement>("BattleFeedUpdated", data => BattleFeedUpdated?.Invoke(data));
        conn.On<object>("BattleConnectionLost", _ => BattleConnectionLost?.Invoke());
    }

    private static bool IsTransientBattleNotFound(Exception ex)
    {
        var msg = ex.Message ?? "";
        return msg.Contains(TransientBattleNotFoundFragment, StringComparison.OrdinalIgnoreCase)
               && msg.Contains("Battle", StringComparison.OrdinalIgnoreCase);
    }
}
