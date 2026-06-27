using Kombats.Bff.Application.Narration;

namespace Kombats.Bff.Application.Relay;

/// <summary>
/// Per-connection state for a live battle relay. Tracks participant context,
/// commentator state, and current HP for narration generation.
/// </summary>
public sealed class BattleConnectionState
{
    public required Guid BattleId { get; init; }
    public required BattleParticipantSnapshot Participants { get; init; }
    public required CommentatorState Commentator { get; init; }
    public int? PlayerAHp { get; set; }
    public int? PlayerBHp { get; set; }
    public int? PlayerAMaxHp { get; init; }
    public int? PlayerBMaxHp { get; init; }
}
