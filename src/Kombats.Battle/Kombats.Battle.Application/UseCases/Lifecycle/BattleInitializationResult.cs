namespace Kombats.Battle.Application.UseCases.Lifecycle;

/// <summary>
/// Result of battle initialization, containing ruleset version and seed used.
/// </summary>
public sealed class BattleInitializationResult
{
    public int RulesetVersion { get; set; }
    public int Seed { get; set; }
    public int PlayerAMaxHp { get; set; }
    public int PlayerBMaxHp { get; set; }
}
