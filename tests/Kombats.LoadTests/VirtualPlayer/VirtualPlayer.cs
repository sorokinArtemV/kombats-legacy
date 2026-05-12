using System.Diagnostics;
using Kombats.Battle.Realtime.Contracts;
using Kombats.LoadTests.Authentication;
using Kombats.LoadTests.SignalR;
using Kombats.LoadTests.Transport;
using Microsoft.Extensions.Logging;

namespace Kombats.LoadTests.VirtualPlayer;

/// <summary>
/// One bot. Runs auth → onboarding → SignalR connect → join queue →
/// wait for matched → JoinBattle → action loop → BattleEnded → disconnect.
/// </summary>
internal sealed class VirtualPlayer : IAsyncDisposable
{
    private readonly VirtualPlayerOptions _options;
    private readonly KeycloakTokenClient _tokens;
    private readonly ILogger<VirtualPlayer> _logger;
    private readonly Random _rng;

    private BffHttpClient? _bff;
    private BattleHubClient? _hub;
    private HubEventTracker? _events;

    // Battle state, updated from snapshot + BattleStateUpdated events.
    private Guid _myId;
    private int? _myHp;
    private int? _opponentHp;
    private int _currentTurnIndex;
    private TaskCompletionSource<int>? _turnReady;
    private TaskCompletionSource<BattleEndedRealtime>? _battleDone;

    public VirtualPlayer(VirtualPlayerOptions options, KeycloakTokenClient tokens, ILogger<VirtualPlayer> logger)
    {
        _options = options;
        _tokens = tokens;
        _logger = logger;
        _rng = new Random(options.RandomSeed);
    }

    public async Task<VirtualPlayerResult> RunOneBattleAsync(CancellationToken ct)
    {
        using var perBotTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.Load.PerBotTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, perBotTimeout.Token);
        var token = linked.Token;

        var totalSw = Stopwatch.StartNew();
        var stepSw = new Stopwatch();
        TimeSpan authDur = default, onboardDur = default, connectDur = default;
        TimeSpan queueWait = default, joinBattleDur = default, battleDur = default;
        Guid? battleId = null;
        var connectionRef = $"loadtest-{_options.User.Username}-{Guid.NewGuid():N}";
        _events = new HubEventTracker();

