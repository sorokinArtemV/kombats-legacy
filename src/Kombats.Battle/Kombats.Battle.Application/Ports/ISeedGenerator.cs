namespace Kombats.Battle.Application.Ports;

/// <summary>
/// Port interface for generating battle seeds.
/// Application uses this to generate seeds without depending on Infrastructure implementation details.
/// </summary>
public interface ISeedGenerator
{
    /// <summary>
    /// Generates a cryptographically safe random seed for a battle.
    /// </summary>
    int GenerateSeed();
}
