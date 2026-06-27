using Kombats.Battle.Infrastructure.Data.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kombats.Battle.Infrastructure.Data;

public class BattleDesignTimeDbContextFactory : IDesignTimeDbContextFactory<BattleDbContext>
{
    public BattleDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BattleDbContext>()
            .UseNpgsql("Host=localhost;Database=kombats;Username=postgres;Password=postgres", npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", BattleDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IHistoryRepository, SnakeCaseHistoryRepository>()
            .Options;

        return new BattleDbContext(options);
    }
}
