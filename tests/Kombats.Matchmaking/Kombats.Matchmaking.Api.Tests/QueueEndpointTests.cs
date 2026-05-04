using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Kombats.Abstractions;
using Kombats.Matchmaking.Api.Tests.Fixtures;
using Kombats.Matchmaking.Application.UseCases.GetQueueStatus;
using Kombats.Matchmaking.Application.UseCases.JoinQueue;
using Kombats.Matchmaking.Application.UseCases.LeaveQueue;
using Kombats.Matchmaking.Domain;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Kombats.Matchmaking.Api.Tests;

public sealed class QueueEndpointTests : IClassFixture<MatchmakingApiFactory>, IDisposable
{
    private readonly MatchmakingApiFactory _factory;
    private readonly HttpClient _client;

    public QueueEndpointTests(MatchmakingApiFactory factory)
    {
        _factory = factory;
        _factory.AuthenticateRequests = true;
        _client = _factory.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    // --- JoinQueue ---

    [Fact]
    public async Task JoinQueue_Success_Returns200WithSearchingStatus()
    {
        _factory.JoinQueueHandler
            .HandleAsync(Arg.Any<JoinQueueCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new JoinQueueResult(QueuePlayerStatus.Searching)));

        var response = await _client.PostAsJsonAsync("/api/v1/matchmaking/queue/join", new { Variant = "default" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetString().Should().Be("Searching");
    }

    [Fact]
    public async Task JoinQueue_AlreadyMatched_Returns409WithMatchInfo()
    {
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();
        _factory.JoinQueueHandler
            .HandleAsync(Arg.Any<JoinQueueCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new JoinQueueResult(
                QueuePlayerStatus.AlreadyMatched, matchId, battleId, MatchState.BattleCreated)));

        var response = await _client.PostAsJsonAsync("/api/v1/matchmaking/queue/join", new { Variant = "default" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetString().Should().Be("Matched");
        doc.RootElement.GetProperty("matchId").GetGuid().Should().Be(matchId);
        doc.RootElement.GetProperty("battleId").GetGuid().Should().Be(battleId);
    }

    [Fact]
    public async Task JoinQueue_ValidationFailure_Returns400()
    {
        _factory.JoinQueueHandler
            .HandleAsync(Arg.Any<JoinQueueCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<JoinQueueResult>(
                Error.Validation("Queue.NoCombatProfile", "No combat profile found.")));

        var response = await _client.PostAsJsonAsync("/api/v1/matchmaking/queue/join", new { Variant = "default" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // --- LeaveQueue ---

    [Fact]
    public async Task LeaveQueue_Success_Returns200()
    {
        _factory.LeaveQueueHandler
            .HandleAsync(Arg.Any<LeaveQueueCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new LeaveQueueResult(LeaveQueueStatus.Left)));

        var response = await _client.PostAsJsonAsync("/api/v1/matchmaking/queue/leave", new { Variant = "default" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LeaveQueue_AlreadyMatched_Returns409()
    {
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();
        _factory.LeaveQueueHandler
            .HandleAsync(Arg.Any<LeaveQueueCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new LeaveQueueResult(
                LeaveQueueStatus.AlreadyMatched, matchId, battleId)));

        var response = await _client.PostAsJsonAsync("/api/v1/matchmaking/queue/leave", new { Variant = "default" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // --- GetQueueStatus ---

    [Fact]
    public async Task GetQueueStatus_Searching_Returns200()
    {
        _factory.GetQueueStatusHandler
            .HandleAsync(Arg.Any<GetQueueStatusQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new QueueStatusResult(QueueStatusType.Searching)));

        var response = await _client.GetAsync("/api/v1/matchmaking/queue/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetString().Should().Be("Searching");
    }

    [Fact]
    public async Task GetQueueStatus_Matched_ReturnsMatchDetails()
    {
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();
        _factory.GetQueueStatusHandler
            .HandleAsync(Arg.Any<GetQueueStatusQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new QueueStatusResult(
                QueueStatusType.Matched, matchId, battleId, MatchState.BattleCreated)));

        var response = await _client.GetAsync("/api/v1/matchmaking/queue/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetString().Should().Be("Matched");
        doc.RootElement.GetProperty("matchId").GetGuid().Should().Be(matchId);
        doc.RootElement.GetProperty("battleId").GetGuid().Should().Be(battleId);
        doc.RootElement.GetProperty("matchState").GetString().Should().Be("BattleCreated");
    }

    // --- Exception handling (ProblemDetails) ---

    [Fact]
    public async Task UnhandledException_Returns500ProblemDetails()
    {
        _factory.GetQueueStatusHandler
            .HandleAsync(Arg.Any<GetQueueStatusQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Unexpected test error"));

        var response = await _client.GetAsync("/api/v1/matchmaking/queue/status");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var body = await response.Content.ReadAsStringAsync();

        // Built-in ProblemDetails should produce valid JSON
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(500);

        // Must NOT leak internal exception details
        body.Should().NotContain("Unexpected test error");
        body.Should().NotContain("InvalidOperationException");
    }
}
