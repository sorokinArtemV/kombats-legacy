namespace Kombats.Matchmaking.Application.Abstractions;

/// <summary>
/// Port for publishing CreateBattle commands via MassTransit transactional outbox.
/// The implementation uses IPublishEndpoint backed by EF Core outbox —
/// the message is written atomically with the DbContext SaveChanges.
/// </summary>
internal interface ICreateBattlePublisher
{
    /// <summary>
    /// Publishes a CreateBattle command with participant snapshots.
    /// Must be called within the same UoW scope — outbox ensures atomicity.
    /// </summary>
    Task PublishAsync(CreateBattleRequest request, CancellationToken ct = default);
}

/// <summary>
/// Application-level request for creating a battle. Maps to Battle.Contracts.CreateBattle.
/// </summary>
internal sealed record CreateBattleRequest(
    Guid BattleId,
    Guid MatchId,
    DateTimeOffset RequestedAt,
    ParticipantSnapshot PlayerA,
    ParticipantSnapshot PlayerB);

/// <summary>
/// Participant snapshot sent with CreateBattle.
/// </summary>
internal sealed record ParticipantSnapshot(
    Guid IdentityId,
    Guid CharacterId,
    string? Name,
    int Level,
    int Strength,
    int Agility,
    int Intuition,
    int Vitality);


