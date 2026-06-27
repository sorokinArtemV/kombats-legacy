using Kombats.LoadTests.SignalR;

namespace Kombats.LoadTests.VirtualPlayer;

internal enum BattleOutcome
{
    Won,
    Lost,
    Draw,
    Error,
    QueueTimeout,
    BattleTimeout,
}

internal sealed record VirtualPlayerResult(
    string Username,
    Guid IdentityId,
    Guid? BattleId,
    BattleOutcome Outcome,
    string? ErrorMessage,
    int TurnsPlayed,
    TimeSpan AuthDuration,
    TimeSpan OnboardDuration,
    TimeSpan ConnectDuration,
    TimeSpan QueueWait,
    TimeSpan JoinBattleDuration,
    TimeSpan BattleDuration,
    TimeSpan TotalDuration,
    TrackedEventSummary Events);
