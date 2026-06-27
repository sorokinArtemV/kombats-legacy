using Kombats.Abstractions;

namespace Kombats.Matchmaking.Application.UseCases.LeaveQueue;

internal sealed record LeaveQueueCommand(Guid PlayerId, string Variant, string ConnectionRef) : ICommand<LeaveQueueResult>;
