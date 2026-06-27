using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Kombats.Bff.Application.Clients;
using Kombats.Bff.Application.Errors;
using Kombats.Bff.Application.Models.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kombats.Bff.Application.Tests;

public sealed class MatchmakingClientTests
{
    [Fact]
    public async Task JoinQueueAsync_ReturnsSearching_OnSuccess()
    {
        var expected = new InternalQueueStatusResponse("Searching");
        using var handler = new StubHandler(HttpStatusCode.OK, expected);
        var client = CreateClient(handler);

        InternalQueueStatusResponse result = await client.JoinQueueAsync();

        result.Status.Should().Be("Searching");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.PathAndQuery.Should().Be("/api/v1/matchmaking/queue/join");
    }

    [Fact]
    public async Task JoinQueueAsync_ReturnsMatchedStatus_On409Conflict()
    {
        Guid matchId = Guid.NewGuid();
        Guid battleId = Guid.NewGuid();
        var expected = new InternalQueueStatusResponse("Matched", matchId, battleId, "Created");
        using var handler = new StubHandler(HttpStatusCode.Conflict, expected);
        var client = CreateClient(handler);

        InternalQueueStatusResponse result = await client.JoinQueueAsync();

        result.Status.Should().Be("Matched");
        result.MatchId.Should().Be(matchId);
        result.BattleId.Should().Be(battleId);
    }

    [Fact]
    public async Task LeaveQueueAsync_ReturnsLeftQueue_OnSuccess()
    {
        var expected = new InternalLeaveQueueResponse(false);
        using var handler = new StubHandler(HttpStatusCode.OK, expected);
        var client = CreateClient(handler);

        InternalLeaveQueueResponse result = await client.LeaveQueueAsync();

        result.Searching.Should().BeFalse();
        result.MatchId.Should().BeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.PathAndQuery.Should().Be("/api/v1/matchmaking/queue/leave");
    }

    [Fact]
    public async Task LeaveQueueAsync_ReturnsMatchInfo_On409Conflict()
    {
        Guid matchId = Guid.NewGuid();
        Guid battleId = Guid.NewGuid();
        var expected = new InternalLeaveQueueResponse(false, matchId, battleId);
        using var handler = new StubHandler(HttpStatusCode.Conflict, expected);
        var client = CreateClient(handler);

        InternalLeaveQueueResponse result = await client.LeaveQueueAsync();

        result.Searching.Should().BeFalse();
        result.MatchId.Should().Be(matchId);
        result.BattleId.Should().Be(battleId);
    }

    [Fact]
    public async Task GetQueueStatusAsync_ReturnsStatus_OnSuccess()
    {
        var expected = new InternalQueueStatusResponse("Searching");
        using var handler = new StubHandler(HttpStatusCode.OK, expected);
        var client = CreateClient(handler);

        InternalQueueStatusResponse? result = await client.GetQueueStatusAsync();

        result.Should().NotBeNull();
        result!.Status.Should().Be("Searching");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.PathAndQuery.Should().Be("/api/v1/matchmaking/queue/status");
    }

    [Fact]
    public async Task GetQueueStatusAsync_ReturnsNull_OnNotFound()
    {
        using var handler = new StubHandler(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        InternalQueueStatusResponse? result = await client.GetQueueStatusAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task JoinQueueAsync_ThrowsServiceUnavailableException_OnHttpRequestException()
    {
        using var handler = new ThrowingHandler();
        var client = CreateClient(handler);

        Func<Task> act = () => client.JoinQueueAsync();

        await act.Should().ThrowAsync<ServiceUnavailableException>()
            .Where(e => e.ServiceName == "Matchmaking");
    }

    [Fact]
    public async Task LeaveQueueAsync_ThrowsServiceUnavailableException_OnHttpRequestException()
    {
        using var handler = new ThrowingHandler();
        var client = CreateClient(handler);

        Func<Task> act = () => client.LeaveQueueAsync();

        await act.Should().ThrowAsync<ServiceUnavailableException>()
            .Where(e => e.ServiceName == "Matchmaking");
    }

    [Fact]
    public async Task JoinQueueAsync_ThrowsBffServiceException_OnServerError()
    {
        using var handler = new StubHandler(HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        Func<Task> act = () => client.JoinQueueAsync();

        await act.Should().ThrowAsync<BffServiceException>()
            .Where(e => e.StatusCode == HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task JoinQueueAsync_SendsVariantInBody()
    {
        var expected = new InternalQueueStatusResponse("Searching");
        using var handler = new StubHandler(HttpStatusCode.OK, expected);
        var client = CreateClient(handler);

        await client.JoinQueueAsync();

        handler.LastRequestBody.Should().NotBeNull();
        handler.LastRequestBody.Should().Contain("variant");
    }

    private static MatchmakingClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5002") };
        return new MatchmakingClient(httpClient, NullLogger<MatchmakingClient>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly object? _responseBody;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public StubHandler(HttpStatusCode statusCode, object? responseBody = null)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            var response = new HttpResponseMessage(_statusCode);
            if (_responseBody is not null)
            {
                response.Content = JsonContent.Create(_responseBody);
            }
            else
            {
                response.Content = new StringContent("");
            }
            return response;
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Connection refused");
        }
    }
}
