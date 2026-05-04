namespace Kombats.Chat.Application.Ports;

internal interface IDisplayNameResolver
{
    /// <summary>
    /// Resolves a player's display name: cache → Players HTTP → "Unknown".
    /// </summary>
    Task<string> ResolveAsync(Guid identityId, CancellationToken ct);
}
