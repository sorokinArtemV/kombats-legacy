using Kombats.Abstractions;

namespace Kombats.Matchmaking.Application.UseCases.JoinQueue;

internal sealed record JoinQueueCommand(Guid PlayerId, string Variant, string ConnectionRef) : ICommand<JoinQueueResult>;
