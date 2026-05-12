using System.Text.Json;
using System.Text.Json.Serialization;
using Kombats.LoadTests.VirtualPlayer;

namespace Kombats.LoadTests.Reporting;

/// <summary>
/// Appends one JSON line per VirtualPlayer iteration to a file under the
/// reports directory. Lets us compute per-phase p50/p95/p99 with jq after the
/// run, including for failed iterations (QueueTimeout, Error) which the
/// NBomber status-code rollup squashes into a single message string.
/// </summary>
internal sealed class IterationRecorder : IAsyncDisposable
{
    private readonly StreamWriter _writer;
    private readonly Lock _gate = new();

    public string FilePath { get; }

    public IterationRecorder(string filePath)
    {
        FilePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream) { AutoFlush = true };
    }

    public void Record(VirtualPlayer.VirtualPlayerResult r)
    {
        var record = new IterationRecord(
            Ts: DateTimeOffset.Now.ToString("O"),
            Username: r.Username,
            BattleId: r.BattleId?.ToString(),
            Outcome: r.Outcome.ToString(),
            Error: r.ErrorMessage,
            TurnsPlayed: r.TurnsPlayed,
            AuthMs: r.AuthDuration.TotalMilliseconds,
            OnboardMs: r.OnboardDuration.TotalMilliseconds,
            ConnectMs: r.ConnectDuration.TotalMilliseconds,
            QueueWaitMs: r.QueueWait.TotalMilliseconds,
            JoinBattleMs: r.JoinBattleDuration.TotalMilliseconds,
            BattleMs: r.BattleDuration.TotalMilliseconds,
            TotalMs: r.TotalDuration.TotalMilliseconds);
        var line = JsonSerializer.Serialize(record, JsonOpts);
        lock (_gate)
        {
            _writer.WriteLine(line);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.FlushAsync();
        await _writer.DisposeAsync();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record IterationRecord(
        [property: JsonPropertyName("ts")] string Ts,
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("battle_id")] string? BattleId,
        [property: JsonPropertyName("outcome")] string Outcome,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("turns_played")] int TurnsPlayed,
        [property: JsonPropertyName("auth_ms")] double AuthMs,
        [property: JsonPropertyName("onboard_ms")] double OnboardMs,
        [property: JsonPropertyName("connect_ms")] double ConnectMs,
        [property: JsonPropertyName("queue_wait_ms")] double QueueWaitMs,
        [property: JsonPropertyName("join_battle_ms")] double JoinBattleMs,
        [property: JsonPropertyName("battle_ms")] double BattleMs,
        [property: JsonPropertyName("total_ms")] double TotalMs);
}
