using Kombats.Battle.Domain.Rules;

namespace Kombats.Battle.Application.Models;

/// <summary>
/// Ruleset configuration without seed (seed is generated per battle).
/// </summary>
public record RulesetWithoutSeed(
    int Version,
    int TurnSeconds,
    int NoActionLimit,
    CombatBalance Balance);
