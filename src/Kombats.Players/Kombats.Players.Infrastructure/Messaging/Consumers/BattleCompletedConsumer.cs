using Kombats.Abstractions;
using Kombats.Battle.Contracts.Battle;
using Kombats.Players.Application.Battles;
using MassTransit;

namespace Kombats.Players.Infrastructure.Messaging.Consumers;

/// <summary>
/// Thin integration consumer for the canonical BattleCompleted event from Battle service.
/// Maps into HandleBattleCompletedCommand and delegates to the application handler.
/// </summary>
internal sealed class BattleCompletedConsumer : IConsumer<BattleCompleted>
{
    private readonly ICommandHandler<HandleBattleCompletedCommand> _handler;

    public BattleCompletedConsumer(ICommandHandler<HandleBattleCompletedCommand> handler)
    {
        _handler = handler;
    }

    public async Task Consume(ConsumeContext<BattleCompleted> context)
    {
        var msg = context.Message;

        var command = new HandleBattleCompletedCommand(
            msg.MessageId,
            msg.BattleId,
            msg.WinnerIdentityId,
            msg.LoserIdentityId,
            msg.Reason.ToString());

        var result = await _handler.HandleAsync(command, context.CancellationToken);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"HandleBattleCompleted failed: [{result.Error.Code}] {result.Error.Description}");
        }
    }
}
