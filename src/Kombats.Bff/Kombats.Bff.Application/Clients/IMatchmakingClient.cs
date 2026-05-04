using Kombats.Bff.Application.Models.Internal;

namespace Kombats.Bff.Application.Clients;

public interface IMatchmakingClient
{
    Task<InternalQueueStatusResponse> JoinQueueAsync(string? connectionRef = null, CancellationToken cancellationToken = default);
    Task<InternalLeaveQueueResponse> LeaveQueueAsync(string? connectionRef = null, CancellationToken cancellationToken = default);
    Task<InternalQueueStatusResponse?> GetQueueStatusAsync(CancellationToken cancellationToken = default);
    Task HeartbeatAsync(string connectionRef, CancellationToken cancellationToken = default);
}
