using Kombats.Battle.Contracts.Battle;
using Kombats.Matchmaking.Application.Abstractions;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Kombats.Matchmaking.Infrastructure.Messaging;

/// <summary>
/// Publishes CreateBattle commands via MassTransit transactional outbox.
/// When the EF Core outbox is configured, Publish() writes to the outbox table
/// instead of sending directly — atomicity is guaranteed by SaveChanges.
/// </summary>
internal sealed class MassTransitCreateBattlePublisher : ICreateBattlePublisher
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<MassTransitCreateBattlePublisher> _logger;

    public MassTransitCreateBattlePublisher(
        IPublishEndpoint publishEndpoint,
        ILogger<MassTransitCreateBattlePublisher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task PublishAsync(CreateBattleRequest request, CancellationToken ct = default)
    {
        var command = new CreateBattle
        {
            BattleId = request.BattleId,
            MatchId = request.MatchId,
            RequestedAt = request.RequestedAt,
            PlayerA = new BattleParticipantSnapshot
            {
                IdentityId = request.PlayerA.IdentityId,
                CharacterId = request.PlayerA.CharacterId,
                Name = request.PlayerA.Name,
                Level = request.PlayerA.Level,
                Strength = request.PlayerA.Strength,
                Agility = request.PlayerA.Agility,
                Intuition = request.PlayerA.Intuition,
                Vitality = request.PlayerA.Vitality
            },
            PlayerB = new BattleParticipantSnapshot
            {
                IdentityId = request.PlayerB.IdentityId,
                CharacterId = request.PlayerB.CharacterId,
                Name = request.PlayerB.Name,
                Level = request.PlayerB.Level,
                Strength = request.PlayerB.Strength,
                Agility = request.PlayerB.Agility,
                Intuition = request.PlayerB.Intuition,
                Vitality = request.PlayerB.Vitality
            }
        };

        await _publishEndpoint.Publish(command, ct);

        _logger.LogInformation(
            "Queued CreateBattle on outbox: MatchId={MatchId}, BattleId={BattleId}, PlayerA={PlayerAIdentityId}, PlayerB={PlayerBIdentityId}",
            request.MatchId, request.BattleId, request.PlayerA.IdentityId, request.PlayerB.IdentityId);
    }
}
