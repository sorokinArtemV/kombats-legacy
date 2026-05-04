using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Domain;
using Kombats.Matchmaking.Infrastructure.Data;
using Kombats.Matchmaking.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kombats.Matchmaking.Infrastructure.Repositories;

/// <summary>
/// Infrastructure implementation of IMatchRepository using EF Core.
/// </summary>
internal sealed class MatchRepository : IMatchRepository
{
    private readonly MatchmakingDbContext _db;
    private readonly ILogger<MatchRepository> _logger;

    public MatchRepository(MatchmakingDbContext db, ILogger<MatchRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Match?> GetActiveForPlayerAsync(Guid playerId, CancellationToken ct = default)
    {
        var entity = await _db.Matches
            .Where(m => (m.PlayerAId == playerId || m.PlayerBId == playerId)
                        && m.State != (int)MatchState.Completed
                        && m.State != (int)MatchState.TimedOut
                        && m.State != (int)MatchState.Cancelled)
            .OrderByDescending(m => m.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        return entity == null ? null : ToDomain(entity);
    }

    public async Task<Match?> GetByMatchIdAsync(Guid matchId, CancellationToken ct = default)
    {
        var entity = await _db.Matches
            .FirstOrDefaultAsync(m => m.MatchId == matchId, ct);

        return entity == null ? null : ToDomain(entity);
    }

    public async Task<Match?> GetByBattleIdAsync(Guid battleId, CancellationToken ct = default)
    {
        var entity = await _db.Matches
            .FirstOrDefaultAsync(m => m.BattleId == battleId, ct);

        return entity == null ? null : ToDomain(entity);
    }

    public void Add(Match match)
    {
        _db.Matches.Add(ToEntity(match));
    }

    public async Task<bool> TryAdvanceToBattleCreatedAsync(Guid matchId, DateTimeOffset now, CancellationToken ct = default)
    {
        var rows = await _db.Matches
            .Where(m => m.MatchId == matchId && m.State == (int)MatchState.BattleCreateRequested)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.State, (int)MatchState.BattleCreated)
                    .SetProperty(m => m.UpdatedAtUtc, now),
                ct);

        if (rows > 0)
            _logger.LogInformation("Match {MatchId} advanced to BattleCreated", matchId);

        return rows > 0;
    }

    public async Task<bool> TryAdvanceToTerminalAsync(Guid matchId, MatchState terminalState, DateTimeOffset now, CancellationToken ct = default)
    {
        var rows = await _db.Matches
            .Where(m => m.MatchId == matchId && m.State == (int)MatchState.BattleCreated)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.State, (int)terminalState)
                    .SetProperty(m => m.UpdatedAtUtc, now),
                ct);

        if (rows > 0)
            _logger.LogInformation("Match {MatchId} advanced to {State}", matchId, terminalState);

        return rows > 0;
    }

    public async Task<List<(Guid PlayerAId, Guid PlayerBId)>> TimeoutStaleMatchesAsync(DateTimeOffset cutoff, DateTimeOffset now, CancellationToken ct = default)
    {
        // Query affected player IDs before updating state
        var affected = await _db.Matches
            .Where(m => m.State == (int)MatchState.BattleCreateRequested && m.UpdatedAtUtc < cutoff)
            .Select(m => new { m.PlayerAId, m.PlayerBId })
            .ToListAsync(ct);

        if (affected.Count == 0)
            return [];

        var rows = await _db.Matches
            .Where(m => m.State == (int)MatchState.BattleCreateRequested && m.UpdatedAtUtc < cutoff)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.State, (int)MatchState.TimedOut)
                    .SetProperty(m => m.UpdatedAtUtc, now),
                ct);

        if (rows > 0)
            _logger.LogWarning("Timed out {Count} stale matches", rows);

        return affected.Select(a => (a.PlayerAId, a.PlayerBId)).ToList();
    }

    public async Task<List<(Guid PlayerAId, Guid PlayerBId)>> TimeoutStaleBattleCreatedMatchesAsync(DateTimeOffset cutoff, DateTimeOffset now, CancellationToken ct = default)
    {
        // Query affected player IDs before updating state
        var affected = await _db.Matches
            .Where(m => m.State == (int)MatchState.BattleCreated && m.UpdatedAtUtc < cutoff)
            .Select(m => new { m.PlayerAId, m.PlayerBId })
            .ToListAsync(ct);

        if (affected.Count == 0)
            return [];

        var rows = await _db.Matches
            .Where(m => m.State == (int)MatchState.BattleCreated && m.UpdatedAtUtc < cutoff)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.State, (int)MatchState.TimedOut)
                    .SetProperty(m => m.UpdatedAtUtc, now),
                ct);

        if (rows > 0)
            _logger.LogWarning("Timed out {Count} stale BattleCreated matches", rows);

        return affected.Select(a => (a.PlayerAId, a.PlayerBId)).ToList();
    }

    private static Match ToDomain(MatchEntity e) =>
        Match.Rehydrate(e.MatchId, e.BattleId, e.PlayerAId, e.PlayerBId, e.Variant,
            (MatchState)e.State, e.CreatedAtUtc, e.UpdatedAtUtc);

    private static MatchEntity ToEntity(Match m) => new()
    {
        MatchId = m.MatchId,
        BattleId = m.BattleId,
        PlayerAId = m.PlayerAId,
        PlayerBId = m.PlayerBId,
        Variant = m.Variant,
        State = (int)m.State,
        CreatedAtUtc = m.CreatedAtUtc,
        UpdatedAtUtc = m.UpdatedAtUtc
    };
}

