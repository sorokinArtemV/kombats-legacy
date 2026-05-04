using Kombats.Abstractions;
using Kombats.Matchmaking.Application.Abstractions;

namespace Kombats.Matchmaking.Application.UseCases.Heartbeat;

/// <summary>
/// Refreshes queue-presence TTL for the given (identity, connectionRef). The
/// presence store treats this idempotently — if no record exists yet (e.g.
/// after a server restart) it creates one as if RegisterAsync had been called.
/// Caller must verify auth before issuing the command.
/// </summary>
internal sealed class HeartbeatHandler : ICommandHandler<HeartbeatCommand>
{
    private readonly IQueuePresenceStore _presenceStore;

    public HeartbeatHandler(IQueuePresenceStore presenceStore)
    {
        _presenceStore = presenceStore;
    }

    public async Task<Result> HandleAsync(HeartbeatCommand cmd, CancellationToken ct)
    {
        await _presenceStore.RefreshAsync(cmd.PlayerId, cmd.ConnectionRef, ct);
        return Result.Success();
    }
}
