using System.Net;
using Kombats.Chat.Api.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Kombats.Chat.Api.Tests.Endpoints;

public sealed class PresenceEndpointTests : IClassFixture<ChatApiFactory>
{
    private readonly ChatApiFactory _factory;

    public PresenceEndpointTests(ChatApiFactory factory)
    {
        _factory = factory;
        _factory.AuthenticateRequests = true;
    }

    [Fact]
    public async Task GetOnlinePlayers_Authenticated_ReturnsOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/internal/presence/online");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetOnlinePlayers_Unauthenticated_Returns401()
    {
        _factory.AuthenticateRequests = false;
        try
        {
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/api/internal/presence/online");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            _factory.AuthenticateRequests = true;
        }
    }
}
