namespace Kombats.Chat.Application.Ports;

internal interface IUserRestriction
{
    /// <summary>
    /// Returns true when the player is allowed to send chat messages.
    /// v1 implementation always returns true; placeholder for future moderation.
    /// </summary>
    Task<bool> CanSendAsync(Guid identityId, CancellationToken ct);
}
