using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kombats.Players.Infrastructure.Data;

public class PlayersDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PlayersDbContext>
{
    public PlayersDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PlayersDbContext>()
            .UseNpgsql("Host=localhost;Database=kombats;Username=postgres;Password=postgres", npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", PlayersDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IHistoryRepository, SnakeCaseHistoryRepository>()
            .Options;

        return new PlayersDbContext(options);
    }
}
