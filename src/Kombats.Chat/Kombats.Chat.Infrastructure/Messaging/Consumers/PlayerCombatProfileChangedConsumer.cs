using Kombats.Abstractions;
using Kombats.Chat.Application.UseCases.HandlePlayerProfileChanged;
using Kombats.Players.Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Kombats.Chat.Infrastructure.Messaging.Consumers;

/// <summary>
/// Thin consumer: delegates to <see cref="HandlePlayerProfileChangedHandler"/>.
/// MassTransit EF Core inbox (configured in <see cref="Kombats.Messaging"/>) provides
/// idempotency for duplicate deliveries. The handler itself is write-only (cache SET),
/// so replays are also safe at the business-logic level.
/// </summary>
internal sealed class PlayerCombatProfileChangedConsumer(
    ICommandHandler<HandlePlayerProfileChangedCommand> handler,
    ILogger<PlayerCombatProfileChangedConsumer> logger)
    : IConsumer<PlayerCombatProfileChanged>
{
    public async Task Consume(ConsumeContext<PlayerCombatProfileChanged> context)
    {
        var message = context.Message;

        var command = new HandlePlayerProfileChangedCommand(
            message.IdentityId,
            message.Name,
            message.IsReady);

        var result = await handler.HandleAsync(command, context.CancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning(
                "HandlePlayerProfileChanged failed for IdentityId={IdentityId}: {ErrorCode} {ErrorMessage}",
                message.IdentityId, result.Error.Code, result.Error.Description);

            // Surface failure so MassTransit retry/redelivery/fault semantics apply.
            // Returning success would cause the inbox to record the message as processed
            // and leave the Chat player-info cache stale until TTL expiry.
            throw new InvalidOperationException(
                $"HandlePlayerProfileChanged failed for IdentityId={message.IdentityId}: {result.Error.Code} {result.Error.Description}");
        }

        logger.LogInformation(
            "Chat player info cache updated: IdentityId={IdentityId}, IsReady={IsReady}, Revision={Revision}",
            message.IdentityId, message.IsReady, message.Revision);
    }
}
