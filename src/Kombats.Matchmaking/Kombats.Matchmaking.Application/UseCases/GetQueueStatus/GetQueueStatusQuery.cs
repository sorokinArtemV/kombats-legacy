using Kombats.Abstractions;

namespace Kombats.Matchmaking.Application.UseCases.GetQueueStatus;

internal sealed record GetQueueStatusQuery(Guid PlayerId) : IQuery<QueueStatusResult>;
