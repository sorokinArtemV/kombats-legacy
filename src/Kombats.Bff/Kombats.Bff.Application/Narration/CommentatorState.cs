namespace Kombats.Bff.Application.Narration;

/// <summary>
/// Per-battle mutable commentator tracking. Tracks which triggers have fired
/// and enforces max-fire limits.
/// </summary>
public sealed class CommentatorState
{
    public bool FirstBloodFired { get; set; }
    public bool DoubleDodgeFired { get; set; }
    public bool DoubleNoActionFired { get; set; }
    public bool NearDeathPlayerAFired { get; set; }
    public bool NearDeathPlayerBFired { get; set; }
    public int BigHitCount { get; set; }
    public bool KnockoutFired { get; set; }
    public bool DrawFired { get; set; }
}
