using Kombats.Battle.Domain.Rules;

namespace Kombats.Battle.Domain.Model;

/// <summary>
/// Domain representation of battle state.
/// Immutable after construction — new state is created by the engine for each turn resolution.
/// Independent of infrastructure (Redis, JSON serialization, etc.).
/// </summary>
public sealed class BattleDomainState
{
    public Guid BattleId { get; init; }
    public Guid MatchId { get; init; }
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
    public Ruleset Ruleset { get; init; }
    public BattlePhase Phase { get; init; }
    public int TurnIndex { get; init; }
    public int NoActionStreakBoth { get; init; }
    public int LastResolvedTurnIndex { get; init; }
    public PlayerState PlayerA { get; init; }
    public PlayerState PlayerB { get; init; }

    public BattleDomainState(
        Guid battleId,
        Guid matchId,
        Guid playerAId,
        Guid playerBId,
        Ruleset ruleset,
        BattlePhase phase,
        int turnIndex,
        int noActionStreakBoth,
        int lastResolvedTurnIndex,
        PlayerState playerA,
        PlayerState playerB)
    {
        BattleId = battleId;
        MatchId = matchId;
        PlayerAId = playerAId;
        PlayerBId = playerBId;
        Ruleset = ruleset;
        Phase = phase;
        TurnIndex = turnIndex;
        NoActionStreakBoth = noActionStreakBoth;
        LastResolvedTurnIndex = lastResolvedTurnIndex;
        PlayerA = playerA;
        PlayerB = playerB;
    }
}
