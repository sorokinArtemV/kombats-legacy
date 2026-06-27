using Kombats.Chat.Application.Ports;

namespace Kombats.Chat.Infrastructure.Services;

/// <summary>
/// v1 no-op user restriction. Always allows. Hook for future moderation (mute/ban).
/// </summary>
internal sealed class UserRestriction : IUserRestriction
{
    public Task<bool> CanSendAsync(Guid identityId, CancellationToken ct) => Task.FromResult(true);
}
