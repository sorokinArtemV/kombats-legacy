namespace Kombats.Bff.Application.Narration;

/// <summary>
/// Participant context for narration. Provides name resolution for player GUIDs.
/// </summary>
public sealed class BattleParticipantSnapshot
{
    public Guid PlayerAId { get; }
    public Guid PlayerBId { get; }
    public string? PlayerAName { get; }
    public string? PlayerBName { get; }

    public BattleParticipantSnapshot(Guid playerAId, Guid playerBId, string? playerAName, string? playerBName)
    {
        PlayerAId = playerAId;
        PlayerBId = playerBId;
        PlayerAName = playerAName;
        PlayerBName = playerBName;
    }

    public string ResolveName(Guid playerId)
    {
        if (playerId == PlayerAId)
            return PlayerAName ?? "Player A";
        if (playerId == PlayerBId)
            return PlayerBName ?? "Player B";
        return "Unknown";
    }

    public Guid GetOpponentId(Guid playerId)
    {
        return playerId == PlayerAId ? PlayerBId : PlayerAId;
    }
}
