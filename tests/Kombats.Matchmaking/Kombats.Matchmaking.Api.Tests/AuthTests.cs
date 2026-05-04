using System.Net;
using FluentAssertions;
using Kombats.Matchmaking.Api.Tests.Fixtures;
using Xunit;

namespace Kombats.Matchmaking.Api.Tests;

public sealed class AuthTests : IClassFixture<MatchmakingApiFactory>, IDisposable
{
    private readonly MatchmakingApiFactory _factory;
    private readonly HttpClient _client;

    public AuthTests(MatchmakingApiFactory factory)
    {
        _factory = factory;
        _factory.AuthenticateRequests = false;
        _client = _factory.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    [Theory]
    [InlineData("POST", "/api/v1/matchmaking/queue/join")]
    [InlineData("POST", "/api/v1/matchmaking/queue/leave")]
    [InlineData("GET", "/api/v1/matchmaking/queue/status")]
    public async Task ProtectedEndpoint_WithoutAuth_Returns401(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthLive_WithoutAuth_Returns200()
    {
        var response = await _client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_WithoutAuth_ReturnsNon401()
    {
        // Ready may return 503 if DB/Redis not connected (expected in test), but should NOT return 401
        var response = await _client.GetAsync("/health/ready");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
