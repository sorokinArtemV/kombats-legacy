using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Kombats.Bff.Application.Clients;
using Kombats.Bff.Application.Errors;
using Kombats.Bff.Application.Models.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kombats.Bff.Application.Tests;

public sealed class PlayersClientTests
{
    [Fact]
    public async Task GetCharacterAsync_ReturnsCharacter_OnSuccess()
    {
        var expected = CreateTestCharacter();
        using var handler = new StubHandler(HttpStatusCode.OK, expected);
        var client = CreateClient(handler);

        InternalCharacterResponse? result = await client.GetCharacterAsync();

        result.Should().NotBeNull();
        result!.Name.Should().Be("TestHero");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.PathAndQuery.Should().Be("/api/v1/me");
    }

    [Fact]
    public async Task GetCharacterAsync_ReturnsNull_OnNotFound()
    {
        using var handler = new StubHandler(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        InternalCharacterResponse? result = await client.GetCharacterAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task EnsureCharacterAsync_PostsToCorrectPath()
    {
        var expected = CreateTestCharacter();
        using var handler = new StubHandler(HttpStatusCode.OK, expected);
        var client = CreateClient(handler);

        await client.EnsureCharacterAsync();

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.PathAndQuery.Should().Be("/api/v1/me/ensure");
    }

    [Fact]
    public async Task SetCharacterNameAsync_SendsNameInBody()
    {
        var expected = CreateTestCharacter();
        using var handler = new StubHandler(HttpStatusCode.OK, expected);
        var client = CreateClient(handler);

        await client.SetCharacterNameAsync("NewName");

        handler.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/api/v1/character/name");
        handler.LastRequestBody.Should().Contain("NewName");
    }

    [Fact]
    public async Task AllocateStatsAsync_SendsExpectedRevisionInBody()
    {
        var expected = CreateTestCharacter();
        using var handler = new StubHandler(HttpStatusCode.OK, expected);
        var client = CreateClient(handler);

        await client.AllocateStatsAsync(expectedRevision: 5, strength: 1, agility: 0, intuition: 0, vitality: 0);

        handler.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/api/v1/players/me/stats/allocate");
        handler.LastRequestBody.Should().NotBeNull();
        using JsonDocument doc = JsonDocument.Parse(handler.LastRequestBody!);
        JsonElement root = doc.RootElement;
        // JsonContent.Create uses camelCase by default — ASP.NET Core binds case-insensitively
        root.TryGetProperty("expectedRevision", out JsonElement rev).Should().BeTrue();
        rev.GetInt32().Should().Be(5);
        root.TryGetProperty("str", out JsonElement str).Should().BeTrue();
        str.GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetCharacterAsync_ThrowsServiceUnavailableException_OnHttpRequestException()
    {
        using var handler = new ThrowingHandler();
        var client = CreateClient(handler);

        Func<Task> act = () => client.GetCharacterAsync();

        await act.Should().ThrowAsync<ServiceUnavailableException>()
            .Where(e => e.ServiceName == "Players");
    }

    [Fact]
    public async Task GetCharacterAsync_ThrowsBffServiceException_OnServerError()
    {
        using var handler = new StubHandler(HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        Func<Task> act = () => client.GetCharacterAsync();

        await act.Should().ThrowAsync<BffServiceException>()
            .Where(e => e.StatusCode == HttpStatusCode.InternalServerError);
    }

    private static PlayersClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5001") };
        return new PlayersClient(httpClient, NullLogger<PlayersClient>.Instance);
    }

    private static InternalCharacterResponse CreateTestCharacter() => new(
        CharacterId: Guid.NewGuid(),
        IdentityId: Guid.NewGuid(),
        OnboardingState: 2,
        Name: "TestHero",
        Strength: 5,
        Agility: 3,
        Intuition: 2,
        Vitality: 4,
        UnspentPoints: 0,
        Revision: 1,
        TotalXp: 100,
        Level: 2,
        LevelingVersion: 1,
        AvatarId: "default");

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
