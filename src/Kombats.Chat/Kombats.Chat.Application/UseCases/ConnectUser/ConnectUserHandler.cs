using Kombats.Abstractions;
using Kombats.Chat.Application.Notifications;
using Kombats.Chat.Application.Ports;

namespace Kombats.Chat.Application.UseCases.ConnectUser;

/// <summary>
/// Records a connection in the presence store. If this is the first connection
/// for the identity (multi-tab refcount == 1), broadcasts <c>PlayerOnline</c> to
/// the global chat group. Does NOT add the connection to any SignalR group —
/// presence is connection-based and group membership is established separately
/// by <c>JoinGlobalChat</c>.
/// </summary>
internal sealed class ConnectUserHandler(
    IPresenceStore presence,
    IDisplayNameResolver displayNames,
    IChatNotifier notifier)
    : ICommandHandler<ConnectUserCommand>
{
    public async Task<Result> HandleAsync(ConnectUserCommand command, CancellationToken cancellationToken)
    {
        string displayName = await displayNames.ResolveAsync(command.IdentityId, cancellationToken);

        bool firstConnection = await presence.ConnectAsync(command.IdentityId, displayName, cancellationToken);

        if (firstConnection)
        {
            await notifier.BroadcastPlayerOnlineAsync(
                new PlayerOnlineEvent(command.IdentityId, displayName),
                cancellationToken);
        }

        return Result.Success();
    }
}
