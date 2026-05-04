using Kombats.Abstractions;

namespace Kombats.Matchmaking.Application.UseCases.Heartbeat;

internal sealed record HeartbeatCommand(Guid PlayerId, string ConnectionRef) : ICommand;
