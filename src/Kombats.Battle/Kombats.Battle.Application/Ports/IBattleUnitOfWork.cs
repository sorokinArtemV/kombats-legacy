namespace Kombats.Battle.Application.Ports;

/// <summary>
/// Narrow persistence port used to flush pending work (including bus-outbox rows)
/// against the Battle DbContext. Required because MassTransit is configured with
/// UseBusOutbox: messages published via IPublishEndpoint from application code are
/// buffered on the DbContext change tracker and only reach the broker once
/// SaveChangesAsync runs. The normal Redis-only battle completion path has no
/// other reason to call SaveChangesAsync, so it must invoke this port explicitly
/// after publishing BattleCompleted.
/// </summary>
public interface IBattleUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
