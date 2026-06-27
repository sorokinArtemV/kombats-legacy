namespace Kombats.Battle.Contracts.Battle;

/// <summary>
/// Combat snapshot for a battle participant, carried in the CreateBattle command.
/// Matchmaking builds this from its local player combat profile projection.
/// Battle uses these values directly for combat initialization — no local profile lookup needed.
/// </summary>
public record BattleParticipantSnapshot
{
    public Guid IdentityId { get; init; }
    public Guid CharacterId { get; init; }
    public string? Name { get; init; }
    public int Level { get; init; }
    public int Strength { get; init; }
    public int Agility { get; init; }
    public int Intuition { get; init; }
    public int Vitality { get; init; }
}
