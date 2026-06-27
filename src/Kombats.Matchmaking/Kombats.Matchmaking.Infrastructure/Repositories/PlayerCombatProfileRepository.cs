using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Infrastructure.Data;
using Kombats.Matchmaking.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kombats.Matchmaking.Infrastructure.Repositories;

/// <summary>
/// Infrastructure implementation of IPlayerCombatProfileRepository using EF Core.
/// Enforces revision monotonicity to prevent stale event overwrites.
/// </summary>
internal sealed class PlayerCombatProfileRepository : IPlayerCombatProfileRepository
{
    private readonly MatchmakingDbContext _dbContext;
    private readonly ILogger<PlayerCombatProfileRepository> _logger;

    public PlayerCombatProfileRepository(
        MatchmakingDbContext dbContext,
        ILogger<PlayerCombatProfileRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PlayerCombatProfile?> GetByIdentityIdAsync(Guid identityId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.PlayerCombatProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.IdentityId == identityId, cancellationToken);

        return entity == null ? null : ToDomain(entity);
    }

    public async Task<bool> UpsertAsync(PlayerCombatProfile profile, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.PlayerCombatProfiles
            .FirstOrDefaultAsync(p => p.IdentityId == profile.IdentityId, cancellationToken);

        if (existing != null)
        {
            // Enforce revision monotonicity: skip if incoming revision is not newer
            if (profile.Revision <= existing.Revision)
            {
                _logger.LogInformation(
                    "Skipping stale profile update for IdentityId={IdentityId}: incoming Revision={IncomingRevision} <= stored Revision={StoredRevision}",
                    profile.IdentityId, profile.Revision, existing.Revision);
                return false;
            }

            existing.CharacterId = profile.CharacterId;
            existing.Name = profile.Name;
            existing.Level = profile.Level;
            existing.Strength = profile.Strength;
            existing.Agility = profile.Agility;
            existing.Intuition = profile.Intuition;
            existing.Vitality = profile.Vitality;
            existing.IsReady = profile.IsReady;
            existing.Revision = profile.Revision;
            existing.OccurredAt = profile.OccurredAt;
            existing.AvatarId = profile.AvatarId ?? PlayerCombatProfileEntityDefaults.AvatarId;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        else
        {
            _dbContext.PlayerCombatProfiles.Add(new PlayerCombatProfileEntity
            {
                IdentityId = profile.IdentityId,
                CharacterId = profile.CharacterId,
                Name = profile.Name,
                Level = profile.Level,
                Strength = profile.Strength,
                Agility = profile.Agility,
                Intuition = profile.Intuition,
                Vitality = profile.Vitality,
                IsReady = profile.IsReady,
                Revision = profile.Revision,
                OccurredAt = profile.OccurredAt,
                AvatarId = profile.AvatarId ?? PlayerCombatProfileEntityDefaults.AvatarId,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Upserted player combat profile: IdentityId={IdentityId}, CharacterId={CharacterId}, Revision={Revision}, IsReady={IsReady}",
            profile.IdentityId, profile.CharacterId, profile.Revision, profile.IsReady);

        return true;
    }

    private static PlayerCombatProfile ToDomain(PlayerCombatProfileEntity entity)
    {
        return new PlayerCombatProfile
        {
            IdentityId = entity.IdentityId,
            CharacterId = entity.CharacterId,
            Name = entity.Name,
            Level = entity.Level,
            Strength = entity.Strength,
            Agility = entity.Agility,
            Intuition = entity.Intuition,
            Vitality = entity.Vitality,
            IsReady = entity.IsReady,
            Revision = entity.Revision,
            OccurredAt = entity.OccurredAt,
            AvatarId = entity.AvatarId
        };
    }
}
