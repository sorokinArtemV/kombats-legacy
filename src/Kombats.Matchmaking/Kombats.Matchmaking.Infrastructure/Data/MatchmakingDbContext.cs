using Kombats.Matchmaking.Infrastructure.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Kombats.Matchmaking.Infrastructure.Data;

/// <summary>
/// EF Core DbContext for Matchmaking service.
/// Uses MassTransit EF Core transactional outbox/inbox (AD-01).
/// </summary>
public sealed class MatchmakingDbContext : DbContext
{
    public const string Schema = "matchmaking";

    public MatchmakingDbContext(DbContextOptions<MatchmakingDbContext> options)
        : base(options)
    {
    }

    internal DbSet<MatchEntity> Matches => Set<MatchEntity>();
    internal DbSet<PlayerCombatProfileEntity> PlayerCombatProfiles => Set<PlayerCombatProfileEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        // Configure MatchEntity
        modelBuilder.Entity<MatchEntity>(entity =>
        {
            entity.ToTable("matches");
            entity.HasKey(e => e.MatchId);

            entity.Property(e => e.BattleId).IsRequired();
            entity.Property(e => e.PlayerAId).IsRequired();
            entity.Property(e => e.PlayerBId).IsRequired();
            entity.Property(e => e.Variant).HasMaxLength(32).IsRequired();
            entity.Property(e => e.State).IsRequired();
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.UpdatedAtUtc).IsRequired();

            entity.HasIndex(e => e.BattleId).IsUnique();
            entity.HasIndex(e => e.PlayerAId);
            entity.HasIndex(e => e.PlayerBId);
            entity.HasIndex(e => new { e.PlayerAId, e.CreatedAtUtc });
            entity.HasIndex(e => new { e.PlayerBId, e.CreatedAtUtc });
        });

        // Configure PlayerCombatProfileEntity
        modelBuilder.Entity<PlayerCombatProfileEntity>(entity =>
        {
            entity.ToTable("player_combat_profiles");
            entity.HasKey(e => e.IdentityId);

            entity.Property(e => e.CharacterId).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(64);
            entity.Property(e => e.Level).IsRequired();
            entity.Property(e => e.Strength).IsRequired();
            entity.Property(e => e.Agility).IsRequired();
            entity.Property(e => e.Intuition).IsRequired();
            entity.Property(e => e.Vitality).IsRequired();
            entity.Property(e => e.IsReady).IsRequired();
            entity.Property(e => e.Revision).IsRequired();
            entity.Property(e => e.OccurredAt).IsRequired();
            entity.Property(e => e.UpdatedAtUtc).IsRequired();
            entity.Property(e => e.AvatarId).HasMaxLength(64).IsRequired();

            entity.HasIndex(e => e.CharacterId);
        });

        // MassTransit EF Core transactional outbox/inbox (AD-01)
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}





