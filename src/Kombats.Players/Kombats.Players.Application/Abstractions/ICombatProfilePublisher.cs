using Kombats.Players.Contracts;

namespace Kombats.Players.Application.Abstractions;

/// <summary>
/// Port for publishing combat profile change events.
/// Infrastructure implements this using MassTransit outbox.
/// </summary>
internal interface ICombatProfilePublisher
{
    Task PublishAsync(PlayerCombatProfileChanged profile, CancellationToken ct);
}
