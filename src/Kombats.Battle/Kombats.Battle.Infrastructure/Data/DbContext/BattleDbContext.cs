using Kombats.Battle.Infrastructure.Data.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Kombats.Battle.Infrastructure.Data.DbContext;

public class BattleDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public const string Schema = "battle";

    public BattleDbContext(DbContextOptions<BattleDbContext> options) : base(options)
    {
    }

    public DbSet<BattleEntity> Battles { get; set; } = null!;
    public DbSet<BattleTurnEntity> BattleTurns { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<BattleEntity>(entity =>
        {
            entity.ToTable("battles");
            entity.HasKey(e => e.BattleId);
            entity.Property(e => e.BattleId).ValueGeneratedNever();
            entity.Property(e => e.MatchId).IsRequired();
            entity.Property(e => e.PlayerAId).IsRequired();
            entity.Property(e => e.PlayerBId).IsRequired();
            entity.Property(e => e.State).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.EndedAt).IsRequired(false);
            entity.Property(e => e.EndReason).HasMaxLength(50).IsRequired(false);
            entity.Property(e => e.WinnerPlayerId).IsRequired(false);
            entity.Property(e => e.PlayerAName).HasMaxLength(16).IsRequired(false);
            entity.Property(e => e.PlayerBName).HasMaxLength(16).IsRequired(false);
            entity.Property(e => e.PlayerAMaxHp).IsRequired(false);
            entity.Property(e => e.PlayerBMaxHp).IsRequired(false);
            entity.HasIndex(e => e.MatchId);
        });

        modelBuilder.Entity<BattleTurnEntity>(entity =>
        {
            entity.ToTable("battle_turns");
            entity.HasKey(e => new { e.BattleId, e.TurnIndex });
            entity.Property(e => e.AtoBOutcome).IsRequired().HasMaxLength(50);
            entity.Property(e => e.AtoBAttackZone).HasMaxLength(20).IsRequired(false);
            entity.Property(e => e.AtoBDefenderBlockPrimary).HasMaxLength(20).IsRequired(false);
            entity.Property(e => e.AtoBDefenderBlockSecondary).HasMaxLength(20).IsRequired(false);
            entity.Property(e => e.BtoAOutcome).IsRequired().HasMaxLength(50);
            entity.Property(e => e.BtoAAttackZone).HasMaxLength(20).IsRequired(false);
            entity.Property(e => e.BtoADefenderBlockPrimary).HasMaxLength(20).IsRequired(false);
            entity.Property(e => e.BtoADefenderBlockSecondary).HasMaxLength(20).IsRequired(false);
            entity.Property(e => e.ResolvedAt).IsRequired();
            entity.HasOne<BattleEntity>()
                .WithMany()
                .HasForeignKey(e => e.BattleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.AddInboxStateEntity(); 
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}