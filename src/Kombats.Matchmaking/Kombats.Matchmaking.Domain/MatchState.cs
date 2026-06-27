namespace Kombats.Matchmaking.Domain;

/// <summary>
/// Match lifecycle states.
/// </summary>
public enum MatchState
{
    /// <summary>Match created, players paired, awaiting battle creation request.</summary>
    Queued = 0,

    /// <summary>CreateBattle command sent to Battle service via outbox.</summary>
    BattleCreateRequested = 1,

    /// <summary>Battle service confirmed battle creation.</summary>
    BattleCreated = 2,

    /// <summary>Battle completed normally.</summary>
    Completed = 3,

    /// <summary>Match timed out (battle creation or battle execution).</summary>
    TimedOut = 4,

    /// <summary>Match cancelled (e.g., admin action).</summary>
    Cancelled = 5
}

