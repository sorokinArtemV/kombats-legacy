using Kombats.Players.Application.Abstractions;
using Kombats.Players.Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Kombats.Players.Infrastructure.Messaging;

/// <summary>
/// Publishes combat profile change events via MassTransit.
/// When MassTransit outbox is configured (AD-01), IPublishEndpoint.Publish() writes to outbox
/// tables in the DbContext rather than directly to RabbitMQ, ensuring atomicity with domain changes.
/// Callers must invoke PublishAsync before SaveChanges for correct outbox semantics.
/// </summary>
internal sealed class MassTransitCombatProfilePublisher : ICombatProfilePublisher
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<MassTransitCombatProfilePublisher> _logger;

    public MassTransitCombatProfilePublisher(
        IPublishEndpoint publishEndpoint,
        ILogger<MassTransitCombatProfilePublisher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task PublishAsync(PlayerCombatProfileChanged profile, CancellationToken ct)
    {
        await _publishEndpoint.Publish(profile, ct);

        _logger.LogInformation(
            "Queued PlayerCombatProfileChanged on outbox: IdentityId={IdentityId}, CharacterId={CharacterId}, Revision={Revision}, IsReady={IsReady}, Version={Version}",
            profile.IdentityId, profile.CharacterId, profile.Revision, profile.IsReady, profile.Version);
    }
}
