using Kombats.Players.Application.Abstractions;
using Kombats.Players.Infrastructure.Data;
using Kombats.Players.Infrastructure.Messaging.Inbox;
using Microsoft.EntityFrameworkCore;

namespace Kombats.Players.Infrastructure.Persistence.Repository;

internal sealed class InboxRepository : IInboxRepository
{
    private readonly PlayersDbContext _db;

    public InboxRepository(PlayersDbContext db) => _db = db;

    public Task<bool> IsProcessedAsync(Guid messageId, CancellationToken ct)
        => _db.InboxMessages
            .AsNoTracking()
            .AnyAsync(x => x.MessageId == messageId, ct);

    public Task AddProcessedAsync(Guid messageId, DateTimeOffset processedAt, CancellationToken ct)
    {
        _db.InboxMessages.Add(new InboxMessage
        {
            MessageId = messageId,
            ProcessedAt = processedAt
        });

        return Task.CompletedTask;
    }
}