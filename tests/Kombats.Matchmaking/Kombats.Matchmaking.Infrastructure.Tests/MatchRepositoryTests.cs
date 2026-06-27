using FluentAssertions;
using Kombats.Matchmaking.Domain;
using Kombats.Matchmaking.Infrastructure.Repositories;
using Kombats.Matchmaking.Infrastructure.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kombats.Matchmaking.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public sealed class MatchRepositoryTests
{
    private readonly PostgresFixture _fixture;

    public MatchRepositoryTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Add_And_GetByMatchId_RoundTrip()
    {
        await using var db = _fixture.CreateDbContext();
        var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);

        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var match = Match.Create(matchId, battleId, playerA, playerB, "default", now);
        match.MarkBattleCreateRequested(now);
        repo.Add(match);
        await db.SaveChangesAsync();

        await using var db2 = _fixture.CreateDbContext();
        var repo2 = new MatchRepository(db2, NullLogger<MatchRepository>.Instance);
        var loaded = await repo2.GetByMatchIdAsync(matchId);

        loaded.Should().NotBeNull();
        loaded!.MatchId.Should().Be(matchId);
        loaded.BattleId.Should().Be(battleId);
        loaded.PlayerAId.Should().Be(playerA);
        loaded.PlayerBId.Should().Be(playerB);
        loaded.Variant.Should().Be("default");
        loaded.State.Should().Be(MatchState.BattleCreateRequested);
    }

    [Fact]
    public async Task TryAdvanceToBattleCreated_FromBattleCreateRequested_Succeeds()
    {
        await using var db = _fixture.CreateDbContext();
        var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);

        var matchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var match = Match.Create(matchId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "default", now);
        match.MarkBattleCreateRequested(now);
        repo.Add(match);
        await db.SaveChangesAsync();

        var result = await repo.TryAdvanceToBattleCreatedAsync(matchId, now.AddSeconds(5));
        result.Should().BeTrue();

        await using var db2 = _fixture.CreateDbContext();
        var repo2 = new MatchRepository(db2, NullLogger<MatchRepository>.Instance);
        var loaded = await repo2.GetByMatchIdAsync(matchId);
        loaded!.State.Should().Be(MatchState.BattleCreated);
    }

    [Fact]
    public async Task TryAdvanceToBattleCreated_WhenAlreadyAdvanced_ReturnsFalse()
    {
        await using var db = _fixture.CreateDbContext();
        var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);

        var matchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var match = Match.Create(matchId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "default", now);
        match.MarkBattleCreateRequested(now);
        repo.Add(match);
        await db.SaveChangesAsync();

        await repo.TryAdvanceToBattleCreatedAsync(matchId, now.AddSeconds(5));

        await using var db2 = _fixture.CreateDbContext();
        var repo2 = new MatchRepository(db2, NullLogger<MatchRepository>.Instance);
        var result = await repo2.TryAdvanceToBattleCreatedAsync(matchId, now.AddSeconds(10));
        result.Should().BeFalse();
    }

    [Fact]
    public async Task TryAdvanceToTerminal_FromBattleCreated_Succeeds()
    {
        await using var db = _fixture.CreateDbContext();
        var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);

        var matchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var match = Match.Create(matchId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "default", now);
        match.MarkBattleCreateRequested(now);
        repo.Add(match);
        await db.SaveChangesAsync();

        await repo.TryAdvanceToBattleCreatedAsync(matchId, now.AddSeconds(5));

        await using var db2 = _fixture.CreateDbContext();
        var repo2 = new MatchRepository(db2, NullLogger<MatchRepository>.Instance);
        var result = await repo2.TryAdvanceToTerminalAsync(matchId, MatchState.Completed, now.AddSeconds(10));
        result.Should().BeTrue();

        await using var db3 = _fixture.CreateDbContext();
        var repo3 = new MatchRepository(db3, NullLogger<MatchRepository>.Instance);
        var loaded = await repo3.GetByMatchIdAsync(matchId);
        loaded!.State.Should().Be(MatchState.Completed);
    }

    [Fact]
    public async Task GetActiveForPlayer_ReturnsNonTerminalMatch()
    {
        await using var db = _fixture.CreateDbContext();
        var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);

        var playerA = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var match = Match.Create(matchId, Guid.NewGuid(), playerA, Guid.NewGuid(), "default", now);
        match.MarkBattleCreateRequested(now);
        repo.Add(match);
        await db.SaveChangesAsync();

        await using var db2 = _fixture.CreateDbContext();
        var repo2 = new MatchRepository(db2, NullLogger<MatchRepository>.Instance);
        var active = await repo2.GetActiveForPlayerAsync(playerA);
        active.Should().NotBeNull();
        active!.MatchId.Should().Be(matchId);
    }

    [Fact]
    public async Task TimeoutStaleMatches_TimesOutOldBattleCreateRequestedMatches()
    {
        await using var db = _fixture.CreateDbContext();
        var repo = new MatchRepository(db, NullLogger<MatchRepository>.Instance);

        var matchId = Guid.NewGuid();
        var oldTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var match = Match.Create(matchId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "default", oldTime);
        match.MarkBattleCreateRequested(oldTime);
        repo.Add(match);
        await db.SaveChangesAsync();

        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
        var now = DateTimeOffset.UtcNow;

        await using var db2 = _fixture.CreateDbContext();
        var repo2 = new MatchRepository(db2, NullLogger<MatchRepository>.Instance);
        var affected = await repo2.TimeoutStaleMatchesAsync(cutoff, now);
        affected.Should().NotBeEmpty();

        await using var db3 = _fixture.CreateDbContext();
        var repo3 = new MatchRepository(db3, NullLogger<MatchRepository>.Instance);
        var loaded = await repo3.GetByMatchIdAsync(matchId);
        loaded!.State.Should().Be(MatchState.TimedOut);
    }
}
