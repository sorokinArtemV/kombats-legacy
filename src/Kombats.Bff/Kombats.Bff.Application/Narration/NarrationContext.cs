namespace Kombats.Bff.Application.Narration;

/// <summary>
/// Values available for template placeholder interpolation.
/// </summary>
public sealed class NarrationContext
{
    public string AttackerName { get; init; } = string.Empty;
    public string DefenderName { get; init; } = string.Empty;
    public string? AttackZone { get; init; }
    public int Damage { get; init; }
    public string? BlockZone { get; init; }
    public string PlayerAName { get; init; } = string.Empty;
    public string PlayerBName { get; init; } = string.Empty;
    public string? WinnerName { get; init; }
    public string? LoserName { get; init; }
    public int? RemainingHp { get; init; }
    public int? MaxHp { get; init; }

    public Dictionary<string, string> ToPlaceholders()
    {
        var placeholders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["attackerName"] = AttackerName,
            ["defenderName"] = DefenderName,
            ["damage"] = Damage.ToString(),
            ["playerAName"] = PlayerAName,
            ["playerBName"] = PlayerBName
        };

        if (AttackZone is not null) placeholders["attackZone"] = AttackZone;
        if (BlockZone is not null) placeholders["blockZone"] = BlockZone;
        if (WinnerName is not null) placeholders["winnerName"] = WinnerName;
        if (LoserName is not null) placeholders["loserName"] = LoserName;
        if (RemainingHp.HasValue) placeholders["remainingHp"] = RemainingHp.Value.ToString();
        if (MaxHp.HasValue) placeholders["maxHp"] = MaxHp.Value.ToString();

        return placeholders;
    }
}
