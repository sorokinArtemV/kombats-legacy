using Kombats.Battle.Application.Models;
using Kombats.Battle.Application.UseCases.Lifecycle;
using Kombats.Battle.Infrastructure.Data.DbContext;
using Kombats.Battle.Infrastructure.Data.Entities;
using Kombats.Battle.Contracts.Battle;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kombats.Battle.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for CreateBattle command.
/// Creates battle entity in DB, publishes BattleCreated event, and initializes battle state in Redis.
/// </summary>
public class CreateBattleConsumer : IConsumer<CreateBattle>
{
    private readonly BattleDbContext _dbContext;
    private readonly BattleLifecycleAppService _lifecycleService;
    private readonly ILogger<CreateBattleConsumer> _logger;

    public CreateBattleConsumer(
        BattleDbContext dbContext,
        BattleLifecycleAppService lifecycleService,
        ILogger<CreateBattleConsumer> logger)
    {
        _dbContext = dbContext;
        _lifecycleService = lifecycleService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CreateBattle> context)
    {
        var command = context.Message;
        var playerAId = command.PlayerA.IdentityId;
        var playerBId = command.PlayerB.IdentityId;

        _logger.LogInformation("Processing CreateBattle command for BattleId: {BattleId}, MatchId: {MatchId}",
            command.BattleId, command.MatchId);

        var playerAName = command.PlayerA.Name;
        var playerBName = command.PlayerB.Name;

        var battle = new BattleEntity
        {
            BattleId = command.BattleId,
            MatchId = command.MatchId,
            PlayerAId = playerAId,
            PlayerBId = playerBId,
            State = "ArenaOpen",
            CreatedAt = DateTimeOffset.UtcNow,
            PlayerAName = playerAName,
            PlayerBName = playerBName
        };

        _dbContext.Battles.Add(battle);

        try
        {
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            // Map contract types to application-owned types at the consumer boundary.
            // Vitality (integration term) maps to Stamina (Battle-internal domain term).
            var profileA = new CombatProfile(
                command.PlayerA.IdentityId,
                command.PlayerA.Strength,
                command.PlayerA.Vitality,
                command.PlayerA.Agility,
                command.PlayerA.Intuition);

            var profileB = new CombatProfile(
                command.PlayerB.IdentityId,
                command.PlayerB.Strength,
                command.PlayerB.Vitality,
                command.PlayerB.Agility,
                command.PlayerB.Intuition);

            var initResult = await _lifecycleService.HandleBattleCreatedAsync(
                battle.BattleId,
                battle.MatchId,
                playerAId,
                playerBId,
                profileA,
                profileB,
                playerAName,
                playerBName,
                context.CancellationToken);

            if (initResult == null)
            {
                _logger.LogWarning(
                    "Battle initialization failed for BattleId: {BattleId}. Not publishing BattleCreated event.",
                    command.BattleId);
                return;
            }

            // Write max HP to BattleEntity (computed by lifecycle service from ruleset)
            battle.PlayerAMaxHp = initResult.PlayerAMaxHp;
            battle.PlayerBMaxHp = initResult.PlayerBMaxHp;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await context.Publish(new BattleCreated
            {
                BattleId = battle.BattleId,
                MatchId = battle.MatchId,
                PlayerAId = playerAId,
                PlayerBId = playerBId,
                OccurredAt = battle.CreatedAt
            }, context.CancellationToken);

            _logger.LogInformation(
                "Successfully created battle {BattleId}, published BattleCreated event, and initialized Redis state",
                command.BattleId);
        }
        catch (DbUpdateException dbEx) when (IsUniqueViolation(dbEx))
        {
            _logger.LogInformation(
                "Battle {BattleId} already exists (unique violation), skipping creation (idempotent behavior)",
                command.BattleId);
            // ACK without publishing duplicate events
            return;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException?.Message?.Contains("23505") == true ||
               ex.InnerException?.Message?.Contains("duplicate key") == true ||
               ex.InnerException?.Message?.Contains("unique constraint") == true;
    }
}
