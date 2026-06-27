using Kombats.Abstractions;
using Kombats.Chat.Application.Notifications;
using Kombats.Chat.Application.Ports;

namespace Kombats.Chat.Application.UseCases.DisconnectUser;

/// <summary>
/// Records a disconnection in the presence store. If this is the last remaining
/// connection for the identity (refcount drops to 0), broadcasts <c>PlayerOffline</c>
/// to the global chat group.
/// </summary>
internal sealed class DisconnectUserHandler(
    IPresenceStore presence,
    IChatNotifier notifier)
    : ICommandHandler<DisconnectUserCommand>
{
    public async Task<Result> HandleAsync(DisconnectUserCommand command, CancellationToken cancellationToken)
    {
        bool lastConnection = await presence.DisconnectAsync(command.IdentityId, cancellationToken);

        if (lastConnection)
        {
            await notifier.BroadcastPlayerOfflineAsync(
                new PlayerOfflineEvent(command.IdentityId),
                cancellationToken);
        }

        return Result.Success();
    }
}
