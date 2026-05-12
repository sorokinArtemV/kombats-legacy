using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Kombats.LoadTests.Transport;

/// <summary>
/// Thin HTTP wrapper around the BFF endpoints the bot lifecycle needs:
/// onboarding, character setup, queue join/status/leave. One instance per
/// virtual player (so the bearer token is bound to one identity).
/// </summary>
internal sealed class BffHttpClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<string>> _tokenFactory;
    private readonly ILogger _logger;

    public BffHttpClient(string bffBaseUrl, Func<CancellationToken, Task<string>> tokenFactory, ILogger logger)
    {
        _http = new HttpClient { BaseAddress = new Uri(bffBaseUrl) };
        _tokenFactory = tokenFactory;
        _logger = logger;
    }

    public async Task<OnboardResponse?> OnboardAsync(CancellationToken ct)
    {
        await EnsureAuthAsync(ct);
        using var resp = await _http.PostAsync("/api/v1/game/onboard", content: null, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"onboard: HTTP {(int)resp.StatusCode}. Body: {body}");
        }
        return await resp.Content.ReadFromJsonAsync<OnboardResponse>(JsonOpts, ct);
    }

    public async Task SetNameAsync(string name, CancellationToken ct)
    {
        await EnsureAuthAsync(ct);
        var payload = new { name };
        using var resp = await _http.PostAsJsonAsync("/api/v1/character/name", payload, JsonOpts, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Already named — treat as success; this is the idempotent retry path.
            return;
        }
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"set-name: HTTP {(int)resp.StatusCode}. Body: {body}");
        }
    }

    public async Task<AllocateStatsResponse?> AllocateStatsAsync(
        int strength, int agility, int intuition, int vitality, int expectedRevision, CancellationToken ct)
    {
        await EnsureAuthAsync(ct);
        var payload = new { strength, agility, intuition, vitality, expectedRevision };
        using var resp = await _http.PostAsJsonAsync("/api/v1/character/stats", payload, JsonOpts, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Stats already allocated — idempotent retry path.
            return null;
        }
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"allocate-stats: HTTP {(int)resp.StatusCode}. Body: {body}");
        }
        return await resp.Content.ReadFromJsonAsync<AllocateStatsResponse>(JsonOpts, ct);
    }

    public async Task<GameStateResponse?> GetGameStateAsync(CancellationToken ct)
    {
        await EnsureAuthAsync(ct);
        using var resp = await _http.GetAsync("/api/v1/game/state", ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"game-state: HTTP {(int)resp.StatusCode}. Body: {body}");
        }
        return await resp.Content.ReadFromJsonAsync<GameStateResponse>(JsonOpts, ct);
    }

    public async Task<QueueStatusResponse> JoinQueueAsync(string connectionRef, CancellationToken ct)
    {
        // The matchmaking-side projection of the player's combat profile is
        // updated asynchronously via the PlayerCombatProfileChanged event. At
        // higher concurrency, a freshly-onboarded bot's onboard → join-queue
        // round-trip can outrun that propagation, and the queue-join returns
        // HTTP 400 with Queue.NotReady or Queue.NoCombatProfile. Both are
        // transient. Retry with exponential backoff up to ~8s total.
        int[] delaysMs = { 250, 500, 750, 1000, 1500, 2000, 2000 };
        for (int attempt = 0; attempt <= delaysMs.Length; attempt++)
        {
            await EnsureAuthAsync(ct);
            var payload = new { connectionRef };
            using var resp = await _http.PostAsJsonAsync("/api/v1/queue/join", payload, JsonOpts, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                return await resp.Content.ReadFromJsonAsync<QueueStatusResponse>(JsonOpts, ct)
                       ?? throw new InvalidOperationException("queue-join 409 body was null");
            }
            if (resp.IsSuccessStatusCode)
            {
                return await resp.Content.ReadFromJsonAsync<QueueStatusResponse>(JsonOpts, ct)
                       ?? throw new InvalidOperationException("queue-join body was null");
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            bool isTransient = resp.StatusCode == System.Net.HttpStatusCode.BadRequest
                               && (body.Contains("Queue.NotReady", StringComparison.OrdinalIgnoreCase)
                                   || body.Contains("Queue.NoCombatProfile", StringComparison.OrdinalIgnoreCase)
                                   || body.Contains("Invalid request to Matchmaking", StringComparison.OrdinalIgnoreCase));
            if (!isTransient || attempt == delaysMs.Length)
            {
                throw new InvalidOperationException($"queue-join: HTTP {(int)resp.StatusCode}. Body: {body}");
            }
            _logger.LogDebug("queue-join transient (attempt {Attempt}); waiting {Delay}ms", attempt + 1, delaysMs[attempt]);
            await Task.Delay(delaysMs[attempt], ct);
        }

        throw new InvalidOperationException("queue-join: unreachable");
    }

    public async Task<QueueStatusResponse> GetQueueStatusAsync(CancellationToken ct)
    {
        await EnsureAuthAsync(ct);
        using var resp = await _http.GetAsync("/api/v1/queue/status", ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"queue-status: HTTP {(int)resp.StatusCode}. Body: {body}");
        }
        return await resp.Content.ReadFromJsonAsync<QueueStatusResponse>(JsonOpts, ct)
               ?? throw new InvalidOperationException("queue-status body was null");
    }

    public async Task ChangeAvatarAsync(int expectedRevision, string avatarId, CancellationToken ct)
    {
        await EnsureAuthAsync(ct);
        var payload = new { expectedRevision, avatarId };
        using var resp = await _http.PostAsJsonAsync("/api/v1/character/avatar", payload, JsonOpts, ct);
        if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"change-avatar: HTTP {(int)resp.StatusCode}. Body: {body}");
        }
    }

    public async Task HeartbeatAsync(string connectionRef, CancellationToken ct)
    {
        // Mirrors src/Kombats.Client/src/transport/http/endpoints/queue.ts:20-22.
        // The SPA pings this every 10s while searching to refresh the 15s
        // presence-ref TTL in Redis (mm:queue:presence:refs:{identityId}).
        // Failures are best-effort — the sweep worker is the safety net.
        await EnsureAuthAsync(ct);
        var payload = new { connectionRef };
        using var resp = await _http.PostAsJsonAsync("/api/v1/queue/heartbeat", payload, JsonOpts, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("queue-heartbeat: HTTP {Code}. Body: {Body}", (int)resp.StatusCode, body);
        }
    }

    public async Task LeaveQueueAsync(string connectionRef, CancellationToken ct)
    {
        await EnsureAuthAsync(ct);
        var payload = new { connectionRef };
        using var resp = await _http.PostAsJsonAsync("/api/v1/queue/leave", payload, JsonOpts, ct);
        // 200 and 404 are both fine — 404 means we weren't queued (e.g. battle started already).
        if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("queue-leave: HTTP {Code}. Body: {Body}", (int)resp.StatusCode, body);
        }
    }

    private async Task EnsureAuthAsync(CancellationToken ct)
    {
        var token = await _tokenFactory(ct);
        _http.DefaultRequestHeaders.Authorization = new("Bearer", token);
    }

    public void Dispose() => _http.Dispose();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed record OnboardResponse(
    Guid CharacterId,
    string OnboardingState,
    string? Name,
    int Strength,
    int Agility,
    int Intuition,
    int Vitality,
    int UnspentPoints,
    int Revision,
    long TotalXp,
    int Level,
    string AvatarId);

internal sealed record AllocateStatsResponse(
    int Strength, int Agility, int Intuition, int Vitality, int UnspentPoints, int Revision);

internal sealed record CharacterResponse(
    Guid CharacterId,
    string OnboardingState,
    string? Name,
    int Strength,
    int Agility,
    int Intuition,
    int Vitality,
    int UnspentPoints,
    int Revision,
    long TotalXp,
    int Level,
    string AvatarId);

internal sealed record GameStateResponse(
    CharacterResponse? Character,
    QueueStatusResponse? QueueStatus,
    bool IsCharacterCreated,
    IReadOnlyList<string>? DegradedServices);

internal sealed record QueueStatusResponse(
    string Status,
    Guid? MatchId,
    Guid? BattleId,
    string? MatchState);
