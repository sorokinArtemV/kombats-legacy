namespace Kombats.Players.Application.Abstractions;

internal interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}