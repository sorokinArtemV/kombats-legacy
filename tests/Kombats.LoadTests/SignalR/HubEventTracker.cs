using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Kombats.Battle.Realtime.Contracts;

namespace Kombats.LoadTests.SignalR;

/// <summary>
/// Records counts and timing for the events a bot receives during one battle.
/// Per-instance; created once per VirtualPlayer.
/// </summary>
internal sealed class HubEventTracker
{
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private long _firstBattleReadyMs = -1;
    private long _firstBattleEndedMs = -1;
    private int _turnOpenedCount;
    private int _turnResolvedCount;
    private int _playerDamagedCount;
    private int _stateUpdatedCount;
    private int _battleFeedUpdatedCount;
    private int _battleConnectionLostCount;
    private readonly ConcurrentBag<long> _feedLatencies = new();

    public void OnBattleReady(BattleReadyRealtime _) =>
        Interlocked.CompareExchange(ref _firstBattleReadyMs, _sw.ElapsedMilliseconds, -1);

    public void OnTurnOpened(TurnOpenedRealtime _) => Interlocked.Increment(ref _turnOpenedCount);
    public void OnTurnResolved(TurnResolvedRealtime _) => Interlocked.Increment(ref _turnResolvedCount);
    public void OnPlayerDamaged(PlayerDamagedRealtime _) => Interlocked.Increment(ref _playerDamagedCount);
    public void OnBattleStateUpdated(BattleStateUpdatedRealtime _) => Interlocked.Increment(ref _stateUpdatedCount);

    public void OnBattleEnded(BattleEndedRealtime _) =>
        Interlocked.CompareExchange(ref _firstBattleEndedMs, _sw.ElapsedMilliseconds, -1);

    public void OnBattleFeedUpdated(JsonElement _)
    {
        Interlocked.Increment(ref _battleFeedUpdatedCount);
        _feedLatencies.Add(_sw.ElapsedMilliseconds);
    }

    public void OnBattleConnectionLost() => Interlocked.Increment(ref _battleConnectionLostCount);

    public TrackedEventSummary Snapshot() => new(
        TurnOpenedCount: _turnOpenedCount,
        TurnResolvedCount: _turnResolvedCount,
        PlayerDamagedCount: _playerDamagedCount,
        BattleStateUpdatedCount: _stateUpdatedCount,
        BattleFeedUpdatedCount: _battleFeedUpdatedCount,
        BattleConnectionLostCount: _battleConnectionLostCount,
        FirstBattleReadyMs: _firstBattleReadyMs,
        FirstBattleEndedMs: _firstBattleEndedMs);
}

internal sealed record TrackedEventSummary(
    int TurnOpenedCount,
    int TurnResolvedCount,
    int PlayerDamagedCount,
    int BattleStateUpdatedCount,
    int BattleFeedUpdatedCount,
    int BattleConnectionLostCount,
    long FirstBattleReadyMs,
    long FirstBattleEndedMs);
