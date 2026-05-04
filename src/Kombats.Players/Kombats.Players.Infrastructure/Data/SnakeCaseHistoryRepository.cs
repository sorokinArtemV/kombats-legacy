using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations.Internal;

namespace Kombats.Players.Infrastructure.Data;

#pragma warning disable EF1001 // Internal EF Core API usage — extending NpgsqlHistoryRepository is the standard pattern
public class SnakeCaseHistoryRepository : NpgsqlHistoryRepository
{
    public SnakeCaseHistoryRepository(HistoryRepositoryDependencies dependencies)
        : base(dependencies)
    {
    }

    protected override void ConfigureTable(EntityTypeBuilder<HistoryRow> history)
    {
        base.ConfigureTable(history);
        history.Property(h => h.MigrationId).HasColumnName("migration_id");
        history.Property(h => h.ProductVersion).HasColumnName("product_version");
    }
}
#pragma warning restore EF1001
