using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Kombats.Bff.Application.Clients;
using Kombats.Bff.Application.Models.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kombats.Bff.Application.Tests.Clients;

public sealed class PlayersClientProfileTests
{
    [Fact]
    public async Task GetProfileAsync_HitsExpectedPath_AndDeserializes()
    {
        var playerId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var expected = new InternalPlayerProfileResponse(
            PlayerId: playerId,
            DisplayName: "Hero",
            Level: 7,
            Strength: 10,
            Agility: 8,
            Intuition: 6,
            Vitality: 5,
            Wins: 3,
            Losses: 1,
            AvatarId: "default");

        using var handler = new StubHandler(HttpStatusCode.OK, expected);
        var client = CreateClient(handler);

        var result = await client.GetProfileAsync(playerId);

        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("Hero");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.PathAndQuery.Should().Be($"/api/v1/players/{playerId}/profile");
    }

    [Fact]
    public async Task GetProfileAsync_ReturnsNull_OnNotFound()
    {
        using var handler = new StubHandler(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        (await client.GetProfileAsync(Guid.NewGuid())).Should().BeNull();
    }

    private static PlayersClient CreateClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5001") };
        return new PlayersClient(http, NullLogger<PlayersClient>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly object? _body;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(HttpStatusCode statusCode, object? body = null)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(_statusCode);
            response.Content = _body is null ? new StringContent("") : JsonContent.Create(_body);
            return Task.FromResult(response);
        }
    }
}
