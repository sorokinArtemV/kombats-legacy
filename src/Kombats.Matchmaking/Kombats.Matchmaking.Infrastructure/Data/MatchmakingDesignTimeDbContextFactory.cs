using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kombats.Matchmaking.Infrastructure.Data;

public sealed class MatchmakingDesignTimeDbContextFactory : IDesignTimeDbContextFactory<MatchmakingDbContext>
{
    public MatchmakingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MatchmakingDbContext>()
            .UseNpgsql("Host=localhost;Database=kombats;Username=postgres;Password=postgres", npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", MatchmakingDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IHistoryRepository, SnakeCaseHistoryRepository>()
            .Options;

        return new MatchmakingDbContext(options);
    }
}
