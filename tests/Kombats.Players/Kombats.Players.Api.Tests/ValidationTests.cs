using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Kombats.Players.Api.Tests.Fixtures;
using Xunit;

namespace Kombats.Players.Api.Tests;

public sealed class ValidationTests : IClassFixture<PlayersApiFactory>, IDisposable
{
    private readonly PlayersApiFactory _factory;
    private readonly HttpClient _client;

    public ValidationTests(PlayersApiFactory factory)
    {
        _factory = factory;
        _factory.AuthenticateRequests = true;
        _client = _factory.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    [Theory]
    [InlineData("ab")]
    [InlineData("a")]
    [InlineData("ThisNameIsTooLongForValidation")]
    public async Task SetCharacterName_InvalidName_Returns400(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/character/name", new { Name = name });

        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);

        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("title").GetString().Should().Be("ValidationFailed");
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(400);
    }

    [Fact]
    public async Task AllocateStats_NegativePoints_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/players/me/stats/allocate", new
        {
            ExpectedRevision = 1,
            Str = -1,
            Agi = 0,
            Intuition = 0,
            Vit = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("title").GetString().Should().Be("ValidationFailed");
    }

    [Fact]
    public async Task AllocateStats_ZeroTotal_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/players/me/stats/allocate", new
        {
            ExpectedRevision = 1,
            Str = 0,
            Agi = 0,
            Intuition = 0,
            Vit = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("title").GetString().Should().Be("ValidationFailed");
    }

    [Fact]
    public async Task AllocateStats_InvalidRevision_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/players/me/stats/allocate", new
        {
            ExpectedRevision = 0,
            Str = 1,
            Agi = 0,
            Intuition = 0,
            Vit = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("title").GetString().Should().Be("ValidationFailed");
    }
}
