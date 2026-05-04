using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Players.Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Kombats.Matchmaking.Infrastructure.Messaging.Consumers;

/// <summary>
/// Thin integration consumer for player combat profile change events from Players service.
/// Consumes the canonical PlayerCombatProfileChanged contract from Kombats.Players.Contracts.
/// </summary>
internal sealed class PlayerCombatProfileChangedConsumer : IConsumer<PlayerCombatProfileChanged>
{
    private readonly IPlayerCombatProfileRepository _repository;
    private readonly ILogger<PlayerCombatProfileChangedConsumer> _logger;

    public PlayerCombatProfileChangedConsumer(
        IPlayerCombatProfileRepository repository,
        ILogger<PlayerCombatProfileChangedConsumer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PlayerCombatProfileChanged> context)
    {
        var message = context.Message;

        var profile = new PlayerCombatProfile
        {
            IdentityId = message.IdentityId,
            CharacterId = message.CharacterId,
            Name = message.Name,
            Level = message.Level,
            Strength = message.Strength,
            Agility = message.Agility,
            Intuition = message.Intuition,
            Vitality = message.Vitality,
            IsReady = message.IsReady,
            Revision = message.Revision,
            OccurredAt = message.OccurredAt,
            AvatarId = message.AvatarId
        };

        await _repository.UpsertAsync(profile, context.CancellationToken);

        _logger.LogInformation(
            "Upserted player combat profile projection: IdentityId={IdentityId}, CharacterId={CharacterId}, Revision={Revision}, IsReady={IsReady}",
            message.IdentityId, message.CharacterId, message.Revision, message.IsReady);
    }
}
