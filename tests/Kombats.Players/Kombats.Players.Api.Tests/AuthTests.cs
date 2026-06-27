using System.Net;
using FluentAssertions;
using Kombats.Players.Api.Tests.Fixtures;
using Xunit;

namespace Kombats.Players.Api.Tests;

public sealed class AuthTests : IClassFixture<PlayersApiFactory>, IDisposable
{
    private readonly PlayersApiFactory _factory;
    private readonly HttpClient _client;

    public AuthTests(PlayersApiFactory factory)
    {
        _factory = factory;
        _factory.AuthenticateRequests = false;
        _client = _factory.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    [Theory]
    [InlineData("GET", "/api/v1/me")]
    [InlineData("POST", "/api/v1/me/ensure")]
    [InlineData("POST", "/api/v1/character/name")]
    [InlineData("POST", "/api/v1/players/me/stats/allocate")]
    [InlineData("GET", "/api/v1/players/d290f1ee-6c54-4b01-90e6-d701748f0851/profile")]
    public async Task ProtectedEndpoint_WithoutAuth_Returns401(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);

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
        // Ready may return 503 if DB is not connected (expected in test), but should NOT return 401
        var response = await _client.GetAsync("/health/ready");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
