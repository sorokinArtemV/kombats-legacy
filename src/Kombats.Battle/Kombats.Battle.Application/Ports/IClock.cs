namespace Kombats.Battle.Application.Ports;

/// <summary>
/// Port interface for time abstraction.
/// Application uses this instead of DateTime.UtcNow for testability and deadline calculations.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
