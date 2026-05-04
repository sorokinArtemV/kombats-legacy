namespace Kombats.Players.Application.Abstractions;

internal interface IInboxRepository
{
    public Task<bool> IsProcessedAsync(Guid messageId, CancellationToken ct);
    public Task AddProcessedAsync(Guid messageId, DateTimeOffset processedAt, CancellationToken ct);
}