        try
        {
            // ---- 1. Token (warm cache once, the BffHttpClient + hub will re-pull from cache)
            stepSw.Restart();
            _ = await _tokens.GetAccessTokenAsync(_options.User, token);
            authDur = stepSw.Elapsed;

            _bff = new BffHttpClient(
                _options.Target.BffBaseUrl,
                ct2 => _tokens.GetAccessTokenAsync(_options.User, ct2),
                _logger);

            // ---- 2. Onboarding (idempotent — second run completes in ~3 round-trips of no-ops)
            stepSw.Restart();
            await EnsureReadyAsync(token);
            onboardDur = stepSw.Elapsed;

            // ---- 3. Connect SignalR
            stepSw.Restart();
            _hub = new BattleHubClient(
                _options.Target.BffBaseUrl,
                _options.Target.BattleHubPath,
                ct2 => _tokens.GetAccessTokenAsync(_options.User, ct2),
                _logger);
            WireHubEvents(_hub, _events);
            await _hub.ConnectAsync(token);
            connectDur = stepSw.Elapsed;

            // ---- 4. Join queue
            stepSw.Restart();
            var joinResult = await _bff.JoinQueueAsync(connectionRef, token);
            if (string.Equals(joinResult.Status, "Matched", StringComparison.OrdinalIgnoreCase) && joinResult.BattleId is { } existingBattle)
            {
                battleId = existingBattle;
            }
            else
            {
                // ---- 5. Poll queue status until paired (or timeout)
                // Run heartbeat alongside the poll loop: the server's presence-ref TTL is
                // 15s (Matchmaking appsettings.json: QueuePresence.PresenceTtlSeconds) and
                // the QueuePresenceSweepWorker kicks stale entries out of mm:queue every
                // SweepIntervalSeconds=20s. Without these pings the bot is evicted at ~t=20s
                // and PollUntilMatchedAsync stays "Searching" forever until QueueTimeout.
                // Matches the SPA's HEARTBEAT_INTERVAL_MS=10s
                // (src/Kombats.Client/src/modules/matchmaking/hooks.ts:20, 259-289).
                using var queueTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.Load.QueueTimeoutSeconds));
                using var queueLinked = CancellationTokenSource.CreateLinkedTokenSource(token, queueTimeout.Token);
                var heartbeatTask = RunHeartbeatLoopAsync(connectionRef, queueLinked.Token);
                try
                {
                    battleId = await PollUntilMatchedAsync(_bff, queueLinked.Token);
                }
                finally
                {
                    queueLinked.Cancel();
                    try { await heartbeatTask.ConfigureAwait(false); }
                    catch { /* heartbeat loop swallows everything; await is just for join */ }
                }
            }
            queueWait = stepSw.Elapsed;

            if (battleId is null)
            {
                return MakeResult(BattleOutcome.QueueTimeout, "Queue timed out before pairing.", null,
                    totalSw, authDur, onboardDur, connectDur, queueWait, joinBattleDur, battleDur);
            }

            // ---- 6. JoinBattle (with the 8-step retry loop)
            stepSw.Restart();
            var snapshot = await _hub.JoinBattleAsync(battleId.Value, token);
            joinBattleDur = stepSw.Elapsed;
            ApplySnapshot(snapshot);

            // ---- 7. Turn loop until BattleEnded
            stepSw.Restart();
            _battleDone = new TaskCompletionSource<BattleEndedRealtime>(TaskCreationOptions.RunContinuationsAsynchronously);
            // The initial TurnOpened may have been broadcast before we joined the group; the snapshot is the source of truth.
            _turnReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            _turnReady.TrySetResult(snapshot.TurnIndex);

            int turnsPlayed = 0;
            BattleEndedRealtime? endEvent = null;
            while (true)
            {
                var nextTurn = _turnReady.Task;
                var battleDoneTask = _battleDone.Task;
                var completed = await Task.WhenAny(nextTurn, battleDoneTask);
                if (completed == battleDoneTask)
                {
                    endEvent = await battleDoneTask;
                    break;
                }

                int turnIndex = await nextTurn;
                _turnReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                _currentTurnIndex = turnIndex;

                var payload = _options.Behavior.PickActionPayload(_rng);
                try
                {
                    await _hub.SubmitTurnActionAsync(battleId.Value, turnIndex, payload, token);
                    turnsPlayed++;
                }
                catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
                {
                    // Battle-ending turn race: BattleEnded handler fires before the completion frame.
                    if (_battleDone.Task.IsCompleted) break;
                    throw;
                }
            }

            battleDur = stepSw.Elapsed;

            var outcome = ResolveOutcome(endEvent);
            return MakeResult(outcome, null, battleId, totalSw, authDur, onboardDur, connectDur, queueWait, joinBattleDur, battleDur, turnsPlayed);
        }
        catch (OperationCanceledException) when (perBotTimeout.IsCancellationRequested)
        {
            return MakeResult(BattleOutcome.BattleTimeout, "Per-bot timeout", battleId,
                totalSw, authDur, onboardDur, connectDur, queueWait, joinBattleDur, battleDur);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VirtualPlayer {Username} failed: {Message}", _options.User.Username, ex.Message);
            return MakeResult(BattleOutcome.Error, ex.Message, battleId,
                totalSw, authDur, onboardDur, connectDur, queueWait, joinBattleDur, battleDur);
        }
        finally
        {
            // Best-effort leave queue (no-op if we already left or never queued)
            try
            {
                if (_bff is not null)
                {
                    using var leaveCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await _bff.LeaveQueueAsync(connectionRef, leaveCts.Token);
                }
            }
            catch
            {
                // Don't let cleanup errors swallow real results.
            }
        }
    }

    private async Task EnsureReadyAsync(CancellationToken ct)
    {
        // 1. onboard (idempotent)
        var onboard = await _bff!.OnboardAsync(ct);
        var state = onboard?.OnboardingState;
        var revision = onboard?.Revision ?? 1;

        // 2. set name + avatar if Draft (matches the SPA's NameSelectionScreen
        // submit: name first, then avatar — see frontend NameSelectionScreen.tsx:27-35).
        if (string.Equals(state, "Draft", StringComparison.OrdinalIgnoreCase))
        {
            // Names must be 3-16 chars (Character.SetNameOnce). loadbot-0001 = 12 chars.
            await _bff.SetNameAsync(_options.User.Username, ct);
            // "ronin" is a non-default avatar (default is "shadow_oni"), so the call
            // actually fires server-side and bumps revision rather than no-opping.
            await _bff.ChangeAvatarAsync(expectedRevision: revision + 1, avatarId: "ronin", ct);
        }

        // 3. allocate stats if not Ready
        if (!string.Equals(state, "Ready", StringComparison.OrdinalIgnoreCase))
        {
            // The character starts with 3 unspent points (Character.CreateDraft).
            // Allocate them deterministically and predictably for every bot.
            // Revision is +2 after name + avatar (or +0 if already Named).
            int expectedRev = string.Equals(state, "Named", StringComparison.OrdinalIgnoreCase)
                ? revision
                : revision + 2;
            try
            {
                await _bff.AllocateStatsAsync(strength: 1, agility: 1, intuition: 1, vitality: 0, expectedRevision: expectedRev, ct);
            }
            catch
            {
                // Revision drift between onboard and allocate is the usual cause; fetch fresh state and retry once.
                var fresh = await _bff.GetGameStateAsync(ct);
                if (fresh?.Character is { } character && !string.Equals(character.OnboardingState, "Ready", StringComparison.OrdinalIgnoreCase))
                {
                    await _bff.AllocateStatsAsync(strength: 1, agility: 1, intuition: 1, vitality: 0, expectedRevision: character.Revision, ct);
                }
            }
        }

        // 4. The matchmaking-side projection of PlayerCombatProfileChanged is
        // asynchronous (RabbitMQ + MassTransit). BffHttpClient.JoinQueueAsync
        // already retries on the transient HTTP 400 "Queue.NotReady" /
        // "Queue.NoCombatProfile" / "Invalid request to Matchmaking" bodies
        // until the projection lands — no explicit wait needed here.
    }

    private async Task RunHeartbeatLoopAsync(string connectionRef, CancellationToken ct)
    {
        // 10s cadence matches the SPA so the per-tab/per-bot connection ref TTL
        // (15s) is comfortably refreshed before the sweep worker can evict.
        var interval = TimeSpan.FromSeconds(10);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            try
            {
                await _bff!.HeartbeatAsync(connectionRef, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // Single missed heartbeat is recoverable as long as the next one
                // succeeds before the 15s TTL elapses; if heartbeats persistently
                // fail the bot will simply hit QueueTimeout, which is the correct
                // outcome to record.
                _logger.LogWarning(ex, "heartbeat failed for {Username}: {Message}", _options.User.Username, ex.Message);
            }
        }
    }

    private async Task<Guid?> PollUntilMatchedAsync(BffHttpClient bff, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var status = await bff.GetQueueStatusAsync(ct);
            if (string.Equals(status.Status, "Matched", StringComparison.OrdinalIgnoreCase) && status.BattleId is { } id)
            {
                return id;
            }
            try
            {
                await Task.Delay(500, ct);
            }
            catch (TaskCanceledException)
            {
                return null;
            }
        }
        return null;
    }

    private void ApplySnapshot(BattleSnapshotRealtime s)
    {
        // We need to know which side we are. The token's `sub` claim is our identity.
        // Since the snapshot has both player IDs and only one matches our `sub`, we use that.
        _myId = Guid.Parse(_options.User.Sub);
        if (_myId == s.PlayerAId)
        {
            _myHp = s.PlayerAHp;
            _opponentHp = s.PlayerBHp;
        }
        else
        {
            _myHp = s.PlayerBHp;
            _opponentHp = s.PlayerAHp;
        }
        _currentTurnIndex = s.TurnIndex;
    }

    private void WireHubEvents(BattleHubClient hub, HubEventTracker tracker)
    {
        hub.BattleReady += tracker.OnBattleReady;
        hub.TurnOpened += t => { tracker.OnTurnOpened(t); _turnReady?.TrySetResult(t.TurnIndex); };
        hub.TurnResolved += tracker.OnTurnResolved;
        hub.PlayerDamaged += tracker.OnPlayerDamaged;
        hub.BattleStateUpdated += s =>
        {
            tracker.OnBattleStateUpdated(s);
            // Track HP. The contract carries both sides; pick mine.
            if (_myId == s.PlayerAId)
            {
                _myHp = s.PlayerAHp;
                _opponentHp = s.PlayerBHp;
            }
            else
            {
                _myHp = s.PlayerBHp;
                _opponentHp = s.PlayerAHp;
            }
        };
        hub.BattleEnded += end => { tracker.OnBattleEnded(end); _battleDone?.TrySetResult(end); };
        hub.BattleFeedUpdated += tracker.OnBattleFeedUpdated;
        hub.BattleConnectionLost += tracker.OnBattleConnectionLost;
    }

    private BattleOutcome ResolveOutcome(BattleEndedRealtime? end)
    {
        if (end is null) return BattleOutcome.Error;
        if (end.WinnerPlayerId is null) return BattleOutcome.Draw;
        return end.WinnerPlayerId == _myId ? BattleOutcome.Won : BattleOutcome.Lost;
    }

    private VirtualPlayerResult MakeResult(
        BattleOutcome outcome, string? error, Guid? battleId,
        Stopwatch totalSw, TimeSpan authDur, TimeSpan onboardDur, TimeSpan connectDur,
        TimeSpan queueWait, TimeSpan joinDur, TimeSpan battleDur, int turnsPlayed = 0)
    {
        return new VirtualPlayerResult(
            Username: _options.User.Username,
            IdentityId: Guid.TryParse(_options.User.Sub, out var s) ? s : Guid.Empty,
            BattleId: battleId,
            Outcome: outcome,
            ErrorMessage: error,
            TurnsPlayed: turnsPlayed,
            AuthDuration: authDur,
            OnboardDuration: onboardDur,
            ConnectDuration: connectDur,
            QueueWait: queueWait,
            JoinBattleDuration: joinDur,
            BattleDuration: battleDur,
            TotalDuration: totalSw.Elapsed,
            Events: _events?.Snapshot() ?? new TrackedEventSummary(0, 0, 0, 0, 0, 0, -1, -1));
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
        _bff?.Dispose();
    }
}
