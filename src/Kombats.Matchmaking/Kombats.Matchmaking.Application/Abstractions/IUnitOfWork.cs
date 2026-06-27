namespace Kombats.Matchmaking.Application.Abstractions;

/// <summary>
/// Port for persisting changes atomically.
/// With MassTransit EF Core outbox, SaveChangesAsync also flushes outbox messages.
/// </summary>
internal interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);
}


