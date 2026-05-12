using System.Text.Json;

namespace Kombats.LoadTests.VirtualPlayer;

/// <summary>
/// Mirrors src/Kombats.Battle/Kombats.Battle.Domain/Rules/BattleZone.cs:7-14.
/// Names are case-insensitive on the server side, but we use the canonical
/// PascalCase form for clarity.
/// </summary>
internal static class BattleZones
{
    public static readonly string[] All = { "Head", "Chest", "Belly", "Waist", "Legs" };

    // Adjacent block pairs on the 5-zone ring.
    // See src/Kombats.Battle/Kombats.Battle.Domain/Rules/BattleZone.cs:24-32.
    public static readonly (string Primary, string Secondary)[] ValidBlockPairs =
    {
        ("Head", "Chest"),
        ("Chest", "Belly"),
        ("Belly", "Waist"),
        ("Waist", "Legs"),
        ("Legs", "Head"),
    };
}

internal interface IPlayerBehavior
{
    string PickActionPayload(Random rng);
}

/// <summary>
/// Uniform-random pick over (5 zones) × (5 adjacent block pairs) = 25 options.
/// Deterministic when given a seeded Random.
/// </summary>
internal sealed class RandomPlayerBehavior : IPlayerBehavior
{
    public string PickActionPayload(Random rng)
    {
        var attackZone = BattleZones.All[rng.Next(BattleZones.All.Length)];
        var (primary, secondary) = BattleZones.ValidBlockPairs[rng.Next(BattleZones.ValidBlockPairs.Length)];
        return JsonSerializer.Serialize(new
        {
            attackZone,
            blockZonePrimary = primary,
            blockZoneSecondary = secondary,
        });
    }
}
