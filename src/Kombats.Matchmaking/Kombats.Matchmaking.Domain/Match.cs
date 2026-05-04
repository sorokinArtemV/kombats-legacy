namespace Kombats.Matchmaking.Domain;

/// <summary>
/// Match aggregate root. Manages the full lifecycle of a paired match
/// from queue pop through battle completion.
///
/// State machine:
///   Queued -> BattleCreateRequested -> BattleCreated -> Completed | TimedOut
///   BattleCreateRequested -> TimedOut (timeout worker)
///   BattleCreated -> TimedOut (timeout worker)
///   Any non-terminal -> Cancelled (admin)
/// </summary>
public sealed class Match
{
    public Guid MatchId { get; private set; }
    public Guid BattleId { get; private set; }
    public Guid PlayerAId { get; private set; }
    public Guid PlayerBId { get; private set; }
    public string Variant { get; private set; } = string.Empty;
    public MatchState State { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private Match() { } // EF Core / rehydration

    /// <summary>
    /// Creates a new match in Queued state.
    /// </summary>
    public static Match Create(
        Guid matchId,
        Guid battleId,
        Guid playerAId,
        Guid playerBId,
        string variant,
        DateTimeOffset now)
    {
        if (matchId == Guid.Empty) throw new ArgumentException("MatchId required.", nameof(matchId));
        if (battleId == Guid.Empty) throw new ArgumentException("BattleId required.", nameof(battleId));
        if (playerAId == Guid.Empty) throw new ArgumentException("PlayerAId required.", nameof(playerAId));
        if (playerBId == Guid.Empty) throw new ArgumentException("PlayerBId required.", nameof(playerBId));
        if (playerAId == playerBId) throw new ArgumentException("PlayerA and PlayerB must be different.", nameof(playerBId));
        if (string.IsNullOrWhiteSpace(variant)) throw new ArgumentException("Variant required.", nameof(variant));

        return new Match
        {
            MatchId = matchId,
            BattleId = battleId,
            PlayerAId = playerAId,
            PlayerBId = playerBId,
            Variant = variant,
            State = MatchState.Queued,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    /// <summary>
    /// Rehydrates a match from persistence. No invariant checks — trusts stored state.
    /// </summary>
    public static Match Rehydrate(
        Guid matchId,
        Guid battleId,
        Guid playerAId,
        Guid playerBId,
        string variant,
        MatchState state,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        return new Match
        {
            MatchId = matchId,
            BattleId = battleId,
            PlayerAId = playerAId,
            PlayerBId = playerBId,
            Variant = variant,
            State = state,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc
        };
    }

    /// <summary>
    /// Transitions Queued -> BattleCreateRequested when outbox message is written.
    /// </summary>
    public void MarkBattleCreateRequested(DateTimeOffset now)
    {
        GuardTransition(MatchState.Queued, MatchState.BattleCreateRequested);
        State = MatchState.BattleCreateRequested;
        UpdatedAtUtc = now;
    }

    /// <summary>
    /// Transitions BattleCreateRequested -> BattleCreated when Battle service confirms.
    /// Returns false if already advanced past BattleCreateRequested (idempotent CAS).
    /// </summary>
    public bool TryMarkBattleCreated(DateTimeOffset now)
    {
        if (State != MatchState.BattleCreateRequested) return false;
        State = MatchState.BattleCreated;
        UpdatedAtUtc = now;
        return true;
    }

    /// <summary>
    /// Transitions BattleCreated -> Completed when battle finishes normally.
    /// Returns false if not in BattleCreated state (idempotent CAS).
    /// </summary>
    public bool TryMarkCompleted(DateTimeOffset now)
    {
        if (State != MatchState.BattleCreated) return false;
        State = MatchState.Completed;
        UpdatedAtUtc = now;
        return true;
    }

    /// <summary>
    /// Transitions BattleCreateRequested|BattleCreated -> TimedOut.
    /// Returns false if already terminal (idempotent CAS).
    /// </summary>
    public bool TryMarkTimedOut(DateTimeOffset now)
    {
        if (State != MatchState.BattleCreateRequested && State != MatchState.BattleCreated) return false;
        State = MatchState.TimedOut;
        UpdatedAtUtc = now;
        return true;
    }

    /// <summary>
    /// Transitions any non-terminal state -> Cancelled.
    /// Returns false if already in a terminal state.
    /// </summary>
    public bool TryCancel(DateTimeOffset now)
    {
        if (IsTerminal) return false;
        State = MatchState.Cancelled;
        UpdatedAtUtc = now;
        return true;
    }

    /// <summary>
    /// Whether the match is in a terminal state (no further transitions possible).
    /// </summary>
    public bool IsTerminal => State is MatchState.Completed or MatchState.TimedOut or MatchState.Cancelled;

    /// <summary>
    /// Whether the match is in an active (non-terminal) state.
    /// </summary>
    public bool IsActive => !IsTerminal;

    /// <summary>
    /// Whether a given player is a participant in this match.
    /// </summary>
    public bool InvolvesPlayer(Guid playerId) => PlayerAId == playerId || PlayerBId == playerId;

    private void GuardTransition(MatchState expected, MatchState target)
    {
        if (State != expected)
            throw new InvalidOperationException(
                $"Cannot transition from {State} to {target}. Expected state: {expected}.");
    }
}





