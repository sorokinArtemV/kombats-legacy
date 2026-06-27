using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Kombats.Battle.Api.Endpoints.BattleHistory;
using Kombats.Battle.Api.Tests.Fixtures;
using Kombats.Battle.Infrastructure.Data.DbContext;
using Kombats.Battle.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kombats.Battle.Api.Tests;

[Collection(BattleHostCollection.Name)]
public sealed class BattleHistoryEndpointHttpTests
{
    private readonly BattleWebApplicationFactory _factory;

    public BattleHistoryEndpointHttpTests(BattleWebApplicationFactory factory)
    {
        _factory = factory;

        // Ensure migrations are applied (AD-13 forbids auto-migrate on startup,
        // so tests must apply them explicitly)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BattleDbContext>();
        db.Database.MigrateAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task GetHistory_NoAuth_Returns401()
    {
        var client = _factory.CreateClient();
        // No Authorization header
        var response = await client.GetAsync($"/api/internal/battles/{Guid.NewGuid()}/history");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetHistory_AuthenticatedParticipant_Returns200()
    {
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();

        // Seed battle in PG
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BattleDbContext>();
            db.Battles.Add(new BattleEntity
            {
                BattleId = battleId,
                MatchId = Guid.NewGuid(),
                PlayerAId = playerAId,
                PlayerBId = playerBId,
                State = "Ended",
                CreatedAt = DateTimeOffset.UtcNow,
                EndedAt = DateTimeOffset.UtcNow,
                EndReason = "Normal",
                WinnerPlayerId = playerAId,
                PlayerAName = "Alice",
                PlayerBName = "Bob",
                PlayerAMaxHp = 150,
                PlayerBMaxHp = 130
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", playerAId.ToString());

        var response = await client.GetAsync($"/api/internal/battles/{battleId}/history");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<BattleHistoryResponse>();
        body.Should().NotBeNull();
        body!.BattleId.Should().Be(battleId);
        body.PlayerAName.Should().Be("Alice");
        body.PlayerBName.Should().Be("Bob");
        body.PlayerAMaxHp.Should().Be(150);
        body.PlayerBMaxHp.Should().Be(130);
    }

    [Fact]
    public async Task GetHistory_AuthenticatedNonParticipant_Returns403()
    {
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();

        // Seed battle
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BattleDbContext>();
            db.Battles.Add(new BattleEntity
            {
                BattleId = battleId,
                MatchId = Guid.NewGuid(),
                PlayerAId = playerAId,
                PlayerBId = playerBId,
                State = "ArenaOpen",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", outsiderId.ToString());

        var response = await client.GetAsync($"/api/internal/battles/{battleId}/history");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